using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

namespace Barotrauma
{
    class AIObjectiveGetItem : AIObjective
    {
        public override Identifier Identifier { get; set; } = "get item".ToIdentifier();

        public override bool AbandonWhenCannotCompleteSubObjectives => false;
        public override bool AllowMultipleInstances => true;
        protected override bool AllowWhileHandcuffed => false;

        public HashSet<Item> ignoredItems = new HashSet<Item>();

        public Func<Item, float> GetItemPriority;
        public Func<Item, bool> ItemFilter;
        public float TargetCondition { get; set; } = 1;
        public bool AllowDangerousPressure { get; set; }

        public readonly ImmutableHashSet<Identifier> IdentifiersOrTags;

        //if the item can't be found, spawn it in the character's inventory (used by outpost NPCs)
        private readonly bool spawnItemIfNotFound = false;

        private Item targetItem;
        private readonly Item originalTarget;
        private ISpatialEntity moveToTarget;
        private bool isDoneSeeking;
        public Item TargetItem => targetItem;
        private int currentSearchIndex;
        public ImmutableHashSet<Identifier> ignoredContainerIdentifiers;
        public ImmutableHashSet<Identifier> ignoredIdentifiersOrTags;
        private AIObjectiveGoTo goToObjective;
        private float currItemPriority;
        private readonly bool checkInventory;

        public const float DefaultReach = 100;
        public const float MaxReach = 150;

        public bool AllowToFindDivingGear { get; set; } = true;
        public bool MustBeSpecificItem { get; set; }

        /// <summary>
        /// Is the character allowed to take the item from somewhere else than their own sub (e.g. an outpost)
        /// </summary>
        public bool AllowStealing { get; set; }
        public bool TakeWholeStack { get; set; }
        /// <summary>
        /// Are variants of the specified item allowed
        /// </summary>
        public bool AllowVariants { get; set; }
        public bool Equip { get; set; }
        public bool Wear { get; set; }
        public bool RequireNonEmpty { get; set; }
        public bool EvaluateCombatPriority { get; set; }
        public bool CheckPathForEachItem { get; set; }
        public bool SpeakIfFails { get; set; }
        public string CannotFindDialogueIdentifierOverride { get; set; }
        public Func<bool> CannotFindDialogueCondition { get; set; }

        private int _itemCount = 1;
        public int ItemCount
        {
            get { return _itemCount; }
            set
            {
                _itemCount = Math.Max(value, 1);
            }
        }

        public InvSlotType? EquipSlotType { get; set; }

        public AIObjectiveGetItem(Character character, Item targetItem, AIObjectiveManager objectiveManager, bool equip = true, float priorityModifier = 1) 
            : base(character, objectiveManager, priorityModifier)
        {
            currentSearchIndex = 0;
            Equip = equip;
            originalTarget = targetItem;
            this.targetItem = targetItem;
            moveToTarget = targetItem?.GetRootInventoryOwner();
        }

        public AIObjectiveGetItem(Character character, Identifier identifierOrTag, AIObjectiveManager objectiveManager, bool equip = true, bool checkInventory = true, float priorityModifier = 1, bool spawnItemIfNotFound = false) 
            : this(character, new Identifier[] { identifierOrTag }, objectiveManager, equip, checkInventory, priorityModifier, spawnItemIfNotFound) { }

        public AIObjectiveGetItem(Character character, IEnumerable<Identifier> identifiersOrTags, AIObjectiveManager objectiveManager, bool equip = true, bool checkInventory = true, float priorityModifier = 1, bool spawnItemIfNotFound = false) 
            : base(character, objectiveManager, priorityModifier)
        {
            currentSearchIndex = 0;
            Equip = equip;
            this.spawnItemIfNotFound = spawnItemIfNotFound;
            this.checkInventory = checkInventory;
            IdentifiersOrTags = ParseGearTags(identifiersOrTags).ToImmutableHashSet();
            ignoredIdentifiersOrTags = ParseIgnoredTags(identifiersOrTags).ToImmutableHashSet();
        }

        public static IEnumerable<Identifier> ParseGearTags(IEnumerable<Identifier> identifiersOrTags)
        {
            var tags = new List<Identifier>();
            foreach (Identifier tag in identifiersOrTags)
            {
                if (!tag.Contains("!"))
                {
                    tags.Add(tag);
                }
            }
            return tags;
        }

        public static IEnumerable<Identifier> ParseIgnoredTags(IEnumerable<Identifier> identifiersOrTags)
        {
            var ignoredTags = new List<Identifier>();
            foreach (Identifier tag in identifiersOrTags)
            {
                if (tag.Contains("!"))
                {
                    ignoredTags.Add(tag.Remove("!"));
                }
            }
            return ignoredTags;
        }

        public static Func<PathNode, bool> CreateEndNodeFilter(ISpatialEntity targetEntity)
        {
            return n => (n.Waypoint.Ladders == null || n.Waypoint.IsInWater) && Vector2.DistanceSquared(n.Waypoint.WorldPosition, targetEntity.WorldPosition) <= MathUtils.Pow2(MaxReach);
        }

        private bool CheckInventory()
        {
            if (IdentifiersOrTags == null) { return false; }
            var item = character.Inventory.FindItem(i => CheckItem(i), recursive: true);
            if (item != null)
            {
                targetItem = item;
                moveToTarget = item.GetRootInventoryOwner();
            }
            return item != null;
        }

        private bool CountItems()
        {
            int itemCount = 0;
            foreach (Item it in character.Inventory.AllItems)
            {
                if (CheckItem(it))
                {
                    itemCount++;
                }
            }
            return itemCount >= ItemCount;
        }

        protected override void Act(float deltaTime)
        {
            if (IdentifiersOrTags != null)
            {
                if (checkInventory)
                {
                    if (CheckInventory())
                    {
                        isDoneSeeking = true;
                        itemCandidates.Clear();
                    }
                }
                if (!isDoneSeeking)
                {
                    if (character.Submarine == null)
                    {
                        Abandon = true;
                        return;
                    }
                    if (!AllowDangerousPressure)
                    {
                        bool dangerousPressure = !character.IsProtectedFromPressure && (character.CurrentHull == null || character.CurrentHull.LethalPressure > 0);
                        if (dangerousPressure)
                        {
#if DEBUG
                            string itemName = targetItem != null ? targetItem.Name : IdentifiersOrTags.FirstOrDefault().Value;
                            DebugConsole.NewMessage($"{character.Name}: Seeking item ({itemName}) aborted, because the pressure is dangerous.", Color.Yellow);
#endif
                            Abandon = true;
                            return;
                        }
                    }
                    FindTargetItem();
                }
                if (targetItem == null)
                {
                    if (isDoneSeeking)
                    {
                        HandlePotentialItems();
                    }
                    if (objectiveManager.CurrentOrder is not AIObjectiveGoTo)
                    {
                        objectiveManager.GetObjective<AIObjectiveIdle>().Wander(deltaTime);
                    }
                    return;
                }
            }
            else if (character.Submarine == null)
            {
                Abandon = true;
                return;
            }
            bool ShouldAbort() => IdentifiersOrTags is null || isDoneSeeking && itemCandidates.None();
            if (targetItem is null or { Removed: true })
            {
                if (ShouldAbort())
                {
#if DEBUG
                    DebugConsole.NewMessage($"{character.Name}: Target null or removed. Aborting.", Color.Red);
#endif
                    Abandon = true;
                }
                return;
            }
            if (moveToTarget is null)
            {
                if (ShouldAbort())
                {
#if DEBUG
                    DebugConsole.NewMessage($"{character.Name}: Move target null. Aborting.", Color.Red);
#endif
                    Abandon = true;
                    return;
                }
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
                    canInteract = character.CanInteractWith(c);
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
                var droppedStack = TargetItem.DroppedStack.ToList();
                if (HumanAIController.TakeItem(targetItem, character.Inventory, Equip, Wear, storeUnequipped: true, targetTags: IdentifiersOrTags))
                {
                    if (TakeWholeStack)
                    {
                        //taking the whole stack in this context means "as many items that can fit in one of the bot's slots", 
                        //and the stack means either a stack of items in an inventory slot or a "dropped stack"
                        //so we need a bit of extra logic here
                        int maxStackSize = 0;
                        int takenItemCount = 1;
                        for (int i = 0; i < character.Inventory.Capacity; i++)
                        {
                            maxStackSize = Math.Max(maxStackSize, character.Inventory.HowManyCanBePut(targetItem.Prefab, i, condition: null));
                        }
                        if (slots != null)
                        {
                            foreach (int slot in slots)
                            {
                                foreach (Item item in itemInventory.GetItemsAt(slot).ToList())
                                {
                                    if (HumanAIController.TakeItem(item, character.Inventory, equip: false, storeUnequipped: true))
                                    {
                                        takenItemCount++;
                                        if (takenItemCount >= maxStackSize) { break; }
                                    }
                                    else
                                    {
                                        break;
                                    }
                                }
                            }
                        }
                        foreach (var item in droppedStack)
                        {
                            if (item == TargetItem) { continue; }
                            if (HumanAIController.TakeItem(item, character.Inventory, equip: false, storeUnequipped: true))
                            {
                                takenItemCount++;
                                if (takenItemCount >= maxStackSize) { break; }
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                    if (IdentifiersOrTags == null)
                    {
                        IsCompleted = true;
                    }
                    else
                    {
                        IsCompleted = CountItems();
                        if (!IsCompleted)
                        {
                            ResetInternal();
                        }
                    }
                }
                else
                {
                    if (!Equip)
                    {
                        // Try equipping and wearing the item
                        Equip = true;
                        if (!objectiveManager.HasActiveObjective<AIObjectiveCleanupItem>() && !objectiveManager.HasActiveObjective<AIObjectiveLoadItem>())
                        {
                            Wear = true;
                        }
                        return;
                    }
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
                            AbortCondition = obj => targetItem == null || (targetItem.GetRootInventoryOwner() is Entity owner && owner != moveToTarget && owner != character),
                            SpeakIfFails = false,
                            endNodeFilter = CreateEndNodeFilter(moveToTarget)
                        };
                    },
                    onAbandon: () =>
                    {
                        if (originalTarget == null)
                        {
                            // Try again
                            ignoredItems.Add(targetItem);
                            if (targetItem != moveToTarget && moveToTarget is Item item)
                            {
                                ignoredItems.Add(item);
                            }
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

        private Stopwatch sw;
        private Stopwatch StopWatch => sw ??= new Stopwatch();
        private readonly List<(Item item, float priority)> itemCandidates = new List<(Item, float)>();
        private List<Item> itemList;
        private void FindTargetItem()
        {
            if (IdentifiersOrTags == null)
            {
                if (targetItem == null)
                {
#if DEBUG
                    DebugConsole.AddWarning($"{character.Name}: Cannot find an item, because neither identifiers nor item was defined.");
#endif
                    Abandon = true;
                }
                return;
            }
            if (HumanAIController.DebugAI)
            {
                StopWatch.Restart();
            }
            float priority = objectiveManager.GetCurrentPriority();
            bool checkPath = CheckPathForEachItem || priority >= AIObjectiveManager.RunPriority || ItemCount > 1;
            // Reset if the character has switched subs.
            if (itemList != null && !character.Submarine.IsEntityFoundOnThisSub(itemList.FirstOrDefault(), includingConnectedSubs: true))
            {
                currentSearchIndex = 0;
            }
            if (currentSearchIndex == 0)
            {
                itemCandidates.Clear();
                itemList = character.Submarine.GetItems(alsoFromConnectedSubs: true);
            }
            int itemsPerFrame = (int)MathHelper.Lerp(30, 300, MathUtils.InverseLerp(10, 100, priority));
            int checkedItems = 0;
            for (int i = 0; i < itemsPerFrame && currentSearchIndex < itemList.Count; i++, currentSearchIndex++)
            {
                checkedItems++;
                var item = itemList[currentSearchIndex];
                Submarine itemSub = item.Submarine ?? item.ParentInventory?.Owner?.Submarine;
                if (itemSub == null) { continue; }
                Submarine mySub = character.Submarine;
                if (mySub == null) { continue; }
                if (!checkInventory)
                {
                    // Ignore items in the inventory when defined not to check it.
                    if (item.IsOwnedBy(character)) { continue; }
                }
                if (!AllowStealing && character.IsOnPlayerTeam)
                {
                    if (item.Illegitimate) { continue; }
                }
                if (!CheckItem(item)) { continue; }
                if (item.Container != null)
                {
                    if (item.Container.HasTag(Tags.DontTakeItems)) { continue; }
                    if (ignoredItems.Contains(item.Container)) { continue; }
                    if (ignoredContainerIdentifiers != null)
                    {
                        if (ignoredContainerIdentifiers.Contains(item.ContainerIdentifier)) { continue; }
                    }
                }
                if (character.IsItemTakenBySomeoneElse(item)) { continue; }
                if (item.ParentInventory is ItemInventory itemInventory)
                {
                    if (!itemInventory.Container.HasRequiredItems(character, addMessage: false)) { continue; }
                }
                float itemPriority = item.Prefab.BotPriority;
                if (GetItemPriority != null)
                {
                    itemPriority *= GetItemPriority(item);
                }
                if (itemPriority <= 0) { continue; }
                Entity rootInventoryOwner = item.GetRootInventoryOwner();
                if (rootInventoryOwner is Item ownerItem)
                {
                    if (!ownerItem.IsInteractable(character)) { continue; }
                    if (ownerItem != item)
                    {
                        if (!(ownerItem.GetComponent<ItemContainer>()?.HasRequiredItems(character, addMessage: false) ?? true)) { continue; }
                        //the item is inside an item inside an item (e.g. fuel tank in a welding tool in a cabinet -> reduce priority to prefer items that aren't inside a tool)
                        if (ownerItem != item.Container)
                        {
                            itemPriority *= 0.1f;
                        }
                    }
                }
                Vector2 itemPos = (rootInventoryOwner ?? item).WorldPosition;
                float distanceFactor =
                    GetDistanceFactor(
                        itemPos,
                        verticalDistanceMultiplier: 5,
                        maxDistance: 10000,
                        factorAtMinDistance: 1.0f,
                        factorAtMaxDistance: EvaluateCombatPriority ? 0.1f : 0);
                itemPriority *= distanceFactor;
                if (EvaluateCombatPriority)
                {
                    var mw = item.GetComponent<MeleeWeapon>();
                    var rw = item.GetComponent<RangedWeapon>();
                    float combatFactor = 0;
                    if (mw != null)
                    {
                        if (mw.CombatPriority > 0)
                        {
                            combatFactor = mw.CombatPriority / 100;
                        }
                        else
                        {
                            // The combat factor of items with zero combat priority is not allowed to be greater than 0.1f
                            combatFactor = Math.Min(AIObjectiveCombat.GetLethalDamage(mw) / 1000, 0.1f);
                        }
                    }
                    else if (rw != null)
                    {
                        if (rw.CombatPriority > 0)
                        {
                            combatFactor = rw.CombatPriority / 100;
                        }
                        else
                        {
                            combatFactor = Math.Min(AIObjectiveCombat.GetLethalDamage(rw) / 1000, 0.1f);
                        }
                    }
                    else
                    {
                        combatFactor = Math.Min(item.Components.Sum(AIObjectiveCombat.GetLethalDamage) / 1000, 0.1f);
                    }
                    itemPriority *= combatFactor;
                }
                else
                {
                    itemPriority *= item.Condition / item.MaxCondition;
                }
                // Ignore if the item has a lower priority than the currently selected one
                if (itemPriority < currItemPriority) { continue; }
                if (EvaluateCombatPriority && itemPriority <= 0)
                {
                    // Not good enough
                    continue;
                }
                if (checkPath)
                {
                    itemCandidates.Add((item, itemPriority));
                }
                else
                {
                    currItemPriority = itemPriority;
                    targetItem = item;
                    moveToTarget = rootInventoryOwner ?? item;
                }
            }
            if (currentSearchIndex >= itemList.Count - 1)
            {
                isDoneSeeking = true;
                if (itemCandidates.Any())
                {
                    itemCandidates.Sort((x, y) => y.priority.CompareTo(x.priority));
                }
                if (HumanAIController.DebugAI && StopWatch.ElapsedMilliseconds > 2)
                { 
                    string msg = $"Went through {checkedItems} of total {itemList.Count} items. Found item {targetItem?.Name ?? "NULL"} in {StopWatch.ElapsedMilliseconds} ms. Completed: {isDoneSeeking}";   
                    if (StopWatch.ElapsedMilliseconds > 5)
                    {
                        DebugConsole.ThrowError(msg);
                    }
                    else
                    {
                        // An occasional warning now and then can be ignored, but multiple at the same time might indicate a performance issue.
                        DebugConsole.AddWarning(msg);
                    }
                }
            }
        }
        
        private void HandlePotentialItems()
        {
            Debug.Assert(isDoneSeeking);
            if (itemCandidates.Any())
            {
                if (PathSteering == null)
                {
                    itemCandidates.Clear();
                    Abandon = true;
                    return;
                }
                if (itemCandidates.FirstOrDefault() is var itemCandidate)
                {
                    var path = PathSteering.PathFinder.FindPath(character.SimPosition, character.GetRelativeSimPosition(itemCandidate.item), character.Submarine, errorMsgStr: $"AIObjectiveGetItem {character.DisplayName}", nodeFilter: node => node.Waypoint.CurrentHull != null);
                    if (path.Unreachable)
                    {
                        // Remove the invalid candidates and continue on the next frame.
                        itemCandidates.Remove(itemCandidate);
                    }
                    else
                    {
                        // The path was valid -> we are done.
                        itemCandidates.Clear();
                        targetItem = itemCandidate.item;
                        moveToTarget = targetItem.GetRootInventoryOwner() ?? targetItem;
                    }
                }
            }
            if (targetItem == null)
            {
                if (spawnItemIfNotFound)
                {
                    ItemPrefab prefab = FindItemToSpawn();
                    if (prefab == null)
                    {
#if DEBUG
                        DebugConsole.NewMessage($"{character.Name}: Cannot find an item with the following identifier(s) or tag(s): {string.Join(", ", IdentifiersOrTags)}, tried to spawn the item but no matching item prefabs were found.", Color.Yellow);
#endif
                        Abandon = true;
                    }
                    else
                    {
                        Entity.Spawner.AddItemToSpawnQueue(prefab, character.Inventory, onSpawned: (Item spawnedItem) => 
                        {
                            targetItem = spawnedItem; 
                            if (character.TeamID == CharacterTeamType.FriendlyNPC && (character.Submarine?.Info.IsOutpost ?? false))
                            {
                                spawnedItem.SpawnedInCurrentOutpost = true;
                            }
                        });
                    }
                }
                else
                {
#if DEBUG
                    DebugConsole.NewMessage($"{character.Name}: Cannot find an item with the following identifier(s) or tag(s): {string.Join(", ", IdentifiersOrTags)}", Color.Yellow);
#endif
                    Abandon = true;
                }
            }
        }

        /// <summary>
        /// Returns the "best" item to spawn when using <see cref="spawnItemIfNotFound"/> and there's multiple suitable items.
        /// Best in this context is the one that's sold at the lowest price in stores (usually the most "basic" item)
        /// </summary>
        /// <returns></returns>
        private ItemPrefab FindItemToSpawn()
        {
            ItemPrefab bestItem = null;
            float lowestCost = float.MaxValue;
            foreach (MapEntityPrefab prefab in MapEntityPrefab.List)
            {
                if (prefab is not ItemPrefab itemPrefab) { continue; }
                if (IdentifiersOrTags.Any(id => id == prefab.Identifier || prefab.Tags.Contains(id)))
                {
                    float cost = itemPrefab.DefaultPrice != null && itemPrefab.CanBeBought ?
                        itemPrefab.DefaultPrice.Price :
                        float.MaxValue;
                    if (cost < lowestCost || bestItem == null)
                    {
                        bestItem = itemPrefab;
                        lowestCost = cost;
                    }
                }
            }
            return bestItem;
        }

        protected override bool CheckObjectiveSpecific()
        {
            if (IsCompleted) { return true; }
            if (targetItem == null)
            {
                // Not yet ready
                return false;
            }
            if (IdentifiersOrTags != null && ItemCount > 1)
            {
                return CountItems();
            }
            else
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
        }

        private bool CheckItem(Item item)
        {
            if (!item.HasAccess(character)) { return false; }
            if (ignoredItems.Contains(item)) { return false; };
            if (ignoredIdentifiersOrTags != null && item.HasIdentifierOrTags(ignoredIdentifiersOrTags)) { return false; }
            if (item.Condition < TargetCondition) { return false; }
            if (ItemFilter != null && !ItemFilter(item)) { return false; }
            if (RequireNonEmpty && item.Components.Any(i => i.IsEmpty(character))) { return false; }
            return item.HasIdentifierOrTags(IdentifiersOrTags) || (AllowVariants && !item.Prefab.VariantOf.IsEmpty && IdentifiersOrTags.Contains(item.Prefab.VariantOf));
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
            currentSearchIndex = 0;
            currItemPriority = 0;
        }

        protected override void OnAbandon()
        {
            base.OnAbandon();
            if (moveToTarget != null)
            {
#if DEBUG
                DebugConsole.NewMessage($"{character.Name}: Get item failed to reach {moveToTarget}", Color.Yellow);
#endif
            }
            SpeakCannotFind();
        }

        private void SpeakCannotFind()
        {
            if (!SpeakIfFails) { return; }
            if (!character.IsOnPlayerTeam) { return; }
            if (objectiveManager.CurrentOrder != objectiveManager.CurrentObjective) { return; }
            if (CannotFindDialogueCondition != null && !CannotFindDialogueCondition()) { return; }
            LocalizedString msg = TextManager.Get(CannotFindDialogueIdentifierOverride, "dialogcannotfinditem");
            if (msg.IsNullOrEmpty() || !msg.Loaded) { return; }
            character.Speak(msg.Value, identifier: "dialogcannotfinditem".ToIdentifier(), minDurationBetweenSimilar: 20.0f);
        }
    }
}
