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

        private const float UpdateTargetsInterval = 1.0f;

        private const float RaycastInterval = 1.0f;

        private float avoidLookAheadDistance;

        private SteeringManager outsideSteering, insideSteering;

        private float updateTargetsTimer;

        private float raycastTimer;
                
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
        
        private float colliderWidth;
        private float colliderLength;
        private bool canAttackSub;
        private bool canAttackCharacters;

        // TODO: expose?
        private readonly float priorityAvoidIncreasement = 1;
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
                var target = GetTarget(Character.HumanSpeciesName);
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

        public bool Reverse { get; private set; }

        public EnemyAIController(Character c, string seed) : base(c)
        {
            if (c.IsHuman)
            {
                throw new Exception($"Tried to create an enemy ai controller for human!");
            }
            string file = Character.GetConfigFilePath(c.SpeciesName);
            if (!Character.TryGetConfigFile(file, out XDocument doc))
            {
                throw new Exception($"Failed to load the config file for {c.SpeciesName} from {file}!");
            }
            var mainElement = doc.Root.IsOverride() ? doc.Root.FirstElement() : doc.Root;
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
                DebugConsole.ThrowError("Error in file \"" + file + "\" - no AI element found.");
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

            bool canBreakDoors = false;
            if (GetTarget("room")?.Priority > 0.0f)
            {
                var currentContexts = Character.GetAttackContexts();
                foreach (Limb limb in Character.AnimController.Limbs)
                {
                    if (limb.attack == null) { continue; }
                    if (!limb.attack.IsValidTarget(AttackTarget.Structure)) { continue; }
                    if (limb.attack.IsValidContext(currentContexts) && limb.attack.StructureDamage > 0.0f)
                    {
                        canBreakDoors = true;
                        break;
                    }
                }
            }

            outsideSteering = new SteeringManager(this);
            insideSteering = new IndoorsSteeringManager(this, false, canBreakDoors);
            steeringManager = outsideSteering;
            State = AIState.Idle;

            var size = Character.AnimController.Collider.GetSize();
            colliderWidth = size.X;
            colliderLength = size.Y;

            avoidLookAheadDistance = Math.Max(colliderWidth * 3, 1.5f);

            canAttackSub = Character.AnimController.CanAttackSubmarine;
            canAttackCharacters = Character.AnimController.CanAttackCharacters;
        }

        private CharacterParams.AIParams AIParams => Character.Params.AI;
        private CharacterParams.TargetParams GetTarget(string targetTag) => AIParams.GetTarget(targetTag, false);

        public override void SelectTarget(AITarget target) => SelectTarget(target, 100);

        public void SelectTarget(AITarget target, float priority)
        {
            SelectedAiTarget = target;
            selectedTargetMemory = GetTargetMemory(target);
            selectedTargetMemory.Priority = priority;
        }

        private float escapeMargin;
        
        public override void Update(float deltaTime)
        {
            if (DisableEnemyAI) { return; }
            base.Update(deltaTime);
            bool ignorePlatforms = (-Character.AnimController.TargetMovement.Y > Math.Abs(Character.AnimController.TargetMovement.X));

            if (steeringManager is IndoorsSteeringManager)
            {
                var currPath = ((IndoorsSteeringManager)steeringManager).CurrentPath;
                if (currPath != null && currPath.CurrentNode != null)
                {
                    if (currPath.CurrentNode.SimPosition.Y < Character.AnimController.GetColliderBottom().Y)
                    {
                        ignorePlatforms = true;
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
            UpdateTargetMemories(deltaTime);
            if (updateTargetsTimer > 0.0)
            {
                updateTargetsTimer -= deltaTime;
            }
            else
            {
                UpdateTargets(Character, out CharacterParams.TargetParams targetingParams);
                updateTargetsTimer = UpdateTargetsInterval * Rand.Range(0.75f, 1.25f);

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

            if (SelectedAiTarget != null && (SelectedAiTarget.Entity == null || SelectedAiTarget.Entity.Removed))
            {
                State = AIState.Idle;
                return;
            }

            if (Character.Submarine == null)
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
                    float distance = Vector2.DistanceSquared(WorldPosition, SelectedAiTarget.WorldPosition);
                    var attackLimb = GetAttackLimb(SelectedAiTarget.WorldPosition);
                    if (attackLimb != null && distance <= Math.Pow(attackLimb.attack.Range, 2))
                    {
                        run = true;
                        UpdateAttack(deltaTime);
                    }
                    else
                    {
                        float reactDistance = selectedTargetingParams != null && selectedTargetingParams.ReactDistance > 0 ? selectedTargetingParams.ReactDistance : GetPerceivingRange(SelectedAiTarget);
                        if (distance <= Math.Pow(reactDistance + escapeMargin, 2))
                        {
                            if (State == AIState.Aggressive || State == AIState.PassiveAggressive && distance < Math.Pow(reactDistance / 2, 2))
                            {
                                run = true;
                                UpdateAttack(deltaTime);
                            }
                            else
                            {
                                run = distance < Math.Pow(reactDistance / 2, 2);
                                escapeMargin = MathHelper.Clamp(escapeMargin += deltaTime, 200, 1000);
                                UpdateEscape(deltaTime);
                            }
                        }
                        else
                        {
                            escapeMargin = 0;
                            UpdateIdle(deltaTime);
                        }
                    }
                    break;
                default:
                    throw new NotImplementedException();
            }

            LatchOntoAI?.Update(this, deltaTime);
            IsSteeringThroughGap = false;
            if (SwarmBehavior != null)
            {
                SwarmBehavior.IsActive = State == AIState.Idle && Character.CurrentHull == null;
                SwarmBehavior.Refresh();
                SwarmBehavior.UpdateSteering(deltaTime);
            }
            steeringManager.Update(Character.AnimController.GetCurrentSpeed(run));
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
                    steeringManager.SteeringManual(deltaTime, Vector2.UnitY);
                    SteeringManager.SteeringAvoid(deltaTime, lookAheadDistance: avoidLookAheadDistance, weight: 1);
                    return;
                }
                SteerInsideLevel(deltaTime);
            }
            var target = SelectedAiTarget ?? _lastAiTarget;
            if (target?.Entity != null && !target.Entity.Removed && PreviousState == AIState.Attack)
            {
                if (Character.CurrentHull == null)
                {
                    var memory = GetTargetMemory(target);
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
                            steeringManager.SteeringSeek(Character.GetRelativeSimPosition(target.Entity, location), 5);
                            SteeringManager.SteeringAvoid(deltaTime, lookAheadDistance: avoidLookAheadDistance, weight: 5);
                            return;
                        }
                    }
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
        private Vector2 escapePoint;
        private void UpdateEscape(float deltaTime)
        {
            if (SelectedAiTarget == null || SelectedAiTarget.Entity == null || SelectedAiTarget.Entity.Removed)
            {
                State = AIState.Idle;
                return;
            }
            else if (selectedTargetMemory != null)
            {
                selectedTargetMemory.Priority += deltaTime * priorityFearIncreasement;
            }
            if (Character.CurrentHull != null)
            {
                // Seek exit, if inside
                if (SteeringManager is IndoorsSteeringManager indoorSteering && escapePoint == Vector2.Zero)
                {
                    foreach (Gap gap in Gap.GapList)
                    {
                        if (gap.Submarine != Character.Submarine) { continue; }
                        if (gap.Open < 1 || gap.IsRoomToRoom) { continue; }
                        if (escapePoint != Vector2.Zero)
                        {
                            // Ignore the gap if it's further away than the previously assigned escape point
                            if (Vector2.DistanceSquared(Character.SimPosition, gap.SimPosition) > Vector2.DistanceSquared(Character.SimPosition, escapePoint)) { continue; }
                        }
                        var path = indoorSteering.PathFinder.FindPath(Character.SimPosition, gap.SimPosition, Character.Submarine);
                        if (!path.Unreachable)
                        {
                            escapePoint = gap.SimPosition;
                        }
                    }
                }
            }
            else if (Character.Submarine == null)
            {
                SteerInsideLevel(deltaTime);
            }
            if (escapePoint != Vector2.Zero && Vector2.DistanceSquared(Character.SimPosition, escapePoint) > 1)
            {
                SteeringManager.SteeringSeek(escapePoint, 10);
            }
            else
            {
                // If outside or near enough the escapePoint, steer away
                escapePoint = Vector2.Zero;
                Vector2 escapeDir = Vector2.Normalize(WorldPosition - SelectedAiTarget.WorldPosition);
                if (!MathUtils.IsValid(escapeDir)) escapeDir = Vector2.UnitY;
                SteeringManager.SteeringManual(deltaTime, escapeDir);
                SteeringManager.SteeringWander();
                SteeringManager.SteeringAvoid(deltaTime, lookAheadDistance: avoidLookAheadDistance, weight: 5);
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
                var pickable = item.GetComponent<Pickable>();
                if (pickable != null)
                {
                    var target = pickable.Picker?.AiTarget;
                    if (target != null)
                    {
                        SelectedAiTarget = target;
                    }
                }
            }

            if (raycastTimer > 0.0)
            {
                raycastTimer -= deltaTime;
            }
            else
            {
                if (!IsLatchedOnSub)
                {
                    UpdateWallTarget();
                }
                raycastTimer = RaycastInterval;
            }

            if (wallTarget != null)
            {
                attackWorldPos = wallTarget.Position;
                if (wallTarget.Structure.Submarine != null)
                {
                    attackWorldPos += wallTarget.Structure.Submarine.Position;
                }
                attackSimPos = ConvertUnits.ToSimUnits(attackWorldPos);
            }
            else
            {
                attackSimPos = Character.GetRelativeSimPosition(SelectedAiTarget.Entity);
            }

            if (CanEnterSubmarine && Character.CurrentHull != null)
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
                            if (SteerThroughGap(wall, section, section.gap.WorldPosition, deltaTime))
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
                    if (door?.LinkedGap?.FlowTargetHull != null && !door.LinkedGap.IsRoomToRoom && door.IsOpen)
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
                // Targeting only the outer walls
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
            if (!canAttack && SelectedAiTarget.Entity.Submarine != null && !canAttackSub)
            {
                float dist = Vector2.Distance(Character.AnimController.MainLimb.WorldPosition, attackWorldPos);
                if (wallTarget != null)
                {
                    // Steer towards the target, but turn away if a wall is blocking the way
                    if (dist < ConvertUnits.ToDisplayUnits(colliderLength) * 3)
                    {
                        State = AIState.Idle;
                        IgnoreTarget(SelectedAiTarget);
                        // Resetting the ai target prevents the character from chasing it
                        ResetAITarget();
                        return;
                    }
                }
                else if (dist < 1000)
                {
                    // Check that we are not bumping into a door
                    Vector2 rayStart = SimPosition;
                    if (Character.Submarine == null)
                    {
                        rayStart -= SelectedAiTarget.Entity.Submarine.SimPosition;
                    }
                    Vector2 toTarget = SelectedAiTarget.WorldPosition - WorldPosition;
                    Vector2 rayEnd = rayStart + toTarget.ClampLength(Character.AnimController.Collider.GetLocalFront().Length() * 2);
                    Body closestBody = Submarine.CheckVisibility(rayStart, rayEnd, ignoreSubs: true);
                    if (Submarine.LastPickedFraction != 1.0f && closestBody != null && closestBody.UserData is Item i && i.Submarine != null && i.GetComponent<Door>() != null)
                    {
                        // Target is unreachable, there's a door ahead
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
                // Target a specific limb instead of the target center position
                if (wallTarget == null && targetCharacter != null)
                {
                    var targetLimbType = AttackingLimb.Params.Attack.Attack.TargetLimbType;
                    attackTargetLimb = GetTargetLimb(AttackingLimb, targetCharacter, targetLimbType);
                    if (attackTargetLimb == null)
                    {
                        State = AIState.Idle;
                        IgnoreTarget(SelectedAiTarget);
                        return;
                    }
                    attackWorldPos = attackTargetLimb.WorldPosition;
                    attackSimPos = Character.GetRelativeSimPosition(attackTargetLimb);
                }
                // Check that we can reach the target
                Vector2 toTarget = attackWorldPos - AttackingLimb.WorldPosition;
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
                    // Add a margin when the target is moving away, because otherwise it might be difficult to reach it (the attack takes some time to perform)
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

                distance = toTarget.Length();
                canAttack = distance < AttackingLimb.attack.Range;
                if (!canAttack && !IsCoolDownRunning)
                {
                    // If not, reset the attacking limb, if the cooldown is not running
                    // Don't use the property, because we don't want cancel reversing, if we are reversing.
                    _attackingLimb = null;
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

            Vector2 offset = Character.SimPosition - steeringLimb.SimPosition;
            // Offset so that we don't overshoot the movement
            Vector2 steerPos = attackSimPos + offset;

            if (SteeringManager is IndoorsSteeringManager pathSteering)
            {
                if (pathSteering.CurrentPath != null)
                {
                    // Attack doors
                    if (canAttackSub)
                    {
                        // If the target is in the same hull, there shouldn't be any doors blocking the path
                        if (targetCharacter == null || targetCharacter.CurrentHull != Character.CurrentHull)
                        {
                            var door = pathSteering.CurrentPath.CurrentNode?.ConnectedDoor ?? pathSteering.CurrentPath.NextNode?.ConnectedDoor;
                            if (door != null && !door.IsOpen)
                            {
                                if (SelectedAiTarget != door.Item.AiTarget)
                                {
                                    SelectTarget(door.Item.AiTarget, selectedTargetMemory.Priority);
                                    return;
                                }
                            }
                        }
                    }
                    // Steer towards the target if in the same room and swimming
                    if ((Character.AnimController.InWater || pursue) && targetCharacter != null && VisibleHulls.Contains(targetCharacter.CurrentHull))
                    {
                        SteeringManager.SteeringManual(deltaTime, Vector2.Normalize(attackSimPos - steeringLimb.SimPosition));
                    }
                    else
                    {
                        SteeringManager.SteeringSeek(steerPos, 2);
                        // Switch to Idle when cannot reach the target and if cannot damage the walls
                        if ((!canAttackSub || wallTarget == null) && !pathSteering.IsPathDirty && pathSteering.CurrentPath.Unreachable)
                        {
                            State = AIState.Idle;
                            return;
                        }
                    }
                }
                else
                {
                    SteeringManager.SteeringSeek(steerPos, 10);
                    SteeringManager.SteeringAvoid(deltaTime, lookAheadDistance: avoidLookAheadDistance, weight: 5);
                }
            }
            else
            {
                SteeringManager.SteeringSeek(steerPos, 10);
                SteeringManager.SteeringAvoid(deltaTime, lookAheadDistance: avoidLookAheadDistance, weight: 5);
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
            SelectedAiTarget = wall.AiTarget;
            wallTarget = null;
            LatchOntoAI?.DeattachFromBody();
            Character.AnimController.ReleaseStuckLimbs();
            Hull targetHull = section.gap?.FlowTargetHull;
            float distance = Vector2.Distance(Character.WorldPosition, targetWorldPos);
            float maxDistance = Math.Min(wall.Rect.Width, wall.Rect.Height);
            if (distance > maxDistance)
            {
                return false;
            }
            if (targetHull != null)
            {
                if (wall.IsHorizontal)
                {
                    targetWorldPos.Y = targetHull.WorldRect.Y - targetHull.Rect.Height / 2;
                }
                else
                {
                    targetWorldPos.X = targetHull.WorldRect.Center.X;
                }
                steeringManager.SteeringManual(deltaTime, Vector2.Normalize(targetWorldPos - Character.WorldPosition));
                return true;
            }
            return false;
        }

        private bool CanAttack(Entity target)
        {
            if (target == null) { return false; }
            if (target is Character ch)
            {
                if (Character.CurrentHull == null && ch.CurrentHull != null || Character.CurrentHull != null && ch.CurrentHull == null)
                {
                    return false;
                }
            }
            return true;
        }

        private Limb GetAttackLimb(Vector2 attackWorldPos, Limb ignoredLimb = null)
        {
            var currentContexts = Character.GetAttackContexts();
            Entity target = wallTarget != null ? wallTarget.Structure : SelectedAiTarget?.Entity;
            if (!CanAttack(target)) { return null; }
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
                if (!attack.IsValidTarget(target)) { continue; }
                if (target is ISerializableEntity se && target is Character)
                {
                    if (attack.Conditionals.Any(c => !c.Matches(se))) { continue; }
                }
                if (attack.Conditionals.Any(c => c.TargetSelf && !c.Matches(Character))) { continue; }
                float priority = CalculatePriority(limb, attackWorldPos);
                if (priority > currentPriority)
                {
                    currentPriority = priority;
                    selectedLimb = limb;
                }
            }
            return selectedLimb;

            float CalculatePriority(Limb limb, Vector2 attackPos)
            {
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

            //check if there's a wall between the target and the Character   
            Vector2 rayStart = SimPosition;
            Vector2 rayEnd = SelectedAiTarget.SimPosition;
            bool offset = SelectedAiTarget.Entity.Submarine != null && Character.Submarine == null;

            if (offset)
            {
                rayStart -= SelectedAiTarget.Entity.Submarine.SimPosition;
            }

            Body closestBody = Submarine.CheckVisibility(rayStart, rayEnd, ignoreSubs: true);

            if (Submarine.LastPickedFraction == 1.0f || closestBody == null)
            {
                return;
            }

            if (closestBody.UserData is Structure wall && wall.Submarine != null)
            {
                int sectionIndex = wall.FindSectionIndex(ConvertUnits.ToDisplayUnits(Submarine.LastPickedPosition));

                float sectionDamage = wall.SectionDamage(sectionIndex);
                for (int i = sectionIndex - 2; i <= sectionIndex + 2; i++)
                {
                    if (wall.SectionBodyDisabled(i))
                    {
                        if (CanEnterSubmarine && CanPassThroughHole(wall, i))
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
                wallTarget = new WallTarget(sectionPos, wall, sectionIndex);
            }         
        }

        public override void OnAttacked(Character attacker, AttackResult attackResult)
        {
            float reactionTime = Rand.Range(0.1f, 0.3f);
            updateTargetsTimer = Math.Min(updateTargetsTimer, reactionTime);
            
            LatchOntoAI?.DeattachFromBody();
            Character.AnimController.ReleaseStuckLimbs();

            if (Character.HealthPercentage <= FleeHealthThreshold)
            {
                State = AIState.Flee;
                SelectedAiTarget = null;
                wallTarget = null;
                return;
            }

            if (attacker == null || attacker.AiTarget == null) { return; }

            if (attackResult.Damage > 0.0f && Character.Params.AI.AttackWhenProvoked)
            {
                if (attacker.Submarine == Character.Submarine && canAttackCharacters ||
                    attacker.Submarine != null && canAttackSub)
                {
                    ChangeTargetState(attacker, AIState.Attack, 100);
                }
            }

            AITargetMemory targetMemory = GetTargetMemory(attacker.AiTarget);
            targetMemory.Priority += GetRelativeDamage(attackResult.Damage, Character.Vitality) * AggressionHurt;

            // Only allow to react once. Otherwise would attack the target with only a fraction of a cooldown
            bool retaliate = SelectedAiTarget != attacker.AiTarget && attacker.Submarine == Character.Submarine;
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
                    SelectTarget(aiTarget, GetTargetMemory(SelectedAiTarget).Priority);
                }
            }
            if (SelectedAiTarget.Entity is IDamageable damageTarget)
            {
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
                SteeringManager.SteeringAvoid(deltaTime, lookAheadDistance: avoidLookAheadDistance, weight: 5);
            }
        }

        #endregion

        #region Eat

        private void UpdateEating(float deltaTime)
        {
            if (SelectedAiTarget == null)
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
                Vector2 mouthPos = Character.AnimController.GetMouthPosition().Value;
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
                        SteeringManager.SteeringAvoid(deltaTime, lookAheadDistance: avoidLookAheadDistance, weight: 5);
                    }
                }
            }
            else
            {
                IgnoreTarget(SelectedAiTarget);
                State = AIState.Idle;
            }
        }

        #endregion

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
                    if (aiTarget.Entity.Submarine != null && aiTarget.Entity.Submarine.IsOutpost) { continue; }
                }
                Character targetCharacter = aiTarget.Entity as Character;
                //ignore the aitarget if it is the Character itself
                if (targetCharacter == character) { continue; }

                float valueModifier = 1;
                string targetingTag = null;
                if (targetCharacter != null)
                {
                    if (targetCharacter.Submarine != Character.Submarine)
                    {
                        // In a different sub or the target is outside when we are inside or vice versa.
                        continue;
                    }
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
                        if (enemy.CombatStrength > CombatStrength)
                        {
                            targetingTag = "stronger";
                        }
                        else if (enemy.CombatStrength < CombatStrength)
                        {
                            targetingTag = "weaker";
                        }
                        if (targetingTag == "stronger" && State == AIState.Escape && SelectedAiTarget.Entity is Character c && c.AIController is EnemyAIController)
                        {
                            // Frightened
                            valueModifier = 2;
                        }
                    }
                }
                else if (aiTarget.Entity != null)
                {
                    // Ignore the target if it's a room and the character is already inside a sub
                    if (character.CurrentHull != null && aiTarget.Entity is Hull) { continue; }
                    
                    Door door = null;
                    if (aiTarget.Entity is Item item)
                    {
                        //item inside and we're outside -> attack the hull
                        if (item.CurrentHull != null && character.CurrentHull == null)
                        {
                            targetingTag = "room";
                        }

                        door = item.GetComponent<Door>();
                        foreach (var prio in AIParams.Targets)
                        {
                            if (item.HasTag(prio.Tag))
                            {
                                targetingTag = prio.Tag;
                                break;
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
                        if (s.IsPlatform)
                        {
                            continue;
                        }
                        if (character.CurrentHull != null)
                        {
                            // Ignore walls when inside (walltargets still work)
                            continue;
                        }
                        valueModifier = 1;
                        if (!Character.AnimController.CanEnterSubmarine)
                        {
                            // Ignore disabled walls
                            bool isBroken = false;
                            if (!isBroken)
                            {
                                for (int i = 0; i < s.Sections.Length; i++)
                                {
                                    if (!s.SectionBodyDisabled(i))
                                    {
                                        isBroken = false;
                                        break;
                                    }
                                }
                            }
                            if (isBroken)
                            {
                                continue;
                            }
                        }
                        for (int i = 0; i < s.Sections.Length; i++)
                        {
                            var section = s.Sections[i];
                            if (section.gap == null) { continue; }
                            bool leadsInside = !section.gap.IsRoomToRoom && section.gap.FlowTargetHull != null;
                            if (Character.AnimController.CanEnterSubmarine)
                            {
                                if (CanPassThroughHole(s, i))
                                {
                                    valueModifier *= leadsInside ? (AggressiveBoarding ? 5 : 1) : 0;
                                }
                                else
                                {
                                    // Ignore holes that cannot be passed through if cannot attack items/structures. Holes that are big enough should be targeted, so that we can get in
                                    if (!canAttackSub)
                                    {
                                        valueModifier = 0;
                                        break;
                                    }
                                    if (AggressiveBoarding)
                                    {
                                        // Up to 100% priority increase for every gap in the wall
                                        valueModifier *= 1 + section.gap.Open;
                                    }
                                }
                            }
                            else if (!leadsInside)
                            {
                                // Ignore inner walls
                                valueModifier = 0;
                                break;
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
                        bool isOpen = door.IsOpen;
                        if (!isOpen && (!canAttackSub))
                        {
                            // Ignore doors that are not open if cannot attack items/structures. Open doors should be targeted, so that we can get in if we are aggressive boarders
                            valueModifier = 0;
                        }
                        if (character.CurrentHull == null)
                        {
                            valueModifier = isOutdoor ? 1 : 0;
                        }
                        else if (AggressiveBoarding)
                        {
                            // Increase priority if the character is outside and an aggressive boarder, and the door is from outside to inside
                            if (character.CurrentHull == null)
                            {
                                valueModifier *= isOpen ? 5 : 1;
                            }
                            else
                            {
                                valueModifier *= isOpen ? 0 : 1;
                            }
                        }
                        else if (!Character.AnimController.CanEnterSubmarine && isOpen) //ignore broken and open doors
                        {
                            continue;
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

                AITargetMemory targetMemory = GetTargetMemory(aiTarget);
                if (Character.CurrentHull != null && Math.Abs(toTarget.Y) > Character.CurrentHull.Size.Y)
                {
                    // Inside the sub, treat objects that are up or down, as they were farther away.
                    dist *= 3;
                }
                valueModifier *= targetMemory.Priority / (float)Math.Sqrt(dist);

                if (valueModifier > targetValue)
                {
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

        private AITargetMemory GetTargetMemory(AITarget target)
        {
            if (!targetMemories.TryGetValue(target, out AITargetMemory memory))
            {
                memory = new AITargetMemory(target, 10);
                targetMemories.Add(target, memory);
            }
            return memory;
        }

        private readonly List<AITarget> removals = new List<AITarget>();
        private void UpdateTargetMemories(float deltaTime)
        {
            if (_selectedAiTarget != null)
            {
                if (_selectedAiTarget.Entity == null || _selectedAiTarget.Entity.Removed)
                {
                    _selectedAiTarget = null;
                }
                else if (CanPerceive(_selectedAiTarget, distSquared: Vector2.DistanceSquared(Character.WorldPosition, _selectedAiTarget.WorldPosition)))
                {
                    var memory = GetTargetMemory(_selectedAiTarget);
                    memory.Location = _selectedAiTarget.WorldPosition;
                }
            }
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

        private const float targetIgnoreTime = 5;
        private float targetIgnoreTimer;
        private readonly HashSet<AITarget> ignoredTargets = new HashSet<AITarget>();
        public void IgnoreTarget(AITarget target)
        {
            if (target == null) { return; }
            ignoredTargets.Add(target);
            targetIgnoreTimer = targetIgnoreTime * Rand.Range(0.75f, 1.25f);
        }

        protected override void OnTargetChanged(AITarget previousTarget, AITarget newTarget)
        {
            if (previousTarget == null || newTarget == null) { return; }
            var previousCharacter = previousTarget.Entity as Character;
            var newCharacter = newTarget.Entity as Character;
            if (previousCharacter == null && newCharacter == null)
            {
                return;
            }
            if (previousCharacter != null && newCharacter != null && previousCharacter.SpeciesName == newCharacter.SpeciesName)
            {
                return;
            }
            modifiedParams.Keys.ForEachMod(tag => TryResetOriginalState(tag));
        }
        #endregion

        #region State switching

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

        /// <summary>
        /// Temporarily changes the predefined state for a target. Eg. Idle -> Attack.
        /// </summary>
        private void ChangeTargetState(Character target, AIState state, float? priority = null)
        {
            ChangeParams(target.SpeciesName);
            // If the target is shooting from the submarine, we might not perceive it because it doesn't move.
            // --> Target the submarine too.
            if (target.Submarine != null && state == AIState.Attack && canAttackSub)
            {
                ChangeParams("room");
                ChangeParams("wall");
                ChangeParams("door");
            }

            void ChangeParams(string tag)
            {
                if (!AIParams.TryGetTarget(tag, out CharacterParams.TargetParams targetParams))
                {
                    if (AIParams.TryAddNewTarget(tag, state, priority ?? 100, out targetParams))
                    {
                        tempParams.Add(tag, targetParams);
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
        }
        #endregion

        protected override void OnStateChanged(AIState from, AIState to)
        {
            LatchOntoAI?.DeattachFromBody();
            Character.AnimController.ReleaseStuckLimbs();
            escapePoint = Vector2.Zero;
            AttackingLimb = null;
            escapeMargin = 0;
        }

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

        private void SteerInsideLevel(float deltaTime)
        {
            if (Level.Loaded == null) { return; } 
            
            Vector2 levelSimSize = new Vector2(
                ConvertUnits.ToSimUnits(Level.Loaded.Size.X),
                ConvertUnits.ToSimUnits(Level.Loaded.Size.Y));

            float margin = 10.0f;

            if (SimPosition.Y < 0.0)
            {
                steeringManager.SteeringManual(deltaTime, Vector2.UnitY * MathUtils.InverseLerp(0.0f, -margin, SimPosition.Y));
            }
            if (SimPosition.X < 0.0f)
            {
                steeringManager.SteeringManual(deltaTime, Vector2.UnitX * MathUtils.InverseLerp(0.0f, -margin, SimPosition.X));
            }
            if (SimPosition.X > levelSimSize.X)
            {
                steeringManager.SteeringManual(deltaTime, Vector2.UnitX * MathUtils.InverseLerp(levelSimSize.X, levelSimSize.X + margin, SimPosition.X));
            }            
        }

        private int GetMinimumPassableHoleCount()
        {
            return (int)Math.Ceiling(ConvertUnits.ToDisplayUnits(colliderWidth) / Structure.WallSectionSize);
        }

        private bool CanPassThroughHole(Structure wall, int sectionIndex)
        {
            int requiredHoleCount = GetMinimumPassableHoleCount();
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

        private List<Limb> targetLimbs = new List<Limb>();
        public Limb GetTargetLimb(Limb attackLimb, Character target, LimbType targetLimbType = LimbType.None)
        {
            targetLimbs.Clear();
            foreach (var limb in target.AnimController.Limbs)
            {
                if (limb.IsSevered) { continue; }
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
                float dist = Vector2.DistanceSquared(limb.WorldPosition, attackLimb.WorldPosition) / Math.Max(limb.AttackPriority, 0.1f);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    targetLimb = limb;
                }
            }
            return targetLimb;
        }
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
