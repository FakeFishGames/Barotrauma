using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Barotrauma.Plugins;
using System.Xml.Linq;
using Barotrauma.Extensions;

namespace HomeAndHarbor
{
    public abstract class ResourceGeneratorBaseComponent : ItemComponent, IClientSerializable, IServerSerializable
    {
        // Previously unique ids were saved under the attribute "UniqueId" which happened to also get saved in the
        // sub editor, causing a bunch of items to have the unique id be baked into the sub save. Use a different
        // attribute name to avoid loading these old ids
        private const string UniqueIdAttributeName = "GeneratorId";

        public static readonly List<ResourceGeneratorBaseComponent> ResourceGeneratorComponents = [];

        /// <summary>
        /// Campaign-persisted last unique id that was assigned.
        /// </summary>
        /// <remarks>
        /// Returns 0 outside of campaign.
        /// </remarks>
        internal static int LastAssignedUniqueId
        {
            get => (GameMain.GameSession?.Campaign?.TryGetExtraField(nameof(LastAssignedUniqueId), out int value) ?? false) ? value : 0;
            set => GameMain.GameSession?.Campaign?.SetExtraField(nameof(LastAssignedUniqueId), value);
        }

        private int uniqueId;

        /// <summary>
        /// Stable unique ID for this workstation, used to correlate it with saved dweller assignments regardless of the outpost being loaded or not.
        /// Copied once from the item's entity ID on first creation, then persisted so that it survives potential entity ID remaps.
        /// </summary>
        public int UniqueId
        {
            get => uniqueId;
            private set
            {
                uniqueId = value;
                if (backingResourceGenerator != null)
                {
                    backingResourceGenerator.UniqueId = value;
                }
            }
        }

        private Identifier logTextTag;
        [Serialize("", IsPropertySaveable.No, description: "Optional custom localization tag to use when writing resource changes to the economy log.")]
        public Identifier LogTextTag
        {
            get => logTextTag;
            private set
            {
                logTextTag = value;
                if (backingResourceGenerator == null) { return; }
                backingResourceGenerator.LogTextTag = value;
            }
        }

        [Serialize("", IsPropertySaveable.Yes, "Identifier that is used to show certain UI elements when present.")]
        public Identifier ShowOnInterfaceIdentifier { get; private set; }

        [Serialize("", IsPropertySaveable.Yes, "Used to save the disabled conversions from the ResourceGenerator to the XML of the item.")]
        public string DisabledConversions
        {
            get => GetConversionHashesString(ResourceGenerator.ConversionState.Disabled);
            set => SetConversionHashesFromString(ResourceGenerator.ConversionState.Disabled, value);
        }

        [Serialize("", IsPropertySaveable.Yes, "Used to save the explicitly enabled conversions from the ResourceGenerator to the XML of the item.")]
        public string EnabledConversions
        {
            get => GetConversionHashesString(ResourceGenerator.ConversionState.Enabled);
            set => SetConversionHashesFromString(ResourceGenerator.ConversionState.Enabled, value);
        }

        private string GetConversionHashesString(ResourceGenerator.ConversionState state)
            => string.Join(',', ResourceGenerator.GetConversionHashes(state));

        private void SetConversionHashesFromString(ResourceGenerator.ConversionState state, string value)
        {
            List<uint> hashes = [];
            foreach (string s in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (uint.TryParse(s, out uint result))
                {
                    hashes.Add(result);
                }
            }
            ResourceGenerator.SetConversionHashes(state, hashes);
        }

        [Serialize("", IsPropertySaveable.No, "When set the resource generator will be shown in the resource UI under the specified resource's category.")]
        public Identifier ResourceUIIdentifier { get; set; }

        private ResourceGenerator? backingResourceGenerator;

        internal ResourceGenerator ResourceGenerator
        {
            get
            {
                if (backingResourceGenerator != null)
                {
                    return backingResourceGenerator;
                }
                var outpostData =
                    GameMain.GameSession?.Map?.CurrentLocation is Location currentLocation ?
                        PersistentOutpostData.GetPersistentOutpostData(currentLocation) : null;
                backingResourceGenerator = new ResourceGenerator(element: null, parentComponent: this, resourceManager: outpostData?.ResourceManager, parentDebugName: item.Name)
                {
                    UniqueId = uniqueId,
                    LogTextTag = logTextTag,
                    IsWorkstation = this is WorkstationComponent,
                    ShowOnInterfaceIdentifier = ShowOnInterfaceIdentifier
                };
                return backingResourceGenerator;
            }
            private set
            {
                backingResourceGenerator = value;
                backingResourceGenerator.UniqueId = uniqueId; // Always override this
                backingResourceGenerator.LogTextTag = logTextTag;
                backingResourceGenerator.IsWorkstation = this is WorkstationComponent;
                backingResourceGenerator.ShowOnInterfaceIdentifier = ShowOnInterfaceIdentifier;
            }
        }

        public ResourceGeneratorBaseComponent(Item item, ContentXElement element) : base(item, element)
        {
            foreach (ContentXElement subElement in element.Elements())
            {
                if (subElement.NameAsIdentifier() == nameof(HomeAndHarbor.ResourceGenerator))
                {
                    var outpostData =
                        GameMain.GameSession?.Map?.CurrentLocation is Location currentLocation ?
                            PersistentOutpostData.GetPersistentOutpostData(currentLocation) : null;
                    ResourceGenerator = new ResourceGenerator(subElement, parentComponent: this, resourceManager: outpostData?.ResourceManager, parentDebugName: item.Name);
                }
            }

            ResourceGeneratorComponents.Add(this);
        }

        public static ResourceGeneratorBaseComponent? FindResourceGeneratorWithId(int uniqueId)
            => ResourceGeneratorComponents.FirstOrDefault(w => w.UniqueId == uniqueId);

        public override void OnItemLoaded()
        {
            base.OnItemLoaded();

            if (UniqueId == 0)
            {
                // Items spawned during a round don't have Load() called, so we need to assign their unique ID here
                AssignUniqueId();
            }

#if SERVER
            // Make sure the clients know about the state of the resource generator
            CoroutineManager.Invoke(() =>
            {
                if (item.FullyInitialized && !item.Removed) 
                { 
                    item.CreateServerEvent(this); 
                }
            }, delay: 0.1f);
#endif
        }

        public override void Load(ContentXElement componentElement, bool usePrefabValues, IdRemap idRemap, bool isItemSwap)
        {
            base.Load(componentElement, usePrefabValues, idRemap, isItemSwap);

            UniqueId = componentElement.GetAttributeInt(UniqueIdAttributeName, 0);

            if (UniqueId == 0)
            {
                AssignUniqueId();
            }
        }

        public override XElement Save(XElement parentElement)
        {
            var element = base.Save(parentElement);

            // Only save unique IDs during gameplay, not in the sub editor
            if (Screen.Selected is not { IsEditor: true })
            {
                element.SetAttributeValue(UniqueIdAttributeName, UniqueId);
            }

            return element;
        }

        /// <summary>
        /// Recheck & refresh whether the resource generator is currently active. Also handles syncing the state to clients if needed.
        /// </summary>
        public void RefreshIsActive(bool forceNetworkEvent = false)
        {
            if (GameMain.NetworkMember is { IsClient: true }) { return; }

            bool shouldBeActive = ShouldResourceGeneratorBeActive();
#if SERVER
            if (shouldBeActive != ResourceGenerator.IsActive || forceNetworkEvent)
            {
                item.CreateServerEvent(this);
            }
#endif
            ResourceGenerator.IsActive = shouldBeActive;
        }

        protected virtual bool ShouldResourceGeneratorBeActive()
        {
            return ResourceGenerator.IsActive;
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            ResourceGeneratorComponents.Remove(this);
        }


#if CLIENT
        public void ClientEventWrite(IWriteMessage msg, NetEntityEvent.IData? extraData = null)
            => SharedEventWrite(msg);

        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            UniqueId = msg.ReadInt32();

            bool wasPreviouslyActive = ResourceGenerator.IsActive;
            ResourceGenerator.IsActive = msg.ReadBoolean();
            SharedEventRead(msg);
            OutpostManagementUI.Instance?.OutpostResourcesUI?.UpdateConversions(this);
            if (wasPreviouslyActive != ResourceGenerator.IsActive)
            {
                OutpostManagementUI.Instance?.TechnologyUIs.ForEach(techUi => techUi.RecreateFramesAndTierLabels());
            }
        }

#elif SERVER
        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData? extraData = null)
        {
            msg.WriteInt32(UniqueId);
            msg.WriteBoolean(ResourceGenerator.IsActive);
            SharedEventWrite(msg);
        }

        public void ServerEventRead(IReadMessage msg, Client c)
        {
            SharedEventRead(msg);
            item.CreateServerEvent(this);
        }
#endif


        private void AssignUniqueId()
        {
            if (GameMain.NetworkMember is not { IsClient: true })
            {
                LastAssignedUniqueId++;
                UniqueId = LastAssignedUniqueId;
            }
        }

        private void SharedEventRead(IReadMessage msg)
        {
            ReadConversionHashes(ResourceGenerator.ConversionState.Enabled);
            ReadConversionHashes(ResourceGenerator.ConversionState.Disabled);

            void ReadConversionHashes(ResourceGenerator.ConversionState state)
            {
                uint count = msg.ReadVariableUInt32();
                List<uint> hashes = [];
                for (int i = 0; i < count; i++)
                {
                    hashes.Add(msg.ReadUInt32());
                }
                ResourceGenerator.SetConversionHashes(state, hashes);
            }
        }

        private void SharedEventWrite(IWriteMessage msg)
        {
            WriteConversionHashes(ResourceGenerator.ConversionState.Enabled);
            WriteConversionHashes(ResourceGenerator.ConversionState.Disabled);

            void WriteConversionHashes(ResourceGenerator.ConversionState state)
            {
                List<uint> hashes = ResourceGenerator.GetConversionHashes(state).ToList();
                msg.WriteVariableUInt32((uint)hashes.Count);
                foreach (uint hash in hashes)
                {
                    msg.WriteUInt32(hash);
                }
            }
        }
    }
}