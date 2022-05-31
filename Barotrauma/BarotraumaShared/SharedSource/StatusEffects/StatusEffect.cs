using Barotrauma.Abilities;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Networking;

namespace Barotrauma
{
    class DurationListElement
    {
        public readonly StatusEffect Parent;
        public readonly Entity Entity;
        public float Duration
        {
            get;
            private set;
        }
        public readonly List<ISerializableEntity> Targets;
        public Character User { get; private set; }

        public float Timer;

        public DurationListElement(StatusEffect parentEffect, Entity parentEntity, IEnumerable<ISerializableEntity> targets, float duration, Character user)
        {
            Parent = parentEffect;
            Entity = parentEntity;
            Targets = new List<ISerializableEntity>(targets);
            Timer = Duration = duration;
            User = user;
        }

        public void Reset(float duration, Character newUser)
        {
            Timer = Duration = duration;
            User = newUser;
        }
    }

    class AITrigger : ISerializableEntity
    {
        public string Name => "ai trigger";

        public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; set; }

        [Serialize(AIState.Idle, IsPropertySaveable.No)]
        public AIState State { get; private set; }

        [Serialize(0f, IsPropertySaveable.No)]
        public float Duration { get; private set; }

        [Serialize(1f, IsPropertySaveable.No)]
        public float Probability { get; private set; }

        [Serialize(0f, IsPropertySaveable.No)]
        public float MinDamage { get; private set; }

        [Serialize(true, IsPropertySaveable.No)]
        public bool AllowToOverride { get; private set; }

        [Serialize(true, IsPropertySaveable.No)]
        public bool AllowToBeOverridden { get; private set; }

        public bool IsTriggered { get; private set; }

        public float Timer { get; private set; }

        public bool IsActive { get; private set; }

        public bool IsPermanent { get; private set; }

        public void Launch()
        {
            IsTriggered = true;
            IsActive = true;
            IsPermanent = Duration <= 0;
            if (!IsPermanent)
            {
                Timer = Duration;
            }
        }

        public void Reset()
        {
            IsTriggered = false;
            IsActive = false;
            Timer = 0;
        }

        public void UpdateTimer(float deltaTime)
        {
            if (IsPermanent) { return; }
            Timer -= deltaTime;
            if (Timer < 0)
            {
                Timer = 0;
                IsActive = false;
            }
        }

        public AITrigger(XElement element)
        {
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
        }
    }

    partial class StatusEffect
    {
        [Flags]
        public enum TargetType
        {
            This = 1,
            Parent = 2,
            Character = 4,
            Contained = 8,
            NearbyCharacters = 16,
            NearbyItems = 32,
            UseTarget = 64,
            Hull = 128,
            Limb = 256,
            AllLimbs = 512,
            LastLimb = 1024
        }

        class ItemSpawnInfo
        {
            public enum SpawnPositionType
            {
                This,
                //the inventory of the StatusEffect's target entity
                ThisInventory,
                //the same inventory the StatusEffect's target entity is in (only valid if the target is an Item)
                SameInventory,
                //the inventory of an item in the inventory of the StatusEffect's target entity (e.g. a container in the character's inventory)
                ContainedInventory
            }

            public enum SpawnRotationType
            {
                Fixed,
                Target,
                Limb,
                MainLimb,
                Collider,
                Random
            }

            public readonly ItemPrefab ItemPrefab;
            public readonly SpawnPositionType SpawnPosition;
            public readonly bool SpawnIfInventoryFull;
            /// <summary>
            /// Should the item spawn even if the container can't contain items of this type
            /// </summary>
            public readonly bool SpawnIfCantBeContained;
            public readonly float Speed;
            public readonly float Rotation;
            public readonly int Count;
            public readonly float Spread;
            public readonly SpawnRotationType RotationType;
            public readonly float AimSpread;
            public readonly bool Equip;

            public readonly float Condition;

            public ItemSpawnInfo(XElement element, string parentDebugName)
            {
                if (element.Attribute("name") != null)
                {
                    //backwards compatibility
                    DebugConsole.ThrowError("Error in StatusEffect config (" + element.ToString() + ") - use item identifier instead of the name.");
                    string itemPrefabName = element.GetAttributeString("name", "");
                    ItemPrefab = ItemPrefab.Prefabs.Find(m => m.NameMatches(itemPrefabName, StringComparison.InvariantCultureIgnoreCase) || m.Tags.Contains(itemPrefabName));
                    if (ItemPrefab == null)
                    {
                        DebugConsole.ThrowError("Error in StatusEffect \"" + parentDebugName + "\" - item prefab \"" + itemPrefabName + "\" not found.");
                    }
                }
                else
                {
                    string itemPrefabIdentifier = element.GetAttributeString("identifier", "");
                    if (string.IsNullOrEmpty(itemPrefabIdentifier)) itemPrefabIdentifier = element.GetAttributeString("identifiers", "");
                    if (string.IsNullOrEmpty(itemPrefabIdentifier))
                    {
                        DebugConsole.ThrowError("Invalid item spawn in StatusEffect \"" + parentDebugName + "\" - identifier not found in the element \"" + element.ToString() + "\"");
                    }
                    ItemPrefab = ItemPrefab.Prefabs.Find(m => m.Identifier == itemPrefabIdentifier);
                    if (ItemPrefab == null)
                    {
                        DebugConsole.ThrowError("Error in StatusEffect config - item prefab with the identifier \"" + itemPrefabIdentifier + "\" not found.");
                        return;
                    }
                }

                SpawnIfInventoryFull = element.GetAttributeBool("spawnifinventoryfull", false);
                SpawnIfCantBeContained = element.GetAttributeBool("spawnifcantbecontained", true);
                Speed = element.GetAttributeFloat("speed", 0.0f);

                Condition = MathHelper.Clamp(element.GetAttributeFloat("condition", 1.0f), 0.0f, 1.0f);

                Rotation = element.GetAttributeFloat("rotation", 0.0f);
                Count = element.GetAttributeInt("count", 1);
                Spread = element.GetAttributeFloat("spread", 0f);
                AimSpread = element.GetAttributeFloat("aimspread", 0f);
                Equip = element.GetAttributeBool("equip", false);

                string spawnTypeStr = element.GetAttributeString("spawnposition", "This");
                if (!Enum.TryParse(spawnTypeStr, ignoreCase: true, out SpawnPosition))
                {
                    DebugConsole.ThrowError("Error in StatusEffect config - \"" + spawnTypeStr + "\" is not a valid spawn position.");
                }
                string rotationTypeStr = element.GetAttributeString("rotationtype", Rotation != 0 ? "Fixed" : "Target");
                if (!Enum.TryParse(rotationTypeStr, ignoreCase: true, out RotationType))
                {
                    DebugConsole.ThrowError("Error in StatusEffect config - \"" + rotationTypeStr + "\" is not a valid rotation type.");
                }
            }
        }

        public class AbilityStatusEffectIdentifier : AbilityObject
        {
            public AbilityStatusEffectIdentifier(Identifier effectIdentifier)
            {
                EffectIdentifier = effectIdentifier;
            }
            public Identifier EffectIdentifier { get; set; }
        }

        public class GiveTalentInfo
        {
            public Identifier[] TalentIdentifiers;
            public bool GiveRandom;

            public GiveTalentInfo(XElement element, string _)
            {
                TalentIdentifiers = element.GetAttributeIdentifierArray("talentidentifiers", Array.Empty<Identifier>());
                GiveRandom = element.GetAttributeBool("giverandom", false);
            }
        }

        public class GiveSkill
        {
            public readonly Identifier SkillIdentifier;
            public readonly float Amount;

            public GiveSkill(XElement element, string parentDebugName)
            {
                SkillIdentifier = element.GetAttributeIdentifier("skillidentifier", Identifier.Empty);
                Amount = element.GetAttributeFloat("amount", 0);

                if (SkillIdentifier == Identifier.Empty)
                {
                    DebugConsole.ThrowError($"GiveSkill StatusEffect did not have a skill identifier defined in {parentDebugName}!");
                }
            }
        }

        public class CharacterSpawnInfo : ISerializableEntity
        {
            public string Name => $"Character Spawn Info ({SpeciesName})";
            public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; set; }

            [Serialize("", IsPropertySaveable.No)]
            public Identifier SpeciesName { get; private set; }

            [Serialize(1, IsPropertySaveable.No)]
            public int Count { get; private set; }

            [Serialize(0f, IsPropertySaveable.No)]
            public float Spread { get; private set; }

            [Serialize("0,0", IsPropertySaveable.No)]
            public Vector2 Offset { get; private set; }

            public CharacterSpawnInfo(XElement element, string parentDebugName)
            {
                SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
                if (SpeciesName.IsEmpty)
                {
                    DebugConsole.ThrowError($"Invalid character spawn ({Name}) in StatusEffect \"{parentDebugName}\" - identifier not found in the element \"{element}\"");
                }
            }
        }

        private readonly TargetType targetTypes;

        /// <summary>
        /// Index of the slot the target must be in when targeting a Contained item
        /// </summary>
        public int TargetSlot = -1;

        private readonly List<RelatedItem> requiredItems;

        public readonly Identifier[] propertyNames;
        public readonly object[] propertyEffects;

        private readonly PropertyConditional.Comparison conditionalComparison = PropertyConditional.Comparison.Or;
        private readonly List<PropertyConditional> propertyConditionals;
        public bool HasConditions => propertyConditionals != null && propertyConditionals.Any();

        private readonly bool setValue;

        private readonly bool disableDeltaTime;

        private readonly HashSet<string> tags;

        private readonly float duration;
        private readonly float lifeTime;
        private float lifeTimer;

        public float intervalTimer;

        public static readonly List<DurationListElement> DurationList = new List<DurationListElement>();

        /// <summary>
        /// Always do the conditional checks for the duration/delay. If false, only check conditional on apply.
        /// </summary>
        public readonly bool CheckConditionalAlways;

        /// <summary>
        /// Only valid if the effect has a duration or delay. Can the effect be applied on the same target(s)s if the effect is already being applied?
        /// </summary>
        public readonly bool Stackable = true;

        /// <summary>
        /// The interval at which the effect is executed. The difference between delay and interval is that effects with a delay find the targets, check the conditions, etc
        /// immediately when Apply is called, but don't apply the effects until the delay has passed. Effects with an interval check if the interval has passed when Apply is
        /// called and apply the effects if it has, otherwise they do nothing.
        /// </summary>
        public readonly float Interval;

#if CLIENT
        private readonly bool playSoundOnRequiredItemFailure = false;
#endif

        private readonly int useItemCount;

        private readonly bool removeItem, dropContainedItems, removeCharacter, breakLimb, hideLimb;
        private readonly float hideLimbTimer;

        public readonly ActionType type = ActionType.OnActive;

        public readonly List<Explosion> Explosions;

        private readonly List<ItemSpawnInfo> spawnItems;
        private readonly bool spawnItemRandomly;
        private readonly List<CharacterSpawnInfo> spawnCharacters;

        public readonly List<GiveTalentInfo> giveTalentInfos;

        private readonly List<AITrigger> aiTriggers;

        private readonly List<EventPrefab> triggeredEvents;
        private readonly Identifier triggeredEventTargetTag = "statuseffecttarget".ToIdentifier(), 
                                triggeredEventEntityTag = "statuseffectentity".ToIdentifier();

        private Character user;

        public readonly float FireSize;

        public readonly LimbType[] targetLimbs;

        public readonly float SeverLimbsProbability;

        public PhysicsBody sourceBody;

        public readonly bool OnlyInside;
        public readonly bool OnlyOutside;
        // Currently only used for OnDamaged. TODO: is there a better, more generic way to do this?
        public readonly bool OnlyPlayerTriggered;

        /// <summary>
        /// Can the StatusEffect be applied when the item applying it is broken
        /// </summary>
        public readonly bool AllowWhenBroken = false;

        public readonly ImmutableHashSet<Identifier> TargetIdentifiers;

        /// <summary>
        /// Which type of afflictions the target must receive for the StatusEffect to be applied. Only valid when the type of the effect is OnDamaged.
        /// </summary>
        private readonly HashSet<(Identifier affliction, float strength)> requiredAfflictions;

        public float AfflictionMultiplier = 1.0f;

        public List<Affliction> Afflictions
        {
            get;
            private set;
        }

        private readonly bool multiplyAfflictionsByMaxVitality;

        public IEnumerable<CharacterSpawnInfo> SpawnCharacters
        {
            get { return spawnCharacters; }
        }

        public readonly List<(Identifier AfflictionIdentifier, float ReduceAmount)> ReduceAffliction;

        private readonly List<Identifier> talentTriggers;
        private readonly List<int> giveExperiences;
        private readonly List<GiveSkill> giveSkills;

        public float Duration => duration;

        //only applicable if targeting NearbyCharacters or NearbyItems
        public float Range
        {
            get;
            private set;
        }

        public Vector2 Offset { get; private set; }

        public string Tags
        {
            get { return string.Join(",", tags); }
            set
            {
                tags.Clear();
                if (value == null) return;

                string[] newTags = value.Split(',');
                foreach (string tag in newTags)
                {
                    string newTag = tag.Trim();
                    if (!tags.Contains(newTag)) tags.Add(newTag);
                }
            }
        }

        public static StatusEffect Load(ContentXElement element, string parentDebugName)
        {
            if (element.GetAttribute("delay") != null || element.GetAttribute("delaytype") != null)
            {
                return new DelayedEffect(element, parentDebugName);
            }

            return new StatusEffect(element, parentDebugName);
        }

        protected StatusEffect(ContentXElement element, string parentDebugName)
        {
            requiredItems = new List<RelatedItem>();
            spawnItems = new List<ItemSpawnInfo>();
            spawnItemRandomly = element.GetAttributeBool("spawnitemrandomly", false);
            spawnCharacters = new List<CharacterSpawnInfo>();
            giveTalentInfos = new List<GiveTalentInfo>();
            aiTriggers = new List<AITrigger>();
            Afflictions = new List<Affliction>();
            Explosions = new List<Explosion>();
            triggeredEvents = new List<EventPrefab>();
            ReduceAffliction = new List<(Identifier affliction, float amount)>();
            talentTriggers = new List<Identifier>();
            giveExperiences = new List<int>();
            giveSkills = new List<GiveSkill>();
            multiplyAfflictionsByMaxVitality = element.GetAttributeBool("multiplyafflictionsbymaxvitality", false);

            tags = new HashSet<string>(element.GetAttributeString("tags", "").Split(','));
            OnlyInside = element.GetAttributeBool("onlyinside", false);
            OnlyOutside = element.GetAttributeBool("onlyoutside", false);
            OnlyPlayerTriggered = element.GetAttributeBool("onlyplayertriggered", false);
            AllowWhenBroken = element.GetAttributeBool("allowwhenbroken", false);

            TargetSlot = element.GetAttributeInt("targetslot", -1);

            Interval = element.GetAttributeFloat("interval", 0.0f);

            Range = element.GetAttributeFloat("range", 0.0f);
            Offset = element.GetAttributeVector2("offset", Vector2.Zero);
            string[] targetLimbNames = element.GetAttributeStringArray("targetlimb", null) ?? element.GetAttributeStringArray("targetlimbs", null);
            if (targetLimbNames != null)
            {
                List<LimbType> targetLimbs = new List<LimbType>();
                foreach (string targetLimbName in targetLimbNames)
                {
                    if (Enum.TryParse(targetLimbName, ignoreCase: true, out LimbType targetLimb)) { targetLimbs.Add(targetLimb); }
                }
                if (targetLimbs.Count > 0) { this.targetLimbs = targetLimbs.ToArray(); }
            }

            IEnumerable<XAttribute> attributes = element.Attributes();
            List<XAttribute> propertyAttributes = new List<XAttribute>();
            propertyConditionals = new List<PropertyConditional>();

            string[] targetTypesStr = 
                element.GetAttributeStringArray("target", null) ?? 
                element.GetAttributeStringArray("targettype", Array.Empty<string>());
            foreach (string s in targetTypesStr)
            {
                if (!Enum.TryParse(s, true, out TargetType targetType))
                {
                    DebugConsole.ThrowError("Invalid target type \"" + s + "\" in StatusEffect (" + parentDebugName + ")");
                }
                else
                {
                    targetTypes |= targetType;
                }
            }

            foreach (XAttribute attribute in attributes)
            {
                switch (attribute.Name.ToString().ToLowerInvariant())
                {
                    case "type":
                        if (!Enum.TryParse(attribute.Value, true, out type))
                        {
                            DebugConsole.ThrowError("Invalid action type \"" + attribute.Value + "\" in StatusEffect (" + parentDebugName + ")");
                        }
                        break;
                    case "targettype":
                    case "target":
                        break;
                    case "disabledeltatime":
                        disableDeltaTime = attribute.GetAttributeBool(false);
                        break;
                    case "setvalue":
                        setValue = attribute.GetAttributeBool(false);
                        break;
                    case "severlimbs":
                    case "severlimbsprobability":
                        SeverLimbsProbability = MathHelper.Clamp(attribute.GetAttributeFloat(0.0f), 0.0f, 1.0f);
                        break;
                    case "targetnames":
                    case "targets":
                    case "targetidentifiers":
                    case "targettags":
                        TargetIdentifiers = attribute.Value.Split(',').ToIdentifiers().ToImmutableHashSet();
                        break;
                    case "allowedafflictions":
                    case "requiredafflictions":
                        string[] types = attribute.Value.Split(',');
                        requiredAfflictions ??= new HashSet<(Identifier, float)>();
                        for (int i = 0; i < types.Length; i++)
                        {
                            requiredAfflictions.Add((types[i].Trim().ToIdentifier(), 0.0f));
                        }
                        break;
                    case "duration":
                        duration = attribute.GetAttributeFloat(0.0f);
                        break;
                    case "stackable":
                        Stackable = attribute.GetAttributeBool(true);
                        break;
                    case "lifetime":
                        lifeTime = attribute.GetAttributeFloat(0);
                        lifeTimer = lifeTime;
                        break;
                    case "eventtargettag":
                        triggeredEventTargetTag = attribute.Value.ToIdentifier();
                        break;
                    case "evententitytag":
                        triggeredEventEntityTag = attribute.Value.ToIdentifier();
                        break;
                    case "checkconditionalalways":
                        CheckConditionalAlways = attribute.GetAttributeBool(false);
                        break;
                    case "conditionalcomparison":
                    case "comparison":
                        if (!Enum.TryParse(attribute.Value, ignoreCase: true, out conditionalComparison))
                        {
                            DebugConsole.ThrowError("Invalid conditional comparison type \"" + attribute.Value + "\" in StatusEffect (" + parentDebugName + ")");
                        }
                        break;
#if CLIENT
                    case "playsoundonrequireditemfailure":
                        playSoundOnRequiredItemFailure = attribute.GetAttributeBool(false);
                        break;
#endif
                    case "sound":
                        DebugConsole.ThrowError("Error in StatusEffect " + element.Parent.Name.ToString() +
                            " - sounds should be defined as child elements of the StatusEffect, not as attributes.");
                        break;
                    case "delay":
                    case "interval":
                        break;
                    case "range":
                        if (!HasTargetType(TargetType.NearbyCharacters) && !HasTargetType(TargetType.NearbyItems))
                        {
                            propertyAttributes.Add(attribute);
                        }
                        break;
                    default:
                        propertyAttributes.Add(attribute);
                        break;
                }
            }

            if (duration > 0.0f && !setValue)
            {
                //a workaround to "tags" possibly meaning either an item's tags or this status effect's tags:
                //if the status effect has a duration, assume tags mean this status effect's tags and leave item tags untouched.
                propertyAttributes.RemoveAll(a => a.Name.ToString().Equals("tags", StringComparison.OrdinalIgnoreCase));
            }

            int count = propertyAttributes.Count;

            propertyNames = new Identifier[count];
            propertyEffects = new object[count];

            int n = 0;
            foreach (XAttribute attribute in propertyAttributes)
            {

                propertyNames[n] = attribute.NameAsIdentifier();
                propertyEffects[n] = XMLExtensions.GetAttributeObject(attribute);
                n++;
            }

            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "explosion":
                        Explosions.Add(new Explosion(subElement, parentDebugName));
                        break;
                    case "fire":
                        FireSize = subElement.GetAttributeFloat("size", 10.0f);
                        break;
                    case "use":
                    case "useitem":
                        useItemCount++;
                        break;
                    case "remove":
                    case "removeitem":
                        removeItem = true;
                        break;
                    case "dropcontaineditems":
                        dropContainedItems = true;
                        break;
                    case "removecharacter":
                        removeCharacter = true;
                        break;
                    case "breaklimb":
                        breakLimb = true;
                        break;
                    case "hidelimb":
                        hideLimb = true;
                        hideLimbTimer = subElement.GetAttributeFloat("duration", 0);
                        break;
                    case "requireditem":
                    case "requireditems":
                        RelatedItem newRequiredItem = RelatedItem.Load(subElement, returnEmpty: false, parentDebugName: parentDebugName);
                        if (newRequiredItem == null)
                        {
                            DebugConsole.ThrowError("Error in StatusEffect config - requires an item with no identifiers.");
                            continue;
                        }
                        requiredItems.Add(newRequiredItem);
                        break;
                    case "requiredaffliction":
                        requiredAfflictions ??= new HashSet<(Identifier, float)>();
                        Identifier[] ids = subElement.GetAttributeIdentifierArray("identifier", null) ?? subElement.GetAttributeIdentifierArray("type", Array.Empty<Identifier>());
                        foreach (var afflictionId in ids)
                        {
                            requiredAfflictions.Add((
                                afflictionId,
                                subElement.GetAttributeFloat("minstrength", 0.0f)));
                        }
                        break;
                    case "conditional":
                        foreach (XAttribute attribute in subElement.Attributes())
                        {
                            if (PropertyConditional.IsValid(attribute))
                            {
                                propertyConditionals.Add(new PropertyConditional(attribute));
                            }
                        }
                        break;
                    case "affliction":
                        AfflictionPrefab afflictionPrefab;
                        if (subElement.GetAttribute("name") != null)
                        {
                            DebugConsole.ThrowError("Error in StatusEffect (" + parentDebugName + ") - define afflictions using identifiers instead of names.");
                            string afflictionName = subElement.GetAttributeString("name", "");
                            afflictionPrefab = AfflictionPrefab.List.FirstOrDefault(ap => ap.Name.Equals(afflictionName, StringComparison.OrdinalIgnoreCase));
                            if (afflictionPrefab == null)
                            {
                                DebugConsole.ThrowError("Error in StatusEffect (" + parentDebugName + ") - Affliction prefab \"" + afflictionName + "\" not found.");
                                continue;
                            }
                        }
                        else
                        {
                            Identifier afflictionIdentifier = subElement.GetAttributeIdentifier("identifier", "");
                            afflictionPrefab = AfflictionPrefab.List.FirstOrDefault(ap => ap.Identifier == afflictionIdentifier);
                            if (afflictionPrefab == null)
                            {
                                DebugConsole.ThrowError("Error in StatusEffect (" + parentDebugName + ") - Affliction prefab with the identifier \"" + afflictionIdentifier + "\" not found.");
                                continue;
                            }
                        }

                        Affliction afflictionInstance = afflictionPrefab.Instantiate(subElement.GetAttributeFloat(1.0f, "amount", "strength"));
                        afflictionInstance.Probability = subElement.GetAttributeFloat(1.0f, "probability");
                        Afflictions.Add(afflictionInstance);

                        break;
                    case "reduceaffliction":
                        if (subElement.GetAttribute("name") != null)
                        {
                            DebugConsole.ThrowError("Error in StatusEffect (" + parentDebugName + ") - define afflictions using identifiers or types instead of names.");
                            ReduceAffliction.Add((
                                subElement.GetAttributeIdentifier("name", ""),
                                subElement.GetAttributeFloat(1.0f, "amount", "strength", "reduceamount")));
                        }
                        else
                        {
                            Identifier name = subElement.GetAttributeIdentifier("identifier", subElement.GetAttributeIdentifier("type", Identifier.Empty));

                            if (AfflictionPrefab.List.Any(ap => ap.Identifier == name || ap.AfflictionType == name))
                            {
                                ReduceAffliction.Add((name, subElement.GetAttributeFloat(1.0f, "amount", "strength", "reduceamount")));
                            }
                            else
                            {
                                DebugConsole.ThrowError("Error in StatusEffect (" + parentDebugName + ") - Affliction prefab with the identifier or type \"" + name + "\" not found.");
                            }
                        }
                        break;
                    case "spawnitem":
                        var newSpawnItem = new ItemSpawnInfo(subElement, parentDebugName);
                        if (newSpawnItem.ItemPrefab != null) { spawnItems.Add(newSpawnItem); }
                        break;
                    case "triggerevent":
                        Identifier identifier = subElement.GetAttributeIdentifier("identifier", Identifier.Empty);
                        if (!identifier.IsEmpty)
                        {
                            EventPrefab prefab = EventSet.GetEventPrefab(identifier);
                            if (prefab != null)
                            {
                                triggeredEvents.Add(prefab);
                            }
                        }
                        foreach (var eventElement in subElement.Elements())
                        {
                            if (eventElement.NameAsIdentifier() != "ScriptedEvent") { continue; }
                            triggeredEvents.Add(new EventPrefab(eventElement, file: null));
                        }
                        break;
                    case "spawncharacter":
                        var newSpawnCharacter = new CharacterSpawnInfo(subElement, parentDebugName);
                        if (!newSpawnCharacter.SpeciesName.IsEmpty) { spawnCharacters.Add(newSpawnCharacter); }
                        break;
                    case "givetalentinfo":
                        var newGiveTalentInfo = new GiveTalentInfo(subElement, parentDebugName);
                        if (newGiveTalentInfo.TalentIdentifiers.Any()) { giveTalentInfos.Add(newGiveTalentInfo); }
                        break;
                    case "aitrigger":
                        aiTriggers.Add(new AITrigger(subElement));
                        break;
                    case "talenttrigger":
                        talentTriggers.Add(subElement.GetAttributeIdentifier("effectidentifier", Identifier.Empty));
                        break;
                    case "giveexperience":
                        giveExperiences.Add(subElement.GetAttributeInt("amount", 0));
                        break;
                    case "giveskill":
                        giveSkills.Add(new GiveSkill(subElement, parentDebugName));
                        break;
                }
            }
            InitProjSpecific(element, parentDebugName);
        }

        partial void InitProjSpecific(ContentXElement element, string parentDebugName);

        public bool HasTargetType(TargetType targetType)
        {
            return (targetTypes & targetType) != 0;
        }

        public bool ReducesItemCondition()
        {
            for (int i = 0; i < propertyNames.Length; i++)
            {
                if (propertyNames[i] != "condition") { continue; }
                object propertyEffect = propertyEffects[i];
                if (propertyEffect.GetType() == typeof(float))
                {
                    return (float)propertyEffect < 0.0f || (setValue && (float)propertyEffect <= 0.0f);
                }
            }
            return false;
        }

        public bool IncreasesItemCondition()
        {
            for (int i = 0; i < propertyNames.Length; i++)
            {
                if (propertyNames[i] != "condition") { continue; }
                object propertyEffect = propertyEffects[i];
                if (propertyEffect.GetType() == typeof(float))
                {
                    return (float)propertyEffect > 0.0f || (setValue && (float)propertyEffect > 0.0f);
                }
            }
            return false;
        }

        public bool MatchesTagConditionals(ItemPrefab itemPrefab)
        {
            if (itemPrefab == null || !HasConditions)
            {
                return false;
            }
            else
            {
                return itemPrefab.Tags.Any(t => propertyConditionals.Any(pc => pc.MatchesTagCondition(t)));
            }
        }

        public bool HasRequiredAfflictions(AttackResult attackResult)
        {
            if (requiredAfflictions == null) { return true; }
            if (attackResult.Afflictions == null) { return false; }
            if (attackResult.Afflictions.None(a => requiredAfflictions.Any(a2 => a.Strength >= a2.strength && (a.Identifier == a2.affliction || a.Prefab.AfflictionType == a2.affliction))))
            {
                return false;
            }
            return true;
        }

        public virtual bool HasRequiredItems(Entity entity)
        {
            if (entity == null) { return true; }
            foreach (RelatedItem requiredItem in requiredItems)
            {
                if (entity is Item item)
                {
                    if (!requiredItem.CheckRequirements(null, item)) { return false; }
                }
                else if (entity is Character character)
                {
                    if (!requiredItem.CheckRequirements(character, null)) { return false; }
                }
            }
            return true;
        }

        public IReadOnlyList<ISerializableEntity> GetNearbyTargets(Vector2 worldPosition, List<ISerializableEntity> targets = null)
        {
            targets ??= new List<ISerializableEntity>();
            if (Range <= 0.0f) { return targets; }
            if (HasTargetType(TargetType.NearbyCharacters))
            {
                foreach (Character c in Character.CharacterList)
                {
                    if (c.Enabled && !c.Removed && CheckDistance(c) && IsValidTarget(c))
                    {
                        targets.Add(c);
                    }
                }
            }
            if (HasTargetType(TargetType.NearbyItems))
            {
                //optimization for powered components that can be easily fetched from Powered.PoweredList
                if (TargetIdentifiers.Count == 1 &&
                    (TargetIdentifiers.Contains("powered") || TargetIdentifiers.Contains("junctionbox") || TargetIdentifiers.Contains("relaycomponent")))
                {
                    foreach (Powered powered in Powered.PoweredList)
                    {
                        Item item = powered.Item;
                        if (!item.Removed && CheckDistance(item) && IsValidTarget(item))
                        {
                            targets.AddRange(item.AllPropertyObjects);
                        }
                    }
                }
                else
                {
                    foreach (Item item in Item.ItemList)
                    {
                        if (!item.Removed && CheckDistance(item) && IsValidTarget(item))
                        {
                            targets.AddRange(item.AllPropertyObjects);
                        }
                    }
                }
            }
            return targets;

            bool CheckDistance(ISpatialEntity e)
            {
                float xDiff = Math.Abs(e.WorldPosition.X - worldPosition.X);
                if (xDiff > Range) { return false; }
                float yDiff = Math.Abs(e.WorldPosition.Y - worldPosition.Y);
                if (yDiff > Range) { return false; }
                if (xDiff * xDiff + yDiff * yDiff < Range * Range)
                {
                    return true;
                }
                return false;
            }
        }

        public bool HasRequiredConditions(IReadOnlyList<ISerializableEntity> targets)
        {
            return HasRequiredConditions(targets, propertyConditionals);
        }

        private bool HasRequiredConditions(IReadOnlyList<ISerializableEntity> targets, IReadOnlyList<PropertyConditional> conditionals, bool targetingContainer = false)
        {
            if (conditionals.Count == 0) { return true; }
            if (targets.Count == 0 && requiredItems.Count > 0 && requiredItems.All(ri => ri.MatchOnEmpty)) { return true; }
            switch (conditionalComparison)
            {
                case PropertyConditional.Comparison.Or:
                    for (int i = 0; i < conditionals.Count; i++)
                    {
                        var pc = conditionals[i];
                        if (pc.TargetContainer && !targetingContainer)
                        {
                            var target = FindTargetItemOrComponent(targets);
                            var targetItem = target as Item ?? (target as ItemComponent)?.Item;
                            if (targetItem?.ParentInventory == null) 
                            {
                                //if we're checking for inequality, not being inside a valid container counts as success
                                //(not inside a container = the container doesn't have a specific tag/value)
                                if (pc.Operator == PropertyConditional.OperatorType.NotEquals)
                                {
                                    return true;
                                }
                                continue; 
                            }
                            var owner = targetItem.ParentInventory.Owner;
                            if (pc.TargetGrandParent && owner is Item ownerItem)
                            {
                                owner = ownerItem.ParentInventory?.Owner;
                            }
                            if (owner is Item container) 
                            { 
                                if (pc.Type == PropertyConditional.ConditionType.HasTag)
                                {
                                    //if we're checking for tags, just check the Item object, not the ItemComponents
                                    if (pc.Matches(container)) { return true; }
                                }
                                else
                                {
                                    if (AnyTargetMatches(container.AllPropertyObjects, pc.TargetItemComponentName, pc)) { return true; } 
                                }                                
                            }
                            if (owner is Character character && pc.Matches(character)) { return true; }                            
                        }
                        else
                        {
                            if (AnyTargetMatches(targets, pc.TargetItemComponentName, pc)) { return true; }                            
                        }
                    }
                    return false;
                case PropertyConditional.Comparison.And:
                    for (int i = 0; i < conditionals.Count; i++)
                    {
                        var pc = conditionals[i];
                        if (pc.TargetContainer && !targetingContainer)
                        {
                            var target = FindTargetItemOrComponent(targets);
                            var targetItem = target as Item ?? (target as ItemComponent)?.Item;
                            if (targetItem?.ParentInventory == null) 
                            {
                                //if we're checking for inequality, not being inside a valid container counts as success
                                //(not inside a container = the container doesn't have a specific tag/value)
                                if (pc.Operator == PropertyConditional.OperatorType.NotEquals)
                                {
                                    continue;
                                }
                                return false; 
                            }
                            var owner = targetItem.ParentInventory.Owner;
                            if (pc.TargetGrandParent && owner is Item ownerItem)
                            {
                                owner = ownerItem.ParentInventory?.Owner;
                            }
                            if (owner is Item container)
                            {
                                if (pc.Type == PropertyConditional.ConditionType.HasTag)
                                {
                                    //if we're checking for tags, just check the Item object, not the ItemComponents
                                    if (!pc.Matches(container)) { return false; }
                                }
                                else
                                {
                                    if (!AnyTargetMatches(container.AllPropertyObjects, pc.TargetItemComponentName, pc)) { return false; }
                                }
                            }
                            if (owner is Character character && !pc.Matches(character)) { return false; }
                        }
                        else
                        {
                            if (!AnyTargetMatches(targets, pc.TargetItemComponentName, pc)) { return false; }
                        }
                    }
                    return true;
                default:
                    throw new NotImplementedException();
            }

            static bool AnyTargetMatches(IReadOnlyList<ISerializableEntity> targets, string targetItemComponentName, PropertyConditional conditional)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    if (!string.IsNullOrEmpty(targetItemComponentName))
                    {
                        if (!(targets[i] is ItemComponent ic) || ic.Name != targetItemComponentName) { continue; }
                    }
                    if (conditional.Matches(targets[i]))
                    {
                        return true;
                    }
                }
                return false;
            }

            static ISerializableEntity FindTargetItemOrComponent(IReadOnlyList<ISerializableEntity> targets)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    if (targets[i] is Item || targets[i] is ItemComponent) { return targets[i]; }
                }
                return null;
            }
        }

        protected bool IsValidTarget(ISerializableEntity entity)
        {
            if (entity is Item item)
            {
                return IsValidTarget(item);
            }
            else if (entity is ItemComponent itemComponent)
            {
                return IsValidTarget(itemComponent);
            }
            else if (entity is Structure structure)
            {
                if (TargetIdentifiers == null) { return true; }
                if (TargetIdentifiers.Contains("structure")) { return true; }
                if (TargetIdentifiers.Contains(structure.Prefab.Identifier)) { return true; }
            }
            else if (entity is Character character)
            {
                return IsValidTarget(character);
            }
            if (TargetIdentifiers == null) { return true; }
            return TargetIdentifiers.Contains(entity.Name);
        }

        protected bool IsValidTarget(ItemComponent itemComponent)
        {
            if (OnlyInside && itemComponent.Item.CurrentHull == null) { return false; }
            if (OnlyOutside && itemComponent.Item.CurrentHull != null) { return false; }
            if (TargetIdentifiers == null) { return true; }
            if (TargetIdentifiers.Contains("itemcomponent")) { return true; }
            if (itemComponent.Item.HasTag(TargetIdentifiers)) { return true; }
            return TargetIdentifiers.Contains(itemComponent.Item.Prefab.Identifier);
        }

        protected bool IsValidTarget(Item item)
        {
            if (OnlyInside && item.CurrentHull == null) { return false; }
            if (OnlyOutside && item.CurrentHull != null) { return false; }
            if (TargetIdentifiers == null) { return true; }
            if (TargetIdentifiers.Contains("item")) { return true; }
            if (item.HasTag(TargetIdentifiers)) { return true; }
            return TargetIdentifiers.Contains(item.Prefab.Identifier);
        }

        protected bool IsValidTarget(Character character)
        {
            if (OnlyInside && character.CurrentHull == null) { return false; }
            if (OnlyOutside && character.CurrentHull != null) { return false; }
            if (TargetIdentifiers == null) { return true; }
            if (TargetIdentifiers.Contains("character")) { return true; }
            return TargetIdentifiers.Contains(character.SpeciesName);
        }

        public void SetUser(Character user)
        {
            this.user = user;
            foreach (Affliction affliction in Afflictions)
            {
                affliction.Source = user;
            }
        }

        public virtual void Apply(ActionType type, float deltaTime, Entity entity, ISerializableEntity target, Vector2? worldPosition = null)
        {
            if (this.type != type || !HasRequiredItems(entity)) { return; }

            if (!IsValidTarget(target)) { return; }

            if (duration > 0.0f && !Stackable)
            {
                //ignore if not stackable and there's already an identical statuseffect
                DurationListElement existingEffect = DurationList.Find(d => d.Parent == this && d.Targets.FirstOrDefault() == target);
                if (existingEffect != null)
                {
                    existingEffect.Reset(Math.Max(existingEffect.Timer, duration), user);
                    return;
                }
            }

            currentTargets.Clear();
            currentTargets.Add(target);
            if (!HasRequiredConditions(currentTargets)) { return; }
            Apply(deltaTime, entity, currentTargets, worldPosition);
        }

        protected readonly List<ISerializableEntity> currentTargets = new List<ISerializableEntity>();
        public virtual void Apply(ActionType type, float deltaTime, Entity entity, IReadOnlyList<ISerializableEntity> targets, Vector2? worldPosition = null)
        {
            if (this.type != type) { return; }

            if (intervalTimer > 0.0f)
            {
                intervalTimer -= deltaTime;
                return;
            }

            currentTargets.Clear();
            foreach (ISerializableEntity target in targets)
            {
                if (!IsValidTarget(target)) { continue; }
                currentTargets.Add(target);
            }

            if (TargetIdentifiers != null && currentTargets.Count == 0) { return; }

            bool hasRequiredItems = HasRequiredItems(entity);
            if (!hasRequiredItems || !HasRequiredConditions(currentTargets))
            {
#if CLIENT
                if (!hasRequiredItems && playSoundOnRequiredItemFailure)
                {
                    PlaySound(entity, GetHull(entity), GetPosition(entity, targets, worldPosition));
                }
#endif
                return; 
            }

            if (duration > 0.0f && !Stackable)
            {
                //ignore if not stackable and there's already an identical statuseffect
                DurationListElement existingEffect = DurationList.Find(d => d.Parent == this && d.Targets.SequenceEqual(currentTargets));
                if (existingEffect != null)
                {
                    existingEffect?.Reset(Math.Max(existingEffect.Timer, duration), user);
                    return;
                }
            }

            Apply(deltaTime, entity, currentTargets, worldPosition);
        }

        private Hull GetHull(Entity entity)
        {
            Hull hull = null;
            if (entity is Character character)
            {
                hull = character.AnimController.CurrentHull;
            }
            else if (entity is Item item)
            {
                hull = item.CurrentHull;
            }
            return hull;
        }

        private Vector2 GetPosition(Entity entity, IReadOnlyList<ISerializableEntity> targets, Vector2? worldPosition = null)
        {
            Vector2 position = worldPosition ?? (entity == null || entity.Removed ? Vector2.Zero : entity.WorldPosition);
            if (worldPosition == null)
            {
                if (entity is Character character && !character.Removed && targetLimbs != null)
                {
                    foreach (var targetLimbType in targetLimbs)
                    {
                        Limb limb = character.AnimController.GetLimb(targetLimbType);
                        if (limb != null && !limb.Removed)
                        {
                            position = limb.WorldPosition;
                            break;
                        }
                    }
                }
                else if (HasTargetType(TargetType.Contained))
                {
                    for (int i = 0; i < targets.Count; i++)
                    {
                        if (targets[i] is Item targetItem)
                        {
                            position = targetItem.WorldPosition;
                            break;
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < targets.Count; i++)
                    {
                        if (targets[i] is Limb targetLimb && !targetLimb.Removed)
                        {
                            position = targetLimb.WorldPosition;
                            break;
                        }
                    }
                }
                
            }
            position += Offset;
            return position;
        }

        protected void Apply(float deltaTime, Entity entity, IReadOnlyList<ISerializableEntity> targets, Vector2? worldPosition = null)
        {
            if (lifeTime > 0)
            {
                lifeTimer -= deltaTime;
                if (lifeTimer <= 0) { return; }
            }
            if (intervalTimer > 0.0f)
            {
                intervalTimer -= deltaTime;
                return;
            }
            Hull hull = GetHull(entity);
            Vector2 position = GetPosition(entity, targets, worldPosition);
            if (useItemCount > 0)
            {
                Character useTargetCharacter = null;
                Limb useTargetLimb = null;
                for (int i = 0; i < targets.Count; i++)
                {
                    if (targets[i] is Character character && !character.Removed)
                    {
                        useTargetCharacter = character;
                        break;
                    }
                    else if (targets[i] is Limb limb && limb.character != null && !limb.character.Removed)
                    {
                        useTargetLimb = limb;
                        useTargetCharacter ??= limb.character;
                        break;
                    }
                }
                for (int i = 0; i < targets.Count; i++)
                {
                    if (!(targets[i] is Item item)) { continue; }                
                    for (int j = 0; j < useItemCount; j++)
                    {
                        if (item.Removed) { continue; }
                        item.Use(deltaTime, useTargetCharacter, useTargetLimb);
                    }
                }
            }

            if (dropContainedItems)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    if (targets[i] is Item item) 
                    { 
                        foreach (var itemContainer in item.GetComponents<ItemContainer>())
                        {
                            foreach (var containedItem in itemContainer.Inventory.AllItemsMod)
                            {
                                containedItem.Drop(dropper: null);
                            }
                        }
                    }
                }
            }
            if (removeItem)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    if (targets[i] is Item item) { Entity.Spawner?.AddItemToRemoveQueue(item); }
                }
            }
            if (removeCharacter)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    var target = targets[i];
                    if (target is Character character) 
                    { 
                        Entity.Spawner?.AddEntityToRemoveQueue(character); 
                    }
                    else if (target is Limb limb)
                    {
                        Entity.Spawner?.AddEntityToRemoveQueue(limb.character);
                    }
                }
            }
            if (breakLimb || hideLimb)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    var target = targets[i];
                    Limb targetLimb = target as Limb;
                    if (targetLimb == null && target is Character character)
                    {
                        foreach (Limb limb in character.AnimController.Limbs)
                        {
                            if (limb.body == sourceBody)
                            {
                                targetLimb = limb;
                                if (breakLimb)
                                {
                                    character.TrySeverLimbJoints(limb, severLimbsProbability: 100, damage: 100, allowBeheading: true, attacker: user);
                                }
                                break;
                            }
                        }
                    }
                    if (hideLimb)
                    {
                        targetLimb?.HideAndDisable(hideLimbTimer);
                    }
                }
            }

            if (duration > 0.0f)
            {
                DurationList.Add(new DurationListElement(this, entity, targets, duration, user));
            }
            else
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    var target = targets[i];
                    if (target == null) { continue; }
                    if (target is Entity targetEntity)
                    {
                        if (targetEntity.Removed) { continue; }
                    }
                    else if (target is Limb limb)
                    {
                        if (limb.Removed) { continue; }
                        position = limb.WorldPosition + Offset;
                    }

                    for (int j = 0; j < propertyNames.Length; j++)
                    {
                        if (target == null || target.SerializableProperties == null || !target.SerializableProperties.TryGetValue(propertyNames[j], out SerializableProperty property))
                        {
                            continue;
                        }
                        ApplyToProperty(target, property, j, deltaTime);
                    }
                }
            }

            foreach (Explosion explosion in Explosions)
            {
                explosion.Explode(position, damageSource: entity, attacker: user);
            }

            bool isNotClient = GameMain.NetworkMember == null || !GameMain.NetworkMember.IsClient;

            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                //if the effect has a duration, these will be done in the UpdateAll method
                if (duration > 0) { break; }
                if (target == null) { continue; }
                foreach (Affliction affliction in Afflictions)
                {
                    if (Rand.Value(Rand.RandSync.Unsynced) > affliction.Probability) { continue; }
                    Affliction newAffliction = affliction;
                    if (target is Character character)
                    {
                        if (character.Removed) { continue; }
                        newAffliction = GetMultipliedAffliction(affliction, entity, character, deltaTime, multiplyAfflictionsByMaxVitality);
                        character.LastDamageSource = entity;
                        foreach (Limb limb in character.AnimController.Limbs)
                        {
                            if (limb.Removed) { continue; }
                            if (limb.IsSevered) { continue; }
                            if (targetLimbs != null && !targetLimbs.Contains(limb.type)) { continue; }
                            AttackResult result = limb.character.DamageLimb(position, limb, newAffliction.ToEnumerable(), stun: 0.0f, playSound: false, attackImpulse: 0.0f, attacker: affliction.Source, allowStacking: !setValue);
                            limb.character.TrySeverLimbJoints(limb, SeverLimbsProbability, disableDeltaTime ? result.Damage : result.Damage / deltaTime, allowBeheading: true, attacker: affliction.Source);
                            RegisterTreatmentResults(entity, limb, affliction, result);
                            //only apply non-limb-specific afflictions to the first limb
                            if (!affliction.Prefab.LimbSpecific) { break; }
                        }
                    }
                    else if (target is Limb limb)
                    {
                        if (limb.IsSevered) { continue; }
                        if (limb.character.Removed || limb.Removed) { continue; }
                        newAffliction = GetMultipliedAffliction(affliction, entity, limb.character, deltaTime, multiplyAfflictionsByMaxVitality);
                        AttackResult result = limb.character.DamageLimb(position, limb, newAffliction.ToEnumerable(), stun: 0.0f, playSound: false, attackImpulse: 0.0f, attacker: affliction.Source, allowStacking: !setValue);
                        limb.character.TrySeverLimbJoints(limb, SeverLimbsProbability, disableDeltaTime ? result.Damage : result.Damage / deltaTime, allowBeheading: true, attacker: affliction.Source);
                        RegisterTreatmentResults(entity, limb, affliction, result);
                    }
                }
                
                foreach (var (affliction, amount) in ReduceAffliction)
                {
                    Limb targetLimb = null;
                    Character targetCharacter = null;
                    if (target is Character character)
                    {
                        targetCharacter = character;
                    }
                    else if (target is Limb limb && !limb.Removed)
                    {
                        targetLimb = limb;
                        targetCharacter = limb.character;
                    }
                    if (targetCharacter != null && !targetCharacter.Removed)
                    {
                        ActionType? actionType = null;
                        if (entity is Item item && item.UseInHealthInterface) { actionType = type; }
                        float reduceAmount = amount * GetAfflictionMultiplier(entity, targetCharacter, deltaTime);
                        float prevVitality = targetCharacter.Vitality;
                        if (targetLimb != null)
                        {
                            targetCharacter.CharacterHealth.ReduceAfflictionOnLimb(targetLimb, affliction, reduceAmount, treatmentAction: actionType);
                        }
                        else
                        {
                            targetCharacter.CharacterHealth.ReduceAfflictionOnAllLimbs(affliction, reduceAmount, treatmentAction: actionType);
                        }
                        targetCharacter.AIController?.OnHealed(healer: user, targetCharacter.Vitality - prevVitality);
                        if (user != null && user != targetCharacter)
                        {
                            if (!targetCharacter.IsDead)
                            {
                                targetCharacter.TryAdjustAttackerSkill(user, targetCharacter.Vitality - prevVitality);
                            }
                        };
#if SERVER
                        GameMain.Server.KarmaManager.OnCharacterHealthChanged(targetCharacter, user, prevVitality - targetCharacter.Vitality, 0.0f);
#endif
                    }
                }

                if (aiTriggers.Count > 0)
                {
                    Character targetCharacter = target as Character;
                    if (targetCharacter == null)
                    {
                        if (target is Limb targetLimb && !targetLimb.Removed)
                        {
                            targetCharacter = targetLimb.character;
                        }
                    }
                    if (targetCharacter != null && !targetCharacter.Removed && !targetCharacter.IsPlayer)
                    {
                        if (targetCharacter.AIController is EnemyAIController enemyAI)
                        {
                            foreach (AITrigger trigger in aiTriggers)
                            {
                                if (Rand.Value(Rand.RandSync.Unsynced) > trigger.Probability) { continue; }
                                if (target is Limb targetLimb && targetCharacter.LastDamage.HitLimb != targetLimb) { continue; }
                                if (targetCharacter.LastDamage.Damage < trigger.MinDamage) { continue; }
                                enemyAI.LaunchTrigger(trigger);
                                break;
                            }
                        }
                    }
                }

                if (talentTriggers.Count > 0)
                {
                    Character targetCharacter = CharacterFromTarget(target);
                    if (targetCharacter != null && !targetCharacter.Removed)
                    {
                        foreach (Identifier talentTrigger in talentTriggers)
                        {
                            targetCharacter.CheckTalents(AbilityEffectType.OnStatusEffectIdentifier, new AbilityStatusEffectIdentifier(talentTrigger));
                        }
                    }
                }

                if (isNotClient)
                {
                    // these effects do not need to be run clientside, as they are replicated from server to clients anyway
                    foreach (int giveExperience in giveExperiences)
                    {
                        Character targetCharacter = CharacterFromTarget(target);
                        if (targetCharacter != null && !targetCharacter.Removed)
                        {
                            targetCharacter?.Info?.GiveExperience(giveExperience);
                        }
                    }

                    if (giveSkills.Count > 0)
                    {
                        foreach (GiveSkill giveSkill in giveSkills)
                        {
                            Character targetCharacter = CharacterFromTarget(target);
                            if (targetCharacter != null && !targetCharacter.Removed)
                            {
                                Identifier skillIdentifier = giveSkill.SkillIdentifier == "randomskill" ? GetRandomSkill() : giveSkill.SkillIdentifier;

                                targetCharacter.Info?.IncreaseSkillLevel(skillIdentifier, giveSkill.Amount);

                                Identifier GetRandomSkill()
                                {
                                    return targetCharacter.Info?.Job?.GetSkills().GetRandomUnsynced()?.Identifier ?? Identifier.Empty;
                                }
                            }
                        }
                    }

                    if (giveTalentInfos.Count > 0)
                    {
                        Character targetCharacter = CharacterFromTarget(target);
                        if (targetCharacter?.Info == null) { continue; }
                        if (!TalentTree.JobTalentTrees.TryGet(targetCharacter.Info.Job.Prefab.Identifier, out TalentTree talentTree)) { continue; }
                        // for the sake of technical simplicity, for now do not allow talents to be given if the character could unlock them in their talent tree as well
                        IEnumerable<Identifier> disallowedTalents = talentTree.TalentSubTrees.SelectMany(s => s.TalentOptionStages.SelectMany(o => o.Talents.Select(t => t.Identifier)));

                        foreach (GiveTalentInfo giveTalentInfo in giveTalentInfos)
                        {
                            IEnumerable<Identifier> viableTalents = giveTalentInfo.TalentIdentifiers.Where(s => !targetCharacter.Info.UnlockedTalents.Contains(s) && !disallowedTalents.Contains(s));
                            if (viableTalents.None()) { continue; }

                            if (giveTalentInfo.GiveRandom)
                            {
                                targetCharacter.GiveTalent(viableTalents.GetRandomUnsynced(), true);
                            }
                            else
                            {
                                foreach (Identifier talent in viableTalents)
                                {
                                    targetCharacter.GiveTalent(talent, true);
                                }
                            }
                        }
                    }
                }
            }

            if (FireSize > 0.0f && entity != null)
            {
                var fire = new FireSource(position, hull);
                fire.Size = new Vector2(FireSize, fire.Size.Y);
            }

            if (isNotClient && GameMain.GameSession?.EventManager is { } eventManager)
            {
                foreach (EventPrefab eventPrefab in triggeredEvents)
                {
                    Event ev = eventPrefab.CreateInstance();
                    if (ev == null) { continue; }
                    eventManager.QueuedEvents.Enqueue(ev);

                    if (ev is ScriptedEvent scriptedEvent)
                    {
                        if (!triggeredEventTargetTag.IsEmpty)
                        {
                            List<Entity> eventTargets = targets.Where(t => t is Entity).Cast<Entity>().ToList();

                            if (eventTargets.Count > 0)
                            {
                                scriptedEvent.Targets.Add(triggeredEventTargetTag, eventTargets);
                            }
                        }

                        if (!triggeredEventEntityTag.IsEmpty && entity != null)
                        {
                            scriptedEvent.Targets.Add(triggeredEventEntityTag, new List<Entity> { entity });
                        }
                    }
                }
            }

            if (isNotClient && entity != null && Entity.Spawner != null) //clients are not allowed to spawn entities
            {
                foreach (CharacterSpawnInfo characterSpawnInfo in spawnCharacters)
                {
                    var characters = new List<Character>();
                    for (int i = 0; i < characterSpawnInfo.Count; i++)
                    {
                        Entity.Spawner.AddCharacterToSpawnQueue(characterSpawnInfo.SpeciesName, position + Rand.Vector(characterSpawnInfo.Spread, Rand.RandSync.Unsynced) + characterSpawnInfo.Offset,
                            onSpawn: newCharacter =>
                            {
                                if (newCharacter.AIController is EnemyAIController enemyAi &&
                                    enemyAi.PetBehavior != null &&
                                    entity is Item item &&
                                    item.ParentInventory is CharacterInventory inv)
                                {
                                    enemyAi.PetBehavior.Owner = inv.Owner as Character;
                                }
                                characters.Add(newCharacter);
                                if (characters.Count == characterSpawnInfo.Count)
                                {
                                    SwarmBehavior.CreateSwarm(characters.Cast<AICharacter>());
                                }
                            });
                    }
                }

                if (spawnItemRandomly)
                {
                    SpawnItem(spawnItems.GetRandomUnsynced());
                }
                else
                {
                    foreach (ItemSpawnInfo itemSpawnInfo in spawnItems)
                    {
                        for (int i = 0; i < itemSpawnInfo.Count; i++)
                        {
                            SpawnItem(itemSpawnInfo);
                        }
                    }
                }


                void SpawnItem(ItemSpawnInfo chosenItemSpawnInfo)
                {
                    switch (chosenItemSpawnInfo.SpawnPosition)
                    {
                        case ItemSpawnInfo.SpawnPositionType.This:
                            Entity.Spawner.AddItemToSpawnQueue(chosenItemSpawnInfo.ItemPrefab, position + Rand.Vector(chosenItemSpawnInfo.Spread, Rand.RandSync.Unsynced), onSpawned: newItem =>
                            {
                                Projectile projectile = newItem.GetComponent<Projectile>();
                                if (projectile != null && user != null && sourceBody != null && entity != null)
                                {
                                    var rope = newItem.GetComponent<Rope>();
                                    if (rope != null && sourceBody.UserData is Limb sourceLimb)
                                    {
                                        rope.Attach(sourceLimb, newItem);
#if SERVER
                                        newItem.CreateServerEvent(rope);
#endif
                                    }
                                    float spread = MathHelper.ToRadians(Rand.Range(-chosenItemSpawnInfo.AimSpread, chosenItemSpawnInfo.AimSpread));
                                    var worldPos = sourceBody.Position;
                                    float rotation = 0;
                                    if (user.Submarine != null)
                                    {
                                        worldPos += user.Submarine.Position;
                                    }
                                    switch (chosenItemSpawnInfo.RotationType)
                                    {
                                        case ItemSpawnInfo.SpawnRotationType.Fixed:
                                            rotation = sourceBody.TransformRotation(chosenItemSpawnInfo.Rotation);
                                            break;
                                        case ItemSpawnInfo.SpawnRotationType.Target:
                                            rotation = MathUtils.VectorToAngle(entity.WorldPosition - worldPos);
                                            break;
                                        case ItemSpawnInfo.SpawnRotationType.Limb:
                                            rotation = sourceBody.TransformedRotation;
                                            break;
                                        case ItemSpawnInfo.SpawnRotationType.Collider:
                                            rotation = user.AnimController.Collider.Rotation + MathHelper.PiOver2;
                                            break;
                                        case ItemSpawnInfo.SpawnRotationType.MainLimb:
                                            rotation = user.AnimController.MainLimb.body.TransformedRotation;
                                            break;
                                        case ItemSpawnInfo.SpawnRotationType.Random:
                                            DebugConsole.ShowError("Random rotation is not supported for Projectiles.");
                                            break;
                                        default:
                                            throw new NotImplementedException("Projectile spawn rotation type not implemented: " + chosenItemSpawnInfo.RotationType);
                                    }
                                    rotation += MathHelper.ToRadians(chosenItemSpawnInfo.Rotation * user.AnimController.Dir);
                                    projectile.Shoot(user, ConvertUnits.ToSimUnits(worldPos), ConvertUnits.ToSimUnits(worldPos), rotation + spread, ignoredBodies: user.AnimController.Limbs.Where(l => !l.IsSevered).Select(l => l.body.FarseerBody).ToList(), createNetworkEvent: true);
                                }
                                else
                                {
                                    var body = newItem.body;
                                    if (body != null)
                                    {
                                        float rotation = MathHelper.ToRadians(chosenItemSpawnInfo.Rotation);
                                        switch (chosenItemSpawnInfo.RotationType)
                                        {
                                            case ItemSpawnInfo.SpawnRotationType.Fixed:
                                                if (sourceBody != null)
                                                {
                                                    rotation = sourceBody.TransformRotation(chosenItemSpawnInfo.Rotation);
                                                }
                                                break;
                                            case ItemSpawnInfo.SpawnRotationType.Limb:
                                                if (sourceBody != null)
                                                {
                                                    rotation += sourceBody.Rotation;
                                                }
                                                break;
                                            case ItemSpawnInfo.SpawnRotationType.Collider:
                                                if (entity is Character character)
                                                {
                                                    rotation += character.AnimController.Collider.Rotation + MathHelper.PiOver2;
                                                }
                                                break;
                                            case ItemSpawnInfo.SpawnRotationType.MainLimb:
                                                if (entity is Character c)
                                                {
                                                    rotation = c.AnimController.MainLimb.body.TransformedRotation;
                                                }
                                                break;
                                            case ItemSpawnInfo.SpawnRotationType.Random:
                                                rotation = Rand.Range(0f, MathHelper.TwoPi, Rand.RandSync.Unsynced);
                                                break;
                                            case ItemSpawnInfo.SpawnRotationType.Target:
                                                break;
                                            default:
                                                throw new NotImplementedException("Spawn rotation type not implemented: " + chosenItemSpawnInfo.RotationType);
                                        }
                                        body.SetTransform(newItem.SimPosition, rotation);
                                        body.ApplyLinearImpulse(Rand.Vector(1) * chosenItemSpawnInfo.Speed);
                                    }
                                }
                                newItem.Condition = newItem.MaxCondition * chosenItemSpawnInfo.Condition;
                            });
                            break;
                        case ItemSpawnInfo.SpawnPositionType.ThisInventory:
                            {
                                Inventory inventory = null;
                                if (entity is Character character && character.Inventory != null)
                                {
                                    inventory = character.Inventory;
                                }
                                else if (entity is Item item)
                                {
                                    var itemContainer = item.GetComponent<ItemContainer>();
                                    inventory = itemContainer?.Inventory;
                                    if (!chosenItemSpawnInfo.SpawnIfCantBeContained && !itemContainer.CanBeContained(chosenItemSpawnInfo.ItemPrefab))
                                    {
                                        return;
                                    }
                                }
                                if (inventory != null && (inventory.CanBePut(chosenItemSpawnInfo.ItemPrefab) || chosenItemSpawnInfo.SpawnIfInventoryFull))
                                {
                                    Entity.Spawner.AddItemToSpawnQueue(chosenItemSpawnInfo.ItemPrefab, inventory, spawnIfInventoryFull: chosenItemSpawnInfo.SpawnIfInventoryFull, onSpawned: item =>
                                    {
                                        if (chosenItemSpawnInfo.Equip && entity is Character character && character.Inventory != null)
                                        {
                                            //if the item is both pickable and wearable, try to wear it instead of picking it up
                                            List<InvSlotType> allowedSlots =
                                               item.GetComponents<Pickable>().Count() > 1 ?
                                               new List<InvSlotType>(item.GetComponent<Wearable>()?.AllowedSlots ?? item.GetComponent<Pickable>().AllowedSlots) :
                                               new List<InvSlotType>(item.AllowedSlots);
                                            allowedSlots.Remove(InvSlotType.Any);
                                            character.Inventory.TryPutItem(item, null, allowedSlots);
                                        }
                                        item.Condition = item.MaxCondition * chosenItemSpawnInfo.Condition;
                                    });
                                }
                            }
                            break;
                        case ItemSpawnInfo.SpawnPositionType.SameInventory:
                            {
                                Inventory inventory = null;
                                if (entity is Character character)
                                {
                                    inventory = character.Inventory;
                                }
                                else if (entity is Item item)
                                {
                                    inventory = item.ParentInventory;
                                }
                                if (inventory != null)
                                {
                                    Entity.Spawner.AddItemToSpawnQueue(chosenItemSpawnInfo.ItemPrefab, inventory, spawnIfInventoryFull: chosenItemSpawnInfo.SpawnIfInventoryFull, onSpawned: (Item newItem) =>
                                    {
                                        newItem.Condition = newItem.MaxCondition * chosenItemSpawnInfo.Condition;
                                    });
                                }
                            }
                            break;
                        case ItemSpawnInfo.SpawnPositionType.ContainedInventory:
                            {
                                Inventory thisInventory = null;
                                if (entity is Character character)
                                {
                                    thisInventory = character.Inventory;
                                }
                                else if (entity is Item item)
                                {
                                    var itemContainer = item.GetComponent<ItemContainer>();
                                    thisInventory = itemContainer?.Inventory;
                                    if (!chosenItemSpawnInfo.SpawnIfCantBeContained && !itemContainer.CanBeContained(chosenItemSpawnInfo.ItemPrefab))
                                    {
                                        return;
                                    }
                                }
                                if (thisInventory != null)
                                {
                                    foreach (Item item in thisInventory.AllItems)
                                    {
                                        Inventory containedInventory = item.GetComponent<ItemContainer>()?.Inventory;
                                        if (containedInventory != null && (containedInventory.CanBePut(chosenItemSpawnInfo.ItemPrefab) || chosenItemSpawnInfo.SpawnIfInventoryFull))
                                        {
                                            Entity.Spawner.AddItemToSpawnQueue(chosenItemSpawnInfo.ItemPrefab, containedInventory, spawnIfInventoryFull: chosenItemSpawnInfo.SpawnIfInventoryFull, onSpawned: (Item newItem) =>
                                            {
                                                newItem.Condition = newItem.MaxCondition * chosenItemSpawnInfo.Condition;
                                            });
                                        }
                                        break;
                                    }
                                }
                            }
                            break;
                    }
                }
            }

            ApplyProjSpecific(deltaTime, entity, targets, hull, position, playSound: true);

            intervalTimer = Interval;

            static Character CharacterFromTarget(ISerializableEntity target)
            {
                Character targetCharacter = target as Character;
                if (targetCharacter == null)
                {
                    if (target is Limb targetLimb && !targetLimb.Removed)
                    {
                        targetCharacter = targetLimb.character;
                    }
                }
                return targetCharacter;
            }
        }

        partial void ApplyProjSpecific(float deltaTime, Entity entity, IReadOnlyList<ISerializableEntity> targets, Hull currentHull, Vector2 worldPosition, bool playSound);

        private void ApplyToProperty(ISerializableEntity target, SerializableProperty property, int effectIndex, float deltaTime)
        {
            if (disableDeltaTime || setValue) { deltaTime = 1.0f; }
            object propertyEffect = propertyEffects[effectIndex];
            if (propertyEffect is int || propertyEffect is float)
            {
                float propertyValueF = property.GetFloatValue(target);
                if (property.PropertyType == typeof(float))
                {
                    float floatValue = propertyEffect is float single ? single : (int)propertyEffect;
                    floatValue *= deltaTime;
                    if (!setValue)
                    {
                        floatValue += propertyValueF;
                    }
                    property.TrySetValue(target, floatValue);
                    return;
                }
                else if (property.PropertyType == typeof(int))
                {
                    int intValue = (int)(propertyEffect is float single ? single * deltaTime : (int)propertyEffect * deltaTime);
                    if (!setValue)
                    {
                        intValue += (int)propertyValueF;
                    }
                    property.TrySetValue(target, intValue);
                    return;
                }
            }
            else if (propertyEffect is bool propertyValueBool)
            {
                property.TrySetValue(target, propertyValueBool);
                return;
            }
            property.TrySetValue(target, propertyEffect);
        }

        public static void UpdateAll(float deltaTime)
        {
            UpdateAllProjSpecific(deltaTime);

            DelayedEffect.Update(deltaTime);
            for (int i = DurationList.Count - 1; i >= 0; i--)
            {
                DurationListElement element = DurationList[i];

                if (element.Parent.CheckConditionalAlways && !element.Parent.HasRequiredConditions(element.Targets))
                {
                    DurationList.RemoveAt(i);
                    continue;
                }

                element.Targets.RemoveAll(t =>
                    (t is Entity entity && entity.Removed) ||
                    (t is Limb limb && (limb.character == null || limb.character.Removed)));
                if (element.Targets.Count == 0)
                {
                    DurationList.RemoveAt(i);
                    continue;
                }

                foreach (ISerializableEntity target in element.Targets)
                {
                    for (int n = 0; n < element.Parent.propertyNames.Length; n++)
                    {
                        if (target == null ||
                            target.SerializableProperties == null ||
                            !target.SerializableProperties.TryGetValue(element.Parent.propertyNames[n], out SerializableProperty property))
                        {
                            continue;
                        }
                        element.Parent.ApplyToProperty(target, property, n, CoroutineManager.UnscaledDeltaTime);
                    }

                    foreach (Affliction affliction in element.Parent.Afflictions)
                    {
                        Affliction newAffliction = affliction;
                        if (target is Character character)
                        {
                            if (character.Removed) { continue; }
                            newAffliction = element.Parent.GetMultipliedAffliction(affliction, element.Entity, character, deltaTime, element.Parent.multiplyAfflictionsByMaxVitality);
                            var result = character.AddDamage(character.WorldPosition, newAffliction.ToEnumerable(), stun: 0.0f, playSound: false, attacker: element.User);
                            element.Parent.RegisterTreatmentResults(element.Entity, result.HitLimb, affliction, result);
                        }
                        else if (target is Limb limb)
                        {
                            if (limb.character.Removed || limb.Removed) { continue; }
                            newAffliction = element.Parent.GetMultipliedAffliction(affliction, element.Entity, limb.character, deltaTime, element.Parent.multiplyAfflictionsByMaxVitality);
                            var result = limb.character.DamageLimb(limb.WorldPosition, limb, newAffliction.ToEnumerable(), stun: 0.0f, playSound: false, attackImpulse: 0.0f, attacker: element.User);
                            element.Parent.RegisterTreatmentResults(element.Entity, limb, affliction, result);
                        }
                    }
                    
                    foreach (var (affliction, amount) in element.Parent.ReduceAffliction)
                    {
                        Limb targetLimb = null;
                        Character targetCharacter = null;
                        if (target is Character character)
                        {
                            targetCharacter = character;
                        }
                        else if (target is Limb limb)
                        {
                            targetLimb = limb;
                            targetCharacter = limb.character;
                        }
                        if (targetCharacter != null && !targetCharacter.Removed)
                        {
                            ActionType? actionType = null;
                            if (element.Entity is Item item && item.UseInHealthInterface) { actionType = element.Parent.type; }
                            float reduceAmount = amount * element.Parent.GetAfflictionMultiplier(element.Entity, targetCharacter, deltaTime);
                            float prevVitality = targetCharacter.Vitality;
                            if (targetLimb != null)
                            {
                                targetCharacter.CharacterHealth.ReduceAfflictionOnLimb(targetLimb, affliction, reduceAmount, treatmentAction: actionType);
                            }
                            else
                            {
                                targetCharacter.CharacterHealth.ReduceAfflictionOnAllLimbs(affliction, reduceAmount, treatmentAction: actionType);
                            }
                            if (element.User != null && element.User != targetCharacter)
                            {
                                targetCharacter.AIController?.OnHealed(healer: element.User, targetCharacter.Vitality - prevVitality);
                                if (!targetCharacter.IsDead)
                                {
                                    targetCharacter.TryAdjustAttackerSkill(element.User, targetCharacter.Vitality - prevVitality);
                                }
                            };
#if SERVER
                            GameMain.Server.KarmaManager.OnCharacterHealthChanged(targetCharacter, element.User, prevVitality - targetCharacter.Vitality, 0.0f);
#endif
                        }
                    }
                }

                element.Parent.ApplyProjSpecific(deltaTime, 
                    element.Entity, 
                    element.Targets, 
                    element.Parent.GetHull(element.Entity), 
                    element.Parent.GetPosition(element.Entity, element.Targets),
                    playSound: element.Timer >= element.Duration);

                element.Timer -= deltaTime;

                if (element.Timer > 0.0f) { continue; }
                DurationList.Remove(element);
            }
        }

        private float GetAfflictionMultiplier(Entity entity, Character targetCharacter, float deltaTime)
        {
            float multiplier = !setValue && !disableDeltaTime ? deltaTime : 1.0f;
            if (entity is Item sourceItem && sourceItem.HasTag("medical"))
            {
                multiplier *= 1 + targetCharacter.GetStatValue(StatTypes.MedicalItemEffectivenessMultiplier);
                
                if (user != null)
                {
                    multiplier *= 1 + user.GetStatValue(StatTypes.MedicalItemApplyingMultiplier);
                }
            }
            return multiplier * AfflictionMultiplier;
        }

        private Affliction GetMultipliedAffliction(Affliction affliction, Entity entity, Character targetCharacter, float deltaTime, bool modifyByMaxVitality)
        {
            float afflictionMultiplier = GetAfflictionMultiplier(entity, targetCharacter, deltaTime);
            if (modifyByMaxVitality)
            {
                afflictionMultiplier *= targetCharacter.MaxVitality / 100f;
            }

            if (!MathUtils.NearlyEqual(afflictionMultiplier, 1.0f))
            {
                return affliction.CreateMultiplied(afflictionMultiplier);
            }
            return affliction;
        }

        private void RegisterTreatmentResults(Entity entity, Limb limb, Affliction affliction, AttackResult result)
        {
            if (entity is Item item && item.UseInHealthInterface && limb != null)
            {
                foreach (Affliction limbAffliction in limb.character.CharacterHealth.GetAllAfflictions())
                {
                    if (result.Afflictions != null && result.Afflictions.Any(a => a.Prefab == limbAffliction.Prefab) &&
                       (!affliction.Prefab.LimbSpecific || limb.character.CharacterHealth.GetAfflictionLimb(affliction) == limb))
                    {
                        if (type == ActionType.OnUse)
                        {
                            limbAffliction.AppliedAsSuccessfulTreatmentTime = Timing.TotalTime;
                        }
                        else if (type == ActionType.OnFailure)
                        {
                            limbAffliction.AppliedAsFailedTreatmentTime = Timing.TotalTime;
                        }
                    }
                }
            }
        }

        static partial void UpdateAllProjSpecific(float deltaTime);

        public static void StopAll()
        {
            CoroutineManager.StopCoroutines("statuseffect");
            DelayedEffect.DelayList.Clear();
            DurationList.Clear();
        }

        public void AddTag(string tag)
        {
            if (tags.Contains(tag)) { return; }
            tags.Add(tag);
        }

        public bool HasTag(string tag)
        {
            if (tag == null) { return true; }

            return (tags.Contains(tag) || tags.Contains(tag.ToLowerInvariant()));
        }
    }
}
