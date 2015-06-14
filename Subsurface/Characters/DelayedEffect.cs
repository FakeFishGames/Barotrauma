using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Subsurface
{
    class DelayedEffect : StatusEffect
    {
        public static List<DelayedEffect> list = new List<DelayedEffect>();

        float delay;

        float timer;
        
        Vector2 position;

        List<IPropertyObject> targets;
        
        public float Timer
        {
            get { return timer; }
        }

        public DelayedEffect(XElement element)
            : base(element)
        {
            delay = ToolBox.GetAttributeFloat(element, "delay", 1.0f);
        }

        public override void Apply(ActionType type, float deltaTime, Vector2 position, List<IPropertyObject> targets)
        {
            if (this.type != type) return;
            
            timer = delay;
            this.position = position;

            this.targets = targets;

            list.Add(this);
        }

        public void Update(float deltaTime)
        {
            timer -= deltaTime;

            if (timer > 0.0f) return;

            base.Apply(1.0f, position, targets);
            list.Remove(this);
        }

    }
}
