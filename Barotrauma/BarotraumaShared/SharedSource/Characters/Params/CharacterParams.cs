using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Xml;
using System.Linq;
using Barotrauma.Extensions;
#if CLIENT
using SoundType = Barotrauma.CharacterSound.SoundType;
#endif

namespace Barotrauma
{
    /// <summary>
    /// Contains character data that should be editable in the character editor.
    /// </summary>
    class CharacterParams : EditableParams
    {
        [Serialize("", true), Editable]
        public string SpeciesName { get; private set; }

        [Serialize("", true, description: "If the creature is a variant that needs to use a pre-existing translation."), Editable]
        public string SpeciesTranslationOverride { get; private set; }

        [Serialize("", true, description: "If the display name is not defined, the game first tries to find the translated name. If that is not found, the species name will be used."), Editable]
        public string DisplayName { get; private set; }

        [Serialize("", true, description: "If defined, different species of the same group are considered like the characters of the same species by the AI."), Editable]
        public string Group { get; private set; }

        [Serialize(false, true), Editable(ReadOnly = true)]
        public bool Humanoid { get; private set; }

        [Serialize(false, true), Editable(ReadOnly = true)]
        public bool HasInfo { get; private set; }

        [Serialize(false, true, description: "Can the creature interact with items?"), Editable]
        public bool CanInteract { get; private set; }

        [Serialize(false, true), Editable]
        public bool Husk { get; private set; }

        [Serialize(false, true), Editable]
        public bool UseHuskAppendage { get; private set; }

        [Serialize(false, true), Editable]
        public bool NeedsAir { get; set; }

        [Serialize(false, true, description: "Can the creature live without water or does it die on dry land?"), Editable]
        public bool NeedsWater { get; set; }

        [Serialize(false, false), Editable]
        public bool CanSpeak { get; set; }

        [Serialize(false, true), Editable]
        public bool UseBossHealthBar { get; private set; }

        [Serialize(100f, true, description: "How much noise the character makes when moving?"), Editable(minValue: 0f, maxValue: 100000f)]
        public float Noise { get; set; }

        [Serialize(100f, true, description: "How visible the character is?"), Editable(minValue: 0f, maxValue: 100000f)]
        public float Visibility { get; set; }

        [Serialize("blood", true), Editable]
        public string BloodDecal { get; private set; }

        [Serialize("blooddrop", true), Editable]
        public string BleedParticleAir { get; private set; }

        [Serialize("waterblood", true), Editable]
        public string BleedParticleWater { get; private set; }

        [Serialize(1f, true), Editable]
        public float BleedParticleMultiplier { get; private set; }

        [Serialize(true, true, description: "Can the creature eat bodies? Used by player controlled creatures to allow them to eat. Currently applicable only to non-humanoids. To allow an AI controller to eat, just add an ai target with the state \"eat\""), Editable]
        public bool CanEat { get; set; }

        [Serialize(10f, true, description: "How effectively/easily the character eats other characters. Affects the forces, the amount of particles, and the time required before the target is eaten away"), Editable(MinValueFloat = 1, MaxValueFloat = 1000, ValueStep = 1)]
        public float EatingSpeed { get; set; }

        [Serialize(true, true), Editable]
        public bool UsePathFinding { get; set; }

        [Serialize(1f, true, "Decreases the intensive path finding call frequency. Set to a lower value for insignificant creatures to improve performance."), Editable(minValue: 0f, maxValue: 1f)]
        public float PathFinderPriority { get; set; }

        [Serialize(false, true), Editable]
        public bool HideInSonar { get; set; }

        [Serialize(false, true), Editable]
        public bool HideInThermalGoggles { get; set; }

        [Serialize(0f, true), Editable]
        public float SonarDisruption { get; set; }

        [Serialize(0f, true), Editable]
        public float DistantSonarRange { get; set; }

        [Serialize(25000f, true, "If the character is farther than this (in pixels) from the sub and the players, it will be disabled. The halved value is used for triggering simple physics where the ragdoll is disabled and only the main collider is updated."), Editable(MinValueFloat = 10000f, MaxValueFloat = 100000f)]
        public float DisableDistance { get; set; }

        [Serialize(10f, true, "How frequent the recurring idle and attack sounds are?"), Editable(MinValueFloat = 1f, MaxValueFloat = 100f)]
        public float SoundInterval { get; set; }

        public readonly string File;

        public XDocument VariantFile { get; private set; }

        public readonly List<SubParam> SubParams = new List<SubParam>();
        public readonly List<SoundParams> Sounds = new List<SoundParams>();
        public readonly List<ParticleParams> BloodEmitters = new List<ParticleParams>();
        public readonly List<ParticleParams> GibEmitters = new List<ParticleParams>();
        public readonly List<ParticleParams> DamageEmitters = new List<ParticleParams>();
        public readonly List<InventoryParams> Inventories = new List<InventoryParams>();
        public HealthParams Health { get; private set; }
        public AIParams AI { get; private set; }

        public CharacterParams(string file)
        {
            File = file;
            Load();
        }

        protected override string GetName() => "Character Config File";

        public override XElement MainElement => doc.Root.IsOverride() ? doc.Root.FirstElement() : doc.Root;

        public bool Load()
        {
            bool success = base.Load(File);
            if (doc.Root.IsCharacterVariant())
            {
                VariantFile = doc;
                var original = CharacterPrefab.FindBySpeciesName(doc.Root.GetAttributeString("inherit", string.Empty));
                success = Load(original.FilePath);
                CreateSubParams();
                TryLoadOverride(this, VariantFile.Root, SerializableProperties);
                foreach (XElement subElement in VariantFile.Root.Elements())
                {
                    var matchingParams = SubParams.FirstOrDefault(p => p.Name.Equals(subElement.Name.ToString(), StringComparison.OrdinalIgnoreCase));
                    if (matchingParams != null)
                    {
                        TryLoadOverride(matchingParams, subElement, matchingParams.SerializableProperties);
                        // TODO: Make recursive? In practice we don't have to go deeper than this, but the implementation would be a lot cleaner with recursion.
                        foreach (XElement subSubElement in subElement.Elements())
                        {
                            if (subSubElement.Name.ToString().Equals("item", StringComparison.OrdinalIgnoreCase)) { continue; }
                            var matchingSubParams = matchingParams.SubParams.FirstOrDefault(p => p.Name.Equals(subSubElement.Name.ToString(), StringComparison.OrdinalIgnoreCase));
                            if (matchingSubParams != null)
                            {
                                TryLoadOverride(matchingSubParams, subSubElement, matchingSubParams.SerializableProperties);
                            }
                        }
                    }
                }
                return success;
            }
            if (string.IsNullOrEmpty(SpeciesName) && MainElement != null)
            {
                //backwards compatibility
                SpeciesName = MainElement.GetAttributeString("name", "");
            }
            CreateSubParams();
            return success;
        }

        public bool Save(string fileNameWithoutExtension = null)
        {
            // Disable saving variants for now. Making it work probably requires more work.
            if (VariantFile != null) { return false; }
            Serialize();
            return base.Save(fileNameWithoutExtension, new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = true,
                NewLineOnAttributes = false
            });
        }

        public override bool Reset(bool forceReload = false)
        {
            if (forceReload)
            {
                return Load();
            }
            Deserialize(OriginalElement, alsoChildren: true);
            SubParams.ForEach(sp => sp.Reset());
            return true;
        }

        public bool CompareGroup(string group) => !string.IsNullOrWhiteSpace(group) && !string.IsNullOrWhiteSpace(Group) && group.Equals(Group, StringComparison.OrdinalIgnoreCase);

        protected void CreateSubParams()
        {
            SubParams.Clear();
            var health = MainElement.GetChildElement("health");
            if (health != null)
            {
                Health = new HealthParams(health, this);
                SubParams.Add(Health);
            }
            // TODO: support for multiple ai elements?
            var ai = MainElement.GetChildElement("ai");
            if (ai != null)
            {
                AI = new AIParams(ai, this);
                SubParams.Add(AI);
            }
            foreach (var element in MainElement.GetChildElements("bloodemitter"))
            {
                var emitter = new ParticleParams(element, this);
                BloodEmitters.Add(emitter);
                SubParams.Add(emitter);
            }
            foreach (var element in MainElement.GetChildElements("gibemitter"))
            {
                var emitter = new ParticleParams(element, this);
                GibEmitters.Add(emitter);
                SubParams.Add(emitter);
            }
            foreach (var element in MainElement.GetChildElements("damageemitter"))
            {
                var emitter = new ParticleParams(element, this);
                GibEmitters.Add(emitter);
                SubParams.Add(emitter);
            }
            foreach (var soundElement in MainElement.GetChildElements("sound"))
            {
                var sound = new SoundParams(soundElement, this);
                Sounds.Add(sound);
                SubParams.Add(sound);
            }
            foreach (var inventoryElement in MainElement.GetChildElements("inventory"))
            {
                var inventory = new InventoryParams(inventoryElement, this);
                Inventories.Add(inventory);
                SubParams.Add(inventory);
            }
        }

        private void TryLoadOverride(object parentObject, XElement element, Dictionary<string, SerializableProperty> properties)
        {
            foreach (var property in properties)
            {
                var matchingAttribute = element.GetAttribute(property.Key);
                if (matchingAttribute != null)
                {
                    property.Value.TrySetValue(parentObject, matchingAttribute.Value);
                }
            }
        }

        public bool Deserialize(XElement element = null, bool alsoChildren = true, bool recursive = true, bool loadDefaultValues = true)
        {
            if (base.Deserialize(element))
            {
                //backwards compatibility
                if (string.IsNullOrEmpty(SpeciesName))
                {
                    SpeciesName = element.GetAttributeString("name", "[NAME NOT GIVEN]");
                }
                if (alsoChildren)
                {
                    SubParams.ForEach(p => p.Deserialize(recursive));
                }
                return true;
            }
            return false;
        }

        public bool Serialize(XElement element = null, bool alsoChildren = true, bool recursive = true)
        {
            if (base.Serialize(element))
            {
                if (alsoChildren)
                {
                    SubParams.ForEach(p => p.Serialize(recursive));
                }
                return true;
            }
            return false;
        }

#if CLIENT
        public void AddToEditor(ParamsEditor editor, bool alsoChildren = true, bool recursive = true, int space = 0)
        {
            base.AddToEditor(editor);
            if (alsoChildren)
            {
                SubParams.ForEach(s => s.AddToEditor(editor, recursive));
            }
            if (space > 0)
            {
                new GUIFrame(new RectTransform(new Point(editor.EditorBox.Rect.Width, (int)(space * GUI.yScale)), editor.EditorBox.Content.RectTransform), style: null, color: ParamsEditor.Color)
                {
                    CanBeFocused = false
                };
            }
        }
#endif

        public bool AddSound() => TryAddSubParam(new XElement("sound"), (e, c) => new SoundParams(e, c), out _, Sounds);

        public void AddInventory() => TryAddSubParam(new XElement("inventory", new XElement("item")), (e, c) => new InventoryParams(e, c), out _, Inventories);

        public void AddBloodEmitter() => AddEmitter("bloodemitter");
        public void AddGibEmitter() => AddEmitter("gibemitter");
        public void AddDamageEmitter() => AddEmitter("damageemitter");

        private void AddEmitter(string type)
        {
            switch (type)
            {
                case "gibemitter":
                    TryAddSubParam(new XElement(type), (e, c) => new ParticleParams(e, c), out _, GibEmitters);
                    break;
                case "bloodemitter":
                    TryAddSubParam(new XElement(type), (e, c) => new ParticleParams(e, c), out _, BloodEmitters);
                    break;
                case "damageemitter":
                    TryAddSubParam(new XElement(type), (e, c) => new ParticleParams(e, c), out _, DamageEmitters);
                    break;
                default: throw new NotImplementedException(type);
            }
        }

        public bool RemoveSound(SoundParams soundParams) => RemoveSubParam(soundParams);
        public bool RemoveBloodEmitter(ParticleParams emitter) => RemoveSubParam(emitter, BloodEmitters);
        public bool RemoveGibEmitter(ParticleParams emitter) => RemoveSubParam(emitter, GibEmitters);
        public bool RemoveDamageEmitter(ParticleParams emitter) => RemoveSubParam(emitter, DamageEmitters);
        public bool RemoveInventory(InventoryParams inventory) => RemoveSubParam(inventory, Inventories);

        protected bool RemoveSubParam<T>(T subParam, IList<T> collection = null) where T : SubParam
        {
            if (subParam == null || subParam.Element == null || subParam.Element.Parent == null) { return false; }
            if (collection != null && !collection.Contains(subParam)) { return false; }
            if (!SubParams.Contains(subParam)) { return false; }
            collection?.Remove(subParam);
            SubParams.Remove(subParam);
            subParam.Element.Remove();
            return  true;
        }

        protected bool TryAddSubParam<T>(XElement element, Func<XElement, CharacterParams, T> constructor, out T subParam, IList<T> collection = null, Func<IList<T>, bool> filter = null) where T : SubParam
        {
            subParam = constructor(element, this);
            if (collection != null && filter != null)
            {
                if (filter(collection)) { return false; }
            }
            MainElement.Add(element);
            SubParams.Add(subParam);
            collection?.Add(subParam);
            return subParam != null;
        }

        #region Subparams
        public class SoundParams : SubParam
        {
            public override string Name => "Sound";

            [Serialize("", true), Editable]
            public string File { get; private set; }

#if CLIENT
            [Serialize(SoundType.Idle, true), Editable]
            public SoundType State { get; private set; }
#endif

            [Serialize(1000f, true), Editable(minValue: 0f, maxValue: 10000f)]
            public float Range { get; private set; }

            [Serialize(1.0f, true), Editable(minValue: 0f, maxValue: 2.0f)]
            public float Volume { get; private set; }

            [Serialize(Gender.None, true, description: "Is the sound gender specific?"), Editable()]
            public Gender Gender { get; private set; }

            public SoundParams(XElement element, CharacterParams character) : base(element, character) { }
        }

        public class ParticleParams : SubParam
        {
            private string name;
            public override string Name
            {
                get
                {
                    if (name == null && Element != null)
                    {
                        name = Element.Name.ToString().FormatCamelCaseWithSpaces();
                    }
                    return name;
                }
            }

            [Serialize("", true), Editable]
            public string Particle { get; set; }

            [Serialize(0f, true), Editable(-360f, 360f, decimals: 0)]
            public float AngleMin { get; private set; }

            [Serialize(0f, true), Editable(-360f, 360f, decimals: 0)]
            public float AngleMax { get; private set; }

            [Serialize(1.0f, true), Editable(0f, 100f, decimals: 2)]
            public float ScaleMin { get; private set; }

            [Serialize(1.0f, true), Editable(0f, 100f, decimals: 2)]
            public float ScaleMax { get; private set; }

            [Serialize(0f, true), Editable(0f, 10000f, decimals: 0)]
            public float VelocityMin { get; private set; }

            [Serialize(0f, true), Editable(0f, 10000f, decimals: 0)]
            public float VelocityMax { get; private set; }

            [Serialize(0f, true), Editable(0f, 100f, decimals: 2)]
            public float EmitInterval { get; private set; }

            [Serialize(0, true), Editable(0, 1000)]
            public int ParticlesPerSecond { get; private set; }

            [Serialize(0, true), Editable(0, 1000)]
            public int ParticleAmount { get; private set; }

            [Serialize(false, true), Editable]
            public bool HighQualityCollisionDetection { get; private set; }

            [Serialize(false, true), Editable]
            public bool CopyEntityAngle { get; private set; }

            public ParticleParams(XElement element, CharacterParams character) : base(element, character) { }
        }

        public class HealthParams : SubParam
        {
            public override string Name => "Health";

            [Serialize(100f, true, description: "How much (max) health does the character have?"), Editable(minValue: 1, maxValue: 10000f)]
            public float Vitality { get; set; }

            [Serialize(true, true), Editable]
            public bool DoesBleed { get; set; }

            [Serialize(float.NegativeInfinity, true), Editable(minValue: float.NegativeInfinity, maxValue: 0)]
            public float CrushDepth { get; set; }

            // Make editable?
            [Serialize(false, true)]
            public bool UseHealthWindow { get; set; }

            [Serialize(0f, true, description: "How easily the character heals from the bleeding wounds. Default 0 (no extra healing)."), Editable(MinValueFloat = 0, MaxValueFloat = 100, DecimalCount = 2)]
            public float BleedingReduction { get; private set; }

            [Serialize(0f, true, description: "How easily the character heals from the burn wounds. Default 0 (no extra healing)."), Editable(MinValueFloat = 0, MaxValueFloat = 100, DecimalCount = 2)]
            public float BurnReduction { get; private set; }

            [Serialize(0f, true), Editable(MinValueFloat = 0, MaxValueFloat = 10, DecimalCount = 2)]
            public float ConstantHealthRegeneration { get; private set; }

            [Serialize(0f, true), Editable(MinValueFloat = 0, MaxValueFloat = 10, DecimalCount = 2)]
            public float HealthRegenerationWhenEating { get; private set; }

            [Serialize(false, true), Editable]
            public bool StunImmunity { get; set; }

            [Serialize(false, true, description: "Can afflictions affect the face/body tint of the character."), Editable]
            public bool ApplyAfflictionColors { get; private set; }

            // TODO: limbhealths, sprite?

            public HealthParams(XElement element, CharacterParams character) : base(element, character) { }
        }

        public class InventoryParams : SubParam
        {
            public class InventoryItem : SubParam
            {
                public override string Name => "Item";

                [Serialize("", true, description: "Item identifier."), Editable()]
                public string Identifier { get; private set; }

                public InventoryItem(XElement element, CharacterParams character) : base(element, character) { }
            }

            public override string Name => "Inventory";

            [Serialize("Any, Any", true, description: "Which slots the inventory holds? Accepted types: None, Any, RightHand, LeftHand, Head, InnerClothes, OuterClothes, Headset, and Card."), Editable()]
            public string Slots { get; private set; }

            [Serialize(false, true), Editable]
            public bool AccessibleWhenAlive { get; private set; }

            [Serialize(1.0f, true, description: "What are the odds that this inventory is spawned on the character?"), Editable(minValue: 0f, maxValue: 1.0f)]
            public float Commonness { get; private set; }

            public List<InventoryItem> Items { get; private set; } = new List<InventoryItem>();

            public InventoryParams(XElement element, CharacterParams character) : base(element, character)
            {
                foreach (var itemElement in element.GetChildElements("item"))
                {
                    var item = new InventoryItem(itemElement, character);
                    SubParams.Add(item);
                    Items.Add(item);
                }
            }

            public void AddItem(string identifier = null)
            {
                identifier = identifier ?? "";
                var element = new XElement("item", new XAttribute("identifier", identifier));
                Element.Add(element);
                var item = new InventoryItem(element, Character);
                SubParams.Add(item);
                Items.Add(item);
            }

            public bool RemoveItem(InventoryItem item) => RemoveSubParam(item, Items);
        }

        public class AIParams : SubParam
        {
            public override string Name => "AI";

            [Serialize(1.0f, true, description: "How strong other characters think this character is? Only affects AI."), Editable()]
            public float CombatStrength { get; private set; }

            [Serialize(1.0f, true, description: "Affects how far the character can see the targets. Used as a multiplier."), Editable(minValue: 0f, maxValue: 10f)]
            public float Sight { get; private set; }

            [Serialize(1.0f, true, description: "Affects how far the character can hear the targets. Used as a multiplier."), Editable(minValue: 0f, maxValue: 10f)]
            public float Hearing { get; private set; }

            [Serialize(100f, true, description: "How much the targeting priority increases each time the character takes damage. Works like the greed value, described above. The default value is 100."), Editable(minValue: -1000f, maxValue: 1000f)]
            public float AggressionHurt { get; private set; }

            [Serialize(10f, true, description: "How much the targeting priority increases each time the character does damage to the target. The actual priority adjustment is calculated based on the damage percentage multiplied by the greed value. The default value is 10, which means the priority will increase by 1 every time the character does damage 10% of the target's current health. If the damage is 50%, then the priority increase is 5."), Editable(minValue: 0f, maxValue: 1000f)]
            public float AggressionGreed { get; private set; }

            [Serialize(0f, true, description: "If the health drops below this threshold, the character flees. In percentages."), Editable(minValue: 0f, maxValue: 100f)]
            public float FleeHealthThreshold { get; private set; }

            [Serialize(false, true, description: "Does the character attack when provoked? When enabled, overrides the predefined targeting state with Attack and increases the priority of it."), Editable()]
            public bool AttackWhenProvoked { get; private set; }

            [Serialize(false, true, description: "The character will flee for a brief moment when being shot at if not performing an attack."), Editable]
            public bool AvoidGunfire { get; private set; }

            [Serialize(3f, true, description: "How long the creature avoids gunfire. Also used when the creature is unlatched."), Editable(minValue: 0f, maxValue: 100f)]
            public float AvoidTime { get; private set; }

            [Serialize(20f, true, description: "How long the creature flees before returning to normal state. When the creature sees the target or is being chased, it will always flee, if it's in the flee state."), Editable(minValue: 0f, maxValue: 100f)]
            public float MinFleeTime { get; private set; }

            [Serialize(false, true, description: "Does the character try to break inside the sub?"), Editable]
            public bool AggressiveBoarding { get; private set; }

            [Serialize(true, true, description: "Enforce aggressive behavior if the creature is spawned as a target of a monster mission."), Editable]
            public bool EnforceAggressiveBehaviorForMissions { get; private set; }

            [Serialize(true, true, description: "Should the character target or ignore walls when it's outside the submarine."), Editable]
            public bool TargetOuterWalls { get; private set; }

            [Serialize(false, true, description: "If enabled, the character chooses randomly from the available attacks. The priority is used as a weight for weighted random."), Editable]
            public bool RandomAttack { get; private set; }

            [Serialize(false, true, description:"Does the creature know how to open doors (still requires a proper ID card). Humans can always open doors (They don't use this AI definition)."), Editable]
            public bool CanOpenDoors { get; private set; }

            [Serialize(false, true, description: "Does the creature close the doors behind it. Humans don't use this AI definition."), Editable]
            public bool KeepDoorsClosed { get; private set; }

            [Serialize(true, true, "Is the creature allowed to navigate from and into the depths of the abyss? When enabled, the creatures will try to avoid the depths."), Editable]
            public bool AvoidAbyss { get; set; }

            [Serialize(false, true, "Does the creature try to keep in the abyss? Has effect only when AvoidAbyss is false."), Editable]
            public bool StayInAbyss { get; set; }

            [Serialize(false, true, "Does the creature patrol the flooded hulls while idling inside a friendly submarine?"), Editable]
            public bool PatrolFlooded { get; set; }

            [Serialize(false, true, "Does the creature patrol the dry hulls while idling inside a friendly submarine?"), Editable]
            public bool PatrolDry { get; set; }

            [Serialize(0f, true, description: ""), Editable]
            public float StartAggression { get; private set; }

            [Serialize(100f, true, description: ""), Editable]
            public float MaxAggression { get; private set; }

            [Serialize(0f, true, description: ""), Editable]
            public float AggressionCumulation { get; private set; }

            [Serialize(WallTargetingMethod.Target, true, description: ""), Editable]
            public WallTargetingMethod WallTargetingMethod { get; private set; }

            public IEnumerable<TargetParams> Targets => targets;
            protected readonly List<TargetParams> targets = new List<TargetParams>();

            public AIParams(XElement element, CharacterParams character) : base(element, character)
            {
                if (element == null) { return; }
                element.GetChildElements("target").ForEach(t => TryAddTarget(t, out _));
                element.GetChildElements("targetpriority").ForEach(t => TryAddTarget(t, out _));
            }

            private bool TryAddTarget(XElement targetElement, out TargetParams target)
            {
                string tag = targetElement.GetAttributeString("tag", null);
                if (HasTag(tag))
                {
                    target = null;
                    DebugConsole.ThrowError($"Multiple targets with the same tag ('{tag}') defined! Only the first will be used!");
                    return false;
                }
                else
                {
                    target = new TargetParams(targetElement, Character);
                    targets.Add(target);
                    SubParams.Add(target);
                    return true;
                }
            }

            public bool TryAddEmptyTarget(out TargetParams targetParams) => TryAddNewTarget("newtarget" + targets.Count, AIState.Attack, 0f, out targetParams);

            public bool TryAddNewTarget(string tag, AIState state, float priority, out TargetParams targetParams)
            {
                var element = TargetParams.CreateNewElement(tag, state, priority);
                if (TryAddTarget(element, out targetParams))
                {
                    Element.Add(element);
                    return true;
                }
                else
                {
                    return false;
                }
            }

            public bool HasTag(string tag)
            {
                if (tag == null) { return false; }
                return targets.Any(t => t.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase));
            }

            public bool RemoveTarget(TargetParams target) => RemoveSubParam(target, targets);

            public bool TryGetTarget(Character character, out TargetParams target)
            {
                // Try to get a tag for the character's species name.
                target = target = targets.FirstOrDefault(t => string.Equals(t.Tag, character.SpeciesName, StringComparison.OrdinalIgnoreCase));
                // Try to get a tag for the character's species group if there isn't any for their species name. 
                if (target == null) { target = targets.FirstOrDefault(t => string.Equals(t.Tag, character.Params.Group.ToString().ToLowerInvariant() + "_group", StringComparison.OrdinalIgnoreCase)); }
                return target != null;
            }

            public bool TryGetTarget(string tag, out TargetParams target)
            {
                target = targets.FirstOrDefault(t => string.Equals(t.Tag, tag, StringComparison.OrdinalIgnoreCase));
                return target != null;
            }

            public bool TryGetAfflictionTarget(Character character, out TargetParams target)
            {
                List<string> afflictiontargets = new List<string>();
                foreach (Affliction affliction in character.CharacterHealth.GetAllAfflictions())
                {
                    var currentEffect = affliction.GetActiveEffect();
                    if (currentEffect != null && !string.IsNullOrEmpty(currentEffect.AITargetingTag) && !afflictiontargets.Contains(currentEffect.AITargetingTag))
                    {
                        afflictiontargets.Add(currentEffect.AITargetingTag);
                    }
                }
                
                // Get the uppermost target that has the same tag as an affliction target tag. This heavily relies on how the target tags are ordered in the XML file.
                target = targets.FirstOrDefault(t => string.Equals(t.Tag, afflictiontargets.Find(aitarget => string.Equals(aitarget, t.Tag, StringComparison.OrdinalIgnoreCase)), StringComparison.OrdinalIgnoreCase));

                return target != null;
            }

            public TargetParams GetTarget(string targetTag, bool throwError = true)
            {
                if (!TryGetTarget(targetTag, out TargetParams target))
                {
                    if (throwError)
                    {
                        DebugConsole.ThrowError($"Cannot find a target with the tag {targetTag}!");
                    }
                }
                return target;
            }
        }

        public class TargetParams : SubParam
        {
            public override string Name => "Target";

            [Serialize("", true, description: "Can be an item tag, species name or something else. Examples: decoy, provocative, light, dead, human, crawler, wall, nasonov, sonar, door, stronger, weaker, light, human, room..."), Editable()]
            public string Tag { get; private set; }

            [Serialize(AIState.Idle, true), Editable]
            public AIState State { get; set; }

            [Serialize(0f, true, description: "What base priority is given to the target?"), Editable(minValue: 0f, maxValue: 1000f, ValueStep = 1, DecimalCount = 0)]
            public float Priority { get; set; }

            [Serialize(0f, true, description: "Generic distance that can be used for different purposes depending on the state. E.g. in Avoid state this defines the distance that the character tries to keep to the target. If the distance is 0, it's not used."), Editable(MinValueFloat = 0, ValueStep = 10, DecimalCount = 0)]
            public float ReactDistance { get; set; }

            [Serialize(0f, true, description: "Used for defining the attack distance for PassiveAggressive and Aggressive states. If the distance is 0, it's not used."), Editable(MinValueFloat = 0, ValueStep = 10, DecimalCount = 0)]
            public float AttackDistance { get; set; }

            [Serialize(0f, true, description: "Generic timer that can be used for different purposes depending on the state. E.g. in Observe state this defines how long the character in general keeps staring the targets (Some random is always applied)."), Editable]
            public float Timer { get; set; }

            [Serialize(false, true, description: "Should the target be ignored if it's inside a container/inventory. Only affects items."), Editable]
            public bool IgnoreContained { get; set; }

            [Serialize(false, true, description: "Should the target be ignored while the creature is inside. Doesn't matter where the target is."), Editable]
            public bool IgnoreInside { get; set; }

            [Serialize(false, true, description: "Should the target be ignored while the creature is outside. Doesn't matter where the target is."), Editable]
            public bool IgnoreOutside { get; set; }

            [Serialize(false, true, description: "Should the target be ignored if it's inside a different submarine than us? Normally only some targets are ignored when they are not inside the same sub."), Editable]
            public bool IgnoreIfNotInSameSub { get; set; }

            [Serialize(false, true), Editable]
            public bool IgnoreIncapacitated { get; set; }

            [Serialize(0f, true, description: "A generic threshold. For example, how much damage the protected target should take from an attacker before the creature starts defending it."), Editable]
            public float Threshold { get; private set; }

            [Serialize(-1f, true, description: "A generic min threshold. Not used if set to negative."), Editable]
            public float ThresholdMin { get; private set; }

            [Serialize(-1f, true, description: "A generic max threshold. Not used if set to negative."), Editable]
            public float ThresholdMax { get; private set; }

            [Serialize("0.0, 0.0", true), Editable]
            public Vector2 Offset { get; private set; }

            [Serialize(10f, true, description: "How much damage should the character take from an attacker with an affliction AI target before it stops using that tag to deal with the attacker? E.g stop idling if the attacker deals any damage to you and attack."), Editable]
            public float MinDamageToIgnoreAfflictionTag { get; private set; }

            [Serialize(AttackPattern.Straight, true), Editable]
            public AttackPattern AttackPattern { get; set; }

            #region Sweep
            [Serialize(0f, true, description: "Use to define a distance at which the creature starts the sweeping movement."), Editable(MinValueFloat = 0, MaxValueFloat = 10000, ValueStep = 1, DecimalCount = 0)]
            public float SweepDistance { get; private set; }

            [Serialize(10f, true, description: "How much the sweep affects the steering?"), Editable(MinValueFloat = 0, MaxValueFloat = 100, ValueStep = 1f, DecimalCount = 1)]
            public float SweepStrength { get; private set; }

            [Serialize(1f, true, description: "How quickly the sweep direction changes. Uses the sine wave pattern."), Editable(MinValueFloat = 0, MaxValueFloat = 10, ValueStep = 0.1f, DecimalCount = 2)]
            public float SweepSpeed { get; private set; }
            #endregion

            #region Circle
            [Serialize(5000f, true), Editable(MinValueFloat = 0f, MaxValueFloat = 20000f)]
            public float CircleStartDistance { get; private set; }

            [Serialize(1f, true), Editable(MinValueFloat = 0.5f, MaxValueFloat = 2f)]
            public float CircleRotationSpeed { get; private set; }

            [Serialize(5f, true), Editable(MinValueFloat = 1f, MaxValueFloat = 10f)]
            public float CircleStrikeDistanceMultiplier { get; private set; }

            [Serialize(0f, true), Editable(MinValueFloat = 0f, MaxValueFloat = 50f)]
            public float CircleMaxRandomOffset { get; private set; }
            #endregion

            public TargetParams(XElement element, CharacterParams character) : base(element, character) { }

            public TargetParams(string tag, AIState state, float priority, CharacterParams character) : base(CreateNewElement(tag, state, priority), character) { }

            public static XElement CreateNewElement(string tag, AIState state, float priority)
            {
                return new XElement("target",
                            new XAttribute("tag", tag),
                            new XAttribute("state", state),
                            new XAttribute("priority", priority));
            }
        }

        public abstract class SubParam : ISerializableEntity
        {
            public virtual string Name { get; set; }
            public Dictionary<string, SerializableProperty> SerializableProperties { get; private set; }
            public XElement Element { get; set; }
            public List<SubParam> SubParams { get; set; } = new List<SubParam>();

            public CharacterParams Character { get; private set; }

            public SubParam(XElement element, CharacterParams character)
            {
                Element = element;
                Character = character;
                SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
            }

            public virtual bool Deserialize(bool recursive = true)
            {
                SerializableProperties = SerializableProperty.DeserializeProperties(this, Element);
                if (recursive)
                {
                    SubParams.ForEach(sp => sp.Deserialize(true));
                }
                return SerializableProperties != null;
            }

            public virtual bool Serialize(bool recursive = true)
            {
                SerializableProperty.SerializeProperties(this, Element, true);
                if (recursive)
                {
                    SubParams.ForEach(sp => sp.Serialize(true));
                }
                return true;
            }

            public virtual void Reset()
            {
                // Don't use recursion, because the reset method might be overriden
                Deserialize(false);
                SubParams.ForEach(sp => sp.Reset());
            }

            protected bool RemoveSubParam<T>(T subParam, IList<T> collection = null) where T : SubParam
            {
                if (subParam == null || subParam.Element == null || subParam.Element.Parent == null) { return false; }
                if (collection != null && !collection.Contains(subParam)) { return false; }
                if (!SubParams.Contains(subParam)) { return false; }
                collection?.Remove(subParam);
                SubParams.Remove(subParam);
                subParam.Element.Remove();
                return true;
            }

#if CLIENT
            public SerializableEntityEditor SerializableEntityEditor { get; protected set; }
            public virtual void AddToEditor(ParamsEditor editor, bool recursive = true, int space = 0, ScalableFont titleFont = null)
            {
                SerializableEntityEditor = new SerializableEntityEditor(editor.EditorBox.Content.RectTransform, this, inGame: false, showName: true, titleFont: titleFont ?? GUI.LargeFont);
                if (recursive)
                {
                    SubParams.ForEach(sp => sp.AddToEditor(editor, true, titleFont: titleFont ?? GUI.SmallFont));
                }
                if (space > 0)
                {
                    new GUIFrame(new RectTransform(new Point(editor.EditorBox.Rect.Width, space), editor.EditorBox.Content.RectTransform), style: null, color: new Color(20, 20, 20, 255))
                    {
                        CanBeFocused = false
                    };
                }
            }
#endif
        }
        #endregion
    }
}
