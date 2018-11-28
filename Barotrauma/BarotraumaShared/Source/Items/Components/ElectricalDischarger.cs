using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class ElectricalDischarger : Powered
    {
        private static List<ElectricalDischarger> list = new List<ElectricalDischarger>();
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

        [Serialize(100.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 5000.0f)]
        public float Range
        {
            get;
            set;
        }

        [Serialize(10.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1000.0f, ToolTip = "How much further can the discharge be carried when moving across walls.")]
        public float RangeMultiplierInWalls
        {
            get;
            set;
        }

        [Serialize(0.25f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1.0f)]
        public float Duration
        {
            get;
            set;
        }

        [Serialize(false, true), Editable()]
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

        private readonly List<Pair<Character,Node>> charactersInRange = new List<Pair<Character, Node>>();

        private bool charging;

        private float timer;

        private Attack attack;

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

            CurrPowerConsumption = powerConsumption;
            charging = true;
            timer = Duration;
            IsActive = true;
            return false;
        }

        public override void Update(float deltaTime, Camera cam)
        {
#if CLIENT
            frameOffset = Rand.Int(electricitySprite.FrameCount);
#endif
            if (timer > 0.0f)
            {
                if (charging)
                {
                    if (voltage > minVoltage || powerConsumption <= 0.0f)
                    {
                        Discharge();
                    }
                }
                timer -= deltaTime;
            }
            else
            {
                nodes.Clear();
                charactersInRange.Clear();
                IsActive = false;
            }

            voltage = 0.0f;
        }

        private void Discharge()
        {
            ApplyStatusEffects(ActionType.OnUse, 1.0f);
            FindNodes(item.WorldPosition, Range);
            if (attack != null)
            {
                foreach (Pair<Character, Node> characterInRange in charactersInRange)
                {
                    characterInRange.First.ApplyAttack(null, characterInRange.Second.WorldPosition, attack, 1.0f);
                }
            }
            DischargeProjSpecific();
            charging = false;
        }

        partial void DischargeProjSpecific();

        private void FindNodes(Vector2 worldPosition, float range)
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

            if (closestIndex == -1 || closestDist > currentRange) return;
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

                    Vector2 targetPos =
                        new Vector2(
                            MathHelper.Clamp(currPos.X, targetStructure.WorldRect.X, targetStructure.WorldRect.Right),
                            targetStructure.WorldPosition.Y + targetStructure.Rect.Height / 2 * yDir);

                    //create nodes from the current position to the closest point on the structure
                    AddNodesBetweenPoints(currPos, targetPos, ref parentNodeIndex);

                    //add a node at the closest point
                    nodes.Add(new Node(targetPos, parentNodeIndex));
                    int nodeIndex = nodes.Count - 1;
                    entitiesInRange.RemoveAt(closestIndex);

                    float newRange = currentRange - (targetStructure.Rect.Width / 2) * (1.0f / RangeMultiplierInWalls);
                    
                    //continue the discharge to the left edge of the structure and extend from there
                    int leftNodeIndex = nodeIndex;
                    Vector2 leftPos = new Vector2(targetStructure.WorldRect.X, targetPos.Y);
                    AddNodesBetweenPoints(targetPos, leftPos, ref leftNodeIndex);
                    nodes.Add(new Node(leftPos, leftNodeIndex));
                    FindNodes(entitiesInRange, leftPos, nodes.Count - 1, newRange);

                    //continue the discharge to the right edge of the structure and extend from there
                    int rightNodeIndex = nodeIndex;
                    Vector2 rightPos = new Vector2(targetStructure.WorldRect.Right, targetPos.Y);
                    AddNodesBetweenPoints(targetPos, rightPos, ref rightNodeIndex);
                    nodes.Add(new Node(rightPos, rightNodeIndex));
                    FindNodes(entitiesInRange, rightPos, nodes.Count - 1, newRange);
                }
                else
                {
                    int xDir = OutdoorsOnly && targetStructure.Submarine != null ?
                        Math.Sign(targetStructure.WorldPosition.X - targetStructure.Submarine.WorldPosition.X) :
                        Math.Sign(currPos.X - targetStructure.WorldPosition.X);

                    Vector2 targetPos = new Vector2(
                            targetStructure.WorldPosition.X - targetStructure.Rect.Width / 2 * xDir,
                            MathHelper.Clamp(currPos.Y, targetStructure.WorldRect.Y - targetStructure.Rect.Height, targetStructure.WorldRect.Y));

                    //create nodes from the current position to the closest point on the structure
                    AddNodesBetweenPoints(currPos, targetPos, ref parentNodeIndex);

                    //add a node at the closest point
                    nodes.Add(new Node(targetPos, parentNodeIndex));
                    int nodeIndex = nodes.Count - 1;
                    entitiesInRange.RemoveAt(closestIndex);

                    float newRange = currentRange - (targetStructure.Rect.Height / 2) * (1.0f / RangeMultiplierInWalls);

                    //continue the discharge to the top edge of the structure and extend from there
                    int topNodeIndex = nodeIndex;
                    Vector2 topPos = new Vector2(targetPos.X, targetStructure.WorldRect.Y);
                    AddNodesBetweenPoints(targetPos, topPos, ref topNodeIndex);
                    nodes.Add(new Node(topPos, topNodeIndex));
                    FindNodes(entitiesInRange, topPos, nodes.Count - 1, newRange);

                    //continue the discharge to the bottom edge of the structure and extend from there
                    int bottomNodeIndex = nodeIndex;
                    Vector2 bottomBos = new Vector2(targetPos.X, targetStructure.WorldRect.Y - targetStructure.Rect.Height);
                    AddNodesBetweenPoints(targetPos, bottomBos, ref bottomNodeIndex);
                    nodes.Add(new Node(bottomBos, bottomNodeIndex));
                    FindNodes(entitiesInRange, bottomBos, nodes.Count - 1, newRange);
                }
            }
            else if (entitiesInRange[closestIndex] is Character character)
            {
                Vector2 targetPos = character.WorldPosition;
                //create nodes from the current position to the closest point on the structure
                AddNodesBetweenPoints(currPos, targetPos, ref parentNodeIndex);
                nodes.Add(new Node(targetPos, parentNodeIndex));
                entitiesInRange.RemoveAt(closestIndex);
                charactersInRange.Add(new Pair<Character, Node>(character, nodes[parentNodeIndex]));
                FindNodes(entitiesInRange, targetPos, nodes.Count - 1, currentRange);
            }     
        }

        private void AddNodesBetweenPoints(Vector2 currPos, Vector2 targetPos, ref int parentNodeIndex)
        {
            Vector2 diff = targetPos - currPos;
            float dist = diff.Length();
            for (float x = MaxNodeDistance; x < dist - MaxNodeDistance; x += MaxNodeDistance)
            {
                nodes.Add(new Node(currPos + (diff / dist) * x, parentNodeIndex));
                parentNodeIndex = nodes.Count - 1;
            }
        }

        protected override void RemoveComponentSpecific()
        {
            list.Remove(this);
        }
    }
}
