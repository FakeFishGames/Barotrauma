using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Barotrauma.IO;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Abilities;

namespace Barotrauma
{
    class CharacterInfoPrefab
    {
        public readonly ImmutableArray<CharacterInfo.HeadPreset> Heads;
        public readonly ImmutableDictionary<Identifier, ImmutableHashSet<Identifier>> VarTags;
        public readonly Identifier MenuCategoryVar;
        public readonly Identifier Pronouns;

        public CharacterInfoPrefab(ContentXElement headsElement, XElement varsElement, XElement menuCategoryElement, XElement pronounsElement)
        {
            Heads = headsElement.Elements().Select(e => new CharacterInfo.HeadPreset(this, e)).ToImmutableArray();
            if (varsElement != null)
            {
                VarTags = varsElement.Elements()
                    .Select(e =>
                        (e.GetAttributeIdentifier("var", ""),
                            e.GetAttributeIdentifierArray("tags", Array.Empty<Identifier>()).ToImmutableHashSet()))
                    .ToImmutableDictionary();
            }
            else
            {
                VarTags = new[]
                {
                    ("GENDER".ToIdentifier(),
                        new[] { "female".ToIdentifier(), "male".ToIdentifier() }.ToImmutableHashSet())
                }.ToImmutableDictionary();
            }

            MenuCategoryVar = menuCategoryElement?.GetAttributeIdentifier("var", Identifier.Empty) ?? "GENDER".ToIdentifier();
            Pronouns = pronounsElement?.GetAttributeIdentifier("vars", Identifier.Empty) ?? "GENDER".ToIdentifier();
        }
        public string ReplaceVars(string str, CharacterInfo.HeadPreset headPreset)
        {
            return ReplaceVars(str, headPreset.TagSet);
        }

        public string ReplaceVars(string str, ImmutableHashSet<Identifier> tagSet)
        {
            foreach (var key in VarTags.Keys)
            {
                str = str.Replace($"[{key}]", tagSet.FirstOrDefault(t => VarTags[key].Contains(t)).Value, StringComparison.OrdinalIgnoreCase);
            }
            return str;
        }
    }

    partial class CharacterInfo
    {
        public class HeadInfo
        {
            public readonly CharacterInfo CharacterInfo;
            public readonly HeadPreset Preset;

            public int HairIndex { get; set; }

            private int? hairWithHatIndex;

            public void SetHairWithHatIndex()
            {
                if (CharacterInfo.Hairs is null)
                {
                    if (HairIndex == -1)
                    {
#if DEBUG
                        DebugConsole.ThrowError("Setting \"hairWithHatIndex\" before \"Hairs\" are defined!");
#else
                        DebugConsole.AddWarning("Setting \"hairWithHatIndex\" before \"Hairs\" are defined!");
#endif
                    }
                    hairWithHatIndex = HairIndex;
                }
                else
                {
                    hairWithHatIndex = HairElement?.GetAttributeInt("replacewhenwearinghat", HairIndex) ?? -1;
                    if (hairWithHatIndex < 0 || hairWithHatIndex >= CharacterInfo.Hairs.Count)
                    {
                        hairWithHatIndex = HairIndex;
                    }
                }
            }

            public int BeardIndex;
            public int MoustacheIndex;
            public int FaceAttachmentIndex;

            public Color HairColor;
            public Color FacialHairColor;
            public Color SkinColor;

            public Vector2 SheetIndex => Preset.SheetIndex;

            public ContentXElement HairElement
            {
                get
                {
                    if (CharacterInfo.Hairs == null) { return null; }
                    if (HairIndex >= CharacterInfo.Hairs.Count)
                    {
                        DebugConsole.AddWarning($"Hair index out of range (character: {CharacterInfo?.Name ?? "null"}, index: {HairIndex})");
                    }
                    return CharacterInfo.Hairs.ElementAtOrDefault(HairIndex);
                }
            }
            public ContentXElement HairWithHatElement
            {
                get
                {
                    if (hairWithHatIndex == null)
                    {
                        SetHairWithHatIndex();
                    }
                    if (CharacterInfo.Hairs == null) { return null; }
                    if (hairWithHatIndex >= CharacterInfo.Hairs.Count)
                    {
                        DebugConsole.AddWarning($"Hair with hat index out of range (character: {CharacterInfo?.Name ?? "null"}, index: {hairWithHatIndex})");
                    }
                    return CharacterInfo.Hairs.ElementAtOrDefault(hairWithHatIndex.Value);
                }
            }            
            public ContentXElement BeardElement
            {
                get
                {
                    if (CharacterInfo.Beards == null) { return null; }
                    if (BeardIndex >= CharacterInfo.Beards.Count)
                    {
                        DebugConsole.AddWarning($"Beard index out of range (character: {CharacterInfo?.Name ?? "null"}, index: {BeardIndex})");
                    }
                    return CharacterInfo.Beards.ElementAtOrDefault(BeardIndex);
                }
            }
            public ContentXElement MoustacheElement
            {
                get
                {
                    if (CharacterInfo.Moustaches == null) { return null; }
                    if (MoustacheIndex >= CharacterInfo.Moustaches.Count)
                    {
                        DebugConsole.AddWarning($"Moustache index out of range (character: {CharacterInfo?.Name ?? "null"}, index: {MoustacheIndex})");
                    }
                    return CharacterInfo.Moustaches.ElementAtOrDefault(MoustacheIndex);
                }
            }
            public ContentXElement FaceAttachment
            {
                get
                {
                    if (CharacterInfo.FaceAttachments == null) { return null; }
                    if (FaceAttachmentIndex >= CharacterInfo.FaceAttachments.Count)
                    {
                        DebugConsole.AddWarning($"Face attachment index out of range (character: {CharacterInfo?.Name ?? "null"}, index: {FaceAttachmentIndex})");
                    }
                    return CharacterInfo.FaceAttachments.ElementAtOrDefault(FaceAttachmentIndex);
                }
            }

            public HeadInfo(CharacterInfo characterInfo, HeadPreset headPreset, int hairIndex = 0, int beardIndex = 0, int moustacheIndex = 0, int faceAttachmentIndex = 0)
            {
                CharacterInfo = characterInfo;
                Preset = headPreset;
                HairIndex = hairIndex;
                BeardIndex = beardIndex;
                MoustacheIndex = moustacheIndex;
                FaceAttachmentIndex = faceAttachmentIndex;
            }

            public void ResetAttachmentIndices()
            {
                HairIndex = -1;
                BeardIndex = -1;
                MoustacheIndex = -1;
                FaceAttachmentIndex = -1;
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
                    HeadSprite = null;
                    AttachmentSprites = null;
                    hairs = null;
                    beards = null;
                    moustaches = null;
                    faceAttachments = null;
                }
            }
        }

        private readonly Identifier maleIdentifier = "Male".ToIdentifier();
        private readonly Identifier femaleIdentifier = "Female".ToIdentifier();

        public bool IsMale { get { return head?.Preset?.TagSet?.Contains(maleIdentifier) ?? false; } }
        public bool IsFemale { get { return head?.Preset?.TagSet?.Contains(femaleIdentifier) ?? false; } }

        public CharacterInfoPrefab Prefab => CharacterPrefab.Prefabs[SpeciesName].CharacterInfoPrefab;
        public class HeadPreset : ISerializableEntity
        {
            private readonly CharacterInfoPrefab characterInfoPrefab;
            public Identifier MenuCategory => TagSet.First(t => characterInfoPrefab.VarTags[characterInfoPrefab.MenuCategoryVar].Contains(t));

            public ImmutableHashSet<Identifier> TagSet { get; private set; }

            [Serialize("", IsPropertySaveable.No)]
            public string Tags
            {
                get { return string.Join(",", TagSet); }
                private set
                {
                    TagSet = value.Split(",")
                        .Select(s => s.ToIdentifier())
                        .Where(id => !id.IsEmpty)
                        .ToImmutableHashSet();
                }
            }

            [Serialize("0,0", IsPropertySaveable.No)]
            public Vector2 SheetIndex { get; private set; }

            public string Name => $"Head Preset {Tags}";

            public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; private set; }

            public HeadPreset(CharacterInfoPrefab charInfoPrefab, XElement element)
            {
                characterInfoPrefab = charInfoPrefab;
                SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
                DetermineTagsFromLegacyFormat(element);
            }

            private void DetermineTagsFromLegacyFormat(XElement element)
            {
                void addTag(string tag)
                    => TagSet = TagSet.Add(tag.ToIdentifier());
                
                string headId = element.GetAttributeString("id", "");
                string gender = element.GetAttributeString("gender", "");
                string race = element.GetAttributeString("race", "");
                if (!headId.IsNullOrEmpty()) { addTag($"head{headId}"); }
                if (!gender.IsNullOrEmpty()) { addTag(gender); }
                if (!race.IsNullOrEmpty()) { addTag(race); }
            }
        }

        public XElement InventoryData;
        public XElement HealthData;
        public XElement OrderData;

        private static ushort idCounter;
        private const string disguiseName = "???";

        public bool HasNickname => Name != OriginalName;
        public string OriginalName { get; private set; }

        public string Name;

        public LocalizedString Title;

        public (Identifier NpcSetIdentifier, Identifier NpcIdentifier) HumanPrefabIds;

        public string DisplayName
        {
            get
            {
                if (Character == null || !Character.HideFace)
                {
                    IsDisguised = IsDisguisedAsAnother = false;
                    return Name;
                }
                else if ((GameMain.NetworkMember != null && !GameMain.NetworkMember.ServerSettings.AllowDisguises))
                {
                    IsDisguised = IsDisguisedAsAnother = false;
                    return Name;
                }

                if (Character.Inventory != null)
                {
                    //Disguise as the ID card name if it's equipped      
                    var idCard = Character.Inventory.GetItemInLimbSlot(InvSlotType.Card);
                    return idCard?.GetComponent<IdCard>()?.OwnerName ?? disguiseName;
                }
                return disguiseName;
            }
        }

        public Identifier SpeciesName { get; }

        /// <summary>
        /// Note: Can be null.
        /// </summary>
        public Character Character;
        
        public Job Job;
        
        public int Salary;

        public int ExperiencePoints { get; private set; }

        public HashSet<Identifier> UnlockedTalents { get; private set; } = new HashSet<Identifier>();

        public (Identifier factionId, float reputation) MinReputationToHire;

        /// <summary>
        /// Endocrine boosters can unlock talents outside the user's talent tree. This method is used to cull them from the selection
        /// </summary>
        public IEnumerable<Identifier> GetUnlockedTalentsInTree()
        {
            if (!TalentTree.JobTalentTrees.TryGet(Job.Prefab.Identifier, out TalentTree talentTree)) { return Enumerable.Empty<Identifier>(); }

            return UnlockedTalents.Where(t => talentTree.TalentIsInTree(t));
        }

        /// <summary>
        /// Returns unlocked talents that aren't part of the character's talent tree (which can be unlocked e.g. with an endocrine booster)
        /// </summary>
        public IEnumerable<Identifier> GetUnlockedTalentsOutsideTree()
        {
            if (!TalentTree.JobTalentTrees.TryGet(Job.Prefab.Identifier, out TalentTree talentTree)) { return Enumerable.Empty<Identifier>(); }
            return UnlockedTalents.Where(t => !talentTree.TalentIsInTree(t));
        }

        public const int MaxAdditionalTalentPoints = 100;

        private int additionalTalentPoints;
        public int AdditionalTalentPoints 
        {
            get { return additionalTalentPoints; }
            set { additionalTalentPoints = MathHelper.Clamp(value, 0, MaxAdditionalTalentPoints); }
        }

        private Sprite _headSprite;
        public Sprite HeadSprite
        {
            get
            {
                if (_headSprite == null)
                {
                    LoadHeadSprite();
                }
#if CLIENT
                if (_headSprite != null)
                {
                    CalculateHeadPosition(_headSprite);
                }
#endif
                return _headSprite;
            }
            private set
            {
                if (_headSprite != null)
                {
                    _headSprite.Remove();
                }
                _headSprite = value;
            }
        }

        /// <summary>
        /// Can be used to disable displaying the job in any info panels
        /// </summary>
        public bool OmitJobInMenus;

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

        public bool IsDisguised = false;
        public bool IsDisguisedAsAnother = false;

        public void CheckDisguiseStatus(bool handleBuff, IdCard idCard = null)
        {
            if (Character == null) { return; }

            string currentlyDisplayedName = DisplayName;

            IsDisguised = currentlyDisplayedName == disguiseName;
            IsDisguisedAsAnother = !IsDisguised && currentlyDisplayedName != Name;

            if (IsDisguisedAsAnother)
            {
                if (handleBuff)
                {
                    var head = Character.AnimController.GetLimb(LimbType.Head);
                    if (head != null)
                    {
                        Character.CharacterHealth.ApplyAffliction(head, AfflictionPrefab.List.FirstOrDefault(a => a.Identifier == "disguised").Instantiate(100f));
                    }
                }

                idCard ??= Character.Inventory?.GetItemInLimbSlot(InvSlotType.Card)?.GetComponent<IdCard>();
                if (idCard != null)
                {
#if CLIENT
                    GetDisguisedSprites(idCard);
#endif
                    return;
                }
            }

#if CLIENT
            disguisedJobIcon = null;
            disguisedPortrait = null;
#endif

            if (handleBuff)
            {
                var head = Character.AnimController.GetLimb(LimbType.Head);
                if (head != null)
                {
                    Character.CharacterHealth.ReduceAfflictionOnLimb(head, "disguised".ToIdentifier(), 100f);
                }
            }
        }

        private List<WearableSprite> attachmentSprites;
        public List<WearableSprite> AttachmentSprites
        {
            get
            {
                if (attachmentSprites == null)
                {
                    LoadAttachmentSprites();
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

        public ContentXElement CharacterConfigElement { get; set; }

        public readonly string ragdollFileName = string.Empty;

        public bool StartItemsGiven;

        public bool IsNewHire;

        public CauseOfDeath CauseOfDeath;

        public CharacterTeamType TeamID;

        public NPCPersonalityTrait PersonalityTrait { get; private set; }

        public const int MaxCurrentOrders = 3;
        public static int HighestManualOrderPriority => MaxCurrentOrders;

        public int GetManualOrderPriority(Order order)
        {
            if (order != null && order.AssignmentPriority < 100 && CurrentOrders.Any())
            {
                int orderPriority = HighestManualOrderPriority;
                for (int i = 0; i < CurrentOrders.Count; i++)
                {
                    if (order.AssignmentPriority >= CurrentOrders[i].AssignmentPriority)
                    {
                        break;
                    }
                    else
                    {
                        orderPriority--;
                    }
                }
                return Math.Max(orderPriority, 1);
            }
            else
            {
                return HighestManualOrderPriority;
            }
        }

        public List<Order> CurrentOrders { get; } = new List<Order>();


        /// <summary>
        /// Unique ID given to character infos in MP. Non-persistent.
        /// Used by clients to identify which infos are the same to prevent duplicate characters in round summary.
        /// </summary>
        public ushort ID;

        public List<Identifier> SpriteTags
        {
            get;
            private set;
        }

        public readonly bool HasSpecifierTags;

        private RagdollParams ragdoll;
        public RagdollParams Ragdoll
        {
            get
            {
                if (ragdoll == null)
                {
                    // TODO: support for variants
                    Identifier speciesName = SpeciesName;
                    bool isHumanoid = CharacterConfigElement.GetAttributeBool("humanoid", speciesName == CharacterPrefab.HumanSpeciesName);
                    ragdoll = isHumanoid 
                        ? HumanRagdollParams.GetRagdollParams(speciesName, ragdollFileName)
                        : RagdollParams.GetRagdollParams<FishRagdollParams>(speciesName, ragdollFileName) as RagdollParams;
                }
                return ragdoll;
            }
            set { ragdoll = value; }
        }

        public bool IsAttachmentsLoaded => Head.HairIndex > -1 && Head.BeardIndex > -1 && Head.MoustacheIndex > -1 && Head.FaceAttachmentIndex > -1;

        public IEnumerable<ContentXElement> GetValidAttachmentElements(IEnumerable<ContentXElement> elements, HeadPreset headPreset, WearableType? wearableType = null)
            => FilterElements(elements, headPreset.TagSet, wearableType);
        
        public int CountValidAttachmentsOfType(WearableType wearableType)
            => GetValidAttachmentElements(Wearables, Head.Preset, wearableType).Count();

        public readonly ImmutableArray<(Color Color, float Commonness)> HairColors;
        public readonly ImmutableArray<(Color Color, float Commonness)> FacialHairColors;
        public readonly ImmutableArray<(Color Color, float Commonness)> SkinColors;
        
        private void GetName(Rand.RandSync randSync, out string name)
        {
            ContentXElement nameElement = CharacterConfigElement.GetChildElement("names") ?? CharacterConfigElement.GetChildElement("name");
            ContentPath namesXmlFile = nameElement?.GetAttributeContentPath("path") ?? ContentPath.Empty;
            XElement namesXml = null;
            if (!namesXmlFile.IsNullOrEmpty()) //names.xml is defined 
            {
                XDocument doc = XMLExtensions.TryLoadXml(namesXmlFile);
                namesXml = doc.Root;
            }
            else //the legacy firstnames.txt/lastnames.txt shit is defined
            {
                namesXml = new XElement("names", new XAttribute("format", "[firstname] [lastname]"));
                string firstNamesPath = nameElement == null ? string.Empty : ReplaceVars(nameElement.GetAttributeContentPath("firstname")?.Value ?? "");
                string lastNamesPath = nameElement == null ? string.Empty : ReplaceVars(nameElement.GetAttributeContentPath("lastname")?.Value ?? "");
                if (File.Exists(firstNamesPath) && File.Exists(lastNamesPath))
                {
                    var firstNames = File.ReadAllLines(firstNamesPath);
                    var lastNames = File.ReadAllLines(lastNamesPath);
                    namesXml.Add(firstNames.Select(n => new XElement("firstname", new XAttribute("value", n))));
                    namesXml.Add(lastNames.Select(n => new XElement("lastname", new XAttribute("value", n))));
                }
                else //the files don't exist, just fall back to the vanilla names
                {
                    XDocument doc = XMLExtensions.TryLoadXml("Content/Characters/Human/names.xml");
                    namesXml = doc.Root;
                }
            }
            name = namesXml.GetAttributeString("format", "");
            Dictionary<Identifier, List<string>> entries = new Dictionary<Identifier, List<string>>();
            foreach (var subElement in namesXml.Elements())
            {
                Identifier elemName = subElement.NameAsIdentifier();
                if (!entries.ContainsKey(elemName))
                {
                    entries.Add(elemName, new List<string>());
                }
                ImmutableHashSet<Identifier> identifiers = subElement.GetAttributeIdentifierArray("tags", Array.Empty<Identifier>()).ToImmutableHashSet();
                if (identifiers.IsSubsetOf(Head.Preset.TagSet))
                {
                    entries[elemName].Add(subElement.GetAttributeString("value", ""));
                }
            }

            foreach (var k in entries.Keys)
            {
                name = name.Replace($"[{k}]", entries[k].GetRandom(randSync), StringComparison.OrdinalIgnoreCase);
            }
        }

        private static void LoadTagsBackwardsCompatibility(XElement element, HashSet<Identifier> tags)
        {
            //we need this to be able to load save files from
            //older versions with the shittier hardcoded character
            //info implementation
            Identifier gender = element.GetAttributeIdentifier("gender", "");
            int headSpriteId = element.GetAttributeInt("headspriteid", -1);
            if (!gender.IsEmpty) { tags.Add(gender); }
            if (headSpriteId > 0) { tags.Add($"head{headSpriteId}".ToIdentifier()); }
        }

        // talent-relevant values
        public int MissionsCompletedSinceDeath = 0;

        private static bool ElementHasSpecifierTags(XElement element)
            => element.GetAttributeBool("specifiertags",
                element.GetAttributeBool("genders",
                    element.GetAttributeBool("races", false)));
        
        // Used for creating the data
        public CharacterInfo(
            Identifier speciesName,
            string name = "",
            string originalName = "",
            Either<Job, JobPrefab> jobOrJobPrefab = null,
            string ragdollFileName = null,
            int variant = 0,
            Rand.RandSync randSync = Rand.RandSync.Unsynced,
            Identifier npcIdentifier = default)
        {
            JobPrefab jobPrefab = null;
            Job job = null;
            if (jobOrJobPrefab != null)
            {
                jobOrJobPrefab.TryGet(out job);
                jobOrJobPrefab.TryGet(out jobPrefab);
            }
            ID = idCounter;
            idCounter++;
            SpeciesName = speciesName;
            SpriteTags = new List<Identifier>();
            CharacterConfigElement = CharacterPrefab.FindBySpeciesName(SpeciesName)?.ConfigElement;
            if (CharacterConfigElement == null) { return; }
            // TODO: support for variants
            HasSpecifierTags = ElementHasSpecifierTags(CharacterConfigElement);
            if (HasSpecifierTags)
            {
                HairColors = CharacterConfigElement.GetAttributeTupleArray("haircolors", new (Color, float)[] { (Color.WhiteSmoke, 100f) }).ToImmutableArray();
                FacialHairColors = CharacterConfigElement.GetAttributeTupleArray("facialhaircolors", new (Color, float)[] { (Color.WhiteSmoke, 100f) }).ToImmutableArray();
                SkinColors = CharacterConfigElement.GetAttributeTupleArray("skincolors", new (Color, float)[] { (new Color(255, 215, 200, 255), 100f) }).ToImmutableArray();

                var headPreset = Prefab.Heads.GetRandom(randSync);
                Head = new HeadInfo(this, headPreset);
                SetAttachments(randSync);
                SetColors(randSync);
                
                Job = job ?? ((jobPrefab == null) ? Job.Random(Rand.RandSync.Unsynced) : new Job(jobPrefab, randSync, variant));

                if (!string.IsNullOrEmpty(name))
                {
                    Name = name;
                }
                else
                {
                    Name = GetRandomName(randSync);
                }
                TryLoadNameAndTitle(npcIdentifier);
                SetPersonalityTrait();

                Salary = CalculateSalary();
            }
            OriginalName = !string.IsNullOrEmpty(originalName) ? originalName : Name;
            if (ragdollFileName != null)
            {
                this.ragdollFileName = ragdollFileName;
            }
        }

        private void SetPersonalityTrait()
            => PersonalityTrait = NPCPersonalityTrait.GetRandom(Name + string.Concat(Head.Preset.TagSet));

        public string GetRandomName(Rand.RandSync randSync)
        {
            GetName(randSync, out string name);

            return name;
        }

        public static Color SelectRandomColor(in ImmutableArray<(Color Color, float Commonness)> array, Rand.RandSync randSync)
            => ToolBox.SelectWeightedRandom(array, array.Select(p => p.Commonness).ToArray(), randSync)
                .Color;

        private void SetAttachments(Rand.RandSync randSync)
        {
            LoadHeadAttachments();

            int pickRandomIndex(IReadOnlyList<ContentXElement> list)
            {
                var elems = GetValidAttachmentElements(list, Head.Preset).ToArray();
                var weights = GetWeights(elems).ToArray();
                return list.IndexOf(ToolBox.SelectWeightedRandom(elems, weights, randSync));
            }

            Head.HairIndex = pickRandomIndex(Hairs);
            Head.BeardIndex = pickRandomIndex(Beards);
            Head.MoustacheIndex = pickRandomIndex(Moustaches);
            Head.FaceAttachmentIndex = pickRandomIndex(FaceAttachments);
        }
        
        private void SetColors(Rand.RandSync randSync)
        {
            Head.HairColor = SelectRandomColor(HairColors, randSync);
            Head.FacialHairColor = SelectRandomColor(FacialHairColors, randSync);
            Head.SkinColor = SelectRandomColor(SkinColors, randSync);
        }

        private bool IsColorValid(in Color clr)
            => clr.R != 0 || clr.G != 0 || clr.B != 0;
        
        public void CheckColors()
        {
            if (!IsColorValid(Head.HairColor))
            {
                Head.HairColor = SelectRandomColor(HairColors, Rand.RandSync.Unsynced);
            }
            if (!IsColorValid(Head.FacialHairColor))
            {
                Head.FacialHairColor = SelectRandomColor(FacialHairColors, Rand.RandSync.Unsynced);
            }
            if (!IsColorValid(Head.SkinColor))
            {
                Head.SkinColor = SelectRandomColor(SkinColors, Rand.RandSync.Unsynced);
            }
        }

        // Used for loading the data
        public CharacterInfo(XElement infoElement, Identifier npcIdentifier = default)
        {
            ID = idCounter;
            idCounter++;
            Name = infoElement.GetAttributeString("name", "");
            OriginalName = infoElement.GetAttributeString("originalname", null);
            Salary = infoElement.GetAttributeInt("salary", 1000);
            ExperiencePoints = infoElement.GetAttributeInt("experiencepoints", 0);
            AdditionalTalentPoints = infoElement.GetAttributeInt("additionaltalentpoints", 0);
            HashSet<Identifier> tags = infoElement.GetAttributeIdentifierArray("tags", Array.Empty<Identifier>()).ToHashSet();
            LoadTagsBackwardsCompatibility(infoElement, tags);
            SpeciesName = infoElement.GetAttributeIdentifier("speciesname", "");
            ContentXElement element;
            if (!SpeciesName.IsEmpty)
            {
                element = CharacterPrefab.FindBySpeciesName(SpeciesName)?.ConfigElement;
            }
            else
            {
                // Backwards support (human only)
                // Actually you know what this is backwards!
                throw new InvalidOperationException("SpeciesName not defined");
            }
            if (element == null) { return; }
            // TODO: support for variants
            CharacterConfigElement = element;
            HasSpecifierTags = ElementHasSpecifierTags(CharacterConfigElement);
            if (HasSpecifierTags)
            {
                RecreateHead(
                    tags.ToImmutableHashSet(),
                    infoElement.GetAttributeInt("hairindex", -1),
                    infoElement.GetAttributeInt("beardindex", -1),
                    infoElement.GetAttributeInt("moustacheindex", -1),
                    infoElement.GetAttributeInt("faceattachmentindex", -1));

                HairColors = CharacterConfigElement.GetAttributeTupleArray("haircolors", new (Color, float)[] { (Color.WhiteSmoke, 100f) }).ToImmutableArray();
                FacialHairColors = CharacterConfigElement.GetAttributeTupleArray("facialhaircolors", new (Color, float)[] { (Color.WhiteSmoke, 100f) }).ToImmutableArray();
                SkinColors = CharacterConfigElement.GetAttributeTupleArray("skincolors", new (Color, float)[] { (new Color(255, 215, 200, 255), 100f) }).ToImmutableArray();
                
                //default to transparent color, it's invalid and will be replaced with a random one in CheckColors
                Head.SkinColor = infoElement.GetAttributeColor("skincolor", Color.Transparent);
                Head.HairColor = infoElement.GetAttributeColor("haircolor", Color.Transparent);
                Head.FacialHairColor = infoElement.GetAttributeColor("facialhaircolor", Color.Transparent);
                CheckColors();

                TryLoadNameAndTitle(npcIdentifier);

                if (string.IsNullOrEmpty(Name))
                {
                    var nameElement = CharacterConfigElement.GetChildElement("names");
                    if (nameElement != null)
                    {
                        GetName(Rand.RandSync.ServerAndClient, out Name);
                    }
                }
            }

            if (string.IsNullOrEmpty(OriginalName))
            {
                OriginalName = Name;
            }

            StartItemsGiven = infoElement.GetAttributeBool("startitemsgiven", false);
            Identifier personalityName = infoElement.GetAttributeIdentifier("personality", "");
            ragdollFileName = infoElement.GetAttributeString("ragdoll", string.Empty);
            if (personalityName != Identifier.Empty)
            {
                if (NPCPersonalityTrait.Traits.TryGet(personalityName, out var trait) ||
                    NPCPersonalityTrait.Traits.TryGet(personalityName.Replace(" ".ToIdentifier(), Identifier.Empty), out trait))
                {
                    PersonalityTrait = trait;
                }
                else
                {
                    DebugConsole.ThrowError($"Error in CharacterInfo \"{OriginalName}\": could not find a personality trait with the identifier \"{personalityName}\".");
                }
            }

            HumanPrefabIds = (
                infoElement.GetAttributeIdentifier("npcsetid", Identifier.Empty),
                infoElement.GetAttributeIdentifier("npcid", Identifier.Empty));

            MissionsCompletedSinceDeath = infoElement.GetAttributeInt("missionscompletedsincedeath", 0);
            UnlockedTalents = new HashSet<Identifier>();

            MinReputationToHire = (infoElement.GetAttributeIdentifier("factionId", Identifier.Empty), infoElement.GetAttributeFloat("minreputation", 0.0f));

            foreach (var subElement in infoElement.Elements())
            {
                bool jobCreated = false;

                Identifier elementName = subElement.Name.ToIdentifier();

                if (elementName == "job" && !jobCreated)
                {
                    Job = new Job(subElement);
                    jobCreated = true;
                    // there used to be a break here, but it had to be removed to make room for statvalues
                    // using the jobCreated boolean to make sure that only the first job found is created
                }
                else if (elementName == "savedstatvalues")
                {
                    foreach (XElement savedStat in subElement.Elements())
                    {
                        string statTypeString = savedStat.GetAttributeString("stattype", "").ToLowerInvariant();
                        if (!Enum.TryParse(statTypeString, true, out StatTypes statType))
                        {
                            DebugConsole.ThrowError("Invalid stat type type \"" + statTypeString + "\" when loading character data in CharacterInfo!");
                            continue;
                        }

                        float value = savedStat.GetAttributeFloat("statvalue", 0f);
                        if (value == 0f) { continue; }

                        Identifier statIdentifier = savedStat.GetAttributeIdentifier("statidentifier", Identifier.Empty);
                        if (statIdentifier.IsEmpty)
                        {
                            DebugConsole.ThrowError("Stat identifier not specified for Stat Value when loading character data in CharacterInfo!");
                            return;
                        }

                        bool removeOnDeath = savedStat.GetAttributeBool("removeondeath", true);
                        ChangeSavedStatValue(statType, value, statIdentifier, removeOnDeath);
                    }
                }
                else if (elementName == "talents")
                {
                    Version version = subElement.GetAttributeVersion("version", GameMain.Version); // for future maybe

                    foreach (XElement talentElement in subElement.Elements())
                    {
                        if (talentElement.Name.ToIdentifier() != "talent") { continue; }

                        Identifier talentIdentifier = talentElement.GetAttributeIdentifier("identifier", Identifier.Empty);
                        if (talentIdentifier == Identifier.Empty) { continue; }

                        UnlockedTalents.Add(talentIdentifier);
                    }
                }
            }
            LoadHeadAttachments();
        }

        private void TryLoadNameAndTitle(Identifier npcIdentifier)
        {
            if (!npcIdentifier.IsEmpty)
            {
                Title = TextManager.Get("npctitle." + npcIdentifier);
                string nameTag = "charactername." + npcIdentifier;
                if (TextManager.ContainsTag(nameTag))
                {
                    Name = TextManager.Get(nameTag).Value;
                }
            }
        }

        private List<ContentXElement> hairs;
        public IReadOnlyList<ContentXElement> Hairs => hairs;
        private List<ContentXElement> beards;
        public IReadOnlyList<ContentXElement> Beards => beards;
        private List<ContentXElement> moustaches;
        public IReadOnlyList<ContentXElement> Moustaches => moustaches;
        private List<ContentXElement> faceAttachments;
        public IReadOnlyList<ContentXElement> FaceAttachments => faceAttachments;

        private IEnumerable<ContentXElement> wearables;
        public IEnumerable<ContentXElement> Wearables
        {
            get
            {
                if (wearables == null)
                {
                    var attachments = CharacterConfigElement.GetChildElement("HeadAttachments");
                    if (attachments != null)
                    {
                        wearables = attachments.GetChildElements("Wearable");
                    }
                }
                return wearables;
            }
        }

        /// <summary>
        /// Returns a presumably (not guaranteed) unique hash using the (current) Name, appearence, and job.
        /// So unless there's another character with the exactly same name, job, and appearance, the hash should be unique.
        /// </summary>
        public int GetIdentifier()
        {
            return GetIdentifierHash(Name);
        }

        /// <summary>
        /// Returns a presumably (not guaranteed) unique hash using the OriginalName, appearence, and job.
        /// So unless there's another character with the exactly same name, job, and appearance, the hash should be unique.
        /// </summary>
        public int GetIdentifierUsingOriginalName()
        {
            return GetIdentifierHash(OriginalName);
        }

        private int GetIdentifierHash(string name)
        {
            int id = ToolBox.StringToInt(name + string.Join("", Head.Preset.TagSet.OrderBy(s => s)));
            id ^= Head.HairIndex << 12;
            id ^= Head.BeardIndex << 18;
            id ^= Head.MoustacheIndex << 24;
            id ^= Head.FaceAttachmentIndex << 30;
            if (Job != null)
            {
                id ^= ToolBox.StringToInt(Job.Prefab.Identifier.Value);
            }
            return id;
        }

        public IEnumerable<ContentXElement> FilterElements(IEnumerable<ContentXElement> elements, ImmutableHashSet<Identifier> tags, WearableType? targetType = null)
        {
            if (elements is null) { return null; }
            return elements.Where(w =>
            {
                if (!(targetType is null))
                {
                    if (Enum.TryParse(w.GetAttributeString("type", ""), true, out WearableType type) && type != targetType) { return false; }
                }
                HashSet<Identifier> t = w.GetAttributeIdentifierArray("tags", Array.Empty<Identifier>()).ToHashSet();
                LoadTagsBackwardsCompatibility(w, t);
                return t.IsSubsetOf(tags);
            });
        }

        public void RecreateHead(ImmutableHashSet<Identifier> tags, int hairIndex, int beardIndex, int moustacheIndex, int faceAttachmentIndex)
        {
            HeadPreset headPreset = Prefab.Heads.FirstOrDefault(h => h.TagSet.SetEquals(tags));
            if (headPreset == null) 
            {
                if (tags.Count == 1)
                {
                    headPreset = Prefab.Heads.FirstOrDefault(h => h.TagSet.Contains(tags.First()));
                }
                headPreset ??= Prefab.Heads.GetRandomUnsynced(); 
            }
            head = new HeadInfo(this, headPreset, hairIndex, beardIndex, moustacheIndex, faceAttachmentIndex);
            ReloadHeadAttachments();
        }

        public string ReplaceVars(string str)
        {
            return Prefab.ReplaceVars(str, Head.Preset);
        }

#if CLIENT
        public void RecreateHead(MultiplayerPreferences characterSettings)
        {
            if (characterSettings.HairIndex == -1 && 
                characterSettings.BeardIndex == -1 && 
                characterSettings.MoustacheIndex == -1 && 
                characterSettings.FaceAttachmentIndex == -1)
            {
                //randomize if nothing is set
                SetAttachments(Rand.RandSync.Unsynced);
                characterSettings.HairIndex = Head.HairIndex;
                characterSettings.BeardIndex = Head.BeardIndex;
                characterSettings.MoustacheIndex = Head.MoustacheIndex;
                characterSettings.FaceAttachmentIndex = Head.FaceAttachmentIndex;
            }

            RecreateHead(
                characterSettings.TagSet.ToImmutableHashSet(),
                characterSettings.HairIndex,
                characterSettings.BeardIndex,
                characterSettings.MoustacheIndex,
                characterSettings.FaceAttachmentIndex);

            Head.SkinColor = ChooseColor(SkinColors, characterSettings.SkinColor);
            Head.HairColor = ChooseColor(HairColors, characterSettings.HairColor);
            Head.FacialHairColor = ChooseColor(FacialHairColors, characterSettings.FacialHairColor);

            Color ChooseColor(in ImmutableArray<(Color Color, float Commonness)> availableColors, Color chosenColor)
            {
                return availableColors.Any(c => c.Color == chosenColor) ? chosenColor : SelectRandomColor(availableColors, Rand.RandSync.Unsynced);
            }
        }
#endif
        
        public void RecreateHead(HeadInfo headInfo)
        {
            RecreateHead(
                headInfo.Preset.TagSet,
                headInfo.HairIndex,
                headInfo.BeardIndex,
                headInfo.MoustacheIndex,
                headInfo.FaceAttachmentIndex);

            Head.SkinColor = headInfo.SkinColor;
            Head.HairColor = headInfo.HairColor;
            Head.FacialHairColor = headInfo.FacialHairColor;
            CheckColors();
        }

        /// <summary>
        /// Reloads the head sprite and the attachment sprites.
        /// </summary>
        public void RefreshHead()
        {
            ReloadHeadAttachments();
            RefreshHeadSprites();
        }

        partial void LoadHeadSpriteProjectSpecific(ContentXElement limbElement);
        
        private void LoadHeadSprite()
        {
            foreach (var limbElement in Ragdoll.MainElement.Elements())
            {
                if (!limbElement.GetAttributeString("type", string.Empty).Equals("head", StringComparison.OrdinalIgnoreCase)) { continue; }

                ContentXElement spriteElement = limbElement.GetChildElement("sprite");
                if (spriteElement == null) { continue; }

                string spritePath = spriteElement.GetAttributeContentPath("texture")?.Value;
                if (string.IsNullOrEmpty(spritePath)) { continue; }

                spritePath = ReplaceVars(spritePath);

                string fileName = Path.GetFileNameWithoutExtension(spritePath);

                if (string.IsNullOrEmpty(fileName)) { continue; }

                //go through the files in the directory to find a matching sprite
                foreach (string file in Directory.GetFiles(Path.GetDirectoryName(spritePath)))
                {
                    if (!file.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    string fileWithoutTags = Path.GetFileNameWithoutExtension(file);
                    fileWithoutTags = fileWithoutTags.Split('[', ']').First();
                    if (fileWithoutTags != fileName) { continue; }

                    HeadSprite = new Sprite(spriteElement, "", file);
                    Portrait = new Sprite(spriteElement, "", file) { RelativeOrigin = Vector2.Zero };

                    //extract the tags out of the filename
                    SpriteTags = file.Split('[', ']').Skip(1).Select(id => id.ToIdentifier()).ToList();
                    if (SpriteTags.Any())
                    {
                        SpriteTags.RemoveAt(SpriteTags.Count - 1);
                    }

                    break;
                }

                LoadHeadSpriteProjectSpecific(limbElement);

                break;
            }
        }

        public void LoadHeadAttachments()
        {
            if (Wearables != null)
            {
                if (hairs == null)
                {
                    float commonness = 0.1f;
                    hairs = AddEmpty(FilterElements(wearables, head.Preset.TagSet, WearableType.Hair), WearableType.Hair, commonness);
                }
                if (beards == null)
                {
                    beards = AddEmpty(FilterElements(wearables, head.Preset.TagSet, WearableType.Beard), WearableType.Beard);
                }
                if (moustaches == null)
                {
                    moustaches = AddEmpty(FilterElements(wearables, head.Preset.TagSet, WearableType.Moustache), WearableType.Moustache);
                }
                if (faceAttachments == null)
                {
                    faceAttachments = AddEmpty(FilterElements(wearables, head.Preset.TagSet, WearableType.FaceAttachment), WearableType.FaceAttachment);
                }
            }
        }

        public static List<ContentXElement> AddEmpty(IEnumerable<ContentXElement> elements, WearableType type, float commonness = 1)
        {
            // Let's add an empty element so that there's a chance that we don't get any actual element -> allows bald and beardless guys, for example.
            var emptyElement = new XElement("EmptyWearable", type.ToString(), new XAttribute("commonness", commonness)).FromPackage(null);
            var list = new List<ContentXElement>() { emptyElement };
            list.AddRange(elements);
            return list;
        }

        public ContentXElement GetRandomElement(IEnumerable<ContentXElement> elements)
        {
            var filtered = elements.Where(IsWearableAllowed);
            if (filtered.Count() == 0) { return null; }
            var element = ToolBox.SelectWeightedRandom(filtered.ToList(), GetWeights(filtered).ToList(), Rand.RandSync.Unsynced);
            return element == null || element.NameAsIdentifier() == "Empty" ? null : element;
        }

        private bool IsWearableAllowed(ContentXElement element)
        {
            string spriteName = element.GetChildElement("sprite").GetAttributeString("name", string.Empty);
            return IsAllowed(Head.HairElement, spriteName) && IsAllowed(Head.BeardElement, spriteName) && IsAllowed(Head.MoustacheElement, spriteName) && IsAllowed(Head.FaceAttachment, spriteName);
        }

        private bool IsAllowed(XElement element, string spriteName)
        {
            if (element != null)
            {
                var disallowed = element.GetAttributeStringArray("disallow", Array.Empty<string>());
                if (disallowed.Any(s => spriteName.Contains(s)))
                {
                    return false;
                }
            }
            return true;
        }

        public static bool IsValidIndex(int index, List<ContentXElement> list) => index >= 0 && index < list.Count;

        private static IEnumerable<float> GetWeights(IEnumerable<ContentXElement> elements) => elements.Select(h => h.GetAttributeFloat("commonness", 1f));

        partial void LoadAttachmentSprites();
        
        public int CalculateSalary()
        {
            if (Name == null || Job == null) { return 0; }

            int salary = 0;
            foreach (Skill skill in Job.GetSkills())
            {
                salary += (int)(skill.Level * skill.PriceMultiplier);
            }

            return (int)(salary * Job.Prefab.PriceMultiplier);
        }

        public void IncreaseSkillLevel(Identifier skillIdentifier, float increase, bool gainedFromAbility = false)
        {
            if (Job == null || (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) || Character == null) { return; }

            if (Job.Prefab.Identifier == "assistant")
            {
                increase *= SkillSettings.Current.AssistantSkillIncreaseMultiplier;
            }

            increase *= 1f + Character.GetStatValue(StatTypes.SkillGainSpeed);

            increase = GetSkillSpecificGain(increase, skillIdentifier);

            float prevLevel = Job.GetSkillLevel(skillIdentifier);
            Job.IncreaseSkillLevel(skillIdentifier, increase, Character.HasAbilityFlag(AbilityFlags.GainSkillPastMaximum));

            float newLevel = Job.GetSkillLevel(skillIdentifier);

            if ((int)newLevel > (int)prevLevel)
            {
                float extraLevel = Character.GetStatValue(StatTypes.ExtraLevelGain);
                Job.IncreaseSkillLevel(skillIdentifier, extraLevel, Character.HasAbilityFlag(AbilityFlags.GainSkillPastMaximum));
                // assume we are getting at least 1 point in skill, since this logic only runs in such cases
                float increaseSinceLastSkillPoint = MathHelper.Max(increase, 1f);
                var abilitySkillGain = new AbilitySkillGain(increaseSinceLastSkillPoint, skillIdentifier, Character, gainedFromAbility);
                Character.CheckTalents(AbilityEffectType.OnGainSkillPoint, abilitySkillGain);
                foreach (Character character in Character.GetFriendlyCrew(Character))
                {
                    character.CheckTalents(AbilityEffectType.OnAllyGainSkillPoint, abilitySkillGain);
                }
            }

            OnSkillChanged(skillIdentifier, prevLevel, newLevel);
        }

        private static readonly ImmutableDictionary<Identifier, StatTypes> skillGainStatValues = new Dictionary<Identifier, StatTypes>
        {
            { new("helm"), StatTypes.HelmSkillGainSpeed },
            { new("medical"), StatTypes.WeaponsSkillGainSpeed },
            { new("weapons"), StatTypes.MedicalSkillGainSpeed },
            { new("electrical"), StatTypes.ElectricalSkillGainSpeed },
            { new("mechanical"), StatTypes.MechanicalSkillGainSpeed }
        }.ToImmutableDictionary();

        private float GetSkillSpecificGain(float increase, Identifier skillIdentifier)
        {
            if (skillGainStatValues.TryGetValue(skillIdentifier, out StatTypes statType))
            {
                increase *= 1f + Character.GetStatValue(statType);
            }

            return increase;
        }

        public void SetSkillLevel(Identifier skillIdentifier, float level)
        {
            if (Job == null) { return; }

            var skill = Job.GetSkill(skillIdentifier);
            if (skill == null)
            {
                Job.IncreaseSkillLevel(skillIdentifier, level, increasePastMax: false);
                OnSkillChanged(skillIdentifier, 0.0f, level);
            }
            else
            {
                float prevLevel = skill.Level;
                skill.Level = level;
                OnSkillChanged(skillIdentifier, prevLevel, skill.Level);
            }
        }

        partial void OnSkillChanged(Identifier skillIdentifier, float prevLevel, float newLevel);

        public void GiveExperience(int amount)
        {
            int prevAmount = ExperiencePoints;

            var experienceGainMultiplier = new AbilityExperienceGainMultiplier(1f);
            experienceGainMultiplier.Value += Character?.GetStatValue(StatTypes.ExperienceGainMultiplier) ?? 0;

            amount = (int)(amount * experienceGainMultiplier.Value);
            if (amount < 0) { return; }

            ExperiencePoints += amount;
            OnExperienceChanged(prevAmount, ExperiencePoints);
        }

        public void SetExperience(int newExperience)
        {
            if (newExperience < 0) { return; }

            int prevAmount = ExperiencePoints;
            ExperiencePoints = newExperience;
            OnExperienceChanged(prevAmount, ExperiencePoints);
        }

        const int BaseExperienceRequired = -50;
        const int AddedExperienceRequiredPerLevel = 500;

        public int GetTotalTalentPoints()
        {
            return GetCurrentLevel() + AdditionalTalentPoints - 1;
        }

        public int GetAvailableTalentPoints()
        {
            // hashset always has at least 1 
            return Math.Max(GetTotalTalentPoints() - GetUnlockedTalentsInTree().Count(), 0);
        }

        public float GetProgressTowardsNextLevel()
        {
            return (ExperiencePoints - GetExperienceRequiredForCurrentLevel()) / (float)(GetExperienceRequiredToLevelUp() - GetExperienceRequiredForCurrentLevel());
        }

        public int GetExperienceRequiredForCurrentLevel()
        {
            GetCurrentLevel(out int experienceRequired);
            return experienceRequired;
        }

        public int GetExperienceRequiredToLevelUp()
        {
            int level = GetCurrentLevel(out int experienceRequired);
            return experienceRequired + ExperienceRequiredPerLevel(level);
        }

        public int GetExperienceRequiredForLevel(int level)
        {
            int currentLevel = GetCurrentLevel(out int experienceRequired);
            if (currentLevel >= level) { return 0; }
            int required = experienceRequired;
            for (int i = currentLevel + 1; i <= level; i++)
            {
                required += ExperienceRequiredPerLevel(i);
            }
            return required;
        }

        public int GetCurrentLevel()
        {
            return GetCurrentLevel(out _);
        }

        private int GetCurrentLevel(out int experienceRequired)
        {
            int level = 1;
            experienceRequired = 0;
            while (experienceRequired + ExperienceRequiredPerLevel(level) <= ExperiencePoints)
            {
                experienceRequired += ExperienceRequiredPerLevel(level);
                level++;
            }
            return level;
        }

        private static int ExperienceRequiredPerLevel(int level)
        {
            return BaseExperienceRequired + AddedExperienceRequiredPerLevel * level;
        }

        partial void OnExperienceChanged(int prevAmount, int newAmount);

        partial void OnPermanentStatChanged(StatTypes statType);

        public void Rename(string newName)
        {
            if (string.IsNullOrEmpty(newName)) { return; }
            // Replace the name tag of any existing id cards or duffel bags
            foreach (var item in Item.ItemList)
            {
                if (!item.HasTag("identitycard") && !item.HasTag("despawncontainer")) { continue; }
                foreach (var tag in item.Tags.Split(','))
                {
                    var splitTag = tag.Split(":");
                    if (splitTag.Length < 2) { continue; }
                    if (splitTag[0] != "name") { continue; }
                    if (splitTag[1] != Name) { continue; }
                    item.ReplaceTag(tag, $"name:{newName}");
                    var idCard = item.GetComponent<IdCard>();
                    if (idCard != null)
                    {
                        idCard.OwnerName = newName;
                    }
                    break;
                }
            }
            Name = newName;
        }

        public void ResetName()
        {
            Name = OriginalName;
        }

        public XElement Save(XElement parentElement)
        {
            XElement charElement = new XElement("Character");

            charElement.Add(
                new XAttribute("name", Name),
                new XAttribute("originalname", OriginalName),
                new XAttribute("speciesname", SpeciesName),
                new XAttribute("tags", string.Join(",", Head.Preset.TagSet)),
                new XAttribute("salary", Salary),
                new XAttribute("experiencepoints", ExperiencePoints),
                new XAttribute("additionaltalentpoints", AdditionalTalentPoints),
                new XAttribute("hairindex", Head.HairIndex),
                new XAttribute("beardindex", Head.BeardIndex),
                new XAttribute("moustacheindex", Head.MoustacheIndex),
                new XAttribute("faceattachmentindex", Head.FaceAttachmentIndex),
                new XAttribute("skincolor", XMLExtensions.ColorToString(Head.SkinColor)),
                new XAttribute("haircolor", XMLExtensions.ColorToString(Head.HairColor)),
                new XAttribute("facialhaircolor", XMLExtensions.ColorToString(Head.FacialHairColor)),
                new XAttribute("startitemsgiven", StartItemsGiven),
                new XAttribute("ragdoll", ragdollFileName),
                new XAttribute("personality", PersonalityTrait?.Identifier ?? Identifier.Empty));
                // TODO: animations?

            if (HumanPrefabIds != default)
            {
                charElement.Add(
                    new XAttribute("npcsetid", HumanPrefabIds.NpcSetIdentifier),
                    new XAttribute("npcid", HumanPrefabIds.NpcIdentifier));
            }

            charElement.Add(new XAttribute("missionscompletedsincedeath", MissionsCompletedSinceDeath));

            if (MinReputationToHire.factionId != default)
            {
                charElement.Add(
                    new XAttribute("factionId", Name),
                    new XAttribute("minreputation", MinReputationToHire.reputation));
            }

            if (Character != null)
            {
                if (Character.AnimController.CurrentHull != null)
                {
                    charElement.Add(new XAttribute("hull", Character.AnimController.CurrentHull.ID));
                }
            }
            
            Job.Save(charElement);

            XElement savedStatElement = new XElement("savedstatvalues");
            foreach (var statValuePair in SavedStatValues)
            {
                foreach (var savedStat in statValuePair.Value)
                {
                    if (savedStat.StatValue == 0f) { continue; }

                    savedStatElement.Add(new XElement("savedstatvalue",
                        new XAttribute("stattype", statValuePair.Key.ToString()),
                        new XAttribute("statidentifier", savedStat.StatIdentifier),
                        new XAttribute("statvalue", savedStat.StatValue),
                        new XAttribute("removeondeath", savedStat.RemoveOnDeath)
                        ));
                }
            }

            XElement talentElement = new XElement("Talents");
            talentElement.Add(new XAttribute("version", GameMain.Version.ToString()));

            foreach (Identifier talentIdentifier in UnlockedTalents)
            {
                talentElement.Add(new XElement("Talent", new XAttribute("identifier", talentIdentifier)));
            }

            charElement.Add(savedStatElement);
            charElement.Add(talentElement);
            parentElement?.Add(charElement);
            return charElement;
        }

        public static void SaveOrders(XElement parentElement, params Order[] orders)
        {
            if (parentElement == null || orders == null || orders.None()) { return; }
            // If an order is invalid, we discard the order and increase the priority of the following orders so
            // 1) the highest priority value will remain equal to CharacterInfo.HighestManualOrderPriority; and
            // 2) the order priorities will remain sequential.
            int priorityIncrease = 0;
            var linkedSubs = GetLinkedSubmarines();
            foreach (var orderInfo in orders)
            {
                var order = orderInfo;
                if (order == null || order.Identifier == Identifier.Empty)
                {
                    DebugConsole.ThrowError("Error saving an order - the order or its identifier is null");
                    priorityIncrease++;
                    continue;
                }
                int? linkedSubIndex = null;
                bool targetAvailableInNextLevel = true;
                if (order.TargetSpatialEntity != null)
                {
                    var entitySub = order.TargetSpatialEntity.Submarine;
                    bool isOutside = entitySub == null;
                    bool canBeOnLinkedSub = !isOutside && Submarine.MainSub != null && entitySub != Submarine.MainSub && linkedSubs.Any();
                    bool isOnConnectedLinkedSub = false;
                    if (canBeOnLinkedSub)
                    {
                        for (int i = 0; i < linkedSubs.Count; i++)
                        {
                            var ls = linkedSubs[i];
                            if (!ls.LoadSub) { continue; }
                            if (ls.Sub != entitySub) { continue; }
                            linkedSubIndex = i;
                            isOnConnectedLinkedSub = Submarine.MainSub.GetConnectedSubs().Contains(entitySub);
                            break;
                        }
                    }
                    targetAvailableInNextLevel = !isOutside && GameMain.GameSession?.Campaign?.PendingSubmarineSwitch == null && (isOnConnectedLinkedSub || entitySub == Submarine.MainSub);
                    if (!targetAvailableInNextLevel)
                    {
                        if (!order.Prefab.CanBeGeneralized)
                        {
                            DebugConsole.Log($"Trying to save an order ({order.Identifier}) targeting an entity that won't be connected to the main sub in the next level. The order requires a target so it won't be saved.");
                            priorityIncrease++;
                            continue;
                        }
                        else
                        {
                            DebugConsole.Log($"Saving an order ({order.Identifier}) targeting an entity that won't be connected to the main sub in the next level. The order will be saved as a generalized version.");
                        }
                    }
                }
                if (orderInfo.ManualPriority < 1)
                {
                    DebugConsole.ThrowError($"Error saving an order ({order.Identifier}) - the order priority is less than 1");
                    priorityIncrease++;
                    continue;
                }
                var orderElement = new XElement("order",
                    new XAttribute("id", order.Identifier),
                    new XAttribute("priority", orderInfo.ManualPriority + priorityIncrease),
                    new XAttribute("targettype", (int)order.TargetType));
                if (orderInfo.Option != Identifier.Empty)
                {
                    orderElement.Add(new XAttribute("option", orderInfo.Option));
                }
                if (order.OrderGiver != null)
                {
                    orderElement.Add(new XAttribute("ordergiver", order.OrderGiver.Info?.GetIdentifier()));
                }
                if (order.TargetSpatialEntity?.Submarine is Submarine targetSub)
                {
                    if (targetSub == Submarine.MainSub)
                    {
                        orderElement.Add(new XAttribute("onmainsub", true));
                    }
                    else if(linkedSubIndex.HasValue)
                    {
                        orderElement.Add(new XAttribute("linkedsubindex", linkedSubIndex));
                    }
                }
                switch (order.TargetType)
                {
                    case Order.OrderTargetType.Entity when targetAvailableInNextLevel && order.TargetEntity is Entity e:
                        orderElement.Add(new XAttribute("targetid", (uint)e.ID));
                        break;
                    case Order.OrderTargetType.Position when targetAvailableInNextLevel && order.TargetSpatialEntity is OrderTarget ot:
                        var orderTargetElement = new XElement("ordertarget");
                        var position = ot.WorldPosition;
                        if (ot.Hull != null)
                        {
                            orderTargetElement.Add(new XAttribute("hullid", (uint)ot.Hull.ID));
                            position -= ot.Hull.WorldPosition;
                        }
                        orderTargetElement.Add(new XAttribute("position", XMLExtensions.Vector2ToString(position)));
                        orderElement.Add(orderTargetElement);
                        break;
                    case Order.OrderTargetType.WallSection when targetAvailableInNextLevel && order.TargetEntity is Structure s && order.WallSectionIndex.HasValue:
                        orderElement.Add(new XAttribute("structureid", s.ID));
                        orderElement.Add(new XAttribute("wallsectionindex", order.WallSectionIndex.Value));
                        break;
                }
                parentElement.Add(orderElement);
            }
        }

        /// <summary>
        /// Save current orders to the parameter element
        /// </summary>
        public static void SaveOrderData(CharacterInfo characterInfo, XElement parentElement)
        {
            var currentOrders = new List<Order>(characterInfo.CurrentOrders);
            // Sort the current orders to make sure the one with the highest priority comes first
            currentOrders.Sort((x, y) => y.ManualPriority.CompareTo(x.ManualPriority));
            SaveOrders(parentElement, currentOrders.ToArray());
        }

        /// <summary>
        /// Save current orders to <see cref="OrderData"/>
        /// </summary>
        public void SaveOrderData()
        {
            OrderData = new XElement("orders");
            SaveOrderData(this, OrderData);
        }

        public static void ApplyOrderData(Character character, XElement orderData)
        {
            if (character == null) { return; }
            var orders = LoadOrders(orderData);
            foreach (var order in orders)
            {
                character.SetOrder(order, isNewOrder: true, speak: false, force: true);
            }
        }

        public void ApplyOrderData()
        {
            ApplyOrderData(Character, OrderData);
        }

        public static List<Order> LoadOrders(XElement ordersElement)
        {
            var orders = new List<Order>();
            if (ordersElement == null) { return orders; }
            // If an order is invalid, we discard the order and increase the priority of the following orders so
            // 1) the highest priority value will remain equal to CharacterInfo.HighestManualOrderPriority; and
            // 2) the order priorities will remain sequential.
            int priorityIncrease = 0;
            var linkedSubs = GetLinkedSubmarines();
            foreach (var orderElement in ordersElement.GetChildElements("order"))
            {
                Order order = null;
                string orderIdentifier = orderElement.GetAttributeString("id", "");
                var orderPrefab = OrderPrefab.Prefabs[orderIdentifier];
                if (orderPrefab == null)
                {
                    DebugConsole.ThrowError($"Error loading a previously saved order - can't find an order prefab with the identifier \"{orderIdentifier}\"");
                    priorityIncrease++;
                    continue;
                }
                var targetType = (Order.OrderTargetType)orderElement.GetAttributeInt("targettype", 0);
                int orderGiverInfoId = orderElement.GetAttributeInt("ordergiver", -1);
                var orderGiver = orderGiverInfoId >= 0 ? Character.CharacterList.FirstOrDefault(c => c.Info?.GetIdentifier() == orderGiverInfoId) : null;
                Entity targetEntity = null;
                switch (targetType)
                {
                    case Order.OrderTargetType.Entity:
                        ushort targetId = (ushort)orderElement.GetAttributeUInt("targetid", Entity.NullEntityID);
                        if (!GetTargetEntity(targetId, out targetEntity)) { continue; }
                        var targetComponent = orderPrefab.GetTargetItemComponent(targetEntity as Item);
                        order = new Order(orderPrefab, targetEntity, targetComponent, orderGiver: orderGiver);
                        break;
                    case Order.OrderTargetType.Position:
                        var orderTargetElement = orderElement.GetChildElement("ordertarget");
                        var position = orderTargetElement.GetAttributeVector2("position", Vector2.Zero);
                        ushort hullId = (ushort)orderTargetElement.GetAttributeUInt("hullid", 0);
                        if (!GetTargetEntity(hullId, out targetEntity)) { continue; }
                        if (!(targetEntity is Hull targetPositionHull))
                        {
                            DebugConsole.ThrowError($"Error loading a previously saved order ({orderIdentifier}) - entity with the ID {hullId} is of type {targetEntity?.GetType()} instead of Hull");
                            priorityIncrease++;
                            continue;
                        }
                        var orderTarget = new OrderTarget(targetPositionHull.WorldPosition + position, targetPositionHull);
                        order = new Order(orderPrefab, orderTarget, orderGiver: orderGiver);
                        break;
                    case Order.OrderTargetType.WallSection:
                        ushort structureId = (ushort)orderElement.GetAttributeInt("structureid", Entity.NullEntityID);
                        if (!GetTargetEntity(structureId, out targetEntity)) { continue; }
                        int wallSectionIndex = orderElement.GetAttributeInt("wallsectionindex", 0);
                        if (!(targetEntity is Structure targetStructure))
                        {
                            DebugConsole.ThrowError($"Error loading a previously saved order ({orderIdentifier}) - entity with the ID {structureId} is of type {targetEntity?.GetType()} instead of Structure");
                            priorityIncrease++;
                            continue;
                        }
                        order = new Order(orderPrefab, targetStructure, wallSectionIndex, orderGiver: orderGiver);
                        break;
                }
                Identifier orderOption = orderElement.GetAttributeIdentifier("option", "");
                int manualPriority = orderElement.GetAttributeInt("priority", 0) + priorityIncrease;
                var orderInfo = order.WithOption(orderOption).WithManualPriority(manualPriority);
                orders.Add(orderInfo);

                bool GetTargetEntity(ushort targetId, out Entity targetEntity)
                {
                    targetEntity = null;
                    if (targetId == Entity.NullEntityID) { return true; }
                    Submarine parentSub = null;
                    if (orderElement.GetAttributeBool("onmainsub", false))
                    {
                        parentSub = Submarine.MainSub;
                    }
                    else
                    {
                        int linkedSubIndex = orderElement.GetAttributeInt("linkedsubindex", -1);
                        if (linkedSubIndex >= 0 && linkedSubIndex < linkedSubs.Count &&
                            linkedSubs[linkedSubIndex] is LinkedSubmarine linkedSub && linkedSub.LoadSub)
                        {
                            parentSub = linkedSub.Sub;
                        }
                    }
                    if (parentSub != null)
                    {
                        targetId = GetOffsetId(parentSub, targetId);
                        targetEntity = Entity.FindEntityByID(targetId);
                    }
                    else
                    {
                        if (!orderPrefab.CanBeGeneralized)
                        {
                            DebugConsole.ThrowError($"Error loading a previously saved order ({orderIdentifier}). Can't find the parent sub of the target entity. The order requires a target so it can't be loaded at all.");
                            priorityIncrease++;
                            return false;
                        }
                        else
                        {
                            DebugConsole.AddWarning($"Trying to load a previously saved order ({orderIdentifier}). Can't find the parent sub of the target entity. The order doesn't require a target so a more generic version of the order will be loaded instead.");
                        }
                    }
                    return true;
                }
            }
            return orders;
        }

        private static List<LinkedSubmarine> GetLinkedSubmarines()
        {
            return Entity.GetEntities()
                .OfType<LinkedSubmarine>()
                .Where(ls => ls.Submarine == Submarine.MainSub)
                .OrderBy(e => e.ID)
                .ToList();
        }

        private static ushort GetOffsetId(Submarine parentSub, ushort id)
        {
            if (parentSub != null)
            {
                var idRemap = new IdRemap(parentSub.Info.SubmarineElement, parentSub.IdOffset);
                return idRemap.GetOffsetId(id);
            }
            return id;
        }

        public static void ApplyHealthData(Character character, XElement healthData, Func<AfflictionPrefab, bool> afflictionPredicate = null)
        {
            if (healthData != null) { character?.CharacterHealth.Load(healthData, afflictionPredicate); }
        }

        /// <summary>
        /// Reloads the attachment xml elements according to the indices. Doesn't reload the sprites.
        /// </summary>
        public void ReloadHeadAttachments()
        {
            ResetLoadedAttachments();
            LoadHeadAttachments();
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

        public void ClearCurrentOrders()
        {
            CurrentOrders.Clear();
        }

        public void Remove()
        {
            Character = null;
            HeadSprite = null;
            Portrait = null;
            AttachmentSprites = null;
        }

        private void RefreshHeadSprites()
        {
            _headSprite = null;
            LoadHeadSprite();
#if CLIENT
            CalculateHeadPosition(_headSprite);
#endif
            attachmentSprites?.Clear();
            LoadAttachmentSprites();
        }

        // This could maybe be a LookUp instead?
        public readonly Dictionary<StatTypes, List<SavedStatValue>> SavedStatValues = new Dictionary<StatTypes, List<SavedStatValue>>();

        public void ClearSavedStatValues()
        {
            foreach (StatTypes statType in SavedStatValues.Keys)
            {
                OnPermanentStatChanged(statType);
            }
            SavedStatValues.Clear();
        }

        public void ClearSavedStatValues(StatTypes statType)
        {
            SavedStatValues.Remove(statType);
            OnPermanentStatChanged(statType);
        }

        public void RemoveSavedStatValuesOnDeath()
        {
            foreach (StatTypes statType in SavedStatValues.Keys)
            {
                foreach (SavedStatValue savedStatValue in SavedStatValues[statType])
                {
                    if (!savedStatValue.RemoveOnDeath) { continue; }
                    if (MathUtils.NearlyEqual(savedStatValue.StatValue, 0.0f)) { continue; }
                    savedStatValue.StatValue = 0.0f;
                    // no need to make a network update, as this is only done after the character has died
                }
            }
        }

        public void ResetSavedStatValue(Identifier statIdentifier)
        {
            foreach (StatTypes statType in SavedStatValues.Keys)
            {
                bool changed = false;
                foreach (SavedStatValue savedStatValue in SavedStatValues[statType])
                {
                    if (!MatchesIdentifier(savedStatValue.StatIdentifier, statIdentifier)) { continue; }

                    if (MathUtils.NearlyEqual(savedStatValue.StatValue, 0.0f)) { continue; }
                    savedStatValue.StatValue = 0.0f;
                    changed = true;
                }
                if (changed) { OnPermanentStatChanged(statType); }
            }

            static bool MatchesIdentifier(Identifier statIdentifier, Identifier identifier)
            {
                if (statIdentifier == identifier) { return true; }

                if (identifier.IndexOf('*') is var index and > -1)
                {
                    return statIdentifier.StartsWith(identifier[0..index]);
                }

                return false;
            }
        }

        public float GetSavedStatValue(StatTypes statType)
        {
            if (SavedStatValues.TryGetValue(statType, out var statValues))
            {
                return statValues.Sum(v => v.StatValue);
            }
            else
            {
                return 0f;
            }
        }
        public float GetSavedStatValue(StatTypes statType, Identifier statIdentifier)
        {
            if (SavedStatValues.TryGetValue(statType, out var statValues))
            {
                return statValues.Where(value => ToolBox.StatIdentifierMatches(value.StatIdentifier, statIdentifier)).Sum(static v => v.StatValue);
            }
            else
            {
                return 0f;
            }
        }

        public void ChangeSavedStatValue(StatTypes statType, float value, Identifier statIdentifier, bool removeOnDeath, float maxValue = float.MaxValue, bool setValue = false)
        {
            if (!SavedStatValues.ContainsKey(statType))
            {
                SavedStatValues.Add(statType, new List<SavedStatValue>());
            }

            bool changed = false;
            if (SavedStatValues[statType].FirstOrDefault(s => s.StatIdentifier == statIdentifier) is SavedStatValue savedStat)
            {
                float prevValue = savedStat.StatValue;
                savedStat.StatValue = setValue ? value : MathHelper.Min(savedStat.StatValue + value, maxValue);
                changed = !MathUtils.NearlyEqual(savedStat.StatValue, prevValue);
            }
            else
            {
                SavedStatValues[statType].Add(new SavedStatValue(statIdentifier, MathHelper.Min(value, maxValue), removeOnDeath));
                changed = true;
            }
            if (changed) { OnPermanentStatChanged(statType); }
        }
    }

    internal sealed class SavedStatValue
    {
        public Identifier StatIdentifier { get; set; }
        public float StatValue { get; set; }
        public bool RemoveOnDeath { get; set; }

        public SavedStatValue(Identifier statIdentifier, float value, bool removeOnDeath)
        {
            StatValue = value;
            RemoveOnDeath = removeOnDeath;
            StatIdentifier = statIdentifier;
        }
    }

    internal sealed class AbilitySkillGain : AbilityObject, IAbilityValue, IAbilitySkillIdentifier, IAbilityCharacter
    {
        public AbilitySkillGain(float skillAmount, Identifier skillIdentifier, Character character, bool gainedFromAbility)
        {
            Value = skillAmount;
            SkillIdentifier = skillIdentifier;
            Character = character;
            GainedFromAbility = gainedFromAbility;
        }
        public Character Character { get; set; }
        public float Value { get; set; }
        public Identifier SkillIdentifier { get; set; }
        public bool GainedFromAbility { get; }
    }

    class AbilityExperienceGainMultiplier : AbilityObject, IAbilityValue
    {
        public AbilityExperienceGainMultiplier(float experienceGainMultiplier)
        {
            Value = experienceGainMultiplier;
        }
        public float Value { get; set; }
    }
}
