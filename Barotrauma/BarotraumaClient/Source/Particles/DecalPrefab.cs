using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Particles
{
    class DecalPrefab
    {
        public readonly string Name;

        public readonly string FilePath;

        public readonly List<Sprite> Sprites;

        public readonly Color Color;

        public readonly float LifeTime;
        public readonly float FadeOutTime;
        public readonly float FadeInTime;

        public DecalPrefab(XElement element, string filePath)
        {
            Name = element.Name.ToString();

            FilePath = filePath;

            Sprites = new List<Sprite>();

            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() == "sprite")
                {
                    Sprites.Add(new Sprite(subElement));             
                }
            }  
            
            Color = new Color(element.GetAttributeVector4("color", Vector4.One));

            LifeTime = element.GetAttributeFloat("lifetime", 10.0f);
            FadeOutTime = Math.Min(LifeTime, element.GetAttributeFloat("fadeouttime", 1.0f));
            FadeInTime = Math.Min(LifeTime - FadeOutTime, element.GetAttributeFloat("fadeintime", 0.0f));
        }
    }
}
