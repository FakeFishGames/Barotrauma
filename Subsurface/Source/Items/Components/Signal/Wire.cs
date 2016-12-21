using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class Wire : ItemComponent, IDrawableComponent
    {
        class WireSection
        {
            private Vector2 start;

            private float angle;
            private float length;

            public WireSection(Vector2 start, Vector2 end)
            {
                this.start = start;

                angle = MathUtils.VectorToAngle(end - start);
                length = Vector2.Distance(start, end);
            }

            public void Draw(SpriteBatch spriteBatch, Color color, Vector2 offset, float depth, float width = 0.3f)
            {
                spriteBatch.Draw(wireSprite.Texture,
                    new Vector2(start.X+offset.X, -(start.Y+offset.Y)), null, color,
                    -angle,
                    new Vector2(0.0f, wireSprite.size.Y / 2.0f),
                    new Vector2(length / wireSprite.Texture.Width, width),
                    SpriteEffects.None,
                    depth);
            }

            public static void Draw(SpriteBatch spriteBatch, Vector2 start, Vector2 end, Color color, float depth, float width = 0.3f)
            {
                start.Y = -start.Y;
                end.Y = -end.Y;

                spriteBatch.Draw(wireSprite.Texture,
                    start, null, color,
                    MathUtils.VectorToAngle(end - start),
                    new Vector2(0.0f, wireSprite.size.Y / 2.0f),
                    new Vector2((Vector2.Distance(start, end)) / wireSprite.Texture.Width, width),
                    SpriteEffects.None,
                    depth);
            }
        }

        const float nodeDistance = 32.0f;
        const float heightFromFloor = 128.0f;

        static Sprite wireSprite;

        private List<Vector2> nodes;
        private List<WireSection> sections;

        Connection[] connections;

        private Vector2 newNodePos;



        private static Wire draggingWire;
        private static int? selectedNodeIndex;
        private static int? highlightedNodeIndex;

        public bool Hidden, Locked;

        public Connection[] Connections
        {
            get { return connections; }
        }
                
        public Wire(Item item, XElement element)
            : base(item, element)
        {
            if (wireSprite == null)
            {
                wireSprite = new Sprite("Content/Items/wireHorizontal.png", new Vector2(0.5f, 0.5f));
                wireSprite.Depth = 0.85f;
            }
            
            nodes = new List<Vector2>();
            sections = new List<WireSection>();

            connections = new Connection[2];
            
            IsActive = false;
        }
                
        public Connection OtherConnection(Connection connection)
        {
            if (connection == null) return null;
            if (connection == connections[0]) return connections[1];
            if (connection == connections[1]) return connections[0];

            return null;
        }

        public bool IsConnectedTo(Item item)
        {
            if (connections[0] != null && connections[0].Item == item) return true;
            return (connections[1] != null && connections[1].Item == item);
        }

        public void RemoveConnection(Item item)
        {
            for (int i = 0; i<2; i++)
            {
                if (connections[i]==null || connections[i].Item!=item) continue;
                
                for (int n = 0; n< connections[i].Wires.Length; n++)
                {
                    if (connections[i].Wires[n] != this) continue;
                    
                    connections[i].Wires[n] = null;
                    connections[i].UpdateRecipients();
                }
                connections[i] = null;
            }
        }

        public void RemoveConnection(Connection connection)
        {
            if (connection == connections[0]) connections[0] = null;            
            if (connection == connections[1]) connections[1] = null;
        }

        public bool Connect(Connection newConnection, bool addNode = true, bool loading = false)
        {
            for (int i = 0; i < 2; i++)
            {
                if (connections[i] == newConnection) return false;
            }

            if (!connections.Any(c => c == null)) return false;

            for (int i = 0; i < 2; i++)
            {
                if (connections[i] != null && connections[i].Item == newConnection.Item)
                {
                    addNode = false;
                    break;
                }
            }

            if (item.body != null) item.Submarine = newConnection.Item.Submarine;

            for (int i = 0; i < 2; i++)
            {
                if (connections[i] != null) continue;

                connections[i] = newConnection;

                if (!addNode) break;

                if (newConnection.Item.Submarine == null) continue;

                if (nodes.Count > 0 && nodes[0] == newConnection.Item.Position - newConnection.Item.Submarine.HiddenSubPosition) break;
                if (nodes.Count > 1 && nodes[nodes.Count-1] == newConnection.Item.Position - newConnection.Item.Submarine.HiddenSubPosition) break;
                               

                if (i == 0)
                {
                    nodes.Insert(0, newConnection.Item.Position - newConnection.Item.Submarine.HiddenSubPosition);                    
                }
                else
                {
                    nodes.Add(newConnection.Item.Position - newConnection.Item.Submarine.HiddenSubPosition);
                }

                
                break;
            }

            if (connections[0] != null && connections[1] != null)
            {
                foreach (ItemComponent ic in item.components)
                {
                    if (ic == this) continue;
                    ic.Drop(null);
                }
                if (item.Container != null) item.Container.RemoveContained(this.item);

                if (item.body != null) item.body.Enabled = false;

                IsActive = false;

                CleanNodes();
            }

            if (!loading)
            {
                Item.NewComponentEvent(this, true, true);
                //the wire is active if only one end has been connected
                IsActive = connections[0] == null ^ connections[1] == null;
            }

            Drawable = IsActive || nodes.Any();


            UpdateSections();

            return true;
        }

        public override void Equip(Character character)
        {
            ClearConnections();

            IsActive = true;
        }

        public override void Unequip(Character character)
        {
            ClearConnections();

            IsActive = false;
        }

        public override void Drop(Character dropper)
        {
            ClearConnections();
            
            IsActive = false;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (nodes.Count == 0) return;

            Submarine sub = null;
            if (connections[0] != null && connections[0].Item.Submarine != null) sub = connections[0].Item.Submarine;
            if (connections[1] != null && connections[1].Item.Submarine != null) sub = connections[1].Item.Submarine;

            if ((item.Submarine != sub || sub == null) && Screen.Selected != GameMain.EditMapScreen)
            {
                ClearConnections();
                return;
            }

            newNodePos = RoundNode(item.Position, item.CurrentHull) - sub.HiddenSubPosition;
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            if (character == Character.Controlled && character.SelectedConstruction != null) return false;

            if (newNodePos!= Vector2.Zero && nodes.Count>0 && Vector2.Distance(newNodePos, nodes[nodes.Count - 1]) > nodeDistance)
            {
                nodes.Add(newNodePos);
                UpdateSections();

                Drawable = true;

                newNodePos = Vector2.Zero;
            }
            return true;
        }

        public override void SecondaryUse(float deltaTime, Character character = null)
        {
            if (nodes.Count > 1)
            {
                nodes.RemoveAt(nodes.Count - 1);
                UpdateSections();

                item.NewComponentEvent(this, true, true);
            }

            Drawable = IsActive || sections.Count > 0;
        }

        public override bool Pick(Character picker)
        {
            ClearConnections();

            return true;
        }

        public override void Move(Vector2 amount)
        {
            if (item.IsSelected) MoveNodes(amount);
        }

        public List<Vector2> GetNodes()
        {
            return new List<Vector2>(nodes);
        }

        public void SetNodes(List<Vector2> nodes)
        {
            this.nodes = new List<Vector2>(nodes);
            UpdateSections();
        }

        public void MoveNodes(Vector2 amount)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i] += amount;
            }
            UpdateSections();
        }

        public void UpdateSections()
        {
            sections.Clear();

            for (int i = 0; i < nodes.Count-1; i++)
            {
                sections.Add(new WireSection(nodes[i], nodes[i + 1]));
            }
            Drawable = IsActive || sections.Count > 0;
        }

        private void ClearConnections()
        {
            nodes.Clear();
            sections.Clear();

            for (int i = 0; i < 2; i++)
            {
                if (connections[i] == null) continue;
                int wireIndex = connections[i].FindWireIndex(item);

                if (wireIndex == -1) continue;
                connections[i].AddLink(wireIndex, null);

                connections[i] = null;
            }

            Drawable = sections.Count > 0;
        }

        private Vector2 RoundNode(Vector2 position, Hull hull)
        {
            if (Screen.Selected == GameMain.EditMapScreen)
            {
                position.X = MathUtils.Round(position.X, Submarine.GridSize.X / 2.0f);
                position.Y = MathUtils.Round(position.Y, Submarine.GridSize.Y / 2.0f);
            }
            else
            {
                position.X = MathUtils.Round(position.X, nodeDistance);
                if (hull == null)
                {
                    position.Y = MathUtils.Round(position.Y, nodeDistance);
                }
                else
                {
                    position.Y -= hull.Rect.Y - hull.Rect.Height;
                    position.Y = Math.Max(MathUtils.Round(position.Y, nodeDistance), heightFromFloor);
                    position.Y += hull.Rect.Y -hull.Rect.Height;
                }
            }

            return position;
        }

        private void CleanNodes()
        {
            for (int i = nodes.Count - 2; i > 0; i--)
            {
                if ((nodes[i - 1].X == nodes[i].X || nodes[i - 1].Y == nodes[i].Y) &&
                    (nodes[i + 1].X == nodes[i].X || nodes[i + 1].Y == nodes[i].Y))
                {
                    if (Vector2.Distance(nodes[i - 1], nodes[i]) == Vector2.Distance(nodes[i + 1], nodes[i]))
                    {
                        nodes.RemoveAt(i);
                    }
                }
            }

            bool removed;
            do
            {
                removed = false;
                for (int i = nodes.Count - 2; i > 0; i--)
                {
                    if ((nodes[i - 1].X == nodes[i].X && nodes[i + 1].X == nodes[i].X)
                        || (nodes[i - 1].Y == nodes[i].Y && nodes[i + 1].Y == nodes[i].Y))
                    {
                        nodes.RemoveAt(i);
                        removed = true;
                    }
                }

            } while (removed);

        }

        public void Draw(SpriteBatch spriteBatch, bool editing)
        {
            if (sections.Count == 0 && !IsActive)
            {
                Drawable = false;
                return;
            }

            Vector2 drawOffset = Vector2.Zero;
            if (item.Submarine != null)
            {
                drawOffset = item.Submarine.DrawPosition + item.Submarine.HiddenSubPosition;
            }

            float depth = item.IsSelected ? 0.0f : wireSprite.Depth + ((item.ID % 100) * 0.00001f);

            if (item.IsHighlighted)
            {
                foreach (WireSection section in sections)
                {
                    section.Draw(spriteBatch, Color.Gold, drawOffset, depth + 0.00001f, 0.7f);
                }
            }
            else if (item.IsSelected)
            {
                foreach (WireSection section in sections)
                {
                    section.Draw(spriteBatch, Color.Red, drawOffset, depth + 0.00001f, 0.7f);
                }
            }

            foreach (WireSection section in sections)
            {
                section.Draw(spriteBatch, item.Color, drawOffset, depth, 0.3f);
            }

            if (IsActive && nodes.Count > 0 && Vector2.Distance(newNodePos, nodes[nodes.Count - 1]) > nodeDistance)
            {
                WireSection.Draw(
                    spriteBatch,
                    new Vector2(nodes[nodes.Count - 1].X, nodes[nodes.Count - 1].Y) + drawOffset, 
                    new Vector2(newNodePos.X, newNodePos.Y) + drawOffset, 
                    item.Color * 0.5f,
                    depth, 
                    0.3f);
            }
            
            if (!editing || !GameMain.EditMapScreen.WiringMode) return;

            for (int i = 0; i < nodes.Count; i++)
            {
                Vector2 drawPos = nodes[i];
                if (item.Submarine != null) drawPos += item.Submarine.Position + item.Submarine.HiddenSubPosition;
                drawPos.Y = -drawPos.Y;

                if (item.IsSelected)
                {
                    GUI.DrawRectangle(spriteBatch, drawPos + new Vector2(-5, -5), new Vector2(10, 10), item.Color, true, 0.0f);
                    
                    if (highlightedNodeIndex == i)
                    {
                        GUI.DrawRectangle(spriteBatch, drawPos + new Vector2(-10, -10), new Vector2(20, 20), Color.Red, false, 0.0f); 
                    }                   
                }
                else
                {
                    GUI.DrawRectangle(spriteBatch, drawPos + new Vector2(-3, -3), new Vector2(6, 6), item.Color, true, 0.0f);
                }
            }
        }

        public static void UpdateEditing(List<Wire> wires)
        {
            //dragging a node of some wire
            if (draggingWire != null)
            {
                //cancel dragging
                if (!PlayerInput.LeftButtonHeld())
                {
                    draggingWire = null;
                    selectedNodeIndex = null;
                }
                //update dragging
                else
                {
                    MapEntity.DisableSelect = true;

                    Submarine sub = null;
                    if (draggingWire.connections[0] != null && draggingWire.connections[0].Item.Submarine != null) sub = draggingWire.connections[0].Item.Submarine;
                    if (draggingWire.connections[1] != null && draggingWire.connections[1].Item.Submarine != null) sub = draggingWire.connections[1].Item.Submarine;

                    Vector2 nodeWorldPos = GameMain.EditMapScreen.Cam.ScreenToWorld(PlayerInput.MousePosition) - sub.HiddenSubPosition - sub.Position;// Nodes[(int)selectedNodeIndex];

                    nodeWorldPos.X = MathUtils.Round(nodeWorldPos.X, Submarine.GridSize.X / 2.0f);
                    nodeWorldPos.Y = MathUtils.Round(nodeWorldPos.Y, Submarine.GridSize.Y / 2.0f);

                    draggingWire.nodes[(int)selectedNodeIndex] = nodeWorldPos;
                    draggingWire.UpdateSections();

                    MapEntity.SelectEntity(draggingWire.item);
                }

                return;
            }

            //a wire has been selected -> check if we should start dragging one of the nodes
            float nodeSelectDist = 10, sectionSelectDist = 5;
            highlightedNodeIndex = null;
            if (MapEntity.SelectedList.Count == 1 && MapEntity.SelectedList[0] is Item)
            {
                Wire selectedWire = ((Item)MapEntity.SelectedList[0]).GetComponent<Wire>();

                if (selectedWire != null)
                {
                    Vector2 mousePos = GameMain.EditMapScreen.Cam.ScreenToWorld(PlayerInput.MousePosition);
                    if (selectedWire.item.Submarine != null) mousePos -= (selectedWire.item.Submarine.Position + selectedWire.item.Submarine.HiddenSubPosition);

                    //left click while holding ctrl -> check if the cursor is on a wire section, 
                    //and add a new node if it is
                    if (PlayerInput.KeyDown(Keys.RightControl) || PlayerInput.KeyDown(Keys.LeftControl))
                    {
                        if (PlayerInput.LeftButtonClicked())
                        {
                            float temp = 0.0f;
                            int closestSectionIndex = selectedWire.GetClosestSectionIndex(mousePos, sectionSelectDist, out temp);

                            if (closestSectionIndex > -1)
                            {
                                selectedWire.nodes.Insert(closestSectionIndex + 1, mousePos);
                                selectedWire.UpdateSections();
                            }
                        }
                    }
                    else
                    {
                        //check if close enough to a node
                        float temp = 0.0f;
                        int closestIndex = selectedWire.GetClosestNodeIndex(mousePos, nodeSelectDist, out temp);
                        if (closestIndex > -1)
                        {
                            highlightedNodeIndex = closestIndex;
                            //start dragging the node
                            if (PlayerInput.LeftButtonHeld())
                            {
                                draggingWire = selectedWire;
                                selectedNodeIndex = closestIndex;
                            }
                            //remove the node
                            else if (PlayerInput.RightButtonClicked() && closestIndex > 0 && closestIndex < selectedWire.nodes.Count - 1)
                            {
                                selectedWire.nodes.RemoveAt(closestIndex);
                                selectedWire.UpdateSections();
                            }
                        }
                    }
                }
            }

            //check which wire is highlighted with the cursor
            Wire highlighted = null;
            float closestDist = 0.0f;
            foreach (Wire w in wires)
            {
                Vector2 mousePos = GameMain.EditMapScreen.Cam.ScreenToWorld(PlayerInput.MousePosition);
                if (w.item.Submarine != null) mousePos -= (w.item.Submarine.Position + w.item.Submarine.HiddenSubPosition);
                
                float dist = 0.0f;
                if (w.GetClosestNodeIndex(mousePos, highlighted == null ? nodeSelectDist : closestDist, out dist) > -1)
                {
                    highlighted = w;
                    closestDist = dist;
                }
    
                if (w.GetClosestSectionIndex(mousePos, highlighted == null ? sectionSelectDist : closestDist, out dist) > -1)
                {
                    highlighted = w;
                    closestDist = dist;                        
                }
                
            }


            if (highlighted != null)
            {
                highlighted.item.IsHighlighted = true;

                if (PlayerInput.LeftButtonClicked())
                {
                    MapEntity.DisableSelect = true;
                    MapEntity.SelectEntity(highlighted.item);
                }
            }
        }

        private int GetClosestNodeIndex(Vector2 pos, float maxDist, out float closestDist)
        {
            closestDist = 0.0f;
            int closestIndex = -1;

            for (int i = 0; i < nodes.Count; i++)
            {
                float dist = Vector2.Distance(nodes[i], pos);
                if (dist > maxDist) continue;

                if (closestIndex == -1 || dist < closestDist)
                {
                    closestIndex = i;
                    closestDist = dist;
                }
            }

            return closestIndex;
        }

        private int GetClosestSectionIndex(Vector2 mousePos, float maxDist, out float closestDist)
        {
            closestDist = 0.0f;
            int closestIndex = -1;

            for (int i = 0; i < nodes.Count-1; i++)
            {
                if ((Math.Abs(nodes[i].X - nodes[i + 1].X)<5 || Math.Sign(mousePos.X - nodes[i].X) != Math.Sign(mousePos.X - nodes[i + 1].X)) &&
                     (Math.Abs(nodes[i].Y - nodes[i + 1].Y)<5 || Math.Sign(mousePos.Y - nodes[i].Y) != Math.Sign(mousePos.Y - nodes[i + 1].Y)))
                {
                    float dist = MathUtils.LineToPointDistance(nodes[i], nodes[i + 1], mousePos);
                    if (dist > maxDist) continue;

                    if (closestIndex == -1 || dist < closestDist)
                    {
                        closestIndex = i;
                        closestDist = dist;
                    }
                }
            }

            return closestIndex;
        }
        
        public override void FlipX()
        {            
            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i] = new Vector2(-nodes[i].X, nodes[i].Y);
            }
            UpdateSections();
        }

        public override XElement Save(XElement parentElement)
        {
            XElement componentElement = base.Save(parentElement);

            if (nodes == null || nodes.Count == 0) return componentElement;

            string[] nodeCoords = new string[nodes.Count * 2];
            for (int i = 0; i < nodes.Count; i++)
            {
                nodeCoords[i * 2] = nodes[i].X.ToString(CultureInfo.InvariantCulture);
                nodeCoords[i * 2 + 1] = nodes[i].Y.ToString(CultureInfo.InvariantCulture);
            }

            componentElement.Add(new XAttribute("nodes", string.Join(";", nodeCoords)));

            return componentElement;
        }

        public override void Load(XElement componentElement)
        {
            base.Load(componentElement);

            string nodeString = ToolBox.GetAttributeString(componentElement, "nodes", "");
            if (nodeString == "") return;

            string[] nodeCoords = nodeString.Split(';');
            for (int i = 0; i < nodeCoords.Length / 2; i++)
            {
                float x = 0.0f, y = 0.0f;

                try
                {
                    x = float.Parse(nodeCoords[i * 2], CultureInfo.InvariantCulture);
                }
                catch { x = 0.0f; }

                try
                {
                    y = float.Parse(nodeCoords[i * 2 + 1], CultureInfo.InvariantCulture);
                }
                catch { y = 0.0f; }

                nodes.Add(new Vector2(x, y));
            }

            Drawable = nodes.Any();
        }

        protected override void ShallowRemoveComponentSpecific()
        {
            for (int i = 0; i < 2; i++)
            {
                if (connections[i] == null) continue;
                int wireIndex = connections[i].FindWireIndex(item);

                if (wireIndex > -1)
                {
                    connections[i].AddLink(wireIndex, null);
                }
            }
        }

        protected override void RemoveComponentSpecific()
        {
            ClearConnections();

            base.RemoveComponentSpecific();
        }

        public override bool FillNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetBuffer message)
        {
            message.Write((byte)Math.Min(nodes.Count, 10));
            for (int i = 0; i < Math.Min(nodes.Count,10); i++)
            {
                message.Write(nodes[i].X);
                message.Write(nodes[i].Y);
            }

            return true;
        }

        public override void ReadNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetIncomingMessage message, float sendingTime)
        {
            nodes.Clear();

            List<Vector2> newNodes = new List<Vector2>();
            int nodeCount = message.ReadByte();
            for (int i = 0; i<nodeCount; i++)
            {
                Vector2 newNode = new Vector2(message.ReadFloat(), message.ReadFloat());
                if (!MathUtils.IsValid(newNode)) return;
                newNodes.Add(newNode);
            }

            SetNodes(newNodes);
            Drawable = nodes.Any();
        }
    }
}
