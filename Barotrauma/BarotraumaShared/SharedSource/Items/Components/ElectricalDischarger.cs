using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class ElectricalDischarger : Powered, IServerSerializable
    {
        private static readonly List<ElectricalDischarger> list = new List<ElectricalDischarger>();
        public static IEnumerable<ElectricalDischarger> List
        {
            get { return list; }
        }

        const int MaxNodes = 100;
        const float MaxNodeDistance = 150.0f;

        public struct Node
        {
            public Vector2 WorldPosition;
            public int ParentIndex;
            public float Length;
            public float Angle;

            public Node(Vector2 worldPosition, int parentIndex, float length = 0.0f, float angle = 0.0f)
            {
                WorldPosition = worldPosition;
                ParentIndex = parentIndex;
                Length = length;
                Angle = angle;
            }
        }

        public override bool IsActive
        {
            get { return base.IsActive; }
            set
            {
                base.IsActive = value;
                if (!value)
                {
                    nodes.Clear();
                    charactersInRange.Clear();
                }
            }
        }

        [Serialize(500.0f, true, description: "How far the discharge can travel from the item.", alwaysUseInstanceValues: true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 5000.0f)]
        public float Range
        {
            get;
            set;
        }

        [Serialize(25.0f, true, description: "How much further can the discharge be carried when moving across walls.", alwaysUseInstanceValues: true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1000.0f)]
        public float RangeMultiplierInWalls
        {
            get;
            set;
        }

        [Serialize(0.25f, true, description: "The duration of an individual discharge (in seconds)."), Editable(MinValueFloat = 0.0f, MaxValueFloat = 60.0f, ValueStep = 0.1f, DecimalCount = 2)]
        public float Duration
        {
            get;
            set;
        }

        [Serialize(false, true, "If set to true, the discharge cannot travel inside the submarine nor shock anyone inside."), Editable]
        public bool OutdoorsOnly
        {
            get;
            set;
        }

        private readonly List<Node> nodes = new List<Node>();
        public IEnumerable<Node> Nodes
        {
            get { return nodes; }
        }

        private readonly List<(Character character, Node node)> charactersInRange = new List<(Character character, Node node)>();

        private bool charging;

        private float timer;

        private readonly Attack attack;

        public ElectricalDischarger(Item item, XElement element) : 
            base(item, element)
        {
            list.Add(this);

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "attack":
                    attack = new Attack(subElement, item.Name);
                    break;
                }
            }

            InitProjSpecific();
        }

        partial void InitProjSpecific();

        public override bool Use(float deltaTime, Character character = null)
        {
            //already active, do nothing
            if (IsActive) { return false; }
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return false; }
            if (character != null && !CharacterUsable) { return false; }

            charging = true;
            timer = Duration;
            IsActive = true;
#if SERVER
            if (GameMain.Server != null) { item.CreateServerEvent(this); }
#endif
            return false;
        }

        public override void Update(float deltaTime, Camera cam)
        {
#if CLIENT
            frameOffset = Rand.Int(electricitySprite.FrameCount);
#endif
            if (timer <= 0.0f)
            {
                IsActive = false;
                return;
            }

            timer -= deltaTime;
            if (charging)
            {
                if (GetAvailableBatteryPower() >= PowerConsumption)
                {
                    List<PowerContainer> batteries = GetConnectedBatteries();
                    float neededPower = PowerConsumption;
                    while (neededPower > 0.0001f && batteries.Count > 0)
                    {
                        batteries.RemoveAll(b => b.Charge <= 0.0001f || b.MaxOutPut <= 0.0001f);
                        float takePower = neededPower / batteries.Count;
                        takePower = Math.Min(takePower, batteries.Min(b => Math.Min(b.Charge * 3600.0f, b.MaxOutPut)));
                        foreach (PowerContainer battery in batteries)
                        {
                            neededPower -= takePower;
                            battery.Charge -= takePower / 3600.0f;
    #if SERVER
                            if (GameMain.Server != null) { battery.Item.CreateServerEvent(battery); }
    #endif
                        }
                    }
                    Discharge();

                }
                else if (Voltage > MinVoltage)
                {
                    Discharge();
                }
            }
        }

        /// <summary>
        /// Discharge coil only draws power when charging
        /// </summary>
        /// <param name="conn"></param>
        /// <returns></returns>
        public override float ConnCurrConsumption(Connection conn = null)
        {
            return charging && IsActive ? PowerConsumption : 0;
        }

        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            base.UpdateBroken(deltaTime, cam);
            nodes.Clear();
            charactersInRange.Clear();
        }

        private void Discharge()
        {
            ApplyStatusEffects(ActionType.OnUse, 1.0f);
            FindNodes(item.WorldPosition, Range);
            if (attack != null)
            {
                foreach ((Character character, Node node) in charactersInRange)
                {
                    if (character == null || character.Removed) { continue; }
                    character.ApplyAttack(null, node.WorldPosition, attack, 1.0f);
                }
            }
            DischargeProjSpecific();
            charging = false;
        }

        partial void DischargeProjSpecific();

        public void FindNodes(Vector2 worldPosition, float range)
        {
            //see which submarines are within range so we can skip structures that are in far-away subs
            List<Submarine> submarinesInRange = new List<Submarine>();
            foreach (Submarine sub in Submarine.Loaded)
            {
                if (item.Submarine == sub)
                {
                    submarinesInRange.Add(sub);
                }
                else
                {
                    Rectangle subBorders = new Rectangle(
                        sub.Borders.X - (int)range, sub.Borders.Y + (int)range,
                        sub.Borders.Width + (int)(range * 2), sub.Borders.Height + (int)(range * 2));
                    subBorders.Location += MathUtils.ToPoint(sub.SubBody.Position);
                    if (Submarine.RectContains(subBorders, worldPosition))
                    {
                        submarinesInRange.Add(sub);
                    }
                }
            }

            //get all walls within range
            List<Entity> entitiesInRange = new List<Entity>(100);
            foreach (Structure structure in Structure.WallList)
            {
                if (!structure.HasBody || structure.IsPlatform) { continue; }
                if (structure.Submarine != null&& !submarinesInRange.Contains(structure.Submarine)) { continue; }

                var structureWorldRect = structure.WorldRect;
                if (worldPosition.X < structureWorldRect.X - range) continue;
                if (worldPosition.X > structureWorldRect.Right + range) continue;
                if (worldPosition.Y > structureWorldRect.Y + range) continue;
                if (worldPosition.Y < structureWorldRect.Y -structureWorldRect.Height - range) continue;

                if (structure.Submarine != null)
                {
                    if (!submarinesInRange.Contains(structure.Submarine)) { continue; }
                    if (OutdoorsOnly)
                    {
                        //check if the structure is within a hull
                        //add a small offset away from the sub's center so structures right at the edge of a hull are still valid
                        Vector2 offset = Vector2.Normalize(structure.WorldPosition - structure.Submarine.WorldPosition);
                        if (Hull.FindHull(structure.Position + offset * Submarine.GridSize, useWorldCoordinates: false) != null) { continue; }
                    }
                }

                entitiesInRange.Add(structure);
            }

            foreach (Character character in Character.CharacterList)
            {
                if (!character.Enabled) continue;
                if (OutdoorsOnly && character.Submarine != null) continue;
                if (character.Submarine != null && !submarinesInRange.Contains(character.Submarine)) continue;

                if (Vector2.DistanceSquared(character.WorldPosition, worldPosition) < range * range * RangeMultiplierInWalls)
                {
                    entitiesInRange.Add(character);
                }
            }

            nodes.Clear();
            nodes.Add(new Node(worldPosition, -1));
            FindNodes(entitiesInRange, worldPosition, 0, range);

            //construct final nodes (w/ lengths and angles so they don't have to be recalculated when rendering the discharge)
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].ParentIndex < 0) continue;
                Node parentNode = nodes[nodes[i].ParentIndex];
                float length = Vector2.Distance(nodes[i].WorldPosition, parentNode.WorldPosition) * Rand.Range(1.0f, 1.25f);
                float angle = MathUtils.VectorToAngle(parentNode.WorldPosition - nodes[i].WorldPosition);
                nodes[i] = new Node(nodes[i].WorldPosition, nodes[i].ParentIndex, length, angle);
            }
        }

        private void FindNodes(List<Entity> entitiesInRange, Vector2 currPos, int parentNodeIndex, float currentRange)
        {
            if (currentRange <= 0.0f || nodes.Count >= MaxNodes) return;

            //find the closest structure
            int closestIndex = -1;
            float closestDist = float.MaxValue;
            for (int i = 0; i < entitiesInRange.Count; i++)
            {
                float dist = float.MaxValue;

                if (entitiesInRange[i] is Structure structure)
                {
                    if (structure.IsHorizontal)
                    {
                        dist = Math.Abs(structure.WorldPosition.Y - currPos.Y);
                        if (currPos.X < structure.WorldRect.X)
                            dist += structure.WorldRect.X - currPos.X;
                        else if (currPos.X > structure.WorldRect.Right)
                            dist += currPos.X - structure.WorldRect.Right;
                    }
                    else
                    {
                        dist = Math.Abs(structure.WorldPosition.X - currPos.X);
                        if (currPos.Y < structure.WorldRect.Y - structure.Rect.Height)
                            dist += (structure.WorldRect.Y - structure.Rect.Height) - currPos.Y;
                        else if (currPos.Y > structure.WorldRect.Y)
                            dist += currPos.Y - structure.WorldRect.Y;
                    }
                }
                else if (entitiesInRange[i] is Character character)
                {
                    dist = Vector2.Distance(character.WorldPosition, currPos);
                }

                if (dist < closestDist)
                {
                    closestIndex = i;
                    closestDist = dist;
                }
            }

            if (closestIndex == -1 || closestDist > currentRange)
            {
                //nothing in range, create some arcs to random directions
                for (int i = 0; i < Rand.Int(4); i++)
                {
                    Vector2 targetPos = currPos + Rand.Vector(MaxNodeDistance * Rand.Range(0.5f, 1.5f));
                    nodes.Add(new Node(targetPos, parentNodeIndex));
                }
                return;
            }
            currentRange -= closestDist;

            if (entitiesInRange[closestIndex] is Structure targetStructure)
            {
                if (targetStructure.IsHorizontal)
                {
                    //which side of the structure to add the nodes to
                    //if outside the sub, use the sides that's furthers from the sub's center position
                    //otherwise the side that's closer to the previous node
                    int yDir = OutdoorsOnly && targetStructure.Submarine != null ?
                        Math.Sign(targetStructure.WorldPosition.Y - targetStructure.Submarine.WorldPosition.Y) :
                        Math.Sign(currPos.Y - targetStructure.WorldPosition.Y);

                    int sectionIndex = targetStructure.FindSectionIndex(currPos, world: true, clamp: true);
                    if (sectionIndex == -1) { return; }
                    Vector2 sectionPos = targetStructure.SectionPosition(sectionIndex, world: true);
                    Vector2 targetPos =
                        new Vector2(
                            MathHelper.Clamp(sectionPos.X, targetStructure.WorldRect.X, targetStructure.WorldRect.Right),
                            sectionPos.Y + targetStructure.BodyHeight / 2 * yDir);

                    //create nodes from the current position to the closest point on the structure
                    AddNodesBetweenPoints(currPos, targetPos, 0.25f, ref parentNodeIndex);

                    //add a node at the closest point
                    nodes.Add(new Node(targetPos, parentNodeIndex));
                    int nodeIndex = nodes.Count - 1;
                    entitiesInRange.RemoveAt(closestIndex);

                    float newRange = currentRange - (targetStructure.Rect.Width / 2) * (1.0f / RangeMultiplierInWalls);
                    
                    //continue the discharge to the left edge of the structure and extend from there
                    int leftNodeIndex = nodeIndex;
                    Vector2 leftPos = targetStructure.SectionPosition(0, world: true);
                    leftPos.Y += targetStructure.BodyHeight / 2 * yDir;
                    AddNodesBetweenPoints(targetPos, leftPos, 0.05f, ref leftNodeIndex);
                    nodes.Add(new Node(leftPos, leftNodeIndex));
                    FindNodes(entitiesInRange, leftPos, nodes.Count - 1, newRange);

                    //continue the discharge to the right edge of the structure and extend from there
                    int rightNodeIndex = nodeIndex;
                    Vector2 rightPos = targetStructure.SectionPosition(targetStructure.SectionCount - 1, world: true);
                    leftPos.Y += targetStructure.BodyHeight / 2 * yDir;
                    AddNodesBetweenPoints(targetPos, rightPos, 0.05f, ref rightNodeIndex);
                    nodes.Add(new Node(rightPos, rightNodeIndex));
                    FindNodes(entitiesInRange, rightPos, nodes.Count - 1, newRange);
                }
                else
                {
                    int xDir = OutdoorsOnly && targetStructure.Submarine != null ?
                        Math.Sign(targetStructure.WorldPosition.X - targetStructure.Submarine.WorldPosition.X) :
                        Math.Sign(currPos.X - targetStructure.WorldPosition.X);

                    int sectionIndex = targetStructure.FindSectionIndex(currPos, world: true, clamp: true);
                    if (sectionIndex == -1) { return; }
                    Vector2 sectionPos = targetStructure.SectionPosition(sectionIndex, world: true);

                    Vector2 targetPos = new Vector2(
                            sectionPos.X + targetStructure.BodyWidth / 2 * xDir,
                            MathHelper.Clamp(sectionPos.Y, targetStructure.WorldRect.Y - targetStructure.Rect.Height, targetStructure.WorldRect.Y));

                    //create nodes from the current position to the closest point on the structure
                    AddNodesBetweenPoints(currPos, targetPos, 0.25f, ref parentNodeIndex);

                    //add a node at the closest point
                    nodes.Add(new Node(targetPos, parentNodeIndex));
                    int nodeIndex = nodes.Count - 1;
                    entitiesInRange.RemoveAt(closestIndex);

                    float newRange = currentRange - (targetStructure.Rect.Height / 2) * (1.0f / RangeMultiplierInWalls);

                    //continue the discharge to the top edge of the structure and extend from there
                    int topNodeIndex = nodeIndex;
                    Vector2 topPos = targetStructure.SectionPosition(0, world: true);
                    topPos.X += targetStructure.BodyWidth / 2 * xDir; 
                    AddNodesBetweenPoints(targetPos, topPos, 0.05f, ref topNodeIndex);
                    nodes.Add(new Node(topPos, topNodeIndex));
                    FindNodes(entitiesInRange, topPos, nodes.Count - 1, newRange);

                    //continue the discharge to the bottom edge of the structure and extend from there
                    int bottomNodeIndex = nodeIndex;
                    Vector2 bottomBos = targetStructure.SectionPosition(targetStructure.SectionCount - 1, world: true);
                    bottomBos.X += targetStructure.BodyWidth / 2 * xDir;
                    AddNodesBetweenPoints(targetPos, bottomBos, 0.05f, ref bottomNodeIndex);
                    nodes.Add(new Node(bottomBos, bottomNodeIndex));
                    FindNodes(entitiesInRange, bottomBos, nodes.Count - 1, newRange);
                }

                //check if any character is close to this structure
                for (int j = 0; j < entitiesInRange.Count; j++)
                {
                    var otherEntity = entitiesInRange[j];
                    if (!(otherEntity is Character character)) continue;
                    if (OutdoorsOnly && character.Submarine != null) continue;

                    if (targetStructure.IsHorizontal)
                    {
                        if (otherEntity.WorldPosition.X < targetStructure.WorldRect.X) continue;
                        if (otherEntity.WorldPosition.X > targetStructure.WorldRect.Right) continue;
                        if (Math.Abs(otherEntity.WorldPosition.Y - targetStructure.WorldPosition.Y) > currentRange) continue;
                    }
                    else
                    {
                        if (otherEntity.WorldPosition.Y < targetStructure.WorldRect.Y - targetStructure.Rect.Height) continue;
                        if (otherEntity.WorldPosition.Y > targetStructure.WorldRect.Y) continue;
                        if (Math.Abs(otherEntity.WorldPosition.X - targetStructure.WorldPosition.X) > currentRange) continue;
                    }
                    float closestNodeDistSqr = float.MaxValue;
                    int closestNodeIndex = -1;
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        float distSqr = Vector2.DistanceSquared(character.WorldPosition, nodes[i].WorldPosition);
                        if (distSqr < closestNodeDistSqr)
                        {
                            closestNodeDistSqr = distSqr;
                            closestNodeIndex = i;
                        }
                    }
                    if (closestNodeIndex > -1)
                    {
                        FindNodes(entitiesInRange, nodes[closestNodeIndex].WorldPosition, closestNodeIndex, currentRange - (float)Math.Sqrt(closestNodeDistSqr));
                    }
                }
            }
            else if (entitiesInRange[closestIndex] is Character character)
            {
                Vector2 targetPos = character.WorldPosition;
                //create nodes from the current position to the closest point on the character
                AddNodesBetweenPoints(currPos, targetPos, 0.25f, ref parentNodeIndex);
                nodes.Add(new Node(targetPos, parentNodeIndex));
                entitiesInRange.RemoveAt(closestIndex);
                charactersInRange.Add((character, nodes[parentNodeIndex]));
                FindNodes(entitiesInRange, targetPos, nodes.Count - 1, currentRange);
            }     
        }

        private void AddNodesBetweenPoints(Vector2 currPos, Vector2 targetPos, float variance, ref int parentNodeIndex)
        {
            Vector2 diff = targetPos - currPos;
            float dist = diff.Length();
            Vector2 normal = new Vector2(-diff.Y, diff.X) / dist;
            for (float x = MaxNodeDistance; x < dist - MaxNodeDistance; x += MaxNodeDistance * Rand.Range(0.5f, 1.5f))
            {
                //0 at the edges, 1 at the center
                float normalOffset = (0.5f - Math.Abs(x / dist - 0.5f)) * 2.0f;
                normalOffset *= variance * dist * Rand.Range(-1.0f, 1.0f);

                nodes.Add(new Node(currPos + (diff / dist) * x + normal * normalOffset, parentNodeIndex));
                parentNodeIndex = nodes.Count - 1;
            }
        }

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            switch (connection.Name)
            {
                case "activate":
                case "use":
                case "trigger_in":
                    if (signal.value != "0")
                    {
                        item.Use(1.0f, null);
                    }
                    break;
            }
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            list.Remove(this);
        }

        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            //no further data needed, the event just triggers the discharge
        }
    }
}
