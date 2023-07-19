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
        public class Logger
        {
            private static readonly RingBuffer<string> buffer = new RingBuffer<string>(30);

            public static void Log(string line)
            {
                buffer.Add(line);
            }

            public static void Log(MyIni ini)
            {
                buffer.Add(ini.ToString());
            }

            public static void Log(StringBuilder text)
            {
                buffer.Add(text.ToString());
            }

            public static void Clear()
            {
                buffer.Clear();
            }

            public static string AsString
            {
                get
                {
                    var text = new StringBuilder();

                    foreach (var line in buffer)
                    {
                        text.AppendLine(line);
                    }

                    return text.ToString();
                }
            }

            private Logger() { }
        }
    }
}
