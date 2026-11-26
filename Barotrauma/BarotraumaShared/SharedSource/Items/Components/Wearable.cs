using Barotrauma.Abilities;
using Barotrauma.Extensions;
using Barotrauma.IO;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    public enum WearableType
    {
        Item,
        Hair,
        Beard,
        Moustache,
        FaceAttachment,
        Husk,
        Herpes
    }

    public class WearableSprite
    {
        public bool IsActive { get; private set; } = true;

        public ContentPath UnassignedSpritePath { get; private set; }
        public string SpritePath { get; private set; }
        public ContentXElement SourceElement { get; private set; }

        public WearableType Type { get; private set; }
        private Sprite _sprite;
        public Sprite Sprite
        {
            get { return _sprite; }
            private set
            {
                if (value == _sprite) { return; }
                if (_sprite != null)
                {
                    _sprite.Remove();
                }
                _sprite = value;
            }
        }
        public LimbType Limb { get; private set; }
        public bool HideLimb { get; private set; }

        public enum ObscuringMode
        {
            None,
            Hide,
            AlphaClip
        }
        public ObscuringMode ObscureOtherWearables { get; private set; }
        public bool HideOtherWearables => ObscureOtherWearables == ObscuringMode.Hide;
        public bool AlphaClipOtherWearables => ObscureOtherWearables == ObscuringMode.AlphaClip;
        public bool CanBeHiddenByOtherWearables { get; private set; }

        /// <summary>
        /// Tags or identifiers of items that can hide this one.
        /// </summary>
        public ImmutableHashSet<Identifier> CanBeHiddenByItem { get; private set; }
        public List<WearableType> HideWearablesOfType { get; private set; }
        public bool InheritLimbDepth { get; private set; }
        /// <summary>
        /// Does the wearable inherit all the scalings of the wearer? Also the wearable's own scale is used!
        /// </summary>
        public bool InheritScale { get; private set; }
        public bool IgnoreRagdollScale { get; private set; }
        public bool IgnoreLimbScale { get; private set; }
        public bool IgnoreTextureScale { get; private set; }
        public bool InheritOrigin { get; private set; }
        public bool InheritSourceRect { get; private set; }

        public float Scale { get; private set; }

        public float Rotation { get; private set; }

        public LimbType DepthLimb { get; private set; }
        public float RelativeDepth { get; private set; }

        private Wearable _wearableComponent;
        public Wearable WearableComponent
        {
            get { return _wearableComponent; }
            set
            {
                if (value == _wearableComponent) { return; }
                if (_wearableComponent != null)
                {
                    _wearableComponent.Remove();
                }
                _wearableComponent = value;
            }
        }
        public string Sound { get; private set; }
        public Point? SheetIndex { get; private set; }

        public LightComponent LightComponent => LightComponents?.FirstOrDefault();

        public List<LightComponent> LightComponents
        {
            get
            {
                if (_lightComponents == null)
                {
                    _lightComponents = new List<LightComponent>();
                }
                return _lightComponents;
            }
        }
        private List<LightComponent> _lightComponents;

        public int Variant { get; set; }

        private Character _picker;
        /// <summary>
        /// None = Any/Not Defined -> no effect.
        /// Changing the gender forces re-initialization, because the textures can be different for male and female characters.
        /// </summary>
        public Character Picker
        {
            get { return _picker; }
            set
            {
                if (value == _picker) { return; }
                _picker = value;
                IsInitialized = false;
                UnassignedSpritePath = ParseSpritePath(SourceElement);
                Init(_picker);
            }
        }

        /// <summary>The sprite is only drawn if these conditions are fulfilled.</summary>
        public ImmutableList<PropertyConditional> IsActiveConditionals { get; private set; } = [];

        public WearableSprite(ContentXElement subElement, WearableType type)
        {
            Type = type;
            SourceElement = subElement;
            UnassignedSpritePath = subElement.GetAttributeContentPath("texture") ?? ContentPath.Empty;
            Init();
            switch (type)
            {
                case WearableType.Hair:
                case WearableType.Beard:
                case WearableType.Moustache:
                case WearableType.FaceAttachment:
                case WearableType.Husk:
                case WearableType.Herpes:
                    Limb = LimbType.Head;
                    break;
            }
        }

        /// <summary>
        /// Note: this constructor cannot initialize automatically, because the gender is unknown at this point. We only know it when the item is equipped.
        /// </summary>
        public WearableSprite(ContentXElement subElement, Wearable wearable, int variant = 0)
        {
            Type = WearableType.Item;
            WearableComponent = wearable;
            Variant = Math.Max(variant, 0);
            UnassignedSpritePath = ParseSpritePath(subElement);
            SourceElement = subElement;
        }

        private ContentPath ParseSpritePath(ContentXElement element)
        {
            if (element.DoesAttributeReferenceFileNameAlone("texture"))
            {
                var basePrefab = WearableComponent.Item.Prefab.ParentPrefab ?? WearableComponent.Item.Prefab;
                string textureName = element.GetAttributeString("texture", "");
                return ContentPath.FromRaw(
                    element.ContentPackage,
                    $"{Path.GetDirectoryName(basePrefab.FilePath)}/{textureName}");
            }
            else
            {
                return element.GetAttributeContentPath("texture") ?? ContentPath.Empty;
            }
        }

        public void ParsePath(bool parseSpritePath)
        {
#if SERVER
            // Server doesn't care about texture paths at all
            return;
#endif
            SpritePath = UnassignedSpritePath.Value;
            if (_picker?.Info != null)
            {
                SpritePath = _picker.Info.ReplaceVars(SpritePath);
            }
            SpritePath = SpritePath.Replace("[VARIANT]", Variant.ToString());
            if (!File.Exists(SpritePath))
            {
                // If the variant does not exist, parse the path so that it uses first variant.
                SpritePath = SpritePath.Replace("[VARIANT]", "1");
            }
            if (!File.Exists(SpritePath) && _picker?.Info == null)
            {
                // If there's no character info is defined, try to use first tagset from CharacterInfoPrefab
                var charInfoPrefab = CharacterPrefab.HumanPrefab.CharacterInfoPrefab;
                SpritePath = charInfoPrefab.ReplaceVars(SpritePath, charInfoPrefab.Heads.First());
            }
            if (parseSpritePath)
            {
                Sprite?.ParseTexturePath(file: SpritePath);
            }
        }

        public bool IsInitialized { get; private set; }
        public void Init(Character picker = null)
        {
            if (IsInitialized) { return; }

            _picker = picker;
            ParsePath(false);
            Sprite?.Remove();
            Sprite = new Sprite(SourceElement, file: SpritePath);
            Limb = (LimbType)Enum.Parse(typeof(LimbType), SourceElement.GetAttributeString("limb", "Head"), true);
            HideLimb = SourceElement.GetAttributeBool("hidelimb", false);

            foreach (var mode in Enum.GetValues<ObscuringMode>())
            {
                if (mode == ObscuringMode.None) { continue; }
                if (SourceElement.GetAttributeBool($"{mode}OtherWearables", false))
                {
                    ObscureOtherWearables = mode;
                }
            }

            CanBeHiddenByOtherWearables = SourceElement.GetAttributeBool("canbehiddenbyotherwearables", true);
            CanBeHiddenByItem = SourceElement.GetAttributeIdentifierArray(nameof(CanBeHiddenByItem), Array.Empty<Identifier>()).ToImmutableHashSet();
            InheritLimbDepth = SourceElement.GetAttributeBool("inheritlimbdepth", true);
            var scale = SourceElement.GetAttribute("inheritscale");
            if (scale != null)
            {
                InheritScale = scale.GetAttributeBool(Type != WearableType.Item);
            }
            else
            {
                InheritScale = SourceElement.GetAttributeBool("inherittexturescale", Type != WearableType.Item);
            }
            IgnoreLimbScale = SourceElement.GetAttributeBool("ignorelimbscale", false);
            IgnoreTextureScale = SourceElement.GetAttributeBool("ignoretexturescale", false);
            IgnoreRagdollScale = SourceElement.GetAttributeBool("ignoreragdollscale", false);
            SourceElement.GetAttributeBool("inherittexturescale", false);
            InheritOrigin = SourceElement.GetAttributeBool("inheritorigin", Type != WearableType.Item);
            InheritSourceRect = SourceElement.GetAttributeBool("inheritsourcerect", Type != WearableType.Item);
            DepthLimb = (LimbType)Enum.Parse(typeof(LimbType), SourceElement.GetAttributeString("depthlimb", "None"), true);
            RelativeDepth = SourceElement.GetAttributeFloat(nameof(RelativeDepth), 0);
            Sound = SourceElement.GetAttributeString("sound", "");
            Scale = SourceElement.GetAttributeFloat("scale", 1.0f);
            Rotation = MathHelper.ToRadians(SourceElement.GetAttributeFloat("rotation", 0.0f));
            var index = SourceElement.GetAttributePoint("sheetindex", new Point(-1, -1));
            if (index.X > -1 && index.Y > -1)
            {
                SheetIndex = index;
            }

            HideWearablesOfType = new List<WearableType>();
            var wearableTypes = SourceElement.GetAttributeStringArray("hidewearablesoftype", null);
            if (wearableTypes != null && wearableTypes.Length > 0)
            {
                foreach (var value in wearableTypes)
                {
                    if (Enum.TryParse(value, ignoreCase: true, out WearableType wearableType))
                    {
                        HideWearablesOfType.Add(wearableType);
                    }
                }
            }

            ImmutableList<PropertyConditional>.Builder builder = ImmutableList.CreateBuilder<PropertyConditional>();
            foreach (ContentXElement subElement in SourceElement.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "conditional" or "isactiveconditional":
                        builder.AddRange(PropertyConditional.FromXElement(subElement));
                        break;
                }
            }
            IsActiveConditionals = builder.ToImmutable();

            IsInitialized = true;
        }

        public void UpdateIsActive() => IsActive = IsActiveConditionals.All(WearableComponent.Item.ConditionalMatches);

        public void Remove()
        {
            Sprite?.Remove();
            //don't use the Picker setter, because it causes the sprite to be re-initialized for "no character"
            _picker = null;
        }

        public void Equip(Limb limb)
        {
            if (WearableComponent.Item.body != null)
            {
                WearableComponent.Item.body.Enabled = false;
            }
            WearableComponent.IsActive = true;
            foreach (LightComponent light in LightComponents)
            {
                light.ParentBody = limb.body;
            }

            if (!limb.WearingItems.Contains(this))
            {
                limb.WearingItems.Add(this);
                limb.WearingItems.Sort(static (wearable, nextWearable) =>
                {
                    float depth = wearable?.Sprite?.Depth ?? 0f;
                    float nextDepth = nextWearable?.Sprite?.Depth ?? 0f;
                    return nextDepth.CompareTo(depth);
                });
                limb.WearingItems.Sort(static (wearable, nextWearable) =>
                {
                    Wearable wearableComponent = wearable?.WearableComponent;
                    Wearable nextWearableComponent = nextWearable?.WearableComponent;
                    if (wearableComponent == null && nextWearableComponent == null) { return 0; }
                    if (wearableComponent == null) { return -1; }
                    if (nextWearableComponent == null) { return 1; }
                    return wearableComponent.AllowedSlots.Contains(InvSlotType.OuterClothes).CompareTo(nextWearableComponent.AllowedSlots.Contains(InvSlotType.OuterClothes));
                });
            }
#if CLIENT
            limb.UpdateWearableTypesToHide();
#endif
        }

        public void Unequip(Limb limb)
        {
            foreach (LightComponent light in LightComponents)
            {
                light.ParentBody = null;
            }

            limb.WearingItems.RemoveAll(w => w == this);
#if CLIENT
            limb.UpdateWearableTypesToHide();
#endif
        }
    }
}

namespace Barotrauma.Items.Components
{
    public partial class Wearable : Pickable, IServerSerializable
    {
        private readonly ContentXElement[] wearableElements;
        private readonly WearableSprite[] wearableSprites;
        private readonly LimbType[] limbType;

        /// <summary>
        /// Did the wearable have the required items when it was last updated? Used to refresh the wearer's stat values if e.g. the battery in the item runs out.
        /// </summary>
        private bool hadRequiredItems;

        /// <summary>
        /// Which types of limbs this item replaces as a prosthetic?
        /// </summary>
        private readonly LimbType[] prostheticsLimbTypes;

        public IEnumerable<LimbType> ProstheticsLimbTypes => prostheticsLimbTypes;

        /// <summary>
        /// Which limb(s) is this item currently serving as a prosthetic for?
        /// </summary>
        private readonly List<Limb> prostheticForLimbs = [];

        /// <summary>
        /// Which limb(s) is this item currently serving as a prosthetic for?
        /// </summary>
        public IEnumerable<Limb> ProstheticForLimbs => prostheticForLimbs;

        private readonly List<DamageModifier> damageModifiers;
        public readonly Dictionary<Identifier, float> SkillModifiers;

        public readonly Dictionary<StatTypes, float> WearableStatValues = new Dictionary<StatTypes, float>();

        public IEnumerable<DamageModifier> DamageModifiers
        {
            get { return damageModifiers; }
        }

        public bool AutoEquipWhenFull { get; private set; }
        public bool DisplayContainedStatus { get; private set; }

        [Serialize(false, IsPropertySaveable.No, description: "Can the item be used (assuming it has components that are usable in some way) when worn.")]
        public bool AllowUseWhenWorn { get; set; }

        public readonly int Variants;

        private int variant;
        public int Variant
        {
            get { return variant; }
            set
            {
                if (variant == value) { return; }
#if SERVER                
                variant = value;
                if (!item.Submarine?.Loading ?? true)
                {
                    item.CreateServerEvent(this);
                }
#elif CLIENT

                Character character = picker;
                if (character != null)
                {
                    Unequip(character);
                }

                for (int i = 0; i < wearableSprites.Length; i++)
                {
                    var subElement = wearableElements[i];

                    wearableSprites[i]?.Sprite?.Remove();
                    wearableSprites[i] = new WearableSprite(subElement, this, value);
                }

                if (character != null)
                {
                    Equip(character);
                }

                variant = value;
#endif
            }
        }

        /// <summary>
        /// How deep down does the item protect from pressure? Determined by status effects.
        /// </summary>
        public readonly float PressureProtection;

        public Wearable(Item item, ContentXElement element) : base(item, element)
        {
            this.item = item;

            damageModifiers = new List<DamageModifier>();
            SkillModifiers = new Dictionary<Identifier, float>();

            int spriteCount = element.Elements().Count(x => x.Name.ToString().ToLowerInvariant() == "sprite");
            Variants = element.GetAttributeInt("variants", 0);
            variant = Rand.Range(1, Variants + 1, Rand.RandSync.ServerAndClient);
            wearableSprites = new WearableSprite[spriteCount];
            wearableElements = new ContentXElement[spriteCount];
            limbType = new LimbType[spriteCount];
            AutoEquipWhenFull = element.GetAttributeBool("autoequipwhenfull", true);
            DisplayContainedStatus = element.GetAttributeBool("displaycontainedstatus", false);

            prostheticsLimbTypes = element.GetAttributeEnumArray<LimbType>("prostheticslimbtypes", []);

            int i = 0;
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "sprite":
                        if (subElement.GetAttribute("texture") == null)
                        {
                            DebugConsole.ThrowError("Item \"" + item.Name + "\" doesn't have a texture specified!",
                                contentPackage: element.ContentPackage);
                            return;
                        }

                        limbType[i] = (LimbType)Enum.Parse(typeof(LimbType),
                            subElement.GetAttributeString("limb", "Head"), true);

                        wearableSprites[i] = new WearableSprite(subElement, this, variant);
                        wearableElements[i] = subElement;

                        foreach (var lightElement in subElement.Elements())
                        {
                            if (!lightElement.Name.ToString().Equals("lightcomponent", StringComparison.OrdinalIgnoreCase)) { continue; }
                            wearableSprites[i].LightComponents.Add(new LightComponent(item, lightElement)
                            {
                                Parent = this
                            });
                            foreach (var light in wearableSprites[i].LightComponents)
                            {
                                item.AddComponent(light);
                            }
                        }

                        i++;
                        break;
                    case "damagemodifier":
                        damageModifiers.Add(new DamageModifier(subElement, item.Name + ", Wearable"));
                        break;
                    case "skillmodifier":
                        Identifier skillIdentifier = subElement.GetAttributeIdentifier("skillidentifier", Identifier.Empty);
                        float skillValue = subElement.GetAttributeFloat("skillvalue", 0f);
                        if (SkillModifiers.ContainsKey(skillIdentifier))
                        {
                            SkillModifiers[skillIdentifier] += skillValue;
                        }
                        else
                        {
                            SkillModifiers.TryAdd(skillIdentifier, skillValue);
                        }
                        break;
                    case "statvalue":
                        StatTypes statType = CharacterAbilityGroup.ParseStatType(subElement.GetAttributeString("stattype", ""), Name);
                        float statValue = subElement.GetAttributeFloat("value", 0f);
                        if (MathUtils.NearlyEqual(statValue, 0.0f))
                        {
                            DebugConsole.AddWarning($"Potential error in {item.Name} ({nameof(Wearable)}): stat value {statType} has a value of 0, meaning it won't do anything.",
                                contentPackage: item.ContentPackage);
                        }
                        if (WearableStatValues.ContainsKey(statType))
                        {
                            WearableStatValues[statType] += statValue;
                        }
                        else
                        {
                            WearableStatValues.TryAdd(statType, statValue);
                        }
                        break;
                    case "statuseffect":
                        if (subElement.GetAttributeString("Target", string.Empty).ToLowerInvariant().Contains("character"))
                        {
                            PressureProtection = Math.Max(subElement.GetAttributeFloat(nameof(PressureProtection), 0), PressureProtection);
                        }
                        break;
                }
            }
        }

        public override void Equip(Character character)
        {
            Equip(character, disableEquipEffects: false);
        }

        public void Equip(Character character, bool disableEquipEffects)
        {
            foreach (var allowedSlot in allowedSlots)
            {
                if (allowedSlot == InvSlotType.Any) { continue; }
                foreach (Enum value in Enum.GetValues(typeof(InvSlotType)))
                {
                    var slotType = (InvSlotType)value;
                    if (slotType == InvSlotType.Any || slotType == InvSlotType.None) { continue; }
                    if (allowedSlot.HasFlag(slotType) && !character.Inventory.IsInLimbSlot(item, slotType))
                    {
                        return;
                    }
                }
            }

            bool isInProstheticSlot = false;
            foreach (var prostheticSlot in EnumExtensions.GetIndividualFlags(CharacterInventory.ProstheticSlots))
            {
                if (character.Inventory.IsInLimbSlot(item, prostheticSlot))
                {
                    isInProstheticSlot = true;
                    break;
                }
            }
            picker = character;

            if (isInProstheticSlot)
            {
                if (!TryEquipProsthetic(character, disableEquipEffects)) { return; }
            }

            for (int i = 0; i < wearableSprites.Length; i++ )
            {
                WearableSprite wearableSprite = wearableSprites[i];
                if (!wearableSprite.IsInitialized) { wearableSprite.Init(picker); }
                // If the item is gender specific (it has a different textures for male and female), we have to change the gender here so that the texture is updated.
                wearableSprite.Picker = picker;

                Limb equipLimb = character.AnimController.GetLimb(limbType[i], excludeSevered: false);
                if (equipLimb == null) { continue; }

                if (!isInProstheticSlot && equipLimb.IsSevered) // If this is being worn as a prosthesis, we can immediately add the item to the severed limb, as it's hidden.
                {
                    if (!equipLimb.SeveredWearableChanges.TryGetValue(wearableSprite, out bool equip))
                    {
                        equipLimb.SeveredWearableChanges.Add(wearableSprite, true);
                    }
                    else if (!equip)
                    {
                        equipLimb.SeveredWearableChanges.Remove(wearableSprite);
                    }
                    continue;
                }

                wearableSprite.Equip(equipLimb);
            }
            character.OnWearablesChanged();
        }

        private bool TryEquipProsthetic(Character character, bool disableEquipEffects = false)
        {
            //unequip first (can't be equipped on anything else at the same time)
            foreach (var wearable in item.GetComponents<Wearable>())
            {
                wearable.Unequip(character);
            }

            prostheticForLimbs.Clear();
            foreach (var limbType in prostheticsLimbTypes)
            {
                var limb = character.AnimController.GetLimb(limbType, excludeSevered: false);
                if (limb == null) { continue; }
                foreach (var connectedLimb in character.AnimController.GetConnectedLimbsFurtherFromMainLimb(limb))
                {
                    foreach (var limbJoint in character.AnimController.LimbJoints.Where(lj => lj.LimbA == connectedLimb || lj.LimbB == connectedLimb))
                    {
                        limbJoint.IsSevered = false;
                    }
                    connectedLimb.HiddenByProsthesis = true;
                    connectedLimb.IsSevered = false;
                    connectedLimb.body.ResetDynamics();
                }
                prostheticForLimbs.Add(limb);
            }

            //The prosthetic must now be connected to the character's main limb.
            //Lets check that e.g. the character's elbow isn't severed when we're trying to equip a prosthetic for the forearm.
            //Should not happen in the vanilla game because only the joints that can be "fixed" by a prosthetic should be severable while the character is alive.
            foreach (var limbType in prostheticsLimbTypes)
            {
                var limb = character.AnimController.GetLimb(limbType, excludeSevered: false);
                if (limb == null) { continue; }
                if (!character.AnimController.GetConnectedLimbs(limb).Contains(character.AnimController.MainLimb))
                {
                    DebugConsole.AddWarning(
                        $"Failed to equip a prosthetic. The limb {limbType} replaced by the prosthetic are not connected to the main limb."+
                        " This can happen e.g. if equipping an arm prosthetic for the forearm when the shoulder has been severed, and probably a problem with how the joints or the prosthetics have been configured: "+
                        " Only joints that can be fixed by equipping a prosthetic should be severable.");
                    Drop(dropper: null);
                    return false;
                }
            }

            if (!disableEquipEffects)
            {
                ApplyStatusEffects(ActionType.OnEquippedAsProsthetic, 1.0f, character);
#if CLIENT
                PlaySound(ActionType.OnEquippedAsProsthetic);
#endif
            }
#if SERVER
            GameMain.NetworkMember?.CreateEntityEvent(character, new Character.CharacterStatusEventData(forceAfflictionData: false, forceLimbSeveringData: true, disableSeverEffects: true));
#endif
            return true;        
        }

        private void UnequipProsthetic(Character character)
        {
            //create a new list out of prostheticForLimbs because it can get modified as a result of unequipping prosthetics inside the loop 
            foreach (var limbWithProsthetic in prostheticForLimbs.ToList())
            {
                //only "re-sever" the limb if there's no other prostheses worn on it
                bool otherItemAsProsthesis = limbWithProsthetic.WearingItems.Any(it => it.WearableComponent.item.GetComponents<Wearable>().Any(w => w != this && w.prostheticForLimbs.Contains(limbWithProsthetic)));
                DebugConsole.NewMessage($"Unequipped item {item.Name} {item.ID} as a prosthesis on limb {limbWithProsthetic.type}", Color.Orange);
                if (!otherItemAsProsthesis)
                {
                    DebugConsole.NewMessage($"Re-severing limb {limbWithProsthetic.type}, no other prostheses on limb", Color.Orange);
                    foreach (var connectedLimb in character.AnimController.GetConnectedLimbsFurtherFromMainLimb(limbWithProsthetic))
                    {
                        connectedLimb.HiddenByProsthesis = false;
                        connectedLimb.IsSevered = true;
                        connectedLimb.SkipSeveredFadeOutTimer();
                    }
                    foreach (var limbJoint in character.AnimController.LimbJoints.Where(lj => lj.LimbA == limbWithProsthetic || lj.LimbB == limbWithProsthetic))
                    {
                        if (limbJoint.IsSevered) { continue; }
                        //no particles here (the limb was "already gone", we're just removing a prosthesis)
                        character.AnimController.SeverLimbJoint(limbJoint, showHints: false, disableSeverEffects: true, dropProstheses: false);
                    }
                }
                ApplyStatusEffects(ActionType.OnSevered, 1.0f, character, targetLimb: limbWithProsthetic);
            }
            prostheticForLimbs.Clear();

#if SERVER
            GameMain.NetworkMember?.CreateEntityEvent(character, new Character.CharacterStatusEventData(forceAfflictionData: false, forceLimbSeveringData: true, disableSeverEffects: true));            
#endif
        }

        public override void Drop(Character dropper, bool setTransform = true)
        {
            Character previousPicker = picker;
            Unequip(picker);
            base.Drop(dropper, setTransform);
            previousPicker?.OnWearablesChanged();
            picker = null;
            IsActive = false;
        }

        public override void Unequip(Character character)
        {
            if (character == null || character.Removed) { return; }
            if (picker == null) { return; }

            bool isProsthesis = prostheticForLimbs.Any();
            UnequipProsthetic(character);            

            for (int i = 0; i < wearableSprites.Length; i++)
            {
                WearableSprite wearableSprite = wearableSprites[i];
                Limb equipLimb = character.AnimController.GetLimb(limbType[i], excludeSevered: false);
                if (equipLimb == null) { continue; }

                if (!isProsthesis && equipLimb.IsSevered) // If this is being worn as a prosthesis, we can immediately remove the item from the severed limb, as it's hidden.
                {
                    if (!equipLimb.SeveredWearableChanges.TryGetValue(wearableSprite, out bool equip))
                    {
                        equipLimb.SeveredWearableChanges.Add(wearableSprite, false);
                    }
                    else if (equip)
                    {
                        equipLimb.SeveredWearableChanges.Remove(wearableSprite);
                    }
                    continue;
                }

                wearableSprite.Unequip(equipLimb);
            }

            IsActive = false;
#if CLIENT
            //stop any sounds that may have been looping while wearing the item
            StopSounds(ActionType.OnWearing);
#endif
        }

        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            Update(deltaTime, cam);
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (picker == null || picker.Removed)
            {
                IsActive = false;
                return;
            }

            //if the state of required items changes, we may need to refresh the stats given by the wearable
            bool hasRequiredItems = HasRequiredContainedItems(picker, addMessage: false);
            if (hasRequiredItems != hadRequiredItems)
            {
                picker.OnWearablesChanged();
                hadRequiredItems = hasRequiredItems;
            }

            //if the item is also being held, let the Holdable component control the position
            if (item.GetComponent<Holdable>() is not { IsActive: true })
            {
                item.SetTransform(picker.SimPosition, 0.0f);
            }
            foreach (ItemComponent ic in item.Components)
            {
                //if the item has multiple wearable components, don't execute them here - let that wearable handle them itself if it's currently worn
                if (ic is Wearable && ic != this) { continue; }
                ic.ApplyStatusEffects(ActionType.OnWearing, deltaTime, picker);
            }
            wearableSprites.ForEach(static wearableSprite => wearableSprite.UpdateIsActive());
#if CLIENT
            PlaySound(ActionType.OnWearing, picker);
#endif
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();

            Unequip(picker);

            foreach (WearableSprite wearableSprite in wearableSprites)
            {
                wearableSprite.Remove();
            }
        }

        public override XElement Save(XElement parentElement)
        {
            XElement componentElement = base.Save(parentElement);
            componentElement.Add(new XAttribute("variant", variant));
            return componentElement;
        }

        private int loadedVariant = -1;
        public override void Load(ContentXElement componentElement, bool usePrefabValues, IdRemap idRemap, bool isItemSwap)
        {
            base.Load(componentElement, usePrefabValues, idRemap, isItemSwap);
            loadedVariant = componentElement.GetAttributeInt("variant", -1);
        }
        public override void OnItemLoaded()
        {
            base.OnItemLoaded();
            //do this here to prevent creating a network event before the item has been fully initialized
            if (loadedVariant > 0 && loadedVariant < Variants + 1)
            {
                Variant = loadedVariant;
            }
        }
        public override void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            msg.WriteByte((byte)Variant);
            base.ServerEventWrite(msg, c, extraData);
        }

        public override void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            Variant = (int)msg.ReadByte();
            base.ClientEventRead(msg, sendingTime);
        }

    }
}
