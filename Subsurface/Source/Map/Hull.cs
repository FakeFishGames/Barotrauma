using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Xml.Linq;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Lidgren.Network;

namespace Barotrauma
{

    class Hull : MapEntity, IPropertyObject
    {
        public static List<Hull> hullList = new List<Hull>();
        private static List<EntityGrid> entityGrids = new List<EntityGrid>();
        public static List<EntityGrid> EntityGrids
        {
            get
            {
                return entityGrids;
            }
        }

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
        
        public readonly Dictionary<string, ObjectProperty> properties;
        public Dictionary<string, ObjectProperty> ObjectProperties
        {
            get { return properties; }
        }

        private float lethalPressure;

        private float surface;
        private float volume;
        private float pressure;

        private float oxygen;

        private bool update;

        public bool Visible = true;

        private Sound currentFlowSound;
        private int soundIndex;
        private float soundVolume;

        float[] waveY; //displacement from the surface of the water
        float[] waveVel; //velocity of the point

        float[] leftDelta;
        float[] rightDelta;

        private float lastSentVolume, lastSentOxygen;
        private float lastNetworkUpdate;
        
        public List<Gap> ConnectedGaps;

        public override string Name
        {
            get
            {
                return "Hull";
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

                if (Submarine == null || !Submarine.Loading)
                {
                    Item.UpdateHulls();
                    Gap.UpdateHulls();
                }

                surface = rect.Y - rect.Height + Volume / rect.Width;
                Pressure = surface;
            }
        }

        public override bool SelectableInEditor
        {
            get
            {
                return ShowHulls;
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
            set 
            {
                if (!MathUtils.IsValid(value)) return;
                oxygen = MathHelper.Clamp(value, 0.0f, FullVolume); 
            }
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
            : this (prefab, rectangle, Submarine.MainSub)
        {

        }

        public Hull(MapEntityPrefab prefab, Rectangle rectangle, Submarine submarine)
            : base (prefab, submarine)
        {
            rect = rectangle;
            
            OxygenPercentage = Rand.Range(90.0f, 100.0f, false);

            fireSources = new List<FireSource>();

            properties = ObjectProperty.GetProperties(this);

            int arraySize = (rectangle.Width / WaveWidth + 1);
            waveY = new float[arraySize];
            waveVel = new float[arraySize];

            leftDelta = new float[arraySize];
            rightDelta = new float[arraySize];

            surface = rect.Y - rect.Height;

            aiTarget = new AITarget(this);

            hullList.Add(this);

            ConnectedGaps = new List<Gap>();
            
            if (submarine==null || !submarine.Loading)
            {
                Item.UpdateHulls();
                Gap.UpdateHulls();
            }

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
        
        public static EntityGrid GenerateEntityGrid(Submarine submarine)
        {
            var newGrid = new EntityGrid(submarine, 200.0f);

            entityGrids.Add(newGrid);
            
            foreach (Hull hull in hullList)
            {
                if (hull.Submarine == submarine) newGrid.InsertEntity(hull);
            }
            return newGrid;
        }

        public void AddToGrid(Submarine submarine)
        {
            foreach (EntityGrid grid in entityGrids)
            {
                if (grid.Submarine != submarine) continue;

                rect.Location -= submarine.HiddenSubPosition.ToPoint();
                
                grid.InsertEntity(this);

                rect.Location += submarine.HiddenSubPosition.ToPoint();
                return;
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

            if (Submarine==null || !Submarine.Loading)
            {
                Item.UpdateHulls();
                Gap.UpdateHulls();
            }

            surface = rect.Y - rect.Height + Volume / rect.Width;
            Pressure = surface;
        }

        public override void Remove()
        {
            base.Remove();
            hullList.Remove(this);

            if (Submarine == null || !Submarine.Loading)
            {
                Item.UpdateHulls();
                Gap.UpdateHulls();
            }

            List<FireSource> fireSourcesToRemove = new List<FireSource>(fireSources);
            foreach (FireSource fireSource in fireSourcesToRemove)
            {
                fireSource.Remove();
            }

            if (soundIndex > -1)
            {
                Sounds.SoundManager.Stop(soundIndex);
                soundIndex = -1;
            }

            //renderer.Dispose();
            if (entityGrids != null)
            {
                foreach (EntityGrid entityGrid in entityGrids)
                {
                    entityGrid.RemoveEntity(this);
                }
            }


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
                    if (PlayerInput.LeftButtonHeld())
                    {
                        //waveY[GetWaveIndex(position.X - rect.X - Submarine.Position.X) / WaveWidth] = 100.0f;
                        Volume = Volume + 1500.0f;
                    }
                    else if (PlayerInput.RightButtonHeld())
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

            aiTarget.SightRange = Submarine == null ? 0.0f : Math.Max(Submarine.Velocity.Length() * 500.0f, 500.0f);
            aiTarget.SoundRange -= deltaTime * 1000.0f;

            float strongestFlow = 0.0f;
            foreach (Gap gap in ConnectedGaps)
            {
                if (gap.IsRoomToRoom)
                {
                    //only the first linked hull plays the flow sound
                    if (gap.linkedTo[1] == this) continue;
                }

                float gapFlow = gap.LerpedFlowForce.Length();
                
                if (gapFlow > strongestFlow)
                {
                    strongestFlow = gapFlow;
                }
            }
            
            if (strongestFlow > 1.0f)
            {
                soundVolume = soundVolume + ((strongestFlow < 100.0f) ? -deltaTime * 0.5f : deltaTime * 0.5f);
                soundVolume = MathHelper.Clamp(soundVolume, 0.0f, 1.0f);

                int index = (int)Math.Floor(strongestFlow / 100.0f);
                index = Math.Min(index, 2);

                var flowSound = SoundPlayer.flowSounds[index];
                if (flowSound != currentFlowSound && soundIndex > -1)
                {
                    Sounds.SoundManager.Stop(soundIndex);
                    currentFlowSound = null;
                    soundIndex = -1;
                }

                currentFlowSound = flowSound;
                soundIndex = currentFlowSound.Loop(soundIndex, soundVolume, WorldPosition, Math.Min(strongestFlow*5.0f, 2000.0f));
            }
            else
            {
                if (soundIndex > -1)
                {
                    Sounds.SoundManager.Stop(soundIndex);
                    currentFlowSound = null;
                    soundIndex = -1;
                }
            }
            
            //update client hulls if the amount of water has changed by >10%
            //or if oxygen percentage has changed by 5%
            if (Math.Abs(lastSentVolume - volume) > FullVolume * 0.1f ||
                Math.Abs(lastSentOxygen - OxygenPercentage) > 5f)
            {
                new Networking.NetworkEvent(ID, false);                
            }
            
            if (!update)
            {
                lethalPressure = 0.0f;
                return;
            }

            float surfaceY = rect.Y - rect.Height + Volume / rect.Width;
            for (int i = 0; i < waveY.Length; i++)
            {
                float maxDelta = Math.Max(Math.Abs(rightDelta[i]), Math.Abs(leftDelta[i]));
                if (maxDelta > Rand.Range(1.0f,10.0f))
                {
                    var particlePos = new Vector2(rect.X + WaveWidth * i, surface + waveY[i]);
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

            //interpolate the position of the rendered surface towards the "target surface"
            surface = Math.Max(MathHelper.Lerp(surface, surfaceY, deltaTime*10.0f), rect.Y - rect.Height);

            if (volume < FullVolume)
            {
                LethalPressure -= 10.0f * deltaTime;
                if (Volume == 0.0f)
                {
                    //wait for the surface to be lerped back to bottom and the waves to settle until disabling update
                    if (surface > rect.Y - rect.Height + 1) return;
                    for (int i = 1; i < waveY.Length - 1; i++)
                    {
                        if (waveY[i] > 0.1f) return;
                    }

                    update = false;
                }
            }
        }

        public void ApplyFlowForces(float deltaTime, Item item)
        {
            foreach (var gap in ConnectedGaps.Where(gap => gap.Open > 0))
            {
                //var pos = gap.Position - body.Position;
                var distance = MathHelper.Max(Vector2.DistanceSquared(item.Position, gap.Position)/1000, 1f);
               
                //pos.Normalize();
                item.body.ApplyForce((gap.LerpedFlowForce/distance) * deltaTime);
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
            //if (back) return;
            Rectangle drawRect;
            if (!Visible)
            {
                drawRect =
                    Submarine == null ? rect : new Rectangle((int)(Submarine.DrawPosition.X + rect.X), (int)(Submarine.DrawPosition.Y + rect.Y), rect.Width, rect.Height);

                GUI.DrawRectangle(spriteBatch,
                    new Vector2(drawRect.X, -drawRect.Y),
                    new Vector2(rect.Width, rect.Height),
                    Color.Black,true,
                    0, (int)Math.Max((1.5f / GameScreen.Selected.Cam.Zoom), 1.0f));
            }

            if (!ShowHulls && !GameMain.DebugDraw) return;

            if (!editing && !GameMain.DebugDraw) return;
            
            if (aiTarget != null) aiTarget.Draw(spriteBatch);

            drawRect =
                Submarine == null ? rect : new Rectangle((int)(Submarine.DrawPosition.X + rect.X), (int)(Submarine.DrawPosition.Y + rect.Y), rect.Width, rect.Height);

            GUI.DrawRectangle(spriteBatch,
                new Vector2(drawRect.X, -drawRect.Y),
                new Vector2(rect.Width, rect.Height),
                Color.Blue, false, 0, (int)Math.Max((1.5f / Screen.Selected.Cam.Zoom), 1.0f));

            GUI.DrawRectangle(spriteBatch,
                new Rectangle(drawRect.X, -drawRect.Y, rect.Width, rect.Height),
                Color.Red * ((100.0f - OxygenPercentage) / 400.0f), true, 0, (int)Math.Max((1.5f / GameScreen.Selected.Cam.Zoom), 1.0f));

            if (GameMain.DebugDraw)
            {
                spriteBatch.DrawString(GUI.SmallFont, "Pressure: " + ((int)pressure - rect.Y).ToString() +
                    " - Oxygen: " + ((int)OxygenPercentage), new Vector2(drawRect.X + 5, -drawRect.Y + 5), Color.White);
                spriteBatch.DrawString(GUI.SmallFont, volume + " / " + FullVolume, new Vector2(drawRect.X + 5, -drawRect.Y + 20), Color.White);

            }

            if ((isSelected || isHighlighted) && editing)
            {
                GUI.DrawRectangle(spriteBatch,
                    new Vector2(drawRect.X + 5, -drawRect.Y + 5),
                    new Vector2(rect.Width - 10, rect.Height - 10),
                    isHighlighted ? Color.LightBlue * 0.5f : Color.Red * 0.5f, true, 0, (int)Math.Max((1.5f / GameScreen.Selected.Cam.Zoom), 1.0f));
            }
        }
        
        public void Render(GraphicsDevice graphicsDevice, Camera cam)
        {
            if (renderer.PositionInBuffer > renderer.vertices.Length - 6) return;

            Vector2 submarinePos = Submarine == null ? Vector2.Zero : Submarine.DrawPosition;

            //calculate where the surface should be based on the water volume
            float top = rect.Y + submarinePos.Y;
            float bottom = top - rect.Height;

            float drawSurface = surface + submarinePos.Y;

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
                    corners[i] += new Vector3(submarinePos, 0.0f);
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
            if (entityGrids == null) return null;

            if (guess != null)
            {
                if (Submarine.RectContains(useWorldCoordinates ? guess.WorldRect : guess.rect, position)) return guess;
            }

            var entities = EntityGrid.GetEntities(entityGrids, position, useWorldCoordinates);
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

        public static void DetectItemVisibility(Character c=null)
        {
            if (c==null)
            {
                foreach (Item it in Item.ItemList)
                {
                    it.Visible = true;
                }
            }
            else
            {
                Hull h = c.CurrentHull;
                hullList.ForEach(j => j.Visible = false);
                List<Hull> visibleHulls;
                if (h == null || c.Submarine == null)
                {
                    visibleHulls = hullList.FindAll(j => j.CanSeeOther(null, false));
                }
                else
                {
                    visibleHulls = hullList.FindAll(j => h.CanSeeOther(j, true));
                }
                visibleHulls.ForEach(j => j.Visible = true);
                foreach (Item it in Item.ItemList)
                {
                    if (it.CurrentHull == null || visibleHulls.Contains(it.CurrentHull)) it.Visible = true;
                    else it.Visible = false;
                }
            }
        }

        private bool CanSeeOther(Hull other,bool allowIndirect=true)
        {
            if (other == this) return true;
            
            if (other != null && other.Submarine==Submarine)
            {
                bool retVal = false;
                foreach (Gap g in ConnectedGaps)
                {
                    if (g.ConnectedWall != null && g.ConnectedWall.CastShadow) continue;
                    List<Hull> otherHulls = Hull.hullList.FindAll(h => h.ConnectedGaps.Contains(g) && h!=this);
                    retVal = otherHulls.Any(h => h == other);
                    if (!retVal && allowIndirect) retVal = otherHulls.Any(h => h.CanSeeOther(other, false));
                    if (retVal) return true;
                }
            }
            else
            {
                foreach (Gap g in ConnectedGaps)
                {
                    if (g.ConnectedDoor != null && !hullList.Any(h => h.ConnectedGaps.Contains(g) && h!=this)) return true;
                }
                List<MapEntity> structures = MapEntity.mapEntityList.FindAll(me => me is Structure && me.Rect.Intersects(Rect));
                return structures.Any(st => !(st as Structure).CastShadow);
            }
            return false;
        }

        //public List<Gap> FindGaps()
        //{
        //    List<Gap> gaps = new List<Gap>();
            
        //    foreach (Gap gap in Gap.GapList)
        //    {
        //        if (gap.Open < 0.01f) continue;

        //        if (gap.linkedTo.Contains(this)) gaps.Add(gap);
        //    }

        //    return gaps;
        //}

        public override XElement Save(XElement parentElement)
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

            Hull h = new Hull(MapEntityPrefab.list.Find(m => m.Name == "Hull"), rect, submarine);

            h.volume = ToolBox.GetAttributeFloat(element, "pressure", 0.0f);

            h.ID = (ushort)int.Parse(element.Attribute("ID").Value);
        }

        public override bool FillNetworkData(Networking.NetworkEventType type, NetBuffer message, object data)
        {            
            message.WriteRangedSingle(MathHelper.Clamp(volume/FullVolume, 0.0f, 1.5f), 0.0f, 1.5f, 6);
            message.WriteRangedSingle(MathHelper.Clamp(OxygenPercentage, 0.0f, 100.0f), 0.0f, 100.0f, 8);

            message.Write(fireSources.Count > 0);
            if (fireSources.Count > 0)
            {
                message.Write((byte)fireSources.Count);
                for (int i = 0; i < Math.Min(fireSources.Count, 16); i++)
                {
                    var fireSource = fireSources[i];

                    Vector2 normalizedPos = new Vector2(
                        (fireSource.Position.X - rect.X) / rect.Width,
                        (fireSource.Position.Y - (rect.Y - rect.Height)) / rect.Height);
                    message.WriteRangedSingle(MathHelper.Clamp(normalizedPos.X, 0.0f, 1.0f), 0.0f, 1.0f, 4);
                    message.WriteRangedSingle(MathHelper.Clamp(normalizedPos.Y, 0.0f, 1.0f), 0.0f, 1.0f, 4);

                    message.WriteRangedSingle(MathHelper.Clamp(fireSource.Size.X / rect.Width, 0.0f, 1.0f), 0, 1.0f, 6);
                }
            }
            
            lastSentVolume = volume;
            lastSentOxygen = OxygenPercentage;

            return true;
        }

        public override bool ReadNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetIncomingMessage message, float sendingTime, out object data)
        {
            data = null;

            if (GameMain.Server != null) return false;

            if (sendingTime < lastNetworkUpdate) return false;

            float newVolume = this.volume;
            float newOxygen = this.OxygenPercentage;

            try
            {
                float newPercentage = message.ReadRangedSingle(0.0f, 1.5f, 6);
                newVolume = newPercentage * FullVolume;

                newOxygen = message.ReadRangedSingle(0.0f, 100.0f, 8);
            }

            catch (Exception e)
            {
                DebugConsole.Log("Failed to read network message for Hull {" + ID + "}! " + e.Message);
                return false;
            }

            Volume = newVolume;
            OxygenPercentage = newOxygen;

            bool hasFireSources = message.ReadBoolean();


            List<FireSource> newFireSources = new List<FireSource>();
            if (hasFireSources)
            {
                int fireSourceCount = message.ReadByte();
                for (int i = 0; i < fireSourceCount; i++)
                {
                    Vector2 pos = Vector2.Zero;
                    float size = 0.0f;
                    pos.X = message.ReadRangedSingle(0.0f, 1.0f, 4);
                    pos.Y = message.ReadRangedSingle(0.0f, 1.0f, 4);
                    size = message.ReadRangedSingle(0.0f, 1.0f, 6);
                    if (!MathUtils.IsValid(pos)) continue;

                    pos.X = MathHelper.Clamp(pos.X, 0.05f, 0.95f);
                    pos.Y = MathHelper.Clamp(pos.Y, 0.05f, 0.95f);

                    pos = new Vector2(rect.X + rect.Width * pos.X, rect.Y - rect.Height + (rect.Height * pos.Y));

                    var existingFire = fireSources.Find(fs => fs.Contains(pos));
                    if (existingFire!=null)
                    {
                        newFireSources.Add(existingFire);
                        existingFire.Position = pos;
                        existingFire.Size = new Vector2(
                            existingFire.Hull == null ?  size : size*existingFire.Hull.rect.Width, 
                            existingFire.Size.Y);
                    }
                    else
                    {
                        var newFire = new FireSource(pos + Submarine.Position, this, true);
                        newFire.Size = new Vector2(
                            newFire.Hull == null ? size : size * newFire.Hull.rect.Width, 
                            newFire.Size.Y);

                        //ignore if the fire wasn't added to this room (invalid position)?
                        if (!fireSources.Contains(newFire)) continue;
                        newFireSources.Add(newFire);
                    }
                }
            }

            var toBeRemoved = fireSources.FindAll(fs => !newFireSources.Contains(fs));
            for (int i = toBeRemoved.Count - 1; i >= 0; i--)
            {
                toBeRemoved[i].Remove(true);
            }
            fireSources = newFireSources;

            lastNetworkUpdate = sendingTime;

            return true;
        }
    

    }
}
