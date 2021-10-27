using Barotrauma.Extensions;
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

        public bool HasValidPath(bool requireNonDirty = false, bool requireUnfinished = true) => 
            steeringManager is IndoorsSteeringManager pathSteering &&
            pathSteering.CurrentPath != null &&
            (!requireUnfinished || !pathSteering.CurrentPath.Finished) &&
            !pathSteering.CurrentPath.Unreachable &&
            (!requireNonDirty || !pathSteering.IsPathDirty);

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
        public bool TakeItem(Item item, CharacterInventory targetInventory, bool equip, bool wear = false, bool dropOtherIfCannotMove = true, bool allowSwapping = false, bool storeUnequipped = false)
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
                var holdable = item.GetComponent<Holdable>();
                if (holdable != null)
                {
                    pickable = holdable;
                }
            }
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

        public void UnequipContainedItems(Item parentItem, Func<Item, bool> predicate = null, bool avoidDroppingInSea = true) => UnequipContainedItems(Character, parentItem, predicate, avoidDroppingInSea);

        public static void UnequipEmptyItems(Character character, Item parentItem, bool avoidDroppingInSea = true) => UnequipContainedItems(character, parentItem, it => it.Condition <= 0, avoidDroppingInSea);

        public static void UnequipContainedItems(Character character, Item parentItem, Func<Item, bool> predicate, bool avoidDroppingInSea = true)
        {
            var inventory = parentItem.OwnInventory;
            if (inventory == null) { return; }
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
                            if (!door.CanBeTraversed)
                            {
                                if (!door.HasAccess(Character))
                                {
                                    if (!canAttackDoors) { continue; }
                                    // Treat doors that don't have access to like they were farther, because it will take time to break them.
                                    multiplier = 5;
                                }
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
                bool isClosedDoor = door != null && !door.IsOpen;
                Vector2 diff = EscapeTarget.WorldPosition - Character.WorldPosition;
                float sqrDist = diff.LengthSquared();
                bool isClose = sqrDist < MathUtils.Pow2(100);
                if (Character.CurrentHull == null || isClose && !isClosedDoor || pathSteering == null || IsCurrentPathUnreachable || IsCurrentPathFinished)
                {
                    // Very close to the target, outside, or at the end of the path -> try to steer through the gap
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
