using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Design;
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
        public class TangosRadarLogger : StateMachine
        {
            private readonly Dictionary<long, TargetData> targets = new Dictionary<long, TargetData>();
            
            private readonly List<IMyTextSurface> textSurfaces = new List<IMyTextSurface>();

            private readonly Program program;

            private readonly IMyShipController controller;
            private readonly IMyBroadcastListener listener;
            private readonly List<Relation> relationList = new List<Relation>
            {
                Relation.Hostile,
                Relation.Neutral,
                Relation.Allied,
            };
            private List<TargetData> Entities =>
                (
                    from target in targets.Values
                    where target.Relation == relationList[relationSelection]
                    orderby target.LastSeen
                    select target
                ).ToList();

            private int relationSelection = 0;
            private int entitySelection = 0;

            public TangosRadarLogger(Program program)
            {
                this.program = program;

                RegisterChildren(
                    Active,
                    new List<Func<ISignal, Response>>
                    {
                        RelationView,
                        ListView,
                        DetailView,
                    }
                );

                try
                {
                    program.Me.CustomData = Settings.Global.Syncronize(program.Me.CustomData);

                    TargetData.Parse(program.Storage, targets);

                    listener = program.IGC.RegisterBroadcastListener(Settings.Global.BroadcastTag);

                    listener.SetMessageCallback();

                    var controllers = new List<IMyShipController>();

                    program.GridTerminalSystem.GetBlocksOfType(
                        controllers,
                        block => block.CustomName.Contains(Settings.Global.ControlTag) && block.IsSameConstructAs(program.Me)
                    );

                    if (controllers.Count == 1)
                    {
                        controller = controllers[0];
                    }
                    else if (controllers.Count > 1)
                    {
                        throw new Exception("Too many controllers tagged. Please tag only one.");
                    }
                    else
                    {
                        Logger.Log("No tagged controller found.");
                    }

                    var surfaceProviders = new List<IMyTextSurfaceProvider>();

                    program.GridTerminalSystem.GetBlocksOfType(
                        surfaceProviders,
                        provider =>
                        {
                            var block = provider as IMyTerminalBlock;

                            if (block.CustomName.Contains(Settings.Global.LCDTag) && block.IsSameConstructAs(program.Me))
                            {
                                GetSurfacesForProvider(provider);

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

            public void Save()
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
                    ini.Set(id, "LastSeen", target.LastSeen);
                }

                program.Storage = ini.ToString();
            }

            override
            protected Response Initial(ISignal signal)
            {
                return TransitionTo(RelationView);
            }

            protected Response Active(ISignal signal)
            {
                if (signal is UpdateInfo && Settings.Global.Debug)
                {
                    Info();

                    return Response.Handled;
                }

                if (signal is IGCSource)
                {
                    while (listener.HasPendingMessage)
                    {
                        MyIGCMessage message = listener.AcceptMessage();

                        Logger.Log($"Message from {message.Source}");

                        if (message.Data is string)
                        {
                            TargetData.Parse(message.Data as string, targets);
                        }
                    }

                    return Response.Handled;
                }

                return Response.Unhandled;
            }

            protected Response RelationView(ISignal signal)
            {
                if (signal is UpdateSource)
                {
                    RenderRelations();

                    return Response.Handled;
                }

                if (signal is TriggerSource)
                {
                    var command = signal as TriggerSource;

                    switch (command.Argument)
                    {
                        case "up":
                            relationSelection--;

                            if (relationSelection < 0) relationSelection = 0;

                            RenderRelations();

                            return Response.Handled;
                        case "down":
                            relationSelection++;

                            if (relationSelection >= relationList.Count) relationSelection = relationList.Count - 1;

                            RenderRelations();

                            return Response.Handled;
                        case "apply": return TransitionTo(ListView);
                    }
                }

                return Response.Unhandled;
            }

            protected Response ListView(ISignal signal)
            {
                if (signal is Enter)
                {
                    entitySelection = 0;

                    return Response.Handled;
                }

                if (signal is UpdateSource)
                {
                    RenderList();

                    return Response.Handled;
                }

                if (signal is TriggerSource)
                {
                    var command = signal as TriggerSource;

                    switch (command.Argument)
                    {
                        case "up":
                            entitySelection--;

                            if (entitySelection < 0) entitySelection = 0;

                            RenderList();

                            return Response.Handled;
                        case "down":
                            entitySelection++;

                            if (entitySelection >= targets.Count) entitySelection = targets.Count - 1;

                            RenderList();

                            return Response.Handled;
                        case "apply": return TransitionTo(DetailView);
                        case "back": return TransitionTo(RelationView);
                    }
                }

                return Response.Unhandled;
            }

            protected Response DetailView(ISignal signal)
            {
                if (signal is UpdateSource)
                {
                    RenderDetails();

                    return Response.Handled;
                }

                if (signal is TriggerSource)
                {
                    var command = signal as TriggerSource;

                    switch (command.Argument)
                    {
                        case "back": return TransitionTo(ListView);
                    }
                }

                return Response.Unhandled;
            }

            //private void Parse(string source)
            //{
            //    var ini = new MyIni();

            //    if (ini.TryParse(source))
            //    {
            //        var ids = new List<string>();

            //        ini.GetSections(ids);

            //        Logger.Log($"Parsing {ids.Count} targets.");

            //        foreach (var id in ids)
            //        {
            //            var entityId = long.Parse(id);
            //            var target = new TargetData
            //            {
            //                Name = ini.Get(id, "Name").ToString(),
            //                Type = ini.Get(id, "Type").ToString(),

            //                Position = ini.Get(id, "Position").ToVector3D(),
            //                Velocity = ini.Get(id, "Velocity").ToVector3D(),

            //                Threat = ini.Get(id, "Threat").ToFloat(),

            //                Relation = (Relation)ini.Get(id, "Relation").ToByte(),

            //                LastSeen = ini.Get(id, "LastSeen").ToDateTime(),
            //            };

            //            targets[entityId] = target;
            //        }
            //    }
            //}

            private void GetSurfacesForProvider(IMyTextSurfaceProvider provider)
            {
                var block = provider as IMyTerminalBlock;
                var ini = new MyIni();

                if (ini.TryParse(block.CustomData))
                {
                    for (int i = 0; i < provider.SurfaceCount; i++)
                    {
                        string value = ini.Get(NAME, $"Surface{i}").ToString();

                        switch (value)
                        {
                            case "Main": textSurfaces.Add(provider.GetSurface(i)); break;
                            default: value = ""; break;
                        }

                        ini.Set(NAME, $"Surface{i}", value);
                    }
                }

                ini.SetSectionComment(NAME, "Options: Main");

                block.CustomData = ini.ToString();
            }

            private void Info()
            {
                var text = new StringBuilder()
                .AppendLine($"{NAME} v{VERSION}")
                .AppendLine("===================")
                .AppendLine()
                .AppendLine($"Task: {CurrentStateName}")
                .AppendLine()
                .AppendLine("Log:")
                .AppendLine(Logger.AsString);

                program.Echo(text.ToString());
            }

            private void RenderRelations()
            {
                var text = new StringBuilder();

                for (var i = 0; i < relationList.Count; i++)
                {
                    var leader = relationSelection == i ? ">" : " ";
                    
                    text.AppendLine($"{leader}{RelationName(relationList[i])}");
                }

                foreach (var surface in textSurfaces)
                {
                    surface.ContentType = ContentType.TEXT_AND_IMAGE;

                    surface.WriteText(text);
                }
            }

            private void RenderList()
            {
                var text = new StringBuilder()
                .AppendLine($"{RelationName(relationList[relationSelection])} Entities Seen: {Entities.Count}");

                for (var i = 0; i < Entities.Count; i++)
                {
                    var entity = Entities[i];
                    var leader = entitySelection == i ? ">" : " ";
                    var identifier = string.IsNullOrWhiteSpace(entity.Name) ? "Unknown Name" : entity.Name;

                    text.AppendLine($"{leader}[{entity.ThreatScore,2:D}:{entity.Type}] {identifier}");
                }

                foreach (var surface in textSurfaces)
                {
                    surface.ContentType = ContentType.TEXT_AND_IMAGE;

                    surface.WriteText(text);
                }
            }

            private void RenderDetails()
            {
                var entity = Entities[entitySelection];
                var identifier = string.IsNullOrWhiteSpace(entity.Name) ? "Unknown Name" : entity.Name;
                
                var text = new StringBuilder()
                .AppendLine($"Type: {entity.Type}")
                .AppendLine($"Relation: {RelationName(entity.Relation)}")
                .AppendLine($"Name: {entity.Name}")
                .AppendLine($"Threat: {entity.ThreatScore}")
                .AppendLine($"LastSeen: {entity.Age.TotalMinutes:N0} minutes ago")
                .AppendLine()
                .AppendLine($"GPS:{identifier}:{entity.Position.X:N2}:{entity.Position.Y:N2}:{entity.Position.Z:N2}:#FF00FFFF:");

                foreach (var surface in textSurfaces)
                {
                    surface.ContentType = ContentType.TEXT_AND_IMAGE;

                    surface.WriteText(text);
                }
            }

            private void PruneOldTargets()
            {
                var oldIds = targets
                .Where(pair => pair.Value.Age.TotalHours > 24)
                .Select(pair => pair.Key)
                .ToList();

                foreach (var id in oldIds)
                {
                    targets.Remove(id);
                }
            }

            private string RelationName(Relation relation)
            {
                switch (relation)
                {
                    case Relation.None: return "None";
                    case Relation.Allied: return "Allied";
                    case Relation.Neutral: return "Neutral";
                    case Relation.Hostile: return "Hostile";
                }

                return "";
            }
        }
    }
}
