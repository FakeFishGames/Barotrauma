using System.Collections.Generic;
using System.Linq;
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
        public static readonly List<DelayedListElement> DelayList = new List<DelayedListElement>();

        private float delay;

        public DelayedEffect(XElement element, string parentDebugName)
            : base(element, parentDebugName)
        {
            delay = element.GetAttributeFloat("delay", 1.0f);
        }

        public override void Apply(ActionType type, float deltaTime, Entity entity, ISerializableEntity target)
        {
            if (this.type != type || !HasRequiredItems(entity)) return;
            if (!Stackable && DelayList.Any(d => d.Parent == this && d.Targets.FirstOrDefault() == target)) return;
            
            if (targetIdentifiers != null && !IsValidTarget(target)) return;
            if (!HasRequiredConditions(new List<ISerializableEntity>() { target })) return;

            DelayedListElement element = new DelayedListElement
            {
                Parent = this,
                StartTimer = delay,
                Entity = entity,
                Targets = new List<ISerializableEntity>() { target }
            };

            DelayList.Add(element);
        }

        public override void Apply(ActionType type, float deltaTime, Entity entity, IEnumerable<ISerializableEntity> targets)
        {
            if (this.type != type || !HasRequiredItems(entity)) return;
            if (!Stackable && DelayList.Any(d => d.Parent == this && d.Targets.SequenceEqual(targets))) return;
            
            currentTargets.Clear();
            foreach (ISerializableEntity target in targets)
            {
                if (targetIdentifiers != null)
                {
                    //ignore invalid targets
                    if (!IsValidTarget(target)) { continue; }
                }
                currentTargets.Add(target);
            }

            if (!HasRequiredConditions(currentTargets)) return;

            DelayedListElement element = new DelayedListElement
            {
                Parent = this,
                StartTimer = delay,
                Entity = entity,
                Targets = currentTargets
            };

            DelayList.Add(element);
        }

        public static void Update(float deltaTime)
        {
            for (int i = DelayList.Count - 1; i >= 0; i--)
            {
                DelayedListElement element = DelayList[i];
                if (element.Parent.CheckConditionalAlways && !element.Parent.HasRequiredConditions(element.Targets))
                {
                    DelayList.Remove(element);
                    continue;
                }

                element.StartTimer -= deltaTime;

                if (element.StartTimer > 0.0f) continue;

                element.Parent.Apply(1.0f, element.Entity, element.Targets);
                DelayList.Remove(element);
            }
        }
    }
}