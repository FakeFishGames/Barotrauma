using System;
using System.Collections.Generic;
using System.Xml.Linq;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.ObjectModel;

namespace Subsurface
{
    class Gap : MapEntity
    {
        public bool isHorizontal;

        //private Sound waterSound;

        //a value between 0.0f-1.0f (0.0 = closed, 1.0f = open)
        float open;           

        //the force of the water flow which is exerted on physics bodies
        Vector2 flowForce;

        Hull flowTargetHull;

        float higherSurface;
        float lowerSurface;

        private int soundIndex;

        float soundVolume;

        public float Open
        {
            get { return open; }
            set { open = MathHelper.Clamp(value, 0.0f, 1.0f); }
        }

        public Vector2 FlowForce
        {
            get { return flowForce*soundVolume; }
        }

        public Hull FlowTargetHull
        {
            get { return flowTargetHull; }
        }

        public Gap(Rectangle newRect)
        {
            rect = newRect;
            linkedTo = new ObservableCollection<MapEntity>();

            //waterSound = new Sound("waterstream", 0.0f);

            flowForce = Vector2.Zero;

            isHorizontal = (rect.Width < rect.Height);

            open = 1.0f;

            FindHulls();

            mapEntityList.Add(this);
        }

        public Gap(Rectangle newRect, bool isHorizontal)
        {
            rect = newRect;
            linkedTo = new ObservableCollection<MapEntity>();

            flowForce = Vector2.Zero;

            this.isHorizontal = isHorizontal;

            open = 1.0f;

            FindHulls();

            mapEntityList.Add(this);
        }

        public static void UpdateHulls()
        {
            foreach (MapEntity entity in mapEntityList)
            {
                Gap g = entity as Gap;
                if (g != null) g.FindHulls();
            }
        }

        public override bool Contains(Vector2 position)
        {
            return (Submarine.RectContains(rect, position) &&
                !Submarine.RectContains(new Rectangle(rect.X + 4, rect.Y - 4, rect.Width - 8, rect.Height - 8), position));
        }

        private void FindHulls()
        {
            Hull[] hulls = new Hull[2];

            linkedTo.Clear();

            foreach (Hull h in Hull.hullList)
            {
                if (!Submarine.RectsOverlap(h.Rect, rect, false)) continue;
                
                //if the gap is inside the hull completely, ignore it
                if (rect.X > h.Rect.X && rect.X + rect.Width < h.Rect.X+h.Rect.Width && 
                    rect.Y < h.Rect.Y && rect.Y - rect.Height > h.Rect.Y - h.Rect.Height) continue;

                for (int i = 0; i < 2; i++ )
                {
                    if (hulls[i] != null) continue;
                    hulls[i] = h;
                    break;
                }

                if (hulls[1] != null) break;
            }

            if (hulls[0] == null && hulls[1] == null) return;

            if (hulls[0]!=null && hulls[1]!=null)
            {
                if ((isHorizontal && hulls[0].Rect.X > hulls[1].Rect.X) || (!isHorizontal && hulls[0].Rect.Y < hulls[1].Rect.Y))
                {
                    //make sure that hull1 is the lefthand room if the gap is horizontal,
                    //or that hull1 is the upper hull if the gap is vertical
                    
                    Hull temp = hulls[0];
                    hulls[0] = hulls[1];
                    hulls[1] = temp;
           
                }
            }

            linkedTo.Add(hulls[0]);
            if (hulls[1] != null) linkedTo.Add(hulls[1]);

            //if (hull1 != null && hull2 != null)
            //{
            //    if (isHorizontal)
            //    {
            //        //make sure that water1 is the lefthand room
            //        //or that water2 is null if the gap doesn't lead to another room
            //        if (hull1.Rect.X < hull2.Rect.X)
            //        {
            //            linkedTo.Add(hull1);
            //            linkedTo.Add(hull2);
            //        }
            //        else
            //        {
            //            linkedTo.Add(hull2);
            //            linkedTo.Add(hull1);
            //        }
            //    }
            //    else
            //    {
            //        //make sure that water1 is the room on the top
            //        //or that water2 is null if the gap doesn't lead to another room
            //        if (hull1.Rect.Y > hull2.Rect.Y)
            //        {
            //            linkedTo.Add(hull1);
            //            linkedTo.Add(hull2);
            //        }
            //        else
            //        {
            //            linkedTo.Add(hull2);
            //            linkedTo.Add(hull1);
            //        }
            //    }
            //}
            //else
            //{
            //    linkedTo.Add(hull1);
            //}
        }

        public override void Draw(SpriteBatch sb, bool editing)
        {
            //if (linkedTo[0] != null)
            //    GUI.DrawLine(sb, new Vector2(Position.X, Position.Y),
            //         new Vector2(linkedTo[0].Position.X, linkedTo[0].Position.Y), Color.Blue);

            //if (linkedTo.Count > 1 && linkedTo[1] != null)
            //    GUI.DrawLine(sb, new Vector2(Position.X, Position.Y),
            //         new Vector2(linkedTo[1].Position.X, linkedTo[1].Position.Y), Color.Blue);


            //GUI.DrawLine(sb, new Vector2(Position.X, -Position.Y), new Vector2(Position.X, -Position.Y)+new Vector2(flowForce.X, -flowForce.Y), Color.LightBlue);

            if (!editing) return;

            Color clr = (open == 0.0f) ? Color.Red : Color.Cyan;

            GUI.DrawRectangle(sb, new Rectangle(rect.X, -rect.Y, rect.Width, rect.Height), clr);

            if (isSelected)
            {
                GUI.DrawRectangle(sb,
                    new Vector2(rect.X - 5, -rect.Y - 5),
                    new Vector2(rect.Width + 10, rect.Height + 10),
                    Color.Red);
            }

            //HUD.DrawLine(sb, new Vector2(position.X, -position.Y),
            //    isHorizontal ? new Vector2(position.X, -position.Y + size) : new Vector2(position.X + size, -position.Y),
            //    clr);
        }
        
        public override void Update(Camera cam, float deltaTime)
        {

            soundVolume = soundVolume + ((flowForce.Length() < 100.0f) ? -deltaTime * 0.5f : deltaTime * 0.5f);
            soundVolume = MathHelper.Clamp(soundVolume, 0.0f, 1.0f);

            int index = (int)Math.Floor(flowForce.Length() / 100.0f);
            index = Math.Min(index,2);

            soundIndex = AmbientSoundManager.flowSounds[index].Loop(soundIndex, soundVolume, Position, 2000.0f);
            //soundVolume = Math.Max(0.0f, soundVolume-deltaTime);
            //Sound.UpdatePosition(soundIndex, Position, 2000.0f);                
            

            flowForce = Vector2.Zero;

            if (open == 0.0f) return;

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

            if (FlowForce.Length() > 150.0f && flowTargetHull!=null && flowTargetHull.Volume < flowTargetHull.FullVolume)
            {
                //UpdateFlowForce();

                Vector2 pos = SimPosition;
                if (isHorizontal)
                {
                    pos.Y = ConvertUnits.ToSimUnits(MathHelper.Clamp(lowerSurface, rect.Y-rect.Height, rect.Y));

                    Game1.particleManager.CreateParticle("watersplash",
                        new Vector2(pos.X, pos.Y - ToolBox.RandomFloatLocal(0.0f, 0.1f)),
                        new Vector2(flowForce.X * ToolBox.RandomFloatLocal(0.005f, 0.007f), flowForce.Y * ToolBox.RandomFloatLocal(0.005f, 0.007f)));

                    pos.Y = ConvertUnits.ToSimUnits(ToolBox.RandomFloatLocal(lowerSurface, rect.Y - rect.Height));
                        Game1.particleManager.CreateParticle("bubbles", pos, flowForce / 200.0f);
                }
                else
                {
                    pos.Y += Math.Sign(flowForce.Y) * ConvertUnits.ToSimUnits(rect.Height / 2.0f);
                    for (int i = 0; i < rect.Width; i += (int)ToolBox.RandomFloatLocal(80, 100))
                    {
                        pos.X = ConvertUnits.ToSimUnits(ToolBox.RandomFloatLocal(rect.X, rect.X+rect.Width));
                        Subsurface.Particles.Particle splash = Game1.particleManager.CreateParticle("watersplash", pos,
                            new Vector2(flowForce.X * ToolBox.RandomFloatLocal(0.005f, 0.008f), flowForce.Y * ToolBox.RandomFloatLocal(0.005f, 0.008f)));

                        if (splash!=null) splash.Size = splash.Size * MathHelper.Clamp(rect.Width / 50.0f, 0.8f, 4.0f);

                        Game1.particleManager.CreateParticle("bubbles", pos, flowForce / 200.0f);
                    }
                }

            }
            
            
        }

        void UpdateRoomToRoom(float deltaTime)
        {
            if (linkedTo.Count < 2) return;
            Hull hull1 = (Hull)linkedTo[0];
            Hull hull2 = (Hull)linkedTo[1];

            if (hull1.Volume == 0.0 && hull2.Volume == 0.0) return;

            float size = (isHorizontal) ? rect.Height : rect.Width;

            //a variable affecting the water flow through the gap
            //the larger the gap is, the faster the water flows
            float sizeModifier = size / 100.0f * open;

            //horizontal gap (such as a regular door)
            if (isHorizontal)
            {
                //higherSurface = Math.Min(hull1.Surface,hull2.Surface);
                    float delta=0.0f;                
                //water level is above the lower boundary of the gap
                if (Math.Max(hull1.Surface+hull1.WaveY[hull1.WaveY.Length - 1], hull2.Surface+hull2.WaveY[0]) > rect.Y - size)
                {

                    int dir = (hull1.Pressure > hull2.Pressure) ? 1 : -1;

                    //water flowing from the righthand room to the lefthand room
                    if (dir == -1)
                    {
                        if (!(hull2.Volume > 0.0f)) return;
                        lowerSurface = hull1.Surface - hull1.WaveY[hull1.WaveY.Length - 1];
                        //delta = Math.Min((room2.water.pressure - room1.water.pressure) * sizeModifier, Math.Min(room2.water.Volume, room2.Volume));
                        //delta = Math.Min(delta, room1.Volume - room1.water.Volume + Water.MaxCompress);

                        flowTargetHull = hull1;

                        //make sure not to move more than what the room contains
                        delta = Math.Min((hull2.Pressure - hull1.Pressure) * sizeModifier, Math.Min(hull2.Volume, hull2.FullVolume));
                        
                        //make sure not to place more water to the target room than it can hold
                        delta = Math.Min(delta, hull1.FullVolume + Hull.MaxCompress - (hull1.Volume));
                        hull1.Volume += delta;
                        hull2.Volume -= delta;
                        if (hull1.Volume > hull1.FullVolume)
                        hull1.Pressure = Math.Max(hull1.Pressure, (hull1.Pressure + hull2.Pressure) / 2);

                        flowForce = new Vector2(-delta, 0.0f);
                    }
                    else if (dir == 1)
                    {
                        if (!(hull1.Volume > 0.0f)) return;
                        lowerSurface = hull2.Surface - hull2.WaveY[1];

                        flowTargetHull = hull2;

                        //make sure not to move more than what the room contains
                        delta = Math.Min((hull1.Pressure - hull2.Pressure) * sizeModifier, Math.Min(hull1.Volume, hull1.FullVolume));

                        //make sure not to place more water to the target room than it can hold
                        delta = Math.Min(delta, hull2.FullVolume + Hull.MaxCompress - (hull2.Volume));
                        hull1.Volume -= delta;
                        hull2.Volume += delta;
                        if (hull2.Volume > hull2.FullVolume)
                        hull2.Pressure = Math.Max(hull2.Pressure, (hull1.Pressure + hull2.Pressure) / 2);
                        
                        flowForce = new Vector2(delta, 0.0f);
                    }

                    if (delta>100.0f)
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
                if (hull2.Pressure > hull1.Pressure)
                {
                    float delta = Math.Min(hull2.Volume - hull2.FullVolume + Hull.MaxCompress / 2.0f, deltaTime * 8000.0f * sizeModifier);

                    flowForce = new Vector2(0.0f, Math.Min(hull2.Pressure - hull1.Pressure, 500.0f));

                    delta = Math.Max(delta, 0.0f);
                    hull1.Volume += delta;
                    hull2.Volume -= delta;

                    flowTargetHull = hull1;

                    //delta = (water2.Pressure - water1.Pressure) * 0.1f;
                    //if (delta > 0.1f)
                    //{
                    //    int posX = (int)((rect.X + size / 2.0f - water1.Rect.X) / Hull.WaveWidth);
                    //    //water1.WaveY[posX] = delta;
                    //    water1.WaveVel[posX] = delta * 0.01f;
                    //}
                    
                    if (hull1.Volume > hull1.FullVolume)
                    {
                        hull1.Pressure = Math.Max(hull1.Pressure, (hull1.Pressure + hull2.Pressure) / 2);
                    }

                    

                }
                //there's water in the upper room, drop to lower
                else if (hull1.Volume > 0)
                {
                    flowTargetHull = hull2;

                    //make sure the amount of water moved isn't more than what the room contains
                    float delta = Math.Min(hull1.Volume, deltaTime * 10000f * sizeModifier);
                    //make sure not to place more water to the target room than it can hold
                    delta = Math.Min(delta, (hull2.FullVolume + Math.Max(hull1.Volume - hull1.FullVolume, 0.0f)) - hull2.Volume + Hull.MaxCompress / 4.0f);
                    
                    hull1.Volume -= delta;
                    hull2.Volume += delta;

                    if (hull2.Volume > hull2.FullVolume)
                    {
                        hull2.Pressure = Math.Max(hull2.Pressure, (hull1.Pressure + hull2.Pressure) / 2);
                    }

                    flowForce = new Vector2(0.0f,-delta);

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
                if (hull1.Volume>hull1.FullVolume && hull2.Volume>hull2.FullVolume)
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
            float sizeModifier = size * open;

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
                    hull1.Surface > -rect.Y)
                {
                    float vel = (rect.Y + hull1.Surface) * 0.03f;

                    if (rect.X > hull1.Rect.X + hull1.Rect.Width / 2.0f)
                    {
                        hull1.WaveVel[hull1.WaveY.Length - 1] += vel;
                        hull1.WaveVel[hull1.WaveY.Length - 2] += vel;
                    }
                    else
                    {
                        hull1.WaveVel[0] += vel;
                        hull1.WaveVel[1] += vel;
                    }
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
            }
        }

        private void UpdateOxygen()
        {
            if (linkedTo.Count < 2) return;
            Hull hull1 = (Hull)linkedTo[0];
            Hull hull2 = (Hull)linkedTo[1];

            float totalOxygen = hull1.Oxygen + hull2.Oxygen;
            float totalVolume = (hull1.FullVolume + hull2.FullVolume);

            hull1.Oxygen += Math.Sign(totalOxygen * hull1.FullVolume / (totalVolume) - hull1.Oxygen) * Hull.OxygenDistributionSpeed;
            hull2.Oxygen += Math.Sign(totalOxygen * hull2.FullVolume / (totalVolume) - hull2.Oxygen) * Hull.OxygenDistributionSpeed;            
        }

        public override void Remove()
        {
            base.Remove();

            if (soundIndex > -1) Sounds.SoundManager.Stop(soundIndex);
        }

        public override XElement Save(XDocument doc)
        {
            XElement element = new XElement("Gap");

            element.Add(new XAttribute("ID", ID),
                new XAttribute("x", rect.X),
                new XAttribute("y", rect.Y),
                new XAttribute("width", rect.Width),
                new XAttribute("height", rect.Height));

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

            doc.Root.Add(element);

            return element;
        }


        public static void Load(XElement element)
        {
            Rectangle rect = new Rectangle(
                int.Parse(element.Attribute("x").Value),
                int.Parse(element.Attribute("y").Value),
                int.Parse(element.Attribute("width").Value),
                int.Parse(element.Attribute("height").Value));

            Gap g = new Gap(rect);
            g.ID = int.Parse(element.Attribute("ID").Value);
            
            g.linkedToID = new List<int>();
            //int i = 0;
            //while (element.Attribute("linkedto" + i) != null)
            //{
            //    g.linkedToID.Add(int.Parse(element.Attribute("linkedto" + i).Value));
            //    i += 1;
            //}
        }
    }
}
