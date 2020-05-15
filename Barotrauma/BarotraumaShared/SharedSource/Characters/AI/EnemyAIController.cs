using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Linq;

namespace Barotrauma
{
    partial class EnemyAIController : AIController
    {
        public static bool DisableEnemyAI;

        /// <summary>
        /// Enable the character to attack the outposts and the characters inside them. Disabled by default.
        /// </summary>
        public bool TargetOutposts;

        // TODO: use a struct?
        class WallTarget
        {
            public Vector2 Position;
            public Structure Structure;
            public int SectionIndex;

            public WallTarget(Vector2 position, Structure structure = null, int sectionIndex = -1)
            {
                Position = position;
                Structure = structure;
                SectionIndex = sectionIndex;
            }
        }

        private readonly float updateTargetsInterval = 1;
        private readonly float updateMemoriesInverval = 1;
        private readonly float attackLimbResetInterval = 2;

        private readonly float avoidLookAheadDistance;

        private IndoorsSteeringManager PathSteering => insideSteering as IndoorsSteeringManager;
        private SteeringManager outsideSteering, insideSteering;

        private float updateTargetsTimer;
        private float updateMemoriesTimer;
        private float attackLimbResetTimer;

        private bool IsCoolDownRunning => AttackingLimb != null && AttackingLimb.attack.CoolDownTimer > 0;
        public float CombatStrength => Character.Params.AI.CombatStrength;
        private float Sight => Character.Params.AI.Sight;
        private float Hearing => Character.Params.AI.Hearing;
        private float FleeHealthThreshold => Character.Params.AI.FleeHealthThreshold;
        private float AggressionGreed => Character.Params.AI.AggressionGreed;
        private float AggressionHurt => Character.Params.AI.AggressionHurt;
        private bool AggressiveBoarding => Character.Params.AI.AggressiveBoarding;

        //a point in a wall which the Character is currently targeting
        private WallTarget wallTarget;

        //the limb selected for the current attack
        private Limb _attackingLimb;
        public Limb AttackingLimb
        {
            get { return _attackingLimb; }
            private set
            {
                attackLimbResetTimer = 0;
                _attackingLimb = value;
                attackVector = null;
                Reverse = _attackingLimb != null && _attackingLimb.attack.Reverse;
                if (Character.AnimController is FishAnimController fishController)
                {
                    fishController.reverse = Reverse;
                }
            }
        }
        
        private AITargetMemory selectedTargetMemory;
        private float targetValue;
        private CharacterParams.TargetParams selectedTargetingParams;
                
        private Dictionary<AITarget, AITargetMemory> targetMemories;
        
        private readonly float colliderWidth;
        private readonly float colliderLength;
        private readonly int requiredHoleCount;
        private bool canAttackWalls;
        private bool canAttackDoors;
        private bool canAttackCharacters;

        private readonly float priorityFearIncreasement = 2;
        private readonly float memoryFadeTime = 0.5f;
        private readonly float avoidTime = 3;

        private float avoidTimer;

        public LatchOntoAI LatchOntoAI { get; private set; }
        public SwarmBehavior SwarmBehavior { get; private set; }

        public bool AttackHumans
        {
            get
            {
                var target = GetTarget(CharacterPrefab.HumanSpeciesName);
                return target != null && target.Priority > 0.0f && (target.State == AIState.Attack || target.State == AIState.Aggressive);
            }
        }

        public bool AttackRooms
        {
            get
            {
                var target = GetTarget("room");
                return target != null && target.Priority > 0.0f && (target.State == AIState.Attack || target.State == AIState.Aggressive);
            }
        }

        public override bool CanEnterSubmarine
        {
            get
            {
                //can't enter a submarine when attached to something
                return Character.AnimController.CanEnterSubmarine && (LatchOntoAI == null || !LatchOntoAI.IsAttached);
            }
        }

        public override bool CanFlip
        {
            get
            {
                //can't flip when attached to something, when eating, or reversing or in a (relatively) small room
                return !Reverse &&
                    (State != AIState.Eat || Character.SelectedCharacter == null) &&
                    (LatchOntoAI == null || !LatchOntoAI.IsAttached) && 
                    (Character.CurrentHull == null || !Character.AnimController.InWater || Math.Min(Character.CurrentHull.Size.X, Character.CurrentHull.Size.Y) > ConvertUnits.ToDisplayUnits(Math.Max(colliderLength, colliderWidth)));
            }
        }

        public bool IsBeingChasedBy(Character c) => c.AIController is EnemyAIController enemyAI && enemyAI.SelectedAiTarget?.Entity is Character && (enemyAI.State == AIState.Aggressive || enemyAI.State == AIState.Attack);
        private bool IsBeingChased => SelectedAiTarget?.Entity is Character targetCharacter && IsBeingChasedBy(targetCharacter);

        public bool Reverse { get; private set; }

        public EnemyAIController(Character c, string seed) : base(c)
        {
            if (c.IsHuman)
            {
                throw new Exception($"Tried to create an enemy ai controller for human!");
            }
            CharacterPrefab prefab = CharacterPrefab.FindBySpeciesName(c.SpeciesName);
            var mainElement = prefab.XDocument.Root.IsOverride() ? prefab.XDocument.Root.FirstElement() : prefab.XDocument.Root;
            targetMemories = new Dictionary<AITarget, AITargetMemory>();
            steeringManager = outsideSteering;

            List<XElement> aiElements = new List<XElement>();
            List<float> aiCommonness = new List<float>();
            foreach (XElement element in mainElement.Elements())
            {
                if (!element.Name.ToString().Equals("ai", StringComparison.OrdinalIgnoreCase)) { continue; }                
                aiElements.Add(element);
                aiCommonness.Add(element.GetAttributeFloat("commonness", 1.0f));                
            }
            
            if (aiElements.Count == 0)
            {
                DebugConsole.ThrowError("Error in file \"" + prefab.FilePath + "\" - no AI element found.");
                outsideSteering = new SteeringManager(this);
                insideSteering = new IndoorsSteeringManager(this, false, false);
                return;
            }
            
            //choose a random ai element
            MTRandom random = new MTRandom(ToolBox.StringToInt(seed));
            XElement aiElement = aiElements.Count == 1 ? aiElements[0] : ToolBox.SelectWeightedRandom(aiElements, aiCommonness, random);
            foreach (XElement subElement in aiElement.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "latchonto":
                        LatchOntoAI = new LatchOntoAI(subElement, this);
                        break;
                    case "swarm":
                    case "swarmbehavior":
                        SwarmBehavior = new SwarmBehavior(subElement, this);
                        break;
                }
            }

            ReevaluateAttacks();
            outsideSteering = new SteeringManager(this);
            insideSteering = new IndoorsSteeringManager(this, false, canAttackDoors);
            steeringManager = outsideSteering;
            State = AIState.Idle;

            var size = Character.AnimController.Collider.GetSize();
            colliderWidth = size.X;
            colliderLength = size.Y;
            requiredHoleCount = (int)Math.Ceiling(ConvertUnits.ToDisplayUnits(colliderWidth) / Structure.WallSectionSize);

            avoidLookAheadDistance = Math.Max(colliderWidth * 3, 1.5f);
        }

        private CharacterParams.AIParams AIParams => Character.Params.AI;
        private CharacterParams.TargetParams GetTarget(string targetTag) => AIParams.GetTarget(targetTag, false);

        public override void SelectTarget(AITarget target) => SelectTarget(target, 100);

        public void SelectTarget(AITarget target, float priority)
        {
            SelectedAiTarget = target;
            selectedTargetMemory = GetTargetMemory(target, true);
            selectedTargetMemory.Priority = priority;
        }

        private float movementMargin;
        
        public override void Update(float deltaTime)
        {
            if (DisableEnemyAI) { return; }
            base.Update(deltaTime);

            bool ignorePlatforms = Character.AnimController.TargetMovement.Y < -0.5f && (-Character.AnimController.TargetMovement.Y > Math.Abs(Character.AnimController.TargetMovement.X));
            if (steeringManager == insideSteering)
            {
                var currPath = PathSteering.CurrentPath;
                if (currPath != null && currPath.CurrentNode != null)
                {
                    if (currPath.CurrentNode.SimPosition.Y < Character.AnimController.GetColliderBottom().Y)
                    {
                        // Don't allow to jump from too high.
                        float allowedJumpHeight = Character.AnimController.ImpactTolerance / 2;
                        float height = Math.Abs(currPath.CurrentNode.SimPosition.Y - Character.SimPosition.Y);
                        ignorePlatforms = height < allowedJumpHeight;
                    }
                }
            }
            Character.AnimController.IgnorePlatforms = ignorePlatforms;

            //clients get the facing direction from the server
            if (Character.AnimController is HumanoidAnimController && 
                (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer || Character.Controlled == Character))
            {
                if (Math.Abs(Character.AnimController.movement.X) > 0.1f && !Character.AnimController.InWater)
                {
                    Character.AnimController.TargetDir = Character.AnimController.movement.X > 0.0f ? Direction.Right : Direction.Left;
                }
            }

            if (isStateChanged)
            {
                if (State == AIState.Idle)
                {
                    stateResetTimer -= deltaTime;
                    if (stateResetTimer <= 0)
                    {
                        ResetOriginalState();
                    }
                }
            }
            if (targetIgnoreTimer > 0)
            {
                targetIgnoreTimer -= deltaTime;
            }
            else
            {
                ignoredTargets.Clear();
                targetIgnoreTimer = targetIgnoreTime;
            }
            avoidTimer -= deltaTime;
            if (avoidTimer < 0)
            {
                avoidTimer = 0;
            }
            UpdateCurrentMemoryLocation();
            if (updateMemoriesTimer > 0)
            {
                updateMemoriesTimer -= deltaTime;
            }
            else
            {
                FadeMemories(updateMemoriesInverval);
                updateMemoriesTimer = updateMemoriesInverval;
            }
            if (Character.HealthPercentage <= FleeHealthThreshold && SelectedAiTarget != null && 
                SelectedAiTarget.Entity is Character target && (target.IsPlayer || IsBeingChasedBy(target)))
            {
                State = AIState.Flee;
                wallTarget = null;
            }
            else
            {
                if (updateTargetsTimer > 0)
                {
                    updateTargetsTimer -= deltaTime;
                }
                else
                {
                    CharacterParams.TargetParams targetingParams = null;
                    if (!IsLatchedOnSub)
                    {
                        UpdateTargets(Character, out targetingParams);
                        UpdateWallTarget();
                    }
                    updateTargetsTimer = updateTargetsInterval * Rand.Range(0.75f, 1.25f);
                    if (avoidTimer > 0)
                    {
                        State = AIState.Escape;
                    }
                    else if (SelectedAiTarget == null)
                    {
                        State = AIState.Idle;
                    }
                    else if (targetingParams != null)
                    {
                        selectedTargetingParams = targetingParams;
                        State = targetingParams.State;
                    }
                }
            }

            if (Character.CurrentHull == null)
            {
                if (steeringManager != outsideSteering)
                {
                    outsideSteering.Reset();
                }
                steeringManager = outsideSteering;
            }
            else
            {
                if (steeringManager != insideSteering)
                {
                    insideSteering.Reset();
                }
                steeringManager = insideSteering;
            }

            bool run = false;
            switch (State)
            {
                case AIState.Idle:
                    UpdateIdle(deltaTime);
                    break;
                case AIState.Attack:
                    run = !IsCoolDownRunning;
                    UpdateAttack(deltaTime);
                    break;
                case AIState.Eat:
                    UpdateEating(deltaTime);
                    break;
                case AIState.Escape:
                case AIState.Flee:
                    run = true;
                    UpdateEscape(deltaTime);
                    break;
                case AIState.Avoid:
                case AIState.PassiveAggressive:
                case AIState.Aggressive:
                    if (SelectedAiTarget == null || SelectedAiTarget.Entity == null || SelectedAiTarget.Entity.Removed)
                    {
                        State = AIState.Idle;
                        return;
                    }
                    float squaredDistance = Vector2.DistanceSquared(WorldPosition, SelectedAiTarget.WorldPosition);
                    var attackLimb = AttackingLimb ?? GetAttackLimb(SelectedAiTarget.WorldPosition);
                    if (attackLimb != null && squaredDistance <= Math.Pow(attackLimb.attack.Range, 2))
                    {
                        run = true;
                        if (State == AIState.Avoid)
                        {
                            UpdateEscape(deltaTime);
                        }
                        else
                        {
                            UpdateAttack(deltaTime);
                        }
                    }
                    else
                    {
                        bool isBeingChased = IsBeingChased;
                        float reactDistance = !isBeingChased && selectedTargetingParams != null && selectedTargetingParams.ReactDistance > 0 ? selectedTargetingParams.ReactDistance : GetPerceivingRange(SelectedAiTarget);
                        if (squaredDistance <= Math.Pow(reactDistance + movementMargin, 2))
                        {
                            float halfReactDistance = reactDistance / 2;
                            float attackDistance = selectedTargetingParams != null && selectedTargetingParams.AttackDistance > 0 ? selectedTargetingParams.AttackDistance : halfReactDistance;
                            if (State == AIState.Aggressive || State == AIState.PassiveAggressive && squaredDistance < Math.Pow(attackDistance, 2))
                            {
                                run = true;
                                UpdateAttack(deltaTime);
                            }
                            else
                            {
                                run = isBeingChased ? true : squaredDistance < Math.Pow(halfReactDistance, 2);
                                if (movementMargin <= 0)
                                {
                                    movementMargin = halfReactDistance;
                                }
                                movementMargin = MathHelper.Clamp(movementMargin += deltaTime, halfReactDistance, reactDistance);
                                UpdateEscape(deltaTime);
                            }
                        }
                        else
                        {
                            movementMargin = 0;
                            UpdateIdle(deltaTime);
                        }
                    }
                    break;
                case AIState.Protect:
                    if (SelectedAiTarget == null || SelectedAiTarget.Entity == null || SelectedAiTarget.Entity.Removed)
                    {
                        State = AIState.Idle;
                        return;
                    }
                    if (SelectedAiTarget.Entity is Character targetCharacter && targetCharacter.LastAttacker is Character attacker)
                    {
                        // Attack the character that attacked the target we are protecting
                        ChangeTargetState(attacker, AIState.Attack, selectedTargetingParams.Priority * 2);
                        SelectTarget(attacker.AiTarget);
                        return;
                    }
                    float sqrDist = Vector2.DistanceSquared(WorldPosition, SelectedAiTarget.WorldPosition);
                    float reactDist = selectedTargetingParams != null && selectedTargetingParams.ReactDistance > 0 ? selectedTargetingParams.ReactDistance : GetPerceivingRange(SelectedAiTarget);
                    if (sqrDist > Math.Pow(reactDist + movementMargin, 2))
                    {
                        movementMargin = reactDist;
                        run = true;
                        UpdateFollow(deltaTime);
                    }
                    else
                    {
                        movementMargin = MathHelper.Clamp(movementMargin -= deltaTime, 0, reactDist);
                        UpdateIdle(deltaTime);
                    }
                    break;
                default:
                    throw new NotImplementedException();
            }

            if (!Character.AnimController.SimplePhysicsEnabled)
            {
                LatchOntoAI?.Update(this, deltaTime);
            }
            IsSteeringThroughGap = false;
            if (SwarmBehavior != null)
            {
                SwarmBehavior.IsActive = State == AIState.Idle && Character.CurrentHull == null;
                SwarmBehavior.Refresh();
                SwarmBehavior.UpdateSteering(deltaTime);
            }
            // Ensure that the creature keeps inside the level
            SteerInsideLevel(deltaTime);
            float speed = Character.AnimController.GetCurrentSpeed(run && Character.CanRun);
            steeringManager.Update(speed);
            Character.AnimController.TargetMovement = Character.ApplyMovementLimits(Steering, State == AIState.Idle && Character.AnimController.InWater ? Steering.Length() : speed);
            if (Character.CurrentHull != null && Character.AnimController.InWater)
            {
                // Halve the swimming speed inside the sub
                Character.AnimController.TargetMovement *= 0.5f;
            }
        }

        #region Idle

        private void UpdateIdle(float deltaTime)
        {
            var pathSteering = SteeringManager as IndoorsSteeringManager;
            if (pathSteering == null)
            {
                if (SimPosition.Y < ConvertUnits.ToSimUnits(Character.CharacterHealth.CrushDepth * 0.75f))
                {
                    // Steer straight up if very deep
                    SteeringManager.SteeringManual(deltaTime, Vector2.UnitY);
                    SteeringManager.SteeringAvoid(deltaTime, lookAheadDistance: avoidLookAheadDistance, weight: 5);
                    return;
                }
            }
            var target = SelectedAiTarget ?? _lastAiTarget;
            if (target?.Entity != null && !target.Entity.Removed && PreviousState == AIState.Attack && Character.CurrentHull == null)
            {
                // Keep heading to the last known position of the target
                var memory = GetTargetMemory(target, false);
                if (memory != null)
                {
                    var location = memory.Location;
                    float dist = Vector2.DistanceSquared(WorldPosition, location);
                    if (dist < 50 * 50)
                    {
                        // Target is gone
                        ResetAITarget();
                    }
                    else
                    {
                        // Steer towards the target
                        SteeringManager.SteeringSeek(Character.GetRelativeSimPosition(target.Entity, location), 5);
                        SteeringManager.SteeringAvoid(deltaTime, lookAheadDistance: avoidLookAheadDistance, weight: 15);
                        return;
                    }
                }
                else
                {
                    ResetAITarget();
                }
            }
            if (pathSteering != null && !Character.AnimController.InWater)
            {
                // Wander around inside
                pathSteering.Wander(deltaTime, ConvertUnits.ToDisplayUnits(colliderLength), stayStillInTightSpace: false);
            }
            else
            {
                // Wander around outside or swimming
                steeringManager.SteeringWander();
                if (Character.AnimController.InWater)
                {
                    SteeringManager.SteeringAvoid(deltaTime, lookAheadDistance: avoidLookAheadDistance, weight: 5);
                }
            }
        }

        #endregion

        #region Escape
        private Gap escapeTarget;
        private bool allGapsSearched;
        private readonly HashSet<Gap> unreachableGaps = new HashSet<Gap>();
        private void UpdateEscape(float deltaTime)
        {
            if (SelectedAiTarget != null && (SelectedAiTarget.Entity == null || SelectedAiTarget.Entity.Removed))
            {
                State = AIState.Idle;
                return;
            }
            else if (selectedTargetMemory != null)
            {
                selectedTargetMemory.Priority += deltaTime * priorityFearIncreasement;
            }
            IndoorsSteeringManager pathSteering = SteeringManager as IndoorsSteeringManager;
            bool hasValidPath = pathSteering?.CurrentPath != null && !pathSteering.IsPathDirty && !pathSteering.CurrentPath.Unreachable;
            if (Character.CurrentHull != null && pathSteering != null)
            {
                // Seek exit if inside
                if (!allGapsSearched)
                {
                    foreach (Gap gap in Gap.GapList)
                    {
                        if (gap == null || gap.Removed) { continue; }
                        if (escapeTarget == gap) { continue; }
                        if (unreachableGaps.Contains(gap)) { continue; }
                        if (gap.Submarine != Character.Submarine) { continue; }
                        if (gap.Open < 1 || gap.IsRoomToRoom) { continue; }
                        bool canGetThrough = ConvertUnits.ToDisplayUnits(colliderWidth) < gap.Size;
                        if (!canGetThrough) { continue; }
                        if (escapeTarget == null)
                        {
                            escapeTarget = gap;
                        }
                        else if (gap.FlowTargetHull == Character.CurrentHull)
                        {
                            escapeTarget = gap;
                            break;
                        }
                        else if (Vector2.DistanceSquared(Character.SimPosition, gap.SimPosition) < Vector2.DistanceSquared(Character.SimPosition, escapeTarget.SimPosition))
                        {
                            escapeTarget = gap;
                        }
                    }
                    allGapsSearched = true;
                }
                else if (escapeTarget != null && escapeTarget.FlowTargetHull != Character.CurrentHull)
                {
                    if (pathSteering.CurrentPath != null && !pathSteering.IsPathDirty && pathSteering.CurrentPath.Unreachable)
                    {
                        unreachableGaps.Add(escapeTarget);
                        escapeTarget = null;
                        allGapsSearched = false;
                    }
                }
            }
            if (escapeTarget != null && Character.CurrentHull != null && Vector2.DistanceSquared(Character.SimPosition, escapeTarget.SimPosition) > 0.5f)
            {
                if (hasValidPath && pathSteering.CurrentPath.Finished)
                {
                    // Steer manually towards the gap
                    SteeringManager.SteeringManual(deltaTime, Vector2.Normalize(escapeTarget.WorldPosition - Character.WorldPosition));
                }
                else if (SelectedAiTarget?.Entity is Character targetCharacter && targetCharacter.CurrentHull == Character.CurrentHull)
                {
                    // Steer away from the target if in the same room
                    Vector2 escapeDir = Vector2.Normalize(SelectedAiTarget != null ? WorldPosition - SelectedAiTarget.WorldPosition : Character.AnimController.TargetMovement);
                    if (!MathUtils.IsValid(escapeDir)) escapeDir = Vector2.UnitY;
                    SteeringManager.SteeringManual(deltaTime, escapeDir);
                }
                else if (pathSteering != null)
                {
                    if (canAttackDoors && hasValidPath)
                    {
                        var door = pathSteering.CurrentPath.CurrentNode?.ConnectedDoor ?? pathSteering.CurrentPath.NextNode?.ConnectedDoor;
                        if (door != null && !door.IsOpen && !door.IsBroken)
                        {
                            if (SelectedAiTarget != door.Item.AiTarget)
                            {
                                SelectTarget(door.Item.AiTarget);
                                State = AIState.Attack;
                                return;
                            }
                        }
                        else
                        {
                            SteeringManager.SteeringSeek(escapeTarget.SimPosition, 5);
                        }
                    }
                    else
                    {
                        SteeringManager.SteeringSeek(escapeTarget.SimPosition, 5);
                    }
                }
                else
                {
                    SteeringManager.SteeringSeek(escapeTarget.SimPosition, 10);
                    SteeringManager.SteeringAvoid(deltaTime, lookAheadDistance: avoidLookAheadDistance, weight: 15);
                }
            }
            else
            {
                escapeTarget = null;
                allGapsSearched = false;
                Vector2 escapeDir = Vector2.Normalize(SelectedAiTarget != null ? WorldPosition - SelectedAiTarget.WorldPosition : Character.AnimController.TargetMovement);
                if (!MathUtils.IsValid(escapeDir)) escapeDir = Vector2.UnitY;
                SteeringManager.SteeringManual(deltaTime, escapeDir);
                if (Character.CurrentHull == null)
                {
                    SteeringManager.SteeringWander();
                    SteeringManager.SteeringAvoid(deltaTime, lookAheadDistance: avoidLookAheadDistance, weight: 5);
                }
            }
        }

        #endregion

        #region Attack

        private Vector2 attackWorldPos;
        private Vector2 attackSimPos;

        private void UpdateAttack(float deltaTime)
        {
            if (SelectedAiTarget == null || SelectedAiTarget.Entity == null || SelectedAiTarget.Entity.Removed)
            {
                State = AIState.Idle;
                return;
            }

            attackWorldPos = SelectedAiTarget.WorldPosition;
            attackSimPos = SelectedAiTarget.SimPosition;

            if (SelectedAiTarget.Entity is Item item)
            {
                // If the item is held by a character, attack the character instead.
                Character owner = GetOwner(item);
                if (owner != null)
                {
                    if (IsFriendly(Character, owner))
                    {
                        ResetAITarget();
                        State = AIState.Idle;
                        return;
                    }
                    else
                    {
                        SelectedAiTarget = owner.AiTarget;
                    }
                }
            }

            if (wallTarget != null)
            {
                attackWorldPos = wallTarget.Position;
                if (wallTarget.Structure.Submarine != null)
                {
                    attackWorldPos += wallTarget.Structure.Submarine.Position;
                }
                attackSimPos = Character.Submarine == wallTarget.Structure.Submarine ? wallTarget.Position : attackWorldPos;
                attackSimPos = ConvertUnits.ToSimUnits(attackSimPos);
            }
            else
            {
                attackSimPos = Character.GetRelativeSimPosition(SelectedAiTarget.Entity);
            }

            if (Character.AnimController.CanEnterSubmarine)
            {
                //targeting a wall section that can be passed through -> steer manually through the hole
                if (wallTarget != null && wallTarget.SectionIndex > -1 && CanPassThroughHole(wallTarget.Structure, wallTarget.SectionIndex))
                {
                    WallSection section = wallTarget.Structure.GetSection(wallTarget.SectionIndex);
                    Vector2 targetPos = wallTarget.Structure.SectionPosition(wallTarget.SectionIndex, true);
                    if (section?.gap != null && SteerThroughGap(wallTarget.Structure, section, targetPos, deltaTime))
                    {
                        return;
                    }
                }
                else if (SelectedAiTarget.Entity is Structure wall)
                {
                    for (int i = 0; i < wall.Sections.Length; i++)
                    {
                        WallSection section = wall.Sections[i];
                        if (CanPassThroughHole(wall, i) && section?.gap != null)
                        {
                            if (SteerThroughGap(wall, section, wall.SectionPosition(i, true), deltaTime))
                            {
                                return;
                            }
                        }
                    }
                }
                else if (SelectedAiTarget.Entity is Item i)
                {
                    var door = i.GetComponent<Door>();
                    // Steer through the door manually if it's open or broken
                    // Don't try to enter dry hulls if cannot walk or if the gap is too narrow
                    if (door?.LinkedGap?.FlowTargetHull != null && !door.LinkedGap.IsRoomToRoom && (door.IsOpen || door.IsBroken))
                    {
                        if (Character.AnimController.CanWalk || door.LinkedGap.FlowTargetHull.WaterPercentage > 25)
                        {
                            if (door.LinkedGap.Size > ConvertUnits.ToDisplayUnits(colliderWidth))
                            {
                                LatchOntoAI?.DeattachFromBody();
                                Character.AnimController.ReleaseStuckLimbs();
                                var velocity = Vector2.Normalize(door.LinkedGap.FlowTargetHull.WorldPosition - Character.WorldPosition);
                                steeringManager.SteeringManual(deltaTime, velocity);
                                return;
                            }
                        }
                    }
                }
            }
            else if (SelectedAiTarget.Entity is Structure w && wallTarget == null)
            {
                bool isBroken = true;
                for (int i = 0; i < w.Sections.Length; i++)
                {
                    if (!w.SectionBodyDisabled(i))
                    {
                        isBroken = false;
                        Vector2 sectionPos = w.SectionPosition(i);
                        attackWorldPos = sectionPos;
                        if (w.Submarine != null)
                        {
                            attackWorldPos += w.Submarine.Position;
                        }
                        attackSimPos = ConvertUnits.ToSimUnits(attackWorldPos);
                        break;
                    }
                }
                if (isBroken)
                {
                    IgnoreTarget(SelectedAiTarget);
                    State = AIState.Idle;
                    ResetAITarget();
                    return;
                }
            }

            if (Math.Abs(Character.AnimController.movement.X) > 0.1f && !Character.AnimController.InWater &&
                (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer || Character.Controlled == Character))
            {
                Character.AnimController.TargetDir = Character.WorldPosition.X < attackWorldPos.X ? Direction.Right : Direction.Left;
            }

            bool canAttack = true;
            bool pursue = false;
            if (IsCoolDownRunning)
            {
                if (AttackingLimb.attack.CoolDownTimer >= AttackingLimb.attack.CoolDown + AttackingLimb.attack.CurrentRandomCoolDown - AttackingLimb.attack.AfterAttackDelay)
                {
                    return;
                }
                switch (AttackingLimb.attack.AfterAttack)
                {
                    case AIBehaviorAfterAttack.Pursue:
                    case AIBehaviorAfterAttack.PursueIfCanAttack:
                        if (AttackingLimb.attack.SecondaryCoolDown <= 0)
                        {
                            // No (valid) secondary cooldown defined.
                            if (AttackingLimb.attack.AfterAttack == AIBehaviorAfterAttack.Pursue)
                            {
                                canAttack = false;
                                pursue = true;
                            }
                            else
                            {
                                UpdateFallBack(attackWorldPos, deltaTime, true);
                                return;
                            }
                        }
                        else
                        {
                            if (AttackingLimb.attack.SecondaryCoolDownTimer <= 0)
                            {
                                // Don't allow attacking when the attack target has just changed.
                                if (_previousAiTarget != null && SelectedAiTarget != _previousAiTarget)
                                {
                                    canAttack = false;
                                    if (AttackingLimb.attack.AfterAttack == AIBehaviorAfterAttack.PursueIfCanAttack)
                                    {
                                        // Fall back if cannot attack.
                                        UpdateFallBack(attackWorldPos, deltaTime, true);
                                        return;
                                    }
                                    AttackingLimb = null;
                                }
                                else
                                {
                                    // If the secondary cooldown is defined and expired, check if we can switch the attack
                                    var newLimb = GetAttackLimb(attackWorldPos, AttackingLimb);
                                    if (newLimb != null)
                                    {
                                        // Attack with the new limb
                                        AttackingLimb = newLimb;
                                    }
                                    else
                                    {
                                        // No new limb was found.
                                        if (AttackingLimb.attack.AfterAttack == AIBehaviorAfterAttack.Pursue)
                                        {
                                            canAttack = false;
                                            pursue = true;
                                        }
                                        else
                                        {
                                            UpdateFallBack(attackWorldPos, deltaTime, true);
                                            return;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // Cooldown not yet expired, cannot attack -> steer towards the target
                                canAttack = false;
                            }
                        }
                        break;
                    case AIBehaviorAfterAttack.FallBackUntilCanAttack:
                    case AIBehaviorAfterAttack.FollowThroughUntilCanAttack:
                        if (AttackingLimb.attack.SecondaryCoolDown <= 0)
                        {
                            // No (valid) secondary cooldown defined.
                            UpdateFallBack(attackWorldPos, deltaTime, AttackingLimb.attack.AfterAttack == AIBehaviorAfterAttack.FollowThroughUntilCanAttack);
                            return;
                        }
                        else
                        {
                            if (AttackingLimb.attack.SecondaryCoolDownTimer <= 0)
                            {
                                // Don't allow attacking when the attack target has just changed.
                                if (_previousAiTarget != null && SelectedAiTarget != _previousAiTarget)
                                {
                                    UpdateFallBack(attackWorldPos, deltaTime, AttackingLimb.attack.AfterAttack == AIBehaviorAfterAttack.FollowThroughUntilCanAttack);
                                    return;
                                }
                                else
                                {
                                    // If the secondary cooldown is defined and expired, check if we can switch the attack
                                    var newLimb = GetAttackLimb(attackWorldPos, AttackingLimb);
                                    if (newLimb != null)
                                    {
                                        // Attack with the new limb
                                        AttackingLimb = newLimb;
                                    }
                                    else
                                    {
                                        // No new limb was found.
                                        UpdateFallBack(attackWorldPos, deltaTime, AttackingLimb.attack.AfterAttack == AIBehaviorAfterAttack.FollowThroughUntilCanAttack);
                                        return;
                                    }
                                }
                            }
                            else
                            {
                                // Cooldown not yet expired -> steer away from the target
                                UpdateFallBack(attackWorldPos, deltaTime, AttackingLimb.attack.AfterAttack == AIBehaviorAfterAttack.FollowThroughUntilCanAttack);
                                return;
                            }
                        }
                        break;
                    case AIBehaviorAfterAttack.FollowThrough:
                        UpdateFallBack(attackWorldPos, deltaTime, followThrough: true);
                        return;
                    case AIBehaviorAfterAttack.FallBack:
                    default:
                        UpdateFallBack(attackWorldPos, deltaTime, followThrough: false);
                        return;
                }
            }
            else
            {
                attackVector = null;
            }

            if (canAttack)
            {
                if (AttackingLimb == null || _previousAiTarget != SelectedAiTarget)
                {
                    AttackingLimb = GetAttackLimb(attackWorldPos);
                }
                canAttack = AttackingLimb != null && AttackingLimb.attack.CoolDownTimer <= 0;
            }
            if (!Character.AnimController.SimplePhysicsEnabled && SelectedAiTarget.Entity.Submarine != null && Character.Submarine == null && (!canAttackDoors || !canAttackWalls || !AIParams.TargetOuterWalls))
            {
                if (Vector2.DistanceSquared(Character.WorldPosition, attackWorldPos) < 2000 * 2000)
                {
                    // Check that we are not bumping into a door or a wall
                    Vector2 rayStart = SimPosition;
                    if (Character.Submarine == null)
                    {
                        rayStart -= SelectedAiTarget.Entity.Submarine.SimPosition;
                    }
                    Vector2 dir = SelectedAiTarget.WorldPosition - WorldPosition;
                    Vector2 rayEnd = rayStart + dir.ClampLength(Character.AnimController.Collider.GetLocalFront().Length() * 2);
                    Body closestBody = Submarine.CheckVisibility(rayStart, rayEnd, ignoreSubs: true);
                    if (Submarine.LastPickedFraction != 1.0f && closestBody != null && 
                        (!AIParams.TargetOuterWalls || !canAttackWalls && closestBody.UserData is Structure s && s.Submarine != null || !canAttackDoors && closestBody.UserData is Item i && i.Submarine != null && i.GetComponent<Door>() != null))
                    {
                        // Target is unreachable, there's a door or wall ahead
                        State = AIState.Idle;
                        IgnoreTarget(SelectedAiTarget);
                        ResetAITarget();
                        return;
                    }
                }
            }
            float distance = 0;
            Limb attackTargetLimb = null;
            Character targetCharacter = SelectedAiTarget.Entity as Character;
            if (canAttack)
            {
                if (!Character.AnimController.SimplePhysicsEnabled)
                {
                    // Target a specific limb instead of the target center position
                    if (wallTarget == null && targetCharacter != null)
                    {
                        var targetLimbType = AttackingLimb.Params.Attack.Attack.TargetLimbType;
                        attackTargetLimb = GetTargetLimb(AttackingLimb, targetCharacter, targetLimbType);
                        if (attackTargetLimb == null)
                        {
                            State = AIState.Idle;
                            IgnoreTarget(SelectedAiTarget);
                            ResetAITarget();
                            return;
                        }
                        attackWorldPos = attackTargetLimb.WorldPosition;
                        attackSimPos = Character.GetRelativeSimPosition(attackTargetLimb);
                    }
                }

                Vector2 attackLimbPos = Character.AnimController.SimplePhysicsEnabled ? Character.WorldPosition : AttackingLimb.WorldPosition;
                Vector2 toTarget = attackWorldPos - attackLimbPos;
                // Add a margin when the target is moving away, because otherwise it might be difficult to reach it (the attack takes some time to perform)
                if (wallTarget != null)
                {
                    if (wallTarget.Structure.Submarine != null)
                    {
                        Vector2 margin = CalculateMargin(wallTarget.Structure.Submarine.Velocity);
                        toTarget += margin;
                    }
                }
                else if (targetCharacter != null)
                {
                    Vector2 margin = CalculateMargin(targetCharacter.AnimController.Collider.LinearVelocity);
                    toTarget += margin;
                }
                else if (SelectedAiTarget.Entity is MapEntity e)
                {
                    if (e.Submarine != null)
                    {
                        Vector2 margin = CalculateMargin(e.Submarine.Velocity);
                        toTarget += margin;
                    }
                }

                Vector2 CalculateMargin(Vector2 targetVelocity)
                {
                    if (targetVelocity == Vector2.Zero) { return targetVelocity; }
                    float dot = Vector2.Dot(Vector2.Normalize(targetVelocity), Vector2.Normalize(Character.AnimController.Collider.LinearVelocity));
                    return ConvertUnits.ToDisplayUnits(targetVelocity) * AttackingLimb.attack.Duration * dot;
                }

                // Check that we can reach the target
                distance = toTarget.Length();
                canAttack = distance < AttackingLimb.attack.Range;
                if (!canAttack && !IsCoolDownRunning)
                {
                    // If not, reset the attacking limb, if the cooldown is not running
                    // Don't use the property, because we don't want cancel reversing, if we are reversing.
                    if (attackLimbResetTimer > attackLimbResetInterval)
                    {
                        _attackingLimb = null;
                        attackLimbResetTimer = 0;
                    }
                    else
                    {
                        attackLimbResetTimer += deltaTime;
                    }
                }
            }
            Limb steeringLimb = canAttack ? AttackingLimb : null;
            if (steeringLimb == null)
            {
                // If the attacking limb is a hand or claw, for example, using it as the steering limb can end in the result where the character circles around the target. For example the Hammerhead steering with the claws when it should use the torso.
                // If we always use the main limb, this causes the character to seek the target with it's torso/head, when it should not. For example Mudraptor steering with it's belly, when it should use it's head.
                // So let's use the one that's closer to the attacking limb.
                var torso = Character.AnimController.GetLimb(LimbType.Torso);
                var head = Character.AnimController.GetLimb(LimbType.Head);
                if (AttackingLimb == null)
                {
                    steeringLimb = head ?? torso;
                }
                else
                {
                    if (head != null && torso != null)
                    {
                        steeringLimb = Vector2.DistanceSquared(AttackingLimb.SimPosition, head.SimPosition) < Vector2.DistanceSquared(AttackingLimb.SimPosition, torso.SimPosition) ? head : torso;
                    }
                    else
                    {
                        steeringLimb = head ?? torso;
                    }
                }
            }

            if (steeringLimb == null)
            {
                State = AIState.Idle;
                return;
            }

            if (AttackingLimb != null && AttackingLimb.attack.Retreat)
            {
                UpdateFallBack(attackWorldPos, deltaTime, false);
            }
            else
            {
                Vector2 steerPos = attackSimPos;
                if (!Character.AnimController.SimplePhysicsEnabled)
                {
                    // Offset so that we don't overshoot the movement
                    Vector2 offset = Character.SimPosition - steeringLimb.SimPosition;
                    steerPos += offset;
                }
                if (SteeringManager is IndoorsSteeringManager pathSteering)
                {
                    if (pathSteering.CurrentPath != null)
                    {
                        // Attack doors
                        if (canAttackDoors)
                        {
                            // If the target is in the same hull, there shouldn't be any doors blocking the path
                            if (targetCharacter == null || targetCharacter.CurrentHull != Character.CurrentHull)
                            {
                                var door = pathSteering.CurrentPath.CurrentNode?.ConnectedDoor ?? pathSteering.CurrentPath.NextNode?.ConnectedDoor;
                                if (door != null && !door.IsOpen && !door.IsBroken)
                                {
                                    if (door.Item.AiTarget != null && SelectedAiTarget != door.Item.AiTarget)
                                    {
                                        SelectTarget(door.Item.AiTarget, selectedTargetMemory.Priority);
                                        return;
                                    }
                                }
                            }
                        }
                        // Steer towards the target if in the same room and swimming
                        if ((Character.AnimController.InWater || pursue || !Character.AnimController.CanWalk) &&
                            (targetCharacter != null && VisibleHulls.Contains(targetCharacter.CurrentHull) || Character.CanSeeTarget(SelectedAiTarget.Entity)))
                        {
                            Vector2 myPos = Character.AnimController.SimplePhysicsEnabled ? Character.SimPosition : steeringLimb.SimPosition;
                            SteeringManager.SteeringManual(deltaTime, Vector2.Normalize(steerPos - myPos));
                        }
                        else
                        {
                            SteeringManager.SteeringSeek(steerPos, 2);
                            // Switch to Idle when cannot reach the target and if cannot damage the walls
                            if ((!canAttackWalls || wallTarget == null) && !pathSteering.IsPathDirty && pathSteering.CurrentPath.Unreachable)
                            {
                                State = AIState.Idle;
                                return;
                            }
                        }
                    }
                    else
                    {
                        SteeringManager.SteeringSeek(steerPos, 5);
                    }
                }
                else
                {
                    SteeringManager.SteeringSeek(steerPos, 10);
                    SteeringManager.SteeringAvoid(deltaTime, lookAheadDistance: avoidLookAheadDistance, weight: 15);
                }
            }
            if (canAttack)
            {
                if (!UpdateLimbAttack(deltaTime, AttackingLimb, attackSimPos, distance, attackTargetLimb))
                {
                    IgnoreTarget(SelectedAiTarget);
                }
            }
        }

        public bool IsSteeringThroughGap { get; private set; }
        private bool SteerThroughGap(Structure wall, WallSection section, Vector2 targetWorldPos, float deltaTime)
        {
            IsSteeringThroughGap = true;
            wallTarget = null;
            LatchOntoAI?.DeattachFromBody();
            Character.AnimController.ReleaseStuckLimbs();
            Hull targetHull = section.gap?.FlowTargetHull;
            float maxDistance = Math.Min(wall.Rect.Width, wall.Rect.Height);
            if (Vector2.DistanceSquared(Character.WorldPosition, targetWorldPos) > maxDistance * maxDistance)
            {
                return false;
            }
            if (targetHull != null)
            {
                // If already inside, target the hull, else target the wall.
                SelectedAiTarget = Character.CurrentHull != null ? targetHull.AiTarget : wall.AiTarget;
                if (wall.IsHorizontal)
                {
                    targetWorldPos.Y = targetHull.WorldRect.Y - targetHull.Rect.Height / 2;
                }
                else
                {
                    targetWorldPos.X = targetHull.WorldRect.Center.X;
                }
                steeringManager.SteeringManual(deltaTime, Vector2.Normalize(targetWorldPos - Character.WorldPosition));
                SteeringManager.SteeringAvoid(deltaTime, lookAheadDistance: avoidLookAheadDistance, weight: 15);
                return true;
            }
            return false;
        }

        private readonly List<Limb> attackLimbs = new List<Limb>();
        private readonly List<float> weights = new List<float>();
        private Limb GetAttackLimb(Vector2 attackWorldPos, Limb ignoredLimb = null)
        {
            var currentContexts = Character.GetAttackContexts();
            Entity target = wallTarget != null ? wallTarget.Structure : SelectedAiTarget?.Entity;
            if (target == null) { return null; }
            Limb selectedLimb = null;
            float currentPriority = -1;
            foreach (Limb limb in Character.AnimController.Limbs)
            {
                if (limb == ignoredLimb) { continue; }
                if (limb.IsSevered || limb.IsStuck) { continue; }
                var attack = limb.attack;
                if (attack == null) { continue; }
                if (attack.CoolDownTimer > 0) { continue; }
                if (!attack.IsValidContext(currentContexts)) { continue; }
                if (!attack.IsValidTarget(target as IDamageable)) { continue; }
                if (target is ISerializableEntity se && target is Character)
                {
                    if (attack.Conditionals.Any(c => !c.Matches(se))) { continue; }
                }
                if (attack.Conditionals.Any(c => c.TargetSelf && !c.Matches(Character))) { continue; }
                if (AIParams.RandomAttack)
                {
                    attackLimbs.Add(limb);
                    weights.Add(limb.attack.Priority);
                }
                else
                {
                    float priority = CalculatePriority(limb, attackWorldPos);
                    if (priority > currentPriority)
                    {
                        currentPriority = priority;
                        selectedLimb = limb;
                    }
                }
            }
            if (AIParams.RandomAttack)
            {
                selectedLimb = ToolBox.SelectWeightedRandom(attackLimbs, weights, Rand.RandSync.Server);
                attackLimbs.Clear();
                weights.Clear();
            }
            return selectedLimb;

            float CalculatePriority(Limb limb, Vector2 attackPos)
            {
                if (Character.AnimController.SimplePhysicsEnabled) { return 1 + limb.attack.Priority; }
                float dist = Vector2.Distance(limb.WorldPosition, attackPos);
                // The limb is ignored if the target is not close. Prevents character going in reverse if very far away from it.
                // We also need a max value that is more than the actual range.
                float distanceFactor = MathHelper.Lerp(1, 0, MathUtils.InverseLerp(0, limb.attack.Range * 3, dist));
                return (1 + limb.attack.Priority) * distanceFactor;
            }
        }

        private void UpdateWallTarget()
        {
            wallTarget = null;
            if (SelectedAiTarget == null) { return; }
            if (SelectedAiTarget.Entity == null) { return; }
            //check if there's a wall between the target and the Character   
            Vector2 rayStart = SimPosition;
            Vector2 rayEnd = SelectedAiTarget.SimPosition;
            if (SelectedAiTarget.Entity.Submarine != null && Character.Submarine == null)
            {
                rayStart -= SelectedAiTarget.Entity.Submarine.SimPosition;
            }
            else if (SelectedAiTarget.Entity.Submarine == null && Character.Submarine != null)
            {
                rayEnd -= Character.Submarine.SimPosition;
            }
            Body closestBody = Submarine.CheckVisibility(rayStart, rayEnd, ignoreSubs: true, ignoreSensors: CanEnterSubmarine, ignoreDisabledWalls: CanEnterSubmarine);
            if (Submarine.LastPickedFraction != 1.0f && closestBody != null)
            {
                if (closestBody.UserData is Structure wall && wall.Submarine != null && (wall.Submarine.Info.IsPlayer || wall.Submarine.Info.IsOutpost && TargetOutposts))
                {
                    int sectionIndex = wall.FindSectionIndex(ConvertUnits.ToDisplayUnits(Submarine.LastPickedPosition));
                    float sectionDamage = wall.SectionDamage(sectionIndex);
                    for (int i = sectionIndex - 2; i <= sectionIndex + 2; i++)
                    {
                        if (wall.SectionBodyDisabled(i))
                        {
                            if (Character.AnimController.CanEnterSubmarine && CanPassThroughHole(wall, i))
                            {
                                sectionIndex = i;
                                break;
                            }
                            else
                            {
                                //otherwise ignore and keep breaking other sections
                                continue;
                            }
                        }
                        if (wall.SectionDamage(i) > sectionDamage)
                        {
                            sectionIndex = i;
                        }
                    }
                    Vector2 sectionPos = wall.SectionPosition(sectionIndex);
                    Vector2 attachTargetNormal;
                    if (wall.IsHorizontal)
                    {
                        attachTargetNormal = new Vector2(0.0f, Math.Sign(WorldPosition.Y - wall.WorldPosition.Y));
                        sectionPos.Y += (wall.BodyHeight <= 0.0f ? wall.Rect.Height : wall.BodyHeight) / 2 * attachTargetNormal.Y;
                    }
                    else
                    {
                        attachTargetNormal = new Vector2(Math.Sign(WorldPosition.X - wall.WorldPosition.X), 0.0f);
                        sectionPos.X += (wall.BodyWidth <= 0.0f ? wall.Rect.Width : wall.BodyWidth) / 2 * attachTargetNormal.X;
                    }
                    LatchOntoAI?.SetAttachTarget(wall.Submarine.PhysicsBody.FarseerBody, wall.Submarine, ConvertUnits.ToSimUnits(sectionPos), attachTargetNormal);
                    if (Character.AnimController.CanEnterSubmarine || !wall.SectionBodyDisabled(sectionIndex) && !IsWallDisabled(wall))
                    {
                        if (AIParams.TargetOuterWalls || wall.prefab.Tags.Contains("inner"))
                        {
                            wallTarget = new WallTarget(sectionPos, wall, sectionIndex);
                        }
                    }
                }
                if (!Character.AnimController.CanEnterSubmarine && wallTarget == null)
                {
                    if (closestBody.UserData is Structure w && w.Submarine != null || closestBody.UserData is Item i && i.Submarine != null)
                    {
                        // Cannot reach the target, because it's blocked by a disabled wall or a door
                        State = AIState.Idle;
                        IgnoreTarget(SelectedAiTarget);
                        ResetAITarget();
                    }
                }
            }
        }

        private bool IsWallDisabled(Structure wall)
        {
            bool isDisabled = true;
            for (int i = 0; i < wall.Sections.Length; i++)
            {
                if (!wall.SectionBodyDisabled(i))
                {
                    isDisabled = false;
                    break;
                }
            }
            return isDisabled;
        }
        public override void OnAttacked(Character attacker, AttackResult attackResult)
        {
            float reactionTime = Rand.Range(0.1f, 0.3f);
            updateTargetsTimer = Math.Min(updateTargetsTimer, reactionTime);

            bool wasLatched = IsLatchedOnSub;
            Character.AnimController.ReleaseStuckLimbs();
            LatchOntoAI?.DeattachFromBody();
            if (attacker == null || attacker.AiTarget == null) { return; }
            bool isFriendly = IsFriendly(Character, attacker);
            if (wasLatched)
            {
                avoidTimer = avoidTime * Rand.Range(0.75f, 1.25f);
                if (!isFriendly)
                {
                    SelectTarget(attacker.AiTarget);
                }
                return;
            }

            if (State == AIState.Flee)
            {
                if (!isFriendly)
                {
                    SelectTarget(attacker.AiTarget);
                }
                return;
            }
            if (!isFriendly && attackResult.Damage > 0.0f)
            {
                bool canAttack = attacker.Submarine == Character.Submarine && canAttackCharacters || attacker.Submarine != null && canAttackWalls;
                if (Character.Params.AI.AttackWhenProvoked && canAttack)
                {
                    if (attacker.IsHusk)
                    {
                        ChangeTargetState("husk", AIState.Attack, 100);
                    }
                    else
                    {
                        ChangeTargetState(attacker, AIState.Attack, 100);
                    }
                }
                else if (!AIParams.HasTag(attacker.SpeciesName))
                {
                    if (attacker.IsHusk)
                    {
                        ChangeTargetState("husk", canAttack ? AIState.Attack : AIState.Escape, 100);
                    }
                    else if (attacker.AIController is EnemyAIController enemyAI)
                    {
                        if (enemyAI.CombatStrength > CombatStrength)
                        {
                            if (!AIParams.HasTag("stronger"))
                            {
                                ChangeTargetState(attacker, canAttack ? AIState.Attack : AIState.Escape, 100);
                            }
                        }
                        else if (enemyAI.CombatStrength < CombatStrength)
                        {
                            if (!AIParams.HasTag("weaker"))
                            {
                                ChangeTargetState(attacker, canAttack ? AIState.Attack : AIState.Escape, 100);
                            }
                        }
                        else
                        {
                            // Equal strength
                            ChangeTargetState(attacker, canAttack ? AIState.Attack : AIState.Escape, 100);
                        }
                    }
                    else
                    {
                        ChangeTargetState(attacker, canAttack ? AIState.Attack : AIState.Escape, 100);
                    }
                }
                else if (canAttack && attacker.IsHuman && AIParams.TryGetTarget(attacker.SpeciesName, out CharacterParams.TargetParams targetingParams))
                {
                    if (targetingParams.State == AIState.Aggressive)
                    {
                        ChangeTargetState(attacker, AIState.Attack, 100);
                    }
                }
            }

            AITargetMemory targetMemory = GetTargetMemory(attacker.AiTarget, true);
            targetMemory.Priority += GetRelativeDamage(attackResult.Damage, Character.Vitality) * AggressionHurt;

            // Only allow to react once. Otherwise would attack the target with only a fraction of a cooldown
            bool retaliate = !isFriendly && SelectedAiTarget != attacker.AiTarget && attacker.Submarine == Character.Submarine;
            bool avoidGunFire = Character.Params.AI.AvoidGunfire && attacker.Submarine != Character.Submarine;

            if (State == AIState.Attack && !IsCoolDownRunning)
            {
                // Don't retaliate or escape while performing an attack
                retaliate = false;
                avoidGunFire = false;
            }
            if (retaliate)
            {
                // Reduce the cooldown so that the character can react
                foreach (var limb in Character.AnimController.Limbs)
                {
                    if (limb.attack != null)
                    {
                        limb.attack.CoolDownTimer *= reactionTime;
                    }
                }
            }
            else if (avoidGunFire)
            {
                avoidTimer = avoidTime * Rand.Range(0.75f, 1.25f);
                SelectTarget(attacker.AiTarget);
            }
        }

        // 10 dmg, 100 health -> 0.1
        private float GetRelativeDamage(float dmg, float vitality) => dmg / Math.Max(vitality, 1.0f);

        private bool UpdateLimbAttack(float deltaTime, Limb attackingLimb, Vector2 attackSimPos, float distance = -1, Limb targetLimb = null)
        {
            if (SelectedAiTarget?.Entity == null) { return false; }
            if (wallTarget != null)
            {
                // If the selected target is not the wall target, make the wall target the selected target.
                var aiTarget = wallTarget.Structure.AiTarget;
                if (aiTarget != null && SelectedAiTarget != aiTarget)
                {
                    SelectTarget(aiTarget, GetTargetMemory(SelectedAiTarget, true).Priority);
                }
            }
            IDamageable damageTarget = wallTarget != null ? wallTarget.Structure : SelectedAiTarget.Entity as IDamageable;
            if (damageTarget != null)
            {
                //simulate attack input to get the character to attack client-side
                Character.SetInput(InputType.Attack, true, true);
                if (attackingLimb.UpdateAttack(deltaTime, attackSimPos, damageTarget, out AttackResult attackResult, distance, targetLimb))
                {
                    if (damageTarget.Health > 0)
                    {
                        // Managed to hit a living/non-destroyed target. Increase the priority more if the target is low in health -> dies easily/soon
                        selectedTargetMemory.Priority += GetRelativeDamage(attackResult.Damage, damageTarget.Health) * AggressionGreed;
                    }
                    else
                    {
                        selectedTargetMemory.Priority = 0;
                    }
                }
                return true;
            }
            return false;
        }

        private Vector2? attackVector = null;
        private void UpdateFallBack(Vector2 attackWorldPos, float deltaTime, bool followThrough)
        {
            if (attackVector == null)
            {
                // TODO: test adding some random variance here?
                attackVector = attackWorldPos - WorldPosition;
            }
            Vector2 attackDir = Vector2.Normalize(followThrough ? attackVector.Value : -attackVector.Value);
            if (!MathUtils.IsValid(attackDir))
            {
                attackDir = Vector2.UnitY;
            }
            steeringManager.SteeringManual(deltaTime, attackDir);
            if (Character.AnimController.InWater)
            {
                SteeringManager.SteeringAvoid(deltaTime, lookAheadDistance: avoidLookAheadDistance, weight: 15);
            }
        }

        #endregion

        #region Eat

        private void UpdateEating(float deltaTime)
        {
            if (SelectedAiTarget == null || SelectedAiTarget.Entity == null || SelectedAiTarget.Entity.Removed)
            {
                State = AIState.Idle;
                return;
            }
            if (SelectedAiTarget.Entity is Character target)
            {
                Limb mouthLimb = Character.AnimController.GetLimb(LimbType.Head);
                if (mouthLimb == null)
                {
                    DebugConsole.ThrowError("Character \"" + Character.SpeciesName + "\" failed to eat a target (No head limb defined)");
                    State = AIState.Idle;
                    return;
                }
                Vector2 mouthPos = Character.AnimController.SimplePhysicsEnabled ? SimPosition : Character.AnimController.GetMouthPosition().Value;
                Vector2 attackSimPosition = Character.GetRelativeSimPosition(target);
                Vector2 limbDiff = attackSimPosition - mouthPos;
                float extent = Math.Max(mouthLimb.body.GetMaxExtent(), 2);
                if (limbDiff.LengthSquared() < extent * extent)
                {
                    Character.SelectCharacter(target);
                    steeringManager.SteeringManual(deltaTime, Vector2.Normalize(limbDiff) * 3);
                    Character.AnimController.Collider.ApplyForce(limbDiff * mouthLimb.Mass * 50.0f, mouthPos);
                }
                else
                {
                    steeringManager.SteeringSeek(attackSimPosition - (mouthPos - SimPosition), 2);
                    if (Character.AnimController.InWater)
                    {
                        SteeringManager.SteeringAvoid(deltaTime, lookAheadDistance: avoidLookAheadDistance, weight: 15);
                    }
                }
            }
            else
            {
                IgnoreTarget(SelectedAiTarget);
                State = AIState.Idle;
                ResetAITarget();
            }
        }

        #endregion

        private void UpdateFollow(float deltaTime)
        {
            if (SelectedAiTarget == null || SelectedAiTarget.Entity == null || SelectedAiTarget.Entity.Removed)
            {
                State = AIState.Idle;
                return;
            }
            Vector2 dir = Vector2.Normalize(SelectedAiTarget.Entity.WorldPosition - Character.WorldPosition);
            if (!MathUtils.IsValid(dir))
            {
                return;
            }
            steeringManager.SteeringManual(deltaTime, dir);
            if (Character.AnimController.InWater)
            {
                SteeringManager.SteeringAvoid(deltaTime, lookAheadDistance: avoidLookAheadDistance, weight: 15);
            }
        }

        #region Targeting
        private bool IsLatchedOnSub => LatchOntoAI != null && LatchOntoAI.IsAttachedToSub;

        //goes through all the AItargets, evaluates how preferable it is to attack the target,
        //whether the Character can see/hear the target and chooses the most preferable target within
        //sight/hearing range
        public AITarget UpdateTargets(Character character, out CharacterParams.TargetParams targetingParams)
        {
            if ((SelectedAiTarget != null || wallTarget != null) && IsLatchedOnSub)
            {
                var wall = SelectedAiTarget.Entity as Structure;
                if (wall == null)
                {
                    wall = wallTarget?.Structure;
                }
                // The target is not a wall or it's not the same as we are attached to -> release
                bool releaseTarget = wall == null || !wall.Bodies.Contains(LatchOntoAI.AttachJoints[0].BodyB);
                if (!releaseTarget)
                {
                    for (int i = 0; i < wall.Sections.Length; i++)
                    {
                        if (CanPassThroughHole(wall, i))
                        {
                            releaseTarget = true;
                        }
                    }
                }
                if (releaseTarget)
                {
                    SelectedAiTarget = null;
                    wallTarget = null;
                    LatchOntoAI.DeattachFromBody();
                }
                else if (SelectedAiTarget?.Entity == wallTarget?.Structure)
                {
                    // If attached to a valid target, just keep the target.
                    // Priority not used in this case.
                    targetingParams = null;
                    return SelectedAiTarget;
                }
            }
            AITarget newTarget = null;
            targetValue = 0;
            selectedTargetMemory = null;
            targetingParams = null;

            foreach (AITarget aiTarget in AITarget.List)
            {
                if (!aiTarget.Enabled) { continue; }
                if (ignoredTargets.Contains(aiTarget)) { continue; }
                if (Level.Loaded != null && aiTarget.WorldPosition.Y > Level.Loaded.Size.Y)
                {
                    continue;
                }
                if (aiTarget.Type == AITarget.TargetType.HumanOnly) { continue; }
                if (!TargetOutposts)
                {
                    if (aiTarget.Entity.Submarine != null && aiTarget.Entity.Submarine.Info.IsOutpost) { continue; }
                }
                Character targetCharacter = aiTarget.Entity as Character;
                //ignore the aitarget if it is the Character itself
                if (targetCharacter == character) { continue; }

                float valueModifier = 1;
                string targetingTag = null;
                if (targetCharacter != null)
                {
                    if (targetCharacter.IsDead)
                    {
                        targetingTag = "dead";
                    }
                    else if (AIParams.TryGetTarget(targetCharacter.SpeciesName, out CharacterParams.TargetParams tP))
                    {
                        targetingTag = tP.Tag;
                    }
                    else if (targetCharacter.AIController is EnemyAIController enemy)
                    {
                        if (targetCharacter.Params.CompareGroup(Character.Params.Group))
                        {
                            // Ignore targets that are in the same group (treat them like they were of the same species)
                            continue;
                        }
                        if (targetCharacter.IsHusk && AIParams.HasTag("husk"))
                        {
                            targetingTag = "husk";
                        }
                        else
                        {
                            if (enemy.CombatStrength > CombatStrength)
                            {
                                targetingTag = "stronger";
                            }
                            else if (enemy.CombatStrength < CombatStrength)
                            {
                                targetingTag = "weaker";
                            }
                            if (targetingTag == "stronger" && (State == AIState.Avoid || State == AIState.Escape || State == AIState.Flee))
                            {
                                if (SelectedAiTarget == aiTarget)
                                {
                                    // Freightened -> hold on to the target
                                    valueModifier *= 2;
                                }
                                if (IsBeingChasedBy(targetCharacter))
                                {
                                    valueModifier *= 2;
                                }
                                if (Character.CurrentHull != null && !VisibleHulls.Contains(targetCharacter.CurrentHull))
                                {
                                    // Inside but in a different room
                                    valueModifier /= 2;
                                }
                            }
                        }
                    }
                }
                else if (aiTarget.Entity != null)
                {
                    // Ignore all structures and items inside wrecks
                    if (aiTarget.Entity.Submarine != null && aiTarget.Entity.Submarine.Info.IsWreck) { continue; }
                    // Ignore the target if it's a room and the character is already inside a sub
                    if (character.CurrentHull != null && aiTarget.Entity is Hull) { continue; }

                    Door door = null;
                    if (aiTarget.Entity is Item item)
                    {
                        door = item.GetComponent<Door>();
                        bool targetingFromOutsideToInside = item.CurrentHull != null && character.CurrentHull == null;
                        if (targetingFromOutsideToInside)
                        {
                            if (door != null && !canAttackDoors || !canAttackWalls)
                            {
                                // Can't reach
                                continue;
                            }
                        }
                        foreach (var prio in AIParams.Targets)
                        {
                            if (item.HasTag(prio.Tag))
                            {
                                targetingTag = prio.Tag;
                                break;
                            }
                        }
                        if (door == null && targetingTag == null)
                        {
                            if (item.GetComponent<Sonar>() != null)
                            {
                                targetingTag = "sonar";
                            }
                            else if (targetingFromOutsideToInside)
                            {
                                targetingTag = "room";
                            }
                        }
                        else if (targetingTag == "nasonov")
                        {
                            if ((item.Submarine == null || !item.Submarine.Info.IsPlayer) && item.ParentInventory == null)
                            {
                                // Only target nasonovartifacts when they are held be a player or inside the playersub
                                continue;
                            }
                        }
                        // Ignore the target if it's a decoy and the character is already inside a sub
                        if (character.CurrentHull != null && targetingTag == "decoy")
                        {
                            continue;
                        }
                    }
                    else if (aiTarget.Entity is Structure s)
                    {
                        targetingTag = "wall";
                        if (!s.HasBody)
                        {
                            // Ignore structures that doesn't have a body (not walls)
                            continue;
                        }
                        if (s.IsPlatform) { continue; }
                        if (s.Submarine == null) { continue; }
                        bool isCharacterInside = character.CurrentHull != null;
                        bool isInnerWall = s.prefab.Tags.Contains("inner");
                        if (isInnerWall && !isCharacterInside)
                        {
                            // Ignore inner walls when outside (walltargets still work)
                            continue;
                        }
                        valueModifier = 1;
                        if (!Character.AnimController.CanEnterSubmarine && IsWallDisabled(s))
                        {
                            continue;
                        }
                        for (int i = 0; i < s.Sections.Length; i++)
                        {
                            var section = s.Sections[i];
                            if (section.gap == null) { continue; }
                            bool leadsInside = !section.gap.IsRoomToRoom && section.gap.FlowTargetHull != null;
                            isInnerWall = isInnerWall || !leadsInside;
                            if (Character.AnimController.CanEnterSubmarine)
                            {
                                if (!isCharacterInside)
                                {
                                    if (CanPassThroughHole(s, i))
                                    {
                                        valueModifier *= leadsInside ? (AggressiveBoarding ? 5 : 1) : 0;
                                    }
                                    else if (AggressiveBoarding && leadsInside && canAttackWalls && AIParams.TargetOuterWalls)
                                    {
                                        // Up to 100% priority increase for every gap in the wall when an aggressive boarder is outside
                                        valueModifier *= 1 + section.gap.Open;
                                    }
                                }
                                else
                                {
                                    // Inside
                                    if (AggressiveBoarding)
                                    {
                                        if (!isInnerWall)
                                        {
                                            // Only interested in getting inside (aggressive boarder) -> don't target outer walls when already inside
                                            valueModifier = 0;
                                            break;
                                        }
                                        else if (CanPassThroughHole(s, i))
                                        {
                                            valueModifier *= isInnerWall ? 1 : 0;
                                        }
                                        else if (!canAttackWalls)
                                        {
                                            valueModifier = 0;
                                            break;
                                        }
                                    }
                                    else
                                    {
                                        if (!canAttackWalls)
                                        {
                                            valueModifier = 0;
                                            break;
                                        }
                                        // We are actually interested in breaking things -> reduce the priority when the wall is already broken
                                        // (Terminalcells)
                                        valueModifier *= 1 - section.gap.Open * 0.25f;
                                    }
                                }
                            }
                            else
                            {
                                // Cannot enter
                                if (isInnerWall || !canAttackWalls)
                                {
                                    // Ignore inner walls and all walls if cannot do damage on walls.
                                    valueModifier = 0;
                                    break;
                                }
                                else if (AggressiveBoarding)
                                {
                                    // Up to 100% priority increase for every gap in the wall when an aggressive boarder is outside
                                    // (Bonethreshers)
                                    valueModifier *= 1 + section.gap.Open;
                                }
                            }
                        }
                    }
                    else
                    {
                        targetingTag = "room";
                    }
                    if (door != null)
                    {
                        // If there's not a more specific tag for the door
                        if (string.IsNullOrEmpty(targetingTag) || targetingTag == "room")
                        {
                            targetingTag = "door";
                        }
                        if (door.Item.Submarine == null) { continue;}
                        bool isOutdoor = door.LinkedGap?.FlowTargetHull != null && !door.LinkedGap.IsRoomToRoom;
                        bool isOpen = door.IsOpen || door.IsBroken;
                        if (!isOpen && !canAttackDoors || (isOutdoor && !AIParams.TargetOuterWalls))
                        {
                            // Ignore doors that are not open if cannot attack doors or shouldn't target outer doors.
                            continue;
                        }
                        if (isOpen && (!Character.AnimController.CanEnterSubmarine || !AggressiveBoarding))
                        {
                            // Ignore broken and open doors
                            // Aggressive boarders don't ignore open doors, because they use them for get in.
                            continue;
                        }
                        if (AggressiveBoarding)
                        {
                            // Increase the priority if the character is outside and the door is from outside to inside
                            if (character.CurrentHull == null && isOutdoor)
                            {
                                valueModifier *= isOpen ? 5 : 1;
                            }
                            else
                            {
                                // Inside
                                valueModifier *= isOpen || isOutdoor ? 0 : 1;
                            }
                        }
                        else if (character.CurrentHull == null)
                        {
                            valueModifier = isOutdoor ? 1 : 0;
                        }
                    }
                    else if (aiTarget.Entity is IDamageable targetDamageable && targetDamageable.Health <= 0.0f)
                    {
                         continue;
                    }
                }

                if (targetingTag == null) { continue; }
                var targetParams = GetTarget(targetingTag);
                if (targetParams == null) { continue; }
                valueModifier *= targetParams.Priority;

                if (valueModifier == 0.0f) { continue; }

                Vector2 toTarget = aiTarget.WorldPosition - character.WorldPosition;
                float dist = toTarget.Length();

                //if the target has been within range earlier, the character will notice it more easily
                if (targetMemories.ContainsKey(aiTarget))
                {
                    dist *= 0.9f;
                }

                if (!CanPerceive(aiTarget, dist)) { continue; }
                if (!aiTarget.IsWithinSector(WorldPosition)) { continue; }

                //if the target is very close, the distance doesn't make much difference 
                // -> just ignore the distance and attack whatever has the highest priority
                dist = Math.Max(dist, 100.0f);

                AITargetMemory targetMemory = GetTargetMemory(aiTarget, true);
                if (Character.CurrentHull != null && Math.Abs(toTarget.Y) > Character.CurrentHull.Size.Y)
                {
                    // Inside the sub, treat objects that are up or down, as they were farther away.
                    dist *= 3;
                }
                valueModifier *= targetMemory.Priority / (float)Math.Sqrt(dist);

                if (valueModifier > targetValue)
                {
                    if (aiTarget.Entity is Item i)
                    {
                        Character owner = GetOwner(i);
                        // Don't target items that we own. 
                        // This is a rare case, and almost entirely related to Humanhusks, so let's check it last to reduce unnecessary checks (although the check shouldn't be expensive)
                        if (owner == character) { continue; }
                        if (owner != null && IsFriendly(Character, owner))
                        {
                            // If the item is held by a friendly character, ignore it.
                            continue;
                        }
                    }
                    if (targetCharacter != null)
                    {
                        if (targetCharacter.Submarine != Character.Submarine)
                        {
                            if (targetCharacter.Submarine != null)
                            {
                                // Target is inside -> reduce the priority
                                valueModifier *= 0.5f;
                                if (Character.Submarine != null)
                                {
                                    // Both inside different submarines -> can ignore safely
                                    continue;
                                }
                            }
                            else if (Character.CurrentHull != null)
                            {
                                // Target outside, but we are inside -> Check if we can get to the target.
                                // Only check if we are not already targeting the character.
                                // If we are, keep the target (unless we choose another).
                                if (SelectedAiTarget?.Entity != targetCharacter)
                                {
                                    foreach (var gap in Character.CurrentHull.ConnectedGaps)
                                    {
                                        var door = gap.ConnectedDoor;
                                        if (door == null || !door.IsOpen && !door.IsBroken)
                                        {
                                            var wall = gap.ConnectedWall;
                                            if (wall != null)
                                            {
                                                for (int j = 0; j < wall.Sections.Length; j++)
                                                {
                                                    WallSection section = wall.Sections[j];
                                                    if (!CanPassThroughHole(wall, j) && section?.gap != null)
                                                    {
                                                        continue;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    newTarget = aiTarget;
                    selectedTargetMemory = targetMemory;
                    targetValue = valueModifier;
                    targetingParams = GetTarget(targetingTag);
                }
            }

            SelectedAiTarget = newTarget;
            if (SelectedAiTarget != _previousAiTarget)
            {
                wallTarget = null;
            }
            return SelectedAiTarget;
        }

        private AITargetMemory GetTargetMemory(AITarget target, bool addIfNotFound)
        {
            if (!targetMemories.TryGetValue(target, out AITargetMemory memory))
            {
                if (addIfNotFound)
                {
                    memory = new AITargetMemory(target, 10);
                    targetMemories.Add(target, memory);
                }
            }
            return memory;
        }

        private void UpdateCurrentMemoryLocation()
        {
            if (_selectedAiTarget != null)
            {
                if (_selectedAiTarget.Entity == null || _selectedAiTarget.Entity.Removed)
                {
                    _selectedAiTarget = null;
                }
                else if (CanPerceive(_selectedAiTarget, distSquared: Vector2.DistanceSquared(Character.WorldPosition, _selectedAiTarget.WorldPosition)))
                {
                    var memory = GetTargetMemory(_selectedAiTarget, false);
                    if (memory != null)
                    {
                        memory.Location = _selectedAiTarget.WorldPosition;
                    }
                }
            }
        }

        private readonly List<AITarget> removals = new List<AITarget>();
        private void FadeMemories(float deltaTime)
        {
            removals.Clear();
            foreach (var kvp in targetMemories)
            {
                var target = kvp.Key;
                var memory = kvp.Value;
                // Slowly decrease all memories
                float fadeTime = memoryFadeTime;
                if (target == SelectedAiTarget)
                {
                    // Don't decrease the current memory
                    fadeTime = 0;
                }
                else if (target == _lastAiTarget)
                {
                    // Halve the latest memory fading. 
                    fadeTime /= 2;
                }
                memory.Priority -= fadeTime * deltaTime;
                // Remove targets that have no priority or have been removed
                if (memory.Priority <= 1 || target.Entity == null || target.Entity.Removed || !AITarget.List.Contains(target))
                {
                    removals.Add(target);
                }
            }
            removals.ForEach(r => targetMemories.Remove(r));
        }

        private readonly float targetIgnoreTime = 5;
        private float targetIgnoreTimer;
        private readonly HashSet<AITarget> ignoredTargets = new HashSet<AITarget>();
        public void IgnoreTarget(AITarget target)
        {
            if (target == null) { return; }
            ignoredTargets.Add(target);
            targetIgnoreTimer = targetIgnoreTime * Rand.Range(0.75f, 1.25f);
        }
        #endregion

        #region State switching
        /// <summary>
        /// How long do we hold on to the current state after losing a target before we reset back to the original state.
        /// In other words, how long do we have to idle before the original state is restored.
        /// </summary>
        private readonly float stateResetCooldown = 10;
        private float stateResetTimer;
        private bool isStateChanged;

        /// <summary>
        /// Resets the target's state to the original value defined in the xml.
        /// </summary>
        private bool TryResetOriginalState(string tag)
        {
            if (!modifiedParams.ContainsKey(tag)) { return false; }
            if (AIParams.TryGetTarget(tag, out CharacterParams.TargetParams targetParams))
            {
                modifiedParams.Remove(tag);
                if (tempParams.ContainsKey(tag))
                {
                    tempParams.Values.ForEach(t => AIParams.RemoveTarget(t));
                    tempParams.Remove(tag);
                }
                targetParams.Reset();
                ResetAITarget();
                // Enforce the idle state so that we don't keep following the target if there's one
                State = AIState.Idle;
                PreviousState = AIState.Idle;
                return true;
            }
            else
            {
                return false;
            }
        }

        private readonly Dictionary<string, CharacterParams.TargetParams> modifiedParams = new Dictionary<string, CharacterParams.TargetParams>();
        private readonly Dictionary<string, CharacterParams.TargetParams> tempParams = new Dictionary<string, CharacterParams.TargetParams>();

        private void ChangeParams(string tag, AIState state, float? priority = null, bool onlyExisting = false)
        {
            if (!AIParams.TryGetTarget(tag, out CharacterParams.TargetParams targetParams))
            {
                if (!onlyExisting && !tempParams.ContainsKey(tag))
                {
                    if (AIParams.TryAddNewTarget(tag, state, priority ?? 100, out targetParams))
                    {
                        tempParams.Add(tag, targetParams);
                    }
                }
            }
            if (targetParams != null)
            {
                if (priority.HasValue)
                {
                    targetParams.Priority = priority.Value;
                }
                targetParams.State = state;
                if (!modifiedParams.ContainsKey(tag))
                {
                    modifiedParams.Add(tag, targetParams);
                }
            }
        }

        private void ChangeTargetState(string tag, AIState state, float? priority = null)
        {
            isStateChanged = true;
            SetStateResetTimer();
            ChangeParams(tag, state, priority);
        }

        /// <summary>
        /// Temporarily changes the predefined state for a target. Eg. Idle -> Attack.
        /// </summary>
        private void ChangeTargetState(Character target, AIState state, float? priority = null)
        {
            isStateChanged = true;
            SetStateResetTimer();
            ChangeParams(target.SpeciesName, state, priority);
            if (target.IsHuman)
            {
                // Target also items, because if we are blind and the target doesn't move, we can only perceive the target when it uses items
                if (state == AIState.Attack || state == AIState.Escape)
                {
                    ChangeParams("weapon", state, priority);
                    ChangeParams("tool", state, priority);
                }
                if (state == AIState.Attack)
                {
                    // If the target is shooting from the submarine, we might not perceive it because it doesn't move.
                    // --> Target the submarine too.
                    if (target.Submarine != null && (canAttackDoors || canAttackWalls))
                    {
                        ChangeParams("room", state, priority);
                        if (canAttackWalls)
                        {
                            ChangeParams("wall", state, priority);
                        }
                        if (canAttackDoors)
                        {
                            ChangeParams("door", state, priority);
                        }
                    }
                    ChangeParams("provocative", state, priority, onlyExisting: true);
                    ChangeParams("light", state, priority, onlyExisting: true);
                }
            }
        }

        private void ResetOriginalState()
        {
            isStateChanged = false;
            modifiedParams.Keys.ForEachMod(tag => TryResetOriginalState(tag));
        }
        #endregion

        protected override void OnStateChanged(AIState from, AIState to)
        {
            LatchOntoAI?.DeattachFromBody();
            Character.AnimController.ReleaseStuckLimbs();
            escapeTarget = null;
            AttackingLimb = null;
            movementMargin = 0;
            allGapsSearched = false;
            unreachableGaps.Clear();
            if (isStateChanged && to == AIState.Idle && from != to)
            {
                SetStateResetTimer();
            }
        }

        private void SetStateResetTimer() => stateResetTimer = stateResetCooldown * Rand.Range(0.75f, 1.25f);

        private float GetPerceivingRange(AITarget target) => Math.Max(target.SightRange * Sight, target.SoundRange * Hearing);

        private bool CanPerceive(AITarget target, float dist = -1, float distSquared = -1)
        {
            if (distSquared > -1)
            {
                return distSquared <= MathUtils.Pow(target.SightRange * Sight, 2) || distSquared <=  MathUtils.Pow(target.SoundRange * Hearing, 2);
            }
            else
            {
                return dist <= target.SightRange * Sight || dist <= target.SoundRange * Hearing;
            }
        }

        public void ReevaluateAttacks()
        {
            canAttackWalls = LatchOntoAI != null && LatchOntoAI.AttachToSub;
            canAttackDoors = false;
            canAttackCharacters = false;
            foreach (var limb in Character.AnimController.Limbs)
            {
                if (limb.IsSevered) { continue; }
                if (limb.attack == null) { continue; }
                if (!canAttackWalls)
                {
                    canAttackWalls = limb.attack.IsValidTarget(AttackTarget.Structure) && limb.attack.StructureDamage > 0;
                }
                if (!canAttackDoors)
                {
                    canAttackDoors = limb.attack.IsValidTarget(AttackTarget.Structure) && limb.attack.ItemDamage > 0;
                }
                if (!canAttackCharacters)
                {
                    canAttackCharacters = limb.attack.IsValidTarget(AttackTarget.Character);
                }
            }
            if (PathSteering != null)
            {
                PathSteering.CanBreakDoors = canAttackDoors;
            }
        }

        private Vector2 returnDir;
        private float returnTimer;
        private void SteerInsideLevel(float deltaTime)
        {
            if (SteeringManager is IndoorsSteeringManager) { return; }
            if (Level.Loaded == null) { return; }
            Vector2 levelSimSize = ConvertUnits.ToSimUnits(Level.Loaded.Size.X, Level.Loaded.Size.Y);
            float returnTime = 3;
            if (SimPosition.Y < 0)
            {
                // Too far down
                returnTimer = returnTime * Rand.Range(0.75f, 1.25f);
                returnDir = Vector2.UnitY;
            }
            if (SimPosition.X < 0)
            {
                // Too far left
                returnTimer = returnTime * Rand.Range(0.75f, 1.25f);
                returnDir = Vector2.UnitX;
            }
            if (SimPosition.X > levelSimSize.X)
            {
                // Too far right
                returnTimer = returnTime * Rand.Range(0.75f, 1.25f);
                returnDir = -Vector2.UnitX;
            }
            if (returnTimer > 0)
            {
                returnTimer -= deltaTime;
                SteeringManager.Reset();
                SteeringManager.SteeringManual(deltaTime, returnDir);
            }
        }

        private bool CanPassThroughHole(Structure wall, int sectionIndex)
        {
            if (!wall.SectionBodyDisabled(sectionIndex)) return false;
            int holeCount = 1;
            for (int j = sectionIndex - 1; j > sectionIndex - requiredHoleCount; j--)
            {
                if (wall.SectionBodyDisabled(j))
                    holeCount++;
                else
                    break;
            }
            for (int j = sectionIndex + 1; j < sectionIndex + requiredHoleCount; j++)
            {
                if (wall.SectionBodyDisabled(j))
                    holeCount++;
                else
                    break;
            }

            return holeCount >= requiredHoleCount;
        }

        private readonly List<Limb> targetLimbs = new List<Limb>();
        public Limb GetTargetLimb(Limb attackLimb, Character target, LimbType targetLimbType = LimbType.None)
        {
            targetLimbs.Clear();
            foreach (var limb in target.AnimController.Limbs)
            {
                if (limb.type == targetLimbType || targetLimbType == LimbType.None)
                {
                    targetLimbs.Add(limb);
                }
            }
            if (targetLimbs.None())
            {
                // If no limbs of given type was found, accept any limb.
                targetLimbs.AddRange(target.AnimController.Limbs);
            }
            float closestDist = float.MaxValue;
            Limb targetLimb = null;
            foreach (Limb limb in targetLimbs)
            {
                if (limb.IsSevered) { continue; }
                float dist = Vector2.DistanceSquared(limb.WorldPosition, attackLimb.WorldPosition) / Math.Max(limb.AttackPriority, 0.1f);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    targetLimb = limb;
                }
            }
            return targetLimb;
        }

        private Character GetOwner(Item item)
        {
            // If the item is held by a character, attack the character instead.
            var pickable = item.GetComponent<Pickable>();
            if (pickable != null)
            {
                Character owner = pickable.Picker ?? item.FindParentInventory(i => i.Owner is Character)?.Owner as Character;
                if (owner != null)
                {
                    var target = owner.AiTarget;
                    if (target?.Entity != null && !target.Entity.Removed)
                    {
                        return owner;
                    }
                }
            }
            return null;
        }

        public static bool IsFriendly(Character me, Character other) => other.SpeciesName == me.SpeciesName || other.Params.CompareGroup(me.Params.Group);
    }

    //the "memory" of the Character 
    //keeps track of how preferable it is to attack a specific target
    //(if the Character can't inflict much damage the target, the priority decreases
    //and if the target attacks the Character, the priority increases)
    class AITargetMemory
    {
        public readonly AITarget Target;
        public Vector2 Location { get; set; }

        private float priority;
        
        public float Priority
        {
            get { return priority; }
            set { priority = MathHelper.Clamp(value, 1.0f, 100.0f); }
        }

        public AITargetMemory(AITarget target, float priority)
        {
            Target = target;
            Location = target.WorldPosition;
            this.priority = priority;
        }
    }
}
