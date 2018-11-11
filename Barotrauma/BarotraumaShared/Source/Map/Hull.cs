using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class Hull : MapEntity, ISerializableEntity, IServerSerializable
    {
        const float NetworkUpdateInterval = 0.5f;

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
        
        public readonly Dictionary<string, SerializableProperty> properties;
        public Dictionary<string, SerializableProperty> SerializableProperties
        {
            get { return properties; }
        }

        private float lethalPressure;

        private float surface;
        private float waterVolume;
        private float pressure;

        private float oxygen;

        private bool update;

        public bool Visible = true;
        
        float[] waveY; //displacement from the surface of the water
        float[] waveVel; //velocity of the point

        float[] leftDelta;
        float[] rightDelta;

        private float lastSentVolume, lastSentOxygen;
        private float sendUpdateTimer;
        
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

                surface = rect.Y - rect.Height + WaterVolume / rect.Width;
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

        public override bool Linkable
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

        public float WaterVolume
        {
            get { return waterVolume; }
            set
            {
                if (!MathUtils.IsValid(value)) return;
                waterVolume = MathHelper.Clamp(value, 0.0f, Volume + MaxCompress);
                if (waterVolume < Volume) Pressure = rect.Y - rect.Height + waterVolume / rect.Width;
                if (waterVolume > 0.0f) update = true;
            }
        }

        [Serialize(90.0f, true)]
        public float Oxygen
        {
            get { return oxygen; }
            set 
            {
                if (!MathUtils.IsValid(value)) return;
                oxygen = MathHelper.Clamp(value, 0.0f, Volume); 
            }
        }

        public float OxygenPercentage
        {
            get { return oxygen / Volume * 100.0f; }
            set { Oxygen = (value / 100.0f) * Volume; }
        }

        public float Volume
        {
            get { return rect.Width * rect.Height; }
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
            
            OxygenPercentage = 100.0f;

            fireSources = new List<FireSource>();

            properties = SerializableProperty.GetProperties(this);

            int arraySize = (rectangle.Width / WaveWidth + 1);
            waveY = new float[arraySize];
            waveVel = new float[arraySize];

            leftDelta = new float[arraySize];
            rightDelta = new float[arraySize];

            surface = rect.Y - rect.Height;

            aiTarget = new AITarget(this);

            hullList.Add(this);

            ConnectedGaps = new List<Gap>();

            if (submarine == null || !submarine.Loading)
            {
                Item.UpdateHulls();
                Gap.UpdateHulls();
            }

            WaterVolume = 0.0f;

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

        public override MapEntity Clone()
        {
            return new Hull(MapEntityPrefab.Find("Hull"), rect, Submarine);
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

                rect.Location -= MathUtils.ToPoint(submarine.HiddenSubPosition);
                
                grid.InsertEntity(this);

                rect.Location += MathUtils.ToPoint(submarine.HiddenSubPosition);
                return;
            }
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

            surface = rect.Y - rect.Height + WaterVolume / rect.Width;
            Pressure = surface;
        }

        public override void ShallowRemove()
        {
            base.Remove();
            hullList.Remove(this);

            if (Submarine == null || (!Submarine.Loading && !Submarine.Unloading))
            {
                Item.UpdateHulls();
                Gap.UpdateHulls();
            }

            List<FireSource> fireSourcesToRemove = new List<FireSource>(fireSources);
            foreach (FireSource fireSource in fireSourcesToRemove)
            {
                fireSource.Remove();
            }
            fireSources.Clear();

#if CLIENT
            if (soundIndex > -1)
            {
                Sounds.SoundManager.Stop(soundIndex);
                soundIndex = -1;
            }
#endif
            
            if (entityGrids != null)
            {
                foreach (EntityGrid entityGrid in entityGrids)
                {
                    entityGrid.RemoveEntity(this);
                }
            }
        }

        public override void Remove()
        {
            base.Remove();
            hullList.Remove(this);

            if (Submarine == null || (!Submarine.Loading && !Submarine.Unloading))
            {
                Item.UpdateHulls();
                Gap.UpdateHulls();
            }

            List<FireSource> fireSourcesToRemove = new List<FireSource>(fireSources);
            foreach (FireSource fireSource in fireSourcesToRemove)
            {
                fireSource.Remove();
            }
            fireSources.Clear();

#if CLIENT
            if (soundIndex > -1)
            {
                Sounds.SoundManager.Stop(soundIndex);
                soundIndex = -1;
            }
#endif
            
            if (entityGrids != null)
            {
                foreach (EntityGrid entityGrid in entityGrids)
                {
                    entityGrid.RemoveEntity(this);
                }
            }
        }

        public void AddFireSource(FireSource fireSource)
        {
            fireSources.Add(fireSource);

            if (GameMain.Server != null && !IdFreed) GameMain.Server.CreateEntityEvent(this);
        }

        public override void Update(float deltaTime, Camera cam)
        {
            UpdateProjSpecific(deltaTime, cam);

            Oxygen -= OxygenDetoriationSpeed * deltaTime;

            FireSource.UpdateAll(fireSources, deltaTime);

            aiTarget.SightRange = Submarine == null ? 0.0f : Math.Max(Submarine.Velocity.Length() * 500.0f, 500.0f);
            aiTarget.SoundRange -= deltaTime * 1000.0f;
         
            //update client hulls if the amount of water has changed by >10%
            //or if oxygen percentage has changed by 5%
            if (Math.Abs(lastSentVolume - waterVolume) > Volume * 0.1f ||
                Math.Abs(lastSentOxygen - OxygenPercentage) > 5f)
            {
                if (GameMain.Server != null && !IdFreed)
                {
                    sendUpdateTimer -= deltaTime;
                    if (sendUpdateTimer < 0.0f)
                    {
                        GameMain.Server.CreateEntityEvent(this);
                        lastSentVolume = waterVolume;
                        lastSentOxygen = OxygenPercentage;
                        sendUpdateTimer = NetworkUpdateInterval;
                    }
                }
            }

            if (!update)
            {
                lethalPressure = 0.0f;
                return;
            }

            float surfaceY = rect.Y - rect.Height + WaterVolume / rect.Width;
            for (int i = 0; i < waveY.Length; i++)
            {
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

            if (waterVolume < Volume)
            {
                LethalPressure -= 10.0f * deltaTime;
                if (WaterVolume <= 0.0f)
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

        partial void UpdateProjSpecific(float deltaTime, Camera cam);

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

        public void Extinguish(float deltaTime, float amount, Vector2 position)
        {
            for (int i = fireSources.Count - 1; i >= 0; i-- )
            {
                fireSources[i].Extinguish(deltaTime, amount, position);
            }
        }

        public void RemoveFire(FireSource fire)
        {
            fireSources.Remove(fire);

            if (GameMain.Server != null) GameMain.Server.CreateEntityEvent(this);
        }

        public List<Hull> GetConnectedHulls(int? searchDepth)
        {
            return GetAdjacentHulls(new List<Hull>(), 0, searchDepth);

        }

        private List<Hull> GetAdjacentHulls(List<Hull> connectedHulls, int steps, int? searchDepth)
        {
            connectedHulls.Add(this);

            if (searchDepth != null && steps >= searchDepth.Value) return connectedHulls;

            foreach (Gap g in ConnectedGaps)
            {
                for (int i = 0; i < 2 && i < g.linkedTo.Count; i++)
                {
                    Hull hull = g.linkedTo[i] as Hull;
                    if (hull != null && !connectedHulls.Contains(hull))
                    {
                        hull.GetAdjacentHulls(connectedHulls, steps++, searchDepth);
                    }                    
                }
            }

            return connectedHulls;
        }

        //returns the water block which contains the point (or null if it isn't inside any)
        public static Hull FindHull(Vector2 position, Hull guess = null, bool useWorldCoordinates = true, bool inclusive = true)
        {
            if (entityGrids == null) return null;

            if (guess != null)
            {
                if (Submarine.RectContains(useWorldCoordinates ? guess.WorldRect : guess.rect, position, inclusive)) return guess;
            }

            var entities = EntityGrid.GetEntities(entityGrids, position, useWorldCoordinates);
            foreach (Hull hull in entities)
            {
                if (Submarine.RectContains(useWorldCoordinates ? hull.WorldRect : hull.rect, position, inclusive)) return hull;
            }

            return null;
        }

        //returns the water block which contains the point (or null if it isn't inside any)
        public static Hull FindHullOld(Vector2 position, Hull guess = null, bool useWorldCoordinates = true, bool inclusive = true)
        {
            return FindHullOld(position, hullList, guess, useWorldCoordinates, inclusive);
        }

        public static Hull FindHullOld(Vector2 position, List<Hull> hulls, Hull guess = null, bool useWorldCoordinates = true, bool inclusive = true)
        {
            if (guess != null && hulls.Contains(guess))
            {
                if (Submarine.RectContains(useWorldCoordinates ? guess.WorldRect : guess.rect, position, inclusive)) return guess;
            }

            foreach (Hull hull in hulls)
            {
                if (Submarine.RectContains(useWorldCoordinates ? hull.WorldRect : hull.rect, position, inclusive)) return hull;
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

        private bool CanSeeOther(Hull other, bool allowIndirect = true)
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

        public void ServerWrite(NetBuffer message, Client c, object[] extraData = null)
        {
            message.WriteRangedSingle(MathHelper.Clamp(waterVolume / Volume, 0.0f, 1.5f), 0.0f, 1.5f, 8);
            message.WriteRangedSingle(MathHelper.Clamp(OxygenPercentage, 0.0f, 100.0f), 0.0f, 100.0f, 8);

            message.Write(fireSources.Count > 0);
            if (fireSources.Count > 0)
            {
                message.WriteRangedInteger(0, 16, Math.Min(fireSources.Count, 16));
                for (int i = 0; i < Math.Min(fireSources.Count, 16); i++)
                {
                    var fireSource = fireSources[i];
                    Vector2 normalizedPos = new Vector2(
                        (fireSource.Position.X - rect.X) / rect.Width,
                        (fireSource.Position.Y - (rect.Y - rect.Height)) / rect.Height);

                    message.WriteRangedSingle(MathHelper.Clamp(normalizedPos.X, 0.0f, 1.0f), 0.0f, 1.0f, 8);
                    message.WriteRangedSingle(MathHelper.Clamp(normalizedPos.Y, 0.0f, 1.0f), 0.0f, 1.0f, 8);
                    message.WriteRangedSingle(MathHelper.Clamp(fireSource.Size.X / rect.Width, 0.0f, 1.0f), 0, 1.0f, 8);
                }
            }
        }

        public void ClientRead(ServerNetObject type, NetBuffer message, float sendingTime)
        {
            WaterVolume = message.ReadRangedSingle(0.0f, 1.5f, 8) * Volume;
            OxygenPercentage = message.ReadRangedSingle(0.0f, 100.0f, 8);

            bool hasFireSources = message.ReadBoolean();
            int fireSourceCount = 0;
            
            if (hasFireSources)
            {
                fireSourceCount = message.ReadRangedInteger(0, 16);
                for (int i = 0; i < fireSourceCount; i++)
                {
                    Vector2 pos = Vector2.Zero;
                    float size = 0.0f;
                    pos.X = MathHelper.Clamp(message.ReadRangedSingle(0.0f, 1.0f, 8), 0.05f, 0.95f);
                    pos.Y = MathHelper.Clamp(message.ReadRangedSingle(0.0f, 1.0f, 8), 0.05f, 0.95f);
                    size = message.ReadRangedSingle(0.0f, 1.0f, 8);

                    pos = new Vector2(
                        rect.X + rect.Width * pos.X, 
                        rect.Y - rect.Height + (rect.Height * pos.Y));
                    size = size * rect.Width;
                    
                    var newFire = i < fireSources.Count ? fireSources[i] : new FireSource(pos + Submarine.Position, null, true);
                    newFire.Position = pos;
                    newFire.Size = new Vector2(size, newFire.Size.Y);

                    //ignore if the fire wasn't added to this room (invalid position)?
                    if (!fireSources.Contains(newFire))
                    {
                        newFire.Remove();
                        continue;
                    }                    
                }
            }

            while (fireSources.Count > fireSourceCount)
            {
                fireSources[fireSources.Count - 1].Remove();
            }            
        }

        public static void Load(XElement element, Submarine submarine)
        {
            Rectangle rect = Rectangle.Empty;
            if (element.Attribute("rect") != null)
            {
                rect = element.GetAttributeRect("rect", Rectangle.Empty);
            }
            else
            {
                //backwards compatibility
                rect = new Rectangle(
                    int.Parse(element.Attribute("x").Value),
                    int.Parse(element.Attribute("y").Value),
                    int.Parse(element.Attribute("width").Value),
                    int.Parse(element.Attribute("height").Value));
            }

            Hull h = new Hull(MapEntityPrefab.Find("Hull"), rect, submarine);

            h.waterVolume = element.GetAttributeFloat("pressure", 0.0f);

            h.ID = (ushort)int.Parse(element.Attribute("ID").Value);
        }

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
                new XAttribute("water", waterVolume)
            );

            parentElement.Add(element);

            return element;
        }

    }
}
