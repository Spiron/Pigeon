﻿using Akka.Actor;
using Akka.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.Routing
{
    public class RoundRobinRoutingLogic : RoutingLogic
    {
        private int next=-1;
        public override Routee Select(object message, Routee[] routees)
        {
            if (routees == null || routees.Length == 0)
            {
                return Routee.NoRoutee;
            }
            else
            {
                return routees[Interlocked.Increment(ref next) % routees.Length];
            }
        }
    }

    public class RoundRobinGroup : Group
    {
        public RoundRobinGroup(Config config)
            : base(config.GetStringList("routees.paths"))
        { }
        public RoundRobinGroup(params string[] paths)
            : base(paths)
        { }
        public RoundRobinGroup(IEnumerable<string> paths) : base(paths)
        { }

        public RoundRobinGroup(IEnumerable<ActorRef> routees) : base(routees) { }

        public override Router CreateRouter()
        {
            return new Router(new RoundRobinRoutingLogic());
        }
    }

    public class RoundRobinPool : Pool
    {
        public RoundRobinPool(int nrOfInstances, Resizer resizer, SupervisorStrategy supervisorStrategy, string routerDispatcher, bool usePoolDispatcher = false) : base(nrOfInstances,resizer,supervisorStrategy,routerDispatcher,usePoolDispatcher)
        {
        }
        public override IEnumerable<Routee> GetRoutees(ActorSystem system)
        {
            throw new NotImplementedException();
        }

        public override RouterActor CreateRouterActor()
        {
            throw new NotImplementedException();
        }

        public override Router CreateRouter()
        {
            throw new NotImplementedException();
        }
    }
}

