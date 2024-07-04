#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;

namespace Barotrauma.Items.Components
{
    internal sealed partial class CircuitBox : ItemComponent, IClientSerializable, IServerSerializable
    {
        public static readonly ImmutableHashSet<CircuitBoxOpcode> UnrealiableOpcodes
            = ImmutableHashSet.Create(CircuitBoxOpcode.Cursor);

        public ImmutableArray<CircuitBoxInputConnection> Inputs;
        public ImmutableArray<CircuitBoxOutputConnection> Outputs;

        public readonly List<CircuitBoxComponent> Components = new List<CircuitBoxComponent>();

        public readonly List<CircuitBoxInputOutputNode> InputOutputNodes = new();

        public readonly List<CircuitBoxLabelNode> Labels = new();

        public readonly List<CircuitBoxWire> Wires = new List<CircuitBoxWire>();

        public override bool IsActive => true;

        // We don't want the components and wires to transfer between subs as it would cause issues.
        public override bool DontTransferInventoryBetweenSubs => true;

        // We don't want to sell the components and wires inside the circuit box
        public override bool DisallowSellingItemsFromContainer => true;

        public Option<CircuitBoxConnection> FindInputOutputConnection(Identifier connectionName)
        {
            foreach (CircuitBoxInputConnection input in Inputs)
            {
                if (input.Name != connectionName) { continue; }
                return Option.Some<CircuitBoxConnection>(input);
            }

            foreach (CircuitBoxOutputConnection output in Outputs)
            {
                if (output.Name != connectionName) { continue; }
                return Option.Some<CircuitBoxConnection>(output);
            }

            return Option.None;
        }

        public Option<CircuitBoxConnection> FindInputOutputConnection(Connection connection)
        {
            foreach (CircuitBoxInputConnection input in Inputs)
            {
                if (input.Connection != connection) { continue; }
                return Option.Some<CircuitBoxConnection>(input);
            }

            foreach (CircuitBoxOutputConnection output in Outputs)
            {
                if (output.Connection != connection) { continue; }
                return Option.Some<CircuitBoxConnection>(output);
            }

            return Option.None;
        }

        public readonly ItemContainer[] containers;

        private const int ComponentContainerIndex = 0,
                          WireContainerIndex = 1;

        public ItemContainer? ComponentContainer
            => GetContainerOrNull(ComponentContainerIndex);

        // wire container falls back to the main container if one isn't specified
        public ItemContainer? WireContainer
            => GetContainerOrNull(WireContainerIndex) ?? GetContainerOrNull(ComponentContainerIndex);

        public bool IsFull => ComponentContainer?.Inventory is { } inventory && inventory.IsFull(true);

        [Editable, Serialize(false, IsPropertySaveable.Yes, description: "Locked circuit boxes can only be viewed and not interacted with.")]
        public bool Locked { get; set; }

        public CircuitBox(Item item, ContentXElement element) : base(item, element)
        {
            containers = item.GetComponents<ItemContainer>().ToArray();
            if (containers.Length < 1)
            {
                DebugConsole.ThrowError("Circuit box must have at least one item container to function.");
            }

            InitProjSpecific(element);

            var inputBuilder = ImmutableArray.CreateBuilder<CircuitBoxInputConnection>();
            var outputBuilder = ImmutableArray.CreateBuilder<CircuitBoxOutputConnection>();

            foreach (Connection conn in Item.Connections)
            {
                if (conn.IsOutput)
                {
                    outputBuilder.Add(new CircuitBoxOutputConnection(Vector2.Zero, conn, this));
                }
                else
                {
                    inputBuilder.Add(new CircuitBoxInputConnection(Vector2.Zero, conn, this));
                }
            }

            Inputs = inputBuilder.ToImmutable();
            Outputs = outputBuilder.ToImmutable();

            InputOutputNodes.Add(new CircuitBoxInputOutputNode(Inputs, new Vector2(-512, 0f), CircuitBoxInputOutputNode.Type.Input, this));
            InputOutputNodes.Add(new CircuitBoxInputOutputNode(Outputs, new Vector2(512, 0f), CircuitBoxInputOutputNode.Type.Output, this));

            item.OnDeselect += OnDeselected;
        }

        /// <summary>
        /// We want to load the components after the map has loaded since we need to link up the components to their items
        /// and pretty much all items have higher ID than the circuit box.
        /// </summary>
        private Option<ContentXElement> delayedElementToLoad;

        public override void Load(ContentXElement componentElement, bool usePrefabValues, IdRemap idRemap, bool isItemSwap)
        {
            base.Load(componentElement, usePrefabValues, idRemap, isItemSwap);
            if (delayedElementToLoad.IsSome()) { return; }
            delayedElementToLoad = Option.Some(componentElement);
        }

        public override void OnInventoryChanged()
            => OnViewUpdateProjSpecific();

        public override void Update(float deltaTime, Camera cam)
        {
#if CLIENT
            // When loading from the server the wires cannot be properly loaded and connected up because we might not be loaded in properly yet.
            // So we need to wait until the circuit box starts updating and then we can ensure the wires are connected.
            if (wasInitializedByServer)
            {
                foreach (var w in Wires)
                {
                    w.EnsureWireConnected();
                }
                wasInitializedByServer = false;
            }
#endif
            TryInitializeNodes();
        }

        public override void OnMapLoaded()
            => TryInitializeNodes();

        private void TryInitializeNodes()
        {
            if (!delayedElementToLoad.TryUnwrap(out var loadElement)) { return; }
            LoadFromXML(loadElement);
            delayedElementToLoad = Option.None;
        }

        public void LoadFromXML(ContentXElement loadElement)
        {
            foreach (var subElement in loadElement.Elements())
            {
                string elementName = subElement.Name.ToString().ToLowerInvariant();
                switch (elementName)
                {
                    case "component" when CircuitBoxComponent.TryLoadFromXML(subElement, this).TryUnwrap(out var comp):
                        Components.Add(comp);
                        break;
                    case "wire" when CircuitBoxWire.TryLoadFromXML(subElement, this).TryUnwrap(out var wire):
                        Wires.Add(wire);
                        break;
                    case "inputnode":
                        LoadFor(CircuitBoxInputOutputNode.Type.Input, subElement);
                        break;
                    case "outputnode":
                        LoadFor(CircuitBoxInputOutputNode.Type.Output, subElement);
                        break;
                    case "label":
                        Labels.Add(CircuitBoxLabelNode.LoadFromXML(subElement, this));
                        break;
                }
            }

#if SERVER
            // We need to let the clients know of the loaded data
            if (needsServerInitialization)
            {
                CreateInitializationEvent();
                needsServerInitialization = false;
            }
#endif

            void LoadFor(CircuitBoxInputOutputNode.Type type, ContentXElement subElement)
            {
                foreach (var node in InputOutputNodes)
                {
                    if (node.NodeType != type) { continue; }

                    node.Load(subElement);
                    break;
                }
            }
        }

        public void CloneFrom(CircuitBox original, Dictionary<ushort, Item> clonedContainedItems)
        {
            Components.Clear();
            Wires.Clear();
            Labels.Clear();

            foreach (var label in original.Labels)
            {
                var newLabel = new CircuitBoxLabelNode(label.ID, label.Color, label.Position, this);
                newLabel.EditText(label.HeaderText, label.BodyText);
                newLabel.ApplyResize(label.Size, label.Position);
                Labels.Add(newLabel);
            }

            for (int ioIndex = 0; ioIndex < original.InputOutputNodes.Count; ioIndex++)
            {
                var origNode = original.InputOutputNodes[ioIndex];
                var cloneNode = InputOutputNodes[ioIndex];

                cloneNode.Position = origNode.Position;
            }

            if (!clonedContainedItems.Any()) { return; }
            
            foreach (var origComp in original.Components)
            {
                if (!clonedContainedItems.TryGetValue(origComp.Item.ID, out var clonedItem)) { continue; }
                var newComponent = new CircuitBoxComponent(origComp.ID, clonedItem, origComp.Position, this, origComp.UsedResource);
                Components.Add(newComponent);
            }

            foreach (var origWire in original.Wires)
            {
                Option<CircuitBoxConnection> to = CircuitBoxConnectorIdentifier.FromConnection(origWire.To).FindConnection(this),
                    from = CircuitBoxConnectorIdentifier.FromConnection(origWire.From).FindConnection(this);

                if (!to.TryUnwrap(out var toConn) || !from.TryUnwrap(out var fromConn))
                {
                    DebugConsole.ThrowError($"Error while cloning item \"{Name}\" - failed to find a connection for a wire. ");
                    continue;
                }

                var wireItem = origWire.BackingWire.Select(w => clonedContainedItems[w.ID]);
                var newWire = new CircuitBoxWire(this, origWire.ID, wireItem, fromConn, toConn, origWire.UsedItemPrefab);
                Wires.Add(newWire);
            }
        }

        public override XElement Save(XElement parentElement)
        {
            XElement componentElement = base.Save(parentElement);

            foreach (CircuitBoxInputOutputNode node in InputOutputNodes)
            {
                componentElement.Add(node.Save());
            }

            foreach (CircuitBoxComponent node in Components)
            {
                componentElement.Add(node.Save());
            }

            foreach (CircuitBoxWire wire in Wires)
            {
                componentElement.Add(wire.Save());
            }

            foreach (var label in Labels)
            {
                componentElement.Add(label.Save());
            }

            return componentElement;
        }

        public partial void OnDeselected(Character c);

        public record struct CreatedWire(CircuitBoxConnectorIdentifier Start, CircuitBoxConnectorIdentifier End, Option<Item> Item, ushort ID);

        public bool Connect(CircuitBoxConnection one, CircuitBoxConnection two, Action<CreatedWire> onCreated, ItemPrefab selectedWirePrefab)
        {
            if (!VerifyConnection(one, two)) { return false; }

            ushort id = ICircuitBoxIdentifiable.FindFreeID(Wires);
            switch (one.IsOutput)
            {
                case true when !two.IsOutput:
                {
                    CircuitBoxConnectorIdentifier start = CircuitBoxConnectorIdentifier.FromConnection(one),
                                                  end = CircuitBoxConnectorIdentifier.FromConnection(two);

                    if (IsExternalConnection(one) || IsExternalConnection(two))
                    {
                        CreateWireWithoutItem(one, two, id, selectedWirePrefab);
                        onCreated(new CreatedWire(start, end, Option.None, id));
                        return true;
                    }

                    CreateWireWithItem(one, two, selectedWirePrefab, id, i => onCreated(new CreatedWire(start, end, Option.Some(i), id)));
                    return true;
                }
                case false when two.IsOutput:
                {
                    CircuitBoxConnectorIdentifier start = CircuitBoxConnectorIdentifier.FromConnection(two),
                                                  end = CircuitBoxConnectorIdentifier.FromConnection(one);
                    if (IsExternalConnection(one) || IsExternalConnection(two))
                    {
                        CreateWireWithoutItem(two, one, id, selectedWirePrefab);
                        onCreated(new CreatedWire(start, end, Option.None, id));
                        return true;
                    }

                    CreateWireWithItem(two, one, selectedWirePrefab, id, i => onCreated(new CreatedWire(start, end, Option.Some(i), id)));
                    return true;
                }
            }

            return false;
        }

        private static bool VerifyConnection(CircuitBoxConnection one, CircuitBoxConnection two)
        {
            if (one.IsOutput == two.IsOutput || one == two) { return false; }

            if (one is CircuitBoxNodeConnection oneNodeConnection &&
                two is CircuitBoxNodeConnection twoNodeConnection)
            {
                if (oneNodeConnection.Component == twoNodeConnection.Component)
                {
                    return false;
                }
            }

            if (one is CircuitBoxNodeConnection { HasAvailableSlots: false } ||
                two is CircuitBoxNodeConnection { HasAvailableSlots: false })
            {
                return one is not CircuitBoxNodeConnection || two is not CircuitBoxNodeConnection;
            }

            return true;
        }

        private void AddLabelInternal(ushort id, Color color, Vector2 pos, NetLimitedString header, NetLimitedString body)
        {
            var newLabel = new CircuitBoxLabelNode(id, color, pos, this);
            newLabel.EditText(header, body);
            Labels.Add(newLabel);
            OnViewUpdateProjSpecific();
        }

        private void RemoveLabelInternal(IReadOnlyCollection<ushort> ids)
        {
            foreach (CircuitBoxLabelNode node in Labels.ToImmutableArray())
            {
                if (!ids.Contains(node.ID)) { continue; }
                Labels.Remove(node);
            }
            OnViewUpdateProjSpecific();
        }

        private void ResizeLabelInternal(ushort id, Vector2 pos, Vector2 size)
        {
            size = Vector2.Max(size, CircuitBoxLabelNode.MinSize);
            foreach (CircuitBoxLabelNode node in Labels)
            {
                if (node.ID != id) { continue; }
                node.ApplyResize(size, pos);
                break;
            }
            OnViewUpdateProjSpecific();
        }

        private void RenameConnectionLabelsInternal(CircuitBoxInputOutputNode.Type type, Dictionary<string, string> overrides)
        {
            foreach (var node in InputOutputNodes)
            {
                if (node.NodeType != type) { continue; }

                node.ReplaceAllConnectionLabelOverrides(overrides);
                break;
            }
            OnViewUpdateProjSpecific();
        }

        private static bool IsExternalConnection(CircuitBoxConnection conn) => conn is (CircuitBoxInputConnection or CircuitBoxOutputConnection);

        private void CreateWireWithoutItem(CircuitBoxConnection one, CircuitBoxConnection two, ushort id, ItemPrefab prefab)
        {
            bool hasExternalConnection = false;
            if (one is CircuitBoxInputConnection input)
            {
                hasExternalConnection = true;
                input.ExternallyConnectedTo.Add(two);
            }

            if (two is CircuitBoxOutputConnection output)
            {
                hasExternalConnection = true;
                one.Connection.CircuitBoxConnections.Add(output);
            }

            if (hasExternalConnection)
            {
                two.ExternallyConnectedFrom.Add(one);
            }

            AddWireDirect(id, prefab, Option.None, one, two);
        }

        private void CreateWireWithItem(CircuitBoxConnection one, CircuitBoxConnection two, ItemPrefab prefab, ushort wireId, Action<Item> onItemSpawned)
        {
            if (WireContainer is null) { return; }

            if (IsExternalConnection(one) || IsExternalConnection(two))
            {
                DebugConsole.ThrowError("Cannot add a wire between an external connection and a component connection.");
                return;
            }

            SpawnItem(prefab, user: null, container: WireContainer, onSpawned: wire =>
            {
                AddWireDirect(wireId, prefab, Option.Some(wire), one, two);
                onItemSpawned(wire);
            });
        }

        private void CreateWireWithItem(CircuitBoxConnection one, CircuitBoxConnection two, ushort wireId, Item it)
        {
            if (IsExternalConnection(one) || IsExternalConnection(two))
            {
                DebugConsole.ThrowError("Cannot add a wire between an external connection and a component connection.");
                return;
            }

            AddWireDirect(wireId, it.Prefab, Option.Some(it), one, two);
        }

        private void AddWireDirect(ushort id, ItemPrefab prefab, Option<Item> backingItem, CircuitBoxConnection one, CircuitBoxConnection two)
            => Wires.Add(new CircuitBoxWire(this, id, backingItem, one, two, prefab));

        private void RenameLabelInternal(ushort id, Color color, NetLimitedString header, NetLimitedString body)
        {
            foreach (CircuitBoxLabelNode node in Labels)
            {
                if (node.ID != id) { continue; }

                node.EditText(header, body);
                node.Color = color;
                break;
            }
        }

        private bool AddComponentInternal(ushort id, ItemPrefab prefab, ItemPrefab usedResource, Vector2 pos, Character? user, Action<Item>? onItemSpawned)
        {
            if (id is ICircuitBoxIdentifiable.NullComponentID)
            {
                DebugConsole.ThrowError("Unable to add component because there are no free IDs.");
                return false;
            }

            if (ComponentContainer?.Inventory is { } inventory && inventory.HowManyCanBePut(prefab) <= 0)
            {
                DebugConsole.ThrowError("Unable to add component because there is no space in the inventory.");
                return false;
            }

            SpawnItem(prefab, user, ComponentContainer, spawnedItem =>
            {
                Components.Add(new CircuitBoxComponent(id, spawnedItem, pos, this, usedResource));
                onItemSpawned?.Invoke(spawnedItem);
                OnViewUpdateProjSpecific();
            });
            OnViewUpdateProjSpecific();
            return true;
        }

        // Unsafe because it doesn't perform error checking since it's data we get from the server
        private void AddComponentInternalUnsafe(ushort id, Item backingItem, ItemPrefab usedResource, Vector2 pos)
        {
            Components.Add(new CircuitBoxComponent(id, backingItem, pos, this, usedResource));
            OnViewUpdateProjSpecific();
        }

        private static void ClearSelectionFor(ushort characterId, IReadOnlyCollection<CircuitBoxSelectable> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.SelectedBy != characterId) { continue; }

                node.SetSelected(Option.None);
            }
        }

        private void ClearAllSelectionsInternal(ushort characterId)
        {
            ClearSelectionFor(characterId, Components);
            ClearSelectionFor(characterId, InputOutputNodes);
            ClearSelectionFor(characterId, Wires);
            ClearSelectionFor(characterId, Labels);
        }

        private void SelectLabelsInternal(IReadOnlyCollection<ushort> ids, ushort characterId, bool overwrite)
        {
            if (overwrite) { ClearSelectionFor(characterId, Labels); }

            if (!ids.Any()) { return; }

            foreach (CircuitBoxLabelNode node in Labels)
            {
                if (!ids.Contains(node.ID)) { continue; }

                node.SetSelected(Option.Some(characterId));
            }
        }

        private void SelectComponentsInternal(IReadOnlyCollection<ushort> ids, ushort characterId, bool overwrite)
        {
            if (overwrite) { ClearSelectionFor(characterId, Components); }

            if (!ids.Any()) { return; }

            foreach (CircuitBoxComponent node in Components)
            {
                if (!ids.Contains(node.ID)) { continue; }

                node.SetSelected(Option.Some(characterId));
            }
        }

        private void UpdateSelections(ImmutableDictionary<ushort, Option<ushort>> nodeIds,
                                      ImmutableDictionary<ushort, Option<ushort>> wireIds,
                                      ImmutableDictionary<CircuitBoxInputOutputNode.Type, Option<ushort>> inputOutputs,
                                      ImmutableDictionary<ushort, Option<ushort>> labels)
        {
            foreach (var wire in Wires)
            {
                if (!wireIds.TryGetValue(wire.ID, out var selectedBy)) { continue; }

                if (selectedBy.TryUnwrap(out var id))
                {
                    wire.IsSelected = true;
                    wire.SelectedBy = id;
                    continue;
                }

                wire.IsSelected = false;
                wire.SelectedBy = 0;
            }

            foreach (var node in Components)
            {
                if (!nodeIds.TryGetValue(node.ID, out var selectedBy)) { continue; }

                node.SetSelected(selectedBy);
            }

            foreach (var node in InputOutputNodes)
            {
                if (!inputOutputs.TryGetValue(node.NodeType, out var selectedBy)) { continue; }

                node.SetSelected(selectedBy);
            }

            foreach (var node in Labels)
            {
                if (!labels.TryGetValue(node.ID, out var selectedBy)) { continue; }

                node.SetSelected(selectedBy);
            }
        }

        private void SelectWiresInternal(IReadOnlyCollection<ushort> ids, ushort characterId, bool overwrite)
        {
            if (overwrite) { ClearSelectionFor(characterId, Wires); }

            foreach (CircuitBoxWire wire in Wires)
            {
                if (!ids.Contains(wire.ID)) { continue; }

                wire.SetSelected(Option.Some(characterId));
            }
        }

        private void SelectInputOutputInternal(IReadOnlyCollection<CircuitBoxInputOutputNode.Type> io, ushort characterId, bool overwrite)
        {
            if (overwrite) { ClearSelectionFor(characterId, InputOutputNodes); }

            foreach (var node in InputOutputNodes)
            {
                if (!io.Contains(node.NodeType)) { continue; }

                node.SetSelected(Option.Some(characterId));
            }
        }

        private void RemoveComponentInternal(IReadOnlyCollection<ushort> ids)
        {
            foreach (CircuitBoxComponent node in Components.ToImmutableArray())
            {
                if (!ids.Contains(node.ID)) { continue; }

                Components.Remove(node);
                node.Remove();

                foreach (CircuitBoxWire wire in Wires.ToImmutableArray())
                {
                    if (node.Connectors.Contains(wire.From) || node.Connectors.Contains(wire.To))
                    {
                        RemoveWireCollectionUnsafe(wire);
                    }
                }
            }
            OnViewUpdateProjSpecific();
        }

        private void RemoveWireInternal(IReadOnlyCollection<ushort> ids)
        {
            foreach (CircuitBoxWire wire in Wires.ToImmutableArray())
            {
                if (!ids.Contains(wire.ID)) { continue; }

                RemoveWireCollectionUnsafe(wire);
            }

            OnViewUpdateProjSpecific();
        }

        private void RemoveWireCollectionUnsafe(CircuitBoxWire wire)
        {
            foreach (CircuitBoxOutputConnection output in Outputs)
            {
                output.Connection.CircuitBoxConnections.Remove(wire.From);
            }

            wire.From.Connection.CircuitBoxConnections.Remove(wire.To);

            if (wire.From is CircuitBoxInputConnection input)
            {
                input.ExternallyConnectedTo.Remove(wire.To);
            }

            wire.To.ExternallyConnectedFrom.Remove(wire.From);
            wire.From.ExternallyConnectedFrom.Remove(wire.To);

            wire.Remove();
            Wires.Remove(wire);
        }

        private void MoveNodesInternal(IReadOnlyCollection<ushort> ids,
                                       IReadOnlyCollection<CircuitBoxInputOutputNode.Type> ios,
                                       IReadOnlyCollection<ushort> labels,
                                       Vector2 moveAmount)
        {
            IEnumerable<CircuitBoxComponent> nodes = Components.Where(node => ids.Contains(node.ID));
            foreach (CircuitBoxComponent node in nodes)
            {
                node.Position += moveAmount;
            }

            foreach (var label in Labels.Where(n => labels.Contains(n.ID)))
            {
                label.Position += moveAmount;
            }


            foreach (var io in InputOutputNodes)
            {
                if (!ios.Contains(io.NodeType)) { continue; }
                io.Position += moveAmount;
            }

            OnViewUpdateProjSpecific();
        }

        public override bool Select(Character character)
            => item.GetComponent<Holdable>() is not { Attached: false } && base.Select(character);

        public partial void OnViewUpdateProjSpecific();

        partial void InitProjSpecific(ContentXElement element);

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            foreach (var input in Inputs)
            {
                if (input.Connection != connection) { continue; }

                input.ReceiveSignal(signal);
                break;
            }
        }

        public static bool IsRoundRunning()
            => !Submarine.Unloading && GameMain.GameSession is { IsRunning: true };

        public static Option<CircuitBox> FindCircuitBox(ushort itemId, byte componentIndex)
        {
            if (!IsRoundRunning() || Entity.FindEntityByID(itemId) is not Item item) { return Option.None; }

            if (componentIndex >= item.Components.Count)
            {
                return Option.None;
            }

            ItemComponent targetComponent = item.Components[componentIndex];
            if (targetComponent is CircuitBox circuitBox)
            {
                return Option.Some(circuitBox);
            }

            return Option.None;
        }

        private ItemContainer? GetContainerOrNull(int index) => index >= 0 && index < containers.Length ? containers[index] : null;

        public void CreateRefundItemsForUsedResources(IReadOnlyCollection<ushort> ids, Character? character)
        {
            if (!IsInGame()) { return; }

            var prefabsToCreate = Components.Where(comp => ids.Contains(comp.ID))
                                            .Select(static comp => comp.UsedResource)
                                            .ToImmutableArray();

            foreach (ItemPrefab prefab in prefabsToCreate)
            {
                if (character?.Inventory is null)
                {
                    Entity.Spawner?.AddItemToSpawnQueue(prefab, item.Position, item.Submarine);
                }
                else
                {
                    Entity.Spawner?.AddItemToSpawnQueue(prefab, character.Inventory);
                }
            }
        }

        public static ImmutableArray<Item> GetSortedCircuitBoxItemsFromPlayer(Character? character)
            => character?.Inventory?.FindAllItems(predicate: CanItemBeAccessed, recursive: true)
                        .OrderBy(static i => i.Prefab.Identifier == Tags.FPGACircuit)
                        .ToImmutableArray() ?? ImmutableArray<Item>.Empty;

        public static bool CanItemBeAccessed(Item item) =>
            item.ParentInventory switch
            {
                ItemInventory ii => ii.Container.DrawInventory,
                _ => true
            };

        public static Option<Item> GetApplicableResourcePlayerHas(ItemPrefab prefab, Character? character)
        {
            if (character is null) { return Option.None; }

            return GetApplicableResourcePlayerHas(prefab, GetSortedCircuitBoxItemsFromPlayer(character));
        }

        public static Option<Item> GetApplicableResourcePlayerHas(ItemPrefab prefab, ImmutableArray<Item> playerItems)
        {
            foreach (var invItem in playerItems)
            {
                if (invItem.Prefab == prefab || invItem.Prefab.Identifier == Tags.FPGACircuit)
                {
                    return Option.Some(invItem);
                }
            }

            return Option.None;
        }

        public static void SpawnItem(ItemPrefab prefab, Character? user, ItemContainer? container, Action<Item> onSpawned)
        {
            if (container is null)
            {
                throw new Exception("Circuit box has no inventory");
            }

            if (IsInGame())
            {
                Entity.Spawner?.AddItemToSpawnQueue(prefab, container.Inventory, onSpawned: it =>
                {
                    AssignWifiComponentTeam(it, user);
                    onSpawned(it);
                });
                return;
            }

            Item forceSpawnedItem = new Item(prefab, Vector2.Zero, null);
            container.Inventory.TryPutItem(forceSpawnedItem, null);
            onSpawned(forceSpawnedItem);
            AssignWifiComponentTeam(forceSpawnedItem, user);

            static void AssignWifiComponentTeam(Item item, Character? user)
            {
                if (user == null) { return; }
                foreach (WifiComponent wifiComponent in item.GetComponents<WifiComponent>())
                {
                    wifiComponent.TeamID = user.TeamID;
                }
            }
        }

        public static void RemoveItem(Item item)
        {
            if (IsInGame())
            {
                Entity.Spawner?.AddItemToRemoveQueue(item);
                return;
            }

            item.Remove();
        }

        public static bool IsInGame()
            => Screen.Selected is not { IsEditor: true };

        public static bool IsCircuitBoxSelected(Character character)
            => character.SelectedItem?.GetComponent<CircuitBox>() is not null;
    }
}