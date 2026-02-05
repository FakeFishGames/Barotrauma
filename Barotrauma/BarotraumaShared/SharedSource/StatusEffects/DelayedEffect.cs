using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class DelayedListElement
    {
        public readonly DelayedEffect Parent;
        public readonly Entity Entity;
        public Vector2? WorldPosition;
        /// <summary>
        /// Should the delayed effect attempt to determine the position of the effect based on the targets, or just use the position that was passed to the constructor.
        /// </summary>
        public bool GetPositionBasedOnTargets;
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

        private enum DelayTypes 
        { 
            Timer = 0,
            [Obsolete("The delay type is unsupported.")]
            ReachCursor = 1 
        }

        private readonly DelayTypes delayType;
        private readonly float delay;

        public DelayedEffect(ContentXElement element, string parentDebugName)
            : base(element, parentDebugName)
        {
            delayType = element.GetAttributeEnum("delaytype", DelayTypes.Timer);
            if (delayType == DelayTypes.ReachCursor)
            {
                DebugConsole.AddWarning($"Potential error in {parentDebugName}: the delay type {DelayTypes.ReachCursor} is not supported.", contentPackage: element.ContentPackage);
            }
            if (delayType is DelayTypes.Timer)
            {
                delay = element.GetAttributeFloat("delay", 1.0f);
            }
        }

        public override void Apply(ActionType type, float deltaTime, Entity entity, ISerializableEntity target, Vector2? worldPosition = null)
        {
            if (this.type != type || !HasRequiredItems(entity)) { return; }
            if (!Stackable)
            {
                foreach (var existingEffect in DelayList)
                {
                    if (existingEffect.Parent == this && existingEffect.Targets.FirstOrDefault() == target) 
                    {
                        return; 
                    }
                }
            }
            if (!IsValidTarget(target)) { return; }

            currentTargets.Clear();
            currentTargets.Add(target);
            if (!HasRequiredConditions(currentTargets)) { return; }

            switch (delayType)
            {
                case DelayTypes.Timer:
                    var newDelayListElement = new DelayedListElement(this, entity, currentTargets, delay, worldPosition ?? GetPosition(entity, currentTargets, worldPosition), startPosition: null)
                    {
                        GetPositionBasedOnTargets = worldPosition == null
                    };
                    DelayList.Add(newDelayListElement);
                    break;
                case DelayTypes.ReachCursor:
                    Projectile projectile = (entity as Item)?.GetComponent<Projectile>();
                    if (projectile == null)
                    {
                        DebugConsole.LogError("Non-projectile using a delaytype of reachcursor");
                        return;
                    }

                    var user =
                        projectile.User ??
                        projectile.Attacker ??
                        projectile.Launcher?.GetRootInventoryOwner() as Character;
                    if (user == null)
                    {
#if DEBUG
                        DebugConsole.LogError($"Projectile \"{projectile.Item.Prefab.Identifier}\" missing user");
#endif
                        return;
                    }

                    DelayList.Add(new DelayedListElement(this, entity, currentTargets, Vector2.Distance(entity.WorldPosition, projectile.User.CursorWorldPosition), worldPosition, entity.WorldPosition));
                    break;
            }
        }

        public override void Apply(ActionType type, float deltaTime, Entity entity, IReadOnlyList<ISerializableEntity> targets, Vector2? worldPosition = null)
        {
            if (this.type != type) { return; }
            if (Disabled) { return; }
            if (ShouldWaitForInterval(entity, deltaTime)) { return; }
            if (!HasRequiredItems(entity)) { return; }
            if (delayType == DelayTypes.ReachCursor && Character.Controlled == null) { return; }
            if (!Stackable) 
            { 
                foreach (var existingEffect in DelayList)
                {
                    if (existingEffect.Parent == this && existingEffect.Targets.SequenceEqual(targets)) { return; }
                }
            }

            currentTargets.Clear();
            foreach (ISerializableEntity target in targets)
            {
                if (!IsValidTarget(target)) { continue; }
                currentTargets.Add(target);
            }

            if (!HasRequiredConditions(currentTargets)) { return; }

            switch (delayType)
            {
                case DelayTypes.Timer:
                    DelayList.Add(new DelayedListElement(this, entity, currentTargets, delay, worldPosition, null));
                    break;
                case DelayTypes.ReachCursor:
                    Projectile projectile = (entity as Item)?.GetComponent<Projectile>();
                    if (projectile == null)
                    {
#if DEBUG
                        DebugConsole.LogError("Non-projectile using a delaytype of reachcursor");
#endif
                        return;
                    }

                    var user =
                        projectile.User ??
                        projectile.Attacker ??
                        projectile.Launcher?.GetRootInventoryOwner() as Character;
                    if (user == null)
                    {
#if DEBUG
                        DebugConsole.LogError($"Projectile \"{projectile.Item.Prefab.Identifier}\" missing user");
#endif
                        return;
                    }

                    DelayList.Add(new DelayedListElement(this, entity, currentTargets, Vector2.Distance(entity.WorldPosition, user.CursorWorldPosition), worldPosition, entity.WorldPosition));
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
                    case DelayTypes.Timer:
                        element.Delay -= deltaTime;
                        if (element.Delay > 0.0f) 
                        { 
                            //if the delayed effect is supposed to get the position from the targets,
                            //keep refreshing the position until the effect runs (so e.g. a delayed effect runs at the last known position of a monster before it despawned)
                            if (element.GetPositionBasedOnTargets && element.Entity is { Removed: false })
                            {
                                element.WorldPosition = element.Parent.GetPosition(element.Entity, element.Parent.currentTargets);
                            }
                            continue; 
                        }
                        break;
                    case DelayTypes.ReachCursor:
                        if (Vector2.Distance(element.Entity.WorldPosition, element.StartPosition.Value) < element.Delay) { continue; }
                        break;
                }

                element.Parent.Apply(deltaTime, element.Entity, element.Targets, element.WorldPosition);
                DelayList.Remove(element);
            }
        }
    }
}