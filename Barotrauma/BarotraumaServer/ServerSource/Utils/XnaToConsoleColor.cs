using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    public static class XnaToConsoleColor
    {
        static Dictionary<Color, ConsoleColor> dictionary;

        public static ConsoleColor Convert(Color xnaCol)
        {
            if (dictionary == null)
            {
                dictionary = new Dictionary<Color, ConsoleColor>
                {
                    { Color.White, ConsoleColor.White },
                    { Color.Gray, ConsoleColor.Gray },
                    { Color.LightGray, ConsoleColor.Gray },
                    { Color.DarkGray, ConsoleColor.Gray },
                    { Color.Red, ConsoleColor.Red },
                    { Color.DarkRed, ConsoleColor.DarkRed },
                    { Color.Yellow, ConsoleColor.Yellow },
                    { Color.Orange, ConsoleColor.Yellow },
                    { Color.Green, ConsoleColor.Green },
                    { Color.Lime, ConsoleColor.Green },
                    { Color.Blue, ConsoleColor.Blue },
                    { Color.Cyan, ConsoleColor.Cyan },
                    { Color.DarkBlue, ConsoleColor.DarkBlue },
                    { Color.Pink, ConsoleColor.Magenta },
                    { Color.Magenta, ConsoleColor.Magenta }
                };
            }

            ConsoleColor val = ConsoleColor.White;
            if (dictionary.TryGetValue(xnaCol, out val))
            {
                return val;
            }
            
            return GetClosestConsoleColor(xnaCol);
        }

        public static ConsoleColor GetClosestConsoleColor(Color color)
        {
            Vector3 hls = ToolBox.RgbToHLS(color.ToVector3());
            if (hls.Z < 0.5)
            {
                // we have a grayish color
                switch ((int)(hls.Y * 3.5))
                {
                    case 0: return ConsoleColor.Black;
                    case 1: return ConsoleColor.DarkGray;
                    case 2: return ConsoleColor.Gray;
                    default: return ConsoleColor.White;
                }
            }
            int hue = (int)Math.Round(hls.X / 60, MidpointRounding.AwayFromZero);
            if (hls.Y < 0.4)
            {
                // dark color
                switch (hue)
                {
                    case 1: return ConsoleColor.DarkYellow;
                    case 2: return ConsoleColor.DarkGreen;
                    case 3: return ConsoleColor.DarkCyan;
                    case 4: return ConsoleColor.DarkBlue;
                    case 5: return ConsoleColor.DarkMagenta;
                    default: return ConsoleColor.DarkRed;
                }
            }
            // bright color
            switch (hue)
            {
                case 1: return ConsoleColor.Yellow;
                case 2: return ConsoleColor.Green;
                case 3: return ConsoleColor.Cyan;
                case 4: return ConsoleColor.Blue;
                case 5: return ConsoleColor.Magenta;
                default: return ConsoleColor.Red;
            }
        }
    }
}
