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
            private readonly List<Relation> relationList = new List<Relation>
            {
                Relation.Hostile,
                Relation.Neutral,
                Relation.Allied,
            };

            private readonly IMyShipController controller;
            private readonly IMyBroadcastListener listener;
            
            private List<TargetData> Entities =>
                (
                    from target in targets.Values
                    where target.Relation == relationList[relationSelection]
                    orderby target.LastSeen
                    select target
                ).ToList();

            private int entityPageSize;

            private int relationSelection = 0;
            private int entityPageStart = 0;
            private int entityPageOffset = 0;

            private int EntityPageEnd => Math.Min(entityPageSize, Entities.Count);
            private int EntitySelection => entityPageStart + entityPageOffset;

            public TangosRadarLogger(Program program) : base(program)
            {
                surfaceTypes = new Dictionary<string, Action<IMyTextSurface>>
                {
                    { "Main", surface => textSurfaces.Add(surface) },
                };

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

                            return block.CustomName.Contains(Settings.Global.LCDTag) && block.IsSameConstructAs(program.Me);
                        }
                    );

                    if (surfaceProviders.Count == 0)
                    {
                        throw new Exception($"No text surface with {Settings.Global.LCDTag} tag found.");
                    }
                    else if (surfaceProviders.Count > 1)
                    {
                        throw new Exception($"Too many surfaces with {Settings.Global.LCDTag} tag found.");
                    }

                    GetSurfaces(surfaceProviders[0]);

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
                
                if (signal is Exit)
                {
                    entityPageStart = 0;
                    entityPageOffset = 0;

                    return Response.Handled;
                }

                return Response.Unhandled;
            }

            protected Response ListView(ISignal signal)
            {
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
                            if (entityPageOffset > 0)
                            {
                                entityPageOffset--;
                            }
                            else if (entityPageStart > 0)
                            {
                                entityPageStart--;
                            }

                            RenderList();

                            return Response.Handled;
                        case "down":
                            if (entityPageOffset < EntityPageEnd - 1)
                            {
                                entityPageOffset++;
                            }
                            else if (EntitySelection < Entities.Count - 1)
                            {
                                entityPageStart++;
                            }

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

            override
            protected void Info()
            {
                var text = new StringBuilder()
                .AppendLine($"{NAME} v{VERSION}")
                .AppendLine("===================")
                .AppendLine()
                .AppendLine($"entityPageSize: {entityPageSize}")
                .AppendLine($"entityPageStart: {entityPageStart}")
                .AppendLine($"entityPageOffset: {entityPageOffset}")
                .AppendLine($"EntityPageEnd: {EntityPageEnd}")
                .AppendLine($"EntitySelection: {EntitySelection}")
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
                    surface.Font = "Monospace";

                    surface.WriteText(text);
                }
            }

            private void RenderList()
            {
                {
                    var surface = textSurfaces[0];
                    var textSize = surface.MeasureStringInPixels(new StringBuilder("W"), "Monospace", surface.FontSize);

                    entityPageSize = (int)Math.Floor(surface.SurfaceSize.Y / textSize.Y) - 1;
                }

                var text = new StringBuilder();

                for (var i = 0; i < EntityPageEnd; i++)
                {
                    var index = entityPageStart + i;
                    var entity = Entities[index];
                    var leader = EntitySelection == index ? ">" : " ";
                    var identifier = string.IsNullOrWhiteSpace(entity.Name) ? "Unknown Name" : entity.Name;
                    var line = 1 + index;

                    text.AppendLine($"{leader}{line,2}. [{entity.ThreatScore,2:D}:{entity.Type}] {identifier}");
                }

                foreach (var surface in textSurfaces)
                {
                    surface.ContentType = ContentType.TEXT_AND_IMAGE;
                    surface.Font = "Monospace";

                    surface.WriteText(text);
                }
            }

            private void RenderDetails()
            {
                var entity = Entities[EntitySelection];
                var identifier = string.IsNullOrWhiteSpace(entity.Name) ? "Unknown Name" : entity.Name;
                
                var text = new StringBuilder()
                .AppendLine($"Type: {entity.Type}")
                .AppendLine($"Relation: {RelationName(entity.Relation)}")
                .AppendLine($"Name: {entity.Name}")
                .AppendLine($"Threat: {entity.ThreatScore}")
                .AppendLine($"LastSeen: {entity.Age.TotalMinutes:N0} minutes ago")
                .AppendLine()
                .AppendLine($"GPS:{identifier}:{entity.Position.X:#.00}:{entity.Position.Y:#.00}:{entity.Position.Z:#.00}:#FF00FFFF:");

                foreach (var surface in textSurfaces)
                {
                    surface.ContentType = ContentType.TEXT_AND_IMAGE;
                    surface.Font = "Monospace";

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
