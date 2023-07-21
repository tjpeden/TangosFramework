using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        public class Settings
        {
            public static readonly Settings Global = new Settings();

            public bool Debug { get; private set; } = true;

            public string LCDTag { get; private set; } = "[Indy:LCD]";

            private Settings() { }

            public string Syncronize(string data)
            {
                MyIni ini = new MyIni();

                if (ini.TryParse(data))
                {
                    Debug = ini.Get(NAME, "Debug").ToBoolean(Debug);

                    LCDTag = ini.Get(NAME, "LCDTag").ToString(LCDTag);
                }

                ini.Set(NAME, "Debug", Debug);

                ini.Set(NAME, "LCDTag", LCDTag);

                return ini.ToString() + ini.EndContent;
            }
        }
    }
}
