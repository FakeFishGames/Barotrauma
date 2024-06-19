using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class SalvageMission : Mission
    {
        private class Target
        {
            public Item Item;
            /// <summary>
            /// The target this item spawns inside (usually a crate for example).
            /// </summary>
            public Target ParentTarget;

            /// <summary>
            /// Note that the integer values matter here:
            /// a larger or equal value than the <see href="RequiredRetrievalState">RequiredRetrievalState</see> means the item counts as retrieved
            /// (if the item needs to be picked up to be considered retrieved, it's also considered retrieved if it's in the sub)
            /// </summary>
            public enum RetrievalState
            {                
                None = 0,
                Interact = 1,
                PickedUp = 2,
                RetrievedToSub = 3
            }

            public readonly ItemPrefab ItemPrefab;
            /// <summary>
            /// Where the target can be spawned to. E.g. MainPath or Wreck.
            /// </summary>
            public readonly Level.PositionType SpawnPositionType;
            public readonly Identifier ContainerTag;
            public readonly Identifier ExistingItemTag;
            
            public readonly bool RemoveItem;

            public readonly LocalizedString SonarLabel;

            /// <summary>
            /// Can the mission continue before this target has been retrieved? Can be used if you want the targets to be retrieved in a specific order.
            /// </summary>
            public readonly bool AllowContinueBeforeRetrieved;

            /// <summary>
            /// Does the target need to be picked up or brought to the sub for mission to be considered successful. 
            /// If None, the target has no effect on the completion of the mission.
            /// </summary>
            public readonly RetrievalState RequiredRetrievalState;

            public readonly bool HideLabelAfterRetrieved;

            public bool Retrieved
            {
                get
                {
                    //if placing the item inside the parent (e.g. some item inside a crate) failed,
                    //consider this item retrieved (= essentially ignoring the item, it's not necessary to retrieve)
                    if (PlacingInsideParentTargetFailed)
                    {
                        return true;
                    }

                    return RequiredRetrievalState switch
                    {
                        RetrievalState.None => true,
                        RetrievalState.Interact or RetrievalState.PickedUp => State >= RequiredRetrievalState,
                        RetrievalState.RetrievedToSub => State == RetrievalState.RetrievedToSub,
                        _ => throw new NotImplementedException(),
                    };
                }
            }

            private RetrievalState state;
            public RetrievalState State
            {
                get { return state; }
                set
                {
                    if (value == state) { return; }
                    state = value;
#if SERVER
                    GameMain.Server?.UpdateMissionState(mission);
#endif
                }
            }

            public bool Interacted;

            private readonly SalvageMission mission;
            public readonly bool RequireInsideOriginalContainer;
            public Item OriginalContainer;

            /// <summary>
            /// Means that the item could not be placed inside the container it was intended to spawn inside (probably meaning the mission has been misconfigured to e.g. spawn more items inside a crate than what the crate can hold).
            /// </summary>
            public bool PlacingInsideParentTargetFailed;

            /// <summary>
            /// Status effects executed on the target item when the mission starts. A random effect is chosen from each child list.
            /// </summary>
            public readonly List<List<StatusEffect>> StatusEffects = new List<List<StatusEffect>>();

            public Target(ContentXElement element, SalvageMission mission, Target parentTarget)
            {
                this.mission = mission;
                ParentTarget = parentTarget;
                ContainerTag = element.GetAttributeIdentifier("containertag", Identifier.Empty);
                RequiredRetrievalState = element.GetAttributeEnum("requireretrieval", parentTarget?.RequiredRetrievalState ?? RetrievalState.RetrievedToSub);
                AllowContinueBeforeRetrieved = element.GetAttributeBool("allowcontinuebeforeretrieved", parentTarget != null);
                HideLabelAfterRetrieved = element.GetAttributeBool("hidelabelafterretrieved", parentTarget?.HideLabelAfterRetrieved ?? false);
                RequireInsideOriginalContainer = element.GetAttributeBool("requireinsideoriginalcontainer", false);
                                
                string sonarLabelTag = element.GetAttributeString("sonarlabel", "");
                if (!string.IsNullOrEmpty(sonarLabelTag))
                {
                    SonarLabel =
                        TextManager.Get($"MissionSonarLabel.{sonarLabelTag}")
                        .Fallback(TextManager.Get(sonarLabelTag))
                        .Fallback(element.GetAttributeString("sonarlabel", ""));
                }
                ExistingItemTag = element.GetAttributeIdentifier("existingitemtag", Identifier.Empty);

                RemoveItem = element.GetAttributeBool("removeitem", true);

                if (element.GetAttribute("itemname") != null)
                {
                    DebugConsole.ThrowError("Error in SalvageMission - use item identifier instead of the name of the item.",
                        contentPackage: element.ContentPackage);
                    string itemName = element.GetAttributeString("itemname", "");
                    ItemPrefab = MapEntityPrefab.Find(itemName) as ItemPrefab;
                    if (ItemPrefab == null && ExistingItemTag.IsEmpty)
                    {
                        DebugConsole.ThrowError($"Error in SalvageMission: couldn't find an item prefab with the name \"{itemName}\"",
                            contentPackage: element.ContentPackage);
                    }
                }
                else
                {
                    Identifier itemIdentifier = element.GetAttributeIdentifier("itemidentifier", Identifier.Empty);
                    if (!itemIdentifier.IsEmpty)
                    {
                        ItemPrefab = MapEntityPrefab.FindByIdentifier(itemIdentifier.ToIdentifier()) as ItemPrefab;
                    }
                    if (ItemPrefab == null)
                    {
                        string itemTag = element.GetAttributeString("itemtag", "");
                        //NOTE: using unsynced random here is fine, the clients receive the info of what item spawned from the server
                        ItemPrefab = MapEntityPrefab.GetRandom(p => p.Tags.Contains(itemTag), Rand.RandSync.Unsynced) as ItemPrefab;
                    }
                    if (ItemPrefab == null && ExistingItemTag.IsEmpty)
                    {
                        DebugConsole.ThrowError($"Error in SalvageMission - couldn't find an item prefab with the identifier \"{itemIdentifier}\"",
                            contentPackage: element.ContentPackage);
                    }
                }

                SpawnPositionType = element.GetAttributeEnum("spawntype", parentTarget?.SpawnPositionType ?? (Level.PositionType.Cave | Level.PositionType.Ruin));

                foreach (var subElement in element.Elements())
                {
                    switch (subElement.Name.ToString().ToLowerInvariant())
                    {
                        case "statuseffect":
                            {
                                var newEffect = StatusEffect.Load(subElement, parentDebugName: mission.Prefab.Name.Value);
                                if (newEffect == null) { continue; }
                                StatusEffects.Add(new List<StatusEffect> { newEffect });
                                break;
                            }
                        case "chooserandom":
                            if (subElement.Elements().Any(static e => e.NameAsIdentifier() == "statuseffect"))
                            {
                                StatusEffects.Add(new List<StatusEffect>());
                                foreach (var effectElement in subElement.Elements())
                                {
                                    var newEffect = StatusEffect.Load(effectElement, parentDebugName: mission.Prefab.Name.Value);
                                    if (newEffect == null) { continue; }
                                    StatusEffects.Last().Add(newEffect);
                                }
                            }
                            break;
                    }
                }
            }

            public void Reset()
            {
                state = RetrievalState.None;
                Item = null;
            }
        }

        private readonly List<Target> targets = new List<Target>();

        /// <summary>
        /// What percentage of targets need to be retrieved for the mission to complete (0.0 - 1.0). Defaults to 0.98.
        /// </summary>
        private readonly float requiredDeliveryAmount;

        /// <summary>
        /// Message displayed when at least one of the targets is retrieved, but the mission is not complete yet.
        /// </summary>
        private LocalizedString partiallyRetrievedMessage;

        /// <summary>
        /// Message displayed when all targets have been retrieved.
        /// </summary>
        private LocalizedString allRetrievedMessage;

        public bool AnyTargetNeedsToBeRetrievedToSub => targets.Any(static t => t.RequiredRetrievalState == Target.RetrievalState.RetrievedToSub && !t.Retrieved);

        private readonly MTRandom rng;

        public override IEnumerable<(LocalizedString Label, Vector2 Position)> SonarLabels
        {
            get
            {
                foreach (var target in targets)
                {
                    if (target.Retrieved && target.HideLabelAfterRetrieved) { continue; }
                    if (target.Item != null && !target.Item.Removed)
                    {
                        if (target.Item.ParentInventory?.Owner is Item parentItem)
                        {
                            bool insideParentItem = false;
                            foreach (var parentTarget in targets)
                            {
                                if (parentTarget.Item == parentItem && !parentTarget.SonarLabel.IsNullOrEmpty())
                                {
                                    insideParentItem = true;
                                    break;
                                }
                            }
                            //if the item is inside another target that has it's own sonar label, no need to show one on this item
                            if (insideParentItem) { continue; }
                        }

                        yield return (
                            target.SonarLabel ?? Prefab.SonarLabel, 
                            target.Item.GetRootInventoryOwner()?.WorldPosition ?? target.Item.WorldPosition);
                    }
                    if (!target.AllowContinueBeforeRetrieved && !target.Retrieved) { break; }
                }
            }
        }

        public SalvageMission(MissionPrefab prefab, Location[] locations, Submarine sub)
            : base(prefab, locations, sub)
        {
            requiredDeliveryAmount = prefab.ConfigElement.GetAttributeFloat(nameof(requiredDeliveryAmount), 0.98f);

            //LevelData may not be instantiated at this point, in that case use the name identifier of the location
            rng = new MTRandom(ToolBox.StringToInt(
                    locations[0].LevelData?.Seed ?? locations[0].NameIdentifier.Value +
                    locations[1].LevelData?.Seed ?? locations[1].NameIdentifier.Value));

            partiallyRetrievedMessage = GetMessage(nameof(partiallyRetrievedMessage));
            allRetrievedMessage = GetMessage(nameof(allRetrievedMessage));

            foreach (ContentXElement subElement in prefab.ConfigElement.Elements())
            {
                if (subElement.NameAsIdentifier() == "target" ||
                    subElement.NameAsIdentifier() == "chooserandom")
                {
                    LoadTarget(subElement, parentTarget: null);
                }
             
            }
            if (!targets.Any())
            {
                targets.Add(new Target(prefab.ConfigElement, this, parentTarget: null));
            }

            LocalizedString GetMessage(string attributeName)
            {
                if (prefab.ConfigElement.GetAttribute(attributeName) != null)
                {
                    string msgTag = prefab.ConfigElement.GetAttributeString(attributeName, string.Empty);
                    return ReplaceVariablesInMissionMessage(TextManager.Get(msgTag).Fallback(msgTag), sub);
                }
                return string.Empty;
            }
        }
        
        private void LoadTarget(ContentXElement element, Target parentTarget)
        {
            ContentXElement chosenElement = element;
            if (element.NameAsIdentifier() == "chooserandom")
            {
                /* chooserandom in this context can be used to choose either between targets or status effects to apply to the target, 
                         ensure we don't try to load a statuseffect as a "child target" */
                if (element.Elements().Any(static e => e.NameAsIdentifier() == "statuseffect"))
                {
                    return;
                }
                //this needs to be deterministic, use RNG with a specific seed
                chosenElement = element.Elements().ToList().GetRandom(rng);
            }

            int amount = GetAmount(chosenElement);
            for (int i = 0; i < amount; i++)
            {
                var target = new Target(chosenElement, this, parentTarget);
                targets.Add(target);
                foreach (ContentXElement subElement in chosenElement.Elements())
                {
                    LoadTarget(subElement, parentTarget: target);                    
                }           
            } 
        }

        private int GetAmount(ContentXElement targetElement)
        {
            int amount = targetElement.GetAttributeInt("amount", 1);
            int minAmount = targetElement.GetAttributeInt("minamount", amount);
            int maxAmount = targetElement.GetAttributeInt("maxamount", amount);
            
            // if the amount is a range, pick a random value between minAmount and maxAmount
            if (minAmount < maxAmount)
            {
                //this needs to be deterministic, use RNG with a specific seed
                amount = rng.Next(minAmount, maxAmount + 1);
            }
            
            return amount;
        }

        protected override void StartMissionSpecific(Level level)
        {
#if SERVER
            spawnInfo.Clear();
#endif
            foreach (var target in targets)
            {
                bool usedExistingItem = false;
                UInt16 originalInventoryID = 0;
                byte originalItemContainerIndex = 0;
                int originalSlotIndex = 0;
                var executedEffectIndices = new List<(int listIndex, int effectIndex)>();

                target.Reset();
                if (!IsClient)
                {
                    //ruin/cave/wreck items are allowed to spawn close to the sub
                    float minDistance = target.SpawnPositionType switch
                    {
                        Level.PositionType.Ruin or
                        Level.PositionType.Cave or
                        Level.PositionType.Wreck or
                        Level.PositionType.Outpost => 0.0f,
                        _ => Level.Loaded.Size.X * 0.3f,
                    };
                    Vector2 position = 
                        target.SpawnPositionType == Level.PositionType.None ? 
                        Vector2.Zero : 
                        Level.Loaded.GetRandomItemPos(target.SpawnPositionType, 100.0f, minDistance, 30.0f);

                    if (!target.ExistingItemTag.IsEmpty)
                    {
                        var suitableItems = Item.ItemList.Where(it => it.HasTag(target.ExistingItemTag));
                        if (GameMain.GameSession?.Missions != null)
                        {
                            //don't choose an item that was already chosen as the target for another salvage mission
                            suitableItems = suitableItems.Where(it =>
                                GameMain.GameSession.Missions.None(m => m != this && m is SalvageMission salvageMission && salvageMission.targets.Any(t => t.Item == it)));
                        }
                        switch (target.SpawnPositionType)
                        {
                            case Level.PositionType.Cave:
                            case Level.PositionType.MainPath:
                            case Level.PositionType.SidePath:
                                target.Item = suitableItems.FirstOrDefault(it => Vector2.DistanceSquared(it.WorldPosition, position) < 1000.0f);
#if SERVER
                                usedExistingItem = target.Item != null;
#endif
                                break;
                            case Level.PositionType.Ruin:
                            case Level.PositionType.Wreck:
                            case Level.PositionType.Outpost:
                                foreach (Item it in suitableItems)
                                {
                                    if (it.Submarine?.Info == null) { continue; }
                                    if (target.SpawnPositionType == Level.PositionType.Ruin && it.Submarine.Info.Type != SubmarineType.Ruin) { continue; }
                                    if (target.SpawnPositionType == Level.PositionType.Wreck && it.Submarine.Info.Type != SubmarineType.Wreck) { continue; }
                                    if (target.SpawnPositionType == Level.PositionType.Outpost && it.Submarine.Info.Type != SubmarineType.Outpost) { continue; }
                                    Rectangle worldBorders = it.Submarine.Borders;
                                    worldBorders.Location += it.Submarine.WorldPosition.ToPoint();
                                    if (Submarine.RectContains(worldBorders, it.WorldPosition))
                                    {
                                        target.Item = it;
#if SERVER
                                        usedExistingItem = true;
#endif
                                        break;
                                    }
                                }
                                break;
                            default:
                                target.Item = suitableItems.FirstOrDefault();
#if SERVER
                                usedExistingItem = target.Item != null;
#endif
                                break;
                        }
                    }

                    if (target.Item == null)
                    {
                        if (target.ItemPrefab == null && target.ContainerTag.IsEmpty)
                        {
                            DebugConsole.ThrowError($"Failed to find a target item for the mission \"{Prefab.Identifier}\". Item tag: {target.ExistingItemTag}",
                                contentPackage: Prefab.ContentPackage);
                            continue;
                        }
                        target.Item = new Item(target.ItemPrefab, position, null);
#if CLIENT
                        target.Item.HighlightColor = GUIStyle.Orange;
                        target.Item.ExternalHighlight = true;
#endif
                        target.Item.UpdateTransform();
                        if (target.Item.CurrentHull == null)
                        {
                            //prevent the body from moving if it spawned outside the hulls (we don't want it e.g. falling to the bottom of a cave or into the abyss)
                            target.Item.body.FarseerBody.BodyType = BodyType.Kinematic;
                        }
                    }
                    else if (target.RequiredRetrievalState == Target.RetrievalState.Interact)
                    {
                        target.Item.OnInteract += () =>
                        {
                            target.Interacted = true;
                        };
                    }
                    for (int i = 0; i < target.StatusEffects.Count; i++)
                    {
                        List<StatusEffect> effectList = target.StatusEffects[i];
                        if (effectList.Count == 0) { continue; }
                        int effectIndex = Rand.Int(effectList.Count);
                        var selectedEffect = effectList[effectIndex];
                        target.Item.ApplyStatusEffect(selectedEffect, selectedEffect.type, deltaTime: 1.0f, worldPosition: target.Item.Position);
#if SERVER
                        executedEffectIndices.Add((i, effectIndex));
#endif
                    }

                    target.Item.IsSalvageMissionItem = true;

                    //try to find a container and place the item inside it
                    if (!target.ContainerTag.IsEmpty && target.Item.ParentInventory == null)
                    {
                        List<ItemContainer> validContainers = new List<ItemContainer>();
                        foreach (Item it in Item.ItemList)
                        {
                            if (!it.HasTag(target.ContainerTag)) { continue; }
                            if (!it.IsPlayerTeamInteractable) { continue; }
                            switch (target.SpawnPositionType)
                            {
                                case Level.PositionType.Cave:
                                case Level.PositionType.MainPath:
                                    if (it.Submarine != null) { continue; }
                                    break;
                                case Level.PositionType.Ruin:
                                    if (it.Submarine?.Info == null || !it.Submarine.Info.IsRuin) { continue; }
                                    break;
                                case Level.PositionType.Wreck:
                                    if (it.Submarine?.Info == null || it.Submarine.Info.Type != SubmarineType.Wreck) { continue; }
                                    break;
                            }
                            var itemContainer = it.GetComponent<ItemContainer>();
                            if (itemContainer != null && itemContainer.Inventory.CanBePut(target.Item)) { validContainers.Add(itemContainer); }
                        }
                        if (validContainers.Any())
                        {
                            //NOTE: using unsynced random here is fine, clients don't run this logic but rely on where the server places the item
                            var selectedContainer = validContainers.GetRandomUnsynced();
                            if (selectedContainer.Combine(target.Item, user: null))
                            {
#if SERVER
                                originalInventoryID = selectedContainer.Item.ID;
                                originalItemContainerIndex = (byte)selectedContainer.Item.GetComponentIndex(selectedContainer);
                                originalSlotIndex = target.Item.ParentInventory?.FindIndex(target.Item) ?? -1;
#endif
                            } // Placement successful
                        }
                    }
                }
#if SERVER
                spawnInfo.Add(
                    target, 
                    new SpawnInfo(usedExistingItem, originalInventoryID, originalItemContainerIndex, originalSlotIndex, executedEffectIndices));
#endif
            }

            if (!IsClient)
            {
                // after spawning all the items from prefabs, need to find all targets where parentTarget is defined, and set the item inside parent target container (if applicable)
                foreach (var target in targets)
                {
                    if (target.ParentTarget == null) { continue; }
                
                    if (target.Item == null)
                    {
                        DebugConsole.ThrowError("Error in salvage mission " + Prefab.Identifier + " (item was null)",
                            contentPackage: Prefab.ContentPackage);
                        continue;
                    }
                
                    if (target.ParentTarget.Item == null)
                    {
                        DebugConsole.ThrowError("Error in salvage mission " + Prefab.Identifier + " (parent item was null)",
                            contentPackage: Prefab.ContentPackage);
                        continue;
                    }

                    target.Item.DontCleanUp = true;

                    if (target.ParentTarget.Item.GetComponent<ItemContainer>() is ItemContainer container)
                    {
                        if (!container.Inventory.TryPutItem(target.Item, user: null))
                        {
                            DebugConsole.ThrowError($"Error in salvage mission {Prefab.Identifier}: failed to put the item {target.Item.Name} inside {target.ParentTarget.Item.Name}.", 
                                contentPackage: Prefab.ContentPackage);
                            target.PlacingInsideParentTargetFailed = true;
                        }
                        target.OriginalContainer = target.ParentTarget.Item;
                    }
                }
            }
        }

        protected override void UpdateMissionSpecific(float deltaTime)
        {
            //make body dynamic when picked up
            foreach (var target in targets)
            {
                var root = target.Item?.RootContainer ?? target.Item;
                if (root == null) { continue; }
                if (target.Item.ParentInventory != null && target.Item.body != null) { target.Item.body.FarseerBody.BodyType = BodyType.Dynamic; }
            }

            if (IsClient) { return; }

            bool atLeastOneTargetWasRetrieved = false;
            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (i > 0 && !targets[i - 1].AllowContinueBeforeRetrieved && !targets[i - 1].Retrieved) { break; }
                if (target.Item == null)
                {
#if DEBUG
                    DebugConsole.ThrowError("Error in salvage mission " + Prefab.Identifier + " (item was null)",
                        contentPackage: Prefab.ContentPackage);
#endif
                    return;
                }

                Entity rootInventoryOwner = target.Item.GetRootInventoryOwner();
                Submarine parentSub = target.Item.CurrentHull?.Submarine ?? rootInventoryOwner?.Submarine;
                bool inPlayerSub = parentSub != null && parentSub.Info.Type == SubmarineType.Player;
                switch (target.State)
                {
                    case Target.RetrievalState.None:
                        {
                            if (target.Interacted)
                            {
                                TrySetRetrievalState(Target.RetrievalState.Interact);
                            }
                            var root = target.Item?.RootContainer ?? target.Item;
                            if (root.ParentInventory?.Owner is Character character && character.TeamID == CharacterTeamType.Team1)
                            {
                                TrySetRetrievalState(Target.RetrievalState.PickedUp);
                            }
                            if (inPlayerSub)
                            {
                                TrySetRetrievalState(Target.RetrievalState.RetrievedToSub);
                            }
                        }
                        break;
                    case Target.RetrievalState.PickedUp:
                    case Target.RetrievalState.RetrievedToSub:
                        {

                            bool inPlayerInventory = false;
                            bool playerInFriendlySub = false;
                            if (rootInventoryOwner is Character character && character.TeamID == CharacterTeamType.Team1)
                            {
                                inPlayerInventory = true;
                                if (character.Submarine != null)
                                {
                                    playerInFriendlySub =
                                        character.IsInFriendlySub ||
                                        (character.Submarine == Level.Loaded?.StartOutpost && Level.IsLoadedFriendlyOutpost && GameMain.GameSession?.Campaign.CurrentLocation is not { IsFactionHostile: true });
                                }
                            }

                            if (inPlayerSub || (inPlayerInventory && playerInFriendlySub))
                            {
                                TrySetRetrievalState(Target.RetrievalState.RetrievedToSub);                            
                            }
                            else
                            {
                                target.State = Target.RetrievalState.PickedUp;
                            }
                        }
                        break;
                }

                void TrySetRetrievalState(Target.RetrievalState retrievalState)
                {
                    if (retrievalState < target.State || target.State == retrievalState) { return; }
                    bool wasRetrieved = target.Retrieved;
                    target.State = retrievalState;
                    //increment the mission state if the target became retrieved               
                    if (!wasRetrieved && target.Retrieved) 
                    { 
                        State = Math.Max(i + 1, State);
                        atLeastOneTargetWasRetrieved = true;
                    }
                }
            }
#if CLIENT
            if (atLeastOneTargetWasRetrieved)
            {
                TryShowRetrievedMessage();
            }
#endif
            if (targets.All(t => t.Retrieved))
            {
                State = targets.Count + 1;
            }            
        }

        protected override bool DetermineCompleted()
        {
            if (requiredDeliveryAmount < 1.0f)
            {
                return targets.Count(t => IsTargetRetrieved(t)) / (float)targets.Count >= requiredDeliveryAmount;
            }
            else
            {
                return targets.All(IsTargetRetrieved);
            }

            static bool IsTargetRetrieved(Target target)
            {
                if (target.State < target.RequiredRetrievalState) { return false; }
                if (target.RequireInsideOriginalContainer)
                {
                    if (target.Item.ParentInventory != target.OriginalContainer?.OwnInventory) { return false; }
                }
                return true;
            }
        }

        protected override void EndMissionSpecific(bool completed)
        {
            //consider failed (can't attempt again) if we picked up any of the items but failed to bring them out of the level
            failed = !completed && targets.Any(t => t.State >= Target.RetrievalState.PickedUp);
            List<Target> targetsToRemove = new List<Target>();
            foreach (var target in targets)
            {
                if (target.RemoveItem ||
                    /*remove the target if it's inside another target that's set to be removed (e.g. inside the crate it spawned in)*/
                    targets.Any(t => t.RemoveItem && target.Item?.ParentInventory?.Owner as Item == t.Item))
                {
                    targetsToRemove.Add(target);
                }
            }
            foreach (var target in targetsToRemove)
            {
                if (target.Item != null && !target.Item.Removed)
                {
                    target.Item.Remove();
                }
                target.Reset();
            }
        }
    }
}
