using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Networking;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.ComponentModel;

namespace Barotrauma
{
    class UnimplementedScreen : Screen
    {
        public static readonly UnimplementedScreen Instance = new UnimplementedScreen();

        public override void Select()
        {
            throw new Exception("Tried to select unimplemented screen");
        }
    }
}
