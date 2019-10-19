using System.Collections.Generic;
using System.Xml.Linq;
using System.Linq;

namespace Barotrauma
{
    partial class ConditionalSprite
    {
        public readonly List<PropertyConditional> conditionals = new List<PropertyConditional>();
        public bool IsActive => Target != null && conditionals.All(c => c.Matches(Target));
        public ISerializableEntity Target { get; private set; }
        public Sprite Sprite { get; private set; }
        public DeformableSprite DeformableSprite { get; private set; }
        public Sprite ActiveSprite => Sprite ?? DeformableSprite.Sprite;

        public ConditionalSprite(XElement element, ISerializableEntity target, string path = "", string file = "", bool lazyLoad = false)
        {
            Target = target;
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "conditional":
                        foreach (XAttribute attribute in subElement.Attributes())
                        {
                            conditionals.Add(new PropertyConditional(attribute));
                        }
                        break;
                    case "sprite":
                        Sprite = new Sprite(subElement, path, file, lazyLoad: lazyLoad);
                        break;
                    case "deformablesprite":
                        DeformableSprite = new DeformableSprite(subElement, filePath: path, lazyLoad: lazyLoad);
                        break;
                }
            }
        }
    }
}
