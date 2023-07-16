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
        public class Debounce
        {
            private bool current;
            private bool previous;
            
            public bool Current
            {
                get
                {
                    return current;
                }
                
                set
                {
                    previous = current;
                    current = value;
                }
            }

            public bool Rising
            {
                get
                {
                    return !previous && current;
                }
            }

            public bool Falling
            {
                get
                {
                    return previous && !current;
                }
            }

            public Debounce(bool initial = false)
            {
                current = previous = initial;
            }
        }
    }
}
