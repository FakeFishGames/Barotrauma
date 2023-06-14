using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Barotrauma
{
    partial class MissionPrefab : PrefabWithUintIdentifier
    {
        private ImmutableArray<Sprite> portraits = new ImmutableArray<Sprite>();

        public bool HasPortraits => portraits.Length > 0;

        public Sprite Icon
        {
            get;
            private set;
        }

        public Color IconColor
        {
            get;
            private set;
        }

        public bool DisplayTargetHudIcons
        {
            get;
            private set;
        }

        public float HudIconMaxDistance
        {
            get;
            private set;
        }

        public Sprite HudIcon
        {
            get
            {
                return hudIcon ?? Icon;
            }
        }

        public Color HudIconColor
        {
            get
            {
                return hudIconColor ?? IconColor;
            }
        } 

        private Sprite hudIcon;
        private Color? hudIconColor;

        private ImmutableDictionary<int, Identifier> overrideMusicOnState;

        partial void InitProjSpecific(ContentXElement element)
        {
            DisplayTargetHudIcons = element.GetAttributeBool("displaytargethudicons", false);
            HudIconMaxDistance = element.GetAttributeFloat("hudiconmaxdistance", 1000.0f);
            Dictionary<int, Identifier> overrideMusic = new Dictionary<int, Identifier>();
            List<Sprite> portraits = new List<Sprite>();
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "icon":
                        Icon = new Sprite(subElement);
                        IconColor = subElement.GetAttributeColor("color", Color.White);
                        break;
                    case "hudicon":
                        hudIcon = new Sprite(subElement);
                        hudIconColor = subElement.GetAttributeColor("color");
                        break;
                    case "overridemusic":
                        overrideMusic.Add(
                            subElement.GetAttributeInt("state", 0),
                            subElement.GetAttributeIdentifier("type", Identifier.Empty));
                        break;
                    case "portrait":
                        var portrait = new Sprite(subElement, lazyLoad: true);
                        if (portrait != null)
                        {
                            portraits.Add(portrait);
                        }
                        break;
                }
            }
            this.portraits = portraits.ToImmutableArray();
            overrideMusicOnState = overrideMusic.ToImmutableDictionary();
        }

        public Identifier GetOverrideMusicType(int state)
        {
            if (overrideMusicOnState.TryGetValue(state, out Identifier id))
            {
                return id;
            }
            return Identifier.Empty;
        }

        public Sprite GetPortrait(int randomSeed)
        {
            if (portraits.Length == 0) { return null; }
            return portraits[Math.Abs(randomSeed) % portraits.Length];
        }

        partial void DisposeProjectSpecific()
        {
            Icon?.Remove();
        }
    }
}
