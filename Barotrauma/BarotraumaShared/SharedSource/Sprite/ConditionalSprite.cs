using System.Collections.Generic;
using System.Xml.Linq;
using System.Linq;
using System;

namespace Barotrauma
{
    partial class ConditionalSprite
    {
        public readonly List<PropertyConditional> conditionals = new List<PropertyConditional>();
        public bool IsActive
        {
            get
            {
                if (Target == null) { return false; }
                return Comparison == PropertyConditional.Comparison.And ? conditionals.All(c => c.Matches(Target)) : conditionals.Any(c => c.Matches(Target));
            }
        }

        public readonly PropertyConditional.Comparison Comparison;
        public readonly bool Exclusive;
        public ISerializableEntity Target { get; private set; }
        public Sprite Sprite { get; private set; }
        public DeformableSprite DeformableSprite { get; private set; }
        public Sprite ActiveSprite => Sprite ?? DeformableSprite.Sprite;

        public ConditionalSprite(XElement element, ISerializableEntity target, string file = "", bool lazyLoad = false)
        {
            Target = target;
            Exclusive = element.GetAttributeBool("exclusive", Exclusive);
            string comparison = element.GetAttributeString("comparison", null);
            if (comparison != null)
            {
                Enum.TryParse(comparison, ignoreCase: true, out Comparison);
            }
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "conditional":
                        foreach (XAttribute attribute in subElement.Attributes())
                        {
                            if (PropertyConditional.IsValid(attribute))
                            {
                                conditionals.Add(new PropertyConditional(attribute));
                            }
                        }
                        break;
                    case "sprite":
                        Sprite = new Sprite(subElement, file: file, lazyLoad: lazyLoad);
                        break;
                    case "deformablesprite":
                        DeformableSprite = new DeformableSprite(subElement, filePath: file, lazyLoad: lazyLoad);
                        break;
                }
            }
        }
    }
}
