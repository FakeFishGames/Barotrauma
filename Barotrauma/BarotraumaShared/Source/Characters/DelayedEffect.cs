using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    class DelayedEffect : StatusEffect
    {
        public static List<DelayedEffect> List = new List<DelayedEffect>();

        private float delay;

        private float startTimer;

        private Entity entity;

        private List<IPropertyObject> targets;
        
        public float StartTimer
        {
            get { return startTimer; }
        }

        public DelayedEffect(XElement element)
            : base(element)
        {
            delay = ToolBox.GetAttributeFloat(element, "delay", 1.0f);
        }

        public override void Apply(ActionType type, float deltaTime, Entity entity, List<IPropertyObject> targets)
        {
            if (this.type != type) return;
            
            startTimer = delay;
            this.entity = entity;

            this.targets = targets;

            List.Add(this);
        }

        public void Update(float deltaTime)
        {
            startTimer -= deltaTime;

            if (startTimer > 0.0f) return;

            base.Apply(1.0f, entity, targets);
            List.Remove(this);
        }

    }
}
