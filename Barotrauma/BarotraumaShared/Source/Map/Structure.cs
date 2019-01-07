using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using FarseerPhysics.Factories;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class WallSection
    {
        public Rectangle rect;
        public float damage;
        public Gap gap;
        
        public WallSection(Rectangle rect)
        {
            System.Diagnostics.Debug.Assert(rect.Width > 0 && rect.Height > 0);

            this.rect = rect;
            damage = 0.0f;
        }

        public WallSection(Rectangle rect, float damage)
        {
            System.Diagnostics.Debug.Assert(rect.Width > 0 && rect.Height > 0);

            this.rect = rect;
            this.damage = 0.0f;
        }
    }

    partial class Structure : MapEntity, IDamageable, IServerSerializable, ISerializableEntity
    {
        public const int WallSectionSize = 96;
        public static List<Structure> WallList = new List<Structure>();

        //how much mechanic skill increases per damage removed from the wall by welding
        public const float SkillIncreaseMultiplier = 0.005f;

        const float LeakThreshold = 0.1f;

        private StructurePrefab prefab;
        private SpriteEffects SpriteEffects = SpriteEffects.None;

        //dimensions of the wall sections' physics bodies (only used for debug rendering)
        private List<Vector2> bodyDebugDimensions = new List<Vector2>();

        //sections of the wall that are supposed to be rendered
        public WallSection[] Sections
        {
            get;
            private set;
        }        

        public override Sprite Sprite
        {
            get { return prefab.sprite; }
        }

        public bool IsPlatform
        {
            get { return prefab.Platform; }
        }

        public Direction StairDirection
        {
            get;
            private set;
        }

        public override string Name
        {
            get { return prefab.Name; }
        }

        public bool HasBody
        {
            get { return prefab.Body; }
        }
        
        public List<Body> Bodies { get; private set; }

        public bool CastShadow
        {
            get { return prefab.CastShadow; }
        }

        public bool IsHorizontal { get; private set; }

        public int SectionCount
        {
            get { return Sections.Length; }
        }

        public float Health
        {
            get { return prefab.Health; }
        }

        public override bool DrawBelowWater
        {
            get
            {
                return base.DrawBelowWater || prefab.BackgroundSprite != null;
            }
        }

        public override bool DrawOverWater
        {
            get
            {
                return !DrawDamageEffect;
            }
        }

        public override bool DrawDamageEffect
        {
            get
            {
                return prefab.Body;
            }
        }

        public StructurePrefab Prefab
        {
            get { return prefab; }
        }

        public HashSet<string> Tags
        {
            get { return prefab.Tags; }
        }

        protected Color spriteColor;
        [Editable, Serialize("1.0,1.0,1.0,1.0", true)]
        public Color SpriteColor
        {
            get { return spriteColor; }
            set { spriteColor = value; }
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
                if (prefab.Body) CreateSections();
                else
                {
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
            get { return prefab.BodyWidth > 0.0f ? prefab.BodyWidth : rect.Width; }
        }
        public float BodyHeight
        {
            get { return prefab.BodyHeight > 0.0f ? prefab.BodyHeight : rect.Height; }
        }

        /// <summary>
        /// In radians, takes flipping into account
        /// </summary>
        public float BodyRotation
        {
            get
            {
                float rotation = MathHelper.ToRadians(prefab.BodyRotation);
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

                Vector2 bodyOffset = prefab.BodyOffset;
                if (FlippedX) { bodyOffset.X = -bodyOffset.X; }
                if (FlippedY) { bodyOffset.Y = -bodyOffset.Y; }
                return bodyOffset;
            }
        }

        public Dictionary<string, SerializableProperty> SerializableProperties
        {
            get;
            private set;
        }

        public override void Move(Vector2 amount)
        {
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
                    b.SetTransform(b.Position + simAmount, b.Rotation);
                }
            }

#if CLIENT
            if (convexHulls!=null)
            {
                convexHulls.ForEach(x => x.Move(amount));
            }
#endif
        }

        public Structure(Rectangle rectangle, StructurePrefab sp, Submarine submarine)
            : base(sp, submarine)
        {
            if (rectangle.Width == 0 || rectangle.Height == 0) return;
            System.Diagnostics.Debug.Assert(rectangle.Width > 0 && rectangle.Height > 0);

            rect = rectangle;
            prefab = sp;

            spriteColor = prefab.SpriteColor;
            if (ResizeHorizontal && !ResizeVertical)
            {
                IsHorizontal = true;
            }
            else if (ResizeVertical && !ResizeHorizontal)
            {
                IsHorizontal = false;
            }
            else
            {
                IsHorizontal = (rect.Width > rect.Height);
            }

            StairDirection = prefab.StairDirection;

            SerializableProperties = SerializableProperty.GetProperties(this);
                      
            if (prefab.Body)
            {
                Bodies = new List<Body>();
                WallList.Add(this);

                CreateSections();
                UpdateSections();
            }
            else
            {
                Sections = new WallSection[1];
                Sections[0] = new WallSection(rect);

                if (StairDirection != Direction.None)
                {
                    CreateStairBodies();
                }
            }

#if CLIENT
            if (prefab.CastShadow)
            {
                GenerateConvexHull();
            }
#endif

            InsertToList();
        }

        public override string ToString()
        {
            return Name;
        }

        public override MapEntity Clone()
        {
            var clone = new Structure(rect, prefab, Submarine);
            foreach (KeyValuePair<string, SerializableProperty> property in SerializableProperties)
            {
                if (!property.Value.Attributes.OfType<Editable>().Any()) continue;
                clone.SerializableProperties[property.Key].TrySetValue(property.Value.GetValue());
            }
            if (FlippedX) clone.FlipX(false);
            if (FlippedY) clone.FlipY(false);
            return clone;
        }

        private void CreateStairBodies()
        {
            Bodies = new List<Body>();

            float bodyWidth = ConvertUnits.ToSimUnits(rect.Width * Math.Sqrt(2.0));
            float bodyHeight = ConvertUnits.ToSimUnits(10);

            Body newBody = BodyFactory.CreateRectangle(GameMain.World,
                bodyWidth, bodyHeight, 1.5f);

            newBody.BodyType = BodyType.Static;
            Vector2 stairPos = new Vector2(Position.X, rect.Y - rect.Height + rect.Width / 2.0f);
            /*stairPos += new Vector2(
                (StairDirection == Direction.Right) ? -Submarine.GridSize.X * 1.5f : Submarine.GridSize.X * 1.5f,
                -Submarine.GridSize.Y * 2.0f);*/
            newBody.Rotation = (StairDirection == Direction.Right) ? MathHelper.PiOver4 : -MathHelper.PiOver4;
            newBody.CollisionCategories = Physics.CollisionStairs;
            newBody.Friction = 0.8f;
            newBody.UserData = this;

            newBody.Position = ConvertUnits.ToSimUnits(stairPos) + BodyOffset;

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
                    xsections = (int)Math.Ceiling((float)rect.Width / prefab.sprite.SourceRect.Width);
                    width = prefab.sprite.SourceRect.Width;
                }
                else if (FlippedY && !IsHorizontal)
                {
                    ysections = (int)Math.Ceiling((float)rect.Height / prefab.sprite.SourceRect.Height);
                    width = prefab.sprite.SourceRect.Height;
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
                    xsections = (int)Math.Ceiling((float)rect.Width / WallSectionSize);
                    Sections = new WallSection[xsections];
                    width = (int)WallSectionSize;
                }
                else
                {
                    ysections = (int)Math.Ceiling((float)rect.Height / WallSectionSize);
                    Sections = new WallSection[ysections];
                    height = (int)WallSectionSize;
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
                        Sections[xIndex + yIndex] = new WallSection(sectionRect);
                    }
                    else
                    {
                        Rectangle sectionRect = new Rectangle(rect.X + x * width, rect.Y - y * height, width, height);
                        sectionRect.Width -= (int)Math.Max(sectionRect.Right - rect.Right, 0.0f);
                        sectionRect.Height -= (int)Math.Max((rect.Y - rect.Height) - (sectionRect.Y - sectionRect.Height), 0.0f);

                        Sections[x + y] = new WallSection(sectionRect);
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
                if (!(mapEntity is Structure structure)) continue;
                if (!structure.Prefab.AllowAttachItems) continue;
                if (structure.Bodies != null && structure.Bodies.Count > 0) continue;
                Rectangle worldRect = mapEntity.WorldRect;
                if (worldPosition.X < worldRect.X || worldPosition.X > worldRect.Right) continue;
                if (worldPosition.Y > worldRect.Y || worldPosition.Y < worldRect.Y - worldRect.Height) continue;
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

                Vector2 bodyPos = WorldPosition + BodyOffset;

                Vector2 transformedMousePos = MathUtils.RotatePointAroundTarget(position, bodyPos, MathHelper.ToDegrees(BodyRotation));

                return 
                    Math.Abs(transformedMousePos.X - bodyPos.X) < rectSize.X / 2.0f && 
                    Math.Abs(transformedMousePos.Y - bodyPos.Y) < rectSize.Y / 2.0f;
            }
            else
            {

                if (StairDirection == Direction.Left)
                {
                    return MathUtils.LineToPointDistance(new Vector2(WorldRect.X, WorldRect.Y), new Vector2(WorldRect.Right, WorldRect.Y - WorldRect.Height), position) < 40.0f;
                }
                else
                {
                    return MathUtils.LineToPointDistance(new Vector2(WorldRect.X, WorldRect.Y - rect.Height), new Vector2(WorldRect.Right, WorldRect.Y), position) < 40.0f;
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
                    GameMain.World.RemoveBody(b);
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
#endif
        }

        public override void Remove()
        {
            base.Remove();

            if (WallList.Contains(this)) WallList.Remove(this);

            if (Bodies != null)
            {
                foreach (Body b in Bodies)
                    GameMain.World.RemoveBody(b);
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
#endif
        }

        public override bool IsVisible(Rectangle WorldView)
        {
            Rectangle worldRect = WorldRect;

            if (worldRect.X > WorldView.Right || worldRect.Right < WorldView.X) return false;
            if (worldRect.Y < WorldView.Y - WorldView.Height || worldRect.Y - worldRect.Height > WorldView.Y) return false;

            return true;
        }

        private bool OnWallCollision(Fixture f1, Fixture f2, Contact contact)
        {
            if (prefab.Platform)
            {
                Limb limb;
                if ((limb = f2.Body.UserData as Limb) != null)
                {
                    if (limb.character.AnimController.IgnorePlatforms) return false;
                }
            }

            if (f2.Body.UserData is Limb)
            {
                var character = ((Limb)f2.Body.UserData).character;
                if (character.DisableImpactDamageTimer > 0.0f || ((Limb)f2.Body.UserData).Mass < 100.0f) return true;
            }
            
            if (!prefab.Platform && prefab.StairDirection == Direction.None)
            {
                Vector2 pos = ConvertUnits.ToDisplayUnits(f2.Body.Position);

                int section = FindSectionIndex(pos);
                if (section > -1)
                {
                    Vector2 normal = contact.Manifold.LocalNormal;

                    float impact = Vector2.Dot(f2.Body.LinearVelocity, -normal) * f2.Body.Mass * 0.1f;
                    if (impact < 10.0f) return true;
#if CLIENT
                    SoundPlayer.PlayDamageSound("StructureBlunt", impact, SectionPosition(section, true), tags: Tags);                    
#endif
                    AddDamage(section, impact);                 
                }
            }


            return true;
        }

        public WallSection GetSection(int sectionIndex)
        {
            if (sectionIndex < 0 || sectionIndex >= Sections.Length) return null;

            return Sections[sectionIndex];

        }
        
        public bool SectionBodyDisabled(int sectionIndex)
        {
            if (sectionIndex < 0 || sectionIndex >= Sections.Length) return false;

            return (Sections[sectionIndex].damage >= prefab.Health);
        }

        /// <summary>
        /// Sections that are leaking have a gap placed on them
        /// </summary>
        public bool SectionIsLeaking(int sectionIndex)
        {
            if (sectionIndex < 0 || sectionIndex >= Sections.Length) return false;

            return (Sections[sectionIndex].damage >= prefab.Health * LeakThreshold);
        }

        public int SectionLength(int sectionIndex)
        {
            if (sectionIndex < 0 || sectionIndex >= Sections.Length) return 0;

            return (IsHorizontal ? Sections[sectionIndex].rect.Width : Sections[sectionIndex].rect.Height);
        }

        public void AddDamage(int sectionIndex, float damage, Character attacker = null)
        {
            if (!prefab.Body || prefab.Platform) return;

            if (sectionIndex < 0 || sectionIndex > Sections.Length - 1) return;

            var section = Sections[sectionIndex];

#if CLIENT
            float particleAmount = Math.Min(Health - section.damage, damage) * Rand.Range(0.01f, 1.0f);

            particleAmount = Math.Min(particleAmount + Rand.Range(-5, 1), 5);
            for (int i = 0; i < particleAmount; i++)
            {
                Vector2 particlePos = new Vector2(
                    Rand.Range(section.rect.X, section.rect.Right),
                    Rand.Range(section.rect.Y - section.rect.Height, section.rect.Y));

                if (Submarine != null) particlePos += Submarine.DrawPosition;
                
                var particle = GameMain.ParticleManager.CreateParticle("shrapnel", particlePos, Rand.Vector(Rand.Range(1.0f, 50.0f)));
                if (particle == null) break;
            }
#endif

#if CLIENT
            if (GameMain.Client == null)
            {
#endif
                SetDamage(sectionIndex, section.damage + damage, attacker);
#if CLIENT
            }
#endif
        }

        public int FindSectionIndex(Vector2 displayPos, bool world = false, bool clamp = false)
        {
            if (!Sections.Any()) return -1;

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

            int index = (IsHorizontal) ?
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
            if (sectionIndex < 0 || sectionIndex >= Sections.Length) return Vector2.Zero;

            if (prefab.BodyRotation == 0.0f)
            {
                Vector2 sectionPos = new Vector2(
                    Sections[sectionIndex].rect.X + Sections[sectionIndex].rect.Width / 2.0f,
                    Sections[sectionIndex].rect.Y - Sections[sectionIndex].rect.Height / 2.0f);
                if (world && Submarine != null) sectionPos += Submarine.Position;
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
                if (FlippedX) diffFromCenter = -diffFromCenter;
                
                Vector2 sectionPos = Position + new Vector2(
                    (float)Math.Cos(IsHorizontal ? -BodyRotation : MathHelper.PiOver2 - BodyRotation),
                    (float)Math.Sin(IsHorizontal ? -BodyRotation : MathHelper.PiOver2 - BodyRotation)) * diffFromCenter;

                if (world && Submarine != null) sectionPos += Submarine.Position;
                return sectionPos;
            }


        }

        partial void AdjustKarma(IDamageable attacker, float amount);

        public AttackResult AddDamage(Character attacker, Vector2 worldPosition, Attack attack, float deltaTime, bool playSound = false)
        {
            if (Submarine != null && Submarine.GodMode) return new AttackResult(0.0f, null);
            if (!prefab.Body || prefab.Platform) return new AttackResult(0.0f, null);

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
                    GameMain.ParticleManager.CreateParticle("dustcloud", SectionPosition(i), 0.0f, 0.0f);
#endif
                }
            }
            
#if CLIENT
            if (playSound)
            {
                SoundPlayer.PlayDamageSound(attack.StructureSoundType, damageAmount, worldPosition, tags: Tags);
            }
#endif

            return new AttackResult(damageAmount, null);
        }

        private void SetDamage(int sectionIndex, float damage, Character attacker = null, bool createNetworkEvent = true)
        {
            if (Submarine != null && Submarine.GodMode) return;
            if (!prefab.Body) return;
            if (!MathUtils.IsValid(damage)) return;

            damage = MathHelper.Clamp(damage, 0.0f, prefab.Health);
            
#if SERVER
            if (GameMain.Server != null && createNetworkEvent && damage != Sections[sectionIndex].damage)
            {
                GameMain.Server.CreateEntityEvent(this);
            }
#endif

            bool noGaps = true;
            for (int i = 0; i < Sections.Length; i++)
            {
                if (i != sectionIndex && SectionIsLeaking(i))
                {
                    noGaps = false;
                    break;
                }
            }

            if (damage < prefab.Health * LeakThreshold)
            {
                if (Sections[sectionIndex].gap != null)
                {
#if SERVER
                    //the structure doesn't have any other gap, log the structure being fixed
                    if (noGaps && attacker != null)
                    {
                        GameServer.Log((Sections[sectionIndex].gap.IsRoomToRoom ? "Inner" : "Outer") + " wall repaired by " + attacker.Name, ServerLog.MessageType.ItemInteraction);
                    }
#endif

                    DebugConsole.Log("Removing gap (ID " + Sections[sectionIndex].gap.ID + ", section: " + sectionIndex + ") from wall " + ID);

                    //remove existing gap if damage is below leak threshold
                    Sections[sectionIndex].gap.Open = 0.0f;
                    Sections[sectionIndex].gap.Remove();
                    Sections[sectionIndex].gap = null;
#if CLIENT
                    if (CastShadow) GenerateConvexHull();
#endif
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
                        if (BodyWidth > 0.0f) gapRect.Width = (int)(BodyWidth * (gapRect.Width / (float)this.rect.Width));
                        if (BodyHeight > 0.0f) gapRect.Height = (int)BodyHeight;
                    }
                    else
                    {
                        diffFromCenter = ((gapRect.Y - gapRect.Height / 2) - (this.rect.Y - this.rect.Height / 2)) / (float)this.rect.Height * BodyHeight;
                        if (BodyWidth > 0.0f) gapRect.Width = (int)BodyWidth;
                        if (BodyHeight > 0.0f) gapRect.Height = (int)(BodyHeight * (gapRect.Height / (float)this.rect.Height));
                    }
                    if (FlippedX) diffFromCenter = -diffFromCenter;

                    if (BodyRotation != 0.0f)
                    {
                        Vector2 structureCenter = Position;
                        Vector2 gapPos = structureCenter + new Vector2(
                            (float)Math.Cos(IsHorizontal ? -BodyRotation : MathHelper.PiOver2 - BodyRotation),
                            (float)Math.Sin(IsHorizontal ? -BodyRotation : MathHelper.PiOver2 - BodyRotation)) * diffFromCenter;
                        gapRect = new Rectangle((int)(gapPos.X - gapRect.Width / 2), (int)(gapPos.Y + gapRect.Height / 2), gapRect.Width, gapRect.Height);
                    }

                    gapRect.X -= 10;
                    gapRect.Y += 10;
                    gapRect.Width += 20;
                    gapRect.Height += 20;
                    
                    bool horizontalGap = !IsHorizontal;
                    if (prefab.BodyRotation != 0.0f)
                    {
                        //rotation within a 90 deg sector (e.g. 100 -> 10, 190 -> 10, -10 -> 80)
                        float sectorizedRotation = MathUtils.WrapAngleTwoPi(BodyRotation) % MathHelper.PiOver2;
                        //diagonal if 30 < angle < 60
                        bool diagonal = sectorizedRotation > MathHelper.Pi / 6 && sectorizedRotation < MathHelper.Pi / 3;
                        //gaps on the lower half of a diagonal wall are horizontal, ones on the upper half are vertical
                        if (diagonal)
                        {
                            horizontalGap = gapRect.Y - gapRect.Height / 2 < Position.Y;
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
                        GameServer.Log((Sections[sectionIndex].gap.IsRoomToRoom ? "Inner" : "Outer") + " wall breached by " + attacker.Name, ServerLog.MessageType.ItemInteraction);
                    }
#endif
#if CLIENT
                    if (CastShadow) GenerateConvexHull();
#endif
                }
                
                float gapOpen = (damage / prefab.Health - LeakThreshold) * (1.0f / (1.0f - LeakThreshold));
                Sections[sectionIndex].gap.Open = gapOpen;
            }

            float damageDiff = damage - Sections[sectionIndex].damage;
            bool hadHole = SectionBodyDisabled(sectionIndex);
            Sections[sectionIndex].damage = MathHelper.Clamp(damage, 0.0f, prefab.Health);
            
            //otherwise it's possible to infinitely gain karma by welding fixed things
            if (attacker != null && damageDiff != 0.0f)
            {
                AdjustKarma(attacker, damageDiff);
#if CLIENT
                if (GameMain.Client == null)
                {
#endif
                    if (damageDiff < 0.0f)
                    {
                        attacker.Info.IncreaseSkillLevel("mechanical", 
                            -damageDiff * SkillIncreaseMultiplier / Math.Max(attacker.GetSkillLevel("mechanical"), 1.0f),
                            SectionPosition(sectionIndex, true));                                    
                    }
#if CLIENT
                }
#endif
            }

            bool hasHole = SectionBodyDisabled(sectionIndex);

            if (hadHole == hasHole) return;
                        
            UpdateSections();
        }

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
            foreach (Body b in Bodies)
            {
                GameMain.World.RemoveBody(b);
            }
            Bodies.Clear();
            bodyDebugDimensions.Clear();
            
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
                    CreateRectBody(mergedRect);
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
                CreateRectBody(mergedRect);
            }

            //if the section has holes (or is just one big hole with no bodies),
            //we need a sensor for repairtools to be able to target the structure
            if (hasHoles || !Bodies.Any())
            {
                Body sensorBody = CreateRectBody(rect);
                sensorBody.CollisionCategories = Physics.CollisionRepair;
                sensorBody.IsSensor = true;
            }
        }

        private Body CreateRectBody(Rectangle rect)
        {
            float diffFromCenter;
            if (IsHorizontal)
            {
                diffFromCenter = (rect.Center.X - this.rect.Center.X) / (float)this.rect.Width * BodyWidth;
                if (BodyWidth > 0.0f) rect.Width = (int)(BodyWidth * (rect.Width / (float)this.rect.Width));
                if (BodyHeight > 0.0f) rect.Height = (int)BodyHeight;
            }
            else
            {
                diffFromCenter = ((rect.Y - rect.Height / 2) - (this.rect.Y - this.rect.Height / 2)) / (float)this.rect.Height * BodyHeight;
                if (BodyWidth > 0.0f) rect.Width = (int)BodyWidth;
                if (BodyHeight > 0.0f) rect.Height = (int)(BodyHeight * (rect.Height / (float)this.rect.Height));
            }
            if (FlippedX) diffFromCenter = -diffFromCenter;
            
            Vector2 bodyOffset = ConvertUnits.ToSimUnits(prefab.BodyOffset);
            if (FlippedX) { bodyOffset.X = -bodyOffset.X; }
            if (FlippedY) { bodyOffset.Y = -bodyOffset.Y; }

            Body newBody = BodyFactory.CreateRectangle(GameMain.World,
                ConvertUnits.ToSimUnits(rect.Width),
                ConvertUnits.ToSimUnits(rect.Height),
                1.5f);
            newBody.BodyType = BodyType.Static;
            //newBody.Position = ConvertUnits.ToSimUnits(new Vector2(rect.X + rect.Width / 2.0f, rect.Y - rect.Height / 2.0f));
            newBody.Friction = 0.5f;
            newBody.OnCollision += OnWallCollision;
            newBody.CollisionCategories = (prefab.Platform) ? Physics.CollisionPlatform : Physics.CollisionWall;
            newBody.UserData = this;

            Vector2 structureCenter = ConvertUnits.ToSimUnits(Position);
            if (BodyRotation != 0.0f)
            {
                newBody.Position = structureCenter + bodyOffset + new Vector2(
                    (float)Math.Cos(IsHorizontal ? -BodyRotation : MathHelper.PiOver2 - BodyRotation),
                    (float)Math.Sin(IsHorizontal ? -BodyRotation : MathHelper.PiOver2 - BodyRotation)) * ConvertUnits.ToSimUnits(diffFromCenter);
                newBody.Rotation = -BodyRotation;
            }
            else
            {
                newBody.Position = structureCenter + (IsHorizontal ? Vector2.UnitX : Vector2.UnitY) * ConvertUnits.ToSimUnits(diffFromCenter) + bodyOffset;
            }

            //OffsetBody(newBody);            
            Bodies.Add(newBody);
            bodyDebugDimensions.Add(new Vector2(ConvertUnits.ToSimUnits(rect.Width), ConvertUnits.ToSimUnits(rect.Height)));

            return newBody;
        }
        
        public void ServerWrite(NetBuffer msg, Client c, object[] extraData = null) 
        {
            for (int i = 0; i < Sections.Length; i++)
            {
                msg.WriteRangedSingle(Sections[i].damage / Health, 0.0f, 1.0f, 8);
            }
        }

        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime) 
        {
            for (int i = 0; i < Sections.Length; i++)
            {
                float damage = msg.ReadRangedSingle(0.0f, 1.0f, 8) * Health;                
                SetDamage(i, damage);                
            }
        }
        public override void FlipX(bool relativeToSub)
        {
            base.FlipX(relativeToSub);
            
            if (prefab.CanSpriteFlipX)
            {
                SpriteEffects ^= SpriteEffects.FlipHorizontally;
            }

            if (StairDirection != Direction.None)
            {
                StairDirection = StairDirection == Direction.Left ? Direction.Right : Direction.Left;
                Bodies.ForEach(b => GameMain.World.RemoveBody(b));
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

            if (prefab.CanSpriteFlipY)
            {
                SpriteEffects ^= SpriteEffects.FlipVertically;
            }

            if (StairDirection != Direction.None)
            {
                StairDirection = StairDirection == Direction.Left ? Direction.Right : Direction.Left;
                Bodies.ForEach(b => GameMain.World.RemoveBody(b));
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

        public static Structure Load(XElement element, Submarine submarine)
        {
            string name = element.Attribute("name").Value;
            string identifier = element.GetAttributeString("identifier", "");

            StructurePrefab prefab;
            if (string.IsNullOrEmpty(identifier))
            {
                //legacy support: 
                //1. attempt to find a prefab with an empty identifier and a matching name
                prefab = MapEntityPrefab.Find(name, "") as StructurePrefab;
                //2. not found, attempt to find a prefab with a matching name
                if (prefab == null) prefab = MapEntityPrefab.Find(name) as StructurePrefab;
            }
            else
            {
                prefab = MapEntityPrefab.Find(null, identifier) as StructurePrefab;
            }

            if (prefab == null)
            {
                DebugConsole.ThrowError("Error loading structure - structure prefab \"" + name + "\" (identifier \"" + identifier + "\") not found.");
                return null;
            }

            Rectangle rect = element.GetAttributeRect("rect", Rectangle.Empty);
            Structure s = new Structure(rect, prefab, submarine)
            {
                Submarine = submarine,
                ID = (ushort)int.Parse(element.Attribute("ID").Value)
            };

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString())
                {
                    case "section":
                        int index = subElement.GetAttributeInt("i", -1);
                        if (index == -1) continue;
                        s.Sections[index].damage = subElement.GetAttributeFloat("damage", 0.0f);
                        break;
                }
            }

            if (element.GetAttributeBool("flippedx", false)) s.FlipX(false);
            if (element.GetAttributeBool("flippedy", false)) s.FlipY(false);
            SerializableProperty.DeserializeProperties(s, element);
#if CLIENT
            if (s.FlippedX || s.FlippedY) s.GenerateConvexHull();            
#endif
            return s;
        }

        public override XElement Save(XElement parentElement)
        {
            XElement element = new XElement("Structure");

            element.Add(
                new XAttribute("name", prefab.Name),
                new XAttribute("identifier", prefab.Identifier),
                new XAttribute("ID", ID),
                new XAttribute("rect",
                    (int)(rect.X - Submarine.HiddenSubPosition.X) + "," +
                    (int)(rect.Y - Submarine.HiddenSubPosition.Y) + "," +
                    rect.Width + "," + rect.Height));

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
    }
}
