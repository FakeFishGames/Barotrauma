using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Barotrauma.Networking;
using Barotrauma.Items.Components;
using System.Text;
using FarseerPhysics;

namespace Barotrauma
{
    static partial class DebugConsole
    {
        private static string InputText;

        private static bool ExecProjSpecific(string[] commands)
        {
            return false; //command not found
        }
    }
}
