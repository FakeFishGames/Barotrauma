using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Barotrauma
{
    partial class EventManager
    {
        public void DebugDraw(SpriteBatch spriteBatch)
        {
            foreach (ScriptedEvent ev in events)
            {
                Vector2 drawPos = ev.DebugDrawPos;
                drawPos.Y = -drawPos.Y;

                GUI.SubmarineIcon.Draw(spriteBatch, drawPos, Color.White * 0.5f, GUI.SubmarineIcon.size / 2, 0.0f, 40.0f);
                GUI.DrawString(spriteBatch, drawPos, ev.DebugDrawText, Color.White, Color.Black, 0, GUI.LargeFont);
            }
        }
    }
}
