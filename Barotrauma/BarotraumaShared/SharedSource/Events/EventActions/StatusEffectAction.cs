using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class StatusEffectAction : EventAction
    {
        private readonly List<StatusEffect> effects = new List<StatusEffect>();

        private int actionIndex;

        [Serialize("", true)]
        public string TargetTag { get; set; }

        public StatusEffectAction(ScriptedEvent parentEvent, XElement element) : base(parentEvent, element) 
        {
            actionIndex = 0;
            foreach (XElement subElement in parentEvent.Prefab.ConfigElement.Descendants())
            {
                if (subElement == element) { break; }
                actionIndex++;
            }

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "statuseffect":
                        effects.Add(StatusEffect.Load(subElement, $"{nameof(StatusEffectAction)} ({parentEvent.Prefab.Identifier})"));
                        break;
                }
            }
        }

        private bool isFinished = false;

        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }
        public override void Reset()
        {
            isFinished = false;
        }

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }
            var targets = ParentEvent.GetTargets(TargetTag);
            foreach (StatusEffect effect in effects)
            {
                foreach (var target in targets)
                {
                    if (target is Item targetItem)
                    {
                        effect.Apply(effect.type, deltaTime, target, targetItem.AllPropertyObjects);
                    }
                    else
                    {
                        effect.Apply(effect.type, deltaTime, target, target as ISerializableEntity);
                    }
                }
            }
#if SERVER
            ServerWrite(targets);
#endif
            isFinished = true;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(StatusEffectAction)} -> (TargetTag: {TargetTag.ColorizeObject()}";
        }
    }
}