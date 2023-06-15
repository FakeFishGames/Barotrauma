﻿using Barotrauma.Abilities;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;

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

    /// <summary>
    /// StatusEffects can be used to execute various kinds of effects: modifying the state of some entity in some way, spawning things, playing sounds,
    /// emitting particles, creating fire and explosions, increasing a characters' skill. They are a crucial part of modding Barotrauma: all kinds of
    /// custom behaviors of an item or a creature for example are generally created using StatusEffects.
    /// </summary>
    /// <doc>
    /// <Field identifier="delay" type="float" defaultValue="0.0">
    ///     Can be used to delay the execution of the effect. For example, you could have an effect that triggers when a character receives damage, 
    ///     but takes 5 seconds before it starts to do anything.
    /// </Field>
    /// <Field identifier="tags" type="string[]" defaultValue="">
    ///     An arbitrary tag (or a list of tags) that describe the status effect and can be used by Conditionals to check whether some StatusEffect is running.
    ///     For example, an item could execute a StatusEffect with the tag "poisoned" on some character, and the character could have an effect that makes
    ///     the character do something when an effect with that tag is active.
    /// </Field>        
    /// <Field identifier="conditionalComparison" type="Comparison" defaultValue="Or">
    ///     And/Or. Do all of the Conditionals defined in the effect be true for the effect to execute, or should the effect execute when any of them is true?
    /// </Field>
    /// <Field identifier="Any property of the target" type="Any" defaultValue="">
    ///     These are the meat of the StatusEffects. You can set, increment or decrement any value of the target, be it an item, character, limb or hull.
    ///     By default, the value is added to the existing value. If you want to instead set the value, use the setValue attribute. 
    ///     For example, Condition="-5" would decrease the condition of the item the effect is targeting by 5 per second. If the target has no property
    ///     with the specified name, the attribute does nothing.
    /// </Field>
    /// </doc>
    partial class StatusEffect
    {
        private static readonly ImmutableHashSet<Identifier> FieldNames;
        static StatusEffect()
        {
            FieldNames = typeof(StatusEffect).GetFields().AsEnumerable().Select(f => f.Name.ToIdentifier()).ToImmutableHashSet();
        }

        [Flags]
        public enum TargetType
        {
            /// <summary>
            /// The entity (item, character, limb) the StatusEffect is defined in.
            /// </summary>
            This = 1,
            /// <summary>
            /// In the context of items, the container the item is inside (if any). In the context of limbs, the character the limb belongs to.
            /// </summary>
            Parent = 2,
            /// <summary>
            /// The character the StatusEffect is defined in. In the context of items and attacks, the character using the item/attack.
            /// </summary>
            Character = 4,
            /// <summary>
            /// The item(s) contained in the inventory of the entity the StatusEffect is defined in.
            /// </summary>
            Contained = 8,
            /// <summary>
            /// Characters near the entity the StatusEffect is defined in. The range is defined using <see cref="Range"/>.
            /// </summary>
            NearbyCharacters = 16,
            /// <summary>
            /// Items near the entity the StatusEffect is defined in. The range is defined using <see cref="Range"/>.
            /// </summary>
            NearbyItems = 32,
            /// <summary>
            /// The entity the item/attack is being used on.
            /// </summary>
            UseTarget = 64,
            /// <summary>
            /// The hull the entity is inside.
            /// </summary>
            Hull = 128,
            /// <summary>
            /// The entity the item/attack is being used on. In the context of characters, one of the character's limbs (specify which one using <see cref="targetLimbs"/>).
            /// </summary>
            Limb = 256,
            /// <summary>
            /// All limbs of the character the effect is being used on.
            /// </summary>
            AllLimbs = 512,
            /// <summary>
            /// Last limb of the character the effect is being used on.
            /// </summary>
            LastLimb = 1024
        }

        /// <summary>
        /// Defines items spawned by the effect, and where and how they're spawned.
        /// </summary>
        class ItemSpawnInfo
        {
            public enum SpawnPositionType
            {
                /// <summary>
                /// The position of the StatusEffect's target.
                /// </summary>
                This,
                /// <summary>
                /// The inventory of the StatusEffect's target.
                /// </summary>
                ThisInventory,
                /// <summary>
                /// The same inventory the StatusEffect's target entity is in. Only valid if the target is an Item.
                /// </summary>
                SameInventory,
                /// <summary>
                /// The inventory of an item in the inventory of the StatusEffect's target entity (e.g. a container in the character's inventory)
                /// </summary>
                ContainedInventory
            }

            public enum SpawnRotationType
            {
                /// <summary>
                /// Fixed rotation specified using the Rotation attribute.
                /// </summary>
                Fixed,
                /// <summary>
                /// The rotation of the entity executing the StatusEffect
                /// </summary>
                Target,
                /// <summary>
                /// The rotation of the limb executing the StatusEffect, or the limb the StatusEffect is targeting
                /// </summary>
                Limb,
                /// <summary>
                /// The rotation of the main limb (usually torso) of the character executing the StatusEffect
                /// </summary>
                MainLimb,
                /// <summary>
                /// The rotation of the collider of the character executing the StatusEffect
                /// </summary>
                Collider,
                /// <summary>
                /// Random rotation between 0 and 360 degrees.
                /// </summary>
                Random
            }

            public readonly ItemPrefab ItemPrefab;
            /// <summary>
            /// Where should the item spawn?
            /// </summary>
            public readonly SpawnPositionType SpawnPosition;

            /// <summary>
            /// Should the item spawn even if the container is already full?
            /// </summary>
            public readonly bool SpawnIfInventoryFull;
            /// <summary>
            /// Should the item spawn even if the container can't contain items of this type or if it's already full?
            /// </summary>
            public readonly bool SpawnIfCantBeContained;
            /// <summary>
            /// Impulse applied to the item when it spawns (i.e. how fast the item launched off).
            /// </summary>
            public readonly float Impulse;
            public readonly float RotationRad;
            /// <summary>
            /// How many items to spawn.
            /// </summary>
            public readonly int Count;
            /// <summary>
            /// Random offset added to the spawn position in pixels.
            /// </summary>
            public readonly float Spread;
            /// <summary>
            /// What should the initial rotation of the item be?
            /// </summary>
            public readonly SpawnRotationType RotationType;
            /// <summary>
            /// Amount of random variance in the initial rotation of the item (in degrees).
            /// </summary>
            public readonly float AimSpreadRad;
            /// <summary>
            /// Should the item be automatically equipped when it spawns? Only valid if the item spawns in a character's inventory.
            /// </summary>
            public readonly bool Equip;
            /// <summary>
            /// Condition of the item when it spawns (1.0 = max).
            /// </summary>
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
                Impulse = element.GetAttributeFloat("impulse", element.GetAttributeFloat("launchimpulse", element.GetAttributeFloat("speed", 0.0f)));

                Condition = MathHelper.Clamp(element.GetAttributeFloat("condition", 1.0f), 0.0f, 1.0f);

                RotationRad = MathHelper.ToRadians(element.GetAttributeFloat("rotation", 0.0f));
                Count = element.GetAttributeInt("count", 1);
                Spread = element.GetAttributeFloat("spread", 0f);
                AimSpreadRad = MathHelper.ToRadians(element.GetAttributeFloat("aimspread", 0f));
                Equip = element.GetAttributeBool("equip", false);

                SpawnPosition = element.GetAttributeEnum("spawnposition", SpawnPositionType.This);
                RotationType = element.GetAttributeEnum("rotationtype", RotationRad != 0 ? SpawnRotationType.Fixed : SpawnRotationType.Target);
            }
        }

        /// <summary>
        /// Can be used by <see cref="AbilityConditionStatusEffectIdentifier"/> to check whether some specific StatusEffect is running.
        /// </summary>
        /// <doc>
        /// <Field identifier="EffectIdentifier" type="identifier" defaultValue="">
        ///     An arbitrary identifier the Ability can check for.
        /// </Field>
        /// </doc>
        public class AbilityStatusEffectIdentifier : AbilityObject
        {
            public AbilityStatusEffectIdentifier(Identifier effectIdentifier)
            {
                EffectIdentifier = effectIdentifier;
            }
            public Identifier EffectIdentifier { get; set; }
        }

        /// <summary>
        /// Unlocks a talent, or multiple talents when the effect executes. Only valid if the target is a character or a limb.
        /// </summary>
        public class GiveTalentInfo
        {
            /// <summary>
            /// The identifier(s) of the talents that should be unlocked.
            /// </summary>
            public Identifier[] TalentIdentifiers;
            /// <summary>
            /// If true and there's multiple identifiers defined, a random one will be chosen instead of unlocking all of them.
            /// </summary>
            public bool GiveRandom;

            public GiveTalentInfo(XElement element, string _)
            {
                TalentIdentifiers = element.GetAttributeIdentifierArray("talentidentifiers", Array.Empty<Identifier>());
                GiveRandom = element.GetAttributeBool("giverandom", false);
            }
        }

        /// <summary>
        /// Increases a character's skills when the effect executes. Only valid if the target is a character or a limb.
        /// </summary>
        public class GiveSkill
        {
            /// <summary>
            /// The identifier of the skill to increase.
            /// </summary>
            public readonly Identifier SkillIdentifier;
            /// <summary>
            /// How much to increase the skill.
            /// </summary>
            public readonly float Amount;
            /// <summary>
            /// Should the talents that trigger when the character gains skills be triggered by the effect?
            /// </summary>
            public readonly bool TriggerTalents;

            public GiveSkill(XElement element, string parentDebugName)
            {
                SkillIdentifier = element.GetAttributeIdentifier(nameof(SkillIdentifier), Identifier.Empty);
                Amount = element.GetAttributeFloat(nameof(Amount), 0);
                TriggerTalents = element.GetAttributeBool(nameof(TriggerTalents), true);

                if (SkillIdentifier == Identifier.Empty)
                {
                    DebugConsole.ThrowError($"GiveSkill StatusEffect did not have a skill identifier defined in {parentDebugName}!");
                }
            }
        }

        /// <summary>
        /// Defines characters spawned by the effect, and where and how they're spawned.
        /// </summary>
        public class CharacterSpawnInfo : ISerializableEntity
        {
            public string Name => $"Character Spawn Info ({SpeciesName})";
            public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; set; }

            [Serialize("", IsPropertySaveable.No, description: "The species name (identifier) of the character to spawn.")]
            public Identifier SpeciesName { get; private set; }

            [Serialize(1, IsPropertySaveable.No, description: "How many characters to spawn.")]
            public int Count { get; private set; }

            [Serialize(false, IsPropertySaveable.No, description: 
                "Should the buffs of the character executing the effect be transferred to the spawned character?"+
                " Useful for effects that \"transform\" a character to something else by deleting the character and spawning a new one on its place.")]
            public bool TransferBuffs { get; private set; }

            [Serialize(false, IsPropertySaveable.No, description:
                "Should the afflictions of the character executing the effect be transferred to the spawned character?" +
                " Useful for effects that \"transform\" a character to something else by deleting the character and spawning a new one on its place.")]
            public bool TransferAfflictions { get; private set; }

            [Serialize(false, IsPropertySaveable.No, description:
                "Should the the items from the character executing the effect be transferred to the spawned character?" +
                " Useful for effects that \"transform\" a character to something else by deleting the character and spawning a new one on its place.")]
            public bool TransferInventory { get; private set; }

            [Serialize(0, IsPropertySaveable.No, description:
                "The maximum number of creatures of the given species and team that can exist in the current level before this status effect stops spawning any more.")]
            public int TotalMaxCount { get; private set; }

            [Serialize(0, IsPropertySaveable.No, description: "Amount of stun to apply on the spawned character.")]
            public int Stun { get; private set; }

            [Serialize("", IsPropertySaveable.No, description: "An affliction to apply on the spawned character.")]
            public Identifier AfflictionOnSpawn { get; private set; }

            [Serialize(1, IsPropertySaveable.No, description: 
                $"The strength of the affliction applied on the spawned character. Only relevant if {nameof(AfflictionOnSpawn)} is defined.")]
            public int AfflictionStrength { get; private set; }

            [Serialize(false, IsPropertySaveable.No, description: 
                "Should the player controlling the character that executes the effect gain control of the spawned character?" +
                " Useful for effects that \"transform\" a character to something else by deleting the character and spawning a new one on its place.")]
            public bool TransferControl { get; private set; }

            [Serialize(false, IsPropertySaveable.No, description:
                "Should the character that executes the effect be removed when the effect executes?" +
                " Useful for effects that \"transform\" a character to something else by deleting the character and spawning a new one on its place.")]
            public bool RemovePreviousCharacter { get; private set; }

            [Serialize(0f, IsPropertySaveable.No, description: "Amount of random spread to add to the spawn position. " +
                "Can be used to prevent all the characters from spawning at the exact same position if the effect spawns multiple ones.")]
            public float Spread { get; private set; }

            [Serialize("0,0", IsPropertySaveable.No, description:
                "Offset added to the spawn position. " +
                "Can be used to for example spawn a character a bit up from the center of an item executing the effect.")]
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

        /// <summary>
        /// Can be used to trigger a behavior change of some kind on an AI character. Only applicable for enemy characters, not humans.
        /// </summary>
        public class AITrigger : ISerializableEntity
        {
            public string Name => "ai trigger";

            public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; set; }

            [Serialize(AIState.Idle, IsPropertySaveable.No, description: "The AI state the character should switch to.")]
            public AIState State { get; private set; }

            [Serialize(0f, IsPropertySaveable.No, description: "How long should the character stay in the specified state? If 0, the effect is permanent (unless overridden by another AITrigger).")]
            public float Duration { get; private set; }

            [Serialize(1f, IsPropertySaveable.No, description: "How likely is the AI to change the state when this effect executes? 1 = always, 0.5 = 50% chance, 0 = never.")]
            public float Probability { get; private set; }

            [Serialize(0f, IsPropertySaveable.No, description:
                "How much damage the character must receive for this AITrigger to become active? " +
                "Checks the amount of damage the latest attack did to the character.")]
            public float MinDamage { get; private set; }

            [Serialize(true, IsPropertySaveable.No, description: "Can this AITrigger override other active AITriggers?")]
            public bool AllowToOverride { get; private set; }

            [Serialize(true, IsPropertySaveable.No, description: "Can this AITrigger be overridden by other AITriggers?")]
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


        /// <summary>
        /// What should this status effect be applied on?
        /// </summary>
        private readonly TargetType targetTypes;

        /// <summary>
        /// Index of the slot the target must be in. Only valid when targeting a Contained item.
        /// </summary>
        public int TargetSlot = -1;

        private readonly List<RelatedItem> requiredItems = new List<RelatedItem>();

        public readonly ImmutableArray<(Identifier propertyName, object value)> PropertyEffects;

        private readonly PropertyConditional.Comparison conditionalComparison = PropertyConditional.Comparison.Or;
        private readonly List<PropertyConditional> propertyConditionals;
        public bool HasConditions => propertyConditionals != null && propertyConditionals.Any();

        /// <summary>
        /// If set to true, the effect will set the properties of the target to the given values, instead of incrementing them by the given value.
        /// </summary>
        private readonly bool setValue;

        /// <summary>
        /// If set to true, the values will not be multiplied by the elapsed time. 
        /// In other words, the values are treated as an increase per frame, as opposed to an increase per second.
        /// Useful for effects that are intended to just run for one frame (e.g. firing a gun, an explosion).
        /// </summary>
        private readonly bool disableDeltaTime;

        /// <summary>
        /// Can be used in conditionals to check if a StatusEffect with a specific tag is currently running. Only relevant for effects with a non-zero duration.
        /// </summary>
        private readonly HashSet<string> tags;

        /// <summary>
        /// How long the effect runs (in seconds). Note that if <see cref="Stackable"/> is true, 
        /// there can be multiple instances of the effect running at a time. 
        /// In other words, if the effect has a duration and executes every frame, you probably want 
        /// to make it non-stackable or it'll lead to a large number of overlapping effects running at the same time.
        /// </summary>
        private readonly float duration;

        /// <summary>
        /// How long _can_ the event run (in seconds). The difference to <see cref="duration"/> is that 
        /// lifetime doesn't force the effect to run for the given amount of time, only restricts how 
        /// long it can run in total. For example, you could have an effect that makes a projectile
        /// emit particles for 1 second when it's active, and not do anything after that.
        /// </summary>
        private readonly float lifeTime;
        private float lifeTimer;

        public Dictionary<Entity, float> intervalTimers = new Dictionary<Entity, float>();

        /// <summary>
        /// Makes the effect only execute once. After it has executed, it'll never execute again (during the same round).
        /// </summary>
        private readonly bool oneShot;

        public static readonly List<DurationListElement> DurationList = new List<DurationListElement>();

        /// <summary>
        /// Only applicable for StatusEffects with a duration or delay. Should the conditional checks only be done when the effect triggers, 
        /// or for the whole duration it executes / when the delay runs out and the effect executes? In other words, if false, the conditionals 
        /// are only checked once when the effect triggers, but after that it can keep running for the whole duration, or is
        /// guaranteed to execute after the delay. 
        /// </summary>
        public readonly bool CheckConditionalAlways;

        /// <summary>
        /// Only valid if the effect has a duration or delay. Can the effect be applied on the same target(s) if the effect is already being applied?
        /// </summary>
        public readonly bool Stackable = true;

        /// <summary>
        /// The interval at which the effect is executed. The difference between delay and interval is that effects with a delay find the targets, check the conditions, etc
        /// immediately when Apply is called, but don't apply the effects until the delay has passed. Effects with an interval check if the interval has passed when Apply is
        /// called and apply the effects if it has, otherwise they do nothing.
        /// </summary>
        public readonly float Interval;

#if CLIENT
        /// <summary>
        /// Should the sound(s) configured in the effect be played if the required items aren't found?
        /// </summary>
        private readonly bool playSoundOnRequiredItemFailure = false;
#endif

        private readonly int useItemCount;

        private readonly bool removeItem, dropContainedItems, dropItem, removeCharacter, breakLimb, hideLimb;
        private readonly float hideLimbTimer;

        public readonly ActionType type = ActionType.OnActive;

        public readonly List<Explosion> Explosions = new List<Explosion>();

        private readonly List<ItemSpawnInfo> spawnItems = new List<ItemSpawnInfo>();

        /// <summary>
        /// If enabled, one of the items this effect is configured to spawn is selected randomly, as opposed to spawning all of them.
        /// </summary>
        private readonly bool spawnItemRandomly;
        private readonly List<CharacterSpawnInfo> spawnCharacters = new List<CharacterSpawnInfo>();

        public readonly List<GiveTalentInfo> giveTalentInfos = new List<GiveTalentInfo>();

        private readonly List<AITrigger> aiTriggers = new List<AITrigger>();

        private readonly List<EventPrefab> triggeredEvents = new List<EventPrefab>();

        /// <summary>
        /// If the effect triggers a scripted event, the target of this effect is added as a target for the event using the specified tag.
        /// For example, an item could have an effect that executes when used on some character, and triggers an event that makes said character say something.
        /// </summary>
        private readonly Identifier triggeredEventTargetTag;

        /// <summary>
        /// If the effect triggers a scripted event, the entity executing this effect is added as a target for the event using the specified tag.
        /// For example, a character could have an effect that executes when the character takes damage, and triggers an event that makes said character say something.
        /// </summary>
        private readonly Identifier triggeredEventEntityTag;

        /// <summary>
        /// If the effect triggers a scripted event, the user of the StatusEffect (= the character who caused it to happen, e.g. a character who used an item) is added as a target for the event using the specified tag.
        /// For example, a gun could have an effect that executes when a character uses it, and triggers an event that makes said character say something.
        /// </summary>
        private readonly Identifier triggeredEventUserTag;

        private Character user;

        public readonly float FireSize;

        /// <summary>
        /// Which types of limbs this effect can target? Only valid when targeting characters or limbs.
        /// </summary>
        public readonly LimbType[] targetLimbs;

        /// <summary>
        /// The probability of severing a limb damaged by this status effect. Only valid when targeting characters or limbs.
        /// </summary>
        public readonly float SeverLimbsProbability;

        public PhysicsBody sourceBody;

        /// <summary>
        /// If enabled, this effect can only execute inside a hull.
        /// </summary>
        public readonly bool OnlyInside;
        /// <summary>
        /// If enabled, this effect can only execute outside hulls.
        /// </summary>
        public readonly bool OnlyOutside;

        /// <summary>
        /// If enabled, the effect only executes when the entity receives damage from a player character 
        /// (a character controlled by a human player). Only valid for characters, and effects of the type <see cref="OnDamaged"/>.
        /// </summary>
        public readonly bool OnlyWhenDamagedByPlayer;

        /// <summary>
        /// Can the StatusEffect be applied when the item applying it is broken?
        /// </summary>
        public readonly bool AllowWhenBroken = false;

        /// <summary>
        /// Identifier(s), tag(s) or species name(s) of the entity the effect can target. Null if there's no identifiers.
        /// </summary>
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
        } = new List<Affliction>();

        /// <summary>
        /// Should the affliction strength be directly proportional to the maximum vitality of the character? 
        /// In other words, when enabled, the strength of the affliction(s) caused by this effect is higher on higher-vitality characters.
        /// Can be used to make characters take the same relative amount of damage regardless of their maximum vitality.
        /// </summary>
        private readonly bool? multiplyAfflictionsByMaxVitality;

        public IEnumerable<CharacterSpawnInfo> SpawnCharacters
        {
            get { return spawnCharacters; }
        }

        public readonly List<(Identifier AfflictionIdentifier, float ReduceAmount)> ReduceAffliction = new List<(Identifier affliction, float amount)>();

        private readonly List<Identifier> talentTriggers = new List<Identifier>();
        private readonly List<int> giveExperiences = new List<int>();
        private readonly List<GiveSkill> giveSkills = new List<GiveSkill>();

        public float Duration => duration;

        /// <summary>
        /// How close to the entity executing the effect the targets must be. Only applicable if targeting NearbyCharacters or NearbyItems.
        /// </summary>
        public float Range
        {
            get;
            private set;
        }

        /// <summary>
        /// An offset added to the position of the effect is executed at. Only relevant if the effect does something where position matters,
        /// for example emitting particles or explosions, spawning something or playing sounds.
        /// </summary>
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

        public bool Disabled { get; private set; }

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
            tags = new HashSet<string>(element.GetAttributeString("tags", "").Split(','));
            OnlyInside = element.GetAttributeBool("onlyinside", false);
            OnlyOutside = element.GetAttributeBool("onlyoutside", false);
            OnlyWhenDamagedByPlayer = element.GetAttributeBool("onlyplayertriggered", element.GetAttributeBool("onlywhendamagedbyplayer", false));
            AllowWhenBroken = element.GetAttributeBool("allowwhenbroken", false);

            Interval = element.GetAttributeFloat("interval", 0.0f);
            duration = element.GetAttributeFloat("duration", 0.0f);
            disableDeltaTime = element.GetAttributeBool("disabledeltatime", false);
            setValue = element.GetAttributeBool("setvalue", false);
            Stackable = element.GetAttributeBool("stackable", true);
            lifeTime = lifeTimer = element.GetAttributeFloat("lifetime", 0.0f);
            CheckConditionalAlways = element.GetAttributeBool("checkconditionalalways", false);

            TargetSlot = element.GetAttributeInt("targetslot", -1);

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

            SeverLimbsProbability = MathHelper.Clamp(element.GetAttributeFloat(0.0f, "severlimbs", "severlimbsprobability"), 0.0f, 1.0f);

            string[] targetTypesStr = 
                element.GetAttributeStringArray("target", null) ?? 
                element.GetAttributeStringArray("targettype", Array.Empty<string>());
            foreach (string s in targetTypesStr)
            {
                if (!Enum.TryParse(s, true, out TargetType targetType))
                {
                    DebugConsole.ThrowError($"Invalid target type \"{s}\" in StatusEffect ({parentDebugName})");
                }
                else
                {
                    targetTypes |= targetType;
                }
            }

            var targetIdentifiers = element.GetAttributeIdentifierArray(Array.Empty<Identifier>(), "targetnames", "targets", "targetidentifiers", "targettags");
            if (targetIdentifiers.Any())
            {
                TargetIdentifiers = targetIdentifiers.ToImmutableHashSet();
            }

            triggeredEventTargetTag = element.GetAttributeIdentifier("eventtargettag", Identifier.Empty);
            triggeredEventEntityTag = element.GetAttributeIdentifier("evententitytag", Identifier.Empty);
            triggeredEventUserTag = element.GetAttributeIdentifier("eventusertag", Identifier.Empty);

            spawnItemRandomly = element.GetAttributeBool("spawnitemrandomly", false);

            var multiplyAfflictionsElement = element.GetAttribute(nameof(multiplyAfflictionsByMaxVitality));
            if (multiplyAfflictionsElement != null)
            {
                multiplyAfflictionsByMaxVitality = multiplyAfflictionsElement.GetAttributeBool(false);
            }

#if CLIENT
            playSoundOnRequiredItemFailure = element.GetAttributeBool("playsoundonrequireditemfailure", false);
#endif

            List<XAttribute> propertyAttributes = new List<XAttribute>();
            propertyConditionals = new List<PropertyConditional>();
            foreach (XAttribute attribute in element.Attributes())
            {
                switch (attribute.Name.ToString().ToLowerInvariant())
                {
                    case "type":
                        if (!Enum.TryParse(attribute.Value, true, out type))
                        {
                            DebugConsole.ThrowError($"Invalid action type \"{attribute.Value}\" in StatusEffect ({parentDebugName})");
                        }
                        break;
                    case "targettype":
                    case "target":
                    case "targetnames":
                    case "targets":
                    case "targetidentifiers":
                    case "targettags":
                    case "severlimbs":
                    case "targetlimb":
                    case "delay":
                    case "interval":
                        //aliases for fields we're already reading above, and which shouldn't be interpreted as values we're trying to set
                        break;
                    case "allowedafflictions":
                    case "requiredafflictions":
                        //backwards compatibility, should be defined as child elements instead
                        string[] types = attribute.Value.Split(',');
                        requiredAfflictions ??= new HashSet<(Identifier, float)>();
                        for (int i = 0; i < types.Length; i++)
                        {
                            requiredAfflictions.Add((types[i].Trim().ToIdentifier(), 0.0f));
                        }
                        break;
                    case "conditionalcomparison":
                    case "comparison":
                        if (!Enum.TryParse(attribute.Value, ignoreCase: true, out conditionalComparison))
                        {
                            DebugConsole.ThrowError($"Invalid conditional comparison type \"{attribute.Value}\" in StatusEffect ({parentDebugName})");
                        }
                        break;
                    case "sound":
                        DebugConsole.ThrowError($"Error in StatusEffect ({parentDebugName}): sounds should be defined as child elements of the StatusEffect, not as attributes.");
                        break;
                    case "range":
                        if (!HasTargetType(TargetType.NearbyCharacters) && !HasTargetType(TargetType.NearbyItems))
                        {
                            propertyAttributes.Add(attribute);
                        }
                        break;
                    case "tags":
                        if (duration <= 0.0f || setValue)
                        {
                            //a workaround to "tags" possibly meaning either an item's tags or this status effect's tags:
                            //if the status effect doesn't have a duration, assume tags mean an item's tags, not this status effect's tags
                            propertyAttributes.Add(attribute);
                        }
                        break;
                    case "oneshot":
                        oneShot = attribute.GetAttributeBool(false);
                        break;
                    default:
                        if (FieldNames.Contains(attribute.Name.ToIdentifier())) { continue; }
                        propertyAttributes.Add(attribute);
                        break;
                }
            }

            List<(Identifier propertyName, object value)> propertyEffects = new List<(Identifier propertyName, object value)>();
            foreach (XAttribute attribute in propertyAttributes)
            {
                propertyEffects.Add((attribute.NameAsIdentifier(), XMLExtensions.GetAttributeObject(attribute)));
            }
            PropertyEffects = propertyEffects.ToImmutableArray();

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
                    case "dropitem":
                        dropItem = true;
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
            foreach (var (propertyName, value) in PropertyEffects)
            {
                if (propertyName == "condition" && value.GetType() == typeof(float))
                {
                    return (float)value < 0.0f || (setValue && (float)value <= 0.0f);
                }
            }
            return false;
        }

        public bool IncreasesItemCondition()
        {
            foreach (var (propertyName, value) in PropertyEffects)
            {
                if (propertyName == "condition" && value.GetType() == typeof(float))
                {
                    return (float)value > 0.0f || (setValue && (float)value > 0.0f);
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

        public void AddNearbyTargets(Vector2 worldPosition, List<ISerializableEntity> targets)
        {
            if (Range <= 0.0f) { return; }
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
                if (TargetIdentifiers != null && 
                    TargetIdentifiers.Count == 1 &&
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

        private static readonly List<Entity> intervalsToRemove = new List<Entity>();
        public bool ShouldWaitForInterval(Entity entity, float deltaTime)
        {
            if (Interval > 0.0f && entity != null)
            {
                if (intervalTimers.ContainsKey(entity))
                {
                    intervalTimers[entity] -= deltaTime;
                    if (intervalTimers[entity] > 0.0f) { return true; }
                }
                intervalsToRemove.Clear();
                intervalsToRemove.AddRange(intervalTimers.Keys.Where(e => e.Removed));
                foreach (var toRemove in intervalsToRemove)
                {
                    intervalTimers.Remove(toRemove);
                }
            }
            return false;
        }

        public virtual void Apply(ActionType type, float deltaTime, Entity entity, ISerializableEntity target, Vector2? worldPosition = null)
        {
            if (Disabled) { return; }
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
            if (Disabled) { return; }
            if (this.type != type) { return; }
            if (ShouldWaitForInterval(entity, deltaTime)) { return; }

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
            if (Disabled) { return; }
            if (lifeTime > 0)
            {
                lifeTimer -= deltaTime;
                if (lifeTimer <= 0) { return; }
            }
            if (ShouldWaitForInterval(entity, deltaTime)) { return; }

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
                    if (targets[i] is not Item item) { continue; }                
                    for (int j = 0; j < useItemCount; j++)
                    {
                        if (item.Removed) { continue; }
                        item.Use(deltaTime, useTargetCharacter, useTargetLimb);
                    }
                }
            }

            if (dropItem)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    if (targets[i] is Item item)
                    {
                        item.Drop(dropper: null);
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
                    else if (targets[i] is Character character && character.Inventory != null)
                    {
                        foreach (var containedItem in character.Inventory.AllItemsMod)
                        {
                            containedItem.Drop(dropper: null);
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
                                break;
                            }
                        }
                    }
                    if (targetLimb != null)
                    {
                        if (breakLimb)
                        {
                            targetLimb.character.TrySeverLimbJoints(targetLimb, severLimbsProbability: 1, damage: -1, allowBeheading: true, ignoreSeveranceProbabilityModifier: true, attacker: user);
                        }
                        if (hideLimb)
                        {
                            targetLimb.HideAndDisable(hideLimbTimer);
                        }
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
                    if (target?.SerializableProperties == null) { continue; }
                    if (target is Entity targetEntity)
                    {
                        if (targetEntity.Removed) { continue; }
                    }
                    else if (target is Limb limb)
                    {
                        if (limb.Removed) { continue; }
                        position = limb.WorldPosition + Offset;
                    }
                    foreach (var (propertyName, value) in PropertyEffects)
                    {
                        if (!target.SerializableProperties.TryGetValue(propertyName, out SerializableProperty property))
                        {
                            continue;
                        }
                        ApplyToProperty(target, property, value, deltaTime);
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
                            RegisterTreatmentResults(user, entity as Item, limb, affliction, result);
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
                        RegisterTreatmentResults(user, entity as Item, limb, affliction, result);
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
                        if (!targetCharacter.IsDead)
                        {
                            float healthChange = targetCharacter.Vitality - prevVitality;
                            targetCharacter.AIController?.OnHealed(healer: user, healthChange);
                            if (user != null)
                            {
                                targetCharacter.TryAdjustHealerSkill(user, healthChange);
#if SERVER
                                GameMain.Server.KarmaManager.OnCharacterHealthChanged(targetCharacter, user, -healthChange, 0.0f);
#endif
                            }
                        }
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

                    Character entityCharacter = entity as Character;
                    targetCharacter ??= entityCharacter;
                    if (targetCharacter != null && !targetCharacter.Removed && !targetCharacter.IsPlayer)
                    {
                        if (targetCharacter.AIController is EnemyAIController enemyAI)
                        {
                            foreach (AITrigger trigger in aiTriggers)
                            {
                                if (Rand.Value(Rand.RandSync.Unsynced) > trigger.Probability) { continue; }
                                if (entityCharacter != targetCharacter)
                                {
                                    if (target is Limb targetLimb && targetCharacter.LastDamage.HitLimb is Limb hitLimb)
                                    {
                                        if (hitLimb != targetLimb) { continue; }
                                    }
                                }
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
                                targetCharacter.Info?.IncreaseSkillLevel(skillIdentifier, giveSkill.Amount, !giveSkill.TriggerTalents);
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
                        if (!TalentTree.JobTalentTrees.TryGet(targetCharacter.Info.Job.Prefab.Identifier, out TalentTree characterTalentTree)) { continue; }

                        foreach (GiveTalentInfo giveTalentInfo in giveTalentInfos)
                        {
                            if (giveTalentInfo.GiveRandom)
                            {                        
                                // for the sake of technical simplicity, for now do not allow talents to be given if the character could unlock them in their talent tree as well
                                IEnumerable<Identifier> viableTalents = giveTalentInfo.TalentIdentifiers.Where(id => !targetCharacter.Info.UnlockedTalents.Contains(id) && !characterTalentTree.AllTalentIdentifiers.Contains(id));
                                if (viableTalents.None()) { continue; }
                                targetCharacter.GiveTalent(viableTalents.GetRandomUnsynced(), true);
                            }
                            else
                            {
                                foreach (Identifier id in giveTalentInfo.TalentIdentifiers)
                                {
                                    if (targetCharacter.Info.UnlockedTalents.Contains(id) || characterTalentTree.AllTalentIdentifiers.Contains(id)) { continue; }
                                    targetCharacter.GiveTalent(id, true);
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
                            IEnumerable<ISerializableEntity> eventTargets = targets.Where(t => t is Entity);
                            if (eventTargets.Any())
                            {
                                scriptedEvent.Targets.Add(triggeredEventTargetTag, eventTargets.Cast<Entity>().ToList());
                            }
                        }
                        if (!triggeredEventEntityTag.IsEmpty && entity != null)
                        {
                            scriptedEvent.Targets.Add(triggeredEventEntityTag, new List<Entity> { entity });
                        }
                        if (!triggeredEventUserTag.IsEmpty && user != null)
                        {
                            scriptedEvent.Targets.Add(triggeredEventUserTag, new List<Entity> { user });
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
                                if (characterSpawnInfo.TotalMaxCount > 0)
                                {
                                    if (Character.CharacterList.Count(c => c.SpeciesName == characterSpawnInfo.SpeciesName && c.TeamID == newCharacter.TeamID) > characterSpawnInfo.TotalMaxCount)
                                    {
                                        Entity.Spawner?.AddEntityToRemoveQueue(newCharacter);
                                        return;
                                    }
                                }
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
                                if (!characterSpawnInfo.AfflictionOnSpawn.IsEmpty)
                                {
                                    if (!AfflictionPrefab.Prefabs.TryGet(characterSpawnInfo.AfflictionOnSpawn, out AfflictionPrefab afflictionPrefab))
                                    {
                                        DebugConsole.NewMessage($"Could not apply an affliction to the spawned character(s). No affliction with the identifier \"{characterSpawnInfo.AfflictionOnSpawn}\" found.", Color.Red);
                                        return;
                                    }
                                    newCharacter.CharacterHealth.ApplyAffliction(newCharacter.AnimController.MainLimb, afflictionPrefab.Instantiate(characterSpawnInfo.AfflictionStrength));
                                }
                                if (characterSpawnInfo.Stun > 0)
                                {
                                    newCharacter.SetStun(characterSpawnInfo.Stun);
                                }
                                foreach (var target in targets)
                                {
                                    if (!(target is Character character)) { continue; }
                                    if (characterSpawnInfo.TransferInventory && character.Inventory != null && newCharacter.Inventory != null)
                                    {
                                        if (character.Inventory.Capacity != newCharacter.Inventory.Capacity) { return; }
                                        for (int i = 0; i < character.Inventory.Capacity && i < newCharacter.Inventory.Capacity; i++)
                                        {
                                            character.Inventory.GetItemsAt(i).ForEachMod(item => newCharacter.Inventory.TryPutItem(item, i, allowSwapping: true, allowCombine: false, user: null));
                                        }
                                    }
                                    if (characterSpawnInfo.TransferBuffs || characterSpawnInfo.TransferAfflictions)
                                    {
                                        foreach (Affliction affliction in character.CharacterHealth.GetAllAfflictions())
                                        {
                                            if (!characterSpawnInfo.TransferAfflictions && characterSpawnInfo.TransferBuffs && affliction.Prefab.IsBuff)
                                            {
                                                newCharacter.CharacterHealth.ApplyAffliction(newCharacter.AnimController.MainLimb, affliction.Prefab.Instantiate(affliction.Strength));
                                            }
                                            if (characterSpawnInfo.TransferAfflictions)
                                            {
                                                newCharacter.CharacterHealth.ApplyAffliction(newCharacter.AnimController.MainLimb, affliction.Prefab.Instantiate(affliction.Strength));
                                            }
                                        }
                                    }
                                    if (i == characterSpawnInfo.Count) // Only perform the below actions if this is the last character being spawned.
                                    {
                                        if (characterSpawnInfo.TransferControl)
                                        {
#if CLIENT
                                            if (Character.Controlled == target)
                                            {
                                                Character.Controlled = newCharacter;
                                            }
#elif SERVER
                                            foreach (Client c in GameMain.Server.ConnectedClients)
                                            {
                                                if (c.Character != target) { continue; }                                                
                                                GameMain.Server.SetClientCharacter(c, newCharacter);                                                
                                            }
#endif
                                        }
                                        if (characterSpawnInfo.RemovePreviousCharacter) { Entity.Spawner?.AddEntityToRemoveQueue(character); }
                                    }                                    
                                }
                            });
                    }
                }

                if (spawnItemRandomly)
                {
                    if (spawnItems.Count > 0)
                    {
                        SpawnItem(spawnItems.GetRandomUnsynced());
                    }
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
                    Item parentItem = entity as Item;
                    if (user == null && parentItem != null)
                    {
                        // Set the user for projectiles spawned from status effects (e.g. flak shrapnels)
                        SetUser(parentItem.GetComponent<Projectile>()?.User);
                    }
                    switch (chosenItemSpawnInfo.SpawnPosition)
                    {
                        case ItemSpawnInfo.SpawnPositionType.This:
                            Entity.Spawner.AddItemToSpawnQueue(chosenItemSpawnInfo.ItemPrefab, position + Rand.Vector(chosenItemSpawnInfo.Spread, Rand.RandSync.Unsynced), onSpawned: newItem =>
                            {
                                Projectile projectile = newItem.GetComponent<Projectile>();
                                if (entity != null)
                                {
                                    var rope = newItem.GetComponent<Rope>();
                                    if (rope != null && sourceBody != null && sourceBody.UserData is Limb sourceLimb)
                                    {
                                        rope.Attach(sourceLimb, newItem);
#if SERVER
                                        newItem.CreateServerEvent(rope);
#endif
                                    }
                                    float spread = Rand.Range(-chosenItemSpawnInfo.AimSpreadRad, chosenItemSpawnInfo.AimSpreadRad);
                                    float rotation = chosenItemSpawnInfo.RotationRad;
                                    Vector2 worldPos;
                                    if (sourceBody != null)
                                    {
                                        worldPos = sourceBody.Position;
                                        if (user?.Submarine != null)
                                        {
                                            worldPos += user.Submarine.Position;
                                        }
                                    }
                                    else
                                    {
                                        worldPos = entity.WorldPosition;
                                    }
                                    switch (chosenItemSpawnInfo.RotationType)
                                    {
                                        case ItemSpawnInfo.SpawnRotationType.Fixed:
                                            if (sourceBody != null)
                                            {
                                                rotation = sourceBody.TransformRotation(chosenItemSpawnInfo.RotationRad);
                                            }
                                            else if (parentItem?.body != null)
                                            {
                                                rotation = parentItem.body.TransformRotation(chosenItemSpawnInfo.RotationRad);
                                            }
                                            break;
                                        case ItemSpawnInfo.SpawnRotationType.Target:
                                            rotation = MathUtils.VectorToAngle(entity.WorldPosition - worldPos);
                                            break;
                                        case ItemSpawnInfo.SpawnRotationType.Limb:
                                            if (sourceBody != null)
                                            {
                                                rotation = sourceBody.TransformedRotation;
                                            }
                                            break;
                                        case ItemSpawnInfo.SpawnRotationType.Collider:
                                            if (parentItem?.body != null)
                                            {
                                                rotation = parentItem.body.Rotation;
                                            }
                                            else if (user != null)
                                            {
                                                rotation = user.AnimController.Collider.Rotation + MathHelper.PiOver2;
                                            }
                                            break;
                                        case ItemSpawnInfo.SpawnRotationType.MainLimb:
                                            if (user != null)
                                            {
                                                rotation = user.AnimController.MainLimb.body.TransformedRotation;
                                            }
                                            break;
                                        case ItemSpawnInfo.SpawnRotationType.Random:
                                            if (projectile != null)
                                            {
                                                DebugConsole.LogError("Random rotation is not supported for Projectiles.");
                                            }
                                            else
                                            {
                                                rotation = Rand.Range(0f, MathHelper.TwoPi, Rand.RandSync.Unsynced);
                                            }
                                            break;
                                        default:
                                            throw new NotImplementedException("Item spawn rotation type not implemented: " + chosenItemSpawnInfo.RotationType);
                                    }
                                    if (user != null)
                                    {
                                        rotation += chosenItemSpawnInfo.RotationRad * user.AnimController.Dir;
                                    }
                                    rotation += spread;
                                    if (projectile != null)
                                    {
                                        Vector2 spawnPos;
                                        if (projectile.Hitscan)
                                        {
                                            spawnPos = sourceBody != null ? sourceBody.SimPosition : entity.SimPosition;
                                        }
                                        else
                                        {
                                            spawnPos = ConvertUnits.ToSimUnits(worldPos);
                                        }
                                        projectile.Shoot(user, spawnPos, spawnPos, rotation,
                                            ignoredBodies: user?.AnimController.Limbs.Where(l => !l.IsSevered).Select(l => l.body.FarseerBody).ToList(), createNetworkEvent: true);
                                    }
                                    else if (newItem.body != null)
                                    {
                                        newItem.body.SetTransform(newItem.SimPosition, rotation);
                                        Vector2 impulseDir = new Vector2(MathF.Cos(rotation), MathF.Sin(rotation));
                                        newItem.body.ApplyLinearImpulse(impulseDir * chosenItemSpawnInfo.Impulse);
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
                                    foreach (ItemContainer itemContainer in item.GetComponents<ItemContainer>())
                                    {
                                        if (itemContainer.CanBeContained(chosenItemSpawnInfo.ItemPrefab))
                                        {
                                            inventory = itemContainer?.Inventory;
                                            break;
                                        }
                                    }
                                    if (!chosenItemSpawnInfo.SpawnIfCantBeContained && inventory == null)
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

            if (oneShot)
            {
                Disabled = true;
            }
            if (Interval > 0.0f && entity != null)
            {
                intervalTimers[entity] = Interval;
            }

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

        private void ApplyToProperty(ISerializableEntity target, SerializableProperty property, object value, float deltaTime)
        {
            if (disableDeltaTime || setValue) { deltaTime = 1.0f; }
            if (value is int || value is float)
            {
                float propertyValueF = property.GetFloatValue(target);
                if (property.PropertyType == typeof(float))
                {
                    float floatValue = value is float single ? single : (int)value;
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
                    int intValue = (int)(value is float single ? single * deltaTime : (int)value * deltaTime);
                    if (!setValue)
                    {
                        intValue += (int)propertyValueF;
                    }
                    property.TrySetValue(target, intValue);
                    return;
                }
            }
            else if (value is bool propertyValueBool)
            {
                property.TrySetValue(target, propertyValueBool);
                return;
            }
            property.TrySetValue(target, value);
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
                    if (target?.SerializableProperties != null)
                    {
                        foreach (var (propertyName, value) in element.Parent.PropertyEffects)
                        {
                            if (!target.SerializableProperties.TryGetValue(propertyName, out SerializableProperty property))
                            {
                                continue;
                            }
                            element.Parent.ApplyToProperty(target, property, value, CoroutineManager.DeltaTime);
                        }
                    }

                    foreach (Affliction affliction in element.Parent.Afflictions)
                    {
                        Affliction newAffliction = affliction;
                        if (target is Character character)
                        {
                            if (character.Removed) { continue; }
                            newAffliction = element.Parent.GetMultipliedAffliction(affliction, element.Entity, character, deltaTime, element.Parent.multiplyAfflictionsByMaxVitality);
                            var result = character.AddDamage(character.WorldPosition, newAffliction.ToEnumerable(), stun: 0.0f, playSound: false, attacker: element.User);
                            element.Parent.RegisterTreatmentResults(element.Parent.user, element.Entity as Item, result.HitLimb, affliction, result);
                        }
                        else if (target is Limb limb)
                        {
                            if (limb.character.Removed || limb.Removed) { continue; }
                            newAffliction = element.Parent.GetMultipliedAffliction(affliction, element.Entity, limb.character, deltaTime, element.Parent.multiplyAfflictionsByMaxVitality);
                            var result = limb.character.DamageLimb(limb.WorldPosition, limb, newAffliction.ToEnumerable(), stun: 0.0f, playSound: false, attackImpulse: 0.0f, attacker: element.User);
                            element.Parent.RegisterTreatmentResults(element.Parent.user, element.Entity as Item, limb, affliction, result);
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
                            if (!targetCharacter.IsDead)
                            {
                                float healthChange = targetCharacter.Vitality - prevVitality;
                                targetCharacter.AIController?.OnHealed(healer: element.User, healthChange);
                                if (element.User != null)
                                {
                                    targetCharacter.TryAdjustHealerSkill(element.User, healthChange);
#if SERVER
                                    GameMain.Server.KarmaManager.OnCharacterHealthChanged(targetCharacter, element.User, -healthChange, 0.0f);
#endif
                                }
                            }
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
            float afflictionMultiplier = !setValue && !disableDeltaTime ? deltaTime : 1.0f;
            if (entity is Item sourceItem)
            {
                if (sourceItem.HasTag("medical"))
                {
                    afflictionMultiplier *= 1 + targetCharacter.GetStatValue(StatTypes.MedicalItemEffectivenessMultiplier);
                    if (user is not null)
                    {
                        afflictionMultiplier *= 1 + user.GetStatValue(StatTypes.MedicalItemApplyingMultiplier);
                    }
                }
                else if (sourceItem.HasTag(AfflictionPrefab.PoisonType) && user is not null)
                {
                    afflictionMultiplier *= 1 + user.GetStatValue(StatTypes.PoisonMultiplier);
                }
            }
            return afflictionMultiplier * AfflictionMultiplier;
        }

        private Affliction GetMultipliedAffliction(Affliction affliction, Entity entity, Character targetCharacter, float deltaTime, bool? multiplyByMaxVitality)
        {
            float afflictionMultiplier = GetAfflictionMultiplier(entity, targetCharacter, deltaTime);
            if (multiplyByMaxVitality ?? affliction.MultiplyByMaxVitality)
            {
                afflictionMultiplier *= targetCharacter.MaxVitality / 100f;
            }
            if (user is not null)
            {
                if (affliction.Prefab.IsBuff)
                {
                    afflictionMultiplier *= 1 + user.GetStatValue(StatTypes.BuffItemApplyingMultiplier);
                }
                else if (affliction.Prefab.Identifier == "organdamage" && targetCharacter.CharacterHealth.GetActiveAfflictionTags().Any(t => t == "poisoned"))
                {
                    afflictionMultiplier *= 1 + user.GetStatValue(StatTypes.PoisonMultiplier);
                }
            }
            if (!MathUtils.NearlyEqual(afflictionMultiplier, 1.0f))
            {
                return affliction.CreateMultiplied(afflictionMultiplier, affliction);
            }
            return affliction;
        }

        private void RegisterTreatmentResults(Character user, Item item, Limb limb, Affliction affliction, AttackResult result)
        {
            if (item == null) { return; }
            if (!item.UseInHealthInterface) { return; }
            if (limb == null) { return; }
            foreach (Affliction limbAffliction in limb.character.CharacterHealth.GetAllAfflictions())
            {
                if (result.Afflictions != null && result.Afflictions.Any(a => a.Prefab == limbAffliction.Prefab) &&
                    (!affliction.Prefab.LimbSpecific || limb.character.CharacterHealth.GetAfflictionLimb(affliction) == limb))
                {
                    if (type == ActionType.OnUse || type == ActionType.OnSuccess)
                    {
                        limbAffliction.AppliedAsSuccessfulTreatmentTime = Timing.TotalTime;
                        limb.character.TryAdjustHealerSkill(user, affliction: affliction);
                    }
                    else if (type == ActionType.OnFailure)
                    {
                        limbAffliction.AppliedAsFailedTreatmentTime = Timing.TotalTime;
                        limb.character.TryAdjustHealerSkill(user, affliction: affliction);
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
