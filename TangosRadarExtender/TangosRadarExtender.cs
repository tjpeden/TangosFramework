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
        public class TangosRadarExtender : StateMachine
        {
            private readonly WCPBAPI wcapi;
            private readonly Program program;

            private readonly IMyRadioAntenna antenna;

            private DateTime LastTransmission;
            private double LastTransmissionSeconds => DateTime.Now.Subtract(LastTransmission).TotalSeconds;

            public readonly Dictionary<long, TargetData> targets;

            public TangosRadarExtender(Program program) : base()
            {
                this.program = program;

                targets = new Dictionary<long, TargetData>();

                wcapi = new WCPBAPI();

                LastTransmission = DateTime.Now;

                try
                {
                    program.Me.CustomData = Settings.Global.Syncronize(program.Me.CustomData);

                    var antennas = new List<IMyRadioAntenna>();

                    program.GridTerminalSystem.GetBlocksOfType(antennas);

                    if (antennas.Count > 0)
                    {
                        antenna = antennas[0];

                        antenna.Enabled = false;
                    }
                    else
                    {
                        throw new Exception("Antenna not found.");
                    }

                    program.Runtime.UpdateFrequency = UpdateFrequency.Update100;
                }
                catch (Exception error)
                {
                    Logger.Log($"{error}");
                }
                finally
                {
                    Info();
                }
            }
            
            override
            protected Response Initial(ISignal signal)
            {
                return Response.Transition(Activate);
            }

            protected Response Activate(ISignal signal)
            {
                if (signal is UpdateSource)
                {
                    try
                    {
                        if (wcapi.Activate(program.Me))
                        {
                            Logger.Log("WeaponCore PB API activated.");

                            return Response.Transition(GetTargets);
                        }
                        else
                        {
                            throw new Exception("Unabled to activate WeaponCore PB API.");
                        }
                    }
                    catch (Exception error)
                    {
                        Logger.Log($"Error:\n{error.Message}");
                    }
                    finally
                    {
                        Info();
                    }
                }
               
                return Response.Handled;
            }

            protected Response Active(ISignal signal)
            {
                if (signal is UpdateInfo && Settings.Global.Debug)
                {
                    Info();
                }
                
                return Response.Handled;
            }

            protected Response GetTargets(ISignal signal)
            {
                if (signal is Enter)
                {
                    foreach (var item in targets.ToList())
                    {
                        if (item.Value.Age >= 5)
                        {
                            targets.Remove(item.Key);
                        }
                    }

                    return Response.Handled;
                }

                if (signal is UpdateSource)
                {
                    try
                    {
                        var wcTargets = new Dictionary<MyDetectedEntityInfo, float>();
                        var wcObstructions = new List<MyDetectedEntityInfo>();

                        wcapi.GetSortedThreats(program.Me, wcTargets);

                        foreach (var target in wcTargets)
                        {
                            AddTarget(target.Key, target.Value);
                        }

                        wcapi.GetObstructions(program.Me, wcObstructions);

                        foreach (var obstruction in wcObstructions)
                        {
                            AddTarget(obstruction);
                        }

                        return Response.Transition(Evaluate);
                    }
                    catch (Exception error)
                    {
                        HandleError(error);
                    }

                    return Response.Handled;
                }

                return Response.Parent(Active);
            }

            protected Response Evaluate(ISignal signal)
            {
                if (signal is UpdateSource)
                {
                    try
                    {
                        if (targets.Count > 0 && LastTransmissionSeconds >= Settings.Global.Delay)
                        {
                            var enemySighted = targets.Any(pair => pair.Value.Relation == Relation.Hostile);

                            if (enemySighted)
                            {
                                return Response.Transition(Transmit);
                            }
                        }

                        return Response.Transition(GetTargets);
                    }
                    catch (Exception error)
                    {
                        HandleError(error);
                    }

                    return Response.Handled;
                }

                return Response.Parent(Active);
            }

            protected Response Transmit(ISignal signal)
            {
                if (signal is Enter)
                {
                    program.Runtime.UpdateFrequency = UpdateFrequency.Update10;
                    antenna.Enabled = true;

                    return Response.Handled;
                }

                if (signal is UpdateSource)
                {
                    try
                    {
                        var ini = new MyIni();

                        foreach (var pair in targets)
                        {
                            var id = pair.Key.ToString();
                            var target = pair.Value;

                            ini.Set(id, "Name", target.Name);
                            ini.Set(id, "Type", target.Type);
                            ini.Set(id, "Threat", target.Threat);
                            ini.Set(id, "Relation", target.Relation);
                            ini.Set(id, "Position", target.Position);
                            ini.Set(id, "Velocity", target.Velocity);
                            ini.Set(id, "LastSeen", target.LastSeen);
                        }

                        program.IGC.SendBroadcastMessage(Settings.Global.BroadcastTag, ini.ToString());

                        LastTransmission = DateTime.Now;

                        return Response.Transition(DisableAntenna);
                    }
                    catch (Exception error)
                    {
                        HandleError(error);
                    }

                    return Response.Handled;
                }

                return Response.Parent(Active);
            }

            protected Response DisableAntenna(ISignal signal)
            {
                if (signal is UpdateSource)
                {
                    try
                    {
                        antenna.Enabled = false;

                        return Response.Transition(GetTargets);
                    }
                    catch (Exception error)
                    {
                        HandleError(error);
                    }

                    return Response.Handled;
                }

                if (signal is Exit)
                {
                    program.Runtime.UpdateFrequency = UpdateFrequency.Update100;

                    return Response.Handled;
                }

                return Response.Parent(Active);
            }

            private void AddTarget(MyDetectedEntityInfo info, float threat = 0)
            {
                if (info.EntityId != 0 && !targets.ContainsKey(info.EntityId))
                {
                    TargetData target = new TargetData
                    {
                        Threat = threat,
                        Name = info.Name,
                        Position = info.Position,
                        Velocity = info.Velocity,
                    };

                    var focus = wcapi.GetAiFocus(info.EntityId);

                    if (focus.HasValue && focus.Value.EntityId != 0)
                    {
                        target.Targeting = focus.Value.EntityId;
                    }

                    switch (info.Type)
                    {
                        case MyDetectedEntityType.CharacterHuman:
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

                    targets[info.EntityId] = target;
                }
            }

            private void HandleError(Exception error)
            {
                Logger.Log($"Error:\n{error.Message}");

                Info();

                program.Runtime.UpdateFrequency = UpdateFrequency.None;
            }

            private void Info()
            {
                var text = new StringBuilder();

                text.AppendLine($"{NAME} v{VERSION}");
                text.AppendLine("===================");

                text.AppendLine();

                text.AppendLine($"Task: {CurrentStateName}");

                text.AppendLine();

                text.AppendLine("Log:");

                text.AppendLine(Logger.AsString);

                program.Echo(text.ToString());
            }
        }
    }
}
