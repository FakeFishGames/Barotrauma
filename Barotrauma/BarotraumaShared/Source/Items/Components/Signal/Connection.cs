using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Connection
    {
        //how many wires can be linked to a single connector
        public const int MaxLinked = 5;

        public readonly string Name;

        private Wire[] wires;
        public IEnumerable<Wire> Wires
        {
            get { return wires; }
        }

        private Item item;

        public readonly bool IsOutput;
        
        public readonly List<StatusEffect> effects;

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
                if (recipientsDirty) RefreshRecipients();
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

        public Connection(XElement element, ConnectionPanel connectionPanel)
        {

#if CLIENT
            if (connector == null)
            {
                connector = GUI.Style.GetComponentStyle("ConnectionPanelConnector").Sprites[GUIComponent.ComponentState.None][0].Sprite;
                wireVertical = GUI.Style.GetComponentStyle("ConnectionPanelWire").Sprites[GUIComponent.ComponentState.None][0].Sprite;
                connectionSprite = GUI.Style.GetComponentStyle("ConnectionPanelConnection").Sprites[GUIComponent.ComponentState.None][0].Sprite;
                connectionSpriteHighlight = GUI.Style.GetComponentStyle("ConnectionPanelConnection").Sprites[GUIComponent.ComponentState.Hover][0].Sprite;
                screwSprites = GUI.Style.GetComponentStyle("ConnectionPanelScrew").Sprites[GUIComponent.ComponentState.None].Select(s => s.Sprite).ToList();
            }
#endif
            ConnectionPanel = connectionPanel;
            item = connectionPanel.Item;

            wires = new Wire[MaxLinked];

            IsOutput = (element.Name.ToString() == "output");
            Name = element.GetAttributeString("name", (IsOutput) ? "output" : "input");

            IsPower = Name == "power_in" || Name == "power" || Name == "power_out";

            effects = new List<StatusEffect>();

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
                        if (id < 0) id = 0;
                        wireId[index] = (ushort)id;

                        break;

                    case "statuseffect":
                        effects.Add(StatusEffect.Load(subElement, item.Name + ", connection " + Name));
                        break;
                }
            }
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

        public void TryAddLink(Wire wire)
        {
            for (int i = 0; i < MaxLinked; i++)
            {
                if (wires[i] == null)
                {
                    SetWire(i, wire);
                    return;
                }
            }
        }

        public void SetWire(int index, Wire wire)
        {
            wires[index] = wire;
            recipientsDirty = true;
        }
        
        public void SendSignal(int stepsTaken, string signal, Item source, Character sender, float power, float signalStrength = 1.0f)
        {
            for (int i = 0; i < MaxLinked; i++)
            {
                if (wires[i] == null) continue;

                Connection recipient = wires[i].OtherConnection(this);
                if (recipient == null) continue;
                if (recipient.item == this.item || recipient.item == source) continue;

                if (source != null && !source.LastSentSignalRecipients.Contains(recipient.item))
                {
                    source.LastSentSignalRecipients.Add(recipient.item);
                }

                foreach (ItemComponent ic in recipient.item.components)
                {
                    ic.ReceiveSignal(stepsTaken, signal, recipient, source, sender, power, signalStrength);
                }

                bool broken = recipient.Item.Condition <= 0.0f;
                foreach (StatusEffect effect in recipient.effects)
                {
                    if (broken && effect.type != ActionType.OnBroken) continue;
                    recipient.Item.ApplyStatusEffect(effect, ActionType.OnUse, 1.0f, null, null, false, false);
                }
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
                if (wireId[i] == 0) continue;

                Item wireItem = Entity.FindEntityByID(wireId[i]) as Item;

                if (wireItem == null) continue;
                wires[i] = wireItem.GetComponent<Wire>();
                recipientsDirty = true;

                if (wires[i] != null)
                {
                    if (wires[i].Item.body != null) wires[i].Item.body.Enabled = false;
                    wires[i].Connect(this, false, false);
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