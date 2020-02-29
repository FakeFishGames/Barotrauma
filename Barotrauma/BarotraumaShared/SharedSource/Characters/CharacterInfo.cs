using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    public enum Gender { None, Male, Female };
    public enum Race { None, White, Black, Brown, Asian };
    
    // TODO: Generating the HeadInfo could be simplified.
    partial class CharacterInfo
    {
        public class HeadInfo
        {
            private int _headSpriteId;
            public int HeadSpriteId
            {
                get { return _headSpriteId; }
                set
                {
                    _headSpriteId = value;
                    if (_headSpriteId < (int)headSpriteRange.X)
                    {
                        _headSpriteId = (int)headSpriteRange.Y;
                    }
                    if (_headSpriteId > (int)headSpriteRange.Y)
                    {
                        _headSpriteId = (int)headSpriteRange.X;
                    }
                    GetSpriteSheetIndex();
                }
            }
            public Vector2? SheetIndex { get; private set; }
            public Vector2 headSpriteRange;
            public Gender gender;
            public Race race;

            public int HairIndex { get; set; } = -1;
            public int BeardIndex { get; set; } = -1;
            public int MoustacheIndex { get; set; } = -1;
            public int FaceAttachmentIndex { get; set; } = -1;

            public XElement HairElement { get; set; }
            public XElement BeardElement { get; set; }
            public XElement MoustacheElement { get; set; }
            public XElement FaceAttachment { get; set; }
            
            public HeadInfo() { }

            public HeadInfo(int headId, Gender gender, Race race, int hairIndex = 0, int beardIndex = 0, int moustacheIndex = 0, int faceAttachmentIndex = 0)
            {
                _headSpriteId = Math.Max(headId, 1);
                this.gender = gender;
                this.race = race;
                HairIndex = hairIndex;
                BeardIndex = beardIndex;
                MoustacheIndex = moustacheIndex;
                FaceAttachmentIndex = faceAttachmentIndex;
                GetSpriteSheetIndex();
            }

            public void ResetAttachmentIndices()
            {
                HairIndex = -1;
                BeardIndex = -1;
                MoustacheIndex = -1;
                FaceAttachmentIndex = -1;
            }

            private void GetSpriteSheetIndex()
            {
                if (heads != null && heads.Any())
                {
                    var matchingHead = heads.Keys.FirstOrDefault(h => h.Gender == gender && h.Race == race && h.ID == _headSpriteId);
                    if (matchingHead != null)
                    {
                        if (heads.TryGetValue(matchingHead, out Vector2 index))
                        {
                            SheetIndex = index;
                        }
                    }
                }
            }
        }

        private HeadInfo head;
        public HeadInfo Head
        {
            get { return head; }
            set
            {
                if (head != value && value != null)
                {
                    head = value;
                    if (head.race == Race.None)
                    {
                        head.race = GetRandomRace();
                    }
                    CalculateHeadSpriteRange();
                    Head.HeadSpriteId = value.HeadSpriteId;
                    HeadSprite = null;
                    AttachmentSprites = null;
                }
            }
        }

        public Dictionary<HeadPreset, Vector2> Heads
        {
            get
            {
                if (heads == null)
                {
                    LoadHeadPresets();
                }
                return heads;
            }
        }

        private static Dictionary<HeadPreset, Vector2> heads;
        public class HeadPreset : ISerializableEntity
        {
            [Serialize(Race.None, false)]
            public Race Race { get; private set; }

            [Serialize(Gender.None, false)]
            public Gender Gender { get; private set; }

            [Serialize(0, false)]
            public int ID { get; private set; }

            [Serialize("0,0", false)]
            public Vector2 SheetIndex { get; private set; }

            public string Name => $"Head Preset {Race} {Gender} {ID}";

            public Dictionary<string, SerializableProperty> SerializableProperties { get; private set; }

            public HeadPreset(XElement element)
            {
                SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
            }
        }

        private static ushort idCounter;

        public string Name;
        public string DisplayName
        {
            get
            {
                string disguiseName = "?";
                if (Character == null || !Character.HideFace)
                {
                    return Name;
                }
                else if ((GameMain.NetworkMember != null && !GameMain.NetworkMember.ServerSettings.AllowDisguises))
                {
                    return Name;
                }

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

        private string _speciesName;
        public string SpeciesName
        {
            get
            {
                if (_speciesName == null)
                {
                    _speciesName = CharacterConfigElement.GetAttributeString("speciesname", string.Empty).ToLowerInvariant();
                }
                return _speciesName;
            }
            set { _speciesName = value; }
        }

        /// <summary>
        /// Note: Can be null.
        /// </summary>
        public Character Character;
        
        public Job Job;
        
        public int Salary;

        private Sprite headSprite;
        public Sprite HeadSprite
        {
            get
            {
                if (headSprite == null)
                {
                    LoadHeadSprite();
                }
#if CLIENT
                if (headSprite != null)
                {
                    CalculateHeadPosition(headSprite);
                }
#endif
                return headSprite;
            }
            private set
            {
                if (headSprite != null)
                {
                    headSprite.Remove();
                }
                headSprite = value;
            }
        }

        public bool OmitJobInPortraitClothing;

        private Sprite portrait;
        public Sprite Portrait
        {
            get
            {
                if (portrait == null)
                {
                    LoadHeadSprite();
                }
                return portrait;
            }
            private set
            {
                if (portrait != null)
                {
                    portrait.Remove();
                }
                portrait = value;
            }
        }

        private List<WearableSprite> attachmentSprites;
        public List<WearableSprite> AttachmentSprites
        {
            get
            {
                if (attachmentSprites == null)
                {
                    LoadAttachmentSprites(OmitJobInPortraitClothing);
                }
                return attachmentSprites;
            }
            private set
            {
                if (attachmentSprites != null)
                {
                    attachmentSprites.ForEach(s => s.Sprite?.Remove());
                }
                attachmentSprites = value;
            }
        }

        public XElement CharacterConfigElement { get; set; }

        public readonly string ragdollFileName = string.Empty;

        public bool StartItemsGiven;

        public CauseOfDeath CauseOfDeath;

        public Character.TeamType TeamID;

        private NPCPersonalityTrait personalityTrait;

        //unique ID given to character infos in MP
        //used by clients to identify which infos are the same to prevent duplicate characters in round summary
        public ushort ID;

        public XElement InventoryData;

        public List<string> SpriteTags
        {
            get;
            private set;
        }

        public NPCPersonalityTrait PersonalityTrait
        {
            get { return personalityTrait; }
        }

        /// <summary>
        /// Setting the value with this property also resets the head attachments. Use Head.headSpriteId if you don't want that.
        /// </summary>
        public int HeadSpriteId
        {
            get { return Head.HeadSpriteId; }
            set
            {
                Head.HeadSpriteId = value;
                HeadSprite = null;
                AttachmentSprites = null;
                ResetHeadAttachments();
            }
        }

        public readonly bool HasGenders;

        public Gender Gender
        {
            get { return Head.gender; }
            set
            {
                if (Head.gender == value) return;
                Head.gender = value;
                if (Head.gender == Gender.None)
                {
                    Head.gender = Gender.Male;
                }
                CalculateHeadSpriteRange();
                ResetHeadAttachments();
                HeadSprite = null;
                AttachmentSprites = null;
            }
        }

        public Race Race
        {
            get { return Head.race; }
            set
            {
                if (Head.race == value) { return; }
                Head.race = value;
                if (Head.race == Race.None)
                {
                    Head.race = Race.White;
                }
                CalculateHeadSpriteRange();
                ResetHeadAttachments();
                HeadSprite = null;
                AttachmentSprites = null;
            }
        }

        public int HairIndex { get => Head.HairIndex; set => Head.HairIndex = value; }
        public int BeardIndex { get => Head.BeardIndex; set => Head.BeardIndex = value; }
        public int MoustacheIndex { get => Head.MoustacheIndex; set => Head.MoustacheIndex = value; }
        public int FaceAttachmentIndex { get => Head.FaceAttachmentIndex; set => Head.FaceAttachmentIndex = value; }

        public XElement HairElement { get => Head.HairElement; set => Head.HairElement = value; }
        public XElement BeardElement { get => Head.BeardElement; set => Head.BeardElement = value; }
        public XElement MoustacheElement { get => Head.MoustacheElement; set => Head.MoustacheElement = value; }
        public XElement FaceAttachment { get => Head.FaceAttachment; set => Head.FaceAttachment = value; }

        private RagdollParams ragdoll;
        public RagdollParams Ragdoll
        {
            get
            {
                if (ragdoll == null)
                {
                    string speciesName = SpeciesName;
                    bool isHumanoid = CharacterConfigElement.GetAttributeBool("humanoid", speciesName.Equals(CharacterPrefab.HumanSpeciesName, StringComparison.OrdinalIgnoreCase));
                    ragdoll = isHumanoid 
                        ? HumanRagdollParams.GetRagdollParams(speciesName, ragdollFileName)
                        : RagdollParams.GetRagdollParams<FishRagdollParams>(speciesName, ragdollFileName) as RagdollParams;
                }
                return ragdoll;
            }
            set { ragdoll = value; }
        }

        public bool IsAttachmentsLoaded => HairIndex > -1 && BeardIndex > -1 && MoustacheIndex > -1 && FaceAttachmentIndex > -1;

        // Used for creating the data
        public CharacterInfo(string speciesName, string name = "", JobPrefab jobPrefab = null, string ragdollFileName = null, int variant = 0)
        {
            if (speciesName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                speciesName = Path.GetFileNameWithoutExtension(speciesName).ToLowerInvariant();
            }
            ID = idCounter;
            idCounter++;
            _speciesName = speciesName;
            SpriteTags = new List<string>();
            XDocument doc = CharacterPrefab.FindBySpeciesName(_speciesName)?.XDocument;
            if (doc == null) { return; }
            CharacterConfigElement = doc.Root.IsOverride() ? doc.Root.FirstElement() : doc.Root;
            head = new HeadInfo();
            HasGenders = CharacterConfigElement.GetAttributeBool("genders", false);
            if (HasGenders)
            {
                Head.gender = GetRandomGender();
            }
            Head.race = GetRandomRace();
            CalculateHeadSpriteRange();
            Head.HeadSpriteId = GetRandomHeadID();
            Job = (jobPrefab == null) ? Job.Random(Rand.RandSync.Server) : new Job(jobPrefab, variant);

            if (!string.IsNullOrEmpty(name))
            {
                Name = name;
            }
            else
            {
                name = "";
                if (CharacterConfigElement.Element("name") != null)
                {
                    string firstNamePath = CharacterConfigElement.Element("name").GetAttributeString("firstname", "");
                    if (firstNamePath != "")
                    {
                        firstNamePath = firstNamePath.Replace("[GENDER]", (Head.gender == Gender.Female) ? "female" : "male");
                        Name = ToolBox.GetRandomLine(firstNamePath);
                    }

                    string lastNamePath = CharacterConfigElement.Element("name").GetAttributeString("lastname", "");
                    if (lastNamePath != "")
                    {
                        lastNamePath = lastNamePath.Replace("[GENDER]", (Head.gender == Gender.Female) ? "female" : "male");
                        if (Name != "") Name += " ";
                        Name += ToolBox.GetRandomLine(lastNamePath);
                    }
                }
            }
            personalityTrait = NPCPersonalityTrait.GetRandom(name + HeadSpriteId);         
            Salary = CalculateSalary();
            if (ragdollFileName != null)
            {
                this.ragdollFileName = ragdollFileName;
            }
            LoadHeadAttachments();
        }

        // Used for loading the data
        public CharacterInfo(XElement infoElement)
        {
            ID = idCounter;
            idCounter++;
            Name = infoElement.GetAttributeString("name", "");
            string genderStr = infoElement.GetAttributeString("gender", "male").ToLowerInvariant();
            Salary = infoElement.GetAttributeInt("salary", 1000);
            Enum.TryParse(infoElement.GetAttributeString("race", "White"), true, out Race race);
            Enum.TryParse(infoElement.GetAttributeString("gender", "None"), true, out Gender gender);
            _speciesName = infoElement.GetAttributeString("speciesname", null);
            XDocument doc = null;
            if (_speciesName != null)
            {
                doc = CharacterPrefab.FindBySpeciesName(_speciesName)?.XDocument;
            }
            else
            {
                // Backwards support (human only)
                string file = infoElement.GetAttributeString("file", "");
                doc = XMLExtensions.TryLoadXml(file);
            }
            if (doc == null) { return; }
            CharacterConfigElement = doc.Root.IsOverride() ? doc.Root.FirstElement() : doc.Root;
            HasGenders = CharacterConfigElement.GetAttributeBool("genders", false);
            if (HasGenders && gender == Gender.None)
            {
                gender = GetRandomGender();
            }
            else if (!HasGenders)
            {
                gender = Gender.None;
            }
            RecreateHead(
                infoElement.GetAttributeInt("headspriteid", 1),
                race,
                gender,
                infoElement.GetAttributeInt("hairindex", -1),
                infoElement.GetAttributeInt("beardindex", -1),
                infoElement.GetAttributeInt("moustacheindex", -1),
                infoElement.GetAttributeInt("faceattachmentindex", -1));

            if (string.IsNullOrEmpty(Name))
            {
                if (CharacterConfigElement.Element("name") != null)
                {
                    string firstNamePath = CharacterConfigElement.Element("name").GetAttributeString("firstname", "");
                    if (firstNamePath != "")
                    {
                        firstNamePath = firstNamePath.Replace("[GENDER]", (Head.gender == Gender.Female) ? "female" : "male");
                        Name = ToolBox.GetRandomLine(firstNamePath);
                    }

                    string lastNamePath = CharacterConfigElement.Element("name").GetAttributeString("lastname", "");
                    if (lastNamePath != "")
                    {
                        lastNamePath = lastNamePath.Replace("[GENDER]", (Head.gender == Gender.Female) ? "female" : "male");
                        if (Name != "") Name += " ";
                        Name += ToolBox.GetRandomLine(lastNamePath);
                    }
                }
            }

            StartItemsGiven = infoElement.GetAttributeBool("startitemsgiven", false);
            string personalityName = infoElement.GetAttributeString("personality", "");
            ragdollFileName = infoElement.GetAttributeString("ragdoll", string.Empty);
            if (!string.IsNullOrEmpty(personalityName))
            {
                personalityTrait = NPCPersonalityTrait.List.Find(p => p.Name == personalityName);
            }      
            foreach (XElement subElement in infoElement.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() != "job") continue;
                Job = new Job(subElement);
                break;
            }
            LoadHeadAttachments();
        }

        public int SetRandomHead() => HeadSpriteId = GetRandomHeadID();

        public Gender GetRandomGender() => (Rand.Range(0.0f, 1.0f, Rand.RandSync.Server) < CharacterConfigElement.GetAttributeFloat("femaleratio", 0.5f)) ? Gender.Female : Gender.Male;
        public Race GetRandomRace() => new Race[] { Race.White, Race.Black, Race.Asian }.GetRandom(Rand.RandSync.Server);
        public int GetRandomHeadID() => Head.headSpriteRange != Vector2.Zero ? Rand.Range((int)Head.headSpriteRange.X, (int)Head.headSpriteRange.Y + 1, Rand.RandSync.Server) : 0;

        private List<XElement> hairs;
        private List<XElement> beards;
        private List<XElement> moustaches;
        private List<XElement> faceAttachments;

        private IEnumerable<XElement> wearables;
        public IEnumerable<XElement> Wearables
        {
            get
            {
                if (wearables == null)
                {
                    var attachments = CharacterConfigElement.Element("HeadAttachments");
                    if (attachments != null)
                    {
                        wearables = attachments.Elements("Wearable");
                    }
                }
                return wearables;
            }
        }

        public IEnumerable<XElement> FilterByTypeAndHeadID(IEnumerable<XElement> elements, WearableType targetType)
        {
            if (elements == null) { return elements; }
            return elements.Where(e =>
            {
                if (Enum.TryParse(e.GetAttributeString("type", ""), true, out WearableType type) && type != targetType) { return false; }
                int headId = e.GetAttributeInt("headid", -1);
                // if the head id is less than 1, the id is not valid and the condition is ignored.
                return headId < 1 || headId == Head.HeadSpriteId;
            });
        }

        public IEnumerable<XElement> FilterElementsByGenderAndRace(IEnumerable<XElement> elements)
        {
            if (elements == null) { return elements; }
            return elements.Where(w =>
                Enum.TryParse(w.GetAttributeString("gender", "None"), true, out Gender g) && g == Head.gender &&
                Enum.TryParse(w.GetAttributeString("race", "None"), true, out Race r) && r == Head.race);
        }

        private void LoadHeadPresets()
        {
            if (CharacterConfigElement == null) { return; }
            heads = new Dictionary<HeadPreset, Vector2>();
            var headsElement = CharacterConfigElement.GetChildElement("heads");
            if (headsElement != null)
            {
                foreach (var head in headsElement.GetChildElements("head"))
                {
                    var preset = new HeadPreset(head);
                    heads.Add(preset, preset.SheetIndex);
                }
            }
        }

        private void CalculateHeadSpriteRange()
        {
            if (CharacterConfigElement == null) { return; }
            Head.headSpriteRange = CharacterConfigElement.GetAttributeVector2("headidrange", Vector2.Zero);
            // If the range is defined, we use it as it is
            if (Head.headSpriteRange != Vector2.Zero) { return; }
            if (heads == null)
            {
                LoadHeadPresets();
            }
            // If there are any head presets defined, use them.
            if (heads.Any())
            {
                var ids = heads.Keys.Where(h => h.Race == Race && h.Gender == Gender).Select(w => w.ID);
                ids = ids.OrderBy(id => id);
                Head.headSpriteRange = new Vector2(ids.First(), ids.Last());
            }
            // Else we calculate the range from the wearables.
            if (Head.headSpriteRange == Vector2.Zero)
            {
                var wearableElements = Wearables;
                if (wearableElements == null) { return; }
                var wearables = FilterElementsByGenderAndRace(wearableElements).ToList();
                if (wearables == null)
                {
                    Head.headSpriteRange = Vector2.Zero;
                    return;
                }
                if (wearables.None())
                {
                    DebugConsole.ThrowError($"[CharacterInfo] No headidrange defined and no wearables matching the gender {Head.gender} and the race {Head.race} could be found. Total wearables found: {Wearables.Count()}.");
                    return;
                }
                else
                {
                    // Ignore head ids that are less than 1, because they are not supported.
                    var ids = wearables.Select(w => w.GetAttributeInt("headid", -1)).Where(id => id > 0);
                    if (ids.None())
                    {
                        DebugConsole.ThrowError($"[CharacterInfo] Wearables with matching gender and race were found but none with a valid headid! Total wearables found: {Wearables.Count()}.");
                        return;
                    }
                    ids = ids.OrderBy(id => id);
                    Head.headSpriteRange = new Vector2(ids.First(), ids.Last());
                }
            }
        }

        public void RecreateHead(int headID, Race race, Gender gender, int hairIndex, int beardIndex, int moustacheIndex, int faceAttachmentIndex)
        {
            if (HasGenders && gender == Gender.None)
            {
                gender = GetRandomGender();
            }
            else if (!HasGenders)
            {
                gender = Gender.None;
            }
            head = new HeadInfo(headID, gender, race, hairIndex, beardIndex, moustacheIndex, faceAttachmentIndex);
            CalculateHeadSpriteRange();
            ReloadHeadAttachments();
        }

        public void LoadHeadSprite()
        {
            foreach (XElement limbElement in Ragdoll.MainElement.Elements())
            {
                if (limbElement.GetAttributeString("type", "").ToLowerInvariant() != "head") { continue; }

                XElement spriteElement = limbElement.Element("sprite");
                if (spriteElement == null) { continue; }

                string spritePath = spriteElement.Attribute("texture").Value;

                spritePath = spritePath.Replace("[GENDER]", (Head.gender == Gender.Female) ? "female" : "male");
                spritePath = spritePath.Replace("[RACE]", Head.race.ToString().ToLowerInvariant());
                spritePath = spritePath.Replace("[HEADID]", HeadSpriteId.ToString());

                string fileName = Path.GetFileNameWithoutExtension(spritePath);

                //go through the files in the directory to find a matching sprite
                foreach (string file in Directory.GetFiles(Path.GetDirectoryName(spritePath)))
                {
                    if (!file.EndsWith(".png", StringComparison.InvariantCultureIgnoreCase))
                    {
                        continue;
                    }
                    string fileWithoutTags = Path.GetFileNameWithoutExtension(file);
                    fileWithoutTags = fileWithoutTags.Split('[', ']').First();
                    if (fileWithoutTags != fileName) { continue; }

                    HeadSprite = new Sprite(spriteElement, "", file);
                    Portrait = new Sprite(spriteElement, "", file) { RelativeOrigin = Vector2.Zero };

                    //extract the tags out of the filename
                    SpriteTags = file.Split('[', ']').Skip(1).ToList();
                    if (SpriteTags.Any())
                    {
                        SpriteTags.RemoveAt(SpriteTags.Count - 1);
                    }

                    break;
                }

                break;
            }
        }

        /// <summary>
        /// Loads only the elements according to the indices, not the sprites.
        /// </summary>
        public void LoadHeadAttachments()
        {
            if (Wearables != null)
            {
                if (hairs == null)
                {
                    float commonness = Gender == Gender.Female ? 0.05f : 0.2f;
                    hairs = AddEmpty(FilterByTypeAndHeadID(FilterElementsByGenderAndRace(wearables), WearableType.Hair), WearableType.Hair, commonness);
                }
                if (beards == null)
                {
                    beards = AddEmpty(FilterByTypeAndHeadID(FilterElementsByGenderAndRace(wearables), WearableType.Beard), WearableType.Beard);
                }
                if (moustaches == null)
                {
                    moustaches = AddEmpty(FilterByTypeAndHeadID(FilterElementsByGenderAndRace(wearables), WearableType.Moustache), WearableType.Moustache);
                }
                if (faceAttachments == null)
                {
                    faceAttachments = AddEmpty(FilterByTypeAndHeadID(FilterElementsByGenderAndRace(wearables), WearableType.FaceAttachment), WearableType.FaceAttachment);
                }

                if (IsValidIndex(Head.HairIndex, hairs))
                {
                    Head.HairElement = hairs[Head.HairIndex];
                }
                else
                {
                    Head.HairElement = GetRandomElement(hairs);
                    Head.HairIndex = hairs.IndexOf(Head.HairElement);
                }
                if (IsValidIndex(Head.BeardIndex, beards))
                {
                    Head.BeardElement = beards[Head.BeardIndex];
                }
                else
                {
                    Head.BeardElement = GetRandomElement(beards);
                    Head.BeardIndex = beards.IndexOf(Head.BeardElement);
                }
                if (IsValidIndex(Head.MoustacheIndex, moustaches))
                {
                    Head.MoustacheElement = moustaches[Head.MoustacheIndex];
                }
                else
                {
                    Head.MoustacheElement = GetRandomElement(moustaches);
                    Head.MoustacheIndex = moustaches.IndexOf(Head.MoustacheElement);
                }
                if (IsValidIndex(Head.FaceAttachmentIndex, faceAttachments))
                {
                    Head.FaceAttachment = faceAttachments[Head.FaceAttachmentIndex];
                }
                else
                {
                    Head.FaceAttachment = GetRandomElement(faceAttachments);
                    Head.FaceAttachmentIndex = faceAttachments.IndexOf(Head.FaceAttachment);
                }

                List<XElement> AddEmpty(IEnumerable<XElement> elements, WearableType type, float commonness = 1)
                {
                    // Let's add an empty element so that there's a chance that we don't get any actual element -> allows bald and beardless guys, for example.
                    var emptyElement = new XElement("EmptyWearable", type.ToString(), new XAttribute("commonness", commonness));
                    var list = new List<XElement>() { emptyElement };
                    list.AddRange(elements);
                    return list;
                }

                XElement GetRandomElement(IEnumerable<XElement> elements)
                {
                    var filtered = elements.Where(e => IsWearableAllowed(e)).ToList();
                    if (filtered.Count == 0) { return null; }
                    var weights = GetWeights(filtered).ToList();
                    var element = ToolBox.SelectWeightedRandom(filtered, weights, Rand.RandSync.Server);
                    return element == null || element.Name == "Empty" ? null : element;
                }

                bool IsWearableAllowed(XElement element)
                {
                    string spriteName = element.Element("sprite").GetAttributeString("name", string.Empty);
                    return IsAllowed(Head.HairElement, spriteName) && IsAllowed(Head.BeardElement, spriteName) && IsAllowed(Head.MoustacheElement, spriteName) && IsAllowed(Head.FaceAttachment, spriteName);
                }

                bool IsAllowed(XElement element, string spriteName)
                {
                    if (element != null)
                    {
                        var disallowed = element.GetAttributeStringArray("disallow", new string[0]);
                        if (disallowed.Any(s => spriteName.Contains(s)))
                        {
                            return false;
                        }
                    }
                    return true;
                }

                bool IsValidIndex(int index, List<XElement> list) => index >= 0 && index < list.Count;
                IEnumerable<float> GetWeights(IEnumerable<XElement> elements) => elements.Select(h => h.GetAttributeFloat("commonness", 1f));
            }
        }

        partial void LoadAttachmentSprites(bool omitJob);
        
        // TODO: change the formula so that it's not linear and so that it takes into account the usefulness of the skill 
        // -> give a weight to each skill, because some are much more valuable than others?
        private int CalculateSalary()
        {
            if (Name == null || Job == null) return 0;

            int salary = Math.Abs(Name.GetHashCode()) % 100;

            foreach (Skill skill in Job.Skills)
            {
                salary += (int)skill.Level * 50;
            }

            return salary;
        }

        public void IncreaseSkillLevel(string skillIdentifier, float increase, Vector2 worldPos)
        {
            if (Job == null || (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) || Character == null) { return; }         

            float prevLevel = Job.GetSkillLevel(skillIdentifier);
            Job.IncreaseSkillLevel(skillIdentifier, increase);

            float newLevel = Job.GetSkillLevel(skillIdentifier);

            OnSkillChanged(skillIdentifier, prevLevel, newLevel, worldPos);

            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer && (int)newLevel != (int)prevLevel)
            {
                GameMain.NetworkMember.CreateEntityEvent(Character, new object[] { NetEntityEvent.Type.UpdateSkills });                
            }
        }

        public void SetSkillLevel(string skillIdentifier, float level, Vector2 worldPos)
        {
            if (Job == null) { return; }

            var skill = Job.Skills.Find(s => s.Identifier == skillIdentifier);
            if (skill == null)
            {
                Job.Skills.Add(new Skill(skillIdentifier, level));
                OnSkillChanged(skillIdentifier, 0.0f, level, worldPos);
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
                new XAttribute("speciesname", SpeciesName),
                new XAttribute("gender", Head.gender == Gender.Male ? "male" : "female"),
                new XAttribute("race", Head.race.ToString()),
                new XAttribute("salary", Salary),
                new XAttribute("headspriteid", HeadSpriteId),
                new XAttribute("hairindex", HairIndex),
                new XAttribute("beardindex", BeardIndex),
                new XAttribute("moustacheindex", MoustacheIndex),
                new XAttribute("faceattachmentindex", FaceAttachmentIndex),
                new XAttribute("startitemsgiven", StartItemsGiven),
                new XAttribute("ragdoll", ragdollFileName),
                new XAttribute("personality", personalityTrait == null ? "" : personalityTrait.Name));
            
            // TODO: animations?

            if (Character != null)
            {
                if (Character.AnimController.CurrentHull != null)
                {
                    charElement.Add(new XAttribute("hull", Character.AnimController.CurrentHull.ID));
                }
            }
            
            Job.Save(charElement);

            parentElement.Add(charElement);
            return charElement;
        }

        public void SpawnInventoryItems(Inventory inventory, XElement itemData)
        {
            SpawnInventoryItemsRecursive(inventory, itemData);
        }

        private void SpawnInventoryItemsRecursive(Inventory inventory, XElement element)
        {
            foreach (XElement itemElement in element.Elements())
            {
                var newItem = Item.Load(itemElement, inventory.Owner.Submarine, createNetworkEvent: true);
                if (newItem == null) { continue; }

                if (!MathUtils.NearlyEqual(newItem.Condition, newItem.MaxCondition) &&
                    GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
                {
                    GameMain.NetworkMember.CreateEntityEvent(newItem, new object[] { NetEntityEvent.Type.Status });
                }

                int[] slotIndices = itemElement.GetAttributeIntArray("i", new int[] { 0 });
                if (!slotIndices.Any())
                {
                    DebugConsole.ThrowError("Invalid inventory data in character \"" + Name + "\" - no slot indices found");
                    continue;
                }

                //make sure there's no other item in the slot
                //this should not happen normally, but can occur if the character is accidentally given new job items while also loading previous items in the campaign
                for (int i = 0; i < inventory.Capacity; i++)
                {
                    if (slotIndices.Contains(i) && inventory.Items[i] != null && inventory.Items[i] != newItem)
                    {
                        DebugConsole.ThrowError($"Error while loading character inventory data. The slot {i} was already occupied by the item \"{inventory.Items[i].Name} ({inventory.Items[i].ID})\" when loading the item \"{newItem.Name} ({newItem.ID})\"");
                        inventory.Items[i].Drop(null, createNetworkEvent: false);
                    }
                }

                inventory.TryPutItem(newItem, slotIndices[0], false, false, null);
                newItem.ParentInventory = inventory;

                //force the item to the correct slots
                //  e.g. putting the item in a hand slot will also put it in the first available Any-slot, 
                //  which may not be where it actually was
                for (int i = 0; i < inventory.Capacity; i++)
                {
                    if (slotIndices.Contains(i))
                    {
                        inventory.Items[i] = newItem;
                    }
                    else if (inventory.Items[i] == newItem)
                    {
                        inventory.Items[i] = null;
                    }
                }

                int itemContainerIndex = 0;
                var itemContainers = newItem.GetComponents<ItemContainer>().ToList();
                foreach (XElement childInvElement in itemElement.Elements())
                {
                    if (itemContainerIndex >= itemContainers.Count) break;
                    if (childInvElement.Name.ToString().ToLowerInvariant() != "inventory") continue;
                    SpawnInventoryItemsRecursive(itemContainers[itemContainerIndex].Inventory, childInvElement);
                    itemContainerIndex++;
                }
            }
        }
        
        public void ReloadHeadAttachments()
        {
            ResetLoadedAttachments();
            LoadHeadAttachments();
        }

        public void ResetHeadAttachments()
        {
            ResetAttachmentIndices();
            ResetLoadedAttachments();
        }

        private void ResetAttachmentIndices()
        {
            Head.ResetAttachmentIndices();
        }

        private void ResetLoadedAttachments()
        {
            hairs = null;
            beards = null;
            moustaches = null;
            faceAttachments = null;
        }

        public void Remove()
        {
            Character = null;
            HeadSprite = null;
            Portrait = null;
            AttachmentSprites = null;
        }
    }
}
