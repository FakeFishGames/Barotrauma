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
            /// Note that the integer values matter here: the state of the target can't go back to a smaller value,
            /// and a larger or equal value than the <see href="RequiredRetrievalState">RequiredRetrievalState</see> means the item counts as retrieved
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
            public readonly Level.PositionType SpawnPositionType;
            public readonly string ContainerTag;
            public readonly string ExistingItemTag;

            public readonly bool RemoveItem;

            public readonly LocalizedString SonarLabel;

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

            /// <summary>
            /// Status effects executed on the target item when the mission starts. A random effect is chosen from each child list.
            /// </summary>
            public readonly List<List<StatusEffect>> StatusEffects = new List<List<StatusEffect>>();

            public Target(ContentXElement element, SalvageMission mission)
            {
                this.mission = mission;
                ContainerTag = element.GetAttributeString("containertag", "");
                RequiredRetrievalState = element.GetAttributeEnum("requireretrieval", RetrievalState.RetrievedToSub);
                AllowContinueBeforeRetrieved = element.GetAttributeBool("allowcontinuebeforeretrieved", false);
                HideLabelAfterRetrieved = element.GetAttributeBool("hidelabelafterretrieved", false);

                string sonarLabelTag = element.GetAttributeString("sonarlabel", "");
                if (!string.IsNullOrEmpty(sonarLabelTag))
                {
                    SonarLabel =
                        TextManager.Get($"MissionSonarLabel.{sonarLabelTag}")
                        .Fallback(TextManager.Get(sonarLabelTag))
                        .Fallback(element.GetAttributeString("sonarlabel", ""));
                }
                ExistingItemTag = element.GetAttributeString("existingitemtag", "");

                RemoveItem = element.GetAttributeBool("removeitem", true);

                if (element.GetAttribute("itemname") != null)
                {
                    DebugConsole.ThrowError("Error in SalvageMission - use item identifier instead of the name of the item.");
                    string itemName = element.GetAttributeString("itemname", "");
                    ItemPrefab = MapEntityPrefab.Find(itemName) as ItemPrefab;
                    if (ItemPrefab == null && ExistingItemTag.IsNullOrEmpty())
                    {
                        DebugConsole.ThrowError($"Error in SalvageMission: couldn't find an item prefab with the name \"{itemName}\"");
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
                        ItemPrefab = MapEntityPrefab.GetRandom(p => p.Tags.Contains(itemTag), Rand.RandSync.Unsynced) as ItemPrefab;
                    }
                    if (ItemPrefab == null && ExistingItemTag.IsNullOrEmpty())
                    {
                        DebugConsole.ThrowError($"Error in SalvageMission - couldn't find an item prefab with the identifier \"{itemIdentifier}\"");
                    }
                }

                SpawnPositionType = element.GetAttributeEnum("spawntype", Level.PositionType.Cave | Level.PositionType.Ruin);

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
                            StatusEffects.Add(new List<StatusEffect>());
                            foreach (var effectElement in subElement.Elements())
                            {
                                var newEffect = StatusEffect.Load(effectElement, parentDebugName: mission.Prefab.Name.Value);
                                if (newEffect == null) { continue; }
                                StatusEffects.Last().Add(newEffect);
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

        public override IEnumerable<(LocalizedString Label, Vector2 Position)> SonarLabels
        {
            get
            {
                foreach (var target in targets)
                {
                    if (target.Retrieved && target.HideLabelAfterRetrieved) { continue; }
                    if (target.Item != null)
                    {
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
            foreach (ContentXElement subElement in prefab.ConfigElement.Elements())
            {
                if (subElement.NameAsIdentifier() == "target")
                {
                    targets.Add(new Target(subElement, this));
                }
            }
            if (!targets.Any())
            {
                targets.Add(new Target(prefab.ConfigElement, this));
            }
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

                    if (!string.IsNullOrEmpty(target.ExistingItemTag))
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
                        if (target.ItemPrefab == null && string.IsNullOrEmpty(target.ContainerTag))
                        {
                            DebugConsole.ThrowError($"Failed to find a target item for the mission \"{Prefab.Identifier}\". Item tag: {target.ExistingItemTag ?? "null"}");
                            continue;
                        }
                        target.Item = new Item(target.ItemPrefab, position, null);
                        target.Item.body.SetTransformIgnoreContacts(target.Item.body.SimPosition, target.Item.body.Rotation);
                        target.Item.body.FarseerBody.BodyType = BodyType.Kinematic;
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

                    //try to find a container and place the item inside it
                    if (!string.IsNullOrEmpty(target.ContainerTag) && target.Item.ParentInventory == null)
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
        }

        protected override void UpdateMissionSpecific(float deltaTime)
        {
            //make body dynamic when picked up
            foreach (var target in targets)
            {
                var root = target.Item?.GetRootContainer() ?? target.Item;
                if (root == null) { continue; }
                if (target.Item.ParentInventory != null && target.Item.body != null) { target.Item.body.FarseerBody.BodyType = BodyType.Dynamic; }
            }

            if (IsClient) { return; }

            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (i > 0 && !targets[i - 1].AllowContinueBeforeRetrieved && !targets[i - 1].Retrieved) { break; }
                if (target.Item == null)
                {
#if DEBUG
                    DebugConsole.ThrowError("Error in salvage mission " + Prefab.Identifier + " (item was null)");
#endif
                    return;
                }
                switch (target.State)
                {
                    case Target.RetrievalState.None:
                        if (target.Interacted)
                        {
                            TrySetRetrievalState(Target.RetrievalState.Interact);
                        }
                        var root = target.Item?.GetRootContainer() ?? target.Item;
                        if (root.ParentInventory?.Owner is Character character && character.TeamID == CharacterTeamType.Team1)
                        {
                            TrySetRetrievalState(Target.RetrievalState.PickedUp);
                        }
                        break;
                    case Target.RetrievalState.PickedUp:
                        Submarine parentSub = target.Item.CurrentHull?.Submarine ?? target.Item.GetRootInventoryOwner()?.Submarine;
                        if (parentSub != null && parentSub.Info.Type == SubmarineType.Player)
                        {
                            TrySetRetrievalState(Target.RetrievalState.RetrievedToSub);
                        }
                        break;
                }

                void TrySetRetrievalState(Target.RetrievalState retrievalState)
                {
                    if (retrievalState < target.State) { return; }
                    bool wasRetrieved = false;
                    target.State = retrievalState;
                    //increment the mission state if the target became retrieved
                    if (!wasRetrieved && target.Retrieved) { State = i + 1; }
                }
            }
            if (targets.All(t => t.Retrieved))
            {
                State = targets.Count + 1;
            }
        }

        protected override bool DetermineCompleted()
        {
            return targets.All(t => t.State >= t.RequiredRetrievalState);
        }

        protected override void EndMissionSpecific(bool completed)
        {
            //consider failed (can't attempt again) if we picked up any of the items but failed to bring them out of the level
            failed = !completed && targets.Any(t => t.State >= Target.RetrievalState.PickedUp);
            foreach (var target in targets)
            {
                if (target.RemoveItem)
                {
                    target.Item?.Remove();
                    target.Reset();
                }
            }
        }
    }
}
