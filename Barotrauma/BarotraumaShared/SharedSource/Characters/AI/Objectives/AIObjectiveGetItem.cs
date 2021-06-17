using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveGetItem : AIObjective
    {
        public override string Identifier { get; set; } = "get item";

        public override bool AbandonWhenCannotCompleteSubjectives => false;

        public HashSet<Item> ignoredItems = new HashSet<Item>();

        public Func<Item, float> GetItemPriority;
        public Func<Item, bool> ItemFilter;
        public float TargetCondition { get; set; } = 1;
        public bool AllowDangerousPressure { get; set; }

        private readonly string[] identifiersOrTags;

        //if the item can't be found, spawn it in the character's inventory (used by outpost NPCs)
        private bool spawnItemIfNotFound = false;

        private Item targetItem;
        private readonly Item originalTarget;
        private ISpatialEntity moveToTarget;
        private bool isDoneSeeking;
        public Item TargetItem => targetItem;
        private int currSearchIndex;
        public string[] ignoredContainerIdentifiers;
        private AIObjectiveGoTo goToObjective;
        private float currItemPriority;
        private readonly bool checkInventory;

        public static float DefaultReach = 100;

        public bool AllowToFindDivingGear { get; set; } = true;
        public bool MustBeSpecificItem { get; set; }

        /// <summary>
        /// Is the character allowed to take the item from somewhere else than their own sub (e.g. an outpost)
        /// </summary>
        public bool AllowStealing { get; set; }
        public bool TakeWholeStack { get; set; }
        public bool Equip { get; set; }
        public bool Wear { get; set; }

        public InvSlotType? EquipSlotType { get; set; }

        public AIObjectiveGetItem(Character character, Item targetItem, AIObjectiveManager objectiveManager, bool equip = true, float priorityModifier = 1) 
            : base(character, objectiveManager, priorityModifier)
        {
            currSearchIndex = -1;
            Equip = equip;
            originalTarget = targetItem;
            this.targetItem = targetItem;
            moveToTarget = targetItem?.GetRootInventoryOwner();
        }

        public AIObjectiveGetItem(Character character, string identifierOrTag, AIObjectiveManager objectiveManager, bool equip = true, bool checkInventory = true, float priorityModifier = 1, bool spawnItemIfNotFound = false) 
            : this(character, new string[] { identifierOrTag }, objectiveManager, equip, checkInventory, priorityModifier, spawnItemIfNotFound) { }

        public AIObjectiveGetItem(Character character, string[] identifiersOrTags, AIObjectiveManager objectiveManager, bool equip = true, bool checkInventory = true, float priorityModifier = 1, bool spawnItemIfNotFound = false) 
            : base(character, objectiveManager, priorityModifier)
        {
            currSearchIndex = -1;
            Equip = equip;
            this.identifiersOrTags = identifiersOrTags;
            this.spawnItemIfNotFound = spawnItemIfNotFound;
            for (int i = 0; i < identifiersOrTags.Length; i++)
            {
                identifiersOrTags[i] = identifiersOrTags[i].ToLowerInvariant();
            }
            this.checkInventory = checkInventory;
        }

        private bool CheckInventory()
        {
            if (identifiersOrTags == null) { return false; }
            var item = character.Inventory.FindItem(i => CheckItem(i), recursive: true);
            if (item != null)
            {
                targetItem = item;
                moveToTarget = item.GetRootInventoryOwner();
            }
            return item != null;
        }

        protected override void Act(float deltaTime)
        {
            if (character.LockHands)
            {
                Abandon = true;
                return;
            }
            if (character.Submarine == null)
            {
                Abandon = true;
                return;
            }
            if (identifiersOrTags != null && !isDoneSeeking)
            {
                if (checkInventory)
                {
                    if (CheckInventory())
                    {
                        isDoneSeeking = true;
                    }
                }
                if (!isDoneSeeking)
                {
                    if (!AllowDangerousPressure)
                    {
                        bool dangerousPressure = character.CurrentHull == null || character.CurrentHull.LethalPressure > 0 && character.PressureProtection <= 0;
                        if (dangerousPressure)
                        {
#if DEBUG
                            string itemName = targetItem != null ? targetItem.Name : identifiersOrTags.FirstOrDefault();
                            DebugConsole.NewMessage($"{character.Name}: Seeking item ({itemName}) aborted, because the pressure is dangerous.", Color.Yellow);
#endif
                            Abandon = true;
                            return;
                        }
                    }
                    FindTargetItem();
                    if (!objectiveManager.IsCurrentOrder<AIObjectiveGoTo>())
                    {
                        objectiveManager.GetObjective<AIObjectiveIdle>().Wander(deltaTime);
                    }
                    return;
                }
            }
            if (targetItem == null || targetItem.Removed)
            {
#if DEBUG
                DebugConsole.NewMessage($"{character.Name}: Target null or removed. Aborting.", Color.Red);
#endif
                Abandon = true;
                return;
            }
            else if (isDoneSeeking && moveToTarget == null)
            {
#if DEBUG
                DebugConsole.NewMessage($"{character.Name}: Move target null. Aborting.", Color.Red);
#endif
                Abandon = true;
                return;
            }
            if (character.IsItemTakenBySomeoneElse(targetItem))
            {
#if DEBUG
                DebugConsole.NewMessage($"{character.Name}: Found an item, but it's already equipped by someone else.", Color.Yellow);
#endif
                if (originalTarget == null)
                {
                    // Try again
                    ignoredItems.Add(targetItem);
                    ResetInternal();
                }
                else
                {
                    Abandon = true;
                }
                return;
            }
            bool canInteract = false;
            if (moveToTarget is Character c)
            {
                if (character == c)
                {
                    canInteract = true;
                    moveToTarget = null;
                }
                else
                {
                    character.SelectCharacter(c);
                    canInteract = character.CanInteractWith(c, maxDist: DefaultReach);
                    character.DeselectCharacter();
                }
            }
            else if (moveToTarget is Item parentItem)
            {
                canInteract = character.CanInteractWith(parentItem, checkLinked: false);
            }
            if (canInteract)
            {
                var pickable = targetItem.GetComponent<Pickable>();
                if (pickable == null)
                {
#if DEBUG
                    DebugConsole.NewMessage($"{character.Name}: Target not pickable. Aborting.", Color.Yellow);
#endif
                    Abandon = true;
                    return;
                }

                Inventory itemInventory = targetItem.ParentInventory;
                var slots = itemInventory?.FindIndices(targetItem);
                if (HumanAIController.TakeItem(targetItem, character.Inventory, Equip, Wear, storeUnequipped: true))
                {
                    if (TakeWholeStack && slots != null)
                    {
                        foreach (int slot in slots)
                        {
                            foreach (Item item in itemInventory.GetItemsAt(slot).ToList())
                            {
                                HumanAIController.TakeItem(item, character.Inventory, equip: false, storeUnequipped: true);
                            }
                        }
                    }
                    IsCompleted = true;
                }
                else
                {
#if DEBUG
                    DebugConsole.NewMessage($"{character.Name}: Failed to equip/move the item '{targetItem.Name}' into the character inventory. Aborting.", Color.Red);
#endif
                    Abandon = true;
                }
            }
            else if (moveToTarget != null)
            {
                TryAddSubObjective(ref goToObjective,
                    constructor: () =>
                    {
                        return new AIObjectiveGoTo(moveToTarget, character, objectiveManager, repeat: false, getDivingGearIfNeeded: AllowToFindDivingGear, closeEnough: DefaultReach)
                        {
                            // If the root container changes, the item is no longer where it was (taken by someone -> need to find another item)
                            AbortCondition = obj => targetItem == null || targetItem.GetRootInventoryOwner() != moveToTarget,
                            SpeakIfFails = false
                        };
                    },
                    onAbandon: () =>
                    {
                        if (originalTarget == null)
                        {
                            // Try again
                            ignoredItems.Add(targetItem);
                            ResetInternal();
                        }
                        else
                        {
                            Abandon = true;
                        }
                    },
                    onCompleted: () => RemoveSubObjective(ref goToObjective));
            }
        }

        private void FindTargetItem()
        {
            if (identifiersOrTags == null)
            {
                if (targetItem == null)
                {
#if DEBUG
                    DebugConsole.NewMessage($"{character.Name}: Cannot find an item, because neither identifiers nor item was defined.", Color.Red);
#endif
                    Abandon = true;
                }
                return;
            }

            float priority = Math.Clamp(objectiveManager.GetCurrentPriority(), 10, 100);
            bool checkPath = priority >= AIObjectiveManager.LowestOrderPriority && (objectiveManager.IsCurrentOrder<AIObjectiveFixLeaks>() || objectiveManager.CurrentOrder is AIObjectiveGoTo gotoOrder && gotoOrder.followControlledCharacter);
            bool hasCalledPathFinder = false;
            int itemsPerFrame = (int)priority;
            for (int i = 0; i < itemsPerFrame && currSearchIndex < Item.ItemList.Count - 1; i++)
            {
                currSearchIndex++;
                var item = Item.ItemList[currSearchIndex];
                Submarine itemSub = item.Submarine ?? item.ParentInventory?.Owner?.Submarine;
                if (itemSub == null) { continue; }
                Submarine mySub = character.Submarine;
                if (mySub == null) { continue; }
                if (!AllowStealing)
                {
                    if (character.TeamID == CharacterTeamType.FriendlyNPC != item.SpawnedInOutpost) { continue; }
                }
                if (!CheckItem(item)) { continue; }
                if (item.Container != null)
                {
                    if (item.Container.HasTag("donttakeitems")) { continue; }
                    if (ignoredContainerIdentifiers != null)
                    {
                        if (ignoredContainerIdentifiers.Contains(item.ContainerIdentifier)) { continue; }
                    }
                }
                // Don't allow going into another sub, unless it's connected and of the same team and type.
                if (!character.Submarine.IsEntityFoundOnThisSub(item, includingConnectedSubs: true)) { continue; }
                if (character.IsItemTakenBySomeoneElse(item)) { continue; }
                if (item.ParentInventory is ItemInventory itemInventory)
                {
                    if (!itemInventory.Container.HasRequiredItems(character, addMessage: false)) { continue; }
                }
                float itemPriority = 1;
                if (GetItemPriority != null)
                {
                    itemPriority = GetItemPriority(item);
                }
                Entity rootInventoryOwner = item.GetRootInventoryOwner();
                if (rootInventoryOwner is Item ownerItem)
                {
                    if (!ownerItem.IsInteractable(character)) { continue; }
                    if (!(ownerItem.GetComponent<ItemContainer>()?.HasRequiredItems(character, addMessage: false) ?? true)) { continue; }
                }
                Vector2 itemPos = (rootInventoryOwner ?? item).WorldPosition;
                float yDist = Math.Abs(character.WorldPosition.Y - itemPos.Y);
                yDist = yDist > 100 ? yDist * 5 : 0;
                float dist = Math.Abs(character.WorldPosition.X - itemPos.X) + yDist;
                float distanceFactor = MathHelper.Lerp(1, 0, MathUtils.InverseLerp(0, 10000, dist));
                itemPriority *= distanceFactor;
                itemPriority *= item.Condition / item.MaxCondition;
                // Ignore if the item has a lower priority than the currently selected one
                if (itemPriority < currItemPriority) { continue; }
                if (!hasCalledPathFinder && PathSteering != null && checkPath)
                {
                    // While following the player, let's ensure that there's a valid path to the target before accepting it.
                    // Otherwise it will take some time for us to find a valid item when there are multiple items that we can't reach and some that we can.
                    // This is relatively expensive, so let's do this only when it significantly improves the behavior.
                    // Only allow one path find call per frame.
                    hasCalledPathFinder = true;
                    var path = PathSteering.PathFinder.FindPath(character.SimPosition, item.SimPosition, errorMsgStr: $"AIObjectiveGetItem {character.DisplayName}", nodeFilter: node => node.Waypoint.CurrentHull != null);
                    if (path.Unreachable) { continue; }
                }
                currItemPriority = itemPriority;
                targetItem = item;
                moveToTarget = rootInventoryOwner ?? item;
            }
            if (currSearchIndex >= Item.ItemList.Count - 1)
            {
                isDoneSeeking = true;
                if (targetItem == null)
                {
                    if (spawnItemIfNotFound)
                    {
                        if (!(MapEntityPrefab.List.FirstOrDefault(me => me is ItemPrefab ip && identifiersOrTags.Any(id => id == ip.Identifier || ip.Tags.Contains(id))) is ItemPrefab prefab))
                        {
#if DEBUG
                            DebugConsole.NewMessage($"{character.Name}: Cannot find an item with the following identifier(s) or tag(s): {string.Join(", ", identifiersOrTags)}, tried to spawn the item but no matching item prefabs were found.", Color.Yellow);
#endif
                            Abandon = true;
                        }
                        else
                        {
                            Entity.Spawner.AddToSpawnQueue(prefab, character.Inventory, onSpawned: (Item spawnedItem) => 
                            {
                                targetItem = spawnedItem; 
                                if (character.TeamID == CharacterTeamType.FriendlyNPC && (character.Submarine?.Info.IsOutpost ?? false))
                                {
                                    spawnedItem.SpawnedInOutpost = true;
                                }
                            });
                        }
                    }
                    else
                    {
#if DEBUG
                        DebugConsole.NewMessage($"{character.Name}: Cannot find an item with the following identifier(s) or tag(s): {string.Join(", ", identifiersOrTags)}", Color.Yellow);
#endif
                        SpeakCannotFind();
                        Abandon = true;
                    }
                }
            }
        }

        protected override bool CheckObjectiveSpecific()
        {
            if (IsCompleted) { return true; }
            if (targetItem != null)
            {
                if (Equip && EquipSlotType.HasValue)
                {
                    return character.HasEquippedItem(targetItem, EquipSlotType.Value);
                }
                else
                {
                    return character.HasItem(targetItem, Equip);
                }
            }
            else if (identifiersOrTags != null)
            {
                var matchingItem = character.Inventory.FindItem(i => CheckItem(i), recursive: true);
                if (matchingItem != null)
                {
                    if (Equip && EquipSlotType.HasValue)
                    {
                        return character.HasEquippedItem(matchingItem, EquipSlotType.Value);
                    }
                    else
                    {
                        return !Equip || character.HasEquippedItem(matchingItem);
                    }
                }
                return false;
            }
            return false;
        }

        private bool CheckItem(Item item)
        {
            if (!item.IsInteractable(character)) { return false; }
            if (item.IsThisOrAnyContainerIgnoredByAI(character)) { return false; }
            if (ignoredItems.Contains(item)) { return false; };
            if (item.Condition < TargetCondition) { return false; }
            if (ItemFilter != null && !ItemFilter(item)) { return false; }
            return identifiersOrTags.Any(id => id == item.Prefab.Identifier || item.HasTag(id));
        }

        public override void Reset()
        {
            base.Reset();
            ResetInternal();
        }

        /// <summary>
        /// Does not reset the ignored items list
        /// </summary>
        private void ResetInternal()
        {
            RemoveSubObjective(ref goToObjective);
            targetItem = originalTarget;
            moveToTarget = targetItem?.GetRootInventoryOwner();
            isDoneSeeking = false;
            currSearchIndex = 0;
            currItemPriority = 0;
        }

        protected override void OnAbandon()
        {
            base.OnAbandon();
            if (moveToTarget == null) { return; }
#if DEBUG
            DebugConsole.NewMessage($"{character.Name}: Get item failed to reach {moveToTarget}", Color.Yellow);
#endif
        }

        private void SpeakCannotFind()
        {
            // TODO: Use the item name as the variable here.
            if (character.IsOnPlayerTeam && objectiveManager.CurrentOrder == objectiveManager.CurrentObjective)
            {
                string msg = TextManager.Get("dialogcannotfinditem", true);
                if (msg != null)
                {
                    character.Speak(msg, identifier: "dialogcannotfinditem", minDurationBetweenSimilar: 20.0f);
                }
            }
        }

        // TODO: remove?
        private void SpeakCannotReach()
        {
            if (character.IsOnPlayerTeam && objectiveManager.CurrentOrder == objectiveManager.CurrentObjective)
            {
                string TargetName = (moveToTarget as MapEntity)?.Name ?? (moveToTarget as Character)?.Name ?? moveToTarget.ToString();
                string msg = TargetName == null ? TextManager.Get("dialogcannotreachtarget", true) : TextManager.GetWithVariable("dialogcannotreachtarget", "[name]", TargetName, formatCapitals: !(moveToTarget is Character));
                if (msg != null)
                {
                    character.Speak(msg, identifier: "dialogcannotreachtarget", minDurationBetweenSimilar: 20.0f);
                }
            }
        }
    }
}
