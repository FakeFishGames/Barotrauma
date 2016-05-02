using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Voronoi2;

namespace Barotrauma.Items.Components
{
    class Radar : Powered
    {
        private float range;

        private float pingState;

        private readonly Sprite pingCircle, screenOverlay;

        private GUITickBox isActiveTickBox;

        private List<RadarBlip> radarBlips;
        private float prevPingRadius;

        [HasDefaultValue(10000.0f, false)]
        public float Range
        {
            get { return range; }
            set { range = MathHelper.Clamp(value, 0.0f, 100000.0f); }
        }

        [HasDefaultValue(false, false)]
        public bool DetectSubmarineWalls
        {
            get;
            set;
        }

        public Radar(Item item, XElement element)
            : base(item, element)
        {
            radarBlips = new List<RadarBlip>();

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "pingcircle":
                        pingCircle = new Sprite(subElement);
                        break;
                    case "screenoverlay":
                        screenOverlay = new Sprite(subElement);
                        break;
                }
            }

            isActiveTickBox = new GUITickBox(new Rectangle(0, 0, 20, 20), "Sonar", Alignment.TopLeft, GuiFrame);
            isActiveTickBox.OnSelected = (GUITickBox box) =>
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

            if (voltage >= minVoltage || powerConsumption <= 0.0f)
            {
                pingState = pingState + deltaTime * 0.5f;
                if (pingState>1.0f)
                {
                    item.Use(deltaTime);
                    pingState = 0.0f;
                }
                
                if (item.CurrentHull != null) item.CurrentHull.AiTarget.SoundRange = Math.Max(Range * pingState, item.CurrentHull.AiTarget.SoundRange);
            }
            else
            {
                pingState = 0.0f;
            }

            Voltage -= deltaTime;
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            return pingState > 1.0f;
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {

            GuiFrame.Update(1.0f / 60.0f);
            GuiFrame.Draw(spriteBatch);

            if (voltage < minVoltage && powerConsumption > 0.0f) return;

            int radius = GuiFrame.Rect.Height / 2 - 30;
            DrawRadar(spriteBatch, new Rectangle((int)GuiFrame.Center.X - radius, (int)GuiFrame.Center.Y - radius, radius * 2, radius * 2));
        }

        private void DrawRadar(SpriteBatch spriteBatch, Rectangle rect)
        {
            Vector2 center = new Vector2(rect.Center.X, rect.Center.Y);

            if (!IsActive) return;

            float pingRadius = (rect.Width / 2.0f) * pingState;
            pingCircle.Draw(spriteBatch, center, Color.White * (1.0f - pingState), 0.0f, (rect.Width / pingCircle.size.X) * pingState);

            float radius = rect.Width / 2.0f;

            float displayScale = radius / range;

            if (DetectSubmarineWalls)
            {
                for (int i = 0; i < Submarine.Loaded.HullVertices.Count; i++)
                {
                    Vector2 start = ConvertUnits.ToDisplayUnits(Submarine.Loaded.HullVertices[i]);
                    Vector2 end = ConvertUnits.ToDisplayUnits(Submarine.Loaded.HullVertices[(i + 1) % Submarine.Loaded.HullVertices.Count]);

                    if (item.CurrentHull!=null)
                    {
                        start += Rand.Vector(500.0f);
                        end += Rand.Vector(500.0f);
                    }

                    CreateBlipsForLine(
                        start + Submarine.Loaded.WorldPosition, 
                        end + Submarine.Loaded.WorldPosition, 
                        radius, displayScale, 2.0f);
                }

            }
            else
            {
                float simScale = displayScale * Physics.DisplayToSimRation;

                Vector2 offset = ConvertUnits.ToSimUnits(Submarine.Loaded.WorldPosition - item.WorldPosition);

                for (int i = 0; i < Submarine.Loaded.HullVertices.Count; i++)
                {
                    Vector2 start = (Submarine.Loaded.HullVertices[i] + offset) * simScale;
                    start.Y = -start.Y;
                    Vector2 end = (Submarine.Loaded.HullVertices[(i + 1) % Submarine.Loaded.HullVertices.Count] + offset) * simScale;
                    end.Y = -end.Y;

                    GUI.DrawLine(spriteBatch, center + start, center + end, Color.Green);                    
                }
            }           
 
            
            if (Level.Loaded != null && (item.CurrentHull==null || !DetectSubmarineWalls))
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

                        CreateBlipsForLine(edge.point1 + cell.Translation, edge.point2+cell.Translation, radius, displayScale, 3.0f * (Math.Abs(facingDot) + 1.0f));
                    }
                }    

                foreach(RuinGeneration.Ruin ruin in Level.Loaded.Ruins)
                {
                    if (!MathUtils.CircleIntersectsRectangle(item.WorldPosition, range, ruin.Area)) continue;

                    foreach (var ruinShape in ruin.RuinShapes)
                    {
                        foreach (RuinGeneration.Line wall in ruinShape.Walls)
                        {

                            float cellDot = Vector2.Dot(
                                Vector2.Normalize(ruinShape.Center - item.WorldPosition), 
                                Vector2.Normalize((wall.A+wall.B)/2.0f - ruinShape.Center));
                            if (cellDot > 0) continue;

                            CreateBlipsForLine(wall.A, wall.B, radius, displayScale, -cellDot*5.0f);
                        }                       
                    }
                }
            }

            foreach (Character c in Character.CharacterList)
            {
                if (c.AnimController.CurrentHull != null) continue;
                if (DetectSubmarineWalls && c.AnimController.CurrentHull == null && item.CurrentHull != null) continue;

                foreach (Limb limb in c.AnimController.Limbs)
                {
                    float pointDist = (limb.WorldPosition - item.WorldPosition).Length() * displayScale;

                    if (limb.SimPosition == Vector2.Zero || pointDist > radius) continue;
                    
                    if (pointDist > prevPingRadius && pointDist < pingRadius)
                    {
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
                (Level.Loaded.StartPosition - item.WorldPosition), displayScale, center, (rect.Width * 0.55f));

            DrawMarker(spriteBatch,
                (GameMain.GameSession.Map == null) ? "End" : GameMain.GameSession.Map.SelectedLocation.Name,
                (Level.Loaded.EndPosition - item.WorldPosition), displayScale, center, (rect.Width * 0.55f));

            if (GameMain.GameSession.Mission != null)
            {
                var mission = GameMain.GameSession.Mission;

                if (!string.IsNullOrWhiteSpace(mission.RadarLabel) && mission.RadarPosition != Vector2.Zero)
                {
                    DrawMarker(spriteBatch,
                        mission.RadarLabel,
                        mission.RadarPosition - item.WorldPosition, displayScale, center, (rect.Width * 0.55f));
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

        private void CreateBlipsForLine(Vector2 point1, Vector2 point2, float radius, float displayScale, float step)
        {
            float pingRadius = radius * pingState;

            float length = (point1 - point2).Length();

            Vector2 lineDir = (point2 - point1) / length;

            for (float x = 0; x < length; x += Rand.Range(300.0f, 400.0f))
            {
                Vector2 point = point1 + lineDir * x;
                //point += cell.Translation;

                float pointDist = Vector2.Distance(item.WorldPosition, point) * displayScale;

                if (pointDist > radius) continue;
                if (pointDist < prevPingRadius || pointDist > pingRadius) continue;


                //float step = 3.0f * (Math.Abs(facingDot) + 1.0f);
                float alpha = Rand.Range(1.5f, 2.0f);
                for (float z = 0; z < radius - pointDist; z += step)
                {

                    var blip = new RadarBlip(
                        point + Rand.Vector(150.0f) + Vector2.Normalize(point - item.WorldPosition) * z / displayScale,
                        alpha);

                    radarBlips.Add(blip);
                    step += 0.5f;
                    alpha -= (z == 0) ? 0.5f : 0.1f;
                }

            }
        }

        private void DrawBlip(SpriteBatch spriteBatch, RadarBlip blip, Vector2 center, Color color, float radius)
        {
            float displayScale = radius / range;

            Vector2 pos = (blip.Position - item.WorldPosition) * displayScale;
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
            spriteBatch.DrawString(GUI.SmallFont, (int)(dist * Physics.DisplayToRealWorldRatio) + " m", 
                new Vector2(markerPos.X + 10, markerPos.Y + 15), Color.LightGreen);                
        }

        protected override void RemoveComponentSpecific()
        {
            if (pingCircle!=null) pingCircle.Remove();
            if (screenOverlay != null) screenOverlay.Remove();

        }

        public override bool FillNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetBuffer message)
        {
            message.Write(IsActive);

            return true;
        }

        public override void ReadNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetIncomingMessage message, float sendingTime)
        {
            try
            {
                IsActive = message.ReadBoolean();
                isActiveTickBox.Selected = IsActive;
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
