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
using VRage.Utils;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        public class TangosIndyInfo : StateMachine
        {
            private readonly Program program;

            private readonly List<IMyTextSurface> assemblerSurfaces = new List<IMyTextSurface>();
            private readonly List<IMyTextSurface> refinerySurfaces = new List<IMyTextSurface>();

            private readonly List<IMyAssembler> assemblers = new List<IMyAssembler>();
            private readonly List<IMyRefinery> refineries = new List<IMyRefinery>();

            public TangosIndyInfo(Program program)
            {
                this.program = program;

                RegisterChildren(
                    Active,
                    new List<Func<ISignal, Response>>
                    {
                        ScanIndustry,
                        ProcessAssemblers,
                        ProcessRefineries,
                    }
                );

                try
                {
                    program.Me.CustomData = Settings.Global.Syncronize(program.Me.CustomData);

                    var surfaceProviders = new List<IMyTextSurfaceProvider>();

                    program.GridTerminalSystem.GetBlocksOfType(
                        surfaceProviders,
                        provider =>
                        {
                            var block = provider as IMyTerminalBlock;

                            if (block.CustomName.Contains(Settings.Global.LCDTag) && block.IsSameConstructAs(program.Me))
                            {
                                GetSurfaces(provider);

                                return true;
                            }

                            return false;
                        }
                    );

                    if (surfaceProviders.Count == 0)
                    {
                        throw new Exception($"No text surface with {Settings.Global.LCDTag} tag found.");
                    }

                    program.Runtime.UpdateFrequency = UpdateFrequency.Update10;
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
                return TransitionTo(ScanIndustry);
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

            protected Response ScanIndustry(ISignal signal)
            {
                if (signal is Enter)
                {
                    assemblers.Clear();
                    refineries.Clear();

                    return Response.Handled;
                }

                if (signal is UpdateSource)
                {
                    try
                    {
                        program.GridTerminalSystem.GetBlocksOfType(assemblers, block => block.IsSameConstructAs(program.Me));
                        program.GridTerminalSystem.GetBlocksOfType(refineries, block => block.IsSameConstructAs(program.Me));

                        if (assemblers.Count > 0)
                        {
                            return TransitionTo(ProcessAssemblers);
                        }

                        if (refineries.Count > 0)
                        {
                            return TransitionTo(ProcessRefineries);
                        }
                    }
                    catch (Exception error)
                    {
                        HandleError(error);
                    }

                    return Response.Handled;
                }

                return Response.Unhandled;
            }

            protected Response ProcessAssemblers(ISignal signal)
            {
                if (signal is UpdateSource)
                {
                    try
                    {
                        var text = new StringBuilder();

                        assemblers.Sort((a, b) => a.CustomName.CompareTo(b.CustomName));

                        foreach (var assembler in assemblers)
                        {
                            text.AppendLine(assembler.CustomName);

                            if (!assembler.IsQueueEmpty)
                            {
                                var items = new List<MyProductionItem>();

                                assembler.GetQueue(items);

                                foreach (var item in items)
                                {
                                    text.AppendLine($"   {item.BlueprintId.SubtypeName}");
                                }
                            }
                        }

                        foreach (var surface in assemblerSurfaces)
                        {
                            surface.ContentType = ContentType.TEXT_AND_IMAGE;

                            surface.WriteText(text.ToString());
                        }

                        if (refineries.Count > 0)
                        {
                            return TransitionTo(ProcessRefineries);
                        }

                        return TransitionTo(ScanIndustry);
                    }
                    catch (Exception error)
                    {
                        HandleError(error);
                    }

                    return Response.Handled;
                }

                return Response.Unhandled;
            }

            protected Response ProcessRefineries(ISignal message)
            {
                if (message is UpdateSource)
                {
                    try
                    {
                        var text = new StringBuilder();

                        refineries.Sort((a, b) => a.CustomName.CompareTo(b.CustomName));

                        foreach (var refinery in refineries)
                        {
                            text.AppendLine(refinery.CustomName);

                            if (refinery.HasInventory)
                            {
                                var items = new List<MyInventoryItem>();

                                refinery.GetInventory().GetItems(items);

                                foreach (var item in items)
                                {
                                    text.AppendLine($"   {item.Type.SubtypeId}");
                                }
                            }
                        }

                        foreach (var surface in refinerySurfaces)
                        {
                            surface.ContentType = ContentType.TEXT_AND_IMAGE;

                            surface.WriteText(text.ToString());
                        }

                        return TransitionTo(ScanIndustry);
                    }
                    catch (Exception error)
                    {
                        HandleError(error);
                    }

                    return Response.Handled;
                }

                return Response.Unhandled;
            }

            private void GetSurfaces(IMyTextSurfaceProvider provider)
            {
                var block = provider as IMyTerminalBlock;
                var ini = new MyIni();

                if (ini.TryParse(block.CustomData))
                {
                    for (int i = 0; i < provider.SurfaceCount; i++)
                    {
                        var value = ini.Get(NAME, $"Surface{i}").ToString("");

                        switch (value)
                        {
                            case "Assembler":
                                assemblerSurfaces.Add(provider.GetSurface(i));

                                break;
                            case "Refinery":
                                refinerySurfaces.Add(provider.GetSurface(i));

                                break;
                            default:
                                value = "";

                                break;
                        }

                        ini.Set(NAME, $"Surface{i}", value);
                    }

                    ini.SetSectionComment(NAME, "Options: Assembler, Refinery");

                    block.CustomData = ini.ToString();
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
