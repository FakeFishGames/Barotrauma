using System.Collections.Generic;
using System.Collections.Immutable;

namespace Barotrauma
{
    public class SlideshowPrefab : Prefab
    {
        public static readonly PrefabCollection<SlideshowPrefab> Prefabs = new PrefabCollection<SlideshowPrefab>();

        public class Slide
        {
            public readonly LocalizedString Text;
            public readonly Sprite Portrait;

            public readonly float FadeInDelay, FadeInDuration, FadeOutDuration;
            public readonly float TextFadeInDelay, TextFadeInDuration;
            public readonly float PressAnyKeyDelay;

            public Slide(ContentXElement element)
            {
                string text = element.GetAttributeString(nameof(Text), string.Empty);
                Text = TextManager.Get(text).Fallback(text);

                FadeInDelay = element.GetAttributeFloat(nameof(FadeInDelay), 0.0f);
                FadeInDuration = element.GetAttributeFloat(nameof(FadeInDuration), 2.0f);
                FadeOutDuration = element.GetAttributeFloat(nameof(FadeOutDuration), 2.0f);
                TextFadeInDelay = element.GetAttributeFloat(nameof(TextFadeInDelay), 2.0f);
                TextFadeInDuration = element.GetAttributeFloat(nameof(TextFadeInDuration), 3.0f);
                PressAnyKeyDelay = element.GetAttributeFloat(nameof(PressAnyKeyDelay), TextFadeInDelay * 2f); // backwards compat

                foreach (var subElement in element.Elements())
                {
                    switch (subElement.Name.ToString().ToLowerInvariant())
                    {
                        case "portrait":
                            Portrait = new Sprite(subElement, lazyLoad: true);
                            break;
                    }
                }
            }
        }

        public readonly ImmutableArray<Slide> Slides;

        public SlideshowPrefab(ContentFile file, ContentXElement element) : base(file, element.GetAttributeIdentifier("identifier", ""))
        {
            List<Slide> slides = new List<Slide>();
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "slide":
                        slides.Add(new Slide(subElement));
                        break;
                }
            }
            Slides = slides.ToImmutableArray();
        }

        public override void Dispose() { }
    }
}
