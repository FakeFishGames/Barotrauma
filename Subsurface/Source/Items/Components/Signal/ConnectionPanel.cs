using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class ConnectionPanel : ItemComponent
    {
        public static Wire HighlightedWire;

        public List<Connection> Connections;

        Character user;

        public ConnectionPanel(Item item, XElement element)
            : base(item, element)
        {
            Connections = new List<Connection>();

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString())
                {
                    case "input":                        
                        Connections.Add(new Connection(subElement, item));
                        break;
                    case "output":
                        Connections.Add(new Connection(subElement, item));
                        break;
                }
            }

            IsActive = true;
        }

        public override void UpdateHUD(Character character)
        {
            if (character != Character.Controlled || character != user) return;

            if (Screen.Selected != GameMain.EditMapScreen &&
                character.IsKeyHit(InputType.Select) &&
                character.SelectedConstruction == this.item) character.SelectedConstruction = null;

            if (HighlightedWire != null)
            {
                HighlightedWire.Item.IsHighlighted = true;
                if (HighlightedWire.Connections[0] != null && HighlightedWire.Connections[0].Item != null) HighlightedWire.Connections[0].Item.IsHighlighted = true;
                if (HighlightedWire.Connections[1] != null && HighlightedWire.Connections[1].Item != null) HighlightedWire.Connections[1].Item.IsHighlighted = true;
            }
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            if (character != Character.Controlled || character != user) return;

            HighlightedWire = null;
            Connection.DrawConnections(spriteBatch, this, character);
            
        }

        public override XElement Save(XElement parentElement)
        {
            XElement componentElement = base.Save(parentElement);

            foreach (Connection c in Connections)
            {
                c.Save(componentElement);
            }

            return componentElement;
        }

        public override void OnMapLoaded()
        {
            foreach (Connection c in Connections)
            {
                c.ConnectLinked();
            }
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (user != null && user.SelectedConstruction != item) user = null;
        }

        public override bool Select(Character picker)
        {
            user = picker;
            IsActive = true;
            return true;
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            if (character == null || character!=user) return false;

            var powered = item.GetComponent<Powered>();
            if (powered != null)
            {
                if (powered.Voltage < 0.1f) return false;
            }

            float degreeOfSuccess = DegreeOfSuccess(character);
            if (Rand.Range(0.0f, 50.0f) < degreeOfSuccess) return false;

            character.StartStun(5.0f);

            item.ApplyStatusEffects(ActionType.OnFailure, 1.0f, character);

            return true;
        }

        public override void Load(XElement element)
        {
            base.Load(element);
                        
            List<Connection> loadedConnections = new List<Connection>();

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString())
                {
                    case "input":
                        loadedConnections.Add(new Connection(subElement, item));
                        break;
                    case "output":
                        loadedConnections.Add(new Connection(subElement, item));
                        break;
                }
            }
            
            for (int i = 0; i<loadedConnections.Count && i<Connections.Count; i++)
            {
                loadedConnections[i].wireId.CopyTo(Connections[i].wireId, 0);
            }
        }

        protected override void RemoveComponentSpecific()
        {
            foreach (Connection c in Connections)
            {
                foreach (Wire wire in c.Wires)
                {
                    if (wire == null) continue;

                    if (wire.OtherConnection(c) == null) //wire not connected to anything else
                    {
                        wire.Item.Drop(null);
                    }
                    else
                    {
                        wire.RemoveConnection(item);
                    }
                }
            }
        }

        protected override void ShallowRemoveComponentSpecific()
        {
        }
        
    }
}
