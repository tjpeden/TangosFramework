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
            private readonly Program program;
            private readonly WCPBAPI wcapi = new WCPBAPI();

            private readonly Dictionary<long, TargetData> targets = new Dictionary<long, TargetData>();
            
            private readonly IMyRadioAntenna antenna;

            private DateTime LastTransmission;

            private double LastTransmissionSeconds => DateTime.Now.Subtract(LastTransmission).TotalSeconds;
            private bool EnemySighted => targets.Any(pair => pair.Value.Relation == Relation.Hostile);

            public TangosRadarExtender(Program program) : base()
            {
                this.program = program;

                LastTransmission = DateTime.Now;

                RegisterChildren(
                    Active,
                    new List<Func<ISignal, Response>>
                    {
                        GetTargets,
                        Evaluate,
                        Transmit,
                        DisableAntenna,
                    }
                );

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
                return TransitionTo(Activate);
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

                            return TransitionTo(GetTargets);
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
                    
                    return Response.Handled;
                }

                return Response.Unhandled;
            }

            protected Response GetTargets(ISignal signal)
            {
                if (signal is Enter)
                {
                    foreach (var item in targets.ToList())
                    {
                        if (item.Value.Age.TotalHours >= 24)
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
                        MyDetectedEntityInfo? focus;
                        var wcTargets = new Dictionary<MyDetectedEntityInfo, float>();
                        var wcObstructions = new List<MyDetectedEntityInfo>();

                        wcapi.GetSortedThreats(program.Me, wcTargets);

                        foreach (var target in wcTargets)
                        {
                            if (target.Key.EntityId == 0) continue;

                            targets[target.Key.EntityId] = TargetData.FromMyDetectedEntityInfo(target.Key, target.Value);

                            focus = wcapi.GetAiFocus(target.Key.EntityId);

                            if (focus.HasValue && focus.Value.EntityId != 0)
                            {
                                targets[target.Key.EntityId].Targeting = focus.Value.EntityId;
                            }
                        }

                        wcapi.GetObstructions(program.Me, wcObstructions);

                        foreach (var obstruction in wcObstructions)
                        {
                            if (obstruction.EntityId == 0) continue;

                            targets[obstruction.EntityId] = TargetData.FromMyDetectedEntityInfo(obstruction);

                            focus = wcapi.GetAiFocus(obstruction.EntityId);

                            if (focus.HasValue && focus.Value.EntityId != 0)
                            {
                                targets[obstruction.EntityId].Targeting = focus.Value.EntityId;
                            }
                        }

                        targets.Remove(program.Me.CubeGrid.EntityId);

                        return TransitionTo(Evaluate);
                    }
                    catch (Exception error)
                    {
                        HandleError(error);
                    }

                    return Response.Handled;
                }

                return Response.Unhandled;
            }

            protected Response Evaluate(ISignal signal)
            {
                if (signal is UpdateSource)
                {
                    try
                    {
                        if (targets.Count > 0 && LastTransmissionSeconds >= Settings.Global.Delay)
                        {
                            return TransitionTo(Transmit);
                        }

                        return TransitionTo(GetTargets);
                    }
                    catch (Exception error)
                    {
                        HandleError(error);
                    }

                    return Response.Handled;
                }

                return Response.Unhandled;
            }

            protected Response Transmit(ISignal signal)
            {
                if (signal is Enter)
                {
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
                            ini.Set(id, "Relation", (byte)target.Relation);
                            ini.Set(id, "Position", target.Position);
                            ini.Set(id, "Velocity", target.Velocity);
                            ini.Set(id, "Targeting", target.Targeting);
                            ini.Set(id, "LastSeen", target.LastSeen);
                        }

                        program.IGC.SendBroadcastMessage(Settings.Global.BroadcastTag, ini.ToString());

                        LastTransmission = DateTime.Now;

                        return TransitionTo(DisableAntenna);
                    }
                    catch (Exception error)
                    {
                        HandleError(error);
                    }

                    return Response.Handled;
                }

                return Response.Unhandled;
            }

            protected Response DisableAntenna(ISignal signal)
            {
                if (signal is UpdateSource)
                {
                    try
                    {
                        antenna.Enabled = false;

                        return TransitionTo(GetTargets);
                    }
                    catch (Exception error)
                    {
                        HandleError(error);
                    }

                    return Response.Handled;
                }

                return Response.Unhandled;
            }

            private void HandleError(Exception error)
            {
                Logger.Log($"Error:\n{error.Message}");

                Info();

                program.Runtime.UpdateFrequency = UpdateFrequency.None;
            }

            private void Info()
            {
                var text = new StringBuilder()
                .AppendLine($"{NAME} v{VERSION}")
                .AppendLine("===================")
                .AppendLine()
                .AppendLine($"Task: {CurrentStateName}")
                .AppendLine()
                .AppendLine($"Entities: {targets.Count}")
                .AppendLine($"Enemy Sighted: {EnemySighted}")
                .AppendLine($"Last Transmission: {LastTransmissionSeconds:N0}")
                .AppendLine()
                .AppendLine("Log:")
                .AppendLine(Logger.AsString);

                program.Echo(text.ToString());
            }
        }
    }
}
