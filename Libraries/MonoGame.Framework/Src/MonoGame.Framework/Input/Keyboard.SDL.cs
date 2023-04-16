// MonoGame - Copyright (C) The MonoGame Team
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using System;
using System.Collections.Generic;

namespace Microsoft.Xna.Framework.Input
{
    public static partial class Keyboard
    {
        static List<Keys> _keys;

        private static KeyboardState PlatformGetState()
        {
            var modifiers = Sdl.Keyboard.GetModState();
            return new KeyboardState(_keys,
                                     (modifiers & Sdl.Keyboard.Keymod.CapsLock) == Sdl.Keyboard.Keymod.CapsLock,
                                     (modifiers & Sdl.Keyboard.Keymod.NumLock) == Sdl.Keyboard.Keymod.NumLock);
        }

        internal static void SetKeys(List<Keys> keys)
        {
            _keys = keys;
        }

        public static Keys QwertyToCurrentLayout(Keys qwertyKey)
        {
            #warning TODO: test Dvorak & other layouts that I'm unaware of that replace letter keys with non-letter keys or vice versa
            int scancode = qwertyKey switch
            {
                Keys.A => 4,
                Keys.B => 5,
                Keys.C => 6,
                Keys.D => 7,
                Keys.E => 8,
                Keys.F => 9,
                Keys.G => 10,
                Keys.H => 11,
                Keys.I => 12,
                Keys.J => 13,
                Keys.K => 14,
                Keys.L => 15,
                Keys.M => 16,
                Keys.N => 17,
                Keys.O => 18,
                Keys.P => 19,
                Keys.Q => 20,
                Keys.R => 21,
                Keys.S => 22,
                Keys.T => 23,
                Keys.U => 24,
                Keys.V => 25,
                Keys.W => 26,
                Keys.X => 27,
                Keys.Y => 28,
                Keys.Z => 29,
                _ => -1
            };
            if (scancode < 0) { return qwertyKey; }
            return KeyboardUtil.ToXna(Sdl.Keyboard.GetKeyFromScancode(scancode));
        }
    }
}
