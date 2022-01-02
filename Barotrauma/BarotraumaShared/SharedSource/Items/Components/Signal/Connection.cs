using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Connection
    {
        //how many wires can be linked to connectors by default
        private const int DefaultMaxWires = 5;

        //how many wires a player can link to this connection
        public readonly int MaxPlayerConnectableWires = 5;

        //how many wires can be linked to this connection in total
        public readonly int MaxWires = 5;

        public readonly string Name;
        public readonly string DisplayName;

        private readonly Wire[] wires;
        public IEnumerable<Wire> Wires
        {
            get { return wires; }
        }

        private Item item;

        public readonly bool IsOutput;
        
        public readonly List<StatusEffect> Effects;

        public readonly ushort[] wireId;

        //The grid the connection is a part of
        public GridInfo Grid;

        //Priority in which power outputted will be handled - load is unaffected
        public int priority = (int)PowerPriority.Default;

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

            MaxWires = element.GetAttributeInt("maxwires", DefaultMaxWires);
            MaxWires = Math.Max(element.Elements().Count(e => e.Name.ToString().Equals("link", StringComparison.OrdinalIgnoreCase)), MaxWires);

            MaxPlayerConnectableWires = element.GetAttributeInt("maxplayerconnectablewires", MaxWires);
            wires = new Wire[MaxWires];

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
                        string prefabConnectionName = connectionElement.GetAttributeString("name", null);
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

            wireId = new ushort[MaxWires];

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "link":
                        int index = -1;
                        for (int i = 0; i < MaxWires; i++)
                        {
                            if (wireId[i] < 1) { index = i; }
                        }
                        if (index == -1) { break; }

                        int id = subElement.GetAttributeInt("w", 0);
                        if (id < 0) { id = 0; }
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
            for (int i = 0; i < MaxWires; i++)
            {
                if (wires[i] == null) continue;
                Connection recipient = wires[i].OtherConnection(this);
                if (recipient != null) recipients.Add(recipient);
            }
            recipientsDirty = false;
        }

        public int FindEmptyIndex()
        {
            for (int i = 0; i < MaxWires; i++)
            {
                if (wires[i] == null) return i;
            }
            return -1;
        }

        public int FindWireIndex(Wire wire)
        {
            for (int i = 0; i < MaxWires; i++)
            {
                if (wires[i] == wire) return i;
            }
            return -1;
        }

        public int FindWireIndex(Item wireItem)
        {
            for (int i = 0; i < MaxWires; i++)
            {
                if (wires[i] == null && wireItem == null) return i;
                if (wires[i] != null && wires[i].Item == wireItem) return i;
            }
            return -1;
        }

        public bool TryAddLink(Wire wire)
        {
            for (int i = 0; i < MaxWires; i++)
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
                    //Change the connection grids or flag them for updating
                    if (IsPower && otherConnection.IsPower && Grid != null)
                    {
                        //Check if both connections belong to a larger grid
                        if (otherConnection.recipients.Count > 1 && recipients.Count > 1)
                        {
                            Powered.ChangedConnections.Add(otherConnection);
                            Powered.ChangedConnections.Add(this);
                        }
                        else if (recipients.Count > 1)
                        {
                            //This wire was the only one at the other grid
                            otherConnection.Grid?.RemoveConnection(otherConnection);
                            otherConnection.Grid = null;
                        }
                        else if (otherConnection.recipients.Count > 1)
                        {
                            Grid?.RemoveConnection(this);
                            Grid = null;
                        }
                        else if (Grid.Connections.Count == 2)
                        {
                            //Delete the grid as these were the only 2 devices
                            Powered.Grids.Remove(Grid.ID);
                            Grid = null;
                            otherConnection.Grid = null;
                        }
                    }
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
                    //Set the other connection grid if a grid exists already
                    if (IsPower && otherConnection.IsPower)
                    {
                        if (Grid == null && otherConnection.Grid != null)
                        {
                            otherConnection.Grid.AddConnection(this);
                            Grid = otherConnection.Grid;
                        }
                        else if (Grid != null && otherConnection.Grid == null)
                        {
                            Grid.AddConnection(otherConnection);
                            otherConnection.Grid = Grid;
                        }
                        else
                        {
                            //Flag change so that proper grids can be formed
                            Powered.ChangedConnections.Add(this);
                            Powered.ChangedConnections.Add(otherConnection);
                        }
                    }

                    otherConnection.recipientsDirty = true;
                }
            }
        }

        public void SendSignal(Signal signal)
        {
            for (int i = 0; i < MaxWires; i++)
            {
                if (wires[i] == null) { continue; }

                Connection recipient = wires[i].OtherConnection(this);
                if (recipient == null) { continue; }
                if (recipient.item == this.item || signal.source?.LastSentSignalRecipients.LastOrDefault() == recipient) { continue; }

                signal.source?.LastSentSignalRecipients.Add(recipient);

                Connection connection = recipient;

                foreach (ItemComponent ic in recipient.item.Components)
                {
                    ic.ReceiveSignal(signal, connection);
                }

                if (signal.value != "0")
                {
                    foreach (StatusEffect effect in recipient.Effects)
                    {
                        recipient.Item.ApplyStatusEffect(effect, ActionType.OnUse, (float)Timing.Step);
                    }
                }
            }
        }
        
        public void ClearConnections()
        {
            if (IsPower && Grid != null)
            {
                Powered.ChangedConnections.Add(this);
                foreach (Connection c in recipients)
                {
                    Powered.ChangedConnections.Add(c);
                }
            }

            for (int i = 0; i < MaxWires; i++)
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
            
            for (int i = 0; i < MaxWires; i++)
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

            for (int i = 0; i < MaxWires; i++)
            {
                if (wires[i] == null) continue;
                
                newElement.Add(new XElement("link",
                    new XAttribute("w", wires[i].Item.ID.ToString())));
            }

            parentElement.Add(newElement);
        }
    }
}