using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    public enum Gender { None, Male, Female };        

    partial class CharacterInfo
    {
        public string Name;

        public Character Character;

        public readonly string File;
        
        public Job Job;

        private List<ushort> pickedItems;

        public ushort? HullID = null;

        private Vector2[] headSpriteRange;

        private Gender gender;

        public int Salary;

        private int headSpriteId;
        private Sprite headSprite;

        public bool StartItemsGiven;

        public CauseOfDeath CauseOfDeath;

        public byte TeamID;

        public List<ushort> PickedItemIDs
        {
            get { return pickedItems; }
        }
        
        public Sprite HeadSprite
        {
            get
            {
                if (headSprite == null) LoadHeadSprite();
                return headSprite;
            }
        }

        public List<string> SpriteTags
        {
            get;
            private set;
        }

        public int HeadSpriteId
        {
            get { return headSpriteId; }
            set
            {
                int oldId = headSpriteId;

                headSpriteId = value;
                Vector2 spriteRange = headSpriteRange[gender == Gender.Male ? 0 : 1];
                
                if (headSpriteId < (int)spriteRange.X) headSpriteId = (int)(spriteRange.Y);
                if (headSpriteId > (int)spriteRange.Y) headSpriteId = (int)(spriteRange.X);

                if (headSpriteId != oldId) headSprite = null;
            }
        }

        public Gender Gender
        {
            get { return gender; }
            set
            {
                if (gender == value) return;
                gender = value;

                int genderIndex = (this.gender == Gender.Female) ? 1 : 0;
                if (headSpriteRange[genderIndex] != Vector2.Zero)
                {
                    HeadSpriteId = Rand.Range((int)headSpriteRange[genderIndex].X, (int)headSpriteRange[genderIndex].Y + 1);
                }
                else
                {
                    HeadSpriteId = 0;
                }

                LoadHeadSprite();
            }
        }

        public CharacterInfo(string file, string name = "", Gender gender = Gender.None, JobPrefab jobPrefab = null)
        {
            this.File = file;

            headSpriteRange = new Vector2[2];

            pickedItems = new List<ushort>();

            SpriteTags = new List<string>();

            //ID = -1;

            XDocument doc = XMLExtensions.TryLoadXml(file);
            if (doc == null) return;

            if (doc.Root.GetAttributeBool("genders", false))
            {
                if (gender == Gender.None)
                {
                    float femaleRatio = doc.Root.GetAttributeFloat("femaleratio", 0.5f);
                    this.gender = (Rand.Range(0.0f, 1.0f, Rand.RandSync.Server) < femaleRatio) ? Gender.Female : Gender.Male;
                }
                else
                {
                    this.gender = gender;
                }
            }
                       
            headSpriteRange[0] = doc.Root.GetAttributeVector2("headid", Vector2.Zero);
            headSpriteRange[1] = headSpriteRange[0];
            if (headSpriteRange[0] == Vector2.Zero)
            {
                headSpriteRange[0] = doc.Root.GetAttributeVector2("maleheadid", Vector2.Zero);
                headSpriteRange[1] = doc.Root.GetAttributeVector2("femaleheadid", Vector2.Zero);
            }

            int genderIndex = (this.gender == Gender.Female) ? 1 : 0;
            if (headSpriteRange[genderIndex] != Vector2.Zero)
            {
                HeadSpriteId = Rand.Range((int)headSpriteRange[genderIndex].X, (int)headSpriteRange[genderIndex].Y + 1);
            }

            this.Job = (jobPrefab == null) ? Job.Random() : new Job(jobPrefab);

            if (!string.IsNullOrEmpty(name))
            {
                this.Name = name;
                return;
            }

            name = "";

            if (doc.Root.Element("name") != null)
            {
                string firstNamePath = doc.Root.Element("name").GetAttributeString("firstname", "");
                if (firstNamePath != "")
                {
                    firstNamePath = firstNamePath.Replace("[GENDER]", (this.gender == Gender.Female) ? "f" : "");
                    this.Name = ToolBox.GetRandomLine(firstNamePath);
                }

                string lastNamePath = doc.Root.Element("name").GetAttributeString("lastname", "");
                if (lastNamePath != "")
                {
                    lastNamePath = lastNamePath.Replace("[GENDER]", (this.gender == Gender.Female) ? "f" : "");
                    if (this.Name != "") this.Name += " ";
                    this.Name += ToolBox.GetRandomLine(lastNamePath);
                }
            }
            
            Salary = CalculateSalary();
        }

        private void LoadHeadSprite()
        {
            XDocument doc = XMLExtensions.TryLoadXml(File);
            if (doc == null) return;

            XElement ragdollElement = doc.Root.Element("ragdoll");
            foreach (XElement limbElement in ragdollElement.Elements())
            {
                if (limbElement.GetAttributeString("type", "").ToLowerInvariant() != "head") continue;

                XElement spriteElement = limbElement.Element("sprite");

                string spritePath = spriteElement.Attribute("texture").Value;

                spritePath = spritePath.Replace("[GENDER]", (this.gender == Gender.Female) ? "f" : "");
                spritePath = spritePath.Replace("[HEADID]", HeadSpriteId.ToString());
                
                string fileName = Path.GetFileNameWithoutExtension(spritePath);

                //go through the files in the directory to find a matching sprite
                var files = Directory.GetFiles(Path.GetDirectoryName(spritePath)).ToList();
                foreach (string file in files)
                {
                    string fileWithoutTags = Path.GetFileNameWithoutExtension(file);
                    fileWithoutTags = fileWithoutTags.Split('[', ']').First();

                    if (fileWithoutTags != fileName) continue;
                    
                    headSprite = new Sprite(spriteElement, "", file);

                    //extract the tags out of the filename
                    SpriteTags = file.Split('[', ']').Skip(1).ToList();
                    if (SpriteTags.Any())
                    {
                        SpriteTags.RemoveAt(SpriteTags.Count-1);
                    }

                    break;                    
                }

                break;
            }
        }
        
        public void UpdateCharacterItems()
        {
            pickedItems.Clear();
            foreach (Item item in Character.Inventory.Items)
            {
                pickedItems.Add(item == null ? (ushort)0 : item.ID);
            }
        }

        public CharacterInfo(XElement element)
        {
            Name = element.GetAttributeString("name", "unnamed");

            string genderStr = element.GetAttributeString("gender", "male").ToLowerInvariant();
            gender = (genderStr == "m") ? Gender.Male : Gender.Female;

            File            = element.GetAttributeString("file", "");
            Salary          = element.GetAttributeInt("salary", 1000);
            headSpriteId    = element.GetAttributeInt("headspriteid", 1);
            StartItemsGiven = element.GetAttributeBool("startitemsgiven", false);
            
            int hullId = element.GetAttributeInt("hull", -1);
            if (hullId > 0 && hullId <= ushort.MaxValue) this.HullID = (ushort)hullId;            

            pickedItems = new List<ushort>();

            string pickedItemString = element.GetAttributeString("items", "");
            if (!string.IsNullOrEmpty(pickedItemString))
            {
                string[] itemIds = pickedItemString.Split(',');
                foreach (string s in itemIds)
                {
                    pickedItems.Add((ushort)int.Parse(s));
                }
            }

            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() != "job") continue;

                Job = new Job(subElement);
                break;
            }
        }

        private int CalculateSalary()
        {
            if (Name == null || Job == null) return 0;

            int salary = Math.Abs(Name.GetHashCode()) % 100;

            foreach (Skill skill in Job.Skills)
            {
                salary += skill.Level * 10;
            }

            return salary;
        }

        public virtual XElement Save(XElement parentElement)
        {
            XElement charElement = new XElement("Character");

            charElement.Add(
                new XAttribute("name", Name),
                new XAttribute("file", File),
                new XAttribute("gender", gender == Gender.Male ? "m" : "f"),
                new XAttribute("salary", Salary),
                new XAttribute("headspriteid", HeadSpriteId),
                new XAttribute("startitemsgiven", StartItemsGiven));
            
            if (Character != null)
            {
                if (Character.Inventory != null)
                {
                    UpdateCharacterItems();
                }

                if (Character.AnimController.CurrentHull != null)
                {
                    HullID = Character.AnimController.CurrentHull.ID;
                    charElement.Add(new XAttribute("hull", Character.AnimController.CurrentHull.ID));
                }
            }
            
            if (pickedItems.Count > 0)
            {
                charElement.Add(new XAttribute("items", string.Join(",", pickedItems)));
            }

            Job.Save(charElement);

            parentElement.Add(charElement);
            return charElement;
        }

        public void Remove()
        {
            Character = null;
            //if (headSprite != null)
            //{
            //    headSprite.Remove();
            //    headSprite = null;
            //}
        }
    }
}
