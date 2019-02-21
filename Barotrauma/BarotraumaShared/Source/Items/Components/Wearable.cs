using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Items.Components;

namespace Barotrauma
{
    public enum WearableType
    {
        Item,
        Hair,
        Beard,
        Moustache,
        FaceAttachment,
        JobIndicator
    }

    class WearableSprite
    {
        public string SpritePath { get; private set; }
        public XElement SourceElement { get; private set; }

        public WearableType Type { get; private set; }
        private Sprite _sprite;
        public Sprite Sprite
        {
            get { return _sprite; }
            set
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
        public bool InheritLimbDepth { get; private set; }
        public bool InheritTextureScale { get; private set; }
        public bool InheritOrigin { get; private set; }
        public bool InheritSourceRect { get; private set; }
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

        public LightComponent LightComponent { get; set; }

        private Gender _gender;
        /// <summary>
        /// None = Any/Not Defined -> no effect.
        /// Changing the gender forces re-initialization, because the textures can be different for male and female characters.
        /// </summary>
        public Gender Gender
        {
            get { return _gender; }
            set
            {
                if (value == _gender) { return; }
                _gender = value;
                IsInitialized = false;
                Init(_gender);
            }
        }

        public WearableSprite(XElement subElement, WearableType type)
        {
            Type = type;
            SourceElement = subElement;
            SpritePath = subElement.Attribute("texture").Value;
            Init();
            switch (type)
            {
                case WearableType.Hair:
                case WearableType.Beard:
                case WearableType.Moustache:
                case WearableType.FaceAttachment:
                case WearableType.JobIndicator:
                    Limb = LimbType.Head;
                    HideLimb = false;
                    HideOtherWearables = false;
                    InheritLimbDepth = true;
                    InheritTextureScale = true;
                    InheritOrigin = true;
                    InheritSourceRect = true;
                    break;
            }
        }

        /// <summary>
        /// Note: this constructor cannot initialize automatically, because the gender is unknown at this point. We only know it when the item is equipped.
        /// </summary>
        public WearableSprite(XElement subElement, Wearable item)
        {
            Type = WearableType.Item;
            WearableComponent = item;
            string texturePath = subElement.GetAttributeString("texture", string.Empty);
            SpritePath = texturePath.Contains("/") ? texturePath : $"{Path.GetDirectoryName(item.Item.Prefab.ConfigFile)}/{texturePath}";
            SourceElement = subElement;
        }

        public bool IsInitialized { get; private set; }
        public void Init(Gender gender = Gender.None)
        {
            if (IsInitialized) { return; }
            _gender = SpritePath.Contains("[GENDER]") ? gender : Gender.None;
            if (_gender != Gender.None)
            {
                SpritePath = SpritePath.Replace("[GENDER]", (_gender == Gender.Female) ? "female" : "male");
            }
            if (Sprite != null)
            {
                Sprite.Remove();
            }
            Sprite = new Sprite(SourceElement, "", SpritePath);
            Limb = (LimbType)Enum.Parse(typeof(LimbType), SourceElement.GetAttributeString("limb", "Head"), true);
            HideLimb = SourceElement.GetAttributeBool("hidelimb", false);
            HideOtherWearables = SourceElement.GetAttributeBool("hideotherwearables", false);
            InheritLimbDepth = SourceElement.GetAttributeBool("inheritlimbdepth", true);
            InheritTextureScale = SourceElement.GetAttributeBool("inherittexturescale", false);
            InheritOrigin = SourceElement.GetAttributeBool("inheritorigin", false);
            InheritSourceRect = SourceElement.GetAttributeBool("inheritsourcerect", false);
            DepthLimb = (LimbType)Enum.Parse(typeof(LimbType), SourceElement.GetAttributeString("depthlimb", "None"), true);
            Sound = SourceElement.GetAttributeString("sound", "");
            var index = SourceElement.GetAttributePoint("sheetindex", new Point(-1, -1));
            if (index.X > -1 && index.Y > -1)
            {
                SheetIndex = index;
            }
            IsInitialized = true;
        }
    }
}

namespace Barotrauma.Items.Components
{
    class Wearable : Pickable
    {
        private WearableSprite[] wearableSprites;
        private LimbType[] limbType;
        private Limb[] limb;

        private List<DamageModifier> damageModifiers;

        public List<DamageModifier> DamageModifiers
        {
            get { return damageModifiers; }
        }
        
        public Wearable (Item item, XElement element) : base(item, element)
        {
            this.item = item;

            damageModifiers = new List<DamageModifier>();
            
            int spriteCount = element.Elements().Count(x => x.Name.ToString() == "sprite");
            wearableSprites = new WearableSprite[spriteCount];
            limbType    = new LimbType[spriteCount];
            limb        = new Limb[spriteCount];

            int i = 0;
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLower())
                {
                    case "sprite":
                        if (subElement.Attribute("texture") == null)
                        {
                            DebugConsole.ThrowError("Item \"" + item.Name + "\" doesn't have a texture specified!");
                            return;
                        }

                        limbType[i] = (LimbType)Enum.Parse(typeof(LimbType),
                            subElement.GetAttributeString("limb", "Head"), true);

                        wearableSprites[i] = new WearableSprite(subElement, this);

                        foreach (XElement lightElement in subElement.Elements())
                        {
                            if (lightElement.Name.ToString().ToLowerInvariant() != "lightcomponent") continue;
                            wearableSprites[i].LightComponent = new LightComponent(item, lightElement);
                            wearableSprites[i].LightComponent.Parent = this;
                            item.components.Add(wearableSprites[i].LightComponent);
                        }

                        i++;
                        break;
                    case "damagemodifier":
                        damageModifiers.Add(new DamageModifier(subElement, item.Name + ", Wearable"));
                        break;
                }
            }
        }

        public override void Equip(Character character)
        {
            picker = character;
            for (int i = 0; i < wearableSprites.Length; i++ )
            {
                var wearableSprite = wearableSprites[i];
                if (!wearableSprite.IsInitialized) { wearableSprite.Init(picker.Info?.Gender ?? Gender.None); }
                if (picker.Info?.Gender != Gender.None && (wearableSprite.Gender != Gender.None))
                {
                    // If the item is gender specific (it has a different textures for male and female), we have to change the gender here so that the texture is updated.
                    wearableSprite.Gender = picker.Info.Gender;
                }

                Limb equipLimb  = character.AnimController.GetLimb(limbType[i]);
                if (equipLimb == null) { continue; }
                
                if (item.body != null)
                {
                    item.body.Enabled = false;
                }
                IsActive = true;
                if (wearableSprite.LightComponent != null)
                {
                    wearableSprite.LightComponent.ParentBody = equipLimb.body;
                }

                limb[i] = equipLimb;
                if (!equipLimb.WearingItems.Contains(wearableSprite))
                {
                    equipLimb.WearingItems.Add(wearableSprite);
                }
            }
        }

        public override void Drop(Character dropper)
        {
            Unequip(picker);

            base.Drop(dropper);

            picker = null;
            IsActive = false;
        }

        public override void Unequip(Character character)
        {
            if (picker == null) return;
            for (int i = 0; i < wearableSprites.Length; i++)
            {
                Limb equipLimb = character.AnimController.GetLimb(limbType[i]);
                if (equipLimb == null) continue;

                if (wearableSprites[i].LightComponent != null)
                {
                    wearableSprites[i].LightComponent.ParentBody = null;
                }

                equipLimb.WearingItems.RemoveAll(w => w != null && w == wearableSprites[i]);

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
            item.SetContainedItemPositions();
            
            item.ApplyStatusEffects(ActionType.OnWearing, deltaTime, picker);

#if CLIENT
            PlaySound(ActionType.OnWearing, picker.WorldPosition, picker);
#endif
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();

            foreach (WearableSprite wearableSprite in wearableSprites)
            {
                if (wearableSprite != null && wearableSprite.Sprite != null) wearableSprite.Sprite.Remove();
            }
        }

    }
}
