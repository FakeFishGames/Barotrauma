using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using FarseerPhysics.Factories;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Networking;
using Barotrauma.Lights;

namespace Barotrauma
{
    class WallSection
    {
        public Rectangle rect;
        public float damage;
        public Gap gap;

        public int GapIndex;

        public float lastSentDamage;

        public bool isHighLighted;
        public ConvexHull hull;

        public WallSection(Rectangle rect)
        {
            this.rect = rect;
            damage = 0.0f;
        }

        public WallSection(Rectangle rect, float damage)
        {
            this.rect = rect;
            this.damage = 0.0f;
        }
    }

    class Structure : MapEntity, IDamageable
    {
        public static int wallSectionSize = 100;
        public static List<Structure> WallList = new List<Structure>();

        List<ConvexHull> convexHulls;

        StructurePrefab prefab;

        //farseer physics bodies, separated by gaps
        List<Body> bodies;

        //sections of the wall that are supposed to be rendered
        private WallSection[] sections;

        bool isHorizontal;

        public float lastUpdate;
        
        public override Sprite Sprite
        {
            get { return prefab.sprite; }
        }

        public bool IsPlatform
        {
            get { return prefab.IsPlatform; }
        }

        public Direction StairDirection
        {
            get { return prefab.StairDirection; }
        }

        public override string Name
        {
            get { return "structure"; }
        }

        public bool HasBody
        {
            get { return prefab.HasBody; }
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
            get { return prefab.MaxHealth; }
        }
                
        public override void Move(Vector2 amount)
        {
            base.Move(amount);

            for (int i = 0; i < sections.Count(); i++)
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

            if (convexHulls!=null)
            {
                convexHulls.ForEach(x => x.Move(amount));
            }
            //if (gaps != null)
            //{
            //    foreach (Gap g in gaps)
            //    {
            //        g.Move(amount);
            //        //g.position.X += amount.X;
            //        //g.position.Y -= amount.Y;
            //    }
            //}
        }

        public Structure(Rectangle rectangle, StructurePrefab sp, Submarine submarine)
            : base(sp, submarine)
        {
            if (rectangle.Width == 0 || rectangle.Height == 0) return;

            rect = rectangle;
            prefab = sp;
            
            isHorizontal = (rect.Width>rect.Height);
            
            if (prefab.HasBody)
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

                newBody.CollisionCategories = (prefab.IsPlatform) ? Physics.CollisionPlatform : Physics.CollisionWall;

                bodies.Add(newBody);

                WallList.Add(this);
                        
                int xsections = 1;
                int ysections = 1;
                int width, height;
                if (isHorizontal)
                {
                    xsections = (int)Math.Ceiling((float)rect.Width / wallSectionSize);
                    sections = new WallSection[xsections];
                    width = (int)wallSectionSize;
                    height = rect.Height;
                }
                else
                {
                    ysections = (int)Math.Ceiling((float)rect.Height / wallSectionSize);
                    sections = new WallSection[ysections];
                    width = rect.Width;
                    height = (int)wallSectionSize;
                }
                
                for (int x = 0; x < xsections; x++ )
                {
                    for (int y = 0; y < ysections; y++)
                    {
                        Rectangle sectionRect = new Rectangle(rect.X + x * width, rect.Y - y * height, width, height);
                        sectionRect.Width -= (int)Math.Max((sectionRect.X + sectionRect.Width) - (rect.X + rect.Width), 0.0f);
                        sectionRect.Height -= (int)Math.Max((rect.Y - rect.Height)-(sectionRect.Y - sectionRect.Height), 0.0f);

                        sections[x+y] = new WallSection(sectionRect);
                    }
                }

            }
            else
            {
                sections = new WallSection[1];
                sections[0] = new WallSection(rect);

                if (StairDirection!=Direction.None)
                {
                    bodies = new List<Body>();

                    Body newBody = BodyFactory.CreateRectangle(GameMain.World,
                        ConvertUnits.ToSimUnits(rect.Width * Math.Sqrt(2.0) + Submarine.GridSize.X*3.0f),
                        ConvertUnits.ToSimUnits(10),
                        1.5f);

                    newBody.BodyType = BodyType.Static;
                    Vector2 stairPos = new Vector2(Position.X, rect.Y - rect.Height + rect.Width / 2.0f);
                    stairPos += new Vector2(
                        (StairDirection == Direction.Right) ? -Submarine.GridSize.X*1.5f : Submarine.GridSize.X*1.5f,
                        - Submarine.GridSize.Y*2.0f);

                    newBody.Position = ConvertUnits.ToSimUnits(stairPos);
                    newBody.Rotation = (StairDirection == Direction.Right) ? MathHelper.PiOver4 : -MathHelper.PiOver4;
                    newBody.Friction = 0.8f;

                    newBody.CollisionCategories = Physics.CollisionStairs;

                    newBody.UserData = this;
                    bodies.Add(newBody);
                }
            }


            if (prefab.CastShadow)
            {
                GenerateConvexHull();
            }

            InsertToList();
        }

        private void GenerateConvexHull()
        {
            // If not null and not empty , remove the hulls from the system
            if(convexHulls != null && convexHulls.Any())
                convexHulls.ForEach(x => x.Remove());

            // list all of hulls for this structure
            convexHulls = new List<ConvexHull>();

            var mergedSections = new List<WallSection>();
            foreach (var section in sections)
            {
                if (mergedSections.Count > 5)
                {
                    mergedSections.Add(section);
                    GenerateMergedHull(mergedSections);
                    continue;
                }

                // if there is a gap and we have sections to merge, do it.
                if (section.gap != null)
                {
                    GenerateMergedHull(mergedSections);
                }
                else
                {
                    mergedSections.Add(section);
                }
            }

            // take care of any leftover pieces
            if (mergedSections.Count > 0)
            {
                GenerateMergedHull(mergedSections);
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

        private void GenerateMergedHull(List<WallSection> mergedSections)
        {
            if (!mergedSections.Any()) return;
            Rectangle mergedRect = GenerateMergedRect(mergedSections);
            
            var h = new ConvexHull(CalculateExtremes(mergedRect), Color.Black, this);
            mergedSections.ForEach(x => x.hull = h);
            convexHulls.Add(h);
            mergedSections.Clear();
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
        
        public override void Remove()
        {
            base.Remove();

            if (WallList.Contains(this)) WallList.Remove(this);

            if (bodies != null)
            {
                foreach (Body b in bodies)
                    GameMain.World.RemoveBody(b);
            }

            foreach (WallSection s in sections)
            {
                if (s.gap != null)
                {
                    s.gap.Remove();
                    s.gap = null;
                }
            }
            if (convexHulls != null) convexHulls.ForEach(x => x.Remove());
        }


        public override void Draw(SpriteBatch spriteBatch, bool editing, bool back = true)
        {
            if (prefab.sprite == null) return;

            Color color = (isHighlighted) ? Color.Orange : Color.White;
            if (isSelected && editing)
            {
                color = Color.Red;

                GUI.DrawRectangle(spriteBatch, new Rectangle(rect.X, -rect.Y, rect.Width, rect.Height), color);
            }

            Vector2 drawOffset = Submarine == null ? Vector2.Zero : Submarine.DrawPosition;
            if(sections.Length == 1)
                prefab.sprite.DrawTiled(spriteBatch, new Vector2(rect.X + drawOffset.X, -(rect.Y + drawOffset.Y)), new Vector2(rect.Width, rect.Height), Vector2.Zero, color,Point.Zero);

            foreach (WallSection s in sections)
            {
                Point offset = rect.Location - s.rect.Location;
                if (sections.Length != 1 && s.damage < prefab.MaxHealth)
                   prefab.sprite.DrawTiled(spriteBatch, new Vector2(s.rect.X + drawOffset.X, -(s.rect.Y + drawOffset.Y)), new Vector2(s.rect.Width, s.rect.Height), Vector2.Zero, color, offset);

                if (s.isHighLighted)
                {
                    GUI.DrawRectangle(spriteBatch,
                        new Vector2(s.rect.X + drawOffset.X, -(s.rect.Y + drawOffset.Y)), new Vector2(s.rect.Width, s.rect.Height),
                        new Color((s.damage / prefab.MaxHealth), 1.0f - (s.damage / prefab.MaxHealth), 0.0f, 1.0f), true);
                }

                s.isHighLighted = false;
                
                if (s.damage < 0.01f) continue;
                
               /* GUI.DrawRectangle(spriteBatch,
                   new Vector2(s.rect.X + drawOffset.X, -(s.rect.Y + drawOffset.Y)), new Vector2(s.rect.Width, s.rect.Height),
                    Color.Black * (s.damage / prefab.MaxHealth), true);*/ 
            }
            /*
            if(_convexHulls == null) return;
            var rand = new Random(32434324);
            foreach (var hull in _convexHulls)
            {
                if (sections.Count(x => x.hull == hull) <= 1)
                    continue;
                var col = new Color((int) (255 * rand.NextDouble()), (int)(255 * rand.NextDouble()), (int)(255 * rand.NextDouble()), 255);
                GUI.DrawRectangle(spriteBatch,new Vector2 (hull.BoundingBox.X + drawOffset.X, -(hull.BoundingBox.Y + drawOffset.Y)), new Vector2(hull.BoundingBox.Width, hull.BoundingBox.Height),col,true );
            }*/
        }

        private bool OnWallCollision(Fixture f1, Fixture f2, Contact contact)
        {
            //Structure structure = f1.Body.UserData as Structure;

            //if (f2.Body.UserData as Item != null)
            //{
            //    if (prefab.IsPlatform || prefab.StairDirection != Direction.None) return false;
            //}

            if (prefab.IsPlatform)
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
            
            if (!prefab.IsPlatform && prefab.StairDirection == Direction.None)
            {
                Vector2 pos = ConvertUnits.ToDisplayUnits(f2.Body.Position);

                int section = FindSectionIndex(pos);
                if (section > 0)
                {
                    Vector2 normal = contact.Manifold.LocalNormal;

                    float impact = Vector2.Dot(f2.Body.LinearVelocity, -normal)*f2.Body.Mass*0.1f;

                    if (impact < 10.0f) return true;

                    SoundPlayer.PlayDamageSound(DamageSoundType.StructureBlunt, impact,
                        new Vector2(
                            sections[section].rect.X + sections[section].rect.Width / 2, 
                            sections[section].rect.Y - sections[section].rect.Height / 2));

                    AddDamage(section, impact);                 
                }
            }


            return true;
        }

        public void HighLightSection(int sectionIndex)
        {
            sections[sectionIndex].isHighLighted = true;
        }
        
        public bool SectionBodyDisabled(int sectionIndex)
        {
            if (sectionIndex < 0 || sectionIndex >= sections.Length) return false;

            return (sections[sectionIndex].damage>=prefab.MaxHealth);
        }

        /// <summary>
        /// Sections that are leaking have a gap placed on them
        /// </summary>
        public bool SectionIsLeaking(int sectionIndex)
        {
            if (sectionIndex < 0 || sectionIndex >= sections.Length) return false;

            return (sections[sectionIndex].damage >= prefab.MaxHealth*0.5f);
        }

        public int SectionLength(int sectionIndex)
        {
            if (sectionIndex < 0 || sectionIndex >= sections.Length) return 0;

            return (isHorizontal ? sections[sectionIndex].rect.Width : sections[sectionIndex].rect.Height);
        }

        public void AddDamage(int sectionIndex, float damage)
        {
            if (!prefab.HasBody || prefab.IsPlatform) return;

            if (sectionIndex < 0 || sectionIndex > sections.Length - 1) return;

            if (GameMain.Client == null) SetDamage(sectionIndex, sections[sectionIndex].damage + damage);

        }

        public int FindSectionIndex(Vector2 displayPos)
        {
            int index = (isHorizontal) ?
                (int)Math.Floor((displayPos.X - rect.X) / wallSectionSize) :
                (int)Math.Floor((rect.Y - displayPos.Y) / wallSectionSize);

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

        public AttackResult AddDamage(IDamageable attacker, Vector2 worldPosition, Attack attack, float deltaTime, bool playSound = false)
        {
            if (Submarine != null && Submarine.GodMode) return new AttackResult(0.0f, 0.0f);
            if (!prefab.HasBody || prefab.IsPlatform) return new AttackResult(0.0f, 0.0f);

            Vector2 transformedPos = worldPosition;
            if (Submarine != null) transformedPos -= Submarine.Position;

            int i = FindSectionIndex(transformedPos);
            if (i == -1) return new AttackResult(0.0f, 0.0f);
            
            GameMain.ParticleManager.CreateParticle("dustcloud", SectionPosition(i), 0.0f, 0.0f);

            float damageAmount = attack.GetStructureDamage(deltaTime);

            if (playSound && !SectionBodyDisabled(i))
            {
                DamageSoundType damageSoundType = (attack.DamageType == DamageType.Blunt) ? DamageSoundType.StructureBlunt : DamageSoundType.StructureSlash;
                SoundPlayer.PlayDamageSound(damageSoundType, damageAmount, worldPosition);
            }

            AddDamage(i, damageAmount);

            return new AttackResult(damageAmount, 0.0f);
        }

        private void SetDamage(int sectionIndex, float damage)
        {
            if (Submarine != null && Submarine.GodMode) return;
            if (!prefab.HasBody) return;

            if (!MathUtils.IsValid(damage)) return;

            if (damage != sections[sectionIndex].damage && Math.Abs(sections[sectionIndex].lastSentDamage - damage)>5.0f)
            {
                new NetworkEvent(NetworkEventType.ImportantEntityUpdate, ID, false);
                //sections[sectionIndex].lastSentDamage = damage;
            }
            
            if (damage < prefab.MaxHealth*0.5f)
            {
                if (sections[sectionIndex].gap != null)
                {
                    //remove existing gap if damage is below 50%
                    sections[sectionIndex].gap.Remove();
                    sections[sectionIndex].gap = null;
                    if(CastShadow)
                        GenerateConvexHull();
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

                    if(CastShadow) GenerateConvexHull();
                }
            }

            if (sections[sectionIndex].gap != null)
                sections[sectionIndex].gap.Open = (damage/prefab.MaxHealth - 0.5f)*2;

            bool hadHole = SectionBodyDisabled(sectionIndex);
            sections[sectionIndex].damage = MathHelper.Clamp(damage, 0.0f, prefab.MaxHealth);

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
            if (hasHoles || !bodies.Any()) CreateRectBody(rect).IsSensor = true;            
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

        
        public override XElement Save(XElement parentElement)
        {
            XElement element = new XElement("Structure");
            
            element.Add(new XAttribute("name", prefab.Name),
                new XAttribute("ID", ID),
                new XAttribute("rect",
                    (int)(rect.X - Submarine.HiddenSubPosition.X) + "," +
                    (int)(rect.Y - Submarine.HiddenSubPosition.Y) + "," +
                    rect.Width + "," + rect.Height));
            
            for (int i = 0; i < sections.Count(); i++)
            {
                if (sections[i].damage == 0.0f) continue;

                var sectionElement = 
                    new XElement("section",
                        new XAttribute("i", i),
                        new XAttribute("damage", sections[i].damage));

                if (sections[i].gap!=null)
                {
                    sectionElement.Add(new XAttribute("gap", sections[i].gap.ID));
                }

                element.Add(sectionElement);
            }
            
            parentElement.Add(element);

            return element;
        }

        public static void Load(XElement element, Submarine submarine)
        {
            string rectString = ToolBox.GetAttributeString(element, "rect", "0,0,0,0");
            string[] rectValues = rectString.Split(',');

            Rectangle rect = new Rectangle(
                int.Parse(rectValues[0]),
                int.Parse(rectValues[1]),
                int.Parse(rectValues[2]),
                int.Parse(rectValues[3]));

            string name = element.Attribute("name").Value;
            
            Structure s = null;

            foreach (MapEntityPrefab ep in MapEntityPrefab.list)
            {
                if (ep.Name == name)
                {
                    s = new Structure(rect, (StructurePrefab)ep, submarine);
                    s.Submarine = submarine;
                    s.ID = (ushort)int.Parse(element.Attribute("ID").Value);
                    break;
                }
            }

            if (s == null)
            {
                DebugConsole.ThrowError("Structure prefab " + name + " not found.");
                return;
            }
            
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString())
                {
                    case "section":
                        int index = ToolBox.GetAttributeInt(subElement, "i", -1);
                        if (index == -1) continue;

                        s.sections[index].damage = 
                            ToolBox.GetAttributeFloat(subElement, "damage", 0.0f);

                        s.sections[index].GapIndex = ToolBox.GetAttributeInt(subElement, "gap", -1);

                        break;
                }
            }
        }

        public override void OnMapLoaded()
        {
            foreach (WallSection s in sections)
            {
                if (s.GapIndex == -1) continue;

                s.gap = FindEntityByID((ushort)s.GapIndex) as Gap;
                if (s.gap != null) s.gap.ConnectedWall = this;
            }
        }

        public override bool FillNetworkData(NetworkEventType type, NetBuffer message, object data)
        {
            //var updateSections = Array.FindAll(sections, s => s != null && Math.Abs(s.damage - s.lastSentDamage) > 0.01f);

            //if (updateSections.Length == 0) return false;

            //Debug.Assert(updateSections.Length<255);

           // message.Write((byte)updateSections.Length);

            for (int i = 0; i < sections.Length; i++)
            {
                //if (Math.Abs(sections[i].damage - sections[i].lastSentDamage) < 0.01f) continue;

                //message.Write((byte)i);
                message.WriteRangedSingle(sections[i].damage / Health, 0.0f, 1.0f, 8);

                sections[i].lastSentDamage = sections[i].damage;
            }

            return true;
        }

        public override bool ReadNetworkData(NetworkEventType type, NetIncomingMessage message, float sendingTime, out object data)
        {
            data = null;

            if (GameMain.Server != null) return false;

            if (sendingTime < lastUpdate) return false;

           // int sectionCount = message.ReadByte();

            for (int i = 0; i<sections.Count(); i++)
            {
                //byte sectionIndex = message.ReadByte();
                float damage = message.ReadRangedSingle(0.0f, 1.0f, 8) * Health;

                //if (sectionIndex < 0 || sectionIndex >= sections.Length) continue;

                SetDamage(i, damage);
            }

            lastUpdate = sendingTime;

            return true;
        }
    
    }
}
