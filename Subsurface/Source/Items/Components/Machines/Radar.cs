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

            var tickBox = new GUITickBox(new Rectangle(0,0,20,20), "Sonar", Alignment.TopLeft, GuiFrame);
            tickBox.OnSelected = (GUITickBox box) =>
            {
                IsActive = box.Selected;
                item.NewComponentEvent(this, true, false);

                return true;
            };

        }

        public override void Update(float deltaTime, Camera cam)
        {
            currPowerConsumption = powerConsumption;

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

            voltage -= deltaTime;
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            return (pingState > 1.0f);
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            int x = GuiFrame.Rect.X;
            int y = GuiFrame.Rect.Y;

            GuiFrame.Update(1.0f / 60.0f);
            GuiFrame.Draw(spriteBatch);

            if (voltage < minVoltage) return;


            int radius = GuiFrame.Rect.Height / 2 - 30;
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
                List<VoronoiCell> cells = Level.Loaded.GetCells(item.WorldPosition, 7);

                foreach (VoronoiCell cell in cells)
                {

                    foreach (GraphEdge edge in cell.edges)
                    {
                        if (!edge.isSolid) continue;
                        float cellDot = Vector2.Dot(cell.Center - item.WorldPosition, (edge.Center+cell.Translation) - cell.Center);
                        if (cellDot > 0) continue;

                        float facingDot = Vector2.Dot(
                            Vector2.Normalize(edge.point1 - edge.point2), 
                            Vector2.Normalize(cell.Center-item.WorldPosition));

                        //if (Math.Abs(facingDot) > 0.5f) continue;

                        //facingDot = 1.0f;// MathHelper.Clamp(facingDot, -1.0f, 1.0f);
                        
                        float length = (edge.point1 - edge.point2).Length();
                        for (float x = 0; x < length; x += Rand.Range(300.0f, 400.0f))
                        {
                            Vector2 point = edge.point1 + Vector2.Normalize(edge.point2 - edge.point1) * x;
                            point += cell.Translation;

                            float pointDist = Vector2.Distance(item.WorldPosition, point) * displayScale;

                            if (pointDist > radius) continue;
                            if (pointDist < prevPingRadius || pointDist > pingRadius) continue;


                            float step = 3.0f * (Math.Abs(facingDot) + 1.0f);
                            float alpha = Rand.Range(1.5f, 2.0f);
                            for (float z = 0; z < radius - pointDist; z += step)
                            {

                                var blip = new RadarBlip(
                                    point + Rand.Vector(150.0f) + Vector2.Normalize(point-item.WorldPosition) * z / displayScale,
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
                    Vector2 start = (Submarine.Loaded.HullVertices[i] - ConvertUnits.ToSimUnits(item.Position - Submarine.HiddenSubPosition)) * simScale;
                    start.Y = -start.Y;
                    Vector2 end = (Submarine.Loaded.HullVertices[(i + 1) % Submarine.Loaded.HullVertices.Count] - ConvertUnits.ToSimUnits(item.Position - Submarine.HiddenSubPosition)) * simScale;
                    end.Y = -end.Y;

                    Vector2 diff = end - start;
                    for (float x = 0; x < diff.Length(); x += 4.0f)
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
                    float pointDist = (limb.WorldPosition - item.WorldPosition).Length() * displayScale;

                    if (limb.SimPosition == Vector2.Zero || pointDist > radius) continue;


                    if (pointDist > radius) continue;
                    if (pointDist > prevPingRadius && pointDist < pingRadius)
                    {
                        float limbSize = limb.Mass;

                        for (int i = 0; i<=limb.Mass/100.0f; i++)
                        {
                            var blip = new RadarBlip(limb.WorldPosition+Rand.Vector(limb.Mass/10.0f), 1.0f);
                            radarBlips.Add(blip);
                        }


                    }
                }
            }

            foreach (RadarBlip radarBlip in radarBlips)
            {
                DrawBlip(spriteBatch, radarBlip, center, Color.Green * radarBlip.FadeTimer, radius);
            }

            prevPingRadius = pingRadius;

            if (screenOverlay != null)
            {
                screenOverlay.Draw(spriteBatch, center, 0.0f, rect.Width / screenOverlay.size.X);
            }

            //prevPingRadius = pingRadius;

            if (GameMain.GameSession == null) return;


            DrawMarker(spriteBatch,
                (GameMain.GameSession.Map == null) ? "Start" : GameMain.GameSession.Map.CurrentLocation.Name,
                (Level.Loaded.StartPosition - (Submarine.Loaded.Position + Submarine.HiddenSubPosition)), displayScale, center, (rect.Width * 0.55f));

            DrawMarker(spriteBatch,
                (GameMain.GameSession.Map == null) ? "End" : GameMain.GameSession.Map.SelectedLocation.Name,
                (Level.Loaded.EndPosition - (Submarine.Loaded.Position+Submarine.HiddenSubPosition)), displayScale, center, (rect.Width * 0.55f));

            if (GameMain.GameSession.Mission != null)
            {
                var mission = GameMain.GameSession.Mission;

                if (!string.IsNullOrWhiteSpace(mission.RadarLabel) && mission.RadarPosition != Vector2.Zero)
                {
                    DrawMarker(spriteBatch,
                        mission.RadarLabel,
                        mission.RadarPosition - (Submarine.Loaded.Position + Submarine.HiddenSubPosition), displayScale, center, (rect.Width * 0.55f));
                }
            }

            if (!GameMain.DebugDraw) return;

            var steering = item.GetComponent<Steering>();
            if (steering == null || steering.SteeringPath == null) return;

            Vector2 prevPos = Vector2.Zero;

            foreach (WayPoint wp in steering.SteeringPath.Nodes)
            {
                Vector2 pos = (wp.Position - item.WorldPosition) * displayScale;
                if (pos.Length() > radius) continue;

                pos.Y = -pos.Y;
                pos += center;

                GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X - 3 / 2, (int)pos.Y - 3, 6, 6), (steering.SteeringPath.CurrentNode == wp) ? Color.LightGreen : Color.Green, false);

                if (prevPos != Vector2.Zero)
                {
                    GUI.DrawLine(spriteBatch, pos, prevPos, Color.Green);
                }

                prevPos = pos;
            }

        }

        private void DrawBlip(SpriteBatch spriteBatch, RadarBlip blip, Vector2 center, Color color, float radius)
        {
            
            Vector2 pos = (blip.Position-item.WorldPosition) * displayScale;
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
