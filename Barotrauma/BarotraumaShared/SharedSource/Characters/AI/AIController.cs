using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Barotrauma.Items.Components;
using System.Linq;

namespace Barotrauma
{
    abstract partial class AIController : ISteerable
    {
        public bool Enabled;

        public readonly Character Character;

        // Update only when the value changes, not when it keeps the same.
        protected AITarget _lastAiTarget;
        // Updated each time the value is updated (also when the value is the same).
        protected AITarget _previousAiTarget;
        protected AITarget _selectedAiTarget;
        public AITarget SelectedAiTarget
        {
            get { return _selectedAiTarget; }
            protected set
            {
                _previousAiTarget = _selectedAiTarget;
                _selectedAiTarget = value;
                if (_selectedAiTarget != _previousAiTarget)
                {
                    if (_previousAiTarget != null)
                    {
                        _lastAiTarget = _previousAiTarget;
                    }
                    OnTargetChanged(_previousAiTarget, _selectedAiTarget);
                }
            }
        }

        protected SteeringManager steeringManager;

        public SteeringManager SteeringManager
        {
            get { return steeringManager; }
        }

        public Vector2 Steering
        {
            get { return Character.AnimController.TargetMovement; }
            set { Character.AnimController.TargetMovement = value; }
        }

        public Vector2 SimPosition
        {
            get { return Character.SimPosition; }
        }

        public Vector2 WorldPosition
        {
            get { return Character.WorldPosition; }
        }

        public Vector2 Velocity
        {
            get { return Character.AnimController.Collider.LinearVelocity; }
        }

        public virtual bool CanEnterSubmarine
        {
            get { return true; }
        }

        public virtual bool CanFlip
        {
            get { return true; }
        }

        private IEnumerable<Hull> visibleHulls;
        private float hullVisibilityTimer;
        const float hullVisibilityInterval = 0.5f;
        public IEnumerable<Hull> VisibleHulls
        {
            get
            {
                if (visibleHulls == null)
                {
                    visibleHulls = Character.GetVisibleHulls();
                }
                return visibleHulls;
            }
            private set
            {
                visibleHulls = value;
            }
        }

        protected bool HasValidPath(bool requireNonDirty = false) => 
            steeringManager is IndoorsSteeringManager pathSteering && pathSteering.CurrentPath != null && !pathSteering.CurrentPath.Finished && !pathSteering.CurrentPath.Unreachable && (!requireNonDirty || !pathSteering.IsPathDirty);

        public AIController (Character c)
        {
            Character = c;
            hullVisibilityTimer = Rand.Range(0f, hullVisibilityTimer);
            Enabled = true;
        }

        public virtual void OnAttacked(Character attacker, AttackResult attackResult) { }

        public virtual void SelectTarget(AITarget target) { }

        public virtual void Update(float deltaTime)
        {
            if (hullVisibilityTimer > 0)
            {
                hullVisibilityTimer--;
            }
            else
            {
                hullVisibilityTimer = hullVisibilityInterval;
                VisibleHulls = Character.GetVisibleHulls();
            }
        }

        public virtual void Reset()
        {
            ResetAITarget();
        }

        protected void ResetAITarget()
        {
            _lastAiTarget = null;
            _selectedAiTarget = null;
        }

        public void FaceTarget(ISpatialEntity target) => Character.AnimController.TargetDir = target.WorldPosition.X > Character.WorldPosition.X ? Direction.Right : Direction.Left;

        public bool IsSteeringThroughGap { get; protected set; }

        public virtual bool SteerThroughGap(Structure wall, WallSection section, Vector2 targetWorldPos, float deltaTime)
        {
            if (wall == null) { return false; }
            if (section == null) { return false; }
            Gap gap = section.gap;
            if (gap == null) { return false; }
            float maxDistance = Math.Min(wall.Rect.Width, wall.Rect.Height);
            if (Vector2.DistanceSquared(Character.WorldPosition, targetWorldPos) > maxDistance * maxDistance) { return false; }
            Hull targetHull = gap.FlowTargetHull;
            if (targetHull == null) { return false; }
            if (wall.IsHorizontal)
            {
                targetWorldPos.Y = targetHull.WorldRect.Y - targetHull.Rect.Height / 2;
            }
            else
            {
                targetWorldPos.X = targetHull.WorldRect.Center.X;
            }
            return SteerThroughGap(gap, targetWorldPos, deltaTime, maxDistance: -1);
        }

        public virtual bool SteerThroughGap(Gap gap, Vector2 targetWorldPos, float deltaTime, float maxDistance = -1)
        {
            Hull targetHull = gap.FlowTargetHull;
            if (targetHull == null) { return false; }
            if (maxDistance > 0)
            {
                if (Vector2.DistanceSquared(Character.WorldPosition, targetWorldPos) > maxDistance * maxDistance) { return false; }
            }
            if (SteeringManager is IndoorsSteeringManager pathSteering)
            {
                pathSteering.ResetPath();
            }
            SteeringManager.SteeringManual(deltaTime, Vector2.Normalize(targetWorldPos - Character.WorldPosition));
            return true;
        }

        public bool CanPassThroughHole(Structure wall, int sectionIndex, int requiredHoleCount)
        {
            if (!wall.SectionBodyDisabled(sectionIndex)) { return false; }
            int holeCount = 1;
            for (int j = sectionIndex - 1; j > sectionIndex - requiredHoleCount; j--)
            {
                if (wall.SectionBodyDisabled(j))
                {
                    holeCount++;
                }
                else
                {
                    break;
                }
            }
            for (int j = sectionIndex + 1; j < sectionIndex + requiredHoleCount; j++)
            {
                if (wall.SectionBodyDisabled(j))
                {
                    holeCount++;
                }
                else
                {
                    break;
                }
            }
            return holeCount >= requiredHoleCount;
        }

        protected bool IsWallDisabled(Structure wall)
        {
            bool isDisabled = true;
            for (int i = 0; i < wall.Sections.Length; i++)
            {
                if (!wall.SectionBodyDisabled(i))
                {
                    isDisabled = false;
                    break;
                }
            }
            return isDisabled;
        }

        private readonly HashSet<Item> unequippedItems = new HashSet<Item>();
        public bool TakeItem(Item item, Inventory targetInventory, bool equip, bool dropOtherIfCannotMove = true, bool allowSwapping = false, bool storeUnequipped = false)
        {
            var pickable = item.GetComponent<Pickable>();
            if (pickable == null) { return false; }
            if (item.ParentInventory is ItemInventory itemInventory)
            {
                if (!itemInventory.Container.HasRequiredItems(Character, addMessage: false)) { return false; }
            }
            if (equip)
            {
                int targetSlot = -1;
                //check if all the slots required by the item are free
                foreach (InvSlotType slots in pickable.AllowedSlots)
                {
                    if (slots.HasFlag(InvSlotType.Any)) { continue; }
                    for (int i = 0; i < targetInventory.Capacity; i++)
                    {
                        if (targetInventory is CharacterInventory characterInventory)
                        {
                            //slot not needed by the item, continue
                            if (!slots.HasFlag(characterInventory.SlotTypes[i])) { continue; }
                        }
                        targetSlot = i;
                        //slot free, continue
                        var otherItem = targetInventory.GetItemAt(i);
                        if (otherItem == null) { continue; }
                        //try to move the existing item to LimbSlot.Any and continue if successful
                        if (otherItem.AllowedSlots.Contains(InvSlotType.Any) && targetInventory.TryPutItem(otherItem, Character, CharacterInventory.anySlot))
                        {
                            if (storeUnequipped && targetInventory.Owner == Character)
                            {
                                unequippedItems.Add(otherItem);
                            }
                            continue;
                        }
                        if (dropOtherIfCannotMove)
                        {
                            //if everything else fails, simply drop the existing item
                            otherItem.Drop(Character);
                        }
                    }
                }
                return targetInventory.TryPutItem(item, targetSlot, allowSwapping, allowCombine: false, Character);
            }
            else
            {
                return targetInventory.TryPutItem(item, Character, CharacterInventory.anySlot);
            }
        }

        public void UnequipEmptyItems(Item parentItem, bool avoidDroppingInSea = true) => UnequipEmptyItems(Character, parentItem, avoidDroppingInSea);

        public void UnequipContainedItems(Item parentItem, Func<Item, bool> predicate, bool avoidDroppingInSea = true) => UnequipContainedItems(Character, parentItem, predicate, avoidDroppingInSea);

        public static void UnequipEmptyItems(Character character, Item parentItem, bool avoidDroppingInSea = true) => UnequipContainedItems(character, parentItem, it => it.Condition <= 0, avoidDroppingInSea);

        public static void UnequipContainedItems(Character character, Item parentItem, Func<Item, bool> predicate, bool avoidDroppingInSea = true)
        {
            var inventory = parentItem.OwnInventory;
            if (inventory == null) { return; }
            if (inventory.AllItems.Any(predicate))
            {
                foreach (Item containedItem in inventory.AllItemsMod)
                {
                    if (containedItem == null) { continue; }
                    if (predicate(containedItem))
                    {
                        if (character.Submarine == null && avoidDroppingInSea)
                        {
                            // If we are outside of main sub, try to put the item in the inventory instead dropping it in the sea.
                            if (character.Inventory.TryPutItem(containedItem, character, CharacterInventory.anySlot))
                            {
                                continue;
                            }
                        }
                        containedItem.Drop(character);
                    }
                }
            }
        }

        public void ReequipUnequipped()
        {
            foreach (var item in unequippedItems)
            {
                if (item != null && !item.Removed && Character.HasItem(item))
                {
                    TakeItem(item, Character.Inventory, equip: true, dropOtherIfCannotMove: true, allowSwapping: true, storeUnequipped: false);
                }
            }
            unequippedItems.Clear();
        }

        protected virtual void OnStateChanged(AIState from, AIState to) { }
        protected virtual void OnTargetChanged(AITarget previousTarget, AITarget newTarget) { }
    }
}
