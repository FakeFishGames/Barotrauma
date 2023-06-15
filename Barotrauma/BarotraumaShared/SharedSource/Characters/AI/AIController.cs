using Barotrauma.Items.Components;
using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
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
                        if (_selectedAiTarget != null)
                        {
                            if (_selectedAiTarget.Entity is Item i && _previousAiTarget.Entity is Character c)
                            {
                                if (i.IsOwnedBy(c)) { return; }
                            }
                            else if (_previousAiTarget.Entity is Item it && _selectedAiTarget.Entity is Character ch)
                            {
                                if (it.IsOwnedBy(ch)) { return; }
                            }
                        }
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

        public virtual bool IsMentallyUnstable => false;

        private IEnumerable<Hull> visibleHulls;
        private float hullVisibilityTimer;
        const float hullVisibilityInterval = 0.5f;
        public IEnumerable<Hull> VisibleHulls
        {
            get
            {
                visibleHulls ??= Character.GetVisibleHulls();
                return visibleHulls;
            }
            private set
            {
                visibleHulls = value;
            }
        }

        /// <summary>
        /// Is the current path valid, using the provided parameters.
        /// </summary>
        /// <param name="requireNonDirty"></param>
        /// <param name="requireUnfinished"></param>
        /// <param name="nodePredicate"></param>
        /// <returns>When <paramref name="nodePredicate"/> is defined, returns false if any of the nodes fails to match the predicate.</returns>
        public bool HasValidPath(bool requireNonDirty = true, bool requireUnfinished = true, Func<WayPoint, bool> nodePredicate = null)
        {
            if (SteeringManager is not IndoorsSteeringManager pathSteering) { return false; }
            if (pathSteering.CurrentPath == null) { return false; }
            if (pathSteering.CurrentPath.Unreachable) { return false; }
            if (requireUnfinished && pathSteering.CurrentPath.Finished) { return false; }
            if (requireNonDirty && pathSteering.IsPathDirty) { return false; }
            if (nodePredicate != null)
            {
                return pathSteering.CurrentPath.Nodes.All(n => nodePredicate(n));
            }
            return true;
        }

        public bool IsCurrentPathNullOrUnreachable => IsCurrentPathUnreachable || steeringManager is IndoorsSteeringManager pathSteering && pathSteering.CurrentPath == null;
        public bool IsCurrentPathUnreachable => steeringManager is IndoorsSteeringManager pathSteering && !pathSteering.IsPathDirty && pathSteering.CurrentPath != null && pathSteering.CurrentPath.Unreachable;
        public bool IsCurrentPathFinished => steeringManager is IndoorsSteeringManager pathSteering && !pathSteering.IsPathDirty && pathSteering.CurrentPath != null && pathSteering.CurrentPath.Finished;

        protected readonly float colliderWidth;
        protected readonly float minGapSize;
        protected readonly float colliderLength;
        protected readonly float avoidLookAheadDistance;

        public AIController (Character c)
        {
            Character = c;
            hullVisibilityTimer = Rand.Range(0f, hullVisibilityTimer);
            Enabled = true;
            var size = Character.AnimController.Collider.GetSize();
            colliderWidth = size.X;
            colliderLength = size.Y;
            avoidLookAheadDistance = Math.Max(Math.Max(colliderWidth, colliderLength) * 3, 1.5f);
            minGapSize = ConvertUnits.ToDisplayUnits(Math.Min(colliderWidth, colliderLength));
        }

        public virtual void OnHealed(Character healer, float healAmount) { }

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
        public bool IsTryingToSteerThroughGap { get; protected set; }

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
        public bool TakeItem(Item item, CharacterInventory targetInventory, bool equip, bool wear = false, bool dropOtherIfCannotMove = true, bool allowSwapping = false, bool storeUnequipped = false, IEnumerable<Identifier> targetTags = null)
        {
            var pickable = item.GetComponent<Pickable>();
            if (pickable == null) { return false; }
            if (wear)
            {
                var wearable = item.GetComponent<Wearable>();
                if (wearable != null)
                {
                    pickable = wearable;
                }
            }
            else
            {
                // Not allowed to wear -> don't use the Wearable component even when it's found.
                pickable = item.GetComponent<Holdable>();
            }
            if (item.ParentInventory is ItemInventory itemInventory)
            {
                if (!itemInventory.Container.HasRequiredItems(Character, addMessage: false)) { return false; }
            }
            if (equip && pickable != null)
            {
                int targetSlot = -1;
                //check if all the slots required by the item are free
                foreach (InvSlotType slots in pickable.AllowedSlots)
                {
                    if (slots.HasFlag(InvSlotType.Any)) { continue; }
                    if (!wear)
                    {
                        if (slots != InvSlotType.RightHand && slots != InvSlotType.LeftHand && slots != (InvSlotType.RightHand | InvSlotType.LeftHand))
                        {
                            // Don't allow other than hand slots if not allowed to wear.
                            continue;
                        }
                    }
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
                        if (otherItem.AllowedSlots.Contains(InvSlotType.Any) && targetInventory.TryPutItem(otherItem, Character, CharacterInventory.AnySlot))
                        {
                            if (storeUnequipped && targetInventory.Owner == Character)
                            {
                                unequippedItems.Add(otherItem);
                            }
                            continue;
                        }
                        if (dropOtherIfCannotMove)
                        {
                            if (otherItem.Prefab.Identifier == item.Prefab.Identifier || otherItem.HasIdentifierOrTags(targetTags))
                            {
                                // Shouldn't try dropping identical items, because that causes infinite looping when trying to get multiple items of the same type and if can't fit them all in the inventory.
                                return false;
                            }
                            //if everything else fails, simply drop the existing item
                            otherItem.Drop(Character);
                        }
                    }
                }
                if (targetSlot < 0) { return false; }
                return targetInventory.TryPutItem(item, targetSlot, allowSwapping, allowCombine: false, Character);
            }
            else
            {
                return targetInventory.TryPutItem(item, Character, CharacterInventory.AnySlot);
            }
        }

        public void UnequipEmptyItems(Item parentItem, bool avoidDroppingInSea = true) => UnequipEmptyItems(Character, parentItem, avoidDroppingInSea);

        public void UnequipContainedItems(Item parentItem, Func<Item, bool> predicate = null, bool avoidDroppingInSea = true, int? unequipMax = null) => UnequipContainedItems(Character, parentItem, predicate, avoidDroppingInSea, unequipMax);

        public static void UnequipEmptyItems(Character character, Item parentItem, bool avoidDroppingInSea = true) => UnequipContainedItems(character, parentItem, it => it.Condition <= 0, avoidDroppingInSea);

        public static void UnequipContainedItems(Character character, Item parentItem, Func<Item, bool> predicate, bool avoidDroppingInSea = true, int? unequipMax = null)
        {
            var inventory = parentItem.OwnInventory;
            if (inventory == null) { return; }
            int removed = 0;
            if (predicate == null || inventory.AllItems.Any(predicate))
            {
                foreach (Item containedItem in inventory.AllItemsMod)
                {
                    if (containedItem == null) { continue; }
                    if (predicate == null || predicate(containedItem))
                    {
                        if (avoidDroppingInSea && !character.IsInFriendlySub)
                        {
                            // If we are not inside a friendly sub (= same team), try to put the item in the inventory instead dropping it.
                            if (character.Inventory.TryPutItem(containedItem, character, CharacterInventory.AnySlot))
                            {
                                if (unequipMax.HasValue && ++removed >= unequipMax) { return; }
                                continue;
                            }
                        }
                        containedItem.Drop(character);
                        if (unequipMax.HasValue && ++removed >= unequipMax) { return; }
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
                    TakeItem(item, Character.Inventory, equip: true, wear: true, dropOtherIfCannotMove: true, allowSwapping: true, storeUnequipped: false);
                }
            }
            unequippedItems.Clear();
        }

        #region Escape
        public abstract bool Escape(float deltaTime);

        public Gap EscapeTarget { get; private set; }

        private readonly float escapeTargetSeekInterval = 2;
        private float escapeTimer;
        protected bool allGapsSearched;
        protected readonly HashSet<Gap> unreachableGaps = new HashSet<Gap>();
        protected bool UpdateEscape(float deltaTime, bool canAttackDoors)
        {
            IndoorsSteeringManager pathSteering = SteeringManager as IndoorsSteeringManager;
            if (allGapsSearched)
            {
                escapeTimer -= deltaTime;
                if (escapeTimer <= 0)
                {
                    allGapsSearched = false;
                }
            }
            if (Character.CurrentHull != null && pathSteering != null)
            {
                // Seek exit if inside
                if (!allGapsSearched)
                {
                    float closestDistance = 0;
                    foreach (Gap gap in Gap.GapList)
                    {
                        if (gap == null || gap.Removed) { continue; }
                        if (EscapeTarget == gap) { continue; }
                        if (unreachableGaps.Contains(gap)) { continue; }
                        if (gap.Submarine != Character.Submarine) { continue; }
                        if (gap.IsRoomToRoom) { continue; }
                        float multiplier = 1;
                        var door = gap.ConnectedDoor;
                        if (door != null)
                        {
                            if (!pathSteering.CanAccessDoor(door))
                            {
                                continue;
                            }
                        }
                        else
                        {
                            if (gap.Open < 1) { continue; }
                            if (gap.Size < minGapSize) { continue; }
                        }
                        if (gap.FlowTargetHull == Character.CurrentHull)
                        {
                            // If the gap is in the same room, it's close enough.
                            EscapeTarget = gap;
                            break;
                        }
                        float distance = Vector2.DistanceSquared(Character.WorldPosition, gap.WorldPosition) * multiplier;
                        if (EscapeTarget == null || distance < closestDistance)
                        {
                            EscapeTarget = gap;
                            closestDistance = distance;
                        }
                    }
                    allGapsSearched = true;
                    escapeTimer = escapeTargetSeekInterval;
                }
                else if (EscapeTarget != null && EscapeTarget.FlowTargetHull != Character.CurrentHull)
                {
                    if (IsCurrentPathUnreachable)
                    {
                        unreachableGaps.Add(EscapeTarget);
                        EscapeTarget = null;
                        allGapsSearched = false;
                    }
                }
            }
            if (EscapeTarget != null)
            {
                var door = EscapeTarget.ConnectedDoor;
                bool isClosedDoor = door != null && door.IsClosed;
                Vector2 diff = EscapeTarget.WorldPosition - Character.WorldPosition;
                float sqrDist = diff.LengthSquared();
                bool isClose = sqrDist < MathUtils.Pow2(100);
                if (Character.CurrentHull == null || (isClose && !isClosedDoor) || pathSteering == null || IsCurrentPathUnreachable || IsCurrentPathFinished)
                {
                    // Very close to the target, outside, or at the end of the path -> try to steer through the gap
                    Character.ReleaseSecondaryItem();
                    SteeringManager.Reset();
                    pathSteering?.ResetPath();
                    Vector2 dir = Vector2.Normalize(diff);
                    if (Character.CurrentHull == null || isClose)
                    {
                        // Outside -> steer away from the target
                        if (EscapeTarget.FlowTargetHull != null)
                        {
                            SteeringManager.SteeringManual(deltaTime, Vector2.Normalize(EscapeTarget.WorldPosition - EscapeTarget.FlowTargetHull.WorldPosition));
                        }
                        else
                        {
                            SteeringManager.SteeringManual(deltaTime, -dir);
                        }
                    }
                    else
                    {
                        // Still inside -> steer towards the target
                        SteeringManager.SteeringManual(deltaTime, dir);
                    }
                    return sqrDist < MathUtils.Pow2(250);
                }
                else if (pathSteering != null)
                {
                    pathSteering.SteeringSeek(EscapeTarget.SimPosition, weight: 1, minGapSize);
                }
                else
                {
                    SteeringManager.SteeringSeek(EscapeTarget.SimPosition, 10);
                }
            }
            else
            {
                // Can't find the target
                EscapeTarget = null;
                allGapsSearched = false;
                unreachableGaps.Clear();
            }
            return false;
        }

        public void ResetEscape()
        {
            EscapeTarget = null;
            allGapsSearched = false;
            unreachableGaps.Clear();
        }

        #endregion

        protected virtual void OnStateChanged(AIState from, AIState to) { }
        protected virtual void OnTargetChanged(AITarget previousTarget, AITarget newTarget) { }

        public virtual void ClientRead(IReadMessage msg) { }
        public virtual void ServerWrite(IWriteMessage msg) { }
    }
}
