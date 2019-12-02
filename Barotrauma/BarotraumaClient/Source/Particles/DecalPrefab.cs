using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Particles
{
    class DecalPrefab : IPrefab, IDisposable
    {
        public readonly string Name;

        public string OriginalName { get { return Name; } }

        public string Identifier { get { return Name; } }

        public string FilePath { get; private set; }

        public ContentPackage ContentPackage { get; private set; }

        public void Dispose()
        {
            foreach (Sprite spr in Sprites)
            {
                spr.Remove();
            }
            Sprites.Clear();
        }

        public readonly List<Sprite> Sprites;

        public readonly Color Color;

        public readonly float LifeTime;
        public readonly float FadeOutTime;
        public readonly float FadeInTime;

        public DecalPrefab(XElement element, ContentFile file)
        {
            Name = element.Name.ToString();

            FilePath = file.Path;

            ContentPackage = file.ContentPackage;

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
