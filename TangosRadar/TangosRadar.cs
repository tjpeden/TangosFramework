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

            private readonly Program program;
            private readonly WCPBAPI wcapi = new WCPBAPI();

            private readonly RadarRenderer renderer;

            private readonly Debounce enemyDetected = new Debounce();
            private readonly Debounce enemyClose = new Debounce();

            private readonly IMyShipController controller;

            private readonly IMyBlockGroup warningGroup;
            private readonly IMyBlockGroup alarmGroup;

            private readonly List<IMyTextSurfaceProvider> surfaceProviders = new List<IMyTextSurfaceProvider>();

            private readonly List<IMyTextSurface> radarSurfaces = new List<IMyTextSurface>();
            private readonly List<IMyTextSurface> enemySurfaces = new List<IMyTextSurface>();
            private readonly List<IMyTextSurface> friendSurfaces = new List<IMyTextSurface>();

            private readonly List<long> enemyTargets = new List<long>();
            private readonly List<long> friendTargets = new List<long>();

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

            public readonly Dictionary<long, TargetData> targets = new Dictionary<long, TargetData>();

            public readonly List<long> targetsAbovePlane = new List<long>();
            public readonly List<long> targetsBelowPlane = new List<long>();

            public long currentTarget;

            public TangosRadar(Program program) : base()
            {
                this.program = program;

                renderer = new RadarRenderer(this);

                lastSpriteCacheReset = DateTime.Now;

                RegisterChildren(
                    Active,
                    new List<Func<ISignal, Response>>
                    {
                        GetTargets,
                        ProcessTargetList,
                        UpdateEnemyLCD,
                        UpdateFriendLCD,
                        UpdateRadar,
                        UpdateAlarm,
                    }
                );

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
                        throw new Exception("No tagged controller found.");
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
                    return Response.Handled;
                }

                return Response.Unhandled;
            }

            protected Response Active(ISignal signal)
            {
                if (signal is IGCSource)
                {
                    while (listener.HasPendingMessage)
                    {
                        MyIGCMessage message = listener.AcceptMessage();

                        if (message.Data is string)
                        {
                            TargetData.Parse(message.Data as string, targets);
                        }
                    }

                    return Response.Handled;
                }

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
                        if (item.Value.Age.TotalSeconds >= 30)
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

                        focus = wcapi.GetAiFocus(program.Me.CubeGrid.EntityId);

                        if (focus.HasValue && focus.Value.EntityId != 0)
                        {
                            currentTarget = focus.Value.EntityId;
                        }

                        targets.Remove(program.Me.CubeGrid.EntityId);

                        return TransitionTo(ProcessTargetList);
                    }
                    catch (Exception error)
                    {
                        HandleError(error);
                    }

                    return Response.Handled;
                }
                
                return Response.Unhandled;
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
                                    float divisor = target.Distance < 1000 ? 500 : 5000;
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

                        return TransitionTo(UpdateEnemyLCD);
                    }
                    catch (Exception error)
                    {
                        HandleError(error);
                    }

                    return Response.Handled;
                }
                
                return Response.Unhandled;
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

                        return TransitionTo(UpdateFriendLCD);
                    }
                    catch (Exception error)
                    {
                        HandleError(error);
                    }

                    return Response.Handled;
                }
                
                return Response.Unhandled;
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

                        return TransitionTo(UpdateRadar);
                    }
                    catch (Exception error)
                    {
                        HandleError(error);
                    }

                    return Response.Handled;
                }
                
                return Response.Unhandled;
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
                            return TransitionTo(UpdateAlarm);
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
                            case "Main": radarSurfaces.Add(provider.GetSurface(i)); break;
                            case "Enemy": enemySurfaces.Add(provider.GetSurface(i)); break;
                            case "Friend": friendSurfaces.Add(provider.GetSurface(i)); break;
                            default: value = ""; break;
                        }

                        ini.Set(NAME, $"Surface{i}", value);
                    }
                }
                
                ini.SetSectionComment(NAME, "Options: Main, Enemy, Friend");

                block.CustomData = ini.ToString();
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
