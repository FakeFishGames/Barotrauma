using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Xml.Linq;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Lidgren.Network;

namespace Barotrauma
{

    class Hull : MapEntity
    {
        public static List<Hull> hullList = new List<Hull>();
        private static EntityGrid entityGrid;

        public static bool ShowHulls = true;

        public static bool EditWater, EditFire;

        public static WaterRenderer renderer;

        private List<FireSource> fireSources;
        
        public const float OxygenDistributionSpeed = 500.0f;
        public const float OxygenDetoriationSpeed = 0.3f;
        public const float OxygenConsumptionSpeed = 1000.0f;

        public const int WaveWidth = 16;
        const float WaveStiffness = 0.003f;
        const float WaveSpread = 0.05f;
        const float WaveDampening = 0.01f;

        //how much excess water the room can contain  (= more than the volume of the room)
        public const float MaxCompress = 10000f;
        
        public readonly Dictionary<string, PropertyDescriptor> properties;

        private float lethalPressure;

        private float surface;
        private float volume;
        private float pressure;

        private float oxygen;

        private bool update;

        float[] waveY; //displacement from the surface of the water
        float[] waveVel; //velocity of the point

        float[] leftDelta;
        float[] rightDelta;

        float lastSentVolume;

        public override string Name
        {
            get
            {
                return "Hull";
            }
        }

        public override bool IsLinkable
        {
            get { return true; }
        }

        public float LethalPressure
        {
            get { return lethalPressure; }
            set { lethalPressure = MathHelper.Clamp(value, 0.0f, 100.0f); }
        }

        public Vector2 Size
        {
            get { return new Vector2(rect.Width, rect.Height); }
        }

        public float Surface
        {
            get { return surface; }
        }

        public float Volume
        {
            get { return volume; }
            set
            {
                if (!MathUtils.IsValid(value)) return;
                volume = MathHelper.Clamp(value, 0.0f, FullVolume + MaxCompress);
                if (volume < FullVolume) Pressure = rect.Y - rect.Height + volume / rect.Width;
                if (volume > 0.0f) update = true;
            }
        }

        [HasDefaultValue(90.0f, true)]
        public float Oxygen
        {
            get { return oxygen; }
            set { oxygen = MathHelper.Clamp(value, 0.0f, FullVolume); }
        }

        public float OxygenPercentage
        {
            get { return oxygen / FullVolume * 100.0f; }
            set { Oxygen = (value / 100.0f) * FullVolume; }
        }

        public float FullVolume
        {
            get { return (rect.Width * rect.Height); }
        }

        public float Pressure
        {
            get { return pressure; }
            set { pressure = value; }
        }

        public float[] WaveY
        {
            get { return waveY; }
        }

        public float[] WaveVel
        {
            get { return waveVel; }
        }

        public List<FireSource> FireSources
        {
            get { return fireSources; }
        }

        public Hull(MapEntityPrefab prefab, Rectangle rectangle)
            : this (rectangle, Submarine.Loaded)
        {

        }

        public Hull(Rectangle rectangle, Submarine submarine)
            : base (submarine)
        {
            rect = rectangle;
            
            OxygenPercentage = Rand.Range(90.0f, 100.0f, false);

            fireSources = new List<FireSource>();

            properties = TypeDescriptor.GetProperties(GetType())
                .Cast<PropertyDescriptor>()
                .ToDictionary(pr => pr.Name);

            int arraySize = (rectangle.Width / WaveWidth + 1);
            waveY = new float[arraySize];
            waveVel = new float[arraySize];

            leftDelta = new float[arraySize];
            rightDelta = new float[arraySize];

            surface = rect.Y - rect.Height;

            aiTarget = new AITarget(this);
            aiTarget.SightRange = (rect.Width + rect.Height)*5.0f;

            hullList.Add(this);

            Item.UpdateHulls();
            Gap.UpdateHulls();

            Volume = 0.0f;


            InsertToList();
        }

        public static Rectangle GetBorders()
        {
            if (!hullList.Any()) return Rectangle.Empty;

            Rectangle rect = hullList[0].rect;
            
            foreach (Hull hull in hullList)
            {
                if (hull.Rect.X < rect.X)
                {
                    rect.Width += rect.X - hull.rect.X;
                    rect.X = hull.rect.X;

                }
                if (hull.rect.Right > rect.Right) rect.Width = hull.rect.Right - rect.X;

                if (hull.rect.Y > rect.Y)
                {
                    rect.Height += hull.rect.Y - rect.Y;

                    rect.Y = hull.rect.Y;
                }
                if (hull.rect.Y - hull.rect.Height < rect.Y - rect.Height) rect.Height = rect.Y - (hull.rect.Y - hull.rect.Height);
            }

            return rect;
        }

        public static void GenerateEntityGrid()
        {
            entityGrid = new EntityGrid(Submarine.Borders, 200.0f);
            
            foreach (Hull hull in hullList)
            {
                entityGrid.InsertEntity(hull);
            }
        }

        public override bool IsMouseOn(Vector2 position)
        {
            if (!GameMain.DebugDraw && !ShowHulls) return false;

            return (Submarine.RectContains(WorldRect, position) &&
                !Submarine.RectContains(MathUtils.ExpandRect(WorldRect, -8), position));
        }

        public int GetWaveIndex(Vector2 position)
        {
            return GetWaveIndex(position.X);
        }

        public int GetWaveIndex(float xPos)
        {
            int index = (int)(xPos - rect.X) / WaveWidth;
            index = (int)MathHelper.Clamp(index, 0, waveY.Length - 1);
            return index;
        }

        public override void Move(Vector2 amount)
        {
            rect.X += (int)amount.X;
            rect.Y += (int)amount.Y;

            Item.UpdateHulls();
            Gap.UpdateHulls();
        }

        public override void Remove()
        {
            base.Remove();

            Item.UpdateHulls();
            Gap.UpdateHulls();

            List<FireSource> fireSourcesToRemove = new List<FireSource>(fireSources);
            foreach (FireSource fireSource in fireSourcesToRemove)
            {
                fireSource.Remove();
            }

            //renderer.Dispose();

            if (entityGrid!=null) entityGrid.RemoveEntity(this);

            hullList.Remove(this);
        }

        public void AddFireSource(FireSource fireSource, bool createNetworkEvent = true)
        {
            fireSources.Add(fireSource);
            if (createNetworkEvent)
            {
                new Networking.NetworkEvent(Networking.NetworkEventType.ImportantEntityUpdate, this.ID, false);
            }
        }

        public override void Update(Camera cam, float deltaTime)
        {
            Oxygen -= OxygenDetoriationSpeed * deltaTime;

            if (EditWater)
            {
                Vector2 position = cam.ScreenToWorld(PlayerInput.MousePosition);
                if (Submarine.RectContains(WorldRect, position))
                {
                    if (PlayerInput.LeftButtonDown())
                    {
                        //waveY[GetWaveIndex(position.X - rect.X - Submarine.Position.X) / WaveWidth] = 100.0f;
                        Volume = Volume + 1500.0f;
                    }
                    else if (PlayerInput.RightButtonDown())
                    {
                        Volume = Volume - 1500.0f;
                    }
                }
            }
            else if (EditFire)
            {
                Vector2 position = cam.ScreenToWorld(PlayerInput.MousePosition);
                if (Submarine.RectContains(WorldRect, position))
                {
                    if (PlayerInput.LeftButtonClicked())
                    {
                        new FireSource(position);
                    }
                }
            }

            FireSource.UpdateAll(fireSources, deltaTime);
            
            //update client hulls if the amount of water has changed by >10%
            if (Math.Abs(lastSentVolume - volume) > FullVolume * 0.1f)
            {
                new Networking.NetworkEvent(ID, false);
                lastSentVolume = volume;
            }
            if (!update) return;

            float surfaceY = rect.Y - rect.Height + Volume / rect.Width;
            for (int i = 0; i < waveY.Length; i++)
            {
                float maxDelta = Math.Max(Math.Abs(rightDelta[i]), Math.Abs(leftDelta[i]));
                if (maxDelta > Rand.Range(1.0f,10.0f))
                {
                    Vector2 particlePos = new Vector2(rect.X + WaveWidth * i, surface + waveY[i]);
                    if (Submarine != null) particlePos += Submarine.Position;

                    GameMain.ParticleManager.CreateParticle("mist",
                        particlePos,
                        new Vector2(0.0f, -50.0f), 0.0f, this);
                }

                waveY[i] = waveY[i] + waveVel[i];

                if (surfaceY + waveY[i] > rect.Y)
                {
                    waveY[i] -= (surfaceY + waveY[i]) - rect.Y;
                    waveVel[i] = waveVel[i] * -0.5f;
                }
                else if (surfaceY + waveY[i] < rect.Y - rect.Height)
                {
                    waveY[i] -= (surfaceY + waveY[i]) - (rect.Y - rect.Height);
                    waveVel[i] = waveVel[i] * -0.5f;
                }


                //acceleration
                float a = -WaveStiffness * waveY[i] - waveVel[i] * WaveDampening;
                waveVel[i] = waveVel[i] + a;

            }

            for (int j = 0; j < 2; j++)
            {
                for (int i = 1; i < waveY.Length - 1; i++)
                {
                    leftDelta[i] = WaveSpread * (waveY[i] - waveY[i - 1]);
                    waveVel[i - 1] = waveVel[i - 1] + leftDelta[i];

                    rightDelta[i] = WaveSpread * (waveY[i] - waveY[i + 1]);
                    waveVel[i + 1] = waveVel[i + 1] + rightDelta[i];
                }

                for (int i = 1; i < waveY.Length - 1; i++)
                {
                    waveY[i - 1] = waveY[i - 1] + leftDelta[i];
                    waveY[i + 1] = waveY[i + 1] + rightDelta[i];
                }
            }

            if (volume < FullVolume)
            {
                LethalPressure -= 10.0f * deltaTime;
                if (Volume == 0.0f)
                {
                    for (int i = 1; i < waveY.Length - 1; i++)
                    {
                        if (waveY[i] > 0.1f) return;
                    }
                    update = false;
                }
            }
            else
            {
                
                LethalPressure += ( Submarine.Loaded!=null && Submarine.Loaded.AtDamageDepth) ? 100.0f*deltaTime : 10.0f * deltaTime;
            }
        }

        public void Extinquish(float deltaTime, float amount, Vector2 position)
        {
            for (int i = fireSources.Count - 1; i >= 0; i-- )
            {
                fireSources[i].Extinquish(deltaTime, amount, position);
            }
        }

        public void RemoveFire(FireSource fire)
        {
            fireSources.Remove(fire);
            new Networking.NetworkEvent(Networking.NetworkEventType.ImportantEntityUpdate, this.ID, false);
        }

        public override void Draw(SpriteBatch spriteBatch, bool editing, bool back = true)
        {
            if (!ShowHulls && !GameMain.DebugDraw) return;

            if (!editing && !GameMain.DebugDraw) return;

            Rectangle drawRect =
            Submarine == null ? rect : new Rectangle((int)(Submarine.DrawPosition.X + rect.X), (int)(Submarine.DrawPosition.Y + rect.Y), rect.Width, rect.Height);

            GUI.DrawRectangle(spriteBatch,
                new Vector2(drawRect.X, -drawRect.Y),
                new Vector2(rect.Width, rect.Height),
                Color.Blue);

            GUI.DrawRectangle(spriteBatch,
                new Rectangle(drawRect.X, -drawRect.Y, rect.Width, rect.Height),
                Color.Red*((100.0f-OxygenPercentage)/400.0f), true);

            spriteBatch.DrawString(GUI.Font, "Pressure: " + ((int)pressure - rect.Y).ToString() +
                " - Oxygen: " + ((int)OxygenPercentage), new Vector2(drawRect.X + 10, -drawRect.Y + 10), Color.Black);
            spriteBatch.DrawString(GUI.Font, volume + " / " + FullVolume, new Vector2(drawRect.X + 10, -drawRect.Y + 30), Color.Black);

            if ((isSelected || isHighlighted) && editing)
            {
                GUI.DrawRectangle(spriteBatch,
                    new Vector2(drawRect.X + 5, -drawRect.Y + 5),
                    new Vector2(rect.Width - 10, rect.Height - 10),
                    isHighlighted ? Color.LightBlue*0.5f : Color.Red*0.5f, true);
            }
        }

        public void Render(GraphicsDevice graphicsDevice, Camera cam)
        {
            if (renderer.PositionInBuffer > renderer.vertices.Length - 6) return;

            //calculate where the surface should be based on the water volume
            float top = rect.Y+Submarine.DrawPosition.Y;
            float bottom = top - rect.Height;
            float surfaceY = bottom + Volume / rect.Width;

            //interpolate the position of the rendered surface towards the "target surface"
            surface = surface + ((surfaceY - Submarine.DrawPosition.Y) - surface) / 10.0f;
            float drawSurface = surface + Submarine.DrawPosition.Y;

            Matrix transform =  cam.Transform * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 1) * 0.5f;

            if (bottom > cam.WorldView.Y || top < cam.WorldView.Y - cam.WorldView.Height) return;

            if (!update)
            {
                // create the four corners of our triangle.

                Vector3[] corners = new Vector3[4];

                corners[0] = new Vector3(rect.X, rect.Y, 0.0f);
                corners[1] = new Vector3(rect.X + rect.Width, rect.Y, 0.0f);

                corners[2] = new Vector3(corners[1].X, rect.Y-rect.Height, 0.0f);
                corners[3] = new Vector3(corners[0].X, corners[2].Y, 0.0f);

                Vector2[] uvCoords = new Vector2[4];
                for (int i = 0; i < 4; i++ )
                {
                    corners[i] += new Vector3(Submarine.DrawPosition, 0.0f);
                    uvCoords[i] = Vector2.Transform(new Vector2(corners[i].X, -corners[i].Y), transform);                    
                }

                renderer.vertices[renderer.PositionInBuffer] = new VertexPositionTexture(corners[0], uvCoords[0]);
                renderer.vertices[renderer.PositionInBuffer + 1] = new VertexPositionTexture(corners[1], uvCoords[1]);
                renderer.vertices[renderer.PositionInBuffer + 2] = new VertexPositionTexture(corners[2], uvCoords[2]);

                renderer.vertices[renderer.PositionInBuffer + 3] = new VertexPositionTexture(corners[0], uvCoords[0]);
                renderer.vertices[renderer.PositionInBuffer + 4] = new VertexPositionTexture(corners[2], uvCoords[2]);
                renderer.vertices[renderer.PositionInBuffer + 5] = new VertexPositionTexture(corners[3], uvCoords[3]);

                renderer.PositionInBuffer += 6;

                return;
            }

            float x = rect.X + Submarine.DrawPosition.X;
            int start = (int)Math.Floor((cam.WorldView.X - x) / WaveWidth);
            start = Math.Max(start, 0);

            int end = (waveY.Length - 1)
                - (int)Math.Floor((float)((x + rect.Width) - (cam.WorldView.X + cam.WorldView.Width)) / WaveWidth);
            end = Math.Min(end, waveY.Length - 1);

            x += start * WaveWidth;
            
            for (int i = start; i < end; i++)
            {
                if (renderer.PositionInBuffer > renderer.vertices.Length - 6) return;

                Vector3[] corners = new Vector3[4];

                corners[0] = new Vector3(x, top, 0.0f);
                corners[3] = new Vector3(corners[0].X, drawSurface + waveY[i], 0.0f);

                //skip adjacent "water rects" if the surface of the water is roughly at the same position
                int width = WaveWidth;
                while (i < end - 1 && Math.Abs(waveY[i + 1] - waveY[i]) < 1.0f)
                {
                    width += WaveWidth;
                    i++;
                }

                corners[1] = new Vector3(x + width, top, 0.0f);
                corners[2] = new Vector3(corners[1].X, drawSurface + waveY[i + 1], 0.0f);
                
                Vector2[] uvCoords = new Vector2[4];
                for (int n = 0; n < 4; n++)
                {
                    uvCoords[n] = Vector2.Transform(new Vector2(corners[n].X, -corners[n].Y), transform);
                }

                renderer.vertices[renderer.PositionInBuffer] = new VertexPositionTexture(corners[0], uvCoords[0]);
                renderer.vertices[renderer.PositionInBuffer + 1] = new VertexPositionTexture(corners[1], uvCoords[1]);
                renderer.vertices[renderer.PositionInBuffer + 2] = new VertexPositionTexture(corners[2], uvCoords[2]);

                renderer.vertices[renderer.PositionInBuffer + 3] = new VertexPositionTexture(corners[0], uvCoords[0]);
                renderer.vertices[renderer.PositionInBuffer + 4] = new VertexPositionTexture(corners[2], uvCoords[2]);
                renderer.vertices[renderer.PositionInBuffer + 5] = new VertexPositionTexture(corners[3], uvCoords[3]);

                renderer.PositionInBuffer += 6;

                x += width;
            }           

        }

        //returns the water block which contains the point (or null if it isn't inside any)
        public static Hull FindHull(Vector2 position, Hull guess = null, bool useWorldCoordinates = true)
        {
            if (entityGrid == null) return null;

            if (guess != null)
            {
                if (Submarine.RectContains(useWorldCoordinates ? guess.WorldRect : guess.rect, position)) return guess;
            }

            var entities = entityGrid.GetEntities(
                useWorldCoordinates && Submarine.Loaded!=null ? position-Submarine.Loaded.Position : position);

            foreach (Hull hull in entities)
            {
                if (Submarine.RectContains(useWorldCoordinates ? hull.WorldRect : hull.rect, position)) return hull;
            }

            return null;
        }

        //returns the water block which contains the point (or null if it isn't inside any)
        public static Hull FindHullOld(Vector2 position, Hull guess = null, bool useWorldCoordinates = true)
        {
            return FindHullOld(position, hullList, guess, useWorldCoordinates);
        }

        public static Hull FindHullOld(Vector2 position, List<Hull> hulls, Hull guess = null, bool useWorldCoordinates = true)
        {
            if (guess != null && hulls.Contains(guess))
            {
                if (Submarine.RectContains(useWorldCoordinates ? guess.WorldRect : guess.rect, position)) return guess;
            }

            foreach (Hull hull in hulls)
            {
                if (Submarine.RectContains(useWorldCoordinates ? hull.WorldRect : hull.rect, position)) return hull;
            }

            return null;
        }

        public List<Gap> FindGaps()
        {
            List<Gap> gaps = new List<Gap>();
            
            foreach (Gap gap in Gap.GapList)
            {
                if (gap.Open < 0.01f) continue;

                if (gap.linkedTo.Contains(this)) gaps.Add(gap);
            }

            return gaps;
        }

        public override XElement Save(XDocument doc)
        {
            XElement element = new XElement("Hull");

            element.Add
            (
                new XAttribute("ID", ID),                
                new XAttribute("rect",
                    (int)(rect.X - Submarine.HiddenSubPosition.X) + "," +
                    (int)(rect.Y - Submarine.HiddenSubPosition.Y) + "," +
                    rect.Width + "," + rect.Height),
                new XAttribute("water", volume)
            );
            
            doc.Root.Add(element);

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

            Hull h = new Hull(rect, submarine);

            h.volume = ToolBox.GetAttributeFloat(element, "pressure", 0.0f);

            h.ID = (ushort)int.Parse(element.Attribute("ID").Value);
        }

        public override bool FillNetworkData(Networking.NetworkEventType type, NetBuffer message, object data)
        {            
            message.WriteRangedSingle(MathHelper.Clamp(volume/FullVolume, 0.0f, 1.5f), 0.0f, 1.5f, 6);

            message.Write((byte)fireSources.Count, 4);
            foreach (FireSource fireSource in fireSources)
            {
                Vector2 normalizedPos = new Vector2(
                    (fireSource.Position.X - rect.X) / rect.Width, 
                    (fireSource.Position.Y - (rect.Y - rect.Height))/rect.Height);
                message.WriteRangedSingle(MathHelper.Clamp(normalizedPos.X, 0.0f, 1.0f), 0.0f, 1.0f, 4);
                message.WriteRangedSingle(MathHelper.Clamp(normalizedPos.Y, 0.0f, 1.0f), 0.0f, 1.0f, 4);
            }

            return true;
        }

        public override void ReadNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetIncomingMessage message, float sendingTime, out object data)
        {
            data = null;

            float newVolume = this.volume;

            try
            {
                float newPercentage = message.ReadRangedSingle(0.0f, 1.5f, 6);
                newVolume = newPercentage * FullVolume;
            }

            catch
            {
                return;
            }

            Volume = newVolume;

            int fireSourceCount = message.ReadByte(4);

            List<FireSource> newFireSources = new List<FireSource>();
            for (int i = 0; i < fireSourceCount; i++)
            {
                Vector2 pos = Vector2.Zero;
                pos.X = message.ReadRangedSingle(0.0f, 1.0f, 4);
                pos.Y = message.ReadRangedSingle(0.0f, 1.0f, 4);
                if (!MathUtils.IsValid(pos)) continue;

                pos.X = MathHelper.Clamp(pos.X, 0.05f, 0.95f);
                pos.Y = MathHelper.Clamp(pos.Y, 0.05f, 0.95f);

                pos = new Vector2(rect.X + rect.Width * pos.X, rect.Y - rect.Height + (rect.Height * pos.Y));

                var existingFire = fireSources.Find(fs => fs.Contains(pos));
                if (existingFire!=null)
                {
                    newFireSources.Add(existingFire);
                    existingFire.Position = pos;
                }
                else
                {
                    var newFire = new FireSource(pos, this, true);

                    //ignore if the fire wasn't added to this room (invalid position)?
                    if (!fireSources.Contains(newFire)) continue;
                    newFireSources.Add(newFire);
                }
            }

            var toBeRemoved = fireSources.FindAll(fs => !newFireSources.Contains(fs));
            for (int i = toBeRemoved.Count - 1; i >= 0; i--)
            {
                toBeRemoved[i].Remove(true);
            }
            fireSources = newFireSources;
        }
    

    }
}
