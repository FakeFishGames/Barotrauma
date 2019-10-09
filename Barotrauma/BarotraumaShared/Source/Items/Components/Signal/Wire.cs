using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Wire : ItemComponent, IDrawableComponent, IServerSerializable
    {
        partial class WireSection
        {
            private Vector2 start;
            private Vector2 end;

            private readonly float angle;
            private readonly float length;

            public Vector2 Start
            {
                get { return start; }
            }
            public Vector2 End
            {
                get { return end; }
            }

            public WireSection(Vector2 start, Vector2 end)
            {
                this.start = start;
                this.end = end;

                angle = MathUtils.VectorToAngle(end - start);
                length = Vector2.Distance(start, end);
            }
        }

        const float nodeDistance = 32.0f;
        const float heightFromFloor = 128.0f;

        const int MaxNodeCount = 255;
        const int MaxNodesPerNetworkEvent = 30;

        private List<Vector2> nodes;
        private readonly List<WireSection> sections;

        private Connection[] connections;

        private bool canPlaceNode;
        private Vector2 newNodePos;

        private Vector2 sectionExtents;

        public bool Hidden;

        private float removeNodeDelay;

        private bool locked;
        public bool Locked
        {
            get
            {
                if (GameMain.NetworkMember?.ServerSettings != null && !GameMain.NetworkMember.ServerSettings.AllowRewiring) { return false; }
                return locked || connections.Any(c => c != null && c.ConnectionPanel.Locked);
            }
            set { locked = value; }
        }

        public Connection[] Connections
        {
            get { return connections; }
        }

        [Serialize(5000.0f, false, description: "The maximum distance the wire can extend (in pixels).")]
        public float MaxLength
        {
            get;
            set;
        }
                
        public Wire(Item item, XElement element)
            : base(item, element)
        {
            nodes = new List<Vector2>();
            sections = new List<WireSection>();
            connections = new Connection[2];            
            IsActive = false;

            InitProjSpecific(element);
        }

        partial void InitProjSpecific(XElement element);

        public Connection OtherConnection(Connection connection)
        {
            if (connection == connections[0]) { return connections[1]; }
            if (connection == connections[1]) { return connections[0]; }

            return null;
        }

        public bool IsConnectedTo(Item item)
        {
            if (connections[0] != null && connections[0].Item == item) return true;
            return (connections[1] != null && connections[1].Item == item);
        }

        public void RemoveConnection(Item item)
        {
            for (int i = 0; i < 2; i++)
            {
                if (connections[i] == null || connections[i].Item != item) continue;

                foreach (Wire wire in connections[i].Wires)
                {
                    if (wire != this) continue;
                    SetConnectedDirty();

                    connections[i].SetWire(connections[i].FindWireIndex(wire), null);
                }

                connections[i] = null;
            }
        }

        public void RemoveConnection(Connection connection)
        {
            if (connection == connections[0]) { connections[0] = null; }
            if (connection == connections[1]) { connections[1] = null; }

            SetConnectedDirty();
        }

        public bool Connect(Connection newConnection, bool addNode = true, bool sendNetworkEvent = false)
        {
            for (int i = 0; i < 2; i++)
            {
                if (connections[i] == newConnection) { return false; }
            }

            if (!connections.Any(c => c == null)) { return false; }

            for (int i = 0; i < 2; i++)
            {
                if (connections[i] != null && connections[i].Item == newConnection.Item)
                {
                    addNode = false;
                    break;
                }
            }
            if (item.body != null) { item.Submarine = newConnection.Item.Submarine; }

            newConnection.ConnectionPanel.DisconnectedWires.Remove(this);

            for (int i = 0; i < 2; i++)
            {
                if (connections[i] != null) { continue; }

                connections[i] = newConnection;
                FixNodeEnds();

                if (!addNode) { break; }

                Submarine refSub = newConnection.Item.Submarine;
                if (refSub == null)
                {
                    Structure attachTarget = Structure.GetAttachTarget(newConnection.Item.WorldPosition);
                    if (attachTarget == null) { continue; }
                    refSub = attachTarget.Submarine;
                }

                Vector2 nodePos = refSub == null ? 
                    newConnection.Item.Position : 
                    newConnection.Item.Position - refSub.HiddenSubPosition;

                if (nodes.Count > 0 && nodes[0] == nodePos) { break; }
                if (nodes.Count > 1 && nodes[nodes.Count - 1] == nodePos) { break; }

                //make sure we place the node at the correct end of the wire (the end that's closest to the new node pos)
                int newNodeIndex = 0;
                if (nodes.Count > 1)
                {
                    if (Vector2.DistanceSquared(nodes[nodes.Count - 1], nodePos) < Vector2.DistanceSquared(nodes[0], nodePos))
                    {
                        newNodeIndex = nodes.Count;
                    }
                }

                if (newNodeIndex == 0)
                {
                    nodes.Insert(0, nodePos);                    
                }
                else
                {
                    nodes.Add(nodePos);
                }
                
                break;
            }

            SetConnectedDirty();

            if (connections[0] != null && connections[1] != null)
            {
                foreach (ItemComponent ic in item.Components)
                {
                    if (ic == this) continue;
                    ic.Drop(null);
                }
                if (item.Container != null) item.Container.RemoveContained(this.item);
                if (item.body != null) item.body.Enabled = false;

                IsActive = false;

                CleanNodes();
            }
            
            if (item.body != null) item.Submarine = newConnection.Item.Submarine;

            if (sendNetworkEvent)
            {
#if SERVER
                if (GameMain.Server != null)
                {
                    CreateNetworkEvent();
                }
#endif
                //the wire is active if only one end has been connected
                IsActive = connections[0] == null ^ connections[1] == null;
            }

            Drawable = IsActive || nodes.Any();

            UpdateSections();
            return true;
        }

        public override void Equip(Character character)
        {
            ClearConnections(character);
            IsActive = true;
        }

        public override void Unequip(Character character)
        {
            ClearConnections(character);
            IsActive = false;
        }

        public override void Drop(Character dropper)
        {
            ClearConnections(dropper);
            IsActive = false;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            removeNodeDelay -= deltaTime;
            if (nodes.Count == 0) { return; }

            Submarine sub = null;
            if (connections[0] != null && connections[0].Item.Submarine != null) { sub = connections[0].Item.Submarine; }
            if (connections[1] != null && connections[1].Item.Submarine != null) { sub = connections[1].Item.Submarine; }

            if (Screen.Selected != GameMain.SubEditorScreen)
            {
                //cannot run wires from sub to another
                if (item.Submarine != sub && sub != null && item.Submarine != null)
                {
                    ClearConnections();
                    return;
                }

                if (item.CurrentHull == null)
                {
                    Structure attachTarget = Structure.GetAttachTarget(item.WorldPosition);
                    canPlaceNode = attachTarget != null;

                    sub = sub ?? attachTarget?.Submarine;
                    newNodePos = sub == null ? 
                        item.WorldPosition :
                        item.WorldPosition - sub.Position - sub.HiddenSubPosition;
                }
                else
                {
                    newNodePos = RoundNode(item.Position, item.CurrentHull);
                    if (sub != null) { newNodePos -= sub.HiddenSubPosition; }
                    canPlaceNode = true;
                }

                //prevent the wire from extending too far when rewiring
                if (nodes.Count > 0)
                {
                    if (!(item.ParentInventory?.Owner is Character user)) return;

                    Vector2 prevNodePos = nodes[nodes.Count - 1];
                    if (sub != null) { prevNodePos += sub.HiddenSubPosition; }

                    float currLength = 0.0f;
                    for (int i = 0; i < nodes.Count - 1; i++)
                    {
                        currLength += Vector2.Distance(nodes[i], nodes[i + 1]);
                    }
                    currLength += Vector2.Distance(nodes[nodes.Count - 1], newNodePos);

                    if (currLength > MaxLength)
                    {
                        Vector2 diff = nodes[nodes.Count - 1] - newNodePos;
                        Vector2 pullBackDir = diff == Vector2.Zero ? Vector2.Zero : Vector2.Normalize(diff);

                        user.AnimController.Collider.ApplyForce(pullBackDir * user.Mass * 50.0f, maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
                        user.AnimController.UpdateUseItem(true, user.WorldPosition + pullBackDir * 200.0f);

                        if (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer)
                        {
                            if (currLength > MaxLength * 1.5f)
                            {
                                ClearConnections();
#if SERVER
                                CreateNetworkEvent();
#endif
                                return;
                            }
                        }
                    }
                }
            }
            else
            {
                newNodePos = RoundNode(item.Position, item.CurrentHull);
                if (sub != null) { newNodePos -= sub.HiddenSubPosition; }
                canPlaceNode = true;
            }

            if (item != null)
            {
                Vector2 relativeNodePos = newNodePos - item.Position;

                if (sub != null)
                {
                    relativeNodePos += sub.HiddenSubPosition;
                }

                sectionExtents = new Vector2(
                    Math.Max(Math.Abs(relativeNodePos.X), sectionExtents.X),
                    Math.Max(Math.Abs(relativeNodePos.Y), sectionExtents.Y));
            }
        }
        
        public override bool Use(float deltaTime, Character character = null)
        {
            if (character == null) { return false; }
            if (character == Character.Controlled && character.SelectedConstruction != null) { return false; }
            if (Screen.Selected == GameMain.SubEditorScreen && !PlayerInput.LeftButtonClicked())
            {
                return false;
            }

            if (newNodePos != Vector2.Zero && canPlaceNode && nodes.Count > 0 && Vector2.Distance(newNodePos, nodes[nodes.Count - 1]) > nodeDistance)
            {
                if (nodes.Count >= MaxNodeCount)
                {
                    nodes.RemoveAt(nodes.Count - 1);
                }

                nodes.Add(newNodePos);
                CleanNodes();
                UpdateSections();
                Drawable = true;
                newNodePos = Vector2.Zero;

#if SERVER
                if (GameMain.Server != null)
                {
                    CreateNetworkEvent();
                }
#endif
            }
            return true;
        }

        public override bool SecondaryUse(float deltaTime, Character character = null)
        {
            if (nodes.Count > 1 && removeNodeDelay <= 0.0f)
            {
                nodes.RemoveAt(nodes.Count - 1);
                UpdateSections();
            }
            removeNodeDelay = 0.1f;

            Drawable = IsActive || sections.Count > 0;
            return true;
        }

        public override bool Pick(Character picker)
        {
            ClearConnections(picker);
            return true;
        }

        public override void Move(Vector2 amount)
        {
#if CLIENT
            if (item.IsSelected) MoveNodes(amount);
#endif
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

        public void MoveNode(int index, Vector2 amount)
        {
            if (index < 0 || index >= nodes.Count) return;
            nodes[index] += amount;            
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

            for (int i = 0; i < nodes.Count - 1; i++)
            {
                sections.Add(new WireSection(nodes[i], nodes[i + 1]));
            }
            Drawable = IsActive || sections.Count > 0;
            CalculateExtents();
        }

        private void CalculateExtents()
        {
            sectionExtents = Vector2.Zero;
            if (sections.Count > 0)
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    sectionExtents.X = Math.Max(Math.Abs(nodes[i].X - item.Position.X), sectionExtents.X);
                    sectionExtents.Y = Math.Max(Math.Abs(nodes[i].Y - item.Position.Y), sectionExtents.Y);
                }
            }
        }

        private void ClearConnections(Character user = null)
        {
            nodes.Clear();
            sections.Clear();

            foreach (Item item in Item.ItemList)
            {
                var connectionPanel = item.GetComponent<ConnectionPanel>();
                if (connectionPanel != null && connectionPanel.DisconnectedWires.Contains(this))
                {
#if SERVER
                    item.CreateServerEvent(connectionPanel);
#endif
                    connectionPanel.DisconnectedWires.Remove(this);
                }
            }

#if SERVER
            if (user != null)
            {
                if (connections[0] != null || connections[1] != null)
                {
                    GameMain.Server.KarmaManager.OnWireDisconnected(user, this);
                }

                if (connections[0] != null && connections[1] != null)
                {
                    GameServer.Log(user.LogName + " disconnected a wire from " + 
                        connections[0].Item.Name + " (" + connections[0].Name + ") to "+
                        connections[1].Item.Name + " (" + connections[1].Name + ")", ServerLog.MessageType.ItemInteraction);
                }
                else if (connections[0] != null)
                {
                    GameServer.Log(user.LogName + " disconnected a wire from " +
                        connections[0].Item.Name + " (" + connections[0].Name + ")", ServerLog.MessageType.ItemInteraction);
                }
                else if (connections[1] != null)
                {
                    GameServer.Log(user.LogName + " disconnected a wire from " +
                        connections[1].Item.Name + " (" + connections[1].Name + ")", ServerLog.MessageType.ItemInteraction);
                }
            }
#endif

            SetConnectedDirty();

            for (int i = 0; i < 2; i++)
            {
                if (connections[i] == null) { continue; }
                int wireIndex = connections[i].FindWireIndex(item);
                if (wireIndex == -1) { continue; }
#if SERVER
                if (!connections[i].Item.Removed)
                {
                    connections[i].Item.CreateServerEvent(connections[i].Item.GetComponent<ConnectionPanel>());
                }
#endif
                connections[i].SetWire(wireIndex, null);
                connections[i] = null;
            }

            Drawable = sections.Count > 0;
        }

        private Vector2 RoundNode(Vector2 position, Hull hull)
        {
            if (Screen.Selected == GameMain.SubEditorScreen)
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

        public void SetConnectedDirty()
        {
            for (int i = 0; i < 2; i++)
            {
                if (connections[i]?.Item != null)
                {
                    var pt = connections[i].Item.GetComponent<PowerTransfer>();
                    if (pt != null) pt.SetConnectionDirty(connections[i]);
                }
            }
        }

        private void CleanNodes()
        {
            bool removed;
            do
            {
                removed = false;
                for (int i = nodes.Count - 2; i > 0; i--)
                {
                    if (Math.Abs(nodes[i - 1].X - nodes[i].X) < 1.0f && Math.Abs(nodes[i + 1].X - nodes[i].X) < 1.0f &&
                        Math.Sign(nodes[i - 1].Y - nodes[i].Y) != Math.Sign(nodes[i + 1].Y - nodes[i].Y))
                    {
                        nodes.RemoveAt(i);
                        removed = true;
                    }
                    else if (Math.Abs(nodes[i - 1].Y - nodes[i].Y) < 1.0f && Math.Abs(nodes[i + 1].Y - nodes[i].Y) < 1.0f &&
                            Math.Sign(nodes[i - 1].X - nodes[i].X) != Math.Sign(nodes[i + 1].X - nodes[i].X))
                    {
                        nodes.RemoveAt(i);
                        removed = true;
                    }
                }

            } while (removed);
        }

        private void FixNodeEnds()
        {
            if (connections[0] == null || connections[1] == null || nodes.Count == 0) { return; }

            Vector2 nodePos = nodes[0];

            Submarine refSub = connections[0].Item.Submarine ?? connections[1].Item.Submarine;
            if (refSub != null) { nodePos += refSub.HiddenSubPosition; }

            float dist1 = Vector2.DistanceSquared(connections[0].Item.Position, nodePos);
            float dist2 = Vector2.DistanceSquared(connections[1].Item.Position, nodePos);

            //first node is closer to the second item
            //= the nodes are "backwards", need to reverse them
            if (dist1 > dist2)
            {
                nodes.Reverse();
                UpdateSections();
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
        
        public override void FlipX(bool relativeToSub)
        {            
            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i] = new Vector2(-nodes[i].X, nodes[i].Y);
            }
            UpdateSections();
        }

        public override void FlipY(bool relativeToSub)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i] = new Vector2(nodes[i].X, -nodes[i].Y);
            }
            UpdateSections();
        }

        public override void Load(XElement componentElement, bool usePrefabValues)
        {
            base.Load(componentElement, usePrefabValues);

            string nodeString = componentElement.GetAttributeString("nodes", "");
            if (nodeString == "") return;

            string[] nodeCoords = nodeString.Split(';');
            for (int i = 0; i < nodeCoords.Length / 2; i++)
            {
                float.TryParse(nodeCoords[i * 2], NumberStyles.Float, CultureInfo.InvariantCulture, out float x);
                float.TryParse(nodeCoords[i * 2 + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y);
                nodes.Add(new Vector2(x, y));
            }

            Drawable = nodes.Any();
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

        protected override void ShallowRemoveComponentSpecific()
        {
            /*for (int i = 0; i < 2; i++)
            {
                if (connections[i] == null) continue;
                int wireIndex = connections[i].FindWireIndex(item);

                if (wireIndex > -1)
                {
                    connections[i].AddLink(wireIndex, null);
                }
            }*/
        }

        protected override void RemoveComponentSpecific()
        {
            ClearConnections();
            base.RemoveComponentSpecific();
#if CLIENT
            overrideSprite?.Remove();
            overrideSprite = null;
            wireSprite = null;
#endif
        }

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            int eventIndex = msg.ReadRangedInteger(0, (int)Math.Ceiling(MaxNodeCount / (float)MaxNodesPerNetworkEvent));
            int nodeCount = msg.ReadRangedInteger(0, MaxNodesPerNetworkEvent);
            int nodeStartIndex = eventIndex * MaxNodesPerNetworkEvent;

            Vector2[] nodePositions = new Vector2[nodeStartIndex + nodeCount];
            for (int i = 0; i < nodes.Count && i < nodePositions.Length; i++)
            {
                nodePositions[i] = nodes[i];
            }

            for (int i = 0; i < nodeCount; i++)
            {
                nodePositions[nodeStartIndex + i] = new Vector2(msg.ReadSingle(), msg.ReadSingle());
            }

            if (nodePositions.Any(n => !MathUtils.IsValid(n)))
            {
                nodes.Clear();
                return;
            }

            nodes = nodePositions.ToList();
            UpdateSections();
            Drawable = nodes.Any();
        }
        
    }
}
