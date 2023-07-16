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
        public class Settings
        {
            public static readonly Settings Global = new Settings();

            public string ControlTag { get; private set; } = "[Radar:Control]";
            public string LCDTag { get; private set; } = "[Radar:LCD]";
            public string BroadcastTag { get; private set; } = "BroadcastTag";
            public string WarningGroup { get; private set; } = "Radar Warning";
            public string AlarmGroup { get; private set; } = "Radar Alarm";
            public string Font { get; private set; } = "Monospace";

            public bool DrawQuadrants { get; private set; } = false;
            public bool DrawObstructions { get; private set; } = false;
            public bool OverrideMaxRange { get; private set; } = false;
            public bool WarningEnabled { get; private set; } = false;
            public bool AlarmEnabled { get; private set; } = false;
            public bool Debug { get; private set; } = false;

            public float ProjectionAngle { get; private set; } = 50;
            public float MaxRange { get; private set; } = 30000;
            public float TitleScale { get; private set; } = 1.1f;
            public float TextScale { get; private set; } = 0.9f;
            public float AlarmThreshold { get; private set; } = 1000;

            public Color BackgroundColor { get; private set; } = new Color(0, 0, 0, 255);
            public Color TitlebarColor { get; private set; } = new Color(50, 50, 50, 5);
            public Color TextColor { get; private set; } = new Color(252, 147, 3, 255);
            public Color LineColor { get; private set; } = new Color(0, 255, 0, 25);
            public Color PlaneColor { get; private set; } = new Color(50, 50, 75, 10);
            public Color EnemyIconColor { get; private set; } = new Color(150, 0, 0, 255);
            public Color EnemyElevationColor { get; private set; } = new Color(75, 0, 0, 100);
            public Color NeutralIconColor { get; private set; } = new Color(150, 150, 150, 255);
            public Color NeutralElevationColor { get; private set; } = new Color(75, 75, 75, 100);
            public Color FriendlyIconColor { get; private set; } = new Color(0, 150, 0, 255);
            public Color FriendlyElevationColor { get; private set; } = new Color(0, 75, 0, 100);
            public Color ObstructionIconColor { get; private set; } = new Color(50, 50, 150, 127);
            public Color ObstructionElevationColor { get; private set; } = new Color(15, 15, 150, 63);
            public Color EnemyTargetingMeColor { get; private set; } = Color.Orange;
            public Color EnemyCountColor { get; private set; } = Color.DarkRed;
            public Color FriendlyCountColor { get; private set; } = Color.Green;

            private Settings() { }

            public string Syncronize(string data)
            {
                MyIni ini = new MyIni();

                if (ini.TryParse(data))
                {
                    ControlTag = ini.Get(NAME, "ControlTag").ToString(ControlTag);
                    LCDTag = ini.Get(NAME, "LCDTag").ToString(LCDTag);
                    BroadcastTag = ini.Get(NAME, "BroadcastTag").ToString(BroadcastTag);
                    WarningGroup = ini.Get(NAME, "WarningGroup").ToString(WarningGroup);
                    AlarmGroup = ini.Get(NAME, "AlarmGroup").ToString(AlarmGroup);
                    Font = ini.Get(NAME, "Font").ToString(Font);

                    DrawQuadrants = ini.Get(NAME, "DrawQuadrants").ToBoolean(DrawQuadrants);
                    DrawObstructions = ini.Get(NAME, "DrawObstructions").ToBoolean(DrawObstructions);
                    OverrideMaxRange = ini.Get(NAME, "OverrideMaxRange").ToBoolean(OverrideMaxRange);
                    WarningEnabled = ini.Get(NAME, "WarningEnabled").ToBoolean(WarningEnabled);
                    AlarmEnabled = ini.Get(NAME, "AlarmEnabled").ToBoolean(AlarmEnabled);
                    Debug = ini.Get(NAME, "Debug").ToBoolean(Debug);

                    ProjectionAngle = (float) ini.Get(NAME, "ProjectionAngle").ToDouble(ProjectionAngle);
                    MaxRange = (float) ini.Get(NAME, "MaxRange").ToDouble(MaxRange);
                    TitleScale = (float) ini.Get(NAME, "TitleScale").ToDouble(TitleScale);
                    TextScale = (float) ini.Get(NAME, "TextScale").ToDouble(TextScale);
                    AlarmThreshold = (float) ini.Get(NAME, "AlarmThreshold").ToDouble(AlarmThreshold);

                    BackgroundColor = ini.Get(NAME, "BackgroundColor").ToColor(BackgroundColor);
                    TitlebarColor = ini.Get(NAME, "TitlebarColor").ToColor(TitlebarColor);
                    TextColor = ini.Get(NAME, "TextColor").ToColor(TextColor);
                    LineColor = ini.Get(NAME, "LineColor").ToColor(LineColor);
                    PlaneColor = ini.Get(NAME, "PlaneColor").ToColor(PlaneColor);
                    EnemyIconColor = ini.Get(NAME, "EnemyIconColor").ToColor(EnemyIconColor);
                    EnemyElevationColor = ini.Get(NAME, "EnemyElevationColor").ToColor(EnemyElevationColor);
                    NeutralIconColor = ini.Get(NAME, "NeutralIconColor").ToColor(NeutralIconColor);
                    NeutralElevationColor = ini.Get(NAME, "NeutralElevationColor").ToColor(NeutralElevationColor);
                    FriendlyIconColor = ini.Get(NAME, "FriendlyIconColor").ToColor(FriendlyIconColor);
                    FriendlyElevationColor = ini.Get(NAME, "FriendlyElevationColor").ToColor(FriendlyElevationColor);
                    ObstructionIconColor = ini.Get(NAME, "ObstructionIconColor").ToColor(ObstructionIconColor);
                    ObstructionElevationColor = ini.Get(NAME, "ObstructionElevationColor").ToColor(ObstructionElevationColor);
                    EnemyTargetingMeColor = ini.Get(NAME, "EnemyTargetingMeColor").ToColor(EnemyTargetingMeColor);
                    EnemyCountColor = ini.Get(NAME, "EnemyCountColor").ToColor(EnemyCountColor);
                    FriendlyCountColor = ini.Get(NAME, "FriendlyCountColor").ToColor(FriendlyCountColor);
                }

                ini.Set(NAME, "ControlTag", ControlTag);
                ini.Set(NAME, "LCDTag", LCDTag);
                ini.Set(NAME, "BroadcastTag", BroadcastTag);
                ini.SetComment(NAME, "BroadcastTag", "It's highly recommended that you change this");
                ini.Set(NAME, "WarningGroup", WarningGroup);
                ini.Set(NAME, "AlarmGroup", AlarmGroup);
                ini.Set(NAME, "Font", Font);

                ini.Set(NAME, "DrawQuadrants", DrawQuadrants);
                ini.Set(NAME, "DrawObstructions", DrawObstructions);
                ini.Set(NAME, "OverrideMaxRange", OverrideMaxRange);
                ini.Set(NAME, "WarningEnabled", WarningEnabled);
                ini.Set(NAME, "AlarmEnabled", AlarmEnabled);
                ini.Set(NAME, "Debug", Debug);

                ini.Set(NAME, "ProjectionAngle", ProjectionAngle);
                ini.Set(NAME, "MaxRange", MaxRange);
                ini.Set(NAME, "TitleScale", TitleScale);
                ini.Set(NAME, "TextScale", TextScale);
                ini.Set(NAME, "AlarmThreshold", AlarmThreshold);

                ini.Set(NAME, "BackgroundColor", BackgroundColor);
                ini.Set(NAME, "TitlebarColor", TitlebarColor);
                ini.Set(NAME, "TextColor", TextColor);
                ini.Set(NAME, "LineColor", LineColor);
                ini.Set(NAME, "PlaneColor", PlaneColor);
                ini.Set(NAME, "EnemyIconColor", EnemyIconColor);
                ini.Set(NAME, "EnemyElevationColor", EnemyElevationColor);
                ini.Set(NAME, "NeutralIconColor", NeutralIconColor);
                ini.Set(NAME, "NeutralElevationColor", NeutralElevationColor);
                ini.Set(NAME, "FriendlyIconColor", FriendlyIconColor);
                ini.Set(NAME, "FriendlyElevationColor", FriendlyElevationColor);
                ini.Set(NAME, "ObstructionIconColor", ObstructionIconColor);
                ini.Set(NAME, "ObstructionElevationColor", ObstructionElevationColor);
                ini.Set(NAME, "EnemyTargetingMeColor", EnemyTargetingMeColor);
                ini.Set(NAME, "EnemyCountColor", EnemyCountColor);
                ini.Set(NAME, "FriendlyCountColor", FriendlyCountColor);

                return ini.ToString() + ini.EndContent;
            }
        }
    }
}
