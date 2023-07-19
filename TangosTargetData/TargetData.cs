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
using VRage.Scripting;
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

            public TimeSpan Age
            {
                get
                {
                    return DateTime.Now.Subtract(LastSeen);
                }
            }

            public static TargetData FromMyDetectedEntityInfo(MyDetectedEntityInfo info, float threat = 0)
            {
                TargetData target = new TargetData
                {
                    Threat = threat,
                    Name = info.Name,
                    Position = info.Position,
                    Velocity = info.Velocity,
                };

                switch (info.Type)
                {
                    case MyDetectedEntityType.CharacterHuman:
                    case MyDetectedEntityType.CharacterOther:
                        target.Type = "P";

                        break;
                    case MyDetectedEntityType.LargeGrid:
                        target.Type = "L";

                        break;
                    case MyDetectedEntityType.SmallGrid:
                        target.Type = "S";

                        break;
                    default:
                        target.Type = "?";

                        break;
                }

                switch (info.Relationship)
                {
                    case MyRelationsBetweenPlayerAndBlock.Enemies:
                        target.Relation = Relation.Hostile;

                        break;

                    case MyRelationsBetweenPlayerAndBlock.Owner:
                    case MyRelationsBetweenPlayerAndBlock.FactionShare:
                    case MyRelationsBetweenPlayerAndBlock.Friends:
                        target.Relation = Relation.Allied;

                        break;

                    default:
                        target.Threat = -1;

                        if (info.Name == "MyVoxelMap")
                        {
                            target.Relation = Relation.None;

                            break;
                        }

                        if (info.Type == MyDetectedEntityType.Unknown)
                        {
                            target.Relation = Relation.Allied;

                            break;
                        }

                        target.Relation = Relation.Neutral;

                        break;
                }

                target.LastSeen = DateTime.Now;

                return target;
            }

            public static void Parse(string text, Dictionary<long, TargetData> targets)
            {
                var ini = new MyIni();

                if (ini.TryParse(text))
                {
                    var ids = new List<string>();

                    ini.GetSections(ids);

                    Logger.Log($"Parsing {ids.Count} targets");

                    foreach (var id in ids)
                    {
                        var entityId = long.Parse(id);
                        var target = new TargetData
                        {
                            Name = ini.Get(id, "Name").ToString(),
                            Type = ini.Get(id, "Type").ToString(),

                            Threat = ini.Get(id, "Threat").ToFloat(),

                            Targeting = ini.Get(id, "Targeting").ToInt64(),

                            Relation = (Relation)ini.Get(id, "Relation").ToByte(),

                            Position = ini.Get(id, "Position").ToVector3D(),
                            Velocity = ini.Get(id, "Velocity").ToVector3D(),

                            LastSeen = ini.Get(id, "LastSeen").ToDateTime(),
                        };

                        targets[entityId] = target;
                    }
                }
            }
        }
    }
}
