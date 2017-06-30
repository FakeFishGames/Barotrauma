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
                dictionary = new Dictionary<Color, ConsoleColor>();
                dictionary.Add(Color.White, ConsoleColor.White);
                dictionary.Add(Color.Gray, ConsoleColor.Gray);
                dictionary.Add(Color.LightGray, ConsoleColor.Gray);
                dictionary.Add(Color.DarkGray, ConsoleColor.Gray);
                dictionary.Add(Color.Red, ConsoleColor.Red);
                dictionary.Add(Color.DarkRed, ConsoleColor.DarkRed);
                dictionary.Add(Color.Yellow, ConsoleColor.Yellow);
                dictionary.Add(Color.Orange, ConsoleColor.Yellow);
                dictionary.Add(Color.Green, ConsoleColor.Green);
                dictionary.Add(Color.Lime, ConsoleColor.Green);
                dictionary.Add(Color.Blue, ConsoleColor.Blue);
                dictionary.Add(Color.Cyan, ConsoleColor.Cyan);
                dictionary.Add(Color.DarkBlue, ConsoleColor.DarkBlue);
                dictionary.Add(Color.Pink, ConsoleColor.Magenta);
                dictionary.Add(Color.Magenta, ConsoleColor.Magenta);
            }

            ConsoleColor val = ConsoleColor.White;
            if (dictionary.TryGetValue(xnaCol, out val))
            {
                return val;
            }

            return ConsoleColor.White;
        }
    }
}
