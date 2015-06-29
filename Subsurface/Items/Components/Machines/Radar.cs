using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Subsurface.Items.Components
{
    class Radar : ItemComponent
    {
        float range;

        float angle;

        //RenderTarget2D renderTarget;

        [HasDefaultValue(0.0f, false)]
        public float Range
        {
            get { return ConvertUnits.ToDisplayUnits(range); }
            set { range = ConvertUnits.ToSimUnits(value); }
        }

        public Radar(Item item, XElement element)
            : base(item, element)
        {
            //renderTarget = new RenderTarget2D(Game1.CurrGraphicsDevice, GuiFrame.Rect.Width, GuiFrame.Rect.Height);
        }

        public override void Update(float deltaTime, Camera cam)
        {
            base.Update(deltaTime, cam);

            angle = (angle + deltaTime) % MathHelper.TwoPi;
        }

        public override void DrawHUD(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch, Character character)
        {
            int width = GuiFrame.Rect.Width, height = GuiFrame.Rect.Height;
            int x = GuiFrame.Rect.X;
            int y = GuiFrame.Rect.Y;

            GuiFrame.Draw(spriteBatch);

            if (GUI.DrawButton(spriteBatch, new Rectangle(x+20, y+20, 200, 30), "Activate Radar")) isActive = !isActive;

            Vector2 lineEnd = GuiFrame.Center;
            lineEnd += new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle))*Math.Min(width,height)/2.0f;
            GUI.DrawLine(spriteBatch, GuiFrame.Center, lineEnd, Color.Green);

            if (!isActive) return;

            float scale = 0.01f;

            List<Vector2[]> edges = Level.Loaded.GetCellEdges(-Level.Loaded.position, 5);
            Vector2 offset = Vector2.Zero; //Level.Loaded.position;
            //offset.Y = -offset.Y;

            for (int i = 0; i < edges.Count; i++)
            {
                GUI.DrawLine(spriteBatch,
                    GuiFrame.Center + (edges[i][0] - offset) * scale,
                    GuiFrame.Center + (edges[i][1] - offset) * scale, Color.Green);
            }

            scale = ConvertUnits.ToDisplayUnits(scale);
            for (int i = 0; i< Submarine.Loaded.HullVertices.Count; i++)
            {
                Vector2 start =Submarine.Loaded.HullVertices[i] * scale;
                start.Y = -start.Y;
                Vector2 end = Submarine.Loaded.HullVertices[(i+1)%Submarine.Loaded.HullVertices.Count] * scale;
                end.Y = -end.Y;

                GUI.DrawLine(spriteBatch, GuiFrame.Center + start,GuiFrame.Center + end, Color.Green);
            }
        }

        private void UpdateRendertarget()
        {

        }
    }
}
