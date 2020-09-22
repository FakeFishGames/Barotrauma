using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class DelayedListElement
    {
        public readonly DelayedEffect Parent;
        public readonly Entity Entity;
        public readonly Vector2? WorldPosition;
        public readonly Vector2? StartPosition;
        public readonly List<ISerializableEntity> Targets;
        public float Delay;

        public DelayedListElement(DelayedEffect parentEffect, Entity parentEntity, IEnumerable<ISerializableEntity> targets, float delay, Vector2? worldPosition, Vector2? startPosition)
        {
            Parent = parentEffect;
            Entity = parentEntity;
            Targets = new List<ISerializableEntity>(targets);
            Delay = delay;
            WorldPosition = worldPosition;
            StartPosition = startPosition;
        }
    }
    class DelayedEffect : StatusEffect
    {
        public static readonly List<DelayedListElement> DelayList = new List<DelayedListElement>();

        private enum DelayTypes { timer = 0, reachcursor = 1 }

        private DelayTypes delayType;
        private float delay;

        public DelayedEffect(XElement element, string parentDebugName)
            : base(element, parentDebugName)
        {
            delayType = (DelayTypes)Enum.Parse(typeof(DelayTypes), element.GetAttributeString("delaytype", "timer"));
            switch (delayType)
            {
                case DelayTypes.timer:
                    delay = element.GetAttributeFloat("delay", 1.0f);
                    break;
            }
        }

        public override void Apply(ActionType type, float deltaTime, Entity entity, ISerializableEntity target, Vector2? worldPosition = null)
        {
            if (this.type != type || !HasRequiredItems(entity)) { return; }
            if (!Stackable && DelayList.Any(d => d.Parent == this && d.Targets.FirstOrDefault() == target)) { return; }
            if (targetIdentifiers != null && !IsValidTarget(target)) { return; }
            if (!HasRequiredConditions(target.ToEnumerable())) { return; }

            switch (delayType)
            {
                case DelayTypes.timer:
                    DelayList.Add(new DelayedListElement(this, entity, target.ToEnumerable(), delay, worldPosition, null));
                    break;
                case DelayTypes.reachcursor:
                    Projectile projectile = (entity as Item)?.GetComponent<Projectile>();
                    if (projectile == null)
                    {
                        DebugConsole.NewMessage("Non-projectile using a delaytype of reachcursor", Color.Red, false, true);
                        return;
                    }

                    if (projectile.User == null)
                    {
                        DebugConsole.NewMessage("Projectile: '" + projectile.Name + "' missing user to determine distance", Color.Red, false, true);
                        return;
                    }

                    DelayList.Add(new DelayedListElement(this, entity, target.ToEnumerable(), Vector2.Distance(entity.WorldPosition, projectile.User.CursorWorldPosition), worldPosition, entity.WorldPosition));
                    break;
            }
        }

        public override void Apply(ActionType type, float deltaTime, Entity entity, IEnumerable<ISerializableEntity> targets, Vector2? worldPosition = null)
        {
            if (this.type != type || !HasRequiredItems(entity)) { return; }
            if (!Stackable && DelayList.Any(d => d.Parent == this && d.Targets.SequenceEqual(targets))) { return; }
            if (delayType == DelayTypes.reachcursor && Character.Controlled == null) return;

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

            if (!HasRequiredConditions(currentTargets)) { return; }

            switch (delayType)
            {
                case DelayTypes.timer:
                    DelayList.Add(new DelayedListElement(this, entity, targets, delay, worldPosition, null));
                    break;
                case DelayTypes.reachcursor:
                    Projectile projectile = (entity as Item)?.GetComponent<Projectile>();
                    if (projectile == null)
                    {
#if DEBUG
                        DebugConsole.NewMessage("Non-projectile using a delaytype of reachcursor", Color.Red, false, true);
#endif
                        return;
                    }

                    if (projectile.User == null)
                    {
#if DEBUG
                        DebugConsole.NewMessage("Projectile " + projectile.Name + "missing user", Color.Red, false, true);
#endif
                        return;
                    }

                    DelayList.Add(new DelayedListElement(this, entity, targets, Vector2.Distance(entity.WorldPosition, projectile.User.CursorWorldPosition), worldPosition, entity.WorldPosition));
                    break;
            }
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

                switch (element.Parent.delayType)
                {
                    case DelayTypes.timer:
                        element.Delay -= deltaTime;
                        if (element.Delay > 0.0f) { continue; }
                        break;
                    case DelayTypes.reachcursor:
                        if (Vector2.Distance(element.Entity.WorldPosition, element.StartPosition.Value) < element.Delay) continue;
                        break;
                }

                element.Parent.Apply(deltaTime, element.Entity, element.Targets, element.WorldPosition);
                DelayList.Remove(element);
            }
        }
    }
}