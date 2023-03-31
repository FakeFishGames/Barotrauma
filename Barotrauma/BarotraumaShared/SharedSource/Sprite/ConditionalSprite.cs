using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class ConditionalSprite
    {
        public readonly List<PropertyConditional> conditionals = new List<PropertyConditional>();
        public bool IsActive { get; private set; } = true;

        public readonly PropertyConditional.LogicalOperatorType LogicalOperator;
        public readonly bool Exclusive;
        public ISerializableEntity Target { get; private set; }
        public Sprite Sprite { get; private set; }
        public DeformableSprite DeformableSprite { get; private set; }
        public Sprite ActiveSprite => Sprite ?? DeformableSprite.Sprite;

        public ConditionalSprite(ContentXElement element, ISerializableEntity target, string file = "", bool lazyLoad = false)
        {
            Target = target;
            Exclusive = element.GetAttributeBool("exclusive", Exclusive);
            LogicalOperator = element.GetAttributeEnum(nameof(LogicalOperator),
                element.GetAttributeEnum("comparison", LogicalOperator));
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "conditional":
                        conditionals.AddRange(PropertyConditional.FromXElement(subElement));
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

        public void CheckConditionals()
        {
            if (Target == null)
            {
                IsActive = false;
            }
            else
            {
                IsActive = LogicalOperator == PropertyConditional.LogicalOperatorType.And ? conditionals.All(c => c.Matches(Target)) : conditionals.Any(c => c.Matches(Target));
            }
        }
    }
}
