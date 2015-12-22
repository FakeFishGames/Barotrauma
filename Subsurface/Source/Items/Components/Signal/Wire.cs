using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class Wire : ItemComponent
    {
        const float nodeDistance = 32.0f;
        const float heightFromFloor = 128.0f;

        static Sprite wireSprite;

        public List<Vector2> Nodes;

        Connection[] connections;

        private Vector2 newNodePos;

        private static int? selectedNodeIndex;
                
        public Wire(Item item, XElement element)
            : base(item, element)
        {
            if (wireSprite == null)
            {
                wireSprite = new Sprite("Content/Items/wireHorizontal.png", new Vector2(0.5f, 0.5f));
                wireSprite.Depth = 0.85f;
            }

            Nodes = new List<Vector2>();

            connections = new Connection[2];

            IsActive = false;
        }
        
        public override void Move(Vector2 amount)
        {
            //for (int i = 0; i < Nodes.Count; i++)
            //{
            //    Nodes[i] += amount;
            //}
        }

        public Connection OtherConnection(Connection connection)
        {
            if (connection == null) return null;
            if (connection == connections[0]) return connections[1];
            if (connection == connections[1]) return connections[0];

            return null;
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

        public void Connect(Connection newConnection, bool addNode = true, bool loading = false)
        {
            for (int i = 0; i < 2; i++)
            {
                if (connections[i] == newConnection) return;
            }

            for (int i = 0; i < 2; i++)
            {
                if (connections[i] != null && connections[i].Item == newConnection.Item)
                {
                    addNode = false;
                    break;
                }
            }

            for (int i = 0; i < 2; i++)
            {
                if (connections[i] != null) continue;

                connections[i] = newConnection;

                if (!addNode) break;

                if (Nodes.Count > 0 && Nodes[0] == newConnection.Item.Position - Submarine.HiddenSubPosition) break;
                if (Nodes.Count > 1 && Nodes[Nodes.Count-1] == newConnection.Item.Position - Submarine.HiddenSubPosition) break;

                if (i == 0)
                {
                    Nodes.Insert(0, newConnection.Item.Position - Submarine.HiddenSubPosition);
                }
                else
                {
                    Nodes.Add(newConnection.Item.Position - Submarine.HiddenSubPosition);
                }

                break;
            }

            if (connections[0] != null && connections[1] != null)
            {
                //List<Vector2> prevNodes = new List<Vector2>(Nodes);
                foreach (ItemComponent ic in item.components)
                {
                    if (ic == this) continue;
                    ic.Drop(null);
                }
                if (item.container != null) item.container.RemoveContained(this.item);

                item.body.Enabled = false;

                IsActive = false;

                //Nodes = prevNodes;
                CleanNodes();
            }

            if (!loading) Item.NewComponentEvent(this, true, true);
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
            if (Nodes.Count == 0) return;

            item.FindHull();

            Vector2 position = item.Position;
            position.X = MathUtils.Round(item.Position.X, nodeDistance);
            if (item.CurrentHull == null)
            {
                position.Y = MathUtils.Round(item.Position.Y, nodeDistance);
            }
            else
            {
                position.Y -= item.CurrentHull.Rect.Y - item.CurrentHull.Rect.Height;
                position.Y = Math.Max(MathUtils.Round(position.Y, nodeDistance), heightFromFloor);
                position.Y += item.CurrentHull.Rect.Y - item.CurrentHull.Rect.Height;
            }

            newNodePos = RoundNode(item.Position, item.CurrentHull)-Submarine.HiddenSubPosition;

            //if (Vector2.Distance(position, nodes[nodes.Count - 1]) > nodeDistance*10)
            //{
            //    nodes.Add(position);

            //    item.NewComponentEvent(this, true);
            //}
            //else if (Math.Abs(position.Y - nodes[nodes.Count - 1].Y) > nodeDistance)
            //{
            //    nodes.Add(new Vector2(nodes[nodes.Count - 1].X,
            //        position.Y));

            //    item.NewComponentEvent(this, true);
            //}
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            if (character == Character.Controlled && character.SelectedConstruction != null) return false;

            if (newNodePos!= Vector2.Zero && Nodes.Count>0 && Vector2.Distance(newNodePos, Nodes[Nodes.Count - 1]) > nodeDistance)
            {
                Nodes.Add(newNodePos);
                newNodePos = Vector2.Zero;
            }
            return true;
        }

        public override void SecondaryUse(float deltaTime, Character character = null)
        {
            if (Nodes.Count > 1)
            {
                Nodes.RemoveAt(Nodes.Count - 1);
                item.NewComponentEvent(this, true, true);
            }
        }

        public override bool Pick(Character picker)
        {
            ClearConnections();

            return true;
        }

        private void ClearConnections()
        {
            Nodes.Clear();

            for (int i = 0; i < 2; i++)
            {
                if (connections[i] == null) continue;
                int wireIndex = connections[i].FindWireIndex(item);

                if (wireIndex == -1) continue;
                connections[i].AddLink(wireIndex, null);

                connections[i] = null;
            }
        }

        private Vector2 RoundNode(Vector2 position, Hull hull)
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

            return position;
        }

        private void CleanNodes()
        {
            for (int i = Nodes.Count - 2; i > 0; i--)
            {
                if ((Nodes[i - 1].X == Nodes[i].X || Nodes[i - 1].Y == Nodes[i].Y) &&
                    (Nodes[i + 1].X == Nodes[i].X || Nodes[i + 1].Y == Nodes[i].Y))
                {
                    if (Vector2.Distance(Nodes[i - 1], Nodes[i]) == Vector2.Distance(Nodes[i + 1], Nodes[i]))
                    {
                        Nodes.RemoveAt(i);
                    }
                }
            }

            bool removed;
            do
            {
                removed = false;
                for (int i = Nodes.Count - 2; i > 0; i--)
                {
                    if ((Nodes[i - 1].X == Nodes[i].X && Nodes[i + 1].X == Nodes[i].X)
                        || (Nodes[i - 1].Y == Nodes[i].Y && Nodes[i + 1].Y == Nodes[i].Y))
                    {
                        Nodes.RemoveAt(i);
                        removed = true;
                    }
                }

            } while (removed);

        }


        public override void Draw(SpriteBatch spriteBatch, bool editing)
        {
            if (Nodes.Count == 0) return;

            //for (int i = 0; i < nodes.Count; i++)
            //{
            //    GUI.DrawRectangle(spriteBatch, new Rectangle((int)nodes[i].X, (int)-nodes[i].Y, 5, 5), Color.DarkGray, true, wireSprite.Depth - 0.01f);
            //}

            for (int i = 1; i < Nodes.Count; i++)
            {
                DrawSection(spriteBatch, Nodes[i], Nodes[i - 1], item.Color);
            }

            if (IsActive && Vector2.Distance(newNodePos, Nodes[Nodes.Count - 1]) > nodeDistance)
            {
                DrawSection(spriteBatch, Nodes[Nodes.Count - 1], newNodePos, item.Color * 0.5f);
                //nodes.Add(newNodePos);
            }

            if (!editing) return;

            for (int i = 1; i < Nodes.Count; i++)
            {
                Vector2 worldPos = Nodes[i];
                if (item.Submarine != null) worldPos += item.Submarine.Position;
                worldPos.Y = -worldPos.Y;

                GUI.DrawRectangle(spriteBatch, worldPos+new Vector2(-3,-3), new Vector2(6, 6), Color.Red, true, 0.0f);

                if (GUIComponent.MouseOn != null ||
                    Vector2.Distance(GameMain.EditMapScreen.Cam.ScreenToWorld(PlayerInput.MousePosition), Nodes[i]) > 10.0f)
                {
                    continue;
                }

                GUI.DrawRectangle(spriteBatch, worldPos + new Vector2(-10, -10), new Vector2(20, 20), Color.Red, false, 0.0f);

                if (selectedNodeIndex == null && !MapEntity.SelectedAny)
                {
                    if (PlayerInput.LeftButtonDown() && PlayerInput.GetOldMouseState.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Released)
                    {
                        MapEntity.SelectEntity(item);
                        selectedNodeIndex = i;
                        MapEntity.DisableSelect = true;
                    }
                    else if (PlayerInput.RightButtonClicked())
                    {
                        Nodes.RemoveAt(i);
                        break;
                    }
                }
            }

            if (PlayerInput.LeftButtonDown())
            {
                if (selectedNodeIndex != null && item.IsSelected)
                {
                    MapEntity.DisableSelect = true;
                    Nodes[(int)selectedNodeIndex] = GameMain.EditMapScreen.Cam.ScreenToWorld(PlayerInput.MousePosition);

                    Vector2 nodeWorldPos = Nodes[(int)selectedNodeIndex];

                    if (item.Submarine != null) nodeWorldPos += item.Submarine.Position;

                    Nodes[(int)selectedNodeIndex] = RoundNode(Nodes[(int)selectedNodeIndex], Hull.FindHull(nodeWorldPos));
                    MapEntity.SelectEntity(item);
                }
            }
            else
            {
                selectedNodeIndex = null;
            }
        }

        private void DrawSection(SpriteBatch spriteBatch, Vector2 start, Vector2 end, Color color)
        {
            if (item.Submarine!=null)
            {
                start += item.Submarine.DrawPosition+Submarine.HiddenSubPosition;
                end += item.Submarine.DrawPosition+Submarine.HiddenSubPosition;
            }

            start.Y = -start.Y;
            end.Y = -end.Y;

            spriteBatch.Draw(wireSprite.Texture,
                start, null, color,
                MathUtils.VectorToAngle(end - start),
                new Vector2(0.0f, wireSprite.size.Y / 2.0f),
                new Vector2((Vector2.Distance(start, end)) / wireSprite.Texture.Width, 0.3f),
                SpriteEffects.None,
                wireSprite.Depth + ((item.ID % 100) * 0.00001f));
        }

        public override XElement Save(XElement parentElement)
        {
            XElement componentElement = base.Save(parentElement);

            if (Nodes == null || Nodes.Count == 0) return componentElement;

            string[] nodeCoords = new string[Nodes.Count() * 2];
            for (int i = 0; i < Nodes.Count(); i++)
            {
                nodeCoords[i * 2] = Nodes[i].X.ToString(CultureInfo.InvariantCulture);
                nodeCoords[i * 2 + 1] = Nodes[i].Y.ToString(CultureInfo.InvariantCulture);
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

                Nodes.Add(new Vector2(x, y));
            }

        }

        public override void Remove()
        {
            ClearConnections();

            base.Remove();
        }

        public override bool FillNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetBuffer message)
        {
            message.Write((byte)Math.Min(Nodes.Count, 10));
            for (int i = 0; i < Math.Min(Nodes.Count,10); i++)
            {
                message.Write(Nodes[i].X);
                message.Write(Nodes[i].Y);
            }

            return true;
        }

        public override void ReadNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetBuffer message, float sendingTime)
        {
            Nodes.Clear();

            List<Vector2> newNodes = new List<Vector2>();
            int nodeCount = message.ReadByte();
            for (int i = 0; i<nodeCount; i++)
            {
                Vector2 newNode = new Vector2(message.ReadFloat(), message.ReadFloat());
                if (!MathUtils.IsValid(newNode)) return;
                newNodes.Add(newNode);
            }

            Nodes = newNodes;
        }
    }
}
