using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

        private Limb limb;

        public float Timer
        {
            get { return timer; }
        }

        public DelayedEffect(XElement element)
            : base(element)
        {
            delay = ToolBox.GetAttributeFloat(element, "delay", 1.0f);
        }

        public override void Apply(ActionType type, float deltaTime, Item item, Character character = null, Limb limb = null)
        {
            if (this.type != type) return;

            this.item = item;
            this.character = character;
            this.limb = limb;

            this.timer = delay;

            list.Add(this);
        }

        public void Update(float deltaTime)
        {
            timer -= deltaTime;

            if (timer > 0.0f) return;

            base.Apply(1.0f, character, item, limb);
            list.Remove(this);
        }

    }
}
