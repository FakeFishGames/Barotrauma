using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.MapCreatures.Behavior;

namespace Barotrauma
{
    partial class BackgroundSection
    {
        public Rectangle Rect;
        public ushort Index;
        public ushort RowIndex;

        private Vector4 colorVector4;
        private Color color;

        public readonly Vector2 Noise;
        public readonly Color DirtColor;

#if CLIENT
        public Sprite GrimeSprite;
#endif

        public float ColorStrength
        {
            get;
            protected set;
        }

        public Color Color
        {
            get { return color; }
            protected set
            {
                color = value;
                colorVector4 = new Vector4(value.R / 255.0f, value.G / 255.0f, value.B / 255.0f, value.A / 255.0f);
            }
        }

        public BackgroundSection(Rectangle rect, ushort index, ushort rowIndex)
        {
            Rect = rect;
            Index = index;
            ColorStrength = 0.0f;
            RowIndex = rowIndex;

            Noise = new Vector2(
                PerlinNoise.GetPerlin(Rect.X / 1000.0f, Rect.Y / 1000.0f),
                PerlinNoise.GetPerlin(Rect.Y / 1000.0f + 0.5f, Rect.X / 1000.0f + 0.5f));

            Color = DirtColor = Color.Lerp(new Color(10, 10, 10, 100), new Color(54, 57, 28, 200), Noise.X);
#if CLIENT
            GrimeSprite = DecalManager.GrimeSprites[$"{nameof(GrimeSprite)}{index % DecalManager.GrimeSpriteCount}"].Sprite;
#endif
        }

        public BackgroundSection(Rectangle rect, ushort index, float colorStrength, Color color, ushort rowIndex)
        {
            System.Diagnostics.Debug.Assert(rect.Width > 0 && rect.Height > 0);

            Rect = rect;
            Index = index;
            ColorStrength = colorStrength;
            Color = color;
            RowIndex = rowIndex;

            Noise = new Vector2(
                PerlinNoise.GetPerlin(Rect.X / 1000.0f, Rect.Y / 1000.0f),
                PerlinNoise.GetPerlin(Rect.Y / 1000.0f + 0.5f, Rect.X / 1000.0f + 0.5f));
            
            DirtColor = Color.Lerp(new Color(10, 10, 10, 100), new Color(54, 57, 28, 200), Noise.X);
#if CLIENT
            GrimeSprite = DecalManager.GrimeSprites[$"{nameof(GrimeSprite)}{index % DecalManager.GrimeSpriteCount}"].Sprite;
#endif
        }

        public bool SetColor(Color color)
        {
            if (Color == color) { return false; }
            Color = color;
            return true;
        }

        public float SetColorStrength(float colorStrength)
        {
            if (ColorStrength == colorStrength) { return -1f; }
            float previous = ColorStrength;
            ColorStrength = colorStrength;
            return previous;
        }

        public bool LerpColor(Color to, float amount)
        {
            if (Color == to) { return false; }
            colorVector4 = Vector4.Lerp(colorVector4, to.ToVector4(), amount);
            color = new Color(colorVector4);
            return true;
        }

        public Color GetStrengthAdjustedColor()
        {
            return Color * ColorStrength;
        }
    }

    partial class Hull : MapEntity, ISerializableEntity, IServerSerializable
    {
        public readonly static List<Hull> HullList = new List<Hull>();
        public readonly static List<EntityGrid> EntityGrids = new List<EntityGrid>();

        public static bool ShowHulls = true;

        public static bool EditWater, EditFire;
        public const float OxygenDistributionSpeed = 30000.0f;
        public const float OxygenDeteriorationSpeed = 0.3f;
        public const float OxygenConsumptionSpeed = 700.0f;

        public const int WaveWidth = 32;
        public static float WaveStiffness = 0.01f;
        public static float WaveSpread = 0.02f;
        public static float WaveDampening = 0.02f;
        
        //how much excess water the room can contain, relative to the volume of the room.
        //needed to make it possible for pressure to "push" water up through U-shaped hull configurations
        public const float MaxCompress = 1.05f;

        public const int BackgroundSectionSize = 16;

        public const int BackgroundSectionsPerNetworkEvent = 16;

        public readonly Dictionary<Identifier, SerializableProperty> properties;
        public Dictionary<Identifier, SerializableProperty> SerializableProperties
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

        private float[] waveY; //displacement from the surface of the water
        private float[] waveVel; //velocity of the point

        private float[] leftDelta;
        private float[] rightDelta;

        public const int MaxDecalsPerHull = 10;

        private readonly List<Decal> decals = new List<Decal>();


        public readonly List<Gap> ConnectedGaps = new List<Gap>();

        public override string Name => "Hull";

        public LocalizedString DisplayName
        {
            get;
            private set;
        }

        private readonly HashSet<Identifier> moduleTags = new HashSet<Identifier>();

        /// <summary>
        /// Inherited flags from outpost generation.
        /// </summary>
        public IEnumerable<Identifier> OutpostModuleTags => moduleTags;

        private string roomName;
        [Editable, Serialize("", IsPropertySaveable.Yes, translationTextTag: "RoomName.")]
        public string RoomName
        {
            get { return roomName; }
            set
            {
                if (roomName == value) { return; }
                roomName = value;
                DisplayName = TextManager.Get(roomName).Fallback(roomName);
                if (!IsWetRoom && ForceAsWetRoom)
                {
                    IsWetRoom = true;
                }
            }
        }

        public Color? OriginalAmbientLight = null;

        private Color ambientLight;

        [Editable, Serialize("0,0,0,0", IsPropertySaveable.Yes)]
        public Color AmbientLight
        {
            get { return ambientLight; }
            set 
            { 
                ambientLight = value;
#if CLIENT
                lastAmbientLightEditTime = Timing.TotalTime;
#endif
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
                float prevOxygenPercentage = OxygenPercentage;

                if (value.Width != rect.Width)
                {
                    int arraySize = (int)Math.Ceiling((float)value.Width / WaveWidth + 1);
                    waveY = new float[arraySize];
                    waveVel = new float[arraySize];
                    leftDelta = new float[arraySize];
                    rightDelta = new float[arraySize];
                }

                base.Rect = value;

                if (Submarine == null || !Submarine.Loading)
                {
                    Item.UpdateHulls();
                    Gap.UpdateHulls();
                }

                OxygenPercentage = prevOxygenPercentage;
                surface = rect.Y - rect.Height + WaterVolume / rect.Width;
#if CLIENT
                drawSurface = surface;
#endif
                Pressure = surface;

                CreateBackgroundSections();
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

        public float CeilingHeight
        {
            get;
            private set;
        }

        public float Surface
        {
            get { return surface; }
        }

        public float WorldSurface
        {
            get { return Submarine == null ? surface : surface + Submarine.Position.Y; }
        }

        public float WaterVolume
        {
            get { return waterVolume; }
            set
            {
                if (!MathUtils.IsValid(value)) { return; }
                waterVolume = MathHelper.Clamp(value, 0.0f, Volume * MaxCompress);
                if (waterVolume < Volume) { Pressure = rect.Y - rect.Height + waterVolume / rect.Width; }
                if (waterVolume > 0.0f)
                {
                    update = true;
                }
            }
        }

        [Serialize(100000.0f, IsPropertySaveable.Yes)]
        public float Oxygen
        {
            get { return oxygen; }
            set 
            {
                if (!MathUtils.IsValid(value)) return;
                oxygen = MathHelper.Clamp(value, 0.0f, Volume); 
            }
        }

        private bool ForceAsWetRoom => 
            roomName != null && (
            roomName.Contains("ballast", StringComparison.OrdinalIgnoreCase) || 
            roomName.Contains("bilge", StringComparison.OrdinalIgnoreCase) || 
            roomName.Contains("airlock", StringComparison.OrdinalIgnoreCase) ||
            roomName.Contains("dockingport", StringComparison.OrdinalIgnoreCase));

        private bool isWetRoom;
        [Editable, Serialize(false, IsPropertySaveable.Yes, description: "It's normal for this hull to be filled with water. If the room name contains 'ballast', 'bilge', or 'airlock', you can't disable this setting.")]
        public bool IsWetRoom
        {
            get { return isWetRoom; }
            set
            {
                isWetRoom = value;
                if (ForceAsWetRoom)
                {
                    isWetRoom = true;
                }
            }
        }

        private bool avoidStaying;
        [Editable, Serialize(false, IsPropertySaveable.Yes, description: "Bots avoid staying here, but they are still allowed to access the room when needed and go through it. Forced true for wet rooms.")]
        public bool AvoidStaying
        {
            get { return avoidStaying || IsWetRoom; }
            set
            {
                avoidStaying = value;
                if (IsWetRoom)
                {
                    avoidStaying = true;
                }
            }
        }

        public float WaterPercentage => MathUtils.Percentage(WaterVolume, Volume);

        public float OxygenPercentage
        {
            get { return Volume <= 0.0f ? 100.0f : oxygen / Volume * 100.0f; }
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

        // sections of a decorative background that can be painted
        public List<BackgroundSection> BackgroundSections
        {
            get;
            private set;
        }

        private readonly HashSet<int> pendingSectionUpdates = new HashSet<int>();

        public int xBackgroundMax, yBackgroundMax;

        public bool SupportsPaintedColors
        {
            get
            {
                return BackgroundSections != null;
            }
        }

        private const int sectorWidth = 4;
        private const int sectorHeight = 4;

        private const float minColorStrength = 0.0f;
        private const float maxColorStrength = 0.7f;

        private bool networkUpdatePending;
        private float networkUpdateTimer;

        public List<FireSource> FireSources { get; private set; }

        public List<DummyFireSource> FakeFireSources { get; private set; }

        public BallastFloraBehavior BallastFlora { get; set; }

        public Hull(Rectangle rectangle)
            : this (rectangle, Submarine.MainSub)
        {
#if CLIENT
            if (SubEditorScreen.IsSubEditor())
            {
                SubEditorScreen.StoreCommand(new AddOrDeleteCommand(new List<MapEntity> { this }, false));
            }
#endif
        }

        public Hull(Rectangle rectangle, Submarine submarine, ushort id = Entity.NullEntityID)
            : base (CoreEntityPrefab.HullPrefab, submarine, id)
        {
            rect = rectangle;

            if (BackgroundSections == null) { CreateBackgroundSections(); }
            
            OxygenPercentage = 100.0f;

            FireSources = new List<FireSource>();
            FakeFireSources = new List<DummyFireSource>();

            properties = SerializableProperty.GetProperties(this);

            int arraySize = (int)Math.Ceiling((float)rectangle.Width / WaveWidth + 1);
            waveY = new float[arraySize];
            waveVel = new float[arraySize];
            leftDelta = new float[arraySize];
            rightDelta = new float[arraySize];

            surface = rect.Y - rect.Height;

            if (submarine?.Info != null && !submarine.Info.IsWreck)
            {
                aiTarget = new AITarget(this)
                {
                    MinSightRange = 1000,
                    MaxSightRange = 5000,
                    SoundRange = 0
                };
            }

            HullList.Add(this);

            if (submarine == null || !submarine.Loading)
            {
                Item.UpdateHulls();
                Gap.UpdateHulls();
            }

            CreateBackgroundSections();

            WaterVolume = 0.0f;

            InsertToList();

            DebugConsole.Log("Created hull (" + ID + ")");
        }

        public static Rectangle GetBorders()
        {
            if (!HullList.Any()) return Rectangle.Empty;

            Rectangle rect = HullList[0].rect;
            
            foreach (Hull hull in HullList)
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
            var clone = new Hull(rect, Submarine);
            foreach (KeyValuePair<Identifier, SerializableProperty> property in SerializableProperties)
            {
                if (!property.Value.Attributes.OfType<Editable>().Any()) { continue; }
                clone.SerializableProperties[property.Key].TrySetValue(clone, property.Value.GetValue(this));
            }
#if CLIENT
            clone.lastAmbientLightEditTime = 0.0;
#endif
            return clone;
        }

        public static EntityGrid GenerateEntityGrid(Rectangle worldRect)
        {
            var newGrid = new EntityGrid(worldRect, 200.0f);
            EntityGrids.Add(newGrid);
            return newGrid;
        }

        public static EntityGrid GenerateEntityGrid(Submarine submarine)
        {
            var newGrid = new EntityGrid(submarine, 200.0f);
            EntityGrids.Add(newGrid);            
            foreach (Hull hull in HullList)
            {
                if (hull.Submarine == submarine && !hull.IdFreed) { newGrid.InsertEntity(hull); }
            }
            return newGrid;
        }

        public void SetModuleTags(IEnumerable<Identifier> tags)
        {
            moduleTags.Clear();
            foreach (Identifier tag in tags)
            {
                moduleTags.Add(tag);
            }
        }

        public override void OnMapLoaded()
        {
            CeilingHeight = Rect.Height;

            Body lowerPickedBody = Submarine.PickBody(SimPosition, SimPosition - new Vector2(0.0f, ConvertUnits.ToSimUnits(rect.Height / 2.0f + 0.1f)), null, Physics.CollisionWall);
            if (lowerPickedBody != null)
            {
                Vector2 lowerPickedPos = Submarine.LastPickedPosition;

                if (Submarine.PickBody(SimPosition, SimPosition + new Vector2(0.0f, ConvertUnits.ToSimUnits(rect.Height / 2.0f + 0.1f)), null, Physics.CollisionWall) != null)
                {
                    Vector2 upperPickedPos = Submarine.LastPickedPosition;

                    CeilingHeight = ConvertUnits.ToDisplayUnits(upperPickedPos.Y - lowerPickedPos.Y);
                }
            }
            Pressure = rect.Y - rect.Height + waterVolume / rect.Width;
            
            BallastFlora?.OnMapLoaded();
#if CLIENT
            lastAmbientLightEditTime = 0.0;
#endif
        }

        public void AddToGrid(Submarine submarine)
        {
            foreach (EntityGrid grid in EntityGrids)
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

        public override void Move(Vector2 amount, bool ignoreContacts = true)
        {
            if (!MathUtils.IsValid(amount))
            {
                DebugConsole.ThrowError($"Attempted to move a hull by an invalid amount ({amount})\n{Environment.StackTrace.CleanupStackTrace()}");
                return;
            }

            rect.X += (int)amount.X;
            rect.Y += (int)amount.Y;

            if (Submarine == null || !Submarine.Loading)
            {
                Item.UpdateHulls();
                Gap.UpdateHulls();
            }

            surface = rect.Y - rect.Height + WaterVolume / rect.Width;
#if CLIENT
            drawSurface = surface;
#endif
            Pressure = surface;
        }

        public override void ShallowRemove()
        {
            base.Remove();
            HullList.Remove(this);

            if (Submarine == null || (!Submarine.Loading && !Submarine.Unloading))
            {
                Item.UpdateHulls();
                Gap.UpdateHulls();
            }

            List<FireSource> fireSourcesToRemove = new List<FireSource>(FireSources);
            fireSourcesToRemove.AddRange(FakeFireSources);
            foreach (FireSource fireSource in fireSourcesToRemove)
            {
                fireSource.Remove();
            }
            FireSources.Clear();
            FakeFireSources.Clear();
            
            if (EntityGrids != null)
            {
                foreach (EntityGrid entityGrid in EntityGrids)
                {
                    entityGrid.RemoveEntity(this);
                }
            }
        }

        public override void Remove()
        {
            base.Remove();
            HullList.Remove(this);
            BallastFlora?.Remove();

            if (Submarine != null && !Submarine.Loading && !Submarine.Unloading)
            {
                Item.UpdateHulls();
                Gap.UpdateHulls();
            }

            BackgroundSections?.Clear();

            List<FireSource> fireSourcesToRemove = new List<FireSource>(FireSources);
            foreach (FireSource fireSource in fireSourcesToRemove)
            {
                fireSource.Remove();
            }
            FireSources.Clear();
            
            if (EntityGrids != null)
            {
                foreach (EntityGrid entityGrid in EntityGrids)
                {
                    entityGrid.RemoveEntity(this);
                }
            }
        }

        public void AddFireSource(FireSource fireSource)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient)
            {
                //clients aren't allowed to create fire sources in hulls whose IDs have been freed (dynamic hulls between docking ports), because they can't be synced
                if (IdFreed) { return; }
            }
            if (fireSource is DummyFireSource dummyFire)
            {
                FakeFireSources.Add(dummyFire);
            }
            else
            {
                FireSources.Add(fireSource);
            }
        }

        public Decal AddDecal(UInt32 decalId, Vector2 worldPosition, float scale, bool isNetworkEvent, int? spriteIndex = null)
        {
            //clients are only allowed to create decals when the server says so
            if (!isNetworkEvent && GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient)
            {
                return null;
            }

            var decal = DecalManager.Prefabs.Find(p => p.UintIdentifier == decalId);
            if (decal == null)
            {
                DebugConsole.ThrowError($"Could not find a decal prefab with the UInt identifier {decalId}!");
                return null;
            }
            return AddDecal(decal.Name, worldPosition, scale, isNetworkEvent, spriteIndex);
        }


        public Decal AddDecal(string decalName, Vector2 worldPosition, float scale, bool isNetworkEvent, int? spriteIndex = null)
        {
            //clients are only allowed to create decals when the server says so
            if (!isNetworkEvent && GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient)
            {
                return null;
            }

            if (decals.Count >= MaxDecalsPerHull) { return null; }

            var decal = DecalManager.CreateDecal(decalName, scale, worldPosition, this, spriteIndex);
            if (decal != null)
            {
                if (GameMain.NetworkMember is { IsServer: true })
                {
                    GameMain.NetworkMember.CreateEntityEvent(this, new DecalEventData());
                }
                decals.Add(decal);
            }

            return decal;
        }

        #region Shared network write
        private void SharedStatusWrite(IWriteMessage msg)
        {
            msg.WriteRangedSingle(MathHelper.Clamp(waterVolume / Volume, 0.0f, 1.5f), 0.0f, 1.5f, 8);

            msg.WriteRangedInteger(Math.Min(FireSources.Count, 16), 0, 16);
            for (int i = 0; i < Math.Min(FireSources.Count, 16); i++)
            {
                var fireSource = FireSources[i];
                Vector2 normalizedPos = new Vector2(
                    (fireSource.Position.X - rect.X) / rect.Width,
                    (fireSource.Position.Y - (rect.Y - rect.Height)) / rect.Height);

                msg.WriteRangedSingle(MathHelper.Clamp(normalizedPos.X, 0.0f, 1.0f), 0.0f, 1.0f, 8);
                msg.WriteRangedSingle(MathHelper.Clamp(normalizedPos.Y, 0.0f, 1.0f), 0.0f, 1.0f, 8);
                msg.WriteRangedSingle(MathHelper.Clamp(fireSource.Size.X / rect.Width, 0.0f, 1.0f), 0, 1.0f, 8);
            }
        }

        private void SharedBackgroundSectionsWrite(IWriteMessage msg, in BackgroundSectionsEventData backgroundSectionsEventData)
        {
            int sectorToUpdate = backgroundSectionsEventData.SectorStartIndex;
            int start = sectorToUpdate * BackgroundSectionsPerNetworkEvent;
            int end = Math.Min((sectorToUpdate + 1) * BackgroundSectionsPerNetworkEvent, BackgroundSections.Count - 1);
            msg.WriteRangedInteger(sectorToUpdate, 0, BackgroundSections.Count - 1);
            for (int i = start; i < end; i++)
            {
                msg.WriteRangedSingle(BackgroundSections[i].ColorStrength, 0.0f, 1.0f, 8);
                msg.WriteUInt32(BackgroundSections[i].Color.PackedValue);
            }
        }
        #endregion

        #region Shared network read
        public readonly struct NetworkFireSource
        {
            public readonly Vector2 Position;
            public readonly float Size;

            public NetworkFireSource(Hull hull, Vector2 normalizedPosition, float normalizedSize)
            {
                Position = hull.Rect.Location.ToVector2()
                           + new Vector2(0, -hull.Rect.Height)
                           + normalizedPosition * hull.Rect.Size.ToVector2();
                Size = normalizedSize * hull.Rect.Width;
            }
        }

        private void SharedStatusRead(IReadMessage msg, out float newWaterVolume, out NetworkFireSource[] newFireSources)
        {
            newWaterVolume = msg.ReadRangedSingle(0.0f, 1.5f, 8) * Volume;

            int fireSourceCount = msg.ReadRangedInteger(0, 16);
            newFireSources = new NetworkFireSource[fireSourceCount];
            for (int i = 0; i < fireSourceCount; i++)
            {
                float x = MathHelper.Clamp(msg.ReadRangedSingle(0.0f, 1.0f, 8), 0.05f, 0.95f);
                float y = MathHelper.Clamp(msg.ReadRangedSingle(0.0f, 1.0f, 8), 0.05f, 0.95f);
                float size = msg.ReadRangedSingle(0.0f, 1.0f, 8);
                newFireSources[i] = new NetworkFireSource(this, new Vector2(x, y), size);
            }
        }

        private readonly struct BackgroundSectionNetworkUpdate
        {
            public readonly int SectionIndex;
            public readonly Color Color;
            public readonly float ColorStrength;
            public BackgroundSectionNetworkUpdate(int sectionIndex, Color color, float colorStrength)
            {
                SectionIndex = sectionIndex;
                Color = color;
                ColorStrength = colorStrength;
            }
        }
        
        private void SharedBackgroundSectionRead(IReadMessage msg, Action<BackgroundSectionNetworkUpdate> action, out int sectorToUpdate)
        {
            sectorToUpdate = msg.ReadRangedInteger(0, BackgroundSections.Count - 1);
            int start = sectorToUpdate * BackgroundSectionsPerNetworkEvent;
            int end = Math.Min((sectorToUpdate + 1) * BackgroundSectionsPerNetworkEvent, BackgroundSections.Count - 1);
            for (int i = start; i < end; i++)
            {
                float colorStrength = msg.ReadRangedSingle(0.0f, 1.0f, 8);
                Color color = new Color(msg.ReadUInt32());

                action(new BackgroundSectionNetworkUpdate(i, color, colorStrength));
            }
        }
        #endregion

        public override void Update(float deltaTime, Camera cam)
        {
            BallastFlora?.Update(deltaTime);
            
            UpdateProjSpecific(deltaTime, cam);

            Oxygen -= OxygenDeteriorationSpeed * deltaTime;

            if (FakeFireSources.Count > 0)
            {
                if ((Character.Controlled?.CharacterHealth?.GetAffliction("psychosis")?.Strength ?? 0.0f) <= 0.0f)
                {
                    for (int i = FakeFireSources.Count - 1; i >= 0; i--)
                    {
                        if (FakeFireSources[i].CausedByPsychosis)
                        {
                            FakeFireSources[i].Remove();
                        }
                    }
                }
                FireSource.UpdateAll(FakeFireSources, deltaTime);
            }

            FireSource.UpdateAll(FireSources, deltaTime);

            foreach (Decal decal in decals)
            {
                decal.Update(deltaTime);
            }
            //clients don't remove decals unless the server says so
            if (GameMain.NetworkMember is not { IsClient: true })
            {
                for (int i = decals.Count - 1; i >= 0; i--)
                {
                    var decal = decals[i];
                    if (decal.FadeTimer >= decal.LifeTime || decal.BaseAlpha <= 0.001f)
                    {
                        decals.RemoveAt(i);
    #if SERVER
                        decalUpdatePending = true;
    #endif
                    }
                }
            }

            if (aiTarget != null)
            {
                aiTarget.SightRange = Submarine == null ? aiTarget.MinSightRange : MathHelper.Lerp(aiTarget.MinSightRange, aiTarget.MaxSightRange, Submarine.Velocity.Length() / 10);
                aiTarget.SoundRange -= deltaTime * 1000.0f;
            }
         
            if (!update)
            {
                lethalPressure = 0.0f;
                return;
            }

            float waterDepth = WaterVolume / rect.Width;
            if (waterDepth < 1.0f)
            {
                //if there's only a minuscule amount of water, consider the surface to be at the bottom of the hull
                //otherwise unnoticeable amounts of water can for example cause magnesium to explode
                waterDepth = 0.0f;
            }
            
            surface = Math.Max(MathHelper.Lerp(
                surface, 
                rect.Y - rect.Height + waterDepth,
                deltaTime * 10.0f), rect.Y - rect.Height);

            for (int i = 0; i < waveY.Length; i++)
            {
                //apply velocity
                waveY[i] = waveY[i] + waveVel[i];

                //if the wave attempts to go "through" the top of the hull, make it bounce back
                if (surface + waveY[i] > rect.Y)
                {
                    float excess = (surface + waveY[i]) - rect.Y;
                    waveY[i] -= excess;
                    waveVel[i] = waveVel[i] * -0.5f;
                }
                //if the wave attempts to go "through" the bottom of the hull, make it bounce back
                else if (surface + waveY[i] < rect.Y - rect.Height)
                {
                    float excess = (surface + waveY[i]) - (rect.Y - rect.Height);
                    waveY[i] -= excess;
                    waveVel[i] = waveVel[i] * -0.5f;
                }

                //acceleration
                float a = -WaveStiffness * waveY[i] - waveVel[i] * WaveDampening;
                waveVel[i] = waveVel[i] + a;
            }

            //apply spread (two iterations)
            for (int j = 0; j < 2; j++)
            {
                for (int i = 1; i < waveY.Length - 1; i++)
                {
                    leftDelta[i] = WaveSpread * (waveY[i] - waveY[i - 1]);
                    waveVel[i - 1] += leftDelta[i];

                    rightDelta[i] = WaveSpread * (waveY[i] - waveY[i + 1]);
                    waveVel[i + 1] += rightDelta[i];
                }
            }

            //make waves propagate through horizontal gaps
            foreach (Gap gap in ConnectedGaps)
            {
                if (this != gap.linkedTo.FirstOrDefault() as Hull)
                {
                    //let the first linked hull handle the water propagation
                    continue;
                }

                if (!gap.IsRoomToRoom || !gap.IsHorizontal || gap.Open <= 0.0f) { continue; }
                if (surface > gap.Rect.Y || surface < gap.Rect.Y - gap.Rect.Height) { continue; }

                // ReSharper refuses to compile this if it's using "as Hull" since "as" means it can be null and you can't compare null to true or false
                Hull hull2 = this == gap.linkedTo[0] ? (Hull)gap.linkedTo[1] : (Hull)gap.linkedTo[0];
                float otherSurfaceY = hull2.surface;
                if (otherSurfaceY > gap.Rect.Y || otherSurfaceY < gap.Rect.Y - gap.Rect.Height) { continue; }

                float surfaceDiff = (surface - otherSurfaceY) * gap.Open;
                for (int j = 0; j < 2; j++)
                {
                    rightDelta[waveY.Length - 1] = WaveSpread * (hull2.waveY[0] - waveY[waveY.Length - 1] - surfaceDiff) * 0.5f;
                    waveVel[waveY.Length - 1] += rightDelta[waveY.Length - 1];
                    waveY[waveY.Length - 1] += rightDelta[waveY.Length - 1];

                    hull2.leftDelta[0] = WaveSpread * (waveY[waveY.Length - 1] - hull2.waveY[0] + surfaceDiff) * 0.5f;
                    hull2.waveVel[0] += hull2.leftDelta[0];
                    hull2.waveY[0] += hull2.leftDelta[0];
                }

                if (surfaceDiff < 32.0f)
                {
                    //update surfaces to the same level
                    hull2.waveY[0] = surfaceDiff * 0.5f;
                    waveY[waveY.Length - 1] = -surfaceDiff * 0.5f;
                }
            }


            //apply spread (two iterations)
            for (int j = 0; j < 2; j++)
            {
                for (int i = 1; i < waveY.Length - 1; i++)
                {
                    waveY[i - 1] += leftDelta[i];
                    waveY[i + 1] += rightDelta[i];
                }
            }

            if (waterVolume < Volume)
            {
                LethalPressure -= 10.0f * deltaTime;
                if (WaterVolume <= 0.0f)
                {
#if CLIENT
                    //wait for the surface to be lerped back to bottom and the waves to settle until disabling update
                    if (drawSurface > rect.Y - rect.Height + 1) { return; }
#endif
                    for (int i = 1; i < waveY.Length - 1; i++)
                    {
                        if (waveY[i] > 0.1f) { return; }
                    }

                    update = false;
                }
            }
        }

        partial void UpdateProjSpecific(float deltaTime, Camera cam);

        public void ApplyFlowForces(float deltaTime, Item item)
        {
            if (item.body.Mass <= 0.0f)
            {
                return;
            }
            foreach (var gap in ConnectedGaps.Where(gap => gap.Open > 0))
            {
                var distance = MathHelper.Max(Vector2.DistanceSquared(item.Position, gap.Position) / 1000, 1f);
                Vector2 force = (gap.LerpedFlowForce / distance) * deltaTime;
                if (force.LengthSquared() > 0.01f)
                {
                    item.body.ApplyForce(force);
                }
            }
        }

        public void Extinguish(float deltaTime, float amount, Vector2 position, bool extinguishRealFires = true, bool extinguishFakeFires = true)
        {
            if (extinguishRealFires)
            {
                for (int i = FireSources.Count - 1; i >= 0; i--)
                {
                    FireSources[i].Extinguish(deltaTime, amount, position);
                }
            }
            if (extinguishFakeFires)
            {
                for (int i = FakeFireSources.Count - 1; i >= 0; i--)
                {
                    FakeFireSources[i].Extinguish(deltaTime, amount, position);
                }
            }
        }

        public void RemoveFire(FireSource fire)
        {
            FireSources.Remove(fire);
            if (fire is DummyFireSource dummyFire)
            {
                FakeFireSources.Remove(dummyFire);
            }
        }

        private readonly HashSet<Hull> adjacentHulls = new HashSet<Hull>();
        public IEnumerable<Hull> GetConnectedHulls(bool includingThis, int? searchDepth = null, bool ignoreClosedGaps = false)
        {
            adjacentHulls.Clear();
            int startStep = 0;
            searchDepth ??= 100;
            GetAdjacentHulls(adjacentHulls, ref startStep, searchDepth.Value, ignoreClosedGaps);
            if (!includingThis) { adjacentHulls.Remove(this); }
            return adjacentHulls;
        }

        private void GetAdjacentHulls(HashSet<Hull> connectedHulls, ref int step, int searchDepth, bool ignoreClosedGaps = false)
        {
            connectedHulls.Add(this);
            if (step > searchDepth) { return; }
            foreach (Gap g in ConnectedGaps)
            {
                if (ignoreClosedGaps && g.Open <= 0.0f) { continue; }
                for (int i = 0; i < 2 && i < g.linkedTo.Count; i++)
                {
                    if (g.linkedTo[i] is Hull hull && !connectedHulls.Contains(hull))
                    {
                        step++;
                        hull.GetAdjacentHulls(connectedHulls, ref step, searchDepth, ignoreClosedGaps);
                    }
                }
            }
        }

        /// <summary>
        /// Approximate distance from this hull to the target hull, moving through open gaps without passing through walls.
        /// Uses a greedy algo and may not use the most optimal path. Returns float.MaxValue if no path is found.
        /// </summary>
        public float GetApproximateDistance(Vector2 startPos, Vector2 endPos, Hull targetHull, float maxDistance, float distanceMultiplierPerClosedDoor = 0)
        {
            return GetApproximateHullDistance(startPos, endPos, new HashSet<Hull>(), targetHull, 0.0f, maxDistance, distanceMultiplierPerClosedDoor);
        }

        private float GetApproximateHullDistance(Vector2 startPos, Vector2 endPos, HashSet<Hull> connectedHulls, Hull target, float distance, float maxDistance, float distanceMultiplierFromDoors = 0)
        {
            if (distance >= maxDistance) { return float.MaxValue; }
            if (this == target)
            {
                return distance + Vector2.Distance(startPos, endPos);
            }

            connectedHulls.Add(this);

            foreach (Gap g in ConnectedGaps)
            {
                float distanceMultiplier = 1;
                if (g.ConnectedDoor != null && !g.ConnectedDoor.IsBroken)
                {
                    //gap blocked if the door is not open or the predicted state is not open
                    if ((g.ConnectedDoor.IsClosed && !g.ConnectedDoor.IsBroken) || (g.ConnectedDoor.PredictedState.HasValue && !g.ConnectedDoor.PredictedState.Value))
                    {
                        if (g.ConnectedDoor.OpenState < 0.1f)
                        {
                            if (distanceMultiplierFromDoors <= 0) { continue; }
                            distanceMultiplier *= distanceMultiplierFromDoors;
                        }
                    }
                }
                else if (g.Open <= 0.0f)
                {
                    continue;
                }

                for (int i = 0; i < 2 && i < g.linkedTo.Count; i++)
                {
                    if (g.linkedTo[i] is Hull hull && !connectedHulls.Contains(hull))
                    {
                        float dist = hull.GetApproximateHullDistance(g.Position, endPos, connectedHulls, target, distance + Vector2.Distance(startPos, g.Position) * distanceMultiplier, maxDistance);
                        if (dist < float.MaxValue)
                        {
                            return dist;
                        }
                    }
                }
            }

            return float.MaxValue;
        }

        /// <summary>
        /// Returns the hull which contains the point (or null if it isn't inside any)
        /// </summary>
        /// <param name="position">The position to check</param>
        /// <param name="guess">This hull is checked first: if the current hull is known, this can be used as an optimization</param>
        /// <param name="useWorldCoordinates">Should world coordinates or the sub's local coordinates be used?</param>
        /// <param name="inclusive">Does being exactly at the edge of the hull count as being inside?</param>
        public static Hull FindHull(Vector2 position, Hull guess = null, bool useWorldCoordinates = true, bool inclusive = true)
        {
            if (EntityGrids == null)
            {
                return null;
            }

            if (guess != null)
            {
                if (Submarine.RectContains(useWorldCoordinates ? guess.WorldRect : guess.rect, position, inclusive))
                {
                    return guess;
                }
            }

            foreach (EntityGrid entityGrid in EntityGrids)
            {
                if (entityGrid.Submarine != null && !entityGrid.Submarine.Loading)
                {
                    System.Diagnostics.Debug.Assert(!entityGrid.Submarine.Removed);
                    Rectangle borders = entityGrid.Submarine.Borders;
                    if (useWorldCoordinates)
                    {
                        Vector2 worldPos = entityGrid.Submarine.WorldPosition;
                        borders.Location += new Point((int)worldPos.X, (int)worldPos.Y);
                    }
                    else
                    {
                        borders.Location += new Point((int)entityGrid.Submarine.HiddenSubPosition.X, (int)entityGrid.Submarine.HiddenSubPosition.Y);
                    }

                    const float padding = 128.0f;
                    if (position.X < borders.X - padding || position.X > borders.Right + padding || 
                        position.Y > borders.Y + padding || position.Y < borders.Y - borders.Height - padding)
                    {
                        continue;
                    }
                }
                Vector2 transformedPosition = position;
                if (useWorldCoordinates && entityGrid.Submarine != null)
                {
                    transformedPosition -= entityGrid.Submarine.Position;
                }
                var entities = entityGrid.GetEntities(transformedPosition);
                if (entities == null) { continue; }
                foreach (Hull hull in entities)
                {
                    if (Submarine.RectContains(hull.rect, transformedPosition, inclusive))
                    {
                        return hull;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the hull which contains the point (or null if it isn't inside any). The difference to FindHull is that this method goes through all hulls without trying
        /// to first find the sub the point is inside and checking the hulls in that sub. 
        /// = This is slower, use with caution in situations where the sub's extents or hulls may have changed after it was loaded.
        /// </summary>
        public static Hull FindHullUnoptimized(Vector2 position, Hull guess = null, bool useWorldCoordinates = true, bool inclusive = true)
        {
            if (guess != null && HullList.Contains(guess))
            {
                if (Submarine.RectContains(useWorldCoordinates ? guess.WorldRect : guess.rect, position, inclusive)) return guess;
            }

            foreach (Hull hull in HullList)
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
                HullList.ForEach(j => j.Visible = false);
                List<Hull> visibleHulls;
                if (h == null || c.Submarine == null)
                {
                    visibleHulls = HullList.FindAll(j => j.CanSeeOther(null, false));
                }
                else
                {
                    visibleHulls = HullList.FindAll(j => h.CanSeeOther(j, true));
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

            if (other != null && other.Submarine == Submarine)
            {
                bool retVal = false;
                foreach (Gap g in ConnectedGaps)
                {
                    if (g.ConnectedWall != null && g.ConnectedWall.CastShadow) continue;
                    List<Hull> otherHulls = HullList.FindAll(h => h.ConnectedGaps.Contains(g) && h != this);
                    retVal = otherHulls.Any(h => h == other);
                    if (!retVal && allowIndirect) retVal = otherHulls.Any(h => h.CanSeeOther(other, false));
                    if (retVal) return true;
                }
            }
            else
            {
                foreach (Gap g in ConnectedGaps)
                {
                    if (g.ConnectedDoor != null && !HullList.Any(h => h.ConnectedGaps.Contains(g) && h != this)) return true;
                }
                List<MapEntity> structures = mapEntityList.FindAll(me => me is Structure && me.Rect.Intersects(Rect));
                return structures.Any(st => !(st as Structure).CastShadow);
            }
            return false;
        }
        
        public string CreateRoomName()
        {
            List<string> roomItems = new List<string>();
            foreach (Item item in Item.ItemList)
            {
                if (item.CurrentHull != this) continue;
                if (item.GetComponent<Items.Components.Reactor>() != null) roomItems.Add("reactor");
                if (item.GetComponent<Items.Components.Engine>() != null) roomItems.Add("engine");
                if (item.GetComponent<Items.Components.Steering>() != null) roomItems.Add("steering");
                if (item.GetComponent<Items.Components.Sonar>() != null) roomItems.Add("sonar");
                if (item.HasTag("ballast")) roomItems.Add("ballast");
            }

            if (roomItems.Contains("reactor"))
                return "RoomName.ReactorRoom";
            else if (roomItems.Contains("engine"))
                return "RoomName.EngineRoom";
            else if (roomItems.Contains("steering") && roomItems.Contains("sonar"))
                return "RoomName.CommandRoom";
            else if (roomItems.Contains("ballast"))
                return "RoomName.Ballast";

            var moduleFlags = Submarine?.Info?.OutpostModuleInfo?.ModuleFlags ?? this.moduleTags;

            if (moduleFlags != null && moduleFlags.Any() && 
                (Submarine.Info.Type == SubmarineType.OutpostModule || Submarine.Info.Type == SubmarineType.Outpost))
            {
                if (moduleFlags.Contains("airlock".ToIdentifier()) &&
                    ConnectedGaps.Any(g => !g.IsRoomToRoom && g.ConnectedDoor != null))
                {
                    return "RoomName.Airlock";
                }
            }
            else
            {
                if (ConnectedGaps.Any(g => !g.IsRoomToRoom && g.ConnectedDoor != null))
                {
                    return "RoomName.Airlock";
                }
            }

            Rectangle subRect = Submarine.Borders;

            Alignment roomPos;
            if (rect.Y - rect.Height / 2 > subRect.Y + subRect.Height * 0.66f)
                roomPos = Alignment.Top;
            else if (rect.Y - rect.Height / 2 > subRect.Y + subRect.Height * 0.33f)
                roomPos = Alignment.CenterY;
            else
                roomPos = Alignment.Bottom;
            
            if (rect.Center.X < subRect.X + subRect.Width * 0.33f)
                roomPos |= Alignment.Left;
            else if (rect.Center.X < subRect.X + subRect.Width * 0.66f)
                roomPos |= Alignment.CenterX;
            else
                roomPos |= Alignment.Right;

            return "RoomName.Sub" + roomPos.ToString();
        }

        /// <summary>
        /// Is this hull or any of the items inside it tagged as "airlock"?
        /// </summary>
        public bool IsTaggedAirlock()
        {
            if (RoomName != null && RoomName.Contains("airlock", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            else
            {
                foreach (Item item in Item.ItemList)
                {
                    if (item.CurrentHull != this && item.HasTag("airlock"))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Does this hull have any doors leading outside?
        /// </summary>
        /// <param name="character">Used to check if this character has access to the door leading outside</param>
        public bool LeadsOutside(Character character)
        {
            foreach (var gap in ConnectedGaps)
            {
                if (gap.ConnectedDoor == null) { continue; }
                if (gap.IsRoomToRoom) { continue; }
                if (!gap.ConnectedDoor.CanBeTraversed && (character == null || !gap.ConnectedDoor.HasAccess(character))) { continue; }
                return true;
            }
            return false;
        }

#region BackgroundSections
        private void CreateBackgroundSections()
        {
            int sectionWidth, sectionHeight;

            sectionWidth = sectionHeight = BackgroundSectionSize;

            xBackgroundMax = rect.Width / sectionWidth;
            yBackgroundMax = rect.Height / sectionHeight;

            BackgroundSections = new List<BackgroundSection>(xBackgroundMax * yBackgroundMax);

            int sections = xBackgroundMax * yBackgroundMax;
            float xSectors = xBackgroundMax / (float)sectorWidth;

            for (int y = 0; y < yBackgroundMax; y++)
            {
                for (int x = 0; x < xBackgroundMax; x++)
                {
                    ushort index = (ushort)BackgroundSections.Count;
                    int sector = (int)Math.Floor(index / (float)sectorWidth - xSectors * y) + y / sectorHeight * (int)Math.Ceiling(xSectors);
                    BackgroundSections.Add(new BackgroundSection(new Rectangle(x * sectionWidth, y * -sectionHeight, sectionWidth, sectionHeight), index, (ushort)y));
                }
            }

#if CLIENT
            minimumPaintAmountToDraw = maxColorStrength / BackgroundSections.Count;
#endif
        }
        
        public static Hull GetCleanTarget(Vector2 worldPosition)
        {
            foreach (Hull hull in HullList)
            {
                Rectangle worldRect = hull.WorldRect;
                if (worldPosition.X < worldRect.X || worldPosition.X > worldRect.Right) { continue; }
                if (worldPosition.Y > worldRect.Y || worldPosition.Y < worldRect.Y - worldRect.Height) { continue; }
                return hull;
            }
            return null;
        }

        public BackgroundSection GetBackgroundSection(Vector2 worldPosition)
        {
            if (!SupportsPaintedColors) { return null; }

            Vector2 subOffset = Submarine == null ? Vector2.Zero : Submarine.Position;
            Vector2 relativePosition = new Vector2(worldPosition.X - subOffset.X - rect.X, worldPosition.Y - subOffset.Y - rect.Y);

            int xIndex = (int)Math.Floor(relativePosition.X / BackgroundSectionSize);
            if (xIndex < 0 || xIndex >= xBackgroundMax) { return null; }
            int yIndex = (int)Math.Floor(-relativePosition.Y / BackgroundSectionSize);
            if (yIndex < 0 || yIndex >= yBackgroundMax) { return null; }

            return BackgroundSections[xIndex + yIndex * xBackgroundMax];
        }

        public IEnumerable<BackgroundSection> GetBackgroundSectionsViaContaining(Rectangle rectArea)
        {
            if (BackgroundSections == null || BackgroundSections.Count == 0) 
            {
                yield break;
            }
            else
            {
                int xMin = Math.Max(rectArea.X / BackgroundSectionSize, 0);
                if (xMin >= xBackgroundMax) { yield break; }
                int xMax = Math.Min(rectArea.Right / BackgroundSectionSize, xBackgroundMax - 1);
                if (xMax < 0) { yield break; }

                int yMin = Math.Max(-rectArea.Bottom / BackgroundSectionSize, 0);
                if (yMin >= yBackgroundMax) { yield break; }
                int yMax = Math.Min(-rectArea.Y / BackgroundSectionSize, yBackgroundMax - 1);
                if (yMax < 0) { yield break; }

                for (int x = xMin; x <= xMax; x++)
                {
                    for (int y = yMin; y <= yMax; y++)
                    {
                        yield return BackgroundSections[x + y * xBackgroundMax];
                    }
                }
            }
        }

        public bool DoesSectionMatch(int index, int row)
        {
            return index >= 0 && row >= 0 && BackgroundSections.Count > index && BackgroundSections[index] != null && BackgroundSections[index].RowIndex == row;
        }
        
        public void IncreaseSectionColorOrStrength(BackgroundSection section, Color? color, float? strength, bool requiresUpdate, bool isCleaning)
        {
            bool sectionUpdated = isCleaning;
            if (color != null)
            {
                if (section.Color != color.Value && strength.HasValue)
                {
                    //already painted with a different color -> interpolate towards the new one

                    //an ad-hoc formula that makes the color changes faster when the current strength is low
                    //(-> a barely dirty wall gets recolored almost immediately, while a more heavily colored one takes a while)
                    float changeSpeed = strength.Value / Math.Max(section.ColorStrength * section.ColorStrength, 0.001f) * 0.1f;
                    if (section.LerpColor(color.Value, changeSpeed)) { sectionUpdated = true; }
                }
                else
                {
                    if (section.SetColor(color.Value)) { sectionUpdated = true; }
                }
            }

            if (strength != null)
            {
                float previous = section.SetColorStrength(Math.Max(minColorStrength, Math.Min(maxColorStrength, section.ColorStrength + strength.Value)));
                if (previous != -1f)
                {
#if CLIENT
                    paintAmount = Math.Max(0, paintAmount + (section.ColorStrength - previous) / BackgroundSections.Count);
#endif
                    sectionUpdated = true;
                }
            }

            if (sectionUpdated && GameMain.NetworkMember != null && requiresUpdate)
            {
                networkUpdatePending = true;
                pendingSectionUpdates.Add((int)Math.Floor(section.Index / (float)BackgroundSectionsPerNetworkEvent));
#if CLIENT
                serverUpdateDelay = 0.5f;
#endif
            }
        }

        public void SetSectionColorOrStrength(BackgroundSection section, Color? color, float? strength)
        {
            if (color != null)
            {
                section.SetColor(color.Value);                
            }

            if (strength != null)
            {
                float previous = section.SetColorStrength(Math.Max(minColorStrength, Math.Min(maxColorStrength, section.ColorStrength + strength.Value)));
                if (previous != -1f)
                {
#if CLIENT
                    paintAmount = Math.Max(0, paintAmount + (section.ColorStrength - previous) / BackgroundSections.Count);
#endif
                }
            }
        }

        public void CleanSection(BackgroundSection section, float cleanVal, bool updateRequired)
        {
            bool decalsCleaned = false;
            foreach (Decal decal in decals)
            {
                if (decal.AffectsSection(section))
                {
                    decal.Clean(cleanVal);
                    decalsCleaned = true;
#if SERVER
                    decalUpdatePending = true;
#elif CLIENT
                    pendingDecalUpdates.Add(decal);
                    networkUpdatePending = true;
#endif
                }
            }

            if (section.ColorStrength == 0 && !decalsCleaned) { return; }
            IncreaseSectionColorOrStrength(section, null, cleanVal, updateRequired, true);
        }
#endregion

        public static Hull Load(ContentXElement element, Submarine submarine, IdRemap idRemap)
        {
            Rectangle rect;
            if (element.GetAttribute("rect") != null)
            {
                rect = element.GetAttributeRect("rect", Rectangle.Empty);
            }
            else
            {
                //backwards compatibility
                rect = new Rectangle(
                    int.Parse(element.GetAttribute("x").Value),
                    int.Parse(element.GetAttribute("y").Value),
                    int.Parse(element.GetAttribute("width").Value),
                    int.Parse(element.GetAttribute("height").Value));
            }

            var hull = new Hull(rect, submarine, idRemap.GetOffsetId(element))
            {
                WaterVolume = element.GetAttributeFloat("water", 0.0f)
            };
            hull.linkedToID = new List<ushort>();

            hull.ParseLinks(element, idRemap);

            string originalAmbientLight = element.GetAttributeString("originalambientlight", null);
            if (!string.IsNullOrWhiteSpace(originalAmbientLight))
            {
                hull.OriginalAmbientLight = XMLExtensions.ParseColor(originalAmbientLight, false);
            }

            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "decal":
                        string id = subElement.GetAttributeString("id", "");
                        Vector2 pos = subElement.GetAttributeVector2("pos", Vector2.Zero);
                        float scale = subElement.GetAttributeFloat("scale", 1.0f);
                        float timer = subElement.GetAttributeFloat("timer", 1.0f);
                        float baseAlpha = subElement.GetAttributeFloat("alpha", 1.0f);
                        var decal = hull.AddDecal(id, pos + hull.WorldRect.Location.ToVector2(), scale, true);
                        if (decal != null)
                        {
                            decal.FadeTimer = timer;
                            decal.BaseAlpha = baseAlpha;
                        }
                        break;
                    case "ballastflorabehavior":
                        Identifier identifier = subElement.GetAttributeIdentifier("identifier", Identifier.Empty);
                        BallastFloraPrefab prefab = BallastFloraPrefab.Find(identifier);
                        if (prefab != null)
                        {
                            hull.BallastFlora = new BallastFloraBehavior(hull, prefab, Vector2.Zero);
                            hull.BallastFlora.LoadSave(subElement, idRemap);
                        }
                        break;
                }
            }
                
            string backgroundSectionStr = element.GetAttributeString("backgroundsections", "");
            if (!string.IsNullOrEmpty(backgroundSectionStr))
            {
                string[] backgroundSectionStrSplit = backgroundSectionStr.Split(';');
                foreach (string str in backgroundSectionStrSplit)
                {
                    string[] backgroundSectionData = str.Split(':');
                    if (backgroundSectionData.Length != 3) { continue; }
                    Color color = XMLExtensions.ParseColor(backgroundSectionData[1]);
                    if (int.TryParse(backgroundSectionData[0], out int index) && 
                        float.TryParse(backgroundSectionData[2], NumberStyles.Any, CultureInfo.InvariantCulture, out float strength))
                    {
                        hull.SetSectionColorOrStrength(hull.BackgroundSections[index], color, strength);
                    }
                }
            }

            SerializableProperty.DeserializeProperties(hull, element);
            if (element.GetAttribute("oxygen") == null) { hull.Oxygen = hull.Volume; }

            return hull;
        }

        public override XElement Save(XElement parentElement)
        {
            if (Submarine == null)
            {
                string errorMsg = "Error - tried to save a hull that's not a part of any submarine.\n" + Environment.StackTrace.CleanupStackTrace();
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("Hull.Save:WorldHull", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                return null;
            }

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

            if (linkedTo != null && linkedTo.Count > 0)
            {
                var saveableLinked = linkedTo.Where(l => l.ShouldBeSaved && (l.Removed == Removed)).ToList();
                element.Add(new XAttribute("linked", string.Join(",", saveableLinked.Select(l => l.ID.ToString()))));
            }

            if (OriginalAmbientLight != null)
            {
                element.Add(new XAttribute("originalambientlight", XMLExtensions.ColorToString(OriginalAmbientLight.Value)));
            }

            if (BackgroundSections != null && BackgroundSections.Count > 0)
            {
                element.Add(
                    new XAttribute(
                        "backgroundsections",
                        string.Join(';', BackgroundSections.Where(b => b.ColorStrength > 0.01f).Select(b => b.Index + ":" + XMLExtensions.ColorToString(b.Color) + ":" + b.ColorStrength.ToString("G", CultureInfo.InvariantCulture)))));
            }

            foreach (Decal decal in decals)
            {
                element.Add(
                    new XElement("decal", 
                        new XAttribute("id", decal.Prefab.Identifier),
                        new XAttribute("pos", XMLExtensions.Vector2ToString(decal.NonClampedPosition)),
                        new XAttribute("scale", decal.Scale.ToString("G", CultureInfo.InvariantCulture)),
                        new XAttribute("timer", decal.FadeTimer.ToString("G", CultureInfo.InvariantCulture)),
                        new XAttribute("alpha", decal.BaseAlpha.ToString("G", CultureInfo.InvariantCulture))
                    ));
            }

            BallastFlora?.Save(element);

            SerializableProperty.SerializeProperties(this, element);
            parentElement.Add(element);
            return element;
        }

        public override string ToString()
        {
            return $"{base.ToString()} ({Name ?? "unnamed"})";
        }
    }
}
