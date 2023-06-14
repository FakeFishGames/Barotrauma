using System.Collections.Generic;

namespace Barotrauma
{
    partial class StatusEffectAction : EventAction
    {
        private readonly List<StatusEffect> effects = new List<StatusEffect>();

        private readonly int actionIndex;

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier TargetTag { get; set; }

        public StatusEffectAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) 
        {
            actionIndex = 0;
            foreach (var subElement in parentEvent.Prefab.ConfigElement.Descendants())
            {
                if (subElement == element) { break; }
                actionIndex++;
            }

            foreach (var subElement in element.Elements())
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
            var eventTargets = ParentEvent.GetTargets(TargetTag);
            foreach (StatusEffect effect in effects)
            {
                foreach (var target in eventTargets)
                {
                    if (effect.HasTargetType(StatusEffect.TargetType.NearbyCharacters))
                    {
                        List<ISerializableEntity> nearbyTargets = new List<ISerializableEntity>();
                        effect.AddNearbyTargets(target.WorldPosition, nearbyTargets);
                        foreach (var nearbyTarget in nearbyTargets)
                        {
                            ApplyOnTarget(nearbyTarget as Entity, effect);
                        }
                        continue;
                    }
                    ApplyOnTarget(target, effect);
                }
            }
#if SERVER
            ServerWrite(eventTargets);
#endif
            isFinished = true;

            void ApplyOnTarget(Entity target, StatusEffect effect)
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

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(StatusEffectAction)} -> (TargetTag: {TargetTag.ColorizeObject()}";
        }
    }
}