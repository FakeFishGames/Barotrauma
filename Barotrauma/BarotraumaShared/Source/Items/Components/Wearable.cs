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
        FaceAttachment
    }

    class WearableSprite
    {
        public WearableType Type { get; private set; }
        public Sprite Sprite { get; private set; }
        public LimbType Limb { get; private set; }
        public bool HideLimb { get; private set; }
        public bool HideOtherWearables { get; private set; }
        public bool InheritLimbDepth { get; private set; }
        public bool InheritTextureScale { get; private set; }
        public bool InheritOrigin { get; private set; }
        public bool InheritSourceRect { get; private set; }
        public LimbType DepthLimb { get; private set; }
        public Wearable WearableComponent { get; private set; }
        public string Sound { get; private set; }
        public Point? SheetIndex { get; private set; }

        public LightComponent LightComponent { get; set; }

        public WearableSprite(XElement subElement, WearableType type)
        {
            Type = type;
            Init(subElement, subElement.Attribute("texture").Value);
            var index = subElement.GetAttributePoint("sheetindex", new Point(-1, -1));
            if (index.X > -1 && index.Y > -1)
            {
                SheetIndex = index;
            }
            switch (type)
            {
                case WearableType.Hair:
                case WearableType.Beard:
                case WearableType.Moustache:
                case WearableType.FaceAttachment:
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

        public WearableSprite(XElement subElement, Wearable item)
        {
            Type = WearableType.Item;
            WearableComponent = item;
            Init(subElement, Path.GetDirectoryName(item.Item.Prefab.ConfigFile) + "/" + subElement.Attribute("texture").Value);
        }

        private void Init(XElement subElement, string spritePath)
        {
            Sprite = new Sprite(subElement, "", spritePath);
            Limb = (LimbType)Enum.Parse(typeof(LimbType), subElement.GetAttributeString("limb", "Head"), true);
            HideLimb = subElement.GetAttributeBool("hidelimb", false);
            HideOtherWearables = subElement.GetAttributeBool("hideotherwearables", false);
            InheritLimbDepth = subElement.GetAttributeBool("inheritlimbdepth", true);
            InheritTextureScale = subElement.GetAttributeBool("inherittexturescale", false);
            InheritOrigin = subElement.GetAttributeBool("inheritorigin", false);
            InheritSourceRect = subElement.GetAttributeBool("inheritsourcerect", false);
            DepthLimb = (LimbType)Enum.Parse(typeof(LimbType), subElement.GetAttributeString("depthlimb", "None"), true);
            Sound = subElement.GetAttributeString("sound", "");
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
        
        public Wearable (Item item, XElement element)
            : base(item, element)
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
                Limb equipLimb  = character.AnimController.GetLimb(limbType[i]);
                if (equipLimb == null) continue;
                
                item.body.Enabled = false;
                IsActive = true;
                if (wearableSprites[i].LightComponent != null)
                {
                    wearableSprites[i].LightComponent.ParentBody = equipLimb.body;
                }

                limb[i] = equipLimb;
                if (!equipLimb.WearingItems.Contains(wearableSprites[i]))
                {
                    equipLimb.WearingItems.Add(wearableSprites[i]);
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
