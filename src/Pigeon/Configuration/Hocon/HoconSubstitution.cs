﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Configuration.Hocon
{
    public class HoconSubstitution : IHoconElement
    {
        public string Path { get;private set; }

        public HoconSubstitution(string path)
        {
            this.Path = path;
        }

        public HoconValue ResolvedValue { get; set; }

        public bool IsString()
        {
            return ResolvedValue.IsString();
        }

        public string GetString()
        {
            return ResolvedValue.GetString();
        }

        public bool IsArray()
        {
            return ResolvedValue.IsArray();
        }

        public IList<HoconValue> GetArray()
        {
            return ResolvedValue.GetArray();
        }
    }
}
