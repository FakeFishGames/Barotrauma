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

        public override void UpdateHUD(Character character)
        {
            GuiFrame.Update((float)Timing.Step);
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            GuiFrame.Draw(spriteBatch);

            if (voltage < minVoltage && powerConsumption > 0.0f) return;

            int radius = GuiFrame.Rect.Height / 2 - 30;
            DrawRadar(spriteBatch, new Rectangle((int)GuiFrame.Center.X - radius, (int)GuiFrame.Center.Y - radius, radius * 2, radius * 2));
        }

        private void DrawRadar(SpriteBatch spriteBatch, Rectangle rect)
        {
            Vector2 center = new Vector2(rect.X + rect.Width*0.5f, rect.Center.Y);

            if (!IsActive) return;

            float pingRadius = (rect.Width / 2.0f) * pingState;
            pingCircle.Draw(spriteBatch, center, Color.White * (1.0f - pingState), 0.0f, (rect.Width / pingCircle.size.X) * pingState);

            float radius = rect.Width / 2.0f;

            float displayScale = radius / range;
            
            foreach (Submarine submarine in Submarine.Loaded)
            {
                if (item.Submarine == submarine && !DetectSubmarineWalls) continue;
                if (item.Submarine != null && item.Submarine.DockedTo.Contains(submarine)) continue;
                if (submarine.HullVertices == null) continue;

                for (int i = 0; i < submarine.HullVertices.Count; i++)
                {
                    Vector2 start = ConvertUnits.ToDisplayUnits(submarine.HullVertices[i]);
                    Vector2 end = ConvertUnits.ToDisplayUnits(submarine.HullVertices[(i + 1) % submarine.HullVertices.Count]);

                    if (item.Submarine == submarine)
                    {
                        start += Rand.Vector(500.0f);
                        end += Rand.Vector(500.0f);
                    }

                    CreateBlipsForLine(
                        start + submarine.WorldPosition,
                        end + submarine.WorldPosition,
                        radius, displayScale, 200.0f, 2.0f);
                }
            }


            if (item.Submarine != null && !DetectSubmarineWalls)
            {
                float simScale = displayScale * Physics.DisplayToSimRation;

                foreach (Submarine submarine in Submarine.Loaded)
                {
                    if (submarine != item.Submarine && !submarine.DockedTo.Contains(item.Submarine)) continue;

                    Vector2 offset = ConvertUnits.ToSimUnits(submarine.WorldPosition - item.WorldPosition);

                    for (int i = 0; i < submarine.HullVertices.Count; i++)
                    {
                        Vector2 start = (submarine.HullVertices[i] + offset) * simScale;
                        start.Y = -start.Y;
                        Vector2 end = (submarine.HullVertices[(i + 1) % submarine.HullVertices.Count] + offset) * simScale;
                        end.Y = -end.Y;

                        GUI.DrawLine(spriteBatch, center + start, center + end, Color.Green);
                    }
                }
            }


            if (Level.Loaded != null && (item.CurrentHull == null || !DetectSubmarineWalls))
            {
                if (Level.Loaded.Size.Y - item.WorldPosition.Y < range)
                {
                    CreateBlipsForLine(
                        new Vector2(item.WorldPosition.X - range, Level.Loaded.Size.Y),
                        new Vector2(item.WorldPosition.X + range, Level.Loaded.Size.Y),
                        radius, displayScale, 500.0f, 10.0f);
                }

                List<VoronoiCell> cells = Level.Loaded.GetCells(item.WorldPosition, 7);
                foreach (VoronoiCell cell in cells)
                {
                    foreach (GraphEdge edge in cell.edges)
                    {
                        if (!edge.isSolid) continue;
                        float cellDot = Vector2.Dot(cell.Center - item.WorldPosition, (edge.Center + cell.Translation) - cell.Center);
                        if (cellDot > 0) continue;

                        float facingDot = Vector2.Dot(
                            Vector2.Normalize(edge.point1 - edge.point2),
                            Vector2.Normalize(cell.Center - item.WorldPosition));

                        CreateBlipsForLine(edge.point1 + cell.Translation, edge.point2 + cell.Translation, radius, displayScale, 350.0f, 3.0f * (Math.Abs(facingDot) + 1.0f));
                    }
                }    

                foreach (RuinGeneration.Ruin ruin in Level.Loaded.Ruins)
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

                            CreateBlipsForLine(wall.A, wall.B, radius, displayScale, 100.0f, 1000.0f);
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
                GameMain.GameSession.StartLocation.Name,
                (Level.Loaded.StartPosition - item.WorldPosition), displayScale, center, (rect.Width * 0.5f));

            DrawMarker(spriteBatch,
                GameMain.GameSession.EndLocation.Name,
                (Level.Loaded.EndPosition - item.WorldPosition), displayScale, center, (rect.Width * 0.5f));

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

            foreach (Submarine sub in Submarine.Loaded)
            {
                if (!sub.OnRadar) continue;
                if (item.Submarine == sub || sub.DockedTo.Contains(item.Submarine)) continue;
                if (sub.WorldPosition.Y > Level.Loaded.Size.Y) continue;
                
                DrawMarker(spriteBatch, sub.Name, sub.WorldPosition - item.WorldPosition, displayScale, center, (rect.Width * 0.45f));
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

        private void CreateBlipsForLine(Vector2 point1, Vector2 point2, float radius, float displayScale, float lineStep, float zStep)
        {
            float pingRadius = radius * pingState;

            float length = (point1 - point2).Length();

            Vector2 lineDir = (point2 - point1) / length;

            for (float x = 0; x < length; x += lineStep*Rand.Range(0.8f,1.2f))
            {
                Vector2 point = point1 + lineDir * x;
                //point += cell.Translation;

                float pointDist = Vector2.Distance(item.WorldPosition, point) * displayScale;

                if (pointDist > radius) continue;
                if (pointDist < prevPingRadius || pointDist > pingRadius) continue;


                //float step = 3.0f * (Math.Abs(facingDot) + 1.0f);
                float alpha = Rand.Range(1.5f, 2.0f);
                for (float z = 0; z < radius - pointDist; z += zStep)
                {

                    var blip = new RadarBlip(
                        point + Rand.Vector(150.0f) + Vector2.Normalize(point - item.WorldPosition) * z / displayScale,
                        alpha);

                    radarBlips.Add(blip);
                    zStep += 0.5f;
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

            float textAlpha = MathHelper.Clamp(1.5f - dist / 50000.0f, 0.5f, 1.0f);

            Vector2 dir = Vector2.Normalize(position);

            Vector2 markerPos = (dist*scale>radius) ? dir * radius : position;
            markerPos += center;

            markerPos.X = (int)markerPos.X;
            markerPos.Y = (int)markerPos.Y;

            GUI.DrawRectangle(spriteBatch, new Rectangle((int)markerPos.X, (int)markerPos.Y, 5, 5), Color.LightGreen * textAlpha);

            if (dir.X < 0.0f) markerPos.X -= GUI.SmallFont.MeasureString(label).X+10;

            string wrappedLabel = ToolBox.WrapText(label, 150, GUI.SmallFont);

            wrappedLabel += "\n"+((int)(dist * Physics.DisplayToRealWorldRatio) + " m");

            GUI.DrawString(spriteBatch, 
                new Vector2(markerPos.X + 10, markerPos.Y), 
                wrappedLabel, 
                Color.LightGreen * textAlpha, Color.Black * textAlpha * 0.5f, 
                2, GUI.SmallFont);              
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
