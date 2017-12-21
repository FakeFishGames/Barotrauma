using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    class DelayedListElement
    {
        public DelayedEffect Parent;
        public Entity Entity;
        public List<ISerializableEntity> Targets;
        public float StartTimer;
    }
    class DelayedEffect : StatusEffect
    {
        public static List<DelayedListElement> List = new List<DelayedListElement>();

        private float delay;

        public DelayedEffect(XElement element)
            : base(element)
        {
            delay = element.GetAttributeFloat("delay", 1.0f);
        }

        public override void Apply(ActionType type, float deltaTime, Entity entity, List<ISerializableEntity> targets)
        {
            if (this.type != type || !HasRequiredItems(entity)) return;
            DelayedListElement element = new DelayedListElement();
            element.Parent = this;
            element.StartTimer = delay;
            element.Entity = entity;
            element.Targets = targets;

            List.Add(element);
        }

        public static void Update(float deltaTime)
        {
            for (int i = DelayedEffect.List.Count - 1; i >= 0; i--)
            {
                DelayedListElement element = DelayedEffect.List[i];

                element.StartTimer -= deltaTime;

                if (element.StartTimer > 0.0f) continue;

                element.Parent.Apply(1.0f, element.Entity, element.Targets);
                List.Remove(element);
            }
        }
    }
}