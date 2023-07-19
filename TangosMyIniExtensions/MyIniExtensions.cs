using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
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
    internal static class MyIniExtensions
    {
        public static Color ToColor(this MyIniValue iniValue)
        {
            Color color;

            TryParseColor(iniValue.ToString(), out color);

            return color;
        }

        public static Color ToColor(this MyIniValue iniValue, Color defaultValue)
        {
            Color color;

            if (!TryParseColor(iniValue.ToString(), out color))
            {
                color = defaultValue;
            }

            return color;
        }

        private static bool TryParseColor(string text, out Color color)
        {
            color = Color.Black;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var values = text.ToLower().Split(' ');

            if (values.Length != 4)
            {
                return false;
            }

            byte rValue;
            if (!byte.TryParse(values[0].Replace("r:", ""), out rValue))
            {
                return false;
            }

            byte gValue;
            if (!byte.TryParse(values[1].Replace("g:", ""), out gValue))
            {
                return false;
            }

            byte bValue;
            if (!byte.TryParse(values[2].Replace("b:", ""), out bValue))
            {
                return false;
            }

            byte aValue;
            if (!byte.TryParse(values[3].Replace("a:", ""), out aValue))
            {
                return false;
            }

            color = new Color(rValue, gValue, bValue, aValue);

            return true;
        }

        public static string ToString(this Color color)
        {
            return string.Format(
                "R:{0} G:{1} B:{2} A:{3}",
                color.R.ToString(),
                color.G.ToString(),
                color.B.ToString(),
                color.A.ToString()
            );
        }

        public static Vector3D ToVector3D(this MyIniValue iniValue)
        {
            Vector3D vector;

            Vector3D.TryParse(iniValue.ToString(), out vector);

            return vector;
        }

        public static Vector3D ToVector3D(this MyIniValue iniValue, Vector3D defaultValue)
        {
            Vector3D vector;

            if (!Vector3D.TryParse(iniValue.ToString(), out vector))
            {
                vector = defaultValue;
            }

            return vector;
        }

        public static DateTime ToDateTime(this MyIniValue iniValue)
        {
            DateTime dateTime;

            DateTime.TryParse(iniValue.ToString(), out dateTime);

            return dateTime;
        }

        public static DateTime ToDateTime(this MyIniValue iniValue, DateTime defaultValue)
        {
            DateTime dateTime;

            if (!DateTime.TryParse(iniValue.ToString(), out dateTime))
            {
                dateTime = defaultValue;
            }

            return dateTime;
        }

        public static float ToFloat(this MyIniValue iniValue)
        {
            return (float)iniValue.ToDouble();
        }

        public static void Set(this MyIni ini, string section, string key, Color value)
        {
            ini.Set(section, key, value.ToString());
        }

        public static void Set(this MyIni ini, string section, string key, Vector3D value)
        {
            ini.Set(section, key, value.ToString());
        }

        public static void Set(this MyIni ini, string section, string key, DateTime value)
        {
            ini.Set(section, key, value.ToString());
        }
    }
}
