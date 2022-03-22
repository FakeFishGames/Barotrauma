using Barotrauma.Extensions;
using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class ButtonTerminal : ItemComponent
    {
        [Editable, Serialize(new string[0], true, description: "Signals sent when the corresponding buttons are pressed.", alwaysUseInstanceValues: true)]
        public string[] Signals { get; set; }
        [Editable, Serialize("", true, description: "Identifiers or tags of items that, when contained, allow the terminal buttons to be used. Multiple ones should be separated by commas.", alwaysUseInstanceValues: true)]
        public string ActivatingItems { get; set; }

        private int RequiredSignalCount { get; set; }
        private ItemContainer Container { get; set; }
        private HashSet<ItemPrefab> ActivatingItemPrefabs { get; set; } = new HashSet<ItemPrefab>();


        private bool AllowUsingButtons => ActivatingItemPrefabs.None() || (Container != null && Container.Inventory.AllItems.Any(i => i != null && ActivatingItemPrefabs.Any(p => p == i.Prefab)));

        public ButtonTerminal(Item item, XElement element) : base(item, element)
        {
            RequiredSignalCount = element.GetChildElements("TerminalButton").Count(c => c.GetAttribute("style") != null);
            if (RequiredSignalCount < 1)
            {
                DebugConsole.ThrowError($"Error in item \"{item.Name}\": no TerminalButton elements defined for the ButtonTerminal component!");
            }
            InitProjSpecific(element);
        }

        partial void InitProjSpecific(XElement element);

        public override void OnItemLoaded()
        {
            base.OnItemLoaded();

            if (Signals == null)
            {
                Signals = new string[RequiredSignalCount];
                for (int i = 0; i < RequiredSignalCount; i++)
                {
                    Signals[i] = string.Empty;
                }
            }
            else if (Signals.Length != RequiredSignalCount)
            {
                string[] newSignals = new string[RequiredSignalCount];
                if (Signals.Length < RequiredSignalCount)
                {
                    Signals.CopyTo(newSignals, 0);
                    for (int i = Signals.Length; i < RequiredSignalCount; i++)
                    {
                        newSignals[i] = string.Empty;
                    }
                }
                else
                {
                    for (int i = 0; i < RequiredSignalCount; i++)
                    {
                        newSignals[i] = Signals[i];
                    }
                }
                Signals = newSignals;
            }

            ActivatingItemPrefabs.Clear();
            if (!string.IsNullOrEmpty(ActivatingItems))
            {
                foreach (var activatingItem in ActivatingItems.Split(','))
                {
                    if (MapEntityPrefab.Find(null, identifier: activatingItem, showErrorMessages: false) is ItemPrefab prefab)
                    {
                        ActivatingItemPrefabs.Add(prefab);
                    }
                    else
                    {
                        ItemPrefab.Prefabs.Where(p => p.Tags.Any(t => t.Equals(activatingItem, StringComparison.OrdinalIgnoreCase)))
                            .ForEach(p => ActivatingItemPrefabs.Add(p));
                    }
                }
                if (ActivatingItemPrefabs.None())
                {
                    DebugConsole.ThrowError($"Error in item \"{item.Name}\": no activating item prefabs found with identifiers or tags \"{ActivatingItems}\"");
                }
            }

            var containers = item.GetComponents<ItemContainer>();
            if (containers.Count() != 1)
            {
                DebugConsole.ThrowError($"Error in item \"{item.Name}\": the ButtonTerminal component requires exactly one ItemContainer component!");
                return;
            }
            Container = containers.FirstOrDefault();

            OnItemLoadedProjSpecific();
        }

        partial void OnItemLoadedProjSpecific();

        private bool SendSignal(int signalIndex, Character sender, bool isServerMessage = false)
        {
            if (!isServerMessage && !AllowUsingButtons) { return false; }
            string signal = Signals[signalIndex];
            string connectionName = $"signal_out{signalIndex + 1}";
            item.SendSignal(new Signal(signal, sender: sender), connectionName);
            return true;
        }

        private void Write(IWriteMessage msg, object[] extraData)
        {
            if (extraData == null || extraData.Length < 3) { return; }
            msg.WriteRangedInteger((int)extraData[2], 0, Signals.Length - 1);
        }
    }
}