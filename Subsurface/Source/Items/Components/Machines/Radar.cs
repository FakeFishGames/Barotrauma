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
    class Radar : Powered
    {
        private float range;

        private float pingState;

        private Sprite pingCircle, screenOverlay;

        [HasDefaultValue(0.0f, false)]
        public float Range
        {
            get { return ConvertUnits.ToDisplayUnits(range); }
            set { range = ConvertUnits.ToSimUnits(value); }
        }

        public Radar(Item item, XElement element)
            : base(item, element)
        {
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLower())
                {
                    case "pingcircle":
                        pingCircle = new Sprite(subElement);
                        break;
                    case "screenoverlay":
                        screenOverlay = new Sprite(subElement);
                        break;
                }
            }

            //renderTarget = new RenderTarget2D(Game1.CurrGraphicsDevice, GuiFrame.Rect.Width, GuiFrame.Rect.Height);
        }

        public override void Update(float deltaTime, Camera cam)
        {
            base.Update(deltaTime, cam);

            if (voltage>=minVoltage)
            {
                pingState = (pingState + deltaTime * 0.5f);
                if (pingState>1.0f)
                {
                    item.Use(deltaTime, null);
                    pingState = 0.0f;
                }
            }
            else
            {
                pingState = 0.0f;
            }            
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            return (pingState > 1.0f);
        }

        public override void DrawHUD(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch, Character character)
        {
            int width = GuiFrame.Rect.Width, height = GuiFrame.Rect.Height;
            int x = GuiFrame.Rect.X;
            int y = GuiFrame.Rect.Y;
            
            GuiFrame.Draw(spriteBatch);

            if (voltage < minVoltage) return;

            if (GUI.DrawButton(spriteBatch, new Rectangle(x + 20, y + 20, 200, 30), "Activate Radar"))
            {
                IsActive = !IsActive;
                item.NewComponentEvent(this, true);
            }

            int radius = GuiFrame.Rect.Height / 2 - 10;
            DrawRadar(spriteBatch, new Rectangle((int)GuiFrame.Center.X - radius, (int)GuiFrame.Center.Y - radius, radius * 2, radius * 2));

            //voltage = 0.0f;
        }

        private void DrawRadar(SpriteBatch spriteBatch, Rectangle rect)
        {

            Vector2 center = new Vector2(rect.Center.X, rect.Center.Y);
            //lineEnd += new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * Math.Min(width, height) / 2.0f;
            //GUI.DrawLine(spriteBatch, GuiFrame.Center, lineEnd, Color.Green);

            if (!IsActive) return;

            if (pingCircle!=null)
            {
                pingCircle.Draw(spriteBatch, center, Color.White * (1.0f-pingState), 0.0f, (rect.Width/pingCircle.size.X)*pingState);
            }

            float radius = rect.Width / 2.0f;
            
            float simScale = 1.5f;
            float displayScale = 0.015f;

            if (Level.Loaded != null)
            {
                List<Vector2[]> edges = Level.Loaded.GetCellEdges(-Level.Loaded.Position, 7);
                Vector2 offset = Vector2.Zero;

                for (int i = 0; i < edges.Count; i++)
                {
                    if ((edges[i][0] * displayScale).Length() > radius) continue;
                    if ((edges[i][1] * displayScale).Length() > radius) continue;

                    GUI.DrawLine(spriteBatch,
                        center + (edges[i][0] - offset) * displayScale,
                        center + (edges[i][1] - offset) * displayScale, Color.White);
                }

                for (int i = 0; i < Submarine.Loaded.HullVertices.Count; i++)
                {
                    Vector2 start = Submarine.Loaded.HullVertices[i] * simScale;
                    start.Y = -start.Y;
                    Vector2 end = Submarine.Loaded.HullVertices[(i + 1) % Submarine.Loaded.HullVertices.Count] * simScale;
                    end.Y = -end.Y;

                    GUI.DrawLine(spriteBatch, center + start, center + end, Color.White);
                }
            }

            foreach (Character c in Character.CharacterList)
            {
                if (c.AnimController.CurrentHull != null) continue;

                Vector2 pos = c.Position * displayScale;
                if (c.SimPosition == Vector2.Zero || pos.Length() > radius) continue;
                
                int width = (int)MathHelper.Clamp(c.Mass / 20, 1, 10);

                pos.Y = -pos.Y;
                pos += center;

                GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X - width / 2, (int)pos.Y - width / 2, width, width), Color.White, true);                
            }

            if (screenOverlay!=null)
            {
                screenOverlay.Draw(spriteBatch, center, 0.0f, rect.Width/screenOverlay.size.X);
            }

            if (GameMain.GameSession == null) return;


            DrawMarker(spriteBatch,
                (GameMain.GameSession.Map == null) ? "Start" : GameMain.GameSession.Map.CurrentLocation.Name,
                (Level.Loaded.StartPosition + Level.Loaded.Position), displayScale, center, (rect.Width * 0.55f));

            DrawMarker(spriteBatch,
                (GameMain.GameSession.Map == null) ? "End" : GameMain.GameSession.Map.SelectedLocation.Name,
                (Level.Loaded.EndPosition + Level.Loaded.Position), displayScale, center, (rect.Width * 0.55f));

            if (GameMain.GameSession.Quest != null)
            {
                var quest = GameMain.GameSession.Quest;

                if (!string.IsNullOrWhiteSpace(quest.RadarLabel))
                {
                    DrawMarker(spriteBatch,
                        quest.RadarLabel,
                        quest.RadarPosition, displayScale, center, (rect.Width * 0.55f));
                }
            }

            if (!GameMain.DebugDraw) return;

            var steering = item.GetComponent<Steering>();
            if (steering == null || steering.SteeringPath == null) return;

            Vector2 prevPos = Vector2.Zero;

            foreach (WayPoint wp in steering.SteeringPath.Nodes)
            {
                Vector2 pos = (wp.Position - Submarine.Loaded.Position) * displayScale;
                if (pos.Length() > radius) continue;

                pos.Y = -pos.Y;
                pos += center;

                GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X -3 / 2, (int)pos.Y - 3, 6, 6), (steering.SteeringPath.CurrentNode==wp) ? Color.LightGreen : Color.Green, false);

                if (prevPos!=Vector2.Zero)
                {
                    GUI.DrawLine(spriteBatch, pos, prevPos, Color.Green);
                }

                prevPos = pos;
            }
        }

        private void DrawMarker(SpriteBatch spriteBatch, string label, Vector2 position, float scale, Vector2 center, float radius)
        {
            //position += Level.Loaded.Position;

            float dist = position.Length();

            position *= scale;
            position.Y = -position.Y;
            
            Vector2 markerPos = (dist*scale>radius) ? Vector2.Normalize(position) * radius : position;
            markerPos += center;

            GUI.DrawRectangle(spriteBatch, new Rectangle((int)markerPos.X, (int)markerPos.Y, 5, 5), Color.LightGreen);

            spriteBatch.DrawString(GUI.SmallFont, label, new Vector2(markerPos.X + 10, markerPos.Y), Color.LightGreen);
            spriteBatch.DrawString(GUI.SmallFont, (int)(dist / 80.0f) + " m", new Vector2(markerPos.X + 10, markerPos.Y + 15), Color.LightGreen);                
        }

        public override void FillNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetOutgoingMessage message)
        {
            message.Write(IsActive);
        }

        public override void ReadNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetIncomingMessage message)
        {
            try
            {
                IsActive = message.ReadBoolean();
            }
            catch
            {
                return;
            }
        }

    }
}
