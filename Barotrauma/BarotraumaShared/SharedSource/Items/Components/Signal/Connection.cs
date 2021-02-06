using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Connection
    {
        //how many wires can be linked to a single connector
        public const int MaxLinked = 5;

        public readonly string Name;
        public readonly string DisplayName;

        private Wire[] wires;
        public IEnumerable<Wire> Wires
        {
            get { return wires; }
        }

        private Item item;

        public readonly bool IsOutput;
        
        public readonly List<StatusEffect> Effects;

        public readonly ushort[] wireId;

        public bool IsPower
        {
            get;
            private set;
        }

        private bool recipientsDirty = true;
        private List<Connection> recipients = new List<Connection>();
        public List<Connection> Recipients
        {
            get
            {
                if (recipientsDirty) { RefreshRecipients(); }
                return recipients;
            }
        }

        public Item Item
        {
            get { return item; }
        }

        public ConnectionPanel ConnectionPanel
        {
            get;
            private set;
        }

        public override string ToString()
        {
            return "Connection (" + item.Name + ", " + Name + ")";
        }

        public Connection(XElement element, ConnectionPanel connectionPanel, IdRemap idRemap)
        {

#if CLIENT
            if (connector == null)
            {
                connector = GUI.Style.GetComponentStyle("ConnectionPanelConnector").GetDefaultSprite();
                wireVertical = GUI.Style.GetComponentStyle("ConnectionPanelWire").GetDefaultSprite();
                connectionSprite = GUI.Style.GetComponentStyle("ConnectionPanelConnection").GetDefaultSprite();
                connectionSpriteHighlight = GUI.Style.GetComponentStyle("ConnectionPanelConnection").GetSprite(GUIComponent.ComponentState.Hover);
                screwSprites = GUI.Style.GetComponentStyle("ConnectionPanelScrew").Sprites[GUIComponent.ComponentState.None].Select(s => s.Sprite).ToList();
            }
#endif
            ConnectionPanel = connectionPanel;
            item = connectionPanel.Item;

            wires = new Wire[MaxLinked];

            IsOutput = element.Name.ToString() == "output";
            Name = element.GetAttributeString("name", IsOutput ? "output" : "input");

            string displayNameTag = "", fallbackTag = "";
            //if displayname is not present, attempt to find it from the prefab
            if (element.Attribute("displayname") == null)
            {
                foreach (XElement subElement in item.Prefab.ConfigElement.Elements())
                {
                    if (!subElement.Name.ToString().Equals("connectionpanel", StringComparison.OrdinalIgnoreCase)) { continue; }
                    
                    foreach (XElement connectionElement in subElement.Elements())
                    {
                        string prefabConnectionName = element.GetAttributeString("name", null);
                        if (prefabConnectionName == Name)
                        {
                            displayNameTag = connectionElement.GetAttributeString("displayname", "");
                            fallbackTag = connectionElement.GetAttributeString("fallbackdisplayname", "");
                        }
                    }
                }
            }
            else
            {
                displayNameTag = element.GetAttributeString("displayname", "");
                fallbackTag = element.GetAttributeString("fallbackdisplayname", null);
            }

            if (!string.IsNullOrEmpty(displayNameTag))
            {
                //extract the tag parts in case the tags contains variables
                string tagWithoutVariables = displayNameTag?.Split('~')?.FirstOrDefault();
                string fallbackTagWithoutVariables = fallbackTag?.Split('~')?.FirstOrDefault();
                //use displayNameTag if found, otherwise fallBack
                if (TextManager.ContainsTag(tagWithoutVariables))
                {
                    DisplayName = TextManager.GetServerMessage(displayNameTag);
                }
                else if (TextManager.ContainsTag(fallbackTagWithoutVariables))
                {
                    DisplayName = TextManager.GetServerMessage(fallbackTag);
                }
            }

            if (string.IsNullOrEmpty(DisplayName))
            {
#if DEBUG
                DebugConsole.ThrowError("Missing display name in connection " + item.Name + ": " + Name);
#endif
                DisplayName = Name;
            }

            IsPower = Name == "power_in" || Name == "power" || Name == "power_out";

            Effects = new List<StatusEffect>();

            wireId = new ushort[MaxLinked];

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "link":
                        int index = -1;
                        for (int i = 0; i < MaxLinked; i++)
                        {
                            if (wireId[i] < 1) index = i;
                        }
                        if (index == -1) break;

                        int id = subElement.GetAttributeInt("w", 0);
                        if (id < 0)
                        {
                            id = 0;
                        }
                        wireId[index] = idRemap.GetOffsetId(id);

                        break;

                    case "statuseffect":
                        Effects.Add(StatusEffect.Load(subElement, item.Name + ", connection " + Name));
                        break;
                }
            }
        }

        public void SetRecipientsDirty()
        {
            recipientsDirty = true;
        }

        private void RefreshRecipients()
        {
            recipients.Clear();
            for (int i = 0; i < MaxLinked; i++)
            {
                if (wires[i] == null) continue;
                Connection recipient = wires[i].OtherConnection(this);
                if (recipient != null) recipients.Add(recipient);
            }
            recipientsDirty = false;
        }

        public int FindEmptyIndex()
        {
            for (int i = 0; i < MaxLinked; i++)
            {
                if (wires[i] == null) return i;
            }
            return -1;
        }

        public int FindWireIndex(Wire wire)
        {
            for (int i = 0; i < MaxLinked; i++)
            {
                if (wires[i] == wire) return i;
            }
            return -1;
        }

        public int FindWireIndex(Item wireItem)
        {
            for (int i = 0; i < MaxLinked; i++)
            {
                if (wires[i] == null && wireItem == null) return i;
                if (wires[i] != null && wires[i].Item == wireItem) return i;
            }
            return -1;
        }

        public bool TryAddLink(Wire wire)
        {
            for (int i = 0; i < MaxLinked; i++)
            {
                if (wires[i] == null)
                {
                    SetWire(i, wire);
                    return true;
                }
            }
            return false;
        }

        public void SetWire(int index, Wire wire)
        {
            Wire previousWire = wires[index];
            if (wire != previousWire && previousWire != null)
            {
                var otherConnection = previousWire.OtherConnection(this);
                if (otherConnection != null)
                {
                    otherConnection.recipientsDirty = true;
                }
            }

            wires[index] = wire;
            recipientsDirty = true;
            if (wire != null)
            {
                ConnectionPanel.DisconnectedWires.Remove(wire);
                var otherConnection = wire.OtherConnection(this);
                if (otherConnection != null)
                {
                    otherConnection.recipientsDirty = true;
                }
            }
        }

        public void SendSignal([NotNull] Signal signal)
        {
            for (int i = 0; i < MaxLinked; i++)
            {
                if (wires[i] == null) { continue; }

                Connection recipient = wires[i].OtherConnection(this);
                if (recipient == null) { continue; }
                if (recipient.item == this.item || recipient.item == signal.source) { continue; }

                signal.source?.LastSentSignalRecipients.Add(recipient.item);

                signal.connection = recipient;

                foreach (ItemComponent ic in recipient.item.Components)
                {
                    ic.ReceiveSignal(signal);
                }

                foreach (StatusEffect effect in recipient.Effects)
                {
                    recipient.Item.ApplyStatusEffect(effect, ActionType.OnUse, (float)Timing.Step);
                }
            }
        }
        
        public void SendSignal(int stepsTaken, string signal, Item source, Character sender, float power, float signalStrength = 1.0f)
        {
            SendSignal(new Signal(signal, stepsTaken, null, sender, source, power, signalStrength));
        }

        public void SendPowerProbeSignal(Item source, float power)
        {
            for (int i = 0; i < MaxLinked; i++)
            {
                if (wires[i] == null) { continue; }

                Connection recipient = wires[i].OtherConnection(this);
                if (recipient == null || !recipient.IsPower) { continue; }

                recipient.item.GetComponent<Powered>()?.ReceivePowerProbeSignal(recipient, source, power);
            }
        }
        public void ClearConnections()
        {
            for (int i = 0; i < MaxLinked; i++)
            {
                if (wires[i] == null) continue;

                wires[i].RemoveConnection(this);
                wires[i] = null;
                recipientsDirty = true;
            }
        }
        
        public void ConnectLinked()
        {
            if (wireId == null) return;
            
            for (int i = 0; i < MaxLinked; i++)
            {
                if (wireId[i] == 0) { continue; }

                if (!(Entity.FindEntityByID(wireId[i]) is Item wireItem)) { continue; }
                wires[i] = wireItem.GetComponent<Wire>();
                recipientsDirty = true;

                if (wires[i] != null)
                {
                    if (wires[i].Item.body != null) wires[i].Item.body.Enabled = false;
                    wires[i].Connect(this, false, false);
                    wires[i].FixNodeEnds();
                }
            }
        }


        public void Save(XElement parentElement)
        {
            XElement newElement = new XElement(IsOutput ? "output" : "input", new XAttribute("name", Name));

            Array.Sort(wires, delegate (Wire wire1, Wire wire2)
            {
                if (wire1 == null) return 1;
                if (wire2 == null) return -1;
                return wire1.Item.ID.CompareTo(wire2.Item.ID);
            });

            for (int i = 0; i < MaxLinked; i++)
            {
                if (wires[i] == null) continue;
                
                newElement.Add(new XElement("link",
                    new XAttribute("w", wires[i].Item.ID.ToString())));
            }

            parentElement.Add(newElement);
        }
    }
}