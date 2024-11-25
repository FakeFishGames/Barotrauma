using Barotrauma.Extensions;
using Barotrauma.Networking;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;

namespace Barotrauma.Items.Components
{
    partial class ButtonTerminal : ItemComponent
    {
        [Editable, Serialize(new string[0], IsPropertySaveable.Yes, description: "Signals sent when the corresponding buttons are pressed.", alwaysUseInstanceValues: true)]
        public string[] Signals { get; set; }
        
        [Editable, Serialize("", IsPropertySaveable.Yes, description: "Identifiers or tags of items that, when contained, allow the terminal buttons to be used. Multiple ones should be separated by commas.", alwaysUseInstanceValues: true)]
        public string ActivatingItems { get; set; }
        
        private readonly int requiredSignalCount;
        private ItemContainer Container { get; set; }
        private HashSet<ItemPrefab> ActivatingItemPrefabs { get; set; } = new HashSet<ItemPrefab>();

        private bool IsActivated => ActivatingItemPrefabs.None() || (Container != null && Container.Inventory.AllItems.Any(i => i != null && ActivatingItemPrefabs.Any(p => p == i.Prefab)));
        
        private readonly IReadOnlyList<string> buttonSignalDefinitions;

        public ButtonTerminal(Item item, ContentXElement element) : base(item, element)
        {
            var buttons = element.GetChildElements("TerminalButton").Where(c => c.GetAttribute("style") != null);
            if (buttons.None())
            {
                DebugConsole.ThrowError($"Error in item \"{item.Name}\": no TerminalButton elements with a style defined for the ButtonTerminal component!", contentPackage: element.ContentPackage);
            }
            requiredSignalCount = buttons.Count();
            List<string> buttonSignals = new ();
            foreach (ContentXElement button in buttons)
            {
                buttonSignals.Add(button.GetAttributeString("signal", null));
            }
            buttonSignalDefinitions = buttonSignals.ToImmutableList();
            InitProjSpecific(element);
        }

        partial void InitProjSpecific(ContentXElement element);

        public override void OnItemLoaded()
        {
            base.OnItemLoaded();
            
            LoadSignals();
            LoadActivatingItems();
            
            var containers = item.GetComponents<ItemContainer>();
            if (containers.Count() != 1)
            {
                DebugConsole.ThrowError($"Error in item \"{item.Name}\": the ButtonTerminal component requires exactly one ItemContainer component!");
                return;
            }
            Container = containers.FirstOrDefault();

            OnItemLoadedProjSpecific();
            // Set active so that update loop is active and we can send the state_out signal.
            IsActive = true;
        }

        partial void OnItemLoadedProjSpecific();
        
        public override void Update(float deltaTime, Camera cam)
        {
            base.Update(deltaTime, cam);
            item.SendSignal(IsActivated ? "1" : "0", "state_out");
        }
        
        private void LoadSignals()
        {
            if (Signals == null || Signals.None())
            {
                Signals = new string[requiredSignalCount];
                for (int i = 0; i < requiredSignalCount; i++)
                {
                    Signals[i] = string.Empty;
                }
                // Load signals from the button elements, if defined.
                for (int i = 0; i < buttonSignalDefinitions.Count; i++)
                {
                    Debug.Assert(Signals.Length > i);
                    string overrideDefinition = buttonSignalDefinitions[i];
                    if (overrideDefinition != null)
                    {
                        Signals[i] = overrideDefinition;
                    }
                }
            }
            else if (Signals.Length != requiredSignalCount)
            {
                string[] newSignals = new string[requiredSignalCount];
                if (Signals.Length < requiredSignalCount)
                {
                    Signals.CopyTo(newSignals, 0);
                    for (int i = Signals.Length; i < requiredSignalCount; i++)
                    {
                        newSignals[i] = string.Empty;
                    }
                }
                else
                {
                    for (int i = 0; i < requiredSignalCount; i++)
                    {
                        newSignals[i] = Signals[i];
                    }
                }
                Signals = newSignals;
            }
        }
        
        private void LoadActivatingItems()
        {
            ActivatingItemPrefabs.Clear();
            if (!string.IsNullOrEmpty(ActivatingItems))
            {
                foreach (string activatingItem in ActivatingItems.Split(','))
                {
                    Identifier itemIdentifier = activatingItem.ToIdentifier();
                    if (MapEntityPrefab.FindByIdentifier(itemIdentifier) is ItemPrefab prefab)
                    {
                        ActivatingItemPrefabs.Add(prefab);
                    }
                    else
                    {
                        ItemPrefab.Prefabs.Where(p => p.Tags.Any(t => t == itemIdentifier))
                            .ForEach(p => ActivatingItemPrefabs.Add(p));
                    }
                }
                if (ActivatingItemPrefabs.None())
                {
                    DebugConsole.ThrowError($"Error in item \"{item.Name}\": no activating item prefabs found with identifiers or tags \"{ActivatingItems}\"");
                }
            }
        }
        
        public override void Reset()
        {
            base.Reset();
            Signals = null;
            LoadSignals();
            LoadActivatingItems();
        }
        
        private bool SendSignal(int signalIndex, Character sender, bool ignoreState = false, string overrideSignal = null)
        {
            if (!ignoreState && !IsActivated) { return false; }
            string signal = overrideSignal ?? Signals[signalIndex];
            string connectionName = $"signal_out{signalIndex + 1}";
            item.SendSignal(new Signal(signal, sender: sender), connectionName);
            AchievementManager.OnButtonTerminalSignal(item, sender);
            return true;
        }

        private readonly struct EventData : IEventData
        {
            public readonly int SignalIndex;
            
            public EventData(int signalIndex)
            {
                SignalIndex = signalIndex;
            }
        }
        
        public override bool ValidateEventData(NetEntityEvent.IData data)
            => TryExtractEventData<EventData>(data, out _);

        private void Write(IWriteMessage msg, NetEntityEvent.IData extraData)
        {
            var eventData = ExtractEventData<EventData>(extraData);
            msg.WriteRangedInteger(eventData.SignalIndex, 0, Signals.Length - 1);
        }
    }
}