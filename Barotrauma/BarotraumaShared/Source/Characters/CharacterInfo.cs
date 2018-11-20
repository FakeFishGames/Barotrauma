using Barotrauma.Networking;
using Barotrauma.Extensions;
using Lidgren.Network;
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
        private static Dictionary<string, XDocument> cachedConfigs = new Dictionary<string, XDocument>();

        private static ushort idCounter;

        public string Name;
        public string DisplayName
        {
            get
            {
                string disguiseName = "?";
                if (Character == null || !Character.HideFace || (GameMain.Server != null && !GameMain.Server.AllowDisguises))
                {
                    return Name;
                }
#if CLIENT
                if (GameMain.Client != null && !GameMain.Client.AllowDisguises)
                {
                    return Name;
                }
#endif
                if (Character.Inventory != null)
                {
                    int cardSlotIndex = Character.Inventory.FindLimbSlot(InvSlotType.Card);
                    if (cardSlotIndex < 0) return disguiseName;

                    var idCard = Character.Inventory.Items[cardSlotIndex];
                    if (idCard == null) return disguiseName;

                    //Disguise as the ID card name if it's equipped                    
                    string[] readTags = idCard.Tags.Split(',');
                    foreach (string tag in readTags)
                    {
                        string[] s = tag.Split(':');
                        if (s[0] == "name")
                        {
                            return s[1];
                        }
                    }
                }
                return disguiseName;
            }
        }

        /// <summary>
        /// Note: Can be null.
        /// </summary>
        public Character Character;

        public readonly string File;
        
        public Job Job;

        private List<ushort> pickedItems;

        public ushort? HullID = null;

        private Gender gender;

        public int Salary;

        private Vector2[] headSpriteRange;

        private int headSpriteId;
        private Sprite headSprite;
        private Sprite portrait;

        public XElement HairElement { get; private set; }
        public XElement BeardElement { get; private set; }
        public XElement MoustacheElement { get; private set; }
        public XElement FaceAttachment { get; private set; }

        public bool StartItemsGiven;

        public CauseOfDeath CauseOfDeath;

        public byte TeamID;

        private NPCPersonalityTrait personalityTrait;

        //unique ID given to character infos in MP
        //used by clients to identify which infos are the same to prevent duplicate characters in round summary
        public ushort ID;

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
        public Sprite Portrait
        {
            get
            {
                if (portrait == null) LoadPortrait();
                return portrait;
            }
        }

        public List<string> SpriteTags
        {
            get;
            private set;
        }

        public NPCPersonalityTrait PersonalityTrait
        {
            get { return personalityTrait; }
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

        private HumanRagdollParams ragdoll;
        public HumanRagdollParams Ragdoll
        {
            get
            {
                if (ragdoll == null)
                {
                    ragdoll = HumanRagdollParams.GetDefaultRagdollParams(Character?.SpeciesName ?? "human");
                }
                return ragdoll;
            }
            set { ragdoll = value; }
        }

        public CharacterInfo(string file, string name = "", Gender gender = Gender.None, JobPrefab jobPrefab = null, HumanRagdollParams ragdoll = null)
        {
            ID = idCounter;
            idCounter++;

            this.File = file;

            headSpriteRange = new Vector2[2];
            pickedItems = new List<ushort>();
            SpriteTags = new List<string>();

            XDocument doc = null;
            if (cachedConfigs.ContainsKey(file))
            {
                doc = cachedConfigs[file];
            }
            else
            {
                doc = XMLExtensions.TryLoadXml(file);
                if (doc == null) return;

                cachedConfigs.Add(file, doc);
            }

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
                HeadSpriteId = Rand.Range((int)headSpriteRange[genderIndex].X, (int)headSpriteRange[genderIndex].Y + 1, Rand.RandSync.Server);
            }

            this.Job = (jobPrefab == null) ? Job.Random(Rand.RandSync.Server) : new Job(jobPrefab);

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

            personalityTrait = NPCPersonalityTrait.GetRandom(name + HeadSpriteId);
            
            Salary = CalculateSalary();
            if (ragdoll != null)
            {
                this.ragdoll = ragdoll;
            }

            var attachments = doc.Root.Element("HeadAttachments");
            if (attachments != null)
            {
                // TODO: also allow no element -> bald/nofacial hair
                HairElement = attachments.Elements("Wearable").GetRandom(e => FilterWearable(e, WearableType.Hair));
                BeardElement = attachments.Elements("Wearable").GetRandom(e => FilterWearable(e, WearableType.Beard));
                MoustacheElement = attachments.Elements("Wearable").GetRandom(e => FilterWearable(e, WearableType.Moustache));
                FaceAttachment = attachments.Elements("Wearable").GetRandom(e => FilterWearable(e, WearableType.FaceAttachment));

                bool FilterWearable(XElement element, WearableType targetType)
                {
                    if (!Enum.TryParse(element.GetAttributeString("type", ""), true, out WearableType type) || type != targetType) { return false; }
                    var p = element.Element("sprite").GetAttributeString("texture", string.Empty);
                    return System.IO.File.Exists(p) && Path.GetFullPath(p) == Path.GetFullPath(HeadSprite.FilePath);
                }
            }
        }

        public CharacterInfo(XElement element)
        {
            ID = idCounter;
            idCounter++;

            Name = element.GetAttributeString("name", "unnamed");

            string genderStr = element.GetAttributeString("gender", "male").ToLowerInvariant();
            gender = (genderStr == "m") ? Gender.Male : Gender.Female;

            File = element.GetAttributeString("file", "");
            Salary = element.GetAttributeInt("salary", 1000);
            headSpriteId = element.GetAttributeInt("headspriteid", 1);
            StartItemsGiven = element.GetAttributeBool("startitemsgiven", false);

            string personalityName = element.GetAttributeString("personality", "");
            if (!string.IsNullOrEmpty(personalityName))
            {
                personalityTrait = NPCPersonalityTrait.List.Find(p => p.Name == personalityName);
            }

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

        private void LoadHeadSprite()
        {
            foreach (XElement limbElement in XMLExtensions.TryLoadXml(Ragdoll.FullPath).Root.Elements())
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

        private void LoadPortrait()
        {
            string headSpriteDir = Path.GetDirectoryName(HeadSprite.FilePath);

            string portraitPath = Path.Combine(headSpriteDir, (gender == Gender.Male ? "portrait" + headSpriteId : "fportrait" + headSpriteId) + ".png");
            if (System.IO.File.Exists(portraitPath))
            {
                portrait = new Sprite(portraitPath, Vector2.Zero);
            }
            else
            {
                portrait = new Sprite("Content/Characters/Human/defaultportrait.png", Vector2.Zero);
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
        
        private int CalculateSalary()
        {
            if (Name == null || Job == null) return 0;

            int salary = Math.Abs(Name.GetHashCode()) % 100;

            foreach (Skill skill in Job.Skills)
            {
                salary += (int)skill.Level * 10;
            }

            return salary;
        }

        public void IncreaseSkillLevel(string skillIdentifier, float increase, Vector2 worldPos)
        {
            if (Job == null || GameMain.Client != null) return;            

            float prevLevel = Job.GetSkillLevel(skillIdentifier);
            Job.IncreaseSkillLevel(skillIdentifier, increase);

            float newLevel = Job.GetSkillLevel(skillIdentifier);

            OnSkillChanged(skillIdentifier, prevLevel, newLevel, worldPos);

            if (GameMain.Server != null && (int)newLevel != (int)prevLevel)
            {
                GameMain.Server.CreateEntityEvent(Character, new object[] { NetEntityEvent.Type.UpdateSkills });                
            }
        }

        public void SetSkillLevel(string skillIdentifier, float level, Vector2 worldPos)
        {
            if (Job == null) return;

            var skill = Job.Skills.Find(s => s.Identifier == skillIdentifier);
            if (skill == null)
            {
                Job.Skills.Add(new Skill(skillIdentifier, level));
                OnSkillChanged(skillIdentifier, 0.0f, skill.Level, worldPos);
            }
            else
            {
                float prevLevel = skill.Level;
                skill.Level = level;
                OnSkillChanged(skillIdentifier, prevLevel, skill.Level, worldPos);
            }
        }

        partial void OnSkillChanged(string skillIdentifier, float prevLevel, float newLevel, Vector2 textPopupPos);

        public virtual XElement Save(XElement parentElement)
        {
            XElement charElement = new XElement("Character");

            charElement.Add(
                new XAttribute("name", Name),
                new XAttribute("file", File),
                new XAttribute("gender", gender == Gender.Male ? "m" : "f"),
                new XAttribute("salary", Salary),
                new XAttribute("headspriteid", HeadSpriteId),
                new XAttribute("startitemsgiven", StartItemsGiven),
                new XAttribute("personality", personalityTrait == null ? "" : personalityTrait.Name));
            
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

        public void ServerWrite(NetBuffer msg)
        {
            msg.Write(ID);
            msg.Write(Name);
            msg.Write(Gender == Gender.Female);
            msg.Write((byte)HeadSpriteId);
            if (Job != null)
            {
                msg.Write(Job.Prefab.Identifier);
                msg.Write((byte)Job.Skills.Count);
                foreach (Skill skill in Job.Skills)
                {
                    msg.Write(skill.Identifier);
                    msg.Write(skill.Level);
                }
            }
            else
            {
                msg.Write("");
            }
        }

        public static CharacterInfo ClientRead(string configPath, NetBuffer inc)
        {
            ushort infoID       = inc.ReadUInt16();
            string newName      = inc.ReadString();
            bool isFemale       = inc.ReadBoolean();
            int headSpriteID    = inc.ReadByte();
            string jobIdentifier      = inc.ReadString();

            JobPrefab jobPrefab = null;
            Dictionary<string, float> skillLevels = new Dictionary<string, float>();
            if (!string.IsNullOrEmpty(jobIdentifier))
            {
                jobPrefab = JobPrefab.List.Find(jp => jp.Identifier == jobIdentifier);
                int skillCount = inc.ReadByte();
                for (int i = 0; i < skillCount; i++)
                {
                    string skillIdentifier = inc.ReadString();
                    float skillLevel = inc.ReadSingle();
                    skillLevels.Add(skillIdentifier, skillLevel);
                }
            }

            CharacterInfo ch = new CharacterInfo(configPath, newName, isFemale ? Gender.Female : Gender.Male, jobPrefab)
            {
                ID = infoID,
                HeadSpriteId = headSpriteID
            };

            System.Diagnostics.Debug.Assert(skillLevels.Count == ch.Job.Skills.Count);
            if (ch.Job != null)
            {
                foreach (KeyValuePair<string, float> skill in skillLevels)
                {
                    Skill matchingSkill = ch.Job.Skills.Find(s => s.Identifier == skill.Key);
                    if (matchingSkill == null)
                    {
                        DebugConsole.ThrowError("Skill \"" + skill.Key + "\" not found in character \"" + newName + "\"");
                        continue;
                    }
                    matchingSkill.Level = skill.Value;
                }
            }
            return ch;
        }

        public void Remove()
        {
            Character = null;
            if (headSprite != null)
            {
                headSprite.Remove();
                headSprite = null;
            }
            if (portrait != null)
            {
                portrait.Remove();
                portrait = null;
            }
        }
    }
}
