using System;
using System.Collections.Generic;
using System.Xml.Linq;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.ObjectModel;
using Barotrauma.Items.Components;

namespace Barotrauma
{
    class Gap : MapEntity
    {
        public static List<Gap> GapList = new List<Gap>();

        public static bool ShowGaps = true;

        public bool isHorizontal;

        //private Sound waterSound;

        //a value between 0.0f-1.0f (0.0 = closed, 1.0f = open)
        private float open;           

        //the force of the water flow which is exerted on physics bodies
        private Vector2 flowForce;

        private Hull flowTargetHull;

        private float higherSurface;
        private float lowerSurface;

        private Vector2 lerpedFlowForce;

        //if set to true, hull connections of this gap won't be updated when changes are being done to hulls
        public bool DisableHullRechecks;
        
        //can ambient light get through the gap even if it's not open
        public bool PassAmbientLight;

        public float Open
        {
            get { return open; }
            set { open = MathHelper.Clamp(value, 0.0f, 1.0f); }
        }

        public Door ConnectedDoor;

        public Structure ConnectedWall;

        public Vector2 LerpedFlowForce
        {
            get { return lerpedFlowForce; }
        }

        public Hull FlowTargetHull
        {
            get { return flowTargetHull; }
        }

        public bool IsRoomToRoom
        {
            get
            {
                return linkedTo.Count == 2;
            }
        }

        public override Rectangle Rect
        {
            get
            {
                return base.Rect;
            }
            set
            {
                base.Rect = value;

                FindHulls();
            }
        }

        public override string Name
        {
            get
            {
                return "Gap";
            }
        }

        public override bool SelectableInEditor
        {
            get
            {
                return ShowGaps;
            }
        }

        public Gap(MapEntityPrefab prefab, Rectangle rectangle)
           : this (rectangle, Submarine.MainSub)
        { }

        public Gap(Rectangle newRect, Submarine submarine)
            : this(newRect, newRect.Width < newRect.Height, submarine)
        { }

        public Gap(Rectangle newRect, bool isHorizontal, Submarine submarine)
            : base (MapEntityPrefab.list.Find(m=> m.Name == "Gap"), submarine)
        {
            rect = newRect;
            linkedTo = new ObservableCollection<MapEntity>();

            flowForce = Vector2.Zero;

            this.isHorizontal = isHorizontal;

            open = 1.0f;

            FindHulls();

            GapList.Add(this);
            InsertToList();
        }

        public override MapEntity Clone()
        {
            return new Gap(rect, isHorizontal, Submarine);
        }

        public override void Move(Vector2 amount)
        {
            base.Move(amount);

            FindHulls();
        }

        public static void UpdateHulls()
        {
            foreach (Gap g in GapList)
            {
                if (g.DisableHullRechecks) continue;
                g.FindHulls();
            }
        }

        public override bool IsMouseOn(Vector2 position)
        {
            return (ShowGaps && Submarine.RectContains(WorldRect, position) &&
                !Submarine.RectContains(MathUtils.ExpandRect(WorldRect, -5), position));
        }

        private void FindHulls()
        {
            Hull[] hulls = new Hull[2];

            linkedTo.Clear();

            Vector2[] searchPos = new Vector2[2];
            if (isHorizontal)
            {
                searchPos[0] = new Vector2(rect.X, rect.Y - rect.Height / 2);
                searchPos[1] = new Vector2(rect.Right, rect.Y - rect.Height / 2);
            }
            else
            {
                searchPos[0] = new Vector2(rect.Center.X, rect.Y);
                searchPos[1] = new Vector2(rect.Center.X, rect.Y - rect.Height);
            }

            hulls[0] = Hull.FindHullOld(searchPos[0], null, false);
            hulls[1] = Hull.FindHullOld(searchPos[1], null, false);

            if (hulls[0] == null && hulls[1] == null) return;

            if (hulls[0]==null && hulls[1]!=null)
            {
                Hull temp = hulls[0];
                hulls[0] = hulls[1];
                hulls[1] = temp;
            }

            flowTargetHull = hulls[0];

            for (int i = 0 ; i <2; i++)
            {
                if (hulls[i]==null) continue;
                linkedTo.Add(hulls[i]);
                if (!hulls[i].ConnectedGaps.Contains(this)) hulls[i].ConnectedGaps.Add(this);
            }
        }

        public override void Draw(SpriteBatch sb, bool editing, bool back = true)
        {
            if (GameMain.DebugDraw)
            {
                Vector2 center = new Vector2(WorldRect.X + rect.Width / 2.0f, -(WorldRect.Y - rect.Height/ 2.0f));

                GUI.DrawLine(sb, center, center + new Vector2(flowForce.X, -flowForce.Y)/10.0f, Color.Red);

                GUI.DrawLine(sb, center + Vector2.One * 5.0f, center + new Vector2(lerpedFlowForce.X, -lerpedFlowForce.Y) / 10.0f + Vector2.One * 5.0f, Color.Orange);
            }
            
            if (!editing || !ShowGaps) return;

            Color clr = (open == 0.0f) ? Color.Red : Color.Cyan;
            if (isHighlighted) clr = Color.Gold;

            float depth = (ID % 255) * 0.000001f;

            GUI.DrawRectangle(
                sb, new Rectangle(WorldRect.X, -WorldRect.Y, rect.Width, rect.Height), 
                clr * 0.5f, true,
                depth, 
                (int)Math.Max((1.5f / GameScreen.Selected.Cam.Zoom), 1.0f));

            for (int i = 0; i < linkedTo.Count; i++)
            {
                Vector2 dir = isHorizontal ?
                    new Vector2(Math.Sign(linkedTo[i].Rect.Center.X - rect.Center.X), 0.0f)
                    : new Vector2(0.0f, Math.Sign((linkedTo[i].Rect.Y - linkedTo[i].Rect.Height / 2.0f) - (rect.Y - rect.Height / 2.0f)));

                Vector2 arrowPos = new Vector2(WorldRect.Center.X, -(WorldRect.Y - WorldRect.Height / 2));
                arrowPos += new Vector2(dir.X * (WorldRect.Width / 2 + 10), dir.Y * (WorldRect.Height / 2 + 10));

                GUI.Arrow.Draw(sb,
                    arrowPos, clr * 0.8f,
                    GUI.Arrow.Origin, MathUtils.VectorToAngle(dir) + MathHelper.PiOver2,
                    isHorizontal ? new Vector2(rect.Height / 16.0f, 1.0f) : new Vector2(rect.Width / 16.0f, 1.0f),
                    SpriteEffects.None, depth);
            }        

            if (IsSelected)
            {
                GUI.DrawRectangle(sb,
                    new Vector2(WorldRect.X - 5, -WorldRect.Y - 5),
                    new Vector2(rect.Width + 10, rect.Height + 10),
                    Color.Red,
                    false,
                    depth,
                    (int)Math.Max((1.5f / GameScreen.Selected.Cam.Zoom), 1.0f));
            }
        }
        
        public override void Update(Camera cam, float deltaTime)
        {
            flowForce = Vector2.Zero;

            if (open == 0.0f)
            {
                lerpedFlowForce = Vector2.Zero;
                return;
            }

            UpdateOxygen();

            if (linkedTo.Count == 1)
            {
                //gap leading from a room to outside
                UpdateRoomToOut(deltaTime);
            }
            else
            {
                //gap leading from a room to another
                UpdateRoomToRoom(deltaTime);
            }

            lerpedFlowForce = Vector2.Lerp(lerpedFlowForce, flowForce, deltaTime);

            if (LerpedFlowForce.LengthSquared() > 20000.0f && flowTargetHull != null && flowTargetHull.Volume < flowTargetHull.FullVolume)
            {
                //UpdateFlowForce();

                Vector2 pos = Position;
                if (isHorizontal)
                {
                    pos.X += Math.Sign(flowForce.X);
                    pos.Y = MathHelper.Clamp((higherSurface + lowerSurface) / 2.0f, rect.Y - rect.Height, rect.Y) + 10;

                    Vector2 velocity = new Vector2(
                        MathHelper.Clamp(flowForce.X, -5000.0f, 5000.0f) * Rand.Range(0.5f, 0.7f),
                        flowForce.Y * Rand.Range(0.5f, 0.7f));

                    var particle = GameMain.ParticleManager.CreateParticle(
                        "watersplash",
                        (Submarine == null ? pos : pos + Submarine.Position) - Vector2.UnitY * Rand.Range(0.0f, 10.0f),
                        velocity, 0, flowTargetHull);

                    if (particle != null)
                    {
                        particle.Size = particle.Size * Math.Min(Math.Abs(flowForce.X / 1000.0f), 5.0f);
                    }

                    if (Math.Abs(flowForce.X) > 300.0f)
                    {
                        pos.X += Math.Sign(flowForce.X) * 10.0f;
                        pos.Y = Rand.Range(lowerSurface, rect.Y - rect.Height);

                        GameMain.ParticleManager.CreateParticle(
                          "bubbles",
                          Submarine == null ? pos : pos + Submarine.Position,
                          flowForce / 10.0f, 0, flowTargetHull);  
                    }
                }
                else
                {
                    if (Math.Sign(flowTargetHull.Rect.Y - rect.Y) != Math.Sign(lerpedFlowForce.Y)) return;

                    pos.Y += Math.Sign(flowForce.Y) * rect.Height / 2.0f;
                    for (int i = 0; i < rect.Width; i += (int)Rand.Range(80, 100))
                    {
                        pos.X = Rand.Range(rect.X, rect.X + rect.Width);

                        Vector2 velocity = new Vector2(
                            lerpedFlowForce.X * Rand.Range(0.5f, 0.7f),
                            Math.Max(lerpedFlowForce.Y, -100.0f) * Rand.Range(0.5f, 0.7f));

                        var splash = GameMain.ParticleManager.CreateParticle(
                            "watersplash", 
                            Submarine == null ? pos : pos + Submarine.Position,
                            -velocity, 0, FlowTargetHull);

                        if (splash != null) splash.Size = splash.Size * MathHelper.Clamp(rect.Width / 50.0f, 0.8f, 4.0f);

                        GameMain.ParticleManager.CreateParticle(
                            "bubbles", 
                            Submarine == null ? pos : pos + Submarine.Position,
                            flowForce / 2.0f, 0, FlowTargetHull);
                    }
                }

            }


            if (flowTargetHull != null && lerpedFlowForce != Vector2.Zero)
            {
                foreach (Character character in Character.CharacterList)
                {
                    if (character.AnimController.CurrentHull != flowTargetHull) continue;

                    foreach (Limb limb in character.AnimController.Limbs)
                    {
                        if (!limb.inWater) continue;

                        float dist = Vector2.Distance(limb.WorldPosition, WorldPosition);
                        if (dist > lerpedFlowForce.Length()) continue;

                        limb.body.ApplyForce(lerpedFlowForce / dist/10.0f);
                    }
                }
            }
            
            
        }

        void UpdateRoomToRoom(float deltaTime)
        {
            if (linkedTo.Count < 2) return;
            Hull hull1 = (Hull)linkedTo[0];
            Hull hull2 = (Hull)linkedTo[1];
            
            Vector2 subOffset = Vector2.Zero;
            if (hull1.Submarine != Submarine)
            {
                subOffset =Submarine.Position - hull1.Submarine.Position;
            }
            else if (hull2.Submarine != Submarine)
            {

                subOffset = hull2.Submarine.Position - Submarine.Position;

            }

            if (hull1.Volume == 0.0 && hull2.Volume == 0.0) return;

            float size = (isHorizontal) ? rect.Height : rect.Width;

            //a variable affecting the water flow through the gap
            //the larger the gap is, the faster the water flows
            float sizeModifier = size / 100.0f * open;

            //horizontal gap (such as a regular door)
            if (isHorizontal)
            {
                higherSurface = Math.Max(hull1.Surface, hull2.Surface + subOffset.Y);
                float delta=0.0f;
                
                //water level is above the lower boundary of the gap
                if (Math.Max(hull1.Surface+hull1.WaveY[hull1.WaveY.Length - 1], hull2.Surface + subOffset.Y +hull2.WaveY[0]) > rect.Y - size)
                {

                    int dir = (hull1.Pressure > hull2.Pressure+subOffset.Y) ? 1 : -1;

                    //water flowing from the righthand room to the lefthand room
                    if (dir == -1)
                    {
                        if (!(hull2.Volume > 0.0f)) return;
                        lowerSurface = hull1.Surface - hull1.WaveY[hull1.WaveY.Length - 1];
                        //delta = Math.Min((room2.water.pressure - room1.water.pressure) * sizeModifier, Math.Min(room2.water.Volume, room2.Volume));
                        //delta = Math.Min(delta, room1.Volume - room1.water.Volume + Water.MaxCompress);

                        flowTargetHull = hull1;

                        //make sure not to move more than what the room contains
                        delta = Math.Min(((hull2.Pressure + subOffset.Y) - hull1.Pressure) * 5.0f * sizeModifier, Math.Min(hull2.Volume, hull2.FullVolume));
                        
                        //make sure not to place more water to the target room than it can hold
                        delta = Math.Min(delta, hull1.FullVolume + Hull.MaxCompress - (hull1.Volume));
                        hull1.Volume += delta;
                        hull2.Volume -= delta;
                        if (hull1.Volume > hull1.FullVolume)
                        {
                            hull1.Pressure = Math.Max(hull1.Pressure, (hull1.Pressure + hull2.Pressure+subOffset.Y) / 2);
                        }

                        flowForce = new Vector2(-delta, 0.0f);
                    }
                    else if (dir == 1)
                    {
                        if (!(hull1.Volume > 0.0f)) return;
                        lowerSurface = hull2.Surface - hull2.WaveY[hull2.WaveY.Length - 1];

                        flowTargetHull = hull2;

                        //make sure not to move more than what the room contains
                        delta = Math.Min((hull1.Pressure - (hull2.Pressure + subOffset.Y)) * 5.0f * sizeModifier, Math.Min(hull1.Volume, hull1.FullVolume));

                        //make sure not to place more water to the target room than it can hold
                        delta = Math.Min(delta, hull2.FullVolume + Hull.MaxCompress - (hull2.Volume));
                        hull1.Volume -= delta;
                        hull2.Volume += delta;
                        if (hull2.Volume > hull2.FullVolume)
                        {
                            hull2.Pressure = Math.Max(hull2.Pressure, ((hull1.Pressure-subOffset.Y) + hull2.Pressure) / 2);
                        }
                        
                        flowForce = new Vector2(delta, 0.0f);
                    }

                    if (delta>100.0f && subOffset == Vector2.Zero)
                    {
                        float avg = (hull1.Surface + hull2.Surface) / 2.0f;
                        //float avgVel = (hull2.WaveVel[1] + hull1.WaveVel[hull1.WaveY.Length - 2]) / 2.0f;

                        if (hull1.Volume < hull1.FullVolume - Hull.MaxCompress &&
                            hull1.Surface + hull1.WaveY[hull1.WaveY.Length - 1] < rect.Y)
                        {
                            hull1.WaveVel[hull1.WaveY.Length - 1] = (avg-(hull1.Surface + hull1.WaveY[hull1.WaveY.Length - 1]))*0.1f;
                            hull1.WaveVel[hull1.WaveY.Length - 2] = hull1.WaveVel[hull1.WaveY.Length - 1];
                        }

                        if (hull2.Volume < hull2.FullVolume - Hull.MaxCompress &&
                            hull2.Surface + hull2.WaveY[0] < rect.Y)
                        {
                            hull2.WaveVel[0] = (avg - (hull2.Surface + hull2.WaveY[0])) * 0.1f;
                            hull2.WaveVel[1] = hull2.WaveVel[0];                   
                        }
                    }



                }

            }
            else
            {
                //lower room is full of water
                if ((hull2.Pressure + subOffset.Y) > hull1.Pressure)
                {
                    float delta = Math.Min(hull2.Volume - hull2.FullVolume + Hull.MaxCompress / 2.0f, deltaTime * 8000.0f * sizeModifier);

                    flowForce = new Vector2(0.0f, Math.Min((hull2.Pressure + subOffset.Y) - hull1.Pressure, 500.0f));

                    delta = Math.Max(delta, 0.0f);
                    hull1.Volume += delta;
                    hull2.Volume -= delta;

                    flowTargetHull = hull1;

                    if (hull1.Volume > hull1.FullVolume)
                    {
                        hull1.Pressure = Math.Max(hull1.Pressure, (hull1.Pressure + (hull2.Pressure + subOffset.Y)) / 2);
                    }                   

                }
                //there's water in the upper room, drop to lower
                else if (hull1.Volume > 0)
                {
                    flowTargetHull = hull2;

                    //make sure the amount of water moved isn't more than what the room contains
                    float delta = Math.Min(hull1.Volume, deltaTime * 25000f * sizeModifier);
                    //make sure not to place more water to the target room than it can hold
                    delta = Math.Min(delta, (hull2.FullVolume + Math.Max(hull1.Volume - hull1.FullVolume, 0.0f)) - hull2.Volume + Hull.MaxCompress / 4.0f);

                    hull1.Volume -= delta;
                    hull2.Volume += delta;

                    if (hull2.Volume > hull2.FullVolume)
                    {
                        hull2.Pressure = Math.Max(hull2.Pressure, ((hull1.Pressure - subOffset.Y) + hull2.Pressure) / 2);
                    }

                    flowForce = new Vector2(0.0f, -delta);

                    flowForce.X = hull1.WaveY[hull1.GetWaveIndex(rect.X)] - hull1.WaveY[hull1.GetWaveIndex(rect.Right)] * 10.0f;

                    //if (water2.Volume < water2.FullVolume - Hull.MaxCompress)
                    //{
                    //    int posX = (int)((rect.X + size / 2.0f - water1.Rect.X) / Hull.WaveWidth);
                    //    //water1.WaveY[posX] = -delta;
                    //    if (posX > -1 && posX < water2.WaveVel.Length)
                    //        water1.WaveVel[posX] = -delta * 0.01f;

                    //    posX = (int)((rect.X + size / 2.0f - water2.Rect.X) / Hull.WaveWidth);                        
                    //    //water2.WaveY[posX] = delta;
                    //    if (posX > -1 && posX<water2.WaveVel.Length)
                    //        water2.WaveVel[posX] = delta * 0.01f;
                    //}
                }
            }

            if (open > 0.0f)
            {
                if (hull1.Volume > hull1.FullVolume - Hull.MaxCompress && hull2.Volume > hull2.FullVolume - Hull.MaxCompress)
                {
                    float avgLethality = (hull1.LethalPressure + hull2.LethalPressure) / 2.0f;
                    hull1.LethalPressure = avgLethality;
                    hull2.LethalPressure = avgLethality;
                }
                else 
                {
                    hull1.LethalPressure = 0.0f;
                    hull2.LethalPressure = 0.0f;
                }
            }
        }

        void UpdateRoomToOut(float deltaTime)
        {
            if (linkedTo.Count != 1) return;

            float size = (isHorizontal) ? rect.Height : rect.Width;

            Hull hull1 = (Hull)linkedTo[0];

            //a variable affecting the water flow through the gap
            //the larger the gap is, the faster the water flows
            float sizeModifier = size * open * open;

            float delta = Hull.MaxCompress * sizeModifier * deltaTime;
            
            //make sure not to place more water to the target room than it can hold
            delta = Math.Min(delta, hull1.FullVolume + Hull.MaxCompress - hull1.Volume);
            hull1.Volume += delta;

            if (hull1.Volume > hull1.FullVolume) hull1.Pressure += 0.5f;

            flowTargetHull = hull1;

            if (isHorizontal)
            {
                //water flowing from right to left
                if (rect.X > hull1.Rect.X + hull1.Rect.Width / 2.0f)
                {
                    flowForce = new Vector2(-delta, 0.0f);
                    
                }
                else
                {
                    flowForce = new Vector2(delta, 0.0f);
                }

                higherSurface = hull1.Surface;
                lowerSurface = rect.Y;

                if (hull1.Volume < hull1.FullVolume - Hull.MaxCompress &&
                    hull1.Surface < rect.Y)
                {
                    if (rect.X > hull1.Rect.X + hull1.Rect.Width / 2.0f)
                    {
                        float vel = ((rect.Y - rect.Height / 2) - (hull1.Surface + hull1.WaveY[hull1.WaveY.Length - 1])) * 0.1f;
                        vel *= Math.Min(Math.Abs(flowForce.X) / 200.0f, 1.0f);

                        hull1.WaveVel[hull1.WaveY.Length - 1] += vel;
                        hull1.WaveVel[hull1.WaveY.Length - 2] += vel;
                    }
                    else
                    {
                        float vel = ((rect.Y - rect.Height / 2) - (hull1.Surface + hull1.WaveY[0])) * 0.1f;
                        vel *= Math.Min(Math.Abs(flowForce.X) / 200.0f, 1.0f);

                        hull1.WaveVel[0] += vel;
                        hull1.WaveVel[1] += vel;
                    } 
                }
                else
                {
                    hull1.LethalPressure += (Submarine != null && Submarine.AtDamageDepth) ? 100.0f * deltaTime : 10.0f * deltaTime;
                }
            }
            else
            {
                if (rect.Y > hull1.Rect.Y - hull1.Rect.Height / 2.0f)
                {
                    flowForce = new Vector2(0.0f, -delta);
                }
                else
                {
                    flowForce = new Vector2(0.0f, delta);
                }
                if (hull1.Volume >= hull1.FullVolume - Hull.MaxCompress)
                {
                    hull1.LethalPressure += (Submarine != null && Submarine.AtDamageDepth) ? 100.0f * deltaTime : 10.0f * deltaTime;
                }
            }

        }

        private void UpdateOxygen()
        {
            if (linkedTo.Count < 2) return;
            Hull hull1 = (Hull)linkedTo[0];
            Hull hull2 = (Hull)linkedTo[1];

            if (isHorizontal)
            {
                if (Math.Max(hull1.Surface + hull1.WaveY[hull1.WaveY.Length - 1], hull2.Surface + hull2.WaveY[0]) > rect.Y) return;
            }

            float totalOxygen = hull1.Oxygen + hull2.Oxygen;
            float totalVolume = (hull1.FullVolume + hull2.FullVolume);
            
            float deltaOxygen = (totalOxygen * hull1.FullVolume / totalVolume) - hull1.Oxygen;
            deltaOxygen = MathHelper.Clamp(deltaOxygen, -Hull.OxygenDistributionSpeed, Hull.OxygenDistributionSpeed);

            hull1.Oxygen += deltaOxygen;
            hull2.Oxygen -= deltaOxygen;            
        }

        public static Gap FindAdjacent(List<Gap> gaps, Vector2 worldPos, float allowedOrthogonalDist)
        {
            foreach (Gap gap in gaps)
            {
                if (gap.Open == 0.0f || gap.IsRoomToRoom) continue;

                if (gap.ConnectedWall != null)
                {
                    int sectionIndex = gap.ConnectedWall.FindSectionIndex(gap.Position);
                    if (sectionIndex > -1 && !gap.ConnectedWall.SectionBodyDisabled(sectionIndex)) continue;
                }

                if (gap.isHorizontal)
                {
                    if (worldPos.Y < gap.WorldRect.Y && worldPos.Y > gap.WorldRect.Y - gap.WorldRect.Height &&
                        Math.Abs(gap.WorldRect.Center.X - worldPos.X) < allowedOrthogonalDist)
                    {
                        return gap;
                    }
                }
                else
                {
                    if (worldPos.X > gap.WorldRect.X && worldPos.X < gap.WorldRect.Right &&
                        Math.Abs(gap.WorldRect.Y - gap.WorldRect.Height / 2 - worldPos.Y) < allowedOrthogonalDist)
                    {
                        return gap;
                    }
                }
            }

            return null;
        }

        public override void ShallowRemove()
        {
            base.ShallowRemove();
            GapList.Remove(this);

            foreach (Hull hull in Hull.hullList)
            {
                hull.ConnectedGaps.Remove(this);
            }
        }

        public override void Remove()
        {
            base.Remove();
            GapList.Remove(this);

            foreach (Hull hull in Hull.hullList)
            {
                hull.ConnectedGaps.Remove(this);
            }
        }

        public override void OnMapLoaded()
        {
            FindHulls();
        }

        public override XElement Save(XElement parentElement)
        {
            XElement element = new XElement("Gap");

            element.Add(
                new XAttribute("ID", ID), 
                new XAttribute("horizontal", isHorizontal ? "true" : "false"));

            element.Add(new XAttribute("rect",
                    (int)(rect.X - Submarine.HiddenSubPosition.X) + "," +
                    (int)(rect.Y - Submarine.HiddenSubPosition.Y) + "," +
                    rect.Width + "," + rect.Height));

            //if (linkedTo != null)
            //{
            //    int i = 0;
            //    foreach (Entity e in linkedTo)
            //    {
            //        if (e == null) continue;
            //        element.Add(new XAttribute("linkedto" + i, e.ID));
            //        i += 1;
            //    }
            //}

            parentElement.Add(element);

            return element;
        }


        public static void Load(XElement element, Submarine submarine)
        {
            Rectangle rect = Rectangle.Empty;

            if (element.Attribute("rect") != null)
            {
                string rectString = ToolBox.GetAttributeString(element, "rect", "0,0,0,0");
                string[] rectValues = rectString.Split(',');

                rect = new Rectangle(
                    int.Parse(rectValues[0]),
                    int.Parse(rectValues[1]),
                    int.Parse(rectValues[2]),
                    int.Parse(rectValues[3]));
            }
            else
            {
                rect = new Rectangle(
                    int.Parse(element.Attribute("x").Value),
                    int.Parse(element.Attribute("y").Value),
                    int.Parse(element.Attribute("width").Value),
                    int.Parse(element.Attribute("height").Value));
            }

            bool isHorizontal = rect.Height > rect.Width;

            var horizontalAttribute = element.Attribute("horizontal");
            if (horizontalAttribute!=null)
            {
                isHorizontal = horizontalAttribute.Value.ToString() == "true";
            }

            Gap g = new Gap(rect, isHorizontal, submarine);
            g.ID = (ushort)int.Parse(element.Attribute("ID").Value);
            
            g.linkedToID = new List<ushort>();
            //int i = 0;
            //while (element.Attribute("linkedto" + i) != null)
            //{
            //    g.linkedToID.Add(int.Parse(element.Attribute("linkedto" + i).Value));
            //    i += 1;
            //}
        }
    }
}
