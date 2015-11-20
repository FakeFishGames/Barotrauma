using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Voronoi2;

namespace Barotrauma.Items.Components
{
    class Radar : Powered
    {
        const float displayScale = 0.015f;

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
            radarBlips = new List<RadarBlip>();

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

            for (int i = radarBlips.Count - 1; i >= 0; i-- )
            {
                radarBlips[i].FadeTimer -= deltaTime*0.5f;
                if (radarBlips[i].FadeTimer <= 0.0f) radarBlips.RemoveAt(i);
            }

            if (voltage >= minVoltage)
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

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            int x = GuiFrame.Rect.X;
            int y = GuiFrame.Rect.Y;
            
            GuiFrame.Draw(spriteBatch);

            if (voltage < minVoltage) return;

            if (GUI.DrawButton(spriteBatch, new Rectangle(x + 0, y + 0, 150, 30), "Activate Sonar"))
            {
                IsActive = !IsActive;
                item.NewComponentEvent(this, true, false);
            }

            int radius = GuiFrame.Rect.Height / 2 - 10;
            DrawRadar(spriteBatch, new Rectangle((int)GuiFrame.Center.X - radius, (int)GuiFrame.Center.Y - radius, radius * 2, radius * 2));

            //voltage = 0.0f;
        }

        private List<RadarBlip> radarBlips;
        private float prevPingRadius;

        private void DrawRadar(SpriteBatch spriteBatch, Rectangle rect)
        {
            Vector2 center = new Vector2(rect.Center.X, rect.Center.Y);

            if (!IsActive) return;

            float pingRadius = (rect.Width / 2) * pingState;
            pingCircle.Draw(spriteBatch, center, Color.White * (1.0f-pingState), 0.0f, (rect.Width/pingCircle.size.X)*pingState);
                

            float radius = rect.Width / 2.0f;
            
            float simScale = 1.5f;

            if (Level.Loaded != null)
            {
                List<VoronoiCell> cells = Level.Loaded.GetCells(-Level.Loaded.Position, 7);

                foreach (VoronoiCell cell in cells)
                {

                    foreach (GraphEdge edge in cell.edges)
                    {
                        //if (!edge.isSolid) continue;
                        float cellDot = Vector2.Dot(cell.Center + Level.Loaded.Position, (edge.point1 + edge.point2) / 2.0f - cell.Center);
                        if (cellDot > 0) continue;

                        float facingDot = Vector2.Dot(Vector2.Normalize(edge.point1 - edge.point2), Vector2.Normalize(cell.Center + Level.Loaded.Position));
                        facingDot = MathHelper.Clamp(facingDot, -1.0f, 1.0f);

                        Vector2 point1 = (edge.point1 + Level.Loaded.Position);
                        Vector2 point2 = (edge.point2 + Level.Loaded.Position);

                        float length = (point1 - point2).Length();
                        for (float x=0; x<length; x+=Rand.Range(600.0f, 800.0f))
                        {
                            Vector2 point = point1 + Vector2.Normalize(point2 - point1) * x;

                            float pointDist = point.Length() * displayScale;

                            if (pointDist > radius) continue;
                            if (pointDist < prevPingRadius || pointDist > pingRadius) continue;


                            float step = 5.0f * (Math.Abs(facingDot)+1.0f);
                            float alpha = Rand.Range(1.5f, 2.0f);
                            for (float z = 0; z<radius-pointDist;z+=step)
                            {
                                
                                var blip = new RadarBlip(
                                    point + Rand.Vector(150.0f) - Level.Loaded.Position + Vector2.Normalize(point) * z / displayScale,
                                    alpha);

                                radarBlips.Add(blip);
                                step += 0.5f;
                                alpha -= (z == 0) ? 0.5f : 0.1f;
                            }
                            
                        }
                    }
                }

                for (int i = 0; i < Submarine.Loaded.HullVertices.Count; i++)
                {
                    Vector2 start = Submarine.Loaded.HullVertices[i] * simScale;
                    start.Y = -start.Y;
                    Vector2 end = Submarine.Loaded.HullVertices[(i + 1) % Submarine.Loaded.HullVertices.Count] * simScale;
                    end.Y = -end.Y;

                    Vector2 diff = end - start;
                    for (float x = 0; x < diff.Length(); x+=4.0f )
                    {
                        GUI.DrawLine(spriteBatch, center + start, center + end, Color.Green);
                    }                        
                }

            }

            foreach (Character c in Character.CharacterList)
            {
                if (c.AnimController.CurrentHull != null) continue;

                foreach (Limb limb in c.AnimController.Limbs)
                {
                    Vector2 pos = limb.Position;
                    float pointDist = pos.Length() * displayScale;

                    if (limb.SimPosition == Vector2.Zero || pointDist > radius) continue;


                    if (pointDist > radius) continue;
                    if (pointDist > prevPingRadius && pointDist < pingRadius)
                    {
                        var blip = new RadarBlip(pos - Level.Loaded.Position, 1.0f);
                        radarBlips.Add(blip);
                    }
                }
            }

            foreach (RadarBlip radarBlip in radarBlips)
            {
                DrawBlip(spriteBatch,radarBlip,  center, Color.Green * radarBlip.FadeTimer, radius);
            }

            prevPingRadius = pingRadius;

            if (screenOverlay!=null)
            {
                screenOverlay.Draw(spriteBatch, center, 0.0f, rect.Width/screenOverlay.size.X);
            }

            //prevPingRadius = pingRadius;

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

            voltage = 0.0f;
        }

        private void DrawBlip(SpriteBatch spriteBatch, RadarBlip blip, Vector2 center, Color color, float radius)
        {
            Vector2 pos = (blip.Position + Level.Loaded.Position) * displayScale;
            pos.Y = -pos.Y;

            if (pos.Length() > radius)
            {
                blip.FadeTimer = 0.0f;
                return;
            }

            pos.X = MathUtils.Round(pos.X, 4);
            pos.Y = MathUtils.Round(pos.Y, 2);

            GUI.DrawRectangle(spriteBatch, center + pos, new Vector2(4, 2), color, true);
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

        public override bool FillNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetBuffer message)
        {
            message.Write(IsActive);

            return true;
        }

        public override void ReadNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetBuffer message, float sendingTime)
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

    class RadarBlip
    {
        public float FadeTimer;
        public Vector2 Position;

        public RadarBlip(Vector2 pos, float fadeTimer)
        {
            Position = pos;
            FadeTimer = fadeTimer;
        }
    }
}
