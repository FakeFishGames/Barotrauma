using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    public class DecalPrefab : PrefabWithUintIdentifier
    {
        public string Name => Identifier.Value;

        public override void Dispose()
        {
            foreach (Sprite spr in Sprites)
            {
                spr.Remove();
            }
            Sprites.Clear();
        }

        public readonly List<Sprite> Sprites;

        public readonly Color Color;

        public readonly float MinScale, MaxScale;

        public readonly float LifeTime;
        public readonly float FadeOutTime;
        public readonly float FadeInTime;

        public DecalPrefab(ContentXElement element, DecalsFile file) : base(file, new Identifier(element.Name.LocalName))
        {
            Sprites = new List<Sprite>();

            foreach (var subElement in element.Elements())
            {
                if (subElement.Name.ToString().Equals("sprite", StringComparison.OrdinalIgnoreCase))
                {
                    Sprites.Add(new Sprite(subElement));             
                }
            }

            Color = element.GetAttributeColor("color", Color.White);

            float scale = element.GetAttributeFloat("scale", 1.0f);
            MinScale = element.GetAttributeFloat(nameof(MinScale), scale);
            MaxScale = element.GetAttributeFloat(nameof(MaxScale), scale);

            LifeTime = element.GetAttributeFloat("lifetime", 10.0f);
            FadeOutTime = Math.Min(LifeTime, element.GetAttributeFloat("fadeouttime", 1.0f));
            FadeInTime = Math.Min(LifeTime - FadeOutTime, element.GetAttributeFloat("fadeintime", 0.0f));
        }
    }
}
