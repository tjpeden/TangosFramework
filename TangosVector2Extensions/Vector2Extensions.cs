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
    internal static class Vector2Extensions
    {
        public static Vector2 Round(this Vector2 vector)
        {
            return new Vector2(
                (float)Math.Round(vector.X),
                (float)Math.Round(vector.Y)
            );
        }

        public static Vector2 Round(this Vector2? vector)
        {
            if (vector.HasValue)
            {
                return new Vector2(
                    (float)Math.Round(vector.Value.X),
                    (float)Math.Round(vector.Value.Y)
                );
            }

            return new Vector2();
        }
    }
}
