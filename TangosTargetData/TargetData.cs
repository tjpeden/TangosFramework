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
        public enum Relation
        {
            None,
            Allied,
            Neutral,
            Hostile,
        }

        public class TargetData
        {
            public string Name;
            public string Type;
            public float Threat;
            public long Targeting;
            public Relation Relation;
            public Vector3D Position;
            public Vector3D Velocity;
            public DateTime LastSeen;

            public Vector3 RelativePosition;
            public double Distance;
            public string Icon;
            public Color IconColor;
            public Color ElevationColor;
            public bool TargetingMe;
            public bool CurrentTarget;

            public int ThreatScore
            {
                get
                {
                    if (Threat >= 5) return 10;
                    if (Threat >= 4) return 9;
                    if (Threat >= 3) return 8;
                    if (Threat >= 2) return 7;
                    if (Threat >= 1) return 6;
                    if (Threat >= 0.5) return 5;
                    if (Threat >= 0.25) return 4;
                    if (Threat >= 0.125) return 3;
                    if (Threat >= 0.0625) return 2;
                    if (Threat >= 0) return 1;
                    
                    return 0;
                }
            }

            public double Age
            {
                get
                {
                    return DateTime.Now.Subtract(LastSeen).TotalSeconds;
                }
            }
        }
    }
}
