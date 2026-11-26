using Barotrauma.Extensions;
using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

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

        [Serialize(500.0f, IsPropertySaveable.No, description: "How far the discharge branches can travel from the main lightning arc."), Editable(MinValueFloat = 0.0f, MaxValueFloat = 5000.0f)]
        public float Range
        {
            get;
            set;
        }

        [Serialize(25.0f, IsPropertySaveable.No, description: "How much further can the discharge be carried when moving across walls."), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1000.0f)]
        public float RangeMultiplierInWalls
        {
            get;
            set;
        }
        
        [Serialize(0.0f, IsPropertySaveable.No, description: "How far the main lightning arc travels from the item."), Editable(MinValueFloat = 0.0f, MaxValueFloat = 5000.0f)]
        public float RaycastRange { get; set; }

        [Serialize(0.25f, IsPropertySaveable.No, description: "The duration of an individual discharge (in seconds)."), Editable(MinValueFloat = 0.0f, MaxValueFloat = 60.0f, ValueStep = 0.1f, DecimalCount = 2)]
        public float DischargeDuration
        {
            get;
            set;
        }

        [Serialize(0.25f, IsPropertySaveable.No), Editable(MinValueFloat = 0.0f, MaxValueFloat = 60.0f, ValueStep = 0.1f, DecimalCount = 2)]
        public float Reload
        {
            get;
            set;
        }
        
        [Serialize(0.5f, IsPropertySaveable.No, description: "Controls how chaotic the main lightning arc is. A higher value creates jagged, spreading arcs with more random paths, while a lower value results in straighter, more predictable lines."), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1.0f, ValueStep = 0.01f, DecimalCount = 2)]
        public float MainArcVariance
        {
            get;
            set;
        }
        
        [Serialize(0.25f, IsPropertySaveable.No, description: "Controls how chaotic the child lightning arcs, traveling in air/water, are. A higher value creates jagged, spreading arcs with more random paths, while a lower value results in straighter, more predictable lines."), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1.0f, ValueStep = 0.01f, DecimalCount = 2)]
        public float ChildArcVariance
        {
            get;
            set;
        }
        
        [Serialize(0.05f, IsPropertySaveable.No, description: "Controls how chaotic the child lightning arcs, traveling via structures, are. A higher value creates jagged, spreading arcs with more random paths, while a lower value results in straighter, more predictable lines."), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1.0f, ValueStep = 0.01f, DecimalCount = 2)]
        public float ChildArcVarianceAcrossStructures
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.Yes, "If set to true, the discharge cannot travel inside the submarine nor shock anyone inside."), Editable]
        public bool OutdoorsOnly
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.No)]
        public bool IgnoreUser
        {
            get;
            set;
        }

        private readonly List<Node> nodes = new List<Node>();
        public IEnumerable<Node> Nodes
        {
            get { return nodes; }
        }
        
        
        private readonly Attack originalAttack;
        public Attack Attack
        {
            get => Item.FindAttackOverride() ?? originalAttack;
            private init => originalAttack = value;
        }

        private readonly List<(Character character, Node node)> charactersInRange = new List<(Character character, Node node)>();
        private bool charging;
        private float timer;
        private Character user;
        private float reloadTimer;

        /// <summary>
        /// Ammo of the turret this discharger belongs to, captured when the discharge is triggered by the turret's shot. Can modify the discharge.
        /// </summary>
        private TurretAmmo dischargeAmmo;

        private float CurrentRange => Range * (dischargeAmmo?.DischargeRangeMultiplier ?? 1.0f);
        private float CurrentRaycastRange => RaycastRange * (dischargeAmmo?.DischargeRaycastRangeMultiplier ?? 1.0f);
        private float CurrentRangeMultiplierInWalls => RangeMultiplierInWalls * (dischargeAmmo?.DischargeWallRangeMultiplier ?? 1.0f);
        private float CurrentDischargeDuration => DischargeDuration * (dischargeAmmo?.DischargeDurationMultiplier ?? 1.0f);
        private float CurrentMainArcVariance => dischargeAmmo?.DischargeMainArcVariance ?? MainArcVariance;
        private float CurrentChildArcVariance => dischargeAmmo?.DischargeChildArcVariance ?? ChildArcVariance;

        /// <summary>
        /// The ammo of the turret's latest shot, if the discharger is part of a turret.
        /// </summary>
        private TurretAmmo GetTurretAmmo() => item.GetComponent<Turret>()?.LastLaunchedTurretAmmo;

        public ElectricalDischarger(Item item, ContentXElement element) : 
            base(item, element)
        {
            list.Add(this);

            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "attack":
                    Attack = new Attack(subElement, item.Name);
                    break;
                }
            }
            
            // Backward support. The name of the property got changed due to it conflicting with StatusEffect.Duration (hence couldn't change the property via status effects).
            DischargeDuration = element.GetAttributeFloat("Duration", DischargeDuration);

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
            dischargeAmmo = GetTurretAmmo();
            timer = CurrentDischargeDuration;
            IsActive = true;
            user = character ?? item.GetComponent<Turret>()?.User;
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
                if (reloadTimer > 0.0f)
                {
                    reloadTimer -= deltaTime;
                    return;
                }
                IsActive = false;
                return;
            }

            timer -= deltaTime;
            if (charging)
            {
                if (CheckPower())
                {
                    Discharge();
                }
            }
        }
        
        private bool CheckPower()
        {
            if (PowerConsumption > 0)
            {
                if (item.Connections == null)
                {
                    //no connections and can't be wired = must be powered by something like batteries
                    return HasPower;
                }
                return GetAvailableInstantaneousBatteryPower() >= PowerConsumption;
            }
            return true;
        }
        
        private void TakePower()
        {
            var batteries = GetDirectlyConnectedBatteries().Where(static b => !b.OutputDisabled && b.Charge > 0.0001f && b.MaxOutPut > 0.0001f);
            float neededPower = PowerConsumption;
            while (neededPower > 0.0001f && batteries.Any())
            {
                float takePower = neededPower / batteries.Count();
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
        }

        /// <summary>
        /// Discharge coil doesn't consume grid power, directly takes from the batteries on its grid instead.
        /// </summary>
        public override float GetCurrentPowerConsumption(Connection conn = null)
        {
            return 0;
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
            if (!CheckPower())
            {
                // Do another check for the power, because the OnUse status effect might change the power consumption (does not happen in the vanilla content though)
                return;
            }
            reloadTimer = Reload;
            // Set the timer again because the status effect might have changed it. Otherwise, it would apply only on the next discharge.
            timer = CurrentDischargeDuration;
            if (PowerConsumption > 0)
            {
                TakePower();
            }
            FindNodes(item.WorldPosition, CurrentRange);
            Attack activeAttack = Attack;
            if (activeAttack != null)
            {
                foreach ((Character character, Node node) in charactersInRange)
                {
                    if (character == null || character.Removed) { continue; }

                    float voltageFactor = MathHelper.Clamp(Voltage, 1.0f, MaxOverVoltageFactor);
                    Limb targetLimb = character.AnimController.Limbs.MinBy(l => Vector2.DistanceSquared(l.WorldPosition, node.WorldPosition));
                    character.ApplyAttack(user, node.WorldPosition, activeAttack, voltageFactor, 
                        impulseDirection: character.WorldPosition - node.WorldPosition, targetLimb: targetLimb);
                    activeAttack.ApplyStatusEffects(ActionType.OnSuccess, user, targetLimb, node.WorldPosition, voltageFactor);
                }
            }
            DischargeProjSpecific();
            charging = false;
        }

        partial void DischargeProjSpecific();

        public void FindNodes(Vector2 worldPosition, float range)
        {
            if (CurrentRaycastRange > 0.0f)
            {
                Vector2 aimDir = new Vector2(1, 0);
                if (item.body != null)
                {
                    float angle = item.body.Rotation;
                    aimDir = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * item.body.Dir;
                }
                else if (item.GetComponent<Turret>() is Turret turret)
                {
                    aimDir = turret.GetFiringDirection();
                }
                worldPosition += aimDir * CurrentRaycastRange;
            }

            //see which submarines are within range so we can skip structures that are in far-away subs
            List<Submarine> submarinesInRange = new List<Submarine>();
            foreach (Submarine sub in Submarine.Loaded)
            {
                if (item.Submarine == sub)
                {
                    submarinesInRange.Add(sub);
                }
                else if (sub != null)
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

            //get all walls within range the arc could potentially hit
            List<Entity> entitiesInRange = new List<Entity>(100);
            foreach (Structure structure in Structure.WallList)
            {
                if (!structure.HasBody || structure.IsPlatform) { continue; }
                if (structure.Submarine != null && !submarinesInRange.Contains(structure.Submarine)) { continue; }

                var structureWorldRect = structure.WorldRect;
                if (worldPosition.X < structureWorldRect.X - range) { continue; }
                if (worldPosition.X > structureWorldRect.Right + range) { continue; }
                if (worldPosition.Y > structureWorldRect.Y + range) { continue; }
                if (worldPosition.Y < structureWorldRect.Y - structureWorldRect.Height - range) { continue; }

                if (structure.Submarine != null)
                {
                    if (!submarinesInRange.Contains(structure.Submarine)) { continue; }
                    if (OutdoorsOnly)
                    {
                        //check if there's a hull at either side of the wall
                        Vector2 normal = new Vector2(
                            (float)-Math.Sin(structure.IsHorizontal ? -structure.BodyRotation : MathHelper.PiOver2 - structure.BodyRotation),
                            (float)Math.Cos(structure.IsHorizontal ? -structure.BodyRotation : MathHelper.PiOver2 - structure.BodyRotation));
                        Vector2 structurePos = structure.Position;
                        float offsetAmount = Submarine.GridSize.X * 2;
                        if (structure.HasBody)
                        {
                            structurePos = ConvertUnits.ToDisplayUnits(structure.Bodies.First().Position);
                            offsetAmount = Math.Max(
                                offsetAmount,
                                structure.IsHorizontal ? structure.BodyHeight : structure.BodyWidth);
                        }
                        if (Hull.FindHull(structurePos + normal * offsetAmount, useWorldCoordinates: false) != null &&
                            Hull.FindHull(structurePos - normal * offsetAmount, useWorldCoordinates: false) != null) 
                        { 
                            continue; 
                        }
                    }
                }

                entitiesInRange.Add(structure);
            }


            nodes.Clear();
            if (CurrentRaycastRange > 0.0f) 
            {
                Vector2 startPosition = item.WorldPosition;
                if (item.GetComponent<Turret>() is Turret turret)
                {
                    startPosition = turret.GetFiringPosition();
                }
                nodes.Add(new Node(startPosition, -1));
                int parentNodeIndex = 0;
                AddNodesBetweenPoints(item.WorldPosition, worldPosition, CurrentMainArcVariance, ref parentNodeIndex);
            }
            else
            {
                nodes.Add(new Node(worldPosition, -1));
            }

            //get all characters within range the arc could potentially hit
            float totalRange = CurrentRaycastRange + range;
            foreach (Character character in Character.CharacterList)
            {
                if (!character.Enabled) { continue; }
                if (IgnoreUser && character == user) { continue; }
                if (OutdoorsOnly && character.Submarine != null) { continue; }
                if (character.Submarine != null && !submarinesInRange.Contains(character.Submarine)) { continue; }

                if (Vector2.DistanceSquared(character.WorldPosition, worldPosition) < totalRange * totalRange * CurrentRangeMultiplierInWalls)
                {
                    entitiesInRange.Add(character);
                }
                //if the weapon does a raycast, check distance to the ray too (not just the end of the ray)
                if (CurrentRaycastRange > 0)
                {
                    float distSqr = MathUtils.LineSegmentToPointDistanceSquared(worldPosition, item.WorldPosition, character.WorldPosition);
                    //if the distance from the initial raycast to the character is small (e.g. goes through the character), we know it must hit
                    if (distSqr < range * range * CurrentRangeMultiplierInWalls)
                    {
                        if (!entitiesInRange.Contains(character)) { entitiesInRange.Add(character); }     
                        if (FindClosestNode(character.WorldPosition, out _) is int closestNodeIndex and >= 0)
                        {
                            charactersInRange.Add((character, nodes[closestNodeIndex]));
                        }
                    }
                }
            }

            FindNodes(entitiesInRange, worldPosition, nodes.Count - 1, range);

            //construct final nodes (w/ lengths and angles so they don't have to be recalculated when rendering the discharge)
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].ParentIndex < 0) { continue; }
                Node parentNode = nodes[nodes[i].ParentIndex];
                float length = Vector2.Distance(nodes[i].WorldPosition, parentNode.WorldPosition) * Rand.Range(1.0f, 1.25f);
                float angle = MathUtils.VectorToAngle(parentNode.WorldPosition - nodes[i].WorldPosition);
                nodes[i] = new Node(nodes[i].WorldPosition, nodes[i].ParentIndex, length, angle);
            }
        }

        private void FindNodes(List<Entity> entitiesInRange, Vector2 currPos, int parentNodeIndex, float currentRange)
        {
            if (currentRange <= 0.0f || nodes.Count >= MaxNodes) { return; }

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
                        {
                            dist += structure.WorldRect.X - currPos.X;
                        }
                        else if (currPos.X > structure.WorldRect.Right)
                        {
                            dist += currPos.X - structure.WorldRect.Right;
                        }
                    }
                    else
                    {
                        dist = Math.Abs(structure.WorldPosition.X - currPos.X);
                        if (currPos.Y < structure.WorldRect.Y - structure.Rect.Height)
                        {
                            dist += (structure.WorldRect.Y - structure.Rect.Height) - currPos.Y;
                        }
                        else if (currPos.Y > structure.WorldRect.Y)
                        {
                            dist += currPos.Y - structure.WorldRect.Y;
                        }
                    }
                }
                else if (entitiesInRange[i] is Character character)
                {
                    dist = MathF.Sqrt(MathUtils.LineSegmentToPointDistanceSquared(currPos, nodes[parentNodeIndex].WorldPosition, character.WorldPosition));
                }

                if (OutdoorsOnly)
                {
                    //rough check to prevent the arch from going through hulls:
                    //if the middlepoint between the current position and the target is inside hulls, the arc would need to go through a hull -> let's disallow that
                    Vector2 middlePos = (currPos + entitiesInRange[i].WorldPosition) / 2;
                    if (Hull.FindHull(middlePos, useWorldCoordinates: true) != null)
                    {
                        continue;
                    }
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
                int createdArcs = 0;
                int randomArcCount = Rand.Int(4);
                //max 20 attempts to create nodes that aren't inside hulls
                const int MaxAttempts = 20;
                for (int i = 0; i< MaxAttempts; i++)
                {
                    Vector2 targetPos = currPos + Rand.Vector(MaxNodeDistance * Rand.Range(0.5f, 1.5f));
                    if (OutdoorsOnly && Hull.FindHull(currPos, useWorldCoordinates: true) != null) { continue; }
                    nodes.Add(new Node(targetPos, parentNodeIndex));
                    createdArcs++;
                    if (createdArcs >= randomArcCount) { break; }
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
                    AddNodesBetweenPoints(currPos, targetPos, CurrentChildArcVariance, ref parentNodeIndex);

                    //add a node at the closest point
                    nodes.Add(new Node(targetPos, parentNodeIndex));
                    int nodeIndex = nodes.Count - 1;
                    entitiesInRange.RemoveAt(closestIndex);
                    
                    //continue the discharge to the left edge of the structure and extend from there
                    int leftNodeIndex = nodeIndex;
                    Vector2 leftPos = targetStructure.SectionPosition(0, world: true);
                    leftPos.Y += targetStructure.BodyHeight / 2 * yDir;
                    AddMinorNodes(targetPos, leftPos, ChildArcVarianceAcrossStructures, ref leftNodeIndex);

                    //continue the discharge to the right edge of the structure and extend from there
                    int rightNodeIndex = nodeIndex;
                    Vector2 rightPos = targetStructure.SectionPosition(targetStructure.SectionCount - 1, world: true);
                    rightPos.Y += targetStructure.BodyHeight / 2 * yDir;
                    AddMinorNodes(targetPos, rightPos, ChildArcVarianceAcrossStructures, ref rightNodeIndex);
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
                    AddNodesBetweenPoints(currPos, targetPos, CurrentChildArcVariance, ref parentNodeIndex);

                    //add a node at the closest point
                    nodes.Add(new Node(targetPos, parentNodeIndex));
                    int nodeIndex = nodes.Count - 1;
                    entitiesInRange.RemoveAt(closestIndex);

                    //continue the discharge to the top edge of the structure and extend from there
                    int topNodeIndex = nodeIndex;
                    Vector2 topPos = targetStructure.SectionPosition(0, world: true);
                    topPos.X += targetStructure.BodyWidth / 2 * xDir; 
                    AddMinorNodes(targetPos, topPos, ChildArcVarianceAcrossStructures, ref topNodeIndex);

                    //continue the discharge to the bottom edge of the structure and extend from there
                    int bottomNodeIndex = nodeIndex;
                    Vector2 bottomPos = targetStructure.SectionPosition(targetStructure.SectionCount - 1, world: true);
                    bottomPos.X += targetStructure.BodyWidth / 2 * xDir;
                    AddMinorNodes(targetPos, bottomPos, ChildArcVarianceAcrossStructures, ref bottomNodeIndex);
                }
                
                void AddMinorNodes(Vector2 position1, Vector2 position2, float variance, ref int index)
                {
                    AddNodesBetweenPoints(position1, position2, variance, ref index);
                    nodes.Add(new Node(position1, index));
                    float range = currentRange - (targetStructure.Rect.Height / 2f) * (1.0f / CurrentRangeMultiplierInWalls);
                    FindNodes(entitiesInRange, position1, nodes.Count - 1, range);
                }

                //check if any character is close to this structure
                for (int j = 0; j < entitiesInRange.Count; j++)
                {
                    var otherEntity = entitiesInRange[j];
                    if (otherEntity is not Character character) { continue; }
                    if (IgnoreUser && character == user) { continue; }
                    if (OutdoorsOnly && character.Submarine != null) { continue; }

                    Vector2 characterMin = new Vector2(character.AnimController.Limbs.Min(l => l.WorldPosition.X), character.AnimController.Limbs.Min(l => l.WorldPosition.Y));
                    Vector2 characterMax = new Vector2(character.AnimController.Limbs.Max(l => l.WorldPosition.X), character.AnimController.Limbs.Max(l => l.WorldPosition.Y));
                    if (targetStructure.IsHorizontal)
                    {
                        if (characterMax.X < targetStructure.WorldRect.X) { continue; }
                        if (characterMin.X > targetStructure.WorldRect.Right) { continue; }
                        if (Math.Abs(characterMin.Y - targetStructure.WorldPosition.Y) > currentRange && 
                            Math.Abs(characterMax.Y - targetStructure.WorldPosition.Y) > currentRange) 
                        {
                            continue;
                        }
                    }
                    else
                    {
                        if (characterMax.Y < targetStructure.WorldRect.Y - targetStructure.Rect.Height) { continue; }
                        if (characterMin.Y > targetStructure.WorldRect.Y) { continue; }
                        if (Math.Abs(characterMin.X - targetStructure.WorldPosition.X) > currentRange &&
                            Math.Abs(characterMax.X - targetStructure.WorldPosition.X) > currentRange) 
                        { 
                            continue; 
                        }
                    }
                    if (charactersInRange.All(c => c.character != character))
                    {
                        charactersInRange.Add((character, nodes[parentNodeIndex]));
                    }
                    if (FindClosestNode(character.WorldPosition, out float closestNodeDist) is int closestNodeIndex and >= 0)
                    {
                        FindNodes(entitiesInRange, nodes[closestNodeIndex].WorldPosition, closestNodeIndex, currentRange - closestNodeDist);
                    }
                }
            }
            else if (entitiesInRange[closestIndex] is Character character)
            {
                Vector2 targetPos = character.WorldPosition;
                //create nodes from the current position to the closest point on the character
                AddNodesBetweenPoints(currPos, targetPos, CurrentChildArcVariance, ref parentNodeIndex);
                nodes.Add(new Node(targetPos, parentNodeIndex));
                entitiesInRange.RemoveAt(closestIndex);
                if (charactersInRange.All(c => c.character != character))
                {
                    charactersInRange.Add((character, nodes[parentNodeIndex]));
                }
                FindNodes(entitiesInRange, targetPos, nodes.Count - 1, currentRange);
            }     
        }
        
        private int FindClosestNode(Vector2 worldPosition, out float closestNodeDist)
        {
            closestNodeDist = float.MaxValue;
            float closestNodeDistSqr = float.MaxValue;
            int closestNodeIndex = -1;
            for (int i = 0; i < nodes.Count; i++)
            {
                float distSqr = Vector2.DistanceSquared(worldPosition, nodes[i].WorldPosition);
                if (distSqr < closestNodeDistSqr)
                {
                    closestNodeDistSqr = distSqr;
                    closestNodeIndex = i;
                }
            }
            closestNodeDist = MathF.Sqrt(closestNodeDistSqr);
            return closestNodeIndex;
        }

        private void AddNodesBetweenPoints(Vector2 currPos, Vector2 targetPos, float variance, ref int parentNodeIndex)
        {
            Vector2 diff = targetPos - currPos;
            float dist = diff.Length();
            if (dist <= 0) { return; }
            Vector2 normal = new Vector2(-diff.Y, diff.X) / dist;
            for (float x = MaxNodeDistance; x < dist - MaxNodeDistance; x += MaxNodeDistance * Rand.Range(0.5f, 1.0f))
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
                        item.Use(1.0f);
                    }
                    break;
            }
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            list.Remove(this);
        }

        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            msg.WriteUInt16(user?.ID ?? Entity.NullEntityID);
        }
    }
}
