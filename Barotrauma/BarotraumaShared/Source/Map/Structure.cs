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

        public int GapID;

        //public float lastSentDamage;

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

        private StructurePrefab prefab;

        //farseer physics bodies, separated by gaps
        private List<Body> bodies;

        private bool isHorizontal;

        private SpriteEffects SpriteEffects = SpriteEffects.None;

        private bool flippedX;

        //sections of the wall that are supposed to be rendered
        public WallSection[] sections
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

        public bool CastShadow
        {
            get { return prefab.CastShadow; }
        }

        public bool IsHorizontal
        {
            get { return isHorizontal; }
        }

        public int SectionCount
        {
            get { return sections.Length; }
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

        public List<string> Tags
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
                    foreach (WallSection sec in sections)
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

        public Dictionary<string, SerializableProperty> SerializableProperties
        {
            get;
            private set;
        }

        public override void Move(Vector2 amount)
        {
            base.Move(amount);

            for (int i = 0; i < sections.Length; i++)
            {
                Rectangle r = sections[i].rect;
                r.X += (int)amount.X;
                r.Y += (int)amount.Y;
                sections[i].rect = r;
            }

            if (bodies != null)
            {
                Vector2 simAmount = ConvertUnits.ToSimUnits(amount);
                foreach (Body b in bodies)
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

            isHorizontal = (rect.Width > rect.Height);

            StairDirection = prefab.StairDirection;

            SerializableProperties = SerializableProperty.GetProperties(this);

            if (prefab.Body)
            {
                bodies = new List<Body>();
                //gaps = new List<Gap>();

                Body newBody = BodyFactory.CreateRectangle(GameMain.World,
                    ConvertUnits.ToSimUnits(rect.Width),
                    ConvertUnits.ToSimUnits(rect.Height),
                    1.5f);
                newBody.BodyType = BodyType.Static;
                newBody.Position = ConvertUnits.ToSimUnits(new Vector2(rect.X + rect.Width / 2.0f, rect.Y - rect.Height / 2.0f));
                newBody.Friction = 0.5f;
                newBody.OnCollision += OnWallCollision;
                newBody.UserData = this;
                newBody.CollisionCategories = (prefab.Platform) ? Physics.CollisionPlatform : Physics.CollisionWall;

                bodies.Add(newBody);

                WallList.Add(this);

                CreateSections();
            }
            else
            {
                sections = new WallSection[1];
                sections[0] = new WallSection(rect);

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

        public override MapEntity Clone()
        {
            return new Structure(rect, prefab, Submarine);
        }

        private void CreateStairBodies()
        {
            bodies = new List<Body>();

            Body newBody = BodyFactory.CreateRectangle(GameMain.World,
                ConvertUnits.ToSimUnits(rect.Width * Math.Sqrt(2.0) + Submarine.GridSize.X * 3.0f),
                ConvertUnits.ToSimUnits(10),
                1.5f);

            newBody.BodyType = BodyType.Static;
            Vector2 stairPos = new Vector2(Position.X, rect.Y - rect.Height + rect.Width / 2.0f);
            stairPos += new Vector2(
                (StairDirection == Direction.Right) ? -Submarine.GridSize.X * 1.5f : Submarine.GridSize.X * 1.5f,
                -Submarine.GridSize.Y * 2.0f);

            newBody.Position = ConvertUnits.ToSimUnits(stairPos);
            newBody.Rotation = (StairDirection == Direction.Right) ? MathHelper.PiOver4 : -MathHelper.PiOver4;
            newBody.Friction = 0.8f;

            newBody.CollisionCategories = Physics.CollisionStairs;

            newBody.UserData = this;
            bodies.Add(newBody);
        }

        private void CreateSections()
        {
            int xsections = 1, ysections = 1;
            int width = rect.Width, height = rect.Height;

            if (!HasBody)
            {          
                if (flippedX && isHorizontal)
                {
                    xsections = (int)Math.Ceiling((float)rect.Width / prefab.sprite.SourceRect.Width);
                    width = prefab.sprite.SourceRect.Width;

                }
                else
                {
                    xsections = 1;
                    ysections = 1;
                }
                sections = new WallSection[xsections];
            }
            else
            {
                if (isHorizontal)
                {
                    xsections = (int)Math.Ceiling((float)rect.Width / WallSectionSize);
                    sections = new WallSection[xsections];
                    width = (int)WallSectionSize;
                }
                else
                {
                    ysections = (int)Math.Ceiling((float)rect.Height / WallSectionSize);
                    sections = new WallSection[ysections];
                    height = (int)WallSectionSize;
                }
            }

            for (int x = 0; x < xsections; x++)
            {
                for (int y = 0; y < ysections; y++)
                {
                    if (flippedX)
                    {
                        Rectangle sectionRect = new Rectangle(rect.Right - (x + 1) * width, rect.Y - y * height, width, height);

                        int over = Math.Max(rect.X - sectionRect.X, 0);

                        sectionRect.X += over;
                        sectionRect.Width -= over;

                        sectionRect.Height -= (int)Math.Max((rect.Y - rect.Height) - (sectionRect.Y - sectionRect.Height), 0.0f);

                        sections[xsections - 1 - x + y] = new WallSection(sectionRect);
                    }

                    else
                    {
                        Rectangle sectionRect = new Rectangle(rect.X + x * width, rect.Y - y * height, width, height);
                        sectionRect.Width -= (int)Math.Max(sectionRect.Right - rect.Right, 0.0f);
                        sectionRect.Height -= (int)Math.Max((rect.Y - rect.Height) - (sectionRect.Y - sectionRect.Height), 0.0f);

                        sections[x + y] = new WallSection(sectionRect);
                    }
                }
            }
        }

        private Rectangle GenerateMergedRect(List<WallSection> mergedSections)
        {
            if (isHorizontal)
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

        public override bool IsMouseOn(Vector2 position)
        {
            if (StairDirection == Direction.None)
            {
                return base.IsMouseOn(position);
            }
            else
            {
                if (!base.IsMouseOn(position)) return false;

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

            if (bodies != null)
            {
                foreach (Body b in bodies)
                    GameMain.World.RemoveBody(b);
            }

            if (sections != null)
            {
                foreach (WallSection s in sections)
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

            if (bodies != null)
            {
                foreach (Body b in bodies)
                    GameMain.World.RemoveBody(b);
            }

            if (sections != null)
            {
                foreach (WallSection s in sections)
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
                if (section > 0)
                {
                    Vector2 normal = contact.Manifold.LocalNormal;

                    float impact = Vector2.Dot(f2.Body.LinearVelocity, -normal)*f2.Body.Mass*0.1f;

                    if (impact < 10.0f) return true;

#if CLIENT
                    SoundPlayer.PlayDamageSound(DamageSoundType.StructureBlunt, impact,
                        new Vector2(
                            sections[section].rect.X + sections[section].rect.Width / 2, 
                            sections[section].rect.Y - sections[section].rect.Height / 2));
#endif

                    AddDamage(section, impact);                 
                }
            }


            return true;
        }

        public WallSection GetSection(int sectionIndex)
        {
            if (sectionIndex < 0 || sectionIndex >= sections.Length) return null;

            return sections[sectionIndex];

        }
        
        public bool SectionBodyDisabled(int sectionIndex)
        {
            if (sectionIndex < 0 || sectionIndex >= sections.Length) return false;

            return (sections[sectionIndex].damage>=prefab.Health);
        }

        /// <summary>
        /// Sections that are leaking have a gap placed on them
        /// </summary>
        public bool SectionIsLeaking(int sectionIndex)
        {
            if (sectionIndex < 0 || sectionIndex >= sections.Length) return false;

            return (sections[sectionIndex].damage >= prefab.Health*0.5f);
        }

        public int SectionLength(int sectionIndex)
        {
            if (sectionIndex < 0 || sectionIndex >= sections.Length) return 0;

            return (isHorizontal ? sections[sectionIndex].rect.Width : sections[sectionIndex].rect.Height);
        }

        public void AddDamage(int sectionIndex, float damage, IDamageable attacker=null)
        {
            if (!prefab.Body || prefab.Platform) return;

            if (sectionIndex < 0 || sectionIndex > sections.Length - 1) return;

            var section = sections[sectionIndex];

#if CLIENT
            float particleAmount = Math.Min(Health - section.damage, damage) * Rand.Range(0.01f, 1.0f);

            particleAmount = Math.Min(particleAmount + Rand.Range(-5,1), 100);
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

            if (GameMain.Client == null) SetDamage(sectionIndex, section.damage + damage, attacker);
        }

        public int FindSectionIndex(Vector2 displayPos)
        {
            if (!sections.Any()) return -1;

            //if the sub has been flipped horizontally, the first section may be smaller than wallSectionSize
            //and we need to adjust the position accordingly
            if (sections[0].rect.Width < WallSectionSize)
            {
                displayPos.X += WallSectionSize - sections[0].rect.Width;
            }

            int index = (isHorizontal) ?
                (int)Math.Floor((displayPos.X - rect.X) / WallSectionSize) :
                (int)Math.Floor((rect.Y - displayPos.Y) / WallSectionSize);

            if (index < 0 || index > sections.Length - 1) return -1;
            return index;
        }

        public float SectionDamage(int sectionIndex)
        {
            if (sectionIndex < 0 || sectionIndex >= sections.Length) return 0.0f;

            return sections[sectionIndex].damage;
        }

        public Vector2 SectionPosition(int sectionIndex, bool world = false)
        {
            if (sectionIndex < 0 || sectionIndex >= sections.Length) return Vector2.Zero;

            Vector2 sectionPos = new Vector2(
                sections[sectionIndex].rect.X + sections[sectionIndex].rect.Width / 2.0f,
                sections[sectionIndex].rect.Y - sections[sectionIndex].rect.Height / 2.0f);

            if (world && Submarine != null) sectionPos += Submarine.Position;

            return sectionPos;
        }

        private void AdjustKarma(IDamageable attacker, float amount)
        {
            if (GameMain.Server != null)
            {
                if (Submarine == null) return;
                if (attacker == null) return;
                if (attacker is Character)
                {
                    Character attackerCharacter = attacker as Character;
                    Barotrauma.Networking.Client attackerClient = GameMain.Server.ConnectedClients.Find(c => c.Character == attackerCharacter);
                    if (attackerClient != null)
                    {
                        if (attackerCharacter.TeamID == Submarine.TeamID)
                        {
                            attackerClient.Karma -= amount * 0.001f;
                        }
                    }
                }
            }
        }

        public AttackResult AddDamage(Character attacker, Vector2 worldPosition, Attack attack, float deltaTime, bool playSound = false)
        {
            if (Submarine != null && Submarine.GodMode) return new AttackResult(0.0f, 0.0f);
            if (!prefab.Body || prefab.Platform) return new AttackResult(0.0f, 0.0f);

            Vector2 transformedPos = worldPosition;
            if (Submarine != null) transformedPos -= Submarine.Position;

            int i = FindSectionIndex(transformedPos);
            if (i == -1) return new AttackResult(0.0f, 0.0f);
            
            float damageAmount = attack.GetStructureDamage(deltaTime);

            AddDamage(i, damageAmount, attacker);

#if CLIENT
            GameMain.ParticleManager.CreateParticle("dustcloud", SectionPosition(i), 0.0f, 0.0f);

            if (playSound && !SectionBodyDisabled(i))
            {
                DamageSoundType damageSoundType = (attack.DamageType == DamageType.Blunt) ? DamageSoundType.StructureBlunt : DamageSoundType.StructureSlash;
                SoundPlayer.PlayDamageSound(damageSoundType, damageAmount, worldPosition);
            }
#endif
            
            return new AttackResult(damageAmount, 0.0f);
        }

        private void SetDamage(int sectionIndex, float damage, IDamageable attacker=null)
        {
            if (Submarine != null && Submarine.GodMode) return;
            if (!prefab.Body) return;

            if (!MathUtils.IsValid(damage)) return;

            float damageDiff = damage - sections[sectionIndex].damage;

            if (GameMain.Server != null && damage != sections[sectionIndex].damage)
            {
                GameMain.Server.CreateEntityEvent(this);
            }

            AdjustKarma(attacker, damageDiff);
            if (damage < prefab.Health*0.5f)
            {
                if (sections[sectionIndex].gap != null)
                {
                    //remove existing gap if damage is below 50%
                    sections[sectionIndex].gap.Remove();
                    sections[sectionIndex].gap = null;
#if CLIENT
                    if(CastShadow) GenerateConvexHull();
#endif
                }
            }
            else
            {
                if (sections[sectionIndex].gap == null)
                {
                    Rectangle gapRect = sections[sectionIndex].rect;
                    gapRect.X -= 10;
                    gapRect.Y += 10;
                    gapRect.Width += 20;
                    gapRect.Height += 20;
                    sections[sectionIndex].gap = new Gap(gapRect, !isHorizontal, Submarine);
                    sections[sectionIndex].gap.ConnectedWall = this;
                    //AdjustKarma(attacker, 300);
#if CLIENT
                    if(CastShadow) GenerateConvexHull();
#endif
                }

                sections[sectionIndex].gap.Open = (damage / prefab.Health - 0.5f) * 2.0f;
            }
            
            bool hadHole = SectionBodyDisabled(sectionIndex);
            sections[sectionIndex].damage = MathHelper.Clamp(damage, 0.0f, prefab.Health);

            bool hasHole = SectionBodyDisabled(sectionIndex);

            if (hadHole == hasHole) return;
            //if (hasHole) Explosion.ApplyExplosionForces(sections[sectionIndex].gap.WorldPosition, 500.0f, 5.0f, 0.0f, 0.0f);
            UpdateSections();
        }

        public void SetCollisionCategory(Category collisionCategory)
        {
            if (bodies == null) return;
            foreach (Body body in bodies)
            {
                body.CollisionCategories = collisionCategory;
            }
        }

        private void UpdateSections()
        {
            foreach (Body b in bodies)
            {
                GameMain.World.RemoveBody(b);
            }
            bodies.Clear();

            bool hasHoles = false;
            var mergedSections = new List<WallSection>();
            for (int i = 0; i < sections.Length; i++ )
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
                    mergedSections.Add(sections[i]);
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
            if (hasHoles || !bodies.Any())
            {
                Body sensorBody = CreateRectBody(rect);
                sensorBody.CollisionCategories = Physics.CollisionRepair;
                sensorBody.IsSensor = true;
            }
        }

        private Body CreateRectBody(Rectangle rect)
        {
            Body newBody = BodyFactory.CreateRectangle(GameMain.World,
                ConvertUnits.ToSimUnits(rect.Width),
                ConvertUnits.ToSimUnits(rect.Height),
                1.5f);
            newBody.BodyType = BodyType.Static;
            newBody.Position = ConvertUnits.ToSimUnits(new Vector2(rect.X + rect.Width / 2.0f, rect.Y - rect.Height / 2.0f));
            newBody.Friction = 0.5f;

            newBody.OnCollision += OnWallCollision;

            newBody.CollisionCategories = Physics.CollisionWall;

            newBody.UserData = this;

            bodies.Add(newBody);

            return newBody;
        }

        public void ServerWrite(NetBuffer msg, Client c, object[] extraData = null) 
        {
            for (int i = 0; i < sections.Length; i++)
            {
                msg.WriteRangedSingle(sections[i].damage / Health, 0.0f, 1.0f, 8);
            }
        }

        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime) 
        {
            for (int i = 0; i < sections.Length; i++)
            {
                float damage = msg.ReadRangedSingle(0.0f, 1.0f, 8) * Health;

                SetDamage(i, damage);
            }
        }
        public override void FlipX()
        {
            base.FlipX();

            flippedX = !flippedX;
            
            if (prefab.CanSpriteFlipX)
            {
                SpriteEffects ^= SpriteEffects.FlipHorizontally;
            }

            if (StairDirection != Direction.None)
            {
                StairDirection = StairDirection == Direction.Left ? Direction.Right : Direction.Left;
                bodies.ForEach(b => GameMain.World.RemoveBody(b));
                bodies.Clear();

                CreateStairBodies();
            }

            CreateSections();
        }
        
        public static void Load(XElement element, Submarine submarine)
        {
            string name = element.Attribute("name").Value;

            StructurePrefab prefab = MapEntityPrefab.Find(name) as StructurePrefab;
            if (prefab == null)
            {
                DebugConsole.ThrowError("Error loading structure - structure prefab " + name + " not found.");
                return;
            }

            Rectangle rect = element.GetAttributeRect("rect", Rectangle.Empty);
            Structure s = new Structure(rect, prefab, submarine);
            s.Submarine = submarine;
            s.ID = (ushort)int.Parse(element.Attribute("ID").Value);
            
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString())
                {
                    case "section":
                        int index = subElement.GetAttributeInt("i", -1);
                        if (index == -1) continue;

                        s.sections[index].damage = 
                            subElement.GetAttributeFloat("damage", 0.0f);

                        s.sections[index].GapID = subElement.GetAttributeInt("gap", -1);

                        break;
                }
            }

            SerializableProperty.DeserializeProperties(s, element);
        }

        public override XElement Save(XElement parentElement)
        {
            XElement element = new XElement("Structure");

            element.Add(new XAttribute("name", prefab.Name),
                new XAttribute("ID", ID),
                new XAttribute("rect",
                    (int)(rect.X - Submarine.HiddenSubPosition.X) + "," +
                    (int)(rect.Y - Submarine.HiddenSubPosition.Y) + "," +
                    rect.Width + "," + rect.Height));

            for (int i = 0; i < sections.Length; i++)
            {
                if (sections[i].damage == 0.0f) continue;

                var sectionElement =
                    new XElement("section",
                        new XAttribute("i", i),
                        new XAttribute("damage", sections[i].damage));

                if (sections[i].gap != null)
                {
                    sectionElement.Add(new XAttribute("gap", sections[i].gap.ID));
                }

                element.Add(sectionElement);
            }

            SerializableProperty.SerializeProperties(this, element);

            parentElement.Add(element);

            return element;
        }

        public override void OnMapLoaded()
        {
            foreach (WallSection s in sections)
            {
                if (s.GapID == -1) continue;

                s.gap = FindEntityByID((ushort)s.GapID) as Gap;
                if (s.gap != null) s.gap.ConnectedWall = this;
            }
        }
        
    }
}
