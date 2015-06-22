using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace Subsurface.Items.Components
{
    class Wire : ItemComponent
    {
        const float nodeDistance = 32.0f;
        const float heightFromFloor = 128.0f;

        static Sprite wireSprite;

        List<Vector2> nodes;

        Connection[] connections;

        private Vector2 newNodePos;

        public Wire(Item item, XElement element)
            : base(item, element)
        {
            if (wireSprite == null)
            {
                wireSprite = new Sprite("Content/Items/wireHorizontal.png", new Vector2(0.5f, 0.5f));
                wireSprite.Depth = 0.85f;
            }

            nodes = new List<Vector2>();

            connections = new Connection[2];
        }

        public Connection OtherConnection(Connection connection)
        {
            if (connection == null) return null;
            if (connection == connections[0]) return connections[1];
            if (connection == connections[1]) return connections[0];

            return null;
        }

        public void RemoveConnection(Connection connection)
        {
            if (connection == connections[0]) connections[0] = null;
            if (connection == connections[1]) connections[1] = null;
        }

        public void Connect(Connection newConnection, bool addNode = true)
        {
            for (int i = 0; i < 2; i++)
            {
                if (connections[i] == newConnection) return;
            }

            for (int i = 0; i < 2; i++)
            {
                if (connections[i] != null) continue;

                connections[i] = newConnection;

                if (!addNode) break;

                if (i == 0)
                {
                    nodes.Insert(0, newConnection.Item.Position);
                }
                else
                {
                    nodes.Add(newConnection.Item.Position);
                }


                break;
            }

            if (connections[0] != null && connections[1] != null)
            {
                item.Drop(null, false);
                item.body.Enabled = false;

                isActive = false;

                CleanNodes();
            }

            //new Networking.NetworkEvent(item.ID, true);

        }

        public override void Equip(Character character)
        {
            ClearConnections();

            isActive = true;
        }

        public override void Unequip(Character character)
        {
            ClearConnections();

            isActive = false;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (nodes.Count == 0) return;

            item.FindHull();

            Vector2 position = item.Position;
            position.X = ToolBox.Round(item.Position.X, nodeDistance);
            if (item.currentHull == null)
            {
                position.Y = ToolBox.Round(item.Position.Y, nodeDistance);
            }
            else
            {
                position.Y -= item.currentHull.Rect.Y - item.currentHull.Rect.Height;
                position.Y = Math.Max(ToolBox.Round(position.Y, nodeDistance), heightFromFloor);
                position.Y += item.currentHull.Rect.Y - item.currentHull.Rect.Height;
            }

            newNodePos = position;

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
            if (newNodePos!= Vector2.Zero && nodes.Count>0 && Vector2.Distance(newNodePos, nodes[nodes.Count - 1]) > nodeDistance)
            {
                nodes.Add(newNodePos);
                newNodePos = Vector2.Zero;
            }



            return true;
        }

        public override void SecondaryUse(float deltaTime, Character character = null)
        {
            if (nodes.Count > 1)
            {
                nodes.RemoveAt(nodes.Count - 1);
                item.NewComponentEvent(this, true);
            }
        }

        public override bool Pick(Character picker)
        {
            ClearConnections();

            return true;
        }

        private void ClearConnections()
        {
            nodes.Clear();

            for (int i = 0; i < 2; i++)
            {
                if (connections[i] == null) continue;
                int wireIndex = connections[i].FindWireIndex(item);

                if (wireIndex == -1) continue;
                connections[i].AddLink(wireIndex, null);

                connections[i] = null;
            }
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


        public override void Draw(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
        {
            if (nodes.Count == 0) return;

            //for (int i = 0; i < nodes.Count; i++)
            //{
            //    GUI.DrawRectangle(spriteBatch, new Rectangle((int)nodes[i].X, (int)-nodes[i].Y, 5, 5), Color.DarkGray, true, wireSprite.Depth - 0.01f);
            //}

            for (int i = 1; i < nodes.Count; i++)
            {
                DrawSection(spriteBatch, nodes[i], nodes[i - 1], i, Color.White);
            }

            if (isActive && Vector2.Distance(newNodePos, nodes[nodes.Count - 1]) > nodeDistance)
            {
                DrawSection(spriteBatch, nodes[nodes.Count - 1], newNodePos, nodes.Count, Color.White * 0.5f);
                //nodes.Add(newNodePos);
            }

        }

        private void DrawSection(SpriteBatch spriteBatch, Vector2 start, Vector2 end, int i, Color color)
        {
            start.Y = -start.Y;
            end.Y = -end.Y;

            spriteBatch.Draw(wireSprite.Texture,
                start, null, color,
                ToolBox.VectorToAngle(end - start),
                new Vector2(0.0f, wireSprite.size.Y / 2.0f),
                new Vector2((Vector2.Distance(start, end)) / wireSprite.Texture.Width, 0.3f),
                SpriteEffects.None,
                wireSprite.Depth + 0.1f + i * 0.00001f);
        }

        public override XElement Save(XElement parentElement)
        {
            XElement componentElement = base.Save(parentElement);

            if (nodes == null || nodes.Count == 0) return componentElement;

            string[] nodeCoords = new string[nodes.Count() * 2];
            for (int i = 0; i < nodes.Count(); i++)
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

        }

        public override void FillNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetOutgoingMessage message)
        {
            message.Write(nodes.Count);
            for (int i = 0; i < nodes.Count; i++)
            {
                message.Write(nodes[i].X);
                message.Write(nodes[i].Y);
            }
        }

        public override void ReadNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetIncomingMessage message)
        {
            nodes.Clear();
            int nodeCount = message.ReadInt32();
            for (int i = 0; i < nodeCount; i++)
            {
                nodes.Add(new Vector2(message.ReadFloat(), message.ReadFloat()));
            }
        }
    }
}
