using Barotrauma.Networking;
using Barotrauma.Extensions;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Immutable;
using Barotrauma.Abilities;
#if CLIENT
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Lights;
#endif

namespace Barotrauma
{
    partial class WallSection : IIgnorable
    {
        public Rectangle rect;
        public float damage;
        public Gap gap;

        public Structure Wall { get; }
        public Vector2 Position => Wall.SectionPosition(Wall.Sections.IndexOf(this));
        public Vector2 WorldPosition => Wall.SectionPosition(Wall.Sections.IndexOf(this), world: true);
        public Vector2 SimPosition => ConvertUnits.ToSimUnits(Position);
        public Submarine Submarine => Wall.Submarine;
        public Rectangle WorldRect => Submarine == null ? rect :
            new Rectangle((int)(rect.X + Submarine.Position.X), (int)(rect.Y + Submarine.Position.Y), rect.Width, rect.Height);
        public bool IgnoreByAI(Character character) => OrderedToBeIgnored && character.IsOnPlayerTeam;
        public bool OrderedToBeIgnored { get; set; }

        public WallSection(Rectangle rect, Structure wall, float damage = 0.0f)
        {
            System.Diagnostics.Debug.Assert(rect.Width > 0 && rect.Height > 0);
            this.rect = rect;
            this.damage = damage;
            Wall = wall;
        }
    }

    partial class Structure : MapEntity, IDamageable, IServerSerializable, ISerializableEntity
    {
        public const int WallSectionSize = 96;
        public static List<Structure> WallList = new List<Structure>();

        const float LeakThreshold = 0.1f;

#if CLIENT
        public SpriteEffects SpriteEffects = SpriteEffects.None;
#endif

        //dimensions of the wall sections' physics bodies (only used for debug rendering)
        private readonly List<Vector2> bodyDebugDimensions = new List<Vector2>();

#if DEBUG
        [Serialize(false, IsPropertySaveable.Yes), Editable]
#else
        [Serialize(false, IsPropertySaveable.Yes)]
#endif
        public bool Indestructible
        {
            get;
            set;
        }

        //sections of the wall that are supposed to be rendered
        public WallSection[] Sections
        {
            get;
            private set;
        }

        public override Sprite Sprite
        {
            get { return base.Prefab.Sprite; }
        }

        public bool IsPlatform
        {
            get { return Prefab.Platform; }
        }

        public Direction StairDirection
        {
            get;
            private set;
        }

        public override string Name
        {
            get { return base.Prefab.Name.Value; }
        }

        public bool HasBody
        {
            get { return Prefab.Body; }
        }

        public List<Body> Bodies { get; private set; }

        public bool CastShadow
        {
            get { return Prefab.CastShadow; }
        }

        public bool IsHorizontal { get; private set; }

        public int SectionCount
        {
            get { return Sections.Length; }
        }

        private float? maxHealth;

        [Serialize(100.0f, IsPropertySaveable.Yes), Editable(MinValueFloat = 0)]
        public float MaxHealth
        {
            get => maxHealth ?? Prefab.Health;
            set => maxHealth = value;
        }

        private float crushDepth;

        [Serialize(Level.DefaultRealWorldCrushDepth, IsPropertySaveable.Yes)]
        public float CrushDepth
        {
            get => crushDepth;
            set => crushDepth = Math.Max(value, Level.DefaultRealWorldCrushDepth);
        }

        public float Health => MaxHealth;

        public override bool DrawBelowWater
        {
            get
            {
                return base.DrawBelowWater || Prefab.BackgroundSprite != null;
            }
        }

        public override bool DrawOverWater
        {
            get
            {
                return (Sprite == null || SpriteDepth <= 0.5f) && !DrawDamageEffect;
            }
        }

        public bool DrawDamageEffect
        {
            get
            {
                return Prefab.Body && !IsPlatform;// && HasDamage;
            }
        }

        public bool HasDamage
        {
            get;
            private set;
        }

        public new StructurePrefab Prefab => base.Prefab as StructurePrefab;

        public ImmutableHashSet<Identifier> Tags => Prefab.Tags;

        protected Color spriteColor;
        [Editable, Serialize("1.0,1.0,1.0,1.0", IsPropertySaveable.Yes)]
        public Color SpriteColor
        {
            get { return spriteColor; }
            set { spriteColor = value; }
        }

        [Editable, Serialize(false, IsPropertySaveable.Yes)]
        public bool UseDropShadow
        {
            get;
            private set;
        }

        [Editable, Serialize("0,0", IsPropertySaveable.Yes, description: "The position of the drop shadow relative to the structure. If set to zero, the shadow is positioned automatically so that it points towards the sub's center of mass.")]
        public Vector2 DropShadowOffset
        {
            get;
            private set;
        }

        private float scale = 1.0f;
        public override float Scale
        {
            get { return scale; }
            set
            {
                if (scale == value) { return; }
                scale = MathHelper.Clamp(value, 0.1f, 10.0f);

                float relativeScale = scale / base.Prefab.Scale;

                if (!ResizeHorizontal || !ResizeVertical)
                {
                    int newWidth = Math.Max(ResizeHorizontal ? rect.Width : (int)(defaultRect.Width * relativeScale), 1);
                    int newHeight = Math.Max(ResizeVertical ? rect.Height : (int)(defaultRect.Height * relativeScale), 1);
                    Rect = new Rectangle(rect.X, rect.Y, newWidth, newHeight);
                    if (StairDirection != Direction.None)
                    {
                        CreateStairBodies();
                    }
                    else if (Sections != null)
                    {
                        UpdateSections();
                    }
                }

#if CLIENT
                foreach (LightSource light in Lights)
                {
                    light.SpriteScale = scale * textureScale;
                }
#endif
            }
        }


        protected Vector2 textureScale = Vector2.One;

        [Editable(DecimalCount = 3, MinValueFloat = 0.01f, MaxValueFloat = 10f, ValueStep = 0.1f), Serialize("1.0, 1.0", IsPropertySaveable.No)]
        public Vector2 TextureScale
        {
            get { return textureScale; }
            set
            {
                textureScale = new Vector2(
                    MathHelper.Clamp(value.X, 0.01f, 10),
                    MathHelper.Clamp(value.Y, 0.01f, 10));

#if CLIENT
                foreach (LightSource light in Lights)
                {
                    light.LightTextureScale = textureScale * scale;
                }
#endif
            }
        }

        protected Vector2 textureOffset = Vector2.Zero;
        [Editable(MinValueFloat = -1000f, MaxValueFloat = 1000f, ValueStep = 10f), Serialize("0.0, 0.0", IsPropertySaveable.Yes)]
        public Vector2 TextureOffset
        {
            get { return textureOffset; }
            set
            {
                textureOffset = value;
#if CLIENT
                SetLightTextureOffset();
#endif
            }
        }


        private Rectangle defaultRect;
        /// <summary>
        /// Unscaled rect
        /// </summary>
        public Rectangle DefaultRect
        {
            get { return defaultRect; }
            set { defaultRect = value; }
        }

        public override Rectangle Rect
        {
            get
            {
                return base.Rect;
            }
            set
            {
                Rectangle oldRect = Rect;
                base.Rect = value;
                if (Prefab.Body)
                {
                    CreateSections();
                    UpdateSections();
                }
                else
                {
                    if (Sections == null) { return; }
                    foreach (WallSection sec in Sections)
                    {
                        Rectangle secRect = sec.rect;
                        secRect.X -= oldRect.X; secRect.Y -= oldRect.Y;
                        secRect.X *= value.Width; secRect.X /= oldRect.Width;
                        secRect.Y *= value.Height; secRect.Y /= oldRect.Height;
                        secRect.Width *= value.Width; secRect.Width /= oldRect.Width;
                        secRect.Height *= value.Height; secRect.Height /= oldRect.Height;
                        secRect.X += value.X; secRect.Y += value.Y;
                        sec.rect = secRect;
                    }
                }
            }
        }

        public float BodyWidth
        {
            get { return Prefab.BodyWidth > 0.0f ? Prefab.BodyWidth * scale : rect.Width; }
        }
        public float BodyHeight
        {
            get { return Prefab.BodyHeight > 0.0f ? Prefab.BodyHeight * scale : rect.Height; }
        }

        /// <summary>
        /// In radians, takes flipping into account
        /// </summary>
        public float BodyRotation
        {
            get
            {
                float rotation = MathHelper.ToRadians(Prefab.BodyRotation);
                if (FlippedX) rotation = -MathHelper.Pi - rotation;
                if (FlippedY) rotation = -rotation;
                return rotation;
            }
        }
        /// <summary>
        /// Offset of the physics body from the center of the structure. Takes flipping into account.
        /// </summary>
        public Vector2 BodyOffset
        {
            get
            {
                Vector2 bodyOffset = Prefab.BodyOffset;
                if (FlippedX) { bodyOffset.X = -bodyOffset.X; }
                if (FlippedY) { bodyOffset.Y = -bodyOffset.Y; }
                return bodyOffset;
            }
        }

        [Serialize(false, IsPropertySaveable.Yes), Editable]
        public bool NoAITarget
        {
            get;
            private set;
        }

        public Dictionary<Identifier, SerializableProperty> SerializableProperties
        {
            get;
            private set;
        }

        public override void Move(Vector2 amount, bool ignoreContacts = false)
        {
            if (!MathUtils.IsValid(amount))
            {
                DebugConsole.ThrowError($"Attempted to move a structure by an invalid amount ({amount})\n{Environment.StackTrace.CleanupStackTrace()}");
                return;
            }

            base.Move(amount);

            for (int i = 0; i < Sections.Length; i++)
            {
                Rectangle r = Sections[i].rect;
                r.X += (int)amount.X;
                r.Y += (int)amount.Y;
                Sections[i].rect = r;
            }

            if (Bodies != null)
            {
                Vector2 simAmount = ConvertUnits.ToSimUnits(amount);
                foreach (Body b in Bodies)
                {
                    Vector2 pos = b.Position + simAmount;
                    if (ignoreContacts)
                    {
                        b.SetTransformIgnoreContacts(ref pos, b.Rotation);
                    }
                    else
                    {
                        b.SetTransform(pos, b.Rotation);
                    }
                }
            }

#if CLIENT
            convexHulls?.ForEach(x => x.Move(amount));

            foreach (LightSource light in Lights)
            {
                light.LightTextureTargetSize = rect.Size.ToVector2();
                light.Position = rect.Location.ToVector2();
            }
#endif
        }

        public Structure(Rectangle rectangle, StructurePrefab sp, Submarine submarine, ushort id = Entity.NullEntityID, XElement element = null)
            : base(sp, submarine, id)
        {
            System.Diagnostics.Debug.Assert(rectangle.Width > 0 && rectangle.Height > 0);
            if (rectangle.Width == 0 || rectangle.Height == 0) { return; }
            defaultRect = rectangle;

            maxHealth = sp.Health;

            rect = rectangle;
            TextureScale = sp.TextureScale;

            spriteColor = base.Prefab.SpriteColor;
            if (sp.IsHorizontal.HasValue)
            {
                IsHorizontal = sp.IsHorizontal.Value;
            }
            else if (ResizeHorizontal && !ResizeVertical)
            {
                IsHorizontal = true;
            }
            else if (ResizeVertical && !ResizeHorizontal)
            {
                IsHorizontal = false;
            }
            else
            {
                if (BodyWidth > 0.0f && BodyHeight > 0.0f)
                {
                    IsHorizontal = BodyWidth > BodyHeight;
                }
                else
                {
                    IsHorizontal = (rect.Width > rect.Height);
                }
            }

            StairDirection = Prefab.StairDirection;
            NoAITarget = Prefab.NoAITarget;

            InitProjSpecific();

            if (!HiddenInGame)
            {
                if (Prefab.Body)
                {
                    Bodies = new List<Body>();
                    WallList.Add(this);

                    CreateSections();
                    UpdateSections();
                }
                else
                {
                    Sections = new WallSection[1];
                    Sections[0] = new WallSection(rect, this);

                    if (StairDirection != Direction.None)
                    {
                        CreateStairBodies();
                    }
                }
            }

            SerializableProperties = element != null ? SerializableProperty.DeserializeProperties(this, element) : SerializableProperty.GetProperties(this);

#if CLIENT
            foreach (var subElement in sp.ConfigElement.Elements())
            {
                if (subElement.Name.ToString().Equals("light", StringComparison.OrdinalIgnoreCase))
                {
                    Vector2 pos = rect.Location.ToVector2();
                    pos.Y += rect.Height;
                    LightSource light = new LightSource(subElement)
                    {
                        ParentSub = Submarine,
                        Position = rect.Location.ToVector2(),
                        CastShadows = false,
                        IsBackground = false,
                        Color = subElement.GetAttributeColor("lightcolor", Color.White),
                        SpriteScale = Vector2.One,
                        Range = 0,
                        LightTextureTargetSize = rect.Size.ToVector2(),
                        LightTextureScale = textureScale * scale,
                        LightSourceParams =
                        {
                            Flicker = subElement.GetAttributeFloat("flicker", 0f),
                            FlickerSpeed = subElement.GetAttributeFloat("flickerspeed", 0f),
                            PulseAmount = subElement.GetAttributeFloat("pulseamount", 0f),
                            PulseFrequency = subElement.GetAttributeFloat("pulsefrequency", 0f),
                            BlinkFrequency = subElement.GetAttributeFloat("blinkfrequency", 0f)
                        }
                    };

                    Lights.Add(light);

                    SetLightTextureOffset();
                }
            }
#endif

            // Only add ai targets automatically to submarine/outpost walls
            if (aiTarget == null && HasBody && Tags.Contains("wall") && submarine != null && !submarine.Info.IsWreck && !NoAITarget)
            {
                aiTarget = new AITarget(this)
                {
                    MinSightRange = 1000,
                    MaxSightRange = 4000,
                    MaxSoundRange = 0
                };
            }

            InsertToList();

            DebugConsole.Log("Created " + Name + " (" + ID + ")");
        }

        partial void InitProjSpecific();

        public override string ToString()
        {
            return Name;
        }

        public override MapEntity Clone()
        {
            var clone = new Structure(rect, Prefab, Submarine)
            {
                defaultRect = defaultRect
            };
            foreach (KeyValuePair<Identifier, SerializableProperty> property in SerializableProperties)
            {
                if (!property.Value.Attributes.OfType<Editable>().Any()) { continue; }
                clone.SerializableProperties[property.Key].TrySetValue(clone, property.Value.GetValue(this));
            }
            if (FlippedX) clone.FlipX(false);
            if (FlippedY) clone.FlipY(false);

            return clone;
        }

        private void CreateStairBodies()
        {
            Bodies = new List<Body>();
            bodyDebugDimensions.Clear();

            float stairAngle = MathHelper.ToRadians(Math.Min(Prefab.StairAngle, 75.0f));

            float bodyWidth = ConvertUnits.ToSimUnits(rect.Width / Math.Cos(stairAngle));
            float bodyHeight = ConvertUnits.ToSimUnits(10);

            float stairHeight = rect.Width * (float)Math.Tan(stairAngle);

            Body newBody = GameMain.World.CreateRectangle(bodyWidth, bodyHeight, 1.5f);

            newBody.BodyType = BodyType.Static;
            Vector2 stairPos = new Vector2(Position.X, rect.Y - rect.Height + stairHeight / 2.0f);
            newBody.Rotation = (StairDirection == Direction.Right) ? stairAngle : -stairAngle;
            newBody.CollisionCategories = Physics.CollisionStairs;
            newBody.Friction = 0.8f;
            newBody.UserData = this;

            newBody.Position = ConvertUnits.ToSimUnits(stairPos) + BodyOffset * Scale;

            bodyDebugDimensions.Add(new Vector2(bodyWidth, bodyHeight));

            Bodies.Add(newBody);
        }

        private void CreateSections()
        {
            int xsections = 1, ysections = 1;
            int width = rect.Width, height = rect.Height;

            if (!HasBody)
            {
                if (FlippedX && IsHorizontal)
                {
                    xsections = (int)Math.Ceiling((float)rect.Width / base.Prefab.Sprite.SourceRect.Width);
                    width = base.Prefab.Sprite.SourceRect.Width;
                }
                else if (FlippedY && !IsHorizontal)
                {
                    ysections = (int)Math.Ceiling((float)rect.Height / base.Prefab.Sprite.SourceRect.Height);
                    width = base.Prefab.Sprite.SourceRect.Height;
                }
                else
                {
                    xsections = 1;
                    ysections = 1;
                }
                Sections = new WallSection[xsections];
            }
            else
            {
                if (IsHorizontal)
                {
                    //equivalent to (int)Math.Ceiling((double)rect.Width / WallSectionSize) without the potential for floating point indeterminism
                    xsections = (rect.Width + WallSectionSize - 1) / WallSectionSize;
                    Sections = new WallSection[xsections];
                    width = WallSectionSize;
                }
                else
                {
                    ysections = (rect.Height + WallSectionSize - 1) / WallSectionSize;
                    Sections = new WallSection[ysections];
                    height = WallSectionSize;
                }
            }

            for (int x = 0; x < xsections; x++)
            {
                for (int y = 0; y < ysections; y++)
                {
                    if (FlippedX || FlippedY)
                    {
                        Rectangle sectionRect = new Rectangle(
                            FlippedX ? rect.Right - (x + 1) * width : rect.X + x * width,
                            FlippedY ? rect.Y - rect.Height + (y + 1) * height : rect.Y - y * height,
                            width, height);

                        if (FlippedX)
                        {
                            int over = Math.Max(rect.X - sectionRect.X, 0);
                            sectionRect.X += over;
                            sectionRect.Width -= over;
                        }
                        else
                        {
                            sectionRect.Width -= (int)Math.Max(sectionRect.Right - rect.Right, 0.0f);
                        }
                        if (FlippedY)
                        {
                            int over = Math.Max(sectionRect.Y - rect.Y, 0);
                            sectionRect.Y -= over;
                            sectionRect.Height -= over;
                        }
                        else
                        {
                            sectionRect.Height -= (int)Math.Max((rect.Y - rect.Height) - (sectionRect.Y - sectionRect.Height), 0.0f);
                        }

                        //sectionRect.Height -= (int)Math.Max((rect.Y - rect.Height) - (sectionRect.Y - sectionRect.Height), 0.0f);
                        int xIndex = FlippedX && IsHorizontal ? (xsections - 1 - x) : x;
                        int yIndex = FlippedY && !IsHorizontal ? (ysections - 1 - y) : y;
                        Sections[xIndex + yIndex] = new WallSection(sectionRect, this);
                    }
                    else
                    {
                        Rectangle sectionRect = new Rectangle(rect.X + x * width, rect.Y - y * height, width, height);
                        sectionRect.Width -= (int)Math.Max(sectionRect.Right - rect.Right, 0.0f);
                        sectionRect.Height -= (int)Math.Max((rect.Y - rect.Height) - (sectionRect.Y - sectionRect.Height), 0.0f);

                        Sections[x + y] = new WallSection(sectionRect, this);
                    }
                }
            }
        }

        private Rectangle GenerateMergedRect(List<WallSection> mergedSections)
        {
            if (IsHorizontal)
               return new Rectangle(mergedSections.Min(x => x.rect.Left), mergedSections.Max(x => x.rect.Top),
                    mergedSections.Sum(x => x.rect.Width), mergedSections.First().rect.Height);
            else
            {
                return new Rectangle(mergedSections.Min(x => x.rect.Left), mergedSections.Max(x => x.rect.Top),
                    mergedSections.First().rect.Width, mergedSections.Sum(x => x.rect.Height));
            }
        }

        private static Vector2[] CalculateExtremes(Rectangle sectionRect)
        {
            Vector2[] corners = new Vector2[4];
            corners[0] = new Vector2(sectionRect.X, sectionRect.Y - sectionRect.Height);
            corners[1] = new Vector2(sectionRect.X, sectionRect.Y);
            corners[2] = new Vector2(sectionRect.Right, sectionRect.Y);
            corners[3] = new Vector2(sectionRect.Right, sectionRect.Y - sectionRect.Height);

            return corners;
        }

        /// <summary>
        /// Checks if there's a structure items can be attached to at the given position and returns it.
        /// </summary>
        public static Structure GetAttachTarget(Vector2 worldPosition)
        {
            foreach (MapEntity mapEntity in mapEntityList)
            {
                if (!(mapEntity is Structure structure)) { continue; }
                if (!structure.Prefab.AllowAttachItems) { continue; }
                if (structure.Bodies != null && structure.Bodies.Count > 0) { continue; }
                Rectangle worldRect = mapEntity.WorldRect;
                if (worldPosition.X < worldRect.X || worldPosition.X > worldRect.Right) { continue; }
                if (worldPosition.Y > worldRect.Y || worldPosition.Y < worldRect.Y - worldRect.Height) { continue; }
                return structure;
            }
            return null;
        }

        public override bool IsMouseOn(Vector2 position)
        {
            if (!base.IsMouseOn(position)) { return false; }

            if (StairDirection == Direction.None)
            {
                Vector2 rectSize = rect.Size.ToVector2();
                if (BodyWidth > 0.0f) { rectSize.X = BodyWidth; }
                if (BodyHeight > 0.0f) { rectSize.Y = BodyHeight; }

                Vector2 bodyPos = WorldPosition + BodyOffset * Scale;

                Vector2 transformedMousePos = MathUtils.RotatePointAroundTarget(position, bodyPos, BodyRotation);

                return
                    Math.Abs(transformedMousePos.X - bodyPos.X) < rectSize.X / 2.0f &&
                    Math.Abs(transformedMousePos.Y - bodyPos.Y) < rectSize.Y / 2.0f;
            }
            else
            {

                if (StairDirection == Direction.Left)
                {
                    return MathUtils.LineToPointDistanceSquared(new Vector2(WorldRect.X, WorldRect.Y), new Vector2(WorldRect.Right, WorldRect.Y - WorldRect.Height), position) < 1600.0f;
                }
                else
                {
                    return MathUtils.LineToPointDistanceSquared(new Vector2(WorldRect.X, WorldRect.Y - rect.Height), new Vector2(WorldRect.Right, WorldRect.Y), position) < 1600.0f;
                }
            }
        }

        public override void ShallowRemove()
        {
            base.ShallowRemove();

            if (WallList.Contains(this)) WallList.Remove(this);

            if (Bodies != null)
            {
                foreach (Body b in Bodies)
                {
                    GameMain.World.Remove(b);
                }
                Bodies.Clear();
            }

            if (Sections != null)
            {
                foreach (WallSection s in Sections)
                {
                    if (s.gap != null)
                    {
                        s.gap.Remove();
                        s.gap = null;
                    }
                }
            }

#if CLIENT
            if (convexHulls != null) convexHulls.ForEach(x => x.Remove());
            foreach (LightSource light in Lights)
            {
                light.Remove();
            }
#endif
        }

        public override void Remove()
        {
            base.Remove();

            if (WallList.Contains(this)) WallList.Remove(this);

            if (Bodies != null)
            {
                foreach (Body b in Bodies)
                {
                    GameMain.World.Remove(b);
                }
                Bodies.Clear();
            }

            if (Sections != null)
            {
                foreach (WallSection s in Sections)
                {
                    if (s.gap != null)
                    {
                        s.gap.Remove();
                        s.gap = null;
                    }
                }
            }

#if CLIENT
            if (convexHulls != null) convexHulls.ForEach(x => x.Remove());
            foreach (LightSource light in Lights)
            {
                light.Remove();
            }
#endif
        }

        private bool OnWallCollision(Fixture f1, Fixture f2, Contact contact)
        {
            if (Prefab.Platform)
            {
                if (f2.Body.UserData is Limb limb)
                {
                    if (limb.character.AnimController.IgnorePlatforms) return false;
                }
            }

            if (f2.Body.UserData is Limb)
            {
                var character = ((Limb)f2.Body.UserData).character;
                if (character.DisableImpactDamageTimer > 0.0f || ((Limb)f2.Body.UserData).Mass < 100.0f) return true;
            }

            OnImpactProjSpecific(f1, f2, contact);

            return true;
        }

        partial void OnImpactProjSpecific(Fixture f1, Fixture f2, Contact contact);

        public WallSection GetSection(int sectionIndex)
        {
            if (sectionIndex < 0 || sectionIndex >= Sections.Length) return null;

            return Sections[sectionIndex];

        }

        public bool SectionBodyDisabled(int sectionIndex)
        {
            if (sectionIndex < 0 || sectionIndex >= Sections.Length) return false;

            return (Sections[sectionIndex].damage >= MaxHealth);
        }

        /// <summary>
        /// Sections that are leaking have a gap placed on them
        /// </summary>
        public bool SectionIsLeaking(int sectionIndex)
        {
            if (sectionIndex < 0 || sectionIndex >= Sections.Length) return false;

            return (Sections[sectionIndex].damage >= MaxHealth * LeakThreshold);
        }

        public int SectionLength(int sectionIndex)
        {
            if (sectionIndex < 0 || sectionIndex >= Sections.Length) return 0;

            return (IsHorizontal ? Sections[sectionIndex].rect.Width : Sections[sectionIndex].rect.Height);
        }

        public override bool AddUpgrade(Upgrade upgrade, bool createNetworkEvent = false)
        {
            if (!upgrade.Prefab.IsWallUpgrade) { return false; }

            Upgrade existingUpgrade = GetUpgrade(upgrade.Identifier);

            if (existingUpgrade != null)
            {
                existingUpgrade.Level += upgrade.Level;
                existingUpgrade.ApplyUpgrade();
                upgrade.Dispose();
            }
            else
            {
                Upgrades.Add(upgrade);
                upgrade.ApplyUpgrade();
            }

            UpdateSections();

            return true;
        }

        public void AddDamage(int sectionIndex, float damage, Character attacker = null, bool emitParticles = true)
        {
            if (!Prefab.Body || Prefab.Platform || Indestructible) { return; }

            if (sectionIndex < 0 || sectionIndex > Sections.Length - 1) { return; }

            var section = Sections[sectionIndex];

#if CLIENT
            if (damage > 0 && emitParticles)
            {
                float dmg = Math.Min(MaxHealth - section.damage, damage);
                float particleAmount = MathHelper.Lerp(0, 25, MathUtils.InverseLerp(0, 100, dmg * Rand.Range(0.75f, 1.25f)));
                // Special case for very low but frequent dmg like plasma cutter: 10% chance for emitting a particle
                if (particleAmount < 1 && Rand.Value() < 0.10f)
                {
                    particleAmount = 1;
                }
                for (int i = 1; i <= particleAmount; i++)
                {
                    var worldRect = section.WorldRect;
                    Vector2 particlePos = new Vector2(
                        Rand.Range(worldRect.X, worldRect.Right + 1),
                        Rand.Range(worldRect.Y - worldRect.Height, worldRect.Y + 1));

                    var particle = GameMain.ParticleManager.CreateParticle("shrapnel", particlePos, Rand.Vector(Rand.Range(1.0f, 50.0f)), collisionIgnoreTimer: 1f);
                    if (particle == null) break;
                }
            }
#endif
            if (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer)
            {
                SetDamage(sectionIndex, section.damage + damage, attacker);
            }
        }

        public int FindSectionIndex(Vector2 displayPos, bool world = false, bool clamp = false)
        {
            if (Sections.None()) { return -1; }

            if (world && Submarine != null)
            {
                displayPos -= Submarine.Position;
            }

            //if the sub has been flipped horizontally, the first section may be smaller than wallSectionSize
            //and we need to adjust the position accordingly
            if (Sections[0].rect.Width < WallSectionSize)
            {
                displayPos.X += WallSectionSize - Sections[0].rect.Width;
            }

            int index = IsHorizontal ?
                (int)Math.Floor((displayPos.X - rect.X) / WallSectionSize) :
                (int)Math.Floor((rect.Y - displayPos.Y) / WallSectionSize);

            if (clamp)
            {
                index = MathHelper.Clamp(index, 0, Sections.Length - 1);
            }
            else if (index < 0 || index > Sections.Length - 1)
            {
                return -1;
            }
            return index;
        }

        public float SectionDamage(int sectionIndex)
        {
            if (sectionIndex < 0 || sectionIndex >= Sections.Length) return 0.0f;

            return Sections[sectionIndex].damage;
        }

        public Vector2 SectionPosition(int sectionIndex, bool world = false)
        {
            if (sectionIndex < 0 || sectionIndex >= Sections.Length)
            {
                return Vector2.Zero;
            }

            if (Prefab.BodyRotation == 0.0f)
            {
                Vector2 sectionPos = new Vector2(
                    Sections[sectionIndex].rect.X + Sections[sectionIndex].rect.Width / 2.0f,
                    Sections[sectionIndex].rect.Y - Sections[sectionIndex].rect.Height / 2.0f);

                if (world && Submarine != null)
                {
                    sectionPos += Submarine.Position;
                }
                return sectionPos;
            }
            else
            {
                Rectangle sectionRect = Sections[sectionIndex].rect;
                float diffFromCenter;
                if (IsHorizontal)
                {
                    diffFromCenter = (sectionRect.Center.X - rect.Center.X) / (float)rect.Width * BodyWidth;
                }
                else
                {
                    diffFromCenter = ((sectionRect.Y - sectionRect.Height / 2) - (rect.Y - rect.Height / 2)) / (float)rect.Height * BodyHeight;
                }
                if (FlippedX)
                {
                    diffFromCenter = -diffFromCenter;
                }

                Vector2 sectionPos = Position + new Vector2(
                    (float)Math.Cos(IsHorizontal ? -BodyRotation : MathHelper.PiOver2 - BodyRotation),
                    (float)Math.Sin(IsHorizontal ? -BodyRotation : MathHelper.PiOver2 - BodyRotation)) * diffFromCenter;

                if (world && Submarine != null)
                {
                    sectionPos += Submarine.Position;
                }
                return sectionPos;
            }
        }

        public AttackResult AddDamage(Character attacker, Vector2 worldPosition, Attack attack, float deltaTime, bool playSound = false)
        {
            if (Submarine != null && Submarine.GodMode) { return new AttackResult(0.0f, null); }
            if (!Prefab.Body || Prefab.Platform || Indestructible) { return new AttackResult(0.0f, null); }

            Vector2 transformedPos = worldPosition;
            if (Submarine != null) transformedPos -= Submarine.Position;

            float damageAmount = 0.0f;
            for (int i = 0; i < SectionCount; i++)
            {
                Rectangle sectionRect = Sections[i].rect;
                sectionRect.Y -= Sections[i].rect.Height;
                if (MathUtils.CircleIntersectsRectangle(transformedPos, attack.DamageRange, sectionRect))
                {
                    damageAmount = attack.GetStructureDamage(deltaTime);
                    AddDamage(i, damageAmount, attacker);
#if CLIENT
                    if (attack.EmitStructureDamageParticles)
                    {
                        GameMain.ParticleManager.CreateParticle("dustcloud", SectionPosition(i), 0.0f, 0.0f);
                    }
#endif
                }
            }
#if CLIENT
            if (playSound && damageAmount > 0)
            {
                string damageSound = Prefab.DamageSound;
                if (string.IsNullOrWhiteSpace(damageSound))
                {
                    damageSound = attack.StructureSoundType;
                }
                SoundPlayer.PlayDamageSound(damageSound, damageAmount, worldPosition, tags: Tags);
            }
#endif

            if (Submarine != null && damageAmount > 0 && attacker != null)
            {
                var abilityAttackerSubmarine = new AbilityAttackerSubmarine(attacker, Submarine);
                foreach (Character character in Character.CharacterList)
                {
                    character.CheckTalents(AbilityEffectType.AfterSubmarineAttacked, abilityAttackerSubmarine);
                }
            }

            return new AttackResult(damageAmount, null);
        }

        public void SetDamage(int sectionIndex, float damage, Character attacker = null, bool createNetworkEvent = true)
        {
            if (Submarine != null && Submarine.GodMode || Indestructible) { return; }
            if (!Prefab.Body) { return; }
            if (!MathUtils.IsValid(damage)) { return; }

            damage = MathHelper.Clamp(damage, 0.0f, MaxHealth - Prefab.MinHealth);

#if SERVER
            if (GameMain.Server != null && createNetworkEvent && damage != Sections[sectionIndex].damage)
            {
                GameMain.Server.CreateEntityEvent(this);
            }
            bool noGaps = true;
            for (int i = 0; i < Sections.Length; i++)
            {
                if (i != sectionIndex && SectionIsLeaking(i))
                {
                    noGaps = false;
                    break;
                }
            }
#endif

            if (damage < MaxHealth * LeakThreshold)
            {
                if (Sections[sectionIndex].gap != null)
                {
#if SERVER
                    //the structure doesn't have any other gap, log the structure being fixed
                    if (noGaps && attacker != null)
                    {
                        GameServer.Log((Sections[sectionIndex].gap.IsRoomToRoom ? "Inner" : "Outer") + " wall repaired by " + GameServer.CharacterLogName(attacker), ServerLog.MessageType.ItemInteraction);
                    }
#endif
                    DebugConsole.Log("Removing gap (ID " + Sections[sectionIndex].gap.ID + ", section: " + sectionIndex + ") from wall " + ID);

                    //remove existing gap if damage is below leak threshold
                    Sections[sectionIndex].gap.Open = 0.0f;
                    Sections[sectionIndex].gap.Remove();
                    Sections[sectionIndex].gap = null;
                }
            }
            else
            {
                if (Sections[sectionIndex].gap == null)
                {
                    Rectangle gapRect = Sections[sectionIndex].rect;
                    float diffFromCenter;
                    if (IsHorizontal)
                    {
                        diffFromCenter = (gapRect.Center.X - this.rect.Center.X) / (float)this.rect.Width * BodyWidth;
                        if (BodyWidth > 0.0f) { gapRect.Width = (int)(BodyWidth * (gapRect.Width / (float)this.rect.Width)); }
                        if (BodyHeight > 0.0f)
                        {
                            gapRect.Y = (gapRect.Y - gapRect.Height / 2) + (int)(BodyHeight / 2 + BodyOffset.Y * scale);
                            gapRect.Height = (int)BodyHeight;
                        }
                    }
                    else
                    {
                        diffFromCenter = ((gapRect.Y - gapRect.Height / 2) - (this.rect.Y - this.rect.Height / 2)) / (float)this.rect.Height * BodyHeight;
                        if (BodyWidth > 0.0f)
                        {
                            gapRect.X = gapRect.Center.X + (int)(-BodyWidth / 2 + BodyOffset.X * scale);
                            gapRect.Width = (int)BodyWidth;
                        }
                        if (BodyHeight > 0.0f) { gapRect.Height = (int)(BodyHeight * (gapRect.Height / (float)this.rect.Height)); }
                    }
                    if (FlippedX) { diffFromCenter = -diffFromCenter; }

                    if (Math.Abs(BodyRotation) > 0.01f)
                    {
                        Vector2 structureCenter = Position;
                        Vector2 gapPos = structureCenter + new Vector2(
                            (float)Math.Cos(IsHorizontal ? -BodyRotation : MathHelper.PiOver2 - BodyRotation),
                            (float)Math.Sin(IsHorizontal ? -BodyRotation : MathHelper.PiOver2 - BodyRotation)) * diffFromCenter + BodyOffset * scale;
                        gapRect = new Rectangle((int)(gapPos.X - gapRect.Width / 2), (int)(gapPos.Y + gapRect.Height / 2), gapRect.Width, gapRect.Height);
                    }

                    gapRect.X -= 10;
                    gapRect.Y += 10;
                    gapRect.Width += 20;
                    gapRect.Height += 20;

                    bool horizontalGap = !IsHorizontal;
                    if (Prefab.BodyRotation != 0.0f)
                    {
                        //rotation within a 90 deg sector (e.g. 100 -> 10, 190 -> 10, -10 -> 80)
                        float sectorizedRotation = MathUtils.WrapAngleTwoPi(BodyRotation) % MathHelper.PiOver2;
                        //diagonal if 30 < angle < 60
                        bool diagonal = sectorizedRotation > MathHelper.Pi / 6 && sectorizedRotation < MathHelper.Pi / 3;
                        //gaps on the lower half of a diagonal wall are horizontal, ones on the upper half are vertical
                        if (diagonal)
                        {
                            horizontalGap = gapRect.Y - gapRect.Height / 2 < Position.Y;
                            if (FlippedY) { horizontalGap = !horizontalGap; }
                        }
                    }

                    Sections[sectionIndex].gap = new Gap(gapRect, horizontalGap, Submarine);

                    //free the ID, because if we give gaps IDs we have to make sure they always match between the clients and the server and
                    //that clients create them in the correct order along with every other entity created/removed during the round
                    //which COULD be done via entityspawner, but it's unnecessary because we never access these gaps by ID
                    Sections[sectionIndex].gap.FreeID();
                    Sections[sectionIndex].gap.ShouldBeSaved = false;
                    Sections[sectionIndex].gap.ConnectedWall = this;
                    DebugConsole.Log("Created gap (ID " + Sections[sectionIndex].gap.ID + ", section: " + sectionIndex + ") on wall " + ID);
                    //AdjustKarma(attacker, 300);

#if SERVER
                    //the structure didn't have any other gaps yet, log the breach
                    if (noGaps && attacker != null)
                    {
                        GameServer.Log((Sections[sectionIndex].gap.IsRoomToRoom ? "Inner" : "Outer") + " wall breached by " + GameServer.CharacterLogName(attacker), ServerLog.MessageType.ItemInteraction);
                    }
#endif
                }

                float gapOpen = MaxHealth <= 0.0f ? 0.0f : (damage / MaxHealth - LeakThreshold) * (1.0f / (1.0f - LeakThreshold));
                Sections[sectionIndex].gap.Open = gapOpen;
            }

            float damageDiff = damage - Sections[sectionIndex].damage;
            bool hadHole = SectionBodyDisabled(sectionIndex);
            Sections[sectionIndex].damage = MathHelper.Clamp(damage, 0.0f, MaxHealth);
            HasDamage = Sections.Any(s => s.damage > 0.0f);

            if (attacker != null && damageDiff != 0.0f)
            {
                HumanAIController.StructureDamaged(this, damageDiff, attacker);
                OnHealthChangedProjSpecific(attacker, damageDiff);
                if (GameMain.NetworkMember == null || !GameMain.NetworkMember.IsClient)
                {
                    if (damageDiff < 0.0f)
                    {
                        attacker.Info?.IncreaseSkillLevel("mechanical".ToIdentifier(),
                            -damageDiff * SkillSettings.Current.SkillIncreasePerRepairedStructureDamage / Math.Max(attacker.GetSkillLevel("mechanical"), 1.0f));
                    }
                }
            }

            bool hasHole = SectionBodyDisabled(sectionIndex);

            if (hadHole == hasHole) { return; }

            UpdateSections();
        }

        partial void OnHealthChangedProjSpecific(Character attacker, float damageAmount);

        public void SetCollisionCategory(Category collisionCategory)
        {
            if (Bodies == null) return;
            foreach (Body body in Bodies)
            {
                body.CollisionCategories = collisionCategory;
            }
        }

        private void UpdateSections()
        {
            if (Bodies == null) { return; }
            foreach (Body b in Bodies)
            {
                GameMain.World.Remove(b);
            }
            Bodies.Clear();
            bodyDebugDimensions.Clear();
#if CLIENT
            convexHulls?.ForEach(ch => ch.Remove());
            convexHulls?.Clear();
#endif

            bool hasHoles = false;
            var mergedSections = new List<WallSection>();
            for (int i = 0; i < Sections.Length; i++ )
            {
                // if there is a gap and we have sections to merge, do it.
                if (SectionBodyDisabled(i))
                {
                    hasHoles = true;

                    if (!mergedSections.Any()) continue;
                    var mergedRect = GenerateMergedRect(mergedSections);
                    mergedSections.Clear();
                    CreateRectBody(mergedRect, createConvexHull: true);
                }
                else
                {
                    mergedSections.Add(Sections[i]);
                }
            }

            // take care of any leftover pieces
            if (mergedSections.Count > 0)
            {
                var mergedRect = GenerateMergedRect(mergedSections);
                CreateRectBody(mergedRect, createConvexHull: true);
            }

            //if the section has holes (or is just one big hole with no bodies),
            //we need a sensor for repairtools to be able to target the structure
            if (hasHoles || !Bodies.Any())
            {
                Body sensorBody = CreateRectBody(rect, createConvexHull: false);
                sensorBody.CollisionCategories = Physics.CollisionRepair;
                sensorBody.SetIsSensor(true);
            }
        }

        private Body CreateRectBody(Rectangle rect, bool createConvexHull)
        {
            float diffFromCenter;
            if (IsHorizontal)
            {
                diffFromCenter = (rect.Center.X - this.rect.Center.X) / (float)this.rect.Width * BodyWidth;
                if (BodyWidth > 0.0f) rect.Width = Math.Max((int)Math.Round(BodyWidth * (rect.Width / (float)this.rect.Width)), 1);
                if (BodyHeight > 0.0f) rect.Height = (int)BodyHeight;
            }
            else
            {
                diffFromCenter = ((rect.Y - rect.Height / 2) - (this.rect.Y - this.rect.Height / 2)) / (float)this.rect.Height * BodyHeight;
                if (BodyWidth > 0.0f) rect.Width = (int)BodyWidth;
                if (BodyHeight > 0.0f) rect.Height = Math.Max((int)Math.Round(BodyHeight * (rect.Height / (float)this.rect.Height)), 1);
            }
            if (FlippedX) { diffFromCenter = -diffFromCenter; }

            Vector2 bodyOffset = ConvertUnits.ToSimUnits(Prefab.BodyOffset) * scale;
            if (FlippedX) { bodyOffset.X = -bodyOffset.X; }
            if (FlippedY) { bodyOffset.Y = -bodyOffset.Y; }

            Body newBody = GameMain.World.CreateRectangle(
                ConvertUnits.ToSimUnits(rect.Width),
                ConvertUnits.ToSimUnits(rect.Height),
                1.5f, 
                bodyType: BodyType.Static,
                findNewContacts: false);
            newBody.Friction = 0.5f;
            newBody.OnCollision += OnWallCollision;
            newBody.CollisionCategories = (Prefab.Platform) ? Physics.CollisionPlatform : Physics.CollisionWall;
            newBody.UserData = this;

            Vector2 structureCenter = ConvertUnits.ToSimUnits(Position);
            if (BodyRotation != 0.0f)
            {
                Vector2 pos = structureCenter + bodyOffset + new Vector2(
                    (float)Math.Cos(IsHorizontal ? -BodyRotation : MathHelper.PiOver2 - BodyRotation),
                    (float)Math.Sin(IsHorizontal ? -BodyRotation : MathHelper.PiOver2 - BodyRotation))
                        * ConvertUnits.ToSimUnits(diffFromCenter);
                newBody.SetTransformIgnoreContacts(ref pos, -BodyRotation);
            }
            else
            {
                Vector2 pos = structureCenter + (IsHorizontal ? Vector2.UnitX : Vector2.UnitY) * ConvertUnits.ToSimUnits(diffFromCenter) + bodyOffset;
                newBody.SetTransformIgnoreContacts(ref pos, newBody.Rotation);
            }

            if (createConvexHull)
            {
                CreateConvexHull(ConvertUnits.ToDisplayUnits(newBody.Position), rect.Size.ToVector2(), newBody.Rotation);
            }

            Bodies.Add(newBody);
            bodyDebugDimensions.Add(new Vector2(ConvertUnits.ToSimUnits(rect.Width), ConvertUnits.ToSimUnits(rect.Height)));

            return newBody;
        }

        partial void CreateConvexHull(Vector2 position, Vector2 size, float rotation);

        public override void FlipX(bool relativeToSub)
        {
            base.FlipX(relativeToSub);

#if CLIENT
            if (Prefab.CanSpriteFlipX)
            {
                SpriteEffects ^= SpriteEffects.FlipHorizontally;
            }
#endif

            if (StairDirection != Direction.None)
            {
                StairDirection = StairDirection == Direction.Left ? Direction.Right : Direction.Left;
                Bodies.ForEach(b => GameMain.World.Remove(b));
                Bodies.Clear();
                bodyDebugDimensions.Clear();

                CreateStairBodies();
            }

            if (HasBody)
            {
                CreateSections();
                UpdateSections();
            }
        }

        public override void FlipY(bool relativeToSub)
        {
            base.FlipY(relativeToSub);

#if CLIENT
            if (Prefab.CanSpriteFlipY)
            {
                SpriteEffects ^= SpriteEffects.FlipVertically;
            }
#endif

            if (StairDirection != Direction.None)
            {
                StairDirection = StairDirection == Direction.Left ? Direction.Right : Direction.Left;
                Bodies.ForEach(b => GameMain.World.Remove(b));
                Bodies.Clear();
                bodyDebugDimensions.Clear();

                CreateStairBodies();
            }

            if (HasBody)
            {
                CreateSections();
                UpdateSections();
            }
        }

        public static Structure Load(ContentXElement element, Submarine submarine, IdRemap idRemap)
        {
            string name = element.GetAttribute("name").Value;
            Identifier identifier = element.GetAttributeIdentifier("identifier", "");

            StructurePrefab prefab = FindPrefab(name, identifier);
            if (prefab == null)
            {
                DebugConsole.ThrowError("Error loading structure - structure prefab \"" + name + "\" (identifier \"" + identifier + "\") not found.");
                return null;
            }

            Rectangle rect = element.GetAttributeRect("rect", Rectangle.Empty);
            Structure s = new Structure(rect, prefab, submarine, idRemap.GetOffsetId(element), element)
            {
                Submarine = submarine,
            };

            if (submarine?.Info.GameVersion != null)
            {
                SerializableProperty.UpgradeGameVersion(s, s.Prefab.ConfigElement, submarine.Info.GameVersion);
            }

            bool hasDamage = false;
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "section":
                        int index = subElement.GetAttributeInt("i", -1);
                        if (index == -1) { continue; }

                        if (index < 0 || index >= s.SectionCount)
                        {
                            string errorMsg = $"Error while loading structure \"{s.Name}\". Section damage index out of bounds. Index: {index}, section count: {s.SectionCount}.";
                            DebugConsole.ThrowError(errorMsg);
                            GameAnalyticsManager.AddErrorEventOnce("Structure.Load:SectionIndexOutOfBounds", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                        }
                        else
                        {
                            float damage = subElement.GetAttributeFloat("damage", 0.0f);
                            s.Sections[index].damage = damage;
                            hasDamage |= damage > 0.0f;
                        }
                        break;
                    case "upgrade":
                    {
                        var upgradeIdentifier = subElement.GetAttributeIdentifier("identifier", Identifier.Empty);
                        UpgradePrefab upgradePrefab = UpgradePrefab.Find(upgradeIdentifier);
                        int level = subElement.GetAttributeInt("level", 1);
                        if (upgradePrefab != null)
                        {
                            s.AddUpgrade(new Upgrade(s, upgradePrefab, level, subElement));
                        }
                        else
                        {
                            DebugConsole.ThrowError($"An upgrade with identifier \"{upgradeIdentifier}\" on {s.Name} was not found. " +
                                                    "It's effect will not be applied and won't be saved after the round ends.");
                        }
                        break;
                    }
                }
            }

            if (element.GetAttributeBool("flippedx", false)) { s.FlipX(false); }
            if (element.GetAttributeBool("flippedy", false)) { s.FlipY(false); }

            //structures with a body drop a shadow by default
            if (element.GetAttribute("usedropshadow") == null)
            {
                s.UseDropShadow = prefab.Body;
            }

            if (element.GetAttribute("noaitarget") == null)
            {
                s.NoAITarget = prefab.NoAITarget;
            }

            if (hasDamage)
            {
                s.UpdateSections();
            }

            return s;
        }

        public static StructurePrefab FindPrefab(string name, Identifier identifier)
        {
            StructurePrefab prefab = null;
            if (identifier.IsEmpty)
            {
                //legacy support:
                //1. attempt to find a prefab with an empty identifier and a matching name
                prefab = MapEntityPrefab.Find(name, "") as StructurePrefab;
                //2. not found, attempt to find a prefab with a matching name
                if (prefab == null) { prefab = MapEntityPrefab.Find(name) as StructurePrefab; }
                //3. not found, attempt to find a prefab that uses the previous name as an identifier
                if (prefab == null) { prefab = MapEntityPrefab.Find(null, name) as StructurePrefab; }
            }
            else
            {
                prefab = MapEntityPrefab.Find(null, identifier) as StructurePrefab;
            }
            return prefab;
        }

        public override XElement Save(XElement parentElement)
        {
            XElement element = new XElement("Structure");

            int width = ResizeHorizontal ? rect.Width : defaultRect.Width;
            int height = ResizeVertical ? rect.Height : defaultRect.Height;

            element.Add(
                new XAttribute("name", base.Prefab.Name),
                new XAttribute("identifier", base.Prefab.Identifier),
                new XAttribute("ID", ID),
                new XAttribute("rect",
                    (int)(rect.X - Submarine.HiddenSubPosition.X) + "," +
                    (int)(rect.Y - Submarine.HiddenSubPosition.Y) + "," +
                    width + "," + height));

            if (FlippedX) element.Add(new XAttribute("flippedx", true));
            if (FlippedY) element.Add(new XAttribute("flippedy", true));

            for (int i = 0; i < Sections.Length; i++)
            {
                if (Sections[i].damage == 0.0f) continue;
                var sectionElement =
                    new XElement("section",
                        new XAttribute("i", i),
                        new XAttribute("damage", Sections[i].damage));
                element.Add(sectionElement);
            }

            SerializableProperty.SerializeProperties(this, element);

            foreach (var upgrade in Upgrades)
            {
                upgrade.Save(element);
            }

            parentElement.Add(element);

            return element;
        }

        public override void OnMapLoaded()
        {
            for (int i = 0; i < Sections.Length; i++)
            {
                SetDamage(i, Sections[i].damage, createNetworkEvent: false);
            }
        }

        public virtual void Reset()
        {
            SerializableProperties = SerializableProperty.DeserializeProperties(this, Prefab.ConfigElement);
            MaxHealth = Prefab.Health;
            Sprite.ReloadXML();
            SpriteDepth = Sprite.Depth;
            NoAITarget = Prefab.NoAITarget;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (aiTarget != null)
            {
                aiTarget.SightRange = Submarine == null ? aiTarget.MinSightRange : MathHelper.Lerp(aiTarget.MinSightRange, aiTarget.MaxSightRange, Submarine.Velocity.Length() / 10);
            }
        }
    }

    class AbilityAttackerSubmarine : AbilityObject, IAbilityCharacter, IAbilitySubmarine
    {
        public AbilityAttackerSubmarine(Character character, Submarine submarine)
        {
            Character = character;
            Submarine = submarine;
        }
        public Character Character { get; set; }
        public Submarine Submarine { get; set; }
    }
}
