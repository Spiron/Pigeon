﻿using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Routing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.Actor
{
    public partial class ActorCell : IActorContext, IActorRefFactory
    {
        
        public virtual ActorSystem System { get;private set; }
        public Props Props { get;private set; }
        public LocalActorRef Self { get; protected set; }
        public InternalActorRef Parent { get; private set; }
        public ActorBase Actor { get;internal set; }
        public object CurrentMessage { get;private set; }
        public ActorRef Sender { get;private set; }
        internal Receive CurrentBehavior { get; private set; }
        protected Stack<Receive> behaviorStack = new Stack<Receive>();
        protected Mailbox Mailbox { get; set; }
        public MessageDispatcher Dispatcher { get;private set; }
        protected HashSet<ActorRef> Watchees = new HashSet<ActorRef>();
        [ThreadStatic]
        private static ActorCell current;
        internal static ActorCell Current
        {
            get
            {
                return current;
            }
        }

        protected ConcurrentDictionary<string, InternalActorRef> Children = new ConcurrentDictionary<string, InternalActorRef>();
        
        public virtual InternalActorRef Child(string name)
        {
            InternalActorRef actorRef = null;
            Children.TryGetValue(name, out actorRef);
            if (actorRef.IsNobody())
                return ActorRef.Nobody;
            else
            return actorRef;
        }

        public ActorSelection ActorSelection(string path)
        {
           if (Uri.IsWellFormedUriString(path,UriKind.Absolute))
           {
               var actorPath = ActorPath.Parse(path);
               var actorRef = System.Provider.RootGuardianAt(actorPath.Address);
               return new ActorSelection(actorRef, actorPath.Elements.ToArray());
           }
           else
           {
               //no path given
               if (string.IsNullOrEmpty(path))
               {
                   return new ActorSelection(this.System.DeadLetters, "");
               }
               else
               {
                   //absolute path
                   if (path.Split('/').First() == "")
                   {
                       return new ActorSelection(this.System.Provider.RootCell.Self, path.TrimStart('/'));
                   }                    
                   else // relative path
                   {
                       return new ActorSelection(this.Self, path);
                   }
               }
           }
        }

        public ActorSelection ActorSelection(ActorPath actorPath)
        {
            var actorRef = System.Provider.ResolveActorRef(actorPath);
            return new ActorSelection(actorRef, ""); 
        }

        public virtual InternalActorRef ActorOf<TActor>(string name = null) where TActor : ActorBase
        {
            return ActorOf(Props.Create<TActor>(), name);
        }

        public virtual InternalActorRef ActorOf(Props props, string name = null)
        {
            return MakeChild(props, name);
        }

        private InternalActorRef MakeChild(Props props, string name)
        {            
            var uid = NewUid();
            name = GetActorName(props, name, uid);
            //reserve the name before we create the actor
            ReserveChild(name);
            try
            {
                var childPath = (this.Self.Path / name).WithUid(uid);
                var actor = System.Provider.ActorOf(System, props, this.Self, childPath);
                //replace the reservation with the real actor
                InitChild(name, actor);
                return actor;
            }
            catch
            {
                //if actor creation failed, unreserve the name
                UnreserveChild(name);
                throw;
            }
            
        }

        private void UnreserveChild(string name)
        {
            InternalActorRef tmp;
            this.Children.TryRemove(name, out tmp);
        }

        private void InitChild(string name, InternalActorRef actor)
        {
            this.Children.TryUpdate(name, actor,ActorRef.Reserved);
        }

        private void ReserveChild(string name)
        {
            if (!this.Children.TryAdd(name, ActorRef.Reserved))
            {
                throw new Exception("The name is already reserved: " + name);
            }
        }

        private long NewUid()
        {
            var auid = Interlocked.Increment(ref uid);
            return auid;
        }

        private static string base64chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789+~";
        private long uid = 0;
        private  string GetActorName(Props props, string name,long uid)
        {            
            var next = uid;
            if (name == null)
            {
                var sb = new StringBuilder("$");

                while(next != 0)
                {
                    var index = (int)(next & 63);
                    var c = base64chars[index];
                    sb.Append(c);
                    next = next >> 6;
                }
                name = sb.ToString();
            }
            return name;
        }

        public virtual void NewActor()
        {
            //set the thread static context or things will break
            this.UseThreadContext( () =>
            {
                //TODO: where should deployment be handled?
                var deployPath = Self.Path.ToStringWithoutAddress();
                var deploy = System.Deployer.Lookup(deployPath);
                behaviorStack.Clear();
                var instance = Props.NewActor();
                instance.supervisorStrategy = Props.SupervisorStrategy; //defaults to null - won't affect lazy instantion unless explicitly set in props
                instance.AroundPreStart();                
            });
        }

        /// <summary>
        /// May be called from anyone
        /// </summary>
        /// <returns></returns>
        public IEnumerable<InternalActorRef> GetChildren()
        {
            return this.Children.Values.ToArray();
        }

        public ActorCell(ActorSystem system,string name,Mailbox mailbox)
        {
            this.Parent = null;
            
            this.System = system;
            this.Self = new LocalActorRef(new RootActorPath(System.Provider.Address, name), this);
            this.Props = null;
            this.Dispatcher = System.Dispatchers.FromConfig("akka.actor.default-dispatcher");
            mailbox.Setup(this.Dispatcher);
            this.Mailbox = mailbox;
            this.Mailbox.Invoke = this.Invoke;
            this.Mailbox.SystemInvoke = this.SystemInvoke;            
        }

        public ActorCell(ActorSystem system,InternalActorRef supervisor, Props props, ActorPath path, Mailbox mailbox)
        {
            this.Parent = supervisor;
            this.System = system;
            this.Self = new LocalActorRef(path, this);
            this.Props = props;
            this.Dispatcher = System.Dispatchers.FromConfig(props.Dispatcher);
            mailbox.Setup(this.Dispatcher);
            this.Mailbox = mailbox;
            this.Mailbox.Invoke = this.Invoke;
            this.Mailbox.SystemInvoke = this.SystemInvoke;
        }

        public void UseThreadContext(Action action)
        {
            var tmp = Current;
            current = this;
            try
            {
                action();
            }
            finally
            {
                //ensure we set back the old context
                current = tmp;
            }
        }


        public void Become(Receive receive)
        {
            behaviorStack.Push(receive);
            CurrentBehavior = receive;
        }
        public void Unbecome()
        {
            CurrentBehavior = behaviorStack.Pop(); ;
        }

        internal void Post(ActorRef sender, object message)
        {
            if (Mailbox == null)
            {
                return;
                //stackoverflow if this is the deadletters actorref
                //this.System.DeadLetters.Tell(new DeadLetter(message, sender, this.Self));
            }

            if (System.Settings.SerializeAllMessages && !(message is NoSerializationVerificationNeeded))
            {
                var serializer = System.Serialization.FindSerializerFor(message);
                var serialized = serializer.ToBinary(message);
                var deserialized = System.Serialization.Deserialize(serialized, serializer.Identifier, message.GetType());
                message = deserialized;
            }

            var m = new Envelope
            {
                Sender = sender,
                Message = message,
            };
            Mailbox.Post(m);
        }

        /// <summary>
        /// May only be called from the owner actor
        /// </summary>
        /// <param name="watchee"></param>
        public void Watch(ActorRef watchee)
        {
            Watchees.Add(watchee);
            watchee.Tell(new Watch(watchee,Self));
        }

        /// <summary>
        /// May only be called from the owner actor
        /// </summary>
        /// <param name="watchee"></param>
        public void Unwatch(ActorRef watchee)
        {
            Watchees.Remove(watchee);
            watchee.Tell(new Unwatch(watchee,Self));
        }

        //public void Kill()
        //{
        //    Mailbox.Stop();
        //}
    }
}
