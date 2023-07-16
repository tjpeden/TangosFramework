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

        public class RadarRenderer
        {
            const float TARGET_ELEVATION_LINE_WIDTH = 2f;
            const float QUADRANT_LINE_WIDTH = 2f;
            const float TITLEBAR_HEIGHT = 40F;

            readonly Vector2 TARGET_ICON_SIZE = new Vector2(10, 10);
            readonly Vector2 SHIP_ICON_SIZE = new Vector2(8, 4);
            readonly Vector2 SHADOW_OFFSET = new Vector2(2, 2);

            readonly TangosRadar machine;

            float? previousProjectionAngle;
            float projectionCos;
            float projectionSin;

            Vector2 quadrantLineDirection;

            bool clearSpriteCache = true;

            public RadarRenderer(TangosRadar machine)
            {
                this.machine = machine;
            }

            public void ClearSpriteCache()
            {
                clearSpriteCache = true;
            }

            public void Draw(IMyTextSurface surface)
            {
                UpdateProjectionAngle();

                Vector2 surfaceSize = surface.TextureSize;
                Vector2 viewportSize = surface.SurfaceSize;

                Vector2 center = surfaceSize * 0.5f;
                Vector2 scale = viewportSize / 512f;

                float minScale = Math.Min(scale.X, scale.Y);
                float sideLength = Math.Min(viewportSize.X, viewportSize.Y - TITLEBAR_HEIGHT * minScale);

                var planeSize = new Vector2(sideLength, sideLength * projectionCos);

                using (var frame = surface.DrawFrame())
                {
                    if (clearSpriteCache)
                    {
                        frame.Add(new MySprite());

                        clearSpriteCache = false;
                    }

                    var sprite = new MySprite(SpriteType.TEXTURE, "SquareSimple")
                    {
                        Position = center,
                        Color = machine.settings.BackgroundColor,
                    };

                    frame.Add(sprite);

                    foreach (var id in machine.targetsBelowPlane)
                    {
                        var target = machine.targets[id];

                        DrawTargetIcon(frame, center, planeSize, target, minScale);
                    }

                    DrawPlane(frame, center, planeSize, viewportSize, minScale);

                    foreach (var id in machine.targetsAbovePlane)
                    {
                        var target = machine.targets[id];

                        DrawTargetIcon(frame, center, planeSize, target, minScale);
                    }

                    DrawText(frame, center, viewportSize, minScale);
                }
            }

            private void UpdateProjectionAngle()
            {
                var projectionAngle = machine.settings.ProjectionAngle;

                if (!previousProjectionAngle.HasValue || previousProjectionAngle != projectionAngle)
                {
                    var projectionAngleRadians = MathHelper.ToRadians(projectionAngle);

                    projectionCos = (float)Math.Cos(projectionAngleRadians);
                    projectionSin = (float)Math.Sin(projectionAngleRadians);

                    quadrantLineDirection = new Vector2(0.25f * MathHelper.Sqrt2, 0.25f * MathHelper.Sqrt2 * projectionCos);
                }

                previousProjectionAngle = projectionAngle;
            }

            private void DrawPlane(MySpriteDrawFrame frame, Vector2 center, Vector2 planeSize, Vector2 viewportSize, float scale)
            {
                var title = machine.targets.ContainsKey(machine.currentTarget) ? machine.targets[machine.currentTarget].Name : "No Target";
                var viewportHalfSize = viewportSize * 0.5f;
                var titlebarHeight = scale * TITLEBAR_HEIGHT;
                
                var sprite = new MySprite(SpriteType.TEXTURE, "SquareSimple")
                {
                    Position = center + new Vector2(0, -viewportHalfSize.Y + titlebarHeight * 0.5f),
                    Size = new Vector2(viewportSize.X, titlebarHeight),
                    Color = machine.settings.TitlebarColor,
                };

                frame.Add(sprite);

                sprite = new MySprite(SpriteType.TEXT, title)
                {
                    Position = center + new Vector2(0, -viewportHalfSize.Y + 4.25f * scale),
                    RotationOrScale = scale * machine.settings.TitleScale,
                    Color = machine.settings.TextColor,
                    Alignment = TextAlignment.CENTER,
                    FontId = machine.settings.Font,
                };

                frame.Add(sprite);

                sprite = new MySprite(SpriteType.TEXTURE, "Circle")
                {
                    Position = center,
                    Size = planeSize * 0.5f,
                    Color = machine.settings.PlaneColor,
                };

                frame.Add(sprite);

                sprite = new MySprite(SpriteType.TEXTURE, "Circle")
                {
                    Position = center,
                    Size = planeSize,
                    Color = machine.settings.PlaneColor,
                };

                frame.Add(sprite);

                var iconSize = SHIP_ICON_SIZE * scale;

                sprite = new MySprite(SpriteType.TEXTURE, "Triangle")
                {
                    Position = center + new Vector2(0, -0.2f * iconSize.Y),
                    Size = iconSize,
                    Color = machine.settings.LineColor,
                };

                frame.Add(sprite);

                if (machine.settings.DrawQuadrants)
                {
                    var quadrantLine = planeSize.X * quadrantLineDirection;
                    var lineWidth = QUADRANT_LINE_WIDTH * scale;
                    var quadrantLineColor = machine.settings.LineColor * 0.5f;

                    DrawQuadrantLines(
                        frame,
                        center,
                        0.2f * quadrantLine,
                        quadrantLine,
                        lineWidth,
                        quadrantLineColor
                    );
                }
            }

            private void DrawTargetIcon(MySpriteDrawFrame frame, Vector2 center, Vector2 planeSize, TargetData target, float scale)
            {
                Vector3 targetPosition = target.RelativePosition * new Vector3(1, projectionCos, projectionSin) * planeSize.X * 0.5f;

                Vector2 targetPlanePosition = new Vector2(targetPosition.X, targetPosition.Y);
                Vector2 iconPosition = targetPlanePosition - targetPosition.Z * Vector2.UnitY;
                Vector2 iconSize = TARGET_ICON_SIZE * scale;

                targetPlanePosition = targetPlanePosition.Round();
                iconPosition = iconPosition.Round();

                float elevationLineWidth = Math.Max(1f, TARGET_ELEVATION_LINE_WIDTH * scale);

                var elevationSprite = new MySprite(SpriteType.TEXTURE, "SquareSimple")
                {
                    Position = center + (iconPosition + targetPlanePosition) * 0.5f,
                    Size = new Vector2(elevationLineWidth, targetPosition.Z),
                    Color = target.ElevationColor,
                };

                elevationSprite.Position = elevationSprite.Position.Round();
                elevationSprite.Size = elevationSprite.Size.Round();

                var iconSprite = new MySprite(SpriteType.TEXTURE, target.Icon)
                {
                    Position = center + iconPosition,
                    Size = iconSize,
                    Color = target.IconColor,
                };

                iconSprite.Position = iconSprite.Position.Round();
                iconSprite.Size = iconSprite.Size.Round();

                var shadowSprite = iconSprite;

                shadowSprite.Color = Color.Black;
                shadowSprite.Size += Vector2.One * 2f * (float)Math.Max(1f, Math.Round(scale * 4f));

                iconSize.Y *= projectionCos;

                var showProjectionElevation = Math.Abs(iconPosition.Y - targetPlanePosition.Y) > iconSize.Y;

                Action drawProjectionElevation = () =>
                {
                    if (showProjectionElevation)
                    {
                        var projectionSprite = new MySprite(SpriteType.TEXTURE, "Circle")
                        {
                            Position = center + targetPlanePosition,
                            Size = iconSize,
                            Color = target.ElevationColor,
                        };

                        projectionSprite.Position = projectionSprite.Position.Round();
                        projectionSprite.Size = projectionSprite.Size.Round();

                        frame.Add(projectionSprite);
                    }
                };
                Action drawCurrentTarget = () =>
                {
                    if (target.CurrentTarget)
                    {
                        var highlightSprite = shadowSprite;

                        highlightSprite.Size += new Vector2(3, 3);
                        highlightSprite.Color = iconSprite.Color;

                        frame.Add(highlightSprite);
                    }
                };
                Action drawThreatScore = () =>
                {
                    if (target.Relation == Relation.Hostile)
                    {
                        var threatSprite = new MySprite(SpriteType.TEXT, target.ThreatScore.ToString())
                        {
                            Position = center + iconPosition + new Vector2(8, -8),
                            RotationOrScale = 0.5f * scale,
                            Color = machine.settings.TextColor,
                            Alignment = TextAlignment.CENTER,
                            FontId = machine.settings.Font,
                        };

                        threatSprite.Position = threatSprite.Position.Round();
                        threatSprite.Size = threatSprite.Size.Round();

                        frame.Add(threatSprite);
                    }
                };

                if (targetPosition.Z >= 0)
                {
                    drawProjectionElevation();
                    
                    frame.Add(elevationSprite);

                    drawCurrentTarget();

                    frame.Add(shadowSprite);
                    frame.Add(iconSprite);

                    drawThreatScore();
                }
                else
                {
                    iconSprite.RotationOrScale = MathHelper.Pi;
                    shadowSprite.RotationOrScale = MathHelper.Pi;

                    frame.Add(elevationSprite);

                    drawCurrentTarget();

                    frame.Add(shadowSprite);
                    frame.Add(iconSprite);

                    drawProjectionElevation();
                    drawThreatScore();
                }
            }

            private void DrawText(MySpriteDrawFrame frame, Vector2 center, Vector2 viewportSize, float scale)
            {
                var textSize = scale * machine.settings.TextScale;
                var viewportHalfSize = viewportSize * 0.5f;
                var shadowOffset = scale * SHADOW_OFFSET;

                var rangeSprite = new MySprite(SpriteType.TEXT, $"Range: {machine.MaxRange:N0}")
                {
                    Position = center + new Vector2(0, -viewportHalfSize.Y + (60 * scale)),
                    RotationOrScale = textSize,
                    Color = machine.settings.TextColor,
                    Alignment = TextAlignment.CENTER,
                    FontId = machine.settings.Font,
                };

                frame.Add(rangeSprite);

                var enemySprite = new MySprite(SpriteType.TEXT, $"Enemies: {machine.EnemyTargetCount}")
                {
                    Position = center + new Vector2(-(viewportHalfSize.X * 0.5f) + 10, viewportHalfSize.Y - (70 * scale)) + shadowOffset,
                    RotationOrScale = textSize,
                    Color = Color.Black,
                    Alignment = TextAlignment.CENTER,
                    FontId = machine.settings.Font,
                };

                frame.Add(enemySprite);

                enemySprite.Color = machine.settings.EnemyCountColor;
                enemySprite.Position -= shadowOffset;

                frame.Add(enemySprite);

                var friendlySprite = new MySprite(SpriteType.TEXT, $"Friendlies: {machine.FriendlyTargetCount}")
                {
                    Position = center + new Vector2(viewportHalfSize.X * 0.5f - 10, viewportHalfSize.Y - (70 * scale)) + shadowOffset,
                    RotationOrScale = textSize,
                    Color = Color.Black,
                    Alignment = TextAlignment.CENTER,
                    FontId = machine.settings.Font,
                };

                frame.Add(friendlySprite);

                friendlySprite.Color = machine.settings.FriendlyCountColor;
                friendlySprite.Position -= shadowOffset;

                frame.Add(friendlySprite);
            }

            private void DrawQuadrantLines(MySpriteDrawFrame frame, Vector2 center, Vector2 point1, Vector2 point2, float width, Color color)
            {
                DrawLine(frame, center + point1, center + point2, width, color);
                DrawLine(frame, center - point1, center - point2, width, color);
                
                point1.X *= -1;
                point2.X *= -1;

                DrawLine(frame, center + point1, center + point2, width, color);
                DrawLine(frame, center - point1, center - point2, width, color);
            }

            private void DrawLine(MySpriteDrawFrame frame, Vector2 point1, Vector2 point2, float width, Color color)
            {
                Vector2 position = 0.5f * (point1 + point2);
                Vector2 difference = point1 - point2;

                float length = difference.Length();

                if (length > 0) difference /= length;

                Vector2 size = new Vector2(length, width);

                float angle = (float)Math.Acos(Vector2.Dot(difference, Vector2.UnitX));

                angle *= Math.Sign(Vector2.Dot(difference, Vector2.UnitY));

                var sprite = new MySprite(SpriteType.TEXTURE, "SquareSimple")
                {
                    Position = position,
                    Size = size,
                    Color = color,
                    RotationOrScale = angle,
                };

                frame.Add(sprite);
            }
        }
    }
}
