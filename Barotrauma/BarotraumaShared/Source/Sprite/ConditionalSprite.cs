using System.Collections.Generic;
using System.Xml.Linq;
using System.Linq;

namespace Barotrauma
{
    class ConditionalSprite : Sprite
    {
        public readonly List<PropertyConditional> conditionals = new List<PropertyConditional>();
        public bool IsActive => Target != null && conditionals.All(c => c.Matches(Target));
        readonly ISerializableEntity Target;

        public ConditionalSprite(XElement element, ISerializableEntity target, string path = "", string file = "") : base(element, path, file)
        {
            Target = target;
            foreach (XElement subElement in element.Elements())
            {
                foreach (XAttribute attribute in subElement.Attributes())
                {
                    conditionals.Add(new PropertyConditional(attribute));
                }
            }
        }
    }
}
