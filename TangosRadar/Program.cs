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
    partial class Program : MyGridProgram
    {
        public const string NAME = "TangosRadar";
        public const string VERSION = "3.4.6";

        private readonly UpdateType Triggers = UpdateType.Trigger | UpdateType.Terminal | UpdateType.Script | UpdateType.Mod;
        private readonly UpdateType Updates = UpdateType.Once | UpdateType.Update1 | UpdateType.Update10 | UpdateType.Update100;

        private readonly TangosRadar machine;

        public Program()
        {
            machine = new TangosRadar(this);
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if ((updateSource & UpdateType.IGC) != 0)
            {
                machine.Handle(IGCSource.Global);
            }

            if ((updateSource & Updates) != 0)
            {
                machine.Handle(UpdateSource.Global);
                machine.Handle(UpdateInfo.Global);
            }

            if ((updateSource & Triggers) != 0)
            {
                machine.Handle(new TriggerSource { Argument = argument });
            }
        }
    }
}
