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
using Subsurface.Networking;
using Subsurface.Lights;

namespace Subsurface
{
    class WallSection
    {
        public Rectangle rect;
        public float damage;
        public Gap gap;

        public bool isHighLighted;
        
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
        public static List<Structure> wallList = new List<Structure>();

        ConvexHull convexHull;

        StructurePrefab prefab;

        //farseer physics bodies, separated by gaps
        List<Body> bodies;

        //sections of the wall that are supposed to be rendered
        private WallSection[] sections;

        bool isHorizontal;
        
        public override Sprite sprite
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

            if (convexHull!=null)
            {
                convexHull.Move(amount);
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
                
        public Structure(Rectangle rectangle, StructurePrefab sp)
        {
            if (rectangle.Width == 0 || rectangle.Height == 0) return;

            rect = rectangle;
            prefab = sp;
            
            isHorizontal = (rect.Width>rect.Height);
            
            if (prefab.HasBody)
            {
                bodies = new List<Body>();
                //gaps = new List<Gap>();

                Body newBody = BodyFactory.CreateRectangle(Game1.World,
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

                wallList.Add(this);
                        
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

                    Body newBody = BodyFactory.CreateRectangle(Game1.World,
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

                    //newBody = BodyFactory.CreateRectangle(Game1.World,
                    //    ConvertUnits.ToSimUnits(Submarine.GridSize.X*2),
                    //    ConvertUnits.ToSimUnits(10.0f),
                    //    1.5f);

                    //newBody.BodyType = BodyType.Static;
                    ////newBody.IsSensor = true;

                    //newBody.Position = ConvertUnits.ToSimUnits(
                    //    new Vector2(Position.X + (rect.Width/2 + Submarine.GridSize.X) * ((StairDirection == Direction.Right) ? -1.0f : 1.0f), rect.Y + 5.0f));
                    ////newBody.Rotation = (StairDirection == Direction.Right) ? MathHelper.PiOver4 : -MathHelper.PiOver4;
                    ////newBody.Friction = 0.8f;

                    //newBody.CollisionCategories = Physics.CollisionStairs;

                    //newBody.UserData = this;

                    //bodies.Add(newBody);

                }
            }


            if (prefab.CastShadow)
            {

                Vector2[] corners = new Vector2[4];
                corners[0] = new Vector2(rect.X, rect.Y - rect.Height);
                corners[1] = new Vector2(rect.X, rect.Y);
                corners[2] = new Vector2(rect.Right, rect.Y);
                corners[3] = new Vector2(rect.Right, rect.Y - rect.Height);

                convexHull = new ConvexHull(corners, Color.Black);
            }

            mapEntityList.Add(this);
        }
        
        public override void Remove()
        {
            base.Remove();

            if (wallList.Contains(this)) wallList.Remove(this);

            if (bodies != null)
            {
                foreach (Body b in bodies)
                    Game1.World.RemoveBody(b);
            }

            if (convexHull != null) convexHull.Remove();
        }


        public override void Draw(SpriteBatch spriteBatch, bool editing)
        {
            if (prefab.sprite == null) return;

            Color color = (isHighlighted) ? Color.Green : Color.White;
            if (isSelected && editing) color = Color.Red;
            
            prefab.sprite.DrawTiled(spriteBatch, new Vector2(rect.X, -rect.Y), new Vector2(rect.Width, rect.Height), Vector2.Zero, color); 
 
            foreach (WallSection s in sections)
            {
                if (s.isHighLighted)
                    GUI.DrawRectangle(spriteBatch,
                        new Rectangle((int)s.rect.X, (int)-s.rect.Y, (int)s.rect.Width, (int)s.rect.Height),
                        new Color((s.damage / prefab.MaxHealth), 1.0f - (s.damage / prefab.MaxHealth), 0.0f, 1.0f), true);

                s.isHighLighted = false;

                if (s.damage == 0.0f) continue;
                
                GUI.DrawRectangle(spriteBatch, 
                    new Rectangle((int)s.rect.X, (int)-s.rect.Y, (int)s.rect.Width, (int)s.rect.Height),
                    Color.Black * (s.damage / prefab.MaxHealth), true); 
            }
            
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
            
            if (!prefab.IsPlatform && prefab.StairDirection == Direction.None)
            {
                Vector2 pos = ConvertUnits.ToDisplayUnits(f2.Body.Position);

                int section = FindSectionIndex(pos);
                if (section>0)
                {
                    Vector2 normal = contact.Manifold.LocalNormal;

                    float impact = Vector2.Dot(f2.Body.LinearVelocity, -normal)*f2.Body.Mass*0.1f;

                    if (impact < 10.0f) return true;

                    AmbientSoundManager.PlayDamageSound(DamageSoundType.StructureBlunt, impact,
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
        
        public bool SectionHasHole(int sectionIndex)
        {
            if (sectionIndex < 0 || sectionIndex >= sections.Length) return false;

            return (sections[sectionIndex].damage>=prefab.MaxHealth);
        }

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

            if (Game1.Client==null)
                SetDamage(sectionIndex, sections[sectionIndex].damage + damage);

        }

        public int FindSectionIndex(Vector2 pos)
        {
            int index = (isHorizontal) ?
                (int)Math.Floor((pos.X - rect.X) / wallSectionSize) :
                (int)Math.Floor((rect.Y - pos.Y) / wallSectionSize);

            if (index < 0 || index > sections.Length - 1) return -1;
            return index;
        }

        public float SectionDamage(int sectionIndex)
        {
            if (sectionIndex < 0 || sectionIndex >= sections.Length) return 0.0f;

            return sections[sectionIndex].damage;
        }

        public Vector2 SectionPosition(int sectionIndex)
        {
            if (sectionIndex < 0 || sectionIndex >= sections.Length) return Vector2.Zero;

            return new Vector2(
                sections[sectionIndex].rect.X + sections[sectionIndex].rect.Width / 2.0f,
                sections[sectionIndex].rect.Y - sections[sectionIndex].rect.Height / 2.0f);
        }

        public AttackResult AddDamage(Vector2 position, DamageType damageType, float amount, float bleedingAmount, float stun, bool playSound = false)
        {
            if (!prefab.HasBody || prefab.IsPlatform) return new AttackResult(0.0f, 0.0f);

            int i = FindSectionIndex(ConvertUnits.ToDisplayUnits(position));
            if (i == -1) return new AttackResult(0.0f, 0.0f);
            
            Game1.ParticleManager.CreateParticle("dustcloud", SectionPosition(i), 0.0f, 0.0f);

            if (playSound && !SectionHasHole(i))
            {
                DamageSoundType damageSoundType = (damageType == DamageType.Blunt) ? DamageSoundType.StructureBlunt : DamageSoundType.StructureSlash;
                AmbientSoundManager.PlayDamageSound(damageSoundType, amount, position);
            }

            AddDamage(i, amount);

            return new AttackResult(amount, 0.0f);
        }

        private void SetDamage(int sectionIndex, float damage)
        {
            if (!prefab.HasBody) return;

            if (damage != sections[sectionIndex].damage)
                new NetworkEvent(NetworkEventType.UpdateEntity, ID, false, sectionIndex);
            
            if (damage < prefab.MaxHealth*0.5f)
            {
                if (sections[sectionIndex].gap != null)
                {
                    //remove existing gap if damage is below 50%
                    sections[sectionIndex].gap.Remove();
                    sections[sectionIndex].gap = null;
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
                    sections[sectionIndex].gap = new Gap(gapRect, !isHorizontal);
                }
            }

            if (sections[sectionIndex].gap != null)                    
                sections[sectionIndex].gap.Open = (float)Math.Pow(((damage / prefab.MaxHealth)-0.5)*2.0, 2.0);

            bool hadHole = SectionHasHole(sectionIndex);
            sections[sectionIndex].damage = MathHelper.Clamp(damage, 0.0f, prefab.MaxHealth);

            bool hasHole = SectionHasHole(sectionIndex);

            if (hadHole != hasHole) UpdateSections();        

        }

        private void UpdateSections()
        {
            foreach (Body b in bodies)
            {
                Game1.World.RemoveBody(b);
            }
            bodies.Clear();

            int x = sections[0].rect.X;
            int y = sections[0].rect.Y;
            int width = sections[0].rect.Width;
            int height = sections[0].rect.Height;

            bool hasHoles = false;

            for (int i = 1; i < sections.Length; i++)
            {
                bool hasHole = SectionHasHole(i);
                if (hasHole) hasHoles = true;
                if (hasHole || i == sections.Length - 1)
                {
                    if (width > 0 && height > 0)
                    {
                        CreateRectBody(new Rectangle(x, y, width, height));
                    }
                    if (isHorizontal)
                    {
                        x = sections[i].rect.X+ sections[i].rect.Width;
                        width = 0;
                    }
                    else
                    {
                        y = sections[i].rect.Y - sections[i].rect.Height;
                        height = 0;
                    }                    
                }
                else
                {
                    if (isHorizontal) 
                    {
                        width += sections[i].rect.Width;
                    }
                    else
                    {
                       height += sections[i].rect.Height;
                    }
                }
            }

            if (hasHoles)
            {
                CreateRectBody(rect).IsSensor = true;
            }

        }

        private Body CreateRectBody(Rectangle rect)
        {
            Body newBody = BodyFactory.CreateRectangle(Game1.World,
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

        
        public override XElement Save(XDocument doc)
        {
            XElement element = new XElement("Structure");
            
            element.Add(new XAttribute("name", prefab.Name),
                new XAttribute("ID", ID),
                new XAttribute("rect", rect.X + "," + rect.Y+","+rect.Width+","+rect.Height));

            for (int i = 0; i < sections.Count(); i++)
            {
                if (sections[i].damage == 0.0f) continue;

                element.Add(new XElement("section",
                    new XAttribute("i", i),
                    new XAttribute("damage", sections[i].damage)));
            }
            
            doc.Root.Add(element);

            return element;
        }

        public static void Load(XElement element)
        {
            string rectString = ToolBox.GetAttributeString(element, "rect", "0,0,0,0");
            string[] rectValues = rectString.Split(',');

            Rectangle rect = new Rectangle(
                int.Parse(rectValues[0]),
                int.Parse(rectValues[1]),
                int.Parse(rectValues[2]),
                int.Parse(rectValues[3]));

            string name = element.Attribute("name").Value;
            
            Debug.WriteLine(name+" - "+rect);

            Structure s = null;

            foreach (MapEntityPrefab ep in MapEntityPrefab.list)
            {
                if (ep.Name == name)
                {
                    s = new Structure(rect, (StructurePrefab)ep);
                    s.ID = int.Parse(element.Attribute("ID").Value);
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
                        if (subElement.Attribute("i") == null) continue;

                        s.sections[int.Parse(subElement.Attribute("i").Value)].damage = 
                            ToolBox.GetAttributeFloat(subElement, "damage", 0.0f);

                        break;
                }
            }

        }

        public override void FillNetworkData(NetworkEventType type, NetOutgoingMessage message, object data)
        {
            int sectionIndex = 0;
            byte byteIndex = 0;

            try
            {
                sectionIndex = (int)data;
                byteIndex = (byte)sectionIndex;
            }
            catch
            {
                return;
            }

            message.Write(byteIndex);
            message.Write(sections[sectionIndex].damage);
        }

        public override void ReadNetworkData(NetworkEventType type, NetIncomingMessage message)
        {
            int sectionIndex = 0;
            float damage = 0.0f;

            try
            {
                sectionIndex = message.ReadByte();
                damage = message.ReadFloat();
            }
            catch
            {
                return;
            }

            SetDamage(sectionIndex, damage);
            
        }
    
    }
}
