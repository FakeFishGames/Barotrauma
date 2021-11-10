using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class SalvageMission : Mission
    {
        private readonly ItemPrefab itemPrefab;

        private Item item;

        private readonly Level.PositionType spawnPositionType;
                
        private readonly string containerTag;

        private readonly string existingItemTag;
        
        private readonly bool showMessageWhenPickedUp;

        /// <summary>
        /// Status effects executed on the target item when the mission starts. A random effect is chosen from each child list.
        /// </summary>
        private readonly List<List<StatusEffect>> statusEffects = new List<List<StatusEffect>>();

        public override IEnumerable<Vector2> SonarPositions
        {
            get
            {
                if (item == null)
                {
                    Enumerable.Empty<Vector2>();
                }
                else
                {
                    yield return item.GetRootInventoryOwner()?.WorldPosition ?? item.WorldPosition;
                }
            }
        }

        public SalvageMission(MissionPrefab prefab, Location[] locations, Submarine sub)
            : base(prefab, locations, sub)
        {
            containerTag = prefab.ConfigElement.GetAttributeString("containertag", "");

            if (prefab.ConfigElement.Attribute("itemname") != null)
            {
                DebugConsole.ThrowError("Error in SalvageMission - use item identifier instead of the name of the item.");
                string itemName = prefab.ConfigElement.GetAttributeString("itemname", "");
                itemPrefab = MapEntityPrefab.Find(itemName) as ItemPrefab;
                if (itemPrefab == null)
                {
                    DebugConsole.ThrowError("Error in SalvageMission: couldn't find an item prefab with the name " + itemName);
                }
            }
            else
            {
                string itemIdentifier = prefab.ConfigElement.GetAttributeString("itemidentifier", "");
                itemPrefab = MapEntityPrefab.Find(null, itemIdentifier) as ItemPrefab;
                if (itemPrefab == null)
                {
                    DebugConsole.ThrowError("Error in SalvageMission - couldn't find an item prefab with the identifier " + itemIdentifier);
                }
            }

            existingItemTag = prefab.ConfigElement.GetAttributeString("existingitemtag", "");
            showMessageWhenPickedUp = prefab.ConfigElement.GetAttributeBool("showmessagewhenpickedup", false);

            string spawnPositionTypeStr = prefab.ConfigElement.GetAttributeString("spawntype", "");
            if (string.IsNullOrWhiteSpace(spawnPositionTypeStr) ||
                !Enum.TryParse(spawnPositionTypeStr, true, out spawnPositionType))
            {
                spawnPositionType = Level.PositionType.Cave | Level.PositionType.Ruin;
            }

            foreach (XElement element in prefab.ConfigElement.Elements())
            {
                switch (element.Name.ToString().ToLowerInvariant())
                {
                    case "statuseffect":
                        {
                            var newEffect = StatusEffect.Load(element, parentDebugName: prefab.Name);
                            if (newEffect == null) { continue; }
                            statusEffects.Add(new List<StatusEffect> { newEffect });
                            break;
                        }
                    case "chooserandom":
                        statusEffects.Add(new List<StatusEffect>());
                        foreach (XElement subElement in element.Elements())
                        {
                            var newEffect = StatusEffect.Load(subElement, parentDebugName: prefab.Name);
                            if (newEffect == null) { continue; }
                            statusEffects.Last().Add(newEffect);
                        }
                        break;
                }
            }
        }

        protected override void StartMissionSpecific(Level level)
        {
#if SERVER
            originalInventoryID = Entity.NullEntityID;
#endif
            item = null;
            if (!IsClient)
            {
                //ruin/cave/wreck items are allowed to spawn close to the sub
                float minDistance = spawnPositionType == Level.PositionType.Ruin || spawnPositionType == Level.PositionType.Cave || spawnPositionType == Level.PositionType.Wreck ?
                    0.0f : Level.Loaded.Size.X * 0.3f;
                Vector2 position = Level.Loaded.GetRandomItemPos(spawnPositionType, 100.0f, minDistance, 30.0f);
            
                if (!string.IsNullOrEmpty(existingItemTag))
                {
                    var suitableItems = Item.ItemList.Where(it => it.HasTag(existingItemTag));
                    switch (spawnPositionType)
                    {
                        case Level.PositionType.Cave:
                        case Level.PositionType.MainPath:
                        case Level.PositionType.SidePath:
                            item = suitableItems.FirstOrDefault(it => Vector2.DistanceSquared(it.WorldPosition, position) < 1000.0f);
                            break;
                        case Level.PositionType.Ruin:
                        case Level.PositionType.Wreck:
                            foreach (Item it in suitableItems)
                            {
                                if (it.Submarine?.Info == null) { continue; }
                                if (spawnPositionType == Level.PositionType.Ruin && it.Submarine.Info.Type != SubmarineType.Ruin) { continue; }
                                if (spawnPositionType == Level.PositionType.Wreck && it.Submarine.Info.Type != SubmarineType.Wreck) { continue; }
                                Rectangle worldBorders = it.Submarine.Borders;
                                worldBorders.Location += it.Submarine.WorldPosition.ToPoint();
                                if (Submarine.RectContains(worldBorders, it.WorldPosition))
                                {
                                    item = it;
#if SERVER
                                    usedExistingItem = true;
#endif
                                    break;
                                }
                            }
                            break;
                    }
                }

                if (item == null)
                {
                    item = new Item(itemPrefab, position, null);
                    item.body.FarseerBody.BodyType = BodyType.Kinematic;
                    item.FindHull();
                }

                for (int i = 0; i < statusEffects.Count; i++)
                {
                    List<StatusEffect> effectList = statusEffects[i];
                    if (effectList.Count == 0) { continue; }
                    int effectIndex = Rand.Int(effectList.Count);
                    var selectedEffect = effectList[effectIndex];
                    item.ApplyStatusEffect(selectedEffect, selectedEffect.type, deltaTime: 1.0f, worldPosition: item.Position);
#if SERVER
                    executedEffectIndices.Add(new Pair<int, int>(i, effectIndex));
#endif
                }

                //try to find a container and place the item inside it
                if (!string.IsNullOrEmpty(containerTag) && item.ParentInventory == null)
                {
                    List<ItemContainer> validContainers = new List<ItemContainer>();
                    foreach (Item it in Item.ItemList)
                    {
                        if (!it.HasTag(containerTag)) { continue; }
                        if (!it.IsPlayerTeamInteractable) { continue; }
                        switch (spawnPositionType)
                        {
                            case Level.PositionType.Cave:
                            case Level.PositionType.MainPath:
                                if (it.Submarine != null) { continue; }
                                break;
                            case Level.PositionType.Ruin:
                                if (it.Submarine?.Info == null || !it.Submarine.Info.IsRuin) { continue; }
                                break;
                            case Level.PositionType.Wreck:
                                if (it.Submarine == null || it.Submarine.Info.Type != SubmarineType.Wreck) { continue; }
                                break;
                        }
                        var itemContainer = it.GetComponent<ItemContainer>();
                        if (itemContainer != null && itemContainer.Inventory.CanBePut(item)) { validContainers.Add(itemContainer); }
                    }
                    if (validContainers.Any())
                    {
                        var selectedContainer = validContainers.GetRandom();
                        if (selectedContainer.Combine(item, user: null))
                        {
#if SERVER
                            originalInventoryID = selectedContainer.Item.ID;
                            originalItemContainerIndex = (byte)selectedContainer.Item.GetComponentIndex(selectedContainer);
#endif
                        } // Placement successful
                    }
                }
            }
        }

        protected override void UpdateMissionSpecific(float deltaTime)
        {
            if (item == null)
            {
#if DEBUG
                DebugConsole.ThrowError("Error in salvage mission " + Prefab.Identifier + " (item was null)");
#endif
                return;
            }

            if (IsClient)
            {
                if (item.ParentInventory != null && item.body != null) { item.body.FarseerBody.BodyType = BodyType.Dynamic; }
                return;
            }
            switch (State)
            {
                case 0:
                    if (item.ParentInventory != null && item.body != null) { item.body.FarseerBody.BodyType = BodyType.Dynamic; }
                    if (showMessageWhenPickedUp)
                    {
                        if (!(item.GetRootInventoryOwner() is Character)) { return; }
                    }
                    else
                    {
                        Submarine parentSub = item.CurrentHull?.Submarine ?? item.GetRootInventoryOwner()?.Submarine;
                        if (parentSub == null || parentSub.Info.Type != SubmarineType.Player) 
                        {
                            return; 
                        }
                    }
                    State = 1;
                    break;
                case 1:
                    if (!Submarine.MainSub.AtEndExit && !Submarine.MainSub.AtStartExit) { return; }
                    State = 2;
                    break;
            }
        }

        public override void End()
        {
            var root = item?.GetRootContainer() ?? item;
            if (root?.CurrentHull?.Submarine == null || (!root.CurrentHull.Submarine.AtEndExit && !root.CurrentHull.Submarine.AtStartExit) || item.Removed) 
            { 
                return; 
            }

            if (Prefab.LocationTypeChangeOnCompleted != null)
            {
                ChangeLocationType(Prefab.LocationTypeChangeOnCompleted);
            }

            item?.Remove();
            item = null;
            GiveReward();
            completed = true;
            failed = !completed && state > 0;
        }
    }
}
