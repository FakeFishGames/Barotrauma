using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Barotrauma.Abilities;

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

    class WearableSprite
    {
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
        public bool HideOtherWearables { get; private set; }
        public bool CanBeHiddenByOtherWearables { get; private set; }
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
                    HideOtherWearables = false;
                    InheritLimbDepth = true;
                    InheritScale = true;
                    InheritOrigin = true;
                    InheritSourceRect = true;
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
                string textureName = element.GetAttributeString("texture", "");
                return ContentPath.FromRaw(
                    element.ContentPackage,
                    $"{Path.GetDirectoryName(WearableComponent.Item.Prefab.FilePath)}/{textureName}");
            }
            else
            {
                return element.GetAttributeContentPath("texture") ?? ContentPath.Empty;
            }
        }

        public void ParsePath(bool parseSpritePath)
        {
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
                Sprite.ParseTexturePath(file: SpritePath);
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
            HideOtherWearables = SourceElement.GetAttributeBool("hideotherwearables", false);
            CanBeHiddenByOtherWearables = SourceElement.GetAttributeBool("canbehiddenbyotherwearables", true);
            InheritLimbDepth = SourceElement.GetAttributeBool("inheritlimbdepth", true);
            var scale = SourceElement.GetAttribute("inheritscale");
            if (scale != null)
            {
                InheritScale = scale.GetAttributeBool(false);
            }
            else
            {
                InheritScale = SourceElement.GetAttributeBool("inherittexturescale", false);
            }
            IgnoreLimbScale = SourceElement.GetAttributeBool("ignorelimbscale", false);
            IgnoreTextureScale = SourceElement.GetAttributeBool("ignoretexturescale", false);
            IgnoreRagdollScale = SourceElement.GetAttributeBool("ignoreragdollscale", false);
            SourceElement.GetAttributeBool("inherittexturescale", false);
            InheritOrigin = SourceElement.GetAttributeBool("inheritorigin", false);
            InheritSourceRect = SourceElement.GetAttributeBool("inheritsourcerect", false);
            DepthLimb = (LimbType)Enum.Parse(typeof(LimbType), SourceElement.GetAttributeString("depthlimb", "None"), true);
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

            IsInitialized = true;
        }
    }
}

namespace Barotrauma.Items.Components
{
    partial class Wearable : Pickable, IServerSerializable
    {
        private readonly ContentXElement[] wearableElements;
        private readonly WearableSprite[] wearableSprites;
        private readonly LimbType[] limbType;
        private readonly Limb[] limb;

        private readonly List<DamageModifier> damageModifiers;
        public readonly Dictionary<Identifier, float> SkillModifiers;

        public readonly Dictionary<StatTypes, float> WearableStatValues = new Dictionary<StatTypes, float>();

        public IEnumerable<DamageModifier> DamageModifiers
        {
            get { return damageModifiers; }
        }

        public bool AutoEquipWhenFull { get; private set; }
        public bool DisplayContainedStatus { get; private set; }

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

        public Wearable(Item item, ContentXElement element) : base(item, element)
        {
            this.item = item;

            damageModifiers = new List<DamageModifier>();
            SkillModifiers = new Dictionary<Identifier, float>();

            int spriteCount = element.Elements().Count(x => x.Name.ToString() == "sprite");
            Variants = element.GetAttributeInt("variants", 0);
            variant = Rand.Range(1, Variants + 1, Rand.RandSync.ServerAndClient);
            wearableSprites = new WearableSprite[spriteCount];
            wearableElements = new ContentXElement[spriteCount];
            limbType    = new LimbType[spriteCount];
            limb        = new Limb[spriteCount];
            AutoEquipWhenFull = element.GetAttributeBool("autoequipwhenfull", true);
            DisplayContainedStatus = element.GetAttributeBool("displaycontainedstatus", false);
            int i = 0;
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "sprite":
                        if (subElement.GetAttribute("texture") == null)
                        {
                            DebugConsole.ThrowError("Item \"" + item.Name + "\" doesn't have a texture specified!");
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
                        if (WearableStatValues.ContainsKey(statType))
                        {
                            WearableStatValues[statType] += statValue;
                        }
                        else
                        {
                            WearableStatValues.TryAdd(statType, statValue);
                        }
                        break;
                }
            }
        }

        public override void Equip(Character character)
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

            picker = character;

            for (int i = 0; i < wearableSprites.Length; i++ )
            {
                var wearableSprite = wearableSprites[i];
                if (!wearableSprite.IsInitialized) { wearableSprite.Init(picker); }
                // If the item is gender specific (it has a different textures for male and female), we have to change the gender here so that the texture is updated.
                wearableSprite.Picker = picker;

                Limb equipLimb  = character.AnimController.GetLimb(limbType[i]);
                if (equipLimb == null) { continue; }
                
                if (item.body != null)
                {
                    item.body.Enabled = false;
                }
                IsActive = true;
                if (wearableSprite.LightComponent != null)
                {
                    foreach (var light in wearableSprite.LightComponents)
                    {
                        light.ParentBody = equipLimb.body;
                    }
                }

                limb[i] = equipLimb;
                if (!equipLimb.WearingItems.Contains(wearableSprite))
                {
                    equipLimb.WearingItems.Add(wearableSprite);
                    equipLimb.WearingItems.Sort((i1, i2) => { return i2.Sprite.Depth.CompareTo(i1.Sprite.Depth); });
                    equipLimb.WearingItems.Sort((i1, i2) => 
                    {
                        if (i1?.WearableComponent == null && i2?.WearableComponent == null)
                        {
                            return 0;
                        }
                        else if (i1?.WearableComponent == null)
                        {
                            return -1;
                        }
                        else if (i2?.WearableComponent == null)
                        {
                            return 1;
                        }
                        return i1.WearableComponent.AllowedSlots.Contains(InvSlotType.OuterClothes).CompareTo(i2.WearableComponent.AllowedSlots.Contains(InvSlotType.OuterClothes));
                    });
                }
#if CLIENT
                equipLimb.UpdateWearableTypesToHide();
#endif
            }
            character.OnWearablesChanged();
        }

        public override void Drop(Character dropper)
        {
            Character previousPicker = picker;
            Unequip(picker);
            base.Drop(dropper);
            previousPicker?.OnWearablesChanged();
            picker = null;
            IsActive = false;
        }

        public override void Unequip(Character character)
        {
            if (character == null || character.Removed) { return; }
            if (picker == null) { return; }

            for (int i = 0; i < wearableSprites.Length; i++)
            {
                Limb equipLimb = character.AnimController.GetLimb(limbType[i]);
                if (equipLimb == null) { continue; }

                if (wearableSprites[i].LightComponent != null)
                {
                    foreach (var light in wearableSprites[i].LightComponents)
                    {
                        light.ParentBody = null;
                    }
                }

                equipLimb.WearingItems.RemoveAll(w => w != null && w == wearableSprites[i]);
#if CLIENT
                equipLimb.UpdateWearableTypesToHide();
#endif
                limb[i] = null;
            }

            IsActive = false;
        }

        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            Update(deltaTime, cam);
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (picker.Removed)
            {
                IsActive = false;
                return;
            }

            item.SetTransform(picker.SimPosition, 0.0f);
            
            item.ApplyStatusEffects(ActionType.OnWearing, deltaTime, picker);

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
                if (wearableSprite != null && wearableSprite.Sprite != null)
                {
                    wearableSprite.Sprite.Remove();
                }
            }
        }

        public override XElement Save(XElement parentElement)
        {
            XElement componentElement = base.Save(parentElement);
            componentElement.Add(new XAttribute("variant", variant));
            return componentElement;
        }

        private int loadedVariant = -1;
        public override void Load(ContentXElement componentElement, bool usePrefabValues, IdRemap idRemap)
        {
            base.Load(componentElement, usePrefabValues, idRemap);
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
