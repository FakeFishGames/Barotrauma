using System.Collections.Generic;
using System.Xml.Linq;

namespace Subsurface
{
    class DelayedEffect : StatusEffect
    {
        public static List<DelayedEffect> list = new List<DelayedEffect>();

        float delay;

        float timer;
        
        private Item item;

        private Character character;
        
        public float Timer
        {
            get { return timer; }
        }

        public DelayedEffect(XElement element)
            : base(element)
        {
            delay = ToolBox.GetAttributeFloat(element, "delay", 1.0f);
        }

        public override void Apply(ActionType type, float deltaTime, Item item, Character character = null)
        {
            if (this.type != type) return;

            this.item = item;
            this.character = character;

            timer = delay;

            list.Add(this);
        }

        public void Update(float deltaTime)
        {
            timer -= deltaTime;

            if (timer > 0.0f) return;

            base.Apply(1.0f, character, item);
            list.Remove(this);
        }

    }
}
