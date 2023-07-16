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
        public class TangosRadar : StateMachine
        {
            private readonly Dictionary<Relation, string> spriteMap = new Dictionary<Relation, string>
            {
                { Relation.None, "SemiCircle" },
                { Relation.Allied, "SquareSimple" },
                { Relation.Neutral, "Triangle" },
                { Relation.Hostile, "Circle" },
            };

            private readonly WCPBAPI wcapi;
            private readonly Program program;

            private readonly RadarRenderer renderer;

            private readonly Debounce enemyDetected;
            private readonly Debounce enemyClose;

            private readonly IMyShipController controller;

            private readonly IMyBlockGroup warningGroup;
            private readonly IMyBlockGroup alarmGroup;

            private readonly List<IMyTextSurfaceProvider> surfaceProviders;

            private readonly List<IMyTextSurface> radarSurfaces;
            private readonly List<IMyTextSurface> enemySurfaces;
            private readonly List<IMyTextSurface> friendSurfaces;

            private readonly List<long> enemyTargets;
            private readonly List<long> friendTargets;

            private List<IMyTextSurface>.Enumerator radarSurfaceEnumerator;

            private DateTime lastSpriteCacheReset;

            private float calculatedMaxRange = 0f;

            public readonly IMyBroadcastListener listener;

            public float MaxRange
            {
                get
                {
                    if (Settings.Global.OverrideMaxRange)
                    {
                        return Settings.Global.MaxRange;
                    }

                    return calculatedMaxRange;
                }
            }

            public int EnemyTargetCount => enemyTargets.Count;
            public int FriendlyTargetCount => friendTargets.Count;

            public readonly Dictionary<long, TargetData> targets;

            public readonly List<long> targetsAbovePlane;
            public readonly List<long> targetsBelowPlane;

            public long currentTarget;

            public TangosRadar(Program program) : base()
            {
                this.program = program;

                renderer = new RadarRenderer(this);

                targets = new Dictionary<long, TargetData>();

                surfaceProviders = new List<IMyTextSurfaceProvider>();

                radarSurfaces = new List<IMyTextSurface>();
                enemySurfaces = new List<IMyTextSurface>();
                friendSurfaces = new List<IMyTextSurface>();

                enemyTargets = new List<long>();
                friendTargets = new List<long>();
                targetsAbovePlane = new List<long>();
                targetsBelowPlane = new List<long>();

                enemyDetected = new Debounce();
                enemyClose = new Debounce();

                wcapi = new WCPBAPI();

                lastSpriteCacheReset = DateTime.Now;

                try
                {
                    program.Me.CustomData = Settings.Global.Syncronize(program.Me.CustomData);

                    listener = program.IGC.RegisterBroadcastListener(Settings.Global.BroadcastTag);

                    listener.SetMessageCallback();

                    var controllers = new List<IMyShipController>();

                    program.GridTerminalSystem.GetBlocksOfType(controllers, block => block.CustomName.Contains(Settings.Global.ControlTag) && block.IsSameConstructAs(program.Me));

                    if (controllers.Count == 1)
                    {
                        controller = controllers[0];
                    }
                    else
                    {
                        throw new Exception("Controller not found.");
                    }

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

                    if (radarSurfaces.Count > 0)
                    {
                        Logger.Log($"Radar Surfaces found: {radarSurfaces.Count}");
                    }
                    else
                    {
                        throw new Exception("Radar LCD not found.");
                    }

                    Logger.Log($"Enemy Surfaces found: {enemySurfaces.Count}");
                    Logger.Log($"Friend Surfaces found: {friendSurfaces.Count}");

                    warningGroup = program.GridTerminalSystem.GetBlockGroupWithName(Settings.Global.WarningGroup);

                    if (warningGroup == null)
                    {
                        Logger.Log("Warning group not configured.");
                    }

                    alarmGroup = program.GridTerminalSystem.GetBlockGroupWithName(Settings.Global.AlarmGroup);

                    if (alarmGroup == null)
                    {
                        Logger.Log("Alarm group not configured.");
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
                if (signal is IGCSource)
                {
                    Logger.Log("IGCSource");

                    while (listener.HasPendingMessage)
                    {
                        MyIGCMessage IGCsignal = listener.AcceptMessage();

                        if (IGCsignal.Data is string)
                        {
                            var ini = new MyIni();

                            if (ini.TryParse(IGCsignal.Data as string))
                            {
                                var ids = new List<string>();

                                ini.GetSections(ids);

                                Logger.Log($"Parsing {ids.Count} target from {IGCsignal.Source}");

                                foreach (var id in ids)
                                {
                                    var entityId = long.Parse(id);
                                    var target = new TargetData
                                    {
                                        Name = ini.Get(id, "Name").ToString(),
                                        Type = ini.Get(id, "Type").ToString(),

                                        Position = ini.Get(id, "Position").ToVector3D(),
                                        Velocity = ini.Get(id, "Velocity").ToVector3D(),

                                        Threat = ini.Get(id, "Threat").ToFloat(),
                                        Relation = ini.Get(id, "Relation").ToRelation(),

                                        LastSeen = ini.Get(id, "LastSeen").ToDateTime(),
                                    };

                                    targets[entityId] = target;
                                }
                            }
                        }
                    }
                }

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
                        if (item.Value.Age >= 30)
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

                        var focus = wcapi.GetAiFocus(program.Me.CubeGrid.EntityId);

                        if (focus.HasValue && focus.Value.EntityId != 0)
                        {
                            currentTarget = focus.Value.EntityId;
                        }

                        targets.Remove(program.Me.CubeGrid.EntityId);

                        return Response.Transition(ProcessTargetList);
                    }
                    catch (Exception error)
                    {
                        HandleError(error);
                    }

                    return Response.Handled;
                }
                
                return Response.Parent(Active);
            }

            protected Response ProcessTargetList(ISignal signal)
            {
                if (signal is Enter)
                {
                    enemyTargets.Clear();
                    friendTargets.Clear();
                    targetsAbovePlane.Clear();
                    targetsBelowPlane.Clear();

                    return Response.Handled;
                }

                if (signal is UpdateSource)
                {
                    try
                    {
                        calculatedMaxRange = 1000;

                        foreach (var item in targets)
                        {
                            var targetEntityID = item.Key;
                            var target = item.Value;

                            var transformedPosition = Vector3D.TransformNormal(
                                target.Position - controller.WorldMatrix.Translation,
                                Matrix.Transpose(controller.WorldMatrix)
                            );

                            target.RelativePosition = new Vector3(
                                (transformedPosition.X / MaxRange),
                                (transformedPosition.Z / MaxRange),
                                (transformedPosition.Y / MaxRange)
                            );

                            target.Distance = Vector3D.Distance(target.Position, program.Me.CubeGrid.GetPosition());
                            target.Icon = spriteMap[target.Relation];

                            switch (target.Relation)
                            {
                                case Relation.Hostile:
                                    if (program.Me.CubeGrid.EntityId == target.Targeting)
                                    {
                                        target.IconColor = Settings.Global.EnemyTargetingMeColor;
                                        target.ElevationColor = Settings.Global.EnemyTargetingMeColor;
                                        target.TargetingMe = true;
                                    }
                                    else
                                    {
                                        target.IconColor = Settings.Global.EnemyIconColor;
                                        target.ElevationColor = Settings.Global.EnemyElevationColor;
                                    }

                                    enemyTargets.Add(targetEntityID);

                                    break;
                                case Relation.Allied:
                                    target.IconColor = Settings.Global.FriendlyIconColor;
                                    target.ElevationColor = Settings.Global.FriendlyElevationColor;

                                    friendTargets.Add(targetEntityID);

                                    break;
                                case Relation.Neutral:
                                    target.IconColor = Settings.Global.NeutralIconColor;
                                    target.ElevationColor = Settings.Global.NeutralElevationColor;

                                    friendTargets.Add(targetEntityID);

                                    break;

                                default:
                                    target.IconColor = Settings.Global.ObstructionIconColor;
                                    target.ElevationColor = Settings.Global.ObstructionElevationColor;

                                    break;
                            }

                            if (target.Type == "P")
                            {
                                target.IconColor = SuitColor(target.IconColor);
                                target.ElevationColor = SuitColor(target.ElevationColor);
                            }

                            if (targetEntityID == currentTarget)
                            {
                                target.CurrentTarget = true;
                            }

                            if (Settings.Global.OverrideMaxRange)
                            {
                                if (target.Distance > MaxRange) continue;
                            }
                            else
                            {
                                if (target.Relation != Relation.None && target.Distance > calculatedMaxRange)
                                {
                                    float divisor = target.Distance < 5000 ? 1000 : 5000;
                                    int multiplier = (int)(target.Distance / divisor + 1);

                                    calculatedMaxRange = divisor * multiplier;
                                }

                                if (target.Relation == Relation.None && target.Distance > calculatedMaxRange)
                                {
                                    continue;
                                }
                            }

                            if (!Settings.Global.DrawObstructions && target.Relation == Relation.None)
                            {
                                continue;
                            }

                            if (target.RelativePosition.Z >= 0)
                            {
                                targetsAbovePlane.Add(targetEntityID);
                            }
                            else
                            {
                                targetsBelowPlane.Add(targetEntityID);
                            }
                        }

                        enemyTargets.Sort(CompareTargetEntityIDs);
                        friendTargets.Sort(CompareTargetEntityIDs);
                        targetsAbovePlane.Sort(CompareTargetEntityIDs);
                        targetsBelowPlane.Sort(CompareTargetEntityIDs);

                        return Response.Transition(UpdateEnemyLCD);
                    }
                    catch (Exception error)
                    {
                        HandleError(error);
                    }

                    return Response.Handled;
                }
                
                return Response.Parent(Active);
            }

            protected Response UpdateEnemyLCD(ISignal signal)
            {
                if (signal is UpdateSource)
                {
                    try
                    {
                        var title = "Hostiles";
                        var lines = new List<string>
                            {
                                ""
                            };

                        foreach (var id in enemyTargets)
                        {
                            var target = targets[id];
                            var leader = target.CurrentTarget ? ">" : target.TargetingMe ? "!" : " ";
                            var line = $"{leader}[{target.ThreatScore,2:D}:{target.Type}] {target.Distance:N1}m @ {target.Velocity.Length():N0}m/s {target.Name}";

                            if (target.CurrentTarget)
                            {
                                lines.Insert(1, line);
                            }
                            else
                            {
                                lines.Add(line);
                            }
                        }

                        foreach (var surface in enemySurfaces)
                        {
                            surface.ContentType = ContentType.TEXT_AND_IMAGE;
                            surface.BackgroundColor = Color.Black;
                            surface.FontColor = Color.DarkRed;
                            surface.Font = Settings.Global.Font;

                            var dashes = (int)((26 / surface.FontSize - title.Length) / 2);

                            lines[0] = $"{new string('-', dashes)}{title}{new string('-', dashes)}";

                            surface.WriteText(string.Join("\n", lines));
                        }

                        return Response.Transition(UpdateFriendLCD);
                    }
                    catch (Exception error)
                    {
                        HandleError(error);
                    }

                    return Response.Handled;
                }
                
                return Response.Parent(Active);
            }

            protected Response UpdateFriendLCD(ISignal signal)
            {
                if (signal is UpdateSource)
                {
                    try
                    {
                        var title = "Friendlies";
                        var lines = new List<string>
                            {
                                ""
                            };

                        foreach (var id in friendTargets)
                        {
                            var target = targets[id];

                            if (target.Name.Length > 0)
                            {
                                lines.Add($"{target.Distance,8:N1}m | {target.Name}");
                            }
                            else
                            {
                                lines.Add($"{target.Distance,8:N1}m | {id:D}");
                            }
                        }

                        foreach (var surface in friendSurfaces)
                        {
                            surface.ContentType = ContentType.TEXT_AND_IMAGE;
                            surface.BackgroundColor = Color.Black;
                            surface.FontColor = Color.Green;
                            surface.Font = Settings.Global.Font;

                            var dashes = (int)((26 / surface.FontSize - title.Length) / 2);

                            lines[0] = $"{new string('-', dashes)}{title}{new string('-', dashes)}";

                            surface.WriteText(string.Join("\n", lines));
                        }

                        return Response.Transition(UpdateRadar);
                    }
                    catch (Exception error)
                    {
                        HandleError(error);
                    }

                    return Response.Handled;
                }
                
                return Response.Parent(Active);
            }

            protected Response UpdateRadar(ISignal signal)
            {
                if (signal is Enter)
                {
                    radarSurfaceEnumerator = radarSurfaces.GetEnumerator();

                    if ((DateTime.Now - lastSpriteCacheReset).TotalSeconds >= 10)
                    {
                        renderer.ClearSpriteCache();

                        lastSpriteCacheReset = DateTime.Now;
                    }

                    return Response.Handled;
                }
                
                if (signal is UpdateSource)
                {
                    try
                    {
                        if (radarSurfaceEnumerator.MoveNext())
                        {
                            var surface = radarSurfaceEnumerator.Current;

                            surface.ContentType = ContentType.SCRIPT;
                            surface.ScriptBackgroundColor = Color.Black;
                            surface.Script = "";

                            renderer.Draw(surface);
                        }
                        else
                        {
                            return Response.Transition(UpdateAlarm);
                        }
                    }
                    catch (Exception error)
                    {
                        HandleError(error);
                    }

                    return Response.Handled;
                }
                
                return Response.Parent(Active);
            }

            protected Response UpdateAlarm(ISignal signal)
            {
                if (signal is UpdateSource)
                {
                    try
                    {
                        enemyDetected.Current = Settings.Global.WarningEnabled && enemyTargets.Count > 0;
                        enemyClose.Current = Settings.Global.AlarmEnabled && enemyTargets.Any(id => targets[id].Distance < Settings.Global.AlarmThreshold);

                        if (warningGroup != null) UpdateGroup(warningGroup, enemyDetected);
                        if (alarmGroup != null) UpdateGroup(alarmGroup, enemyClose);

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

            private void GetSurfacesForProvider(IMyTextSurfaceProvider provider)
            {
                var block = provider as IMyTerminalBlock;
                var ini = new MyIni();

                if (ini.TryParse(block.CustomData))
                {
                    for (int i = 0; i < provider.SurfaceCount; i++)
                    {
                        string value = ini.Get(NAME, $"Surface{i}").ToString("");

                        switch (value)
                        {
                            case "Main":
                                radarSurfaces.Add(provider.GetSurface(i));

                                break;
                            case "Enemy":
                                enemySurfaces.Add(provider.GetSurface(i));

                                break;
                            case "Friend":
                                friendSurfaces.Add(provider.GetSurface(i));

                                break;
                            default:
                                value = "";

                                break;
                        }

                        ini.Set(NAME, $"Surface{i}", value);
                    }
                }
                
                ini.SetSectionComment(NAME, "Options: Main, Enemy, Friend");

                block.CustomData = ini.ToString();
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

            private Color SuitColor(Color original)
            {
                return Color.Lerp(original, Color.Yellow, 0.5f);
            }

            private int CompareTargetEntityIDs(long a, long b)
            {
                int result;
                var targetA = targets[a];
                var targetB = targets[b];

                result = targetA.CurrentTarget.CompareTo(targetB.CurrentTarget);
                if (result == 0)
                {
                    result = targetA.TargetingMe.CompareTo(targetB.TargetingMe);

                    if (result == 0)
                    {
                        result = targetA.Distance.CompareTo(targetB.Distance);
                    }
                }

                return result;
            }

            private void UpdateGroup(IMyBlockGroup group, Debounce value)
            {
                var blocks = new List<IMyTerminalBlock>();

                group.GetBlocksOfType<IMyLightingBlock>(blocks);

                foreach (var block in blocks)
                {
                    (block as IMyLightingBlock).Enabled = value.Current;
                }

                group.GetBlocksOfType<IMyTextPanel>(
                    blocks,
                    block => {
                        return !surfaceProviders.Any(provider => (provider as IMyTerminalBlock) == block);
                    }
                );

                foreach (var block in blocks)
                {
                    (block as IMyTextPanel).Enabled = !value.Current;
                }

                if (value.Rising)
                {
                    blocks.Clear();

                    group.GetBlocksOfType<IMyTimerBlock>(blocks);

                    foreach (var block in blocks)
                    {
                        (block as IMyTimerBlock).Trigger();
                    }

                    blocks.Clear();

                    group.GetBlocksOfType<IMySoundBlock>(blocks);

                    foreach (var block in blocks)
                    {
                        (block as IMySoundBlock).Play();
                    }
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

                text.AppendLine($"Last Sprite Cache Reset: {(DateTime.Now - lastSpriteCacheReset).TotalSeconds:f2}");

                text.AppendLine();

                text.AppendLine("Log:");

                text.AppendLine(Logger.AsString);

                program.Echo(text.ToString());
            }
        }
    }
}
