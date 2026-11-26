using Barotrauma.Extensions;
using Barotrauma.IO;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Barotrauma
{
    public sealed partial class MissionPrefab : PrefabWithUintIdentifier
    {
        private ImmutableArray<Sprite> portraits = [];
        public bool HasPortraits => portraits.Length > 0;

        public Sprite Icon { get; private set; }
        public Color IconColor { get; private set; }

        public Sprite MapIcon { get; private set; }
        public Color MapIconColor { get; private set; }
        public float MapIconScale { get; private set; }
        public bool RotateMapIcon { get; private set; }
        public bool PulsateMapIcon { get; private set; }
        public bool ShowOffScreenIndicator { get; private set; }

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

        private Sprite hudIcon;
        public Sprite HudIcon => hudIcon ?? Icon;

        private Color? hudIconColor;
        public Color HudIconColor => hudIconColor ?? IconColor;

        public Color ProgressBarColor { get; private set; }

        private ImmutableDictionary<int, Identifier> overrideMusicOnState;

        private void ParseConfigElementClient(ContentXElement element, MissionPrefab variantOf = null)
        {
            DisplayTargetHudIcons = element.GetAttributeBool("displaytargethudicons", false);
            HudIconMaxDistance = element.GetAttributeFloat("hudiconmaxdistance", 1000f);
            Dictionary<int, Identifier> overrideMusic = [];
            List<Sprite> portraits = [];
            foreach (ContentXElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "icon":
                        Icon = new Sprite(subElement, GetTexturePath(subElement, variantOf));
                        IconColor = subElement.GetAttributeColor("color", Color.White);
                        break;
                    case "mapicon":
                        MapIcon = new Sprite(subElement);
                        MapIconColor = subElement.GetAttributeColor("color", Color.White);
                        MapIconScale = subElement.GetAttributeFloat("scale", 0.5f);
                        RotateMapIcon = subElement.GetAttributeBool("rotate", false);
                        PulsateMapIcon = subElement.GetAttributeBool("pulsate", false);
                        ShowOffScreenIndicator = subElement.GetAttributeBool("showoffscreenindicator", false);
                        break;
                    case "hudicon":
                        hudIcon = new Sprite(subElement, GetTexturePath(subElement, variantOf));
                        hudIconColor = subElement.GetAttributeColor("color");
                        break;
                    case "overridemusic":
                        overrideMusic.Add(
                            subElement.GetAttributeInt("state", 0),
                            subElement.GetAttributeIdentifier("type", Identifier.Empty));
                        break;
                    case "portrait":
                        Sprite portrait = new(subElement, GetTexturePath(subElement, variantOf), lazyLoad: true);
                        if (portrait != null)
                        {
                            portraits.Add(portrait);
                        }
                        break;
                }
            }
            this.portraits = [.. portraits];
            overrideMusicOnState = overrideMusic.ToImmutableDictionary();
            ProgressBarColor = element.GetAttributeColor(nameof(ProgressBarColor), GUIStyle.Blue);
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

        public string GetTexturePath(ContentXElement subElement, MissionPrefab variantOf = null)
            => subElement.DoesAttributeReferenceFileNameAlone("texture")
                ? Path.GetDirectoryName(variantOf?.ContentFile.Path ?? ContentFile.Path)
                : "";

        partial void DisposeProjectSpecific()
        {
            Icon?.Remove();
            MapIcon?.Remove();
        }
    }
}
