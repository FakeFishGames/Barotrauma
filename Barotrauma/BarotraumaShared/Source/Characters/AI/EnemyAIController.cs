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

        public class TargetingPriority
        {
            public string TargetTag;
            public AIState State;
            public float Priority;

            public TargetingPriority(XElement element)
            {
                TargetTag = element.GetAttributeString("tag", "").ToLowerInvariant();
                Enum.TryParse(element.GetAttributeString("state", ""), out State);
                Priority = element.GetAttributeFloat("priority", 0.0f);
            }

            public TargetingPriority(string tag, AIState state, float priority)
            {
                TargetTag = tag;
                State = state;
                Priority = priority;
            }
        }

        private const float UpdateTargetsInterval = 1.0f;

        private const float RaycastInterval = 1.0f;

        private bool attackWhenProvoked;

        private Dictionary<string, TargetingPriority> targetingPriorities = new Dictionary<string, TargetingPriority>();
        
        //the preference to attack a specific type of target (-1.0 - 1.0)
        //0.0 = doesn't attack targets of the type
        //positive values = attacks targets of this type
        //negative values = escapes targets of this type        
        //private float attackRooms, attackHumans, attackWeaker, attackStronger, eatDeadPriority;

        //determines which characters are considered weaker/stronger
        private float combatStrength;

        private SteeringManager outsideSteering, insideSteering;

        private float updateTargetsTimer;

        private float raycastTimer;
                
        private bool IsCoolDownRunning => AttackingLimb != null && AttackingLimb.attack.CoolDownTimer > 0;
        
        private bool aggressiveBoarding;

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
                Reverse = _attackingLimb != null && _attackingLimb.attack.Reverse;
                if (Character.AnimController is FishAnimController fishController)
                {
                    fishController.reverse = Reverse;
                }
            }
        }

        //flee when the health is below this value
        private float fleeHealthThreshold;
        
        private AITargetMemory selectedTargetMemory;
        private float targetValue;
                
        private Dictionary<AITarget, AITargetMemory> targetMemories;

        //the eyesight of the NPC (0.0 = blind, 1.0 = sees every target within sightRange)
        public float sight;
        //how far the NPC can hear targets from (0.0 = deaf, 1.0 = hears every target within soundRange)
        public float hearing;

        private float colliderSize;

        private readonly float aggressiongreed;
        private readonly float aggressionhurt;
        // TODO: expose?
        private readonly float priorityFearIncreasement = 2;
        private readonly float memoryFadeTime = 0.5f;

        public LatchOntoAI LatchOntoAI { get; private set; }
        public SwarmBehavior SwarmBehavior { get; private set; }

        public bool AttackHumans
        {
            get
            {
                var targetingPriority = GetTargetingPriority("human");
                return targetingPriority != null && targetingPriority.State == AIState.Attack && targetingPriority.Priority > 0.0f;
            }
        }

        public bool AttackRooms
        {
            get
            {
                var targetingPriority = GetTargetingPriority("room");
                return targetingPriority != null && targetingPriority.State == AIState.Attack && targetingPriority.Priority > 0.0f;
            }
        }

        public float CombatStrength
        {
            get { return combatStrength; }
        }

        public override bool CanEnterSubmarine
        {
            get
            {
                //can't enter a submarine when attached to something
                return LatchOntoAI == null || !LatchOntoAI.IsAttached;
            }
        }

        public override bool CanFlip
        {
            get
            {
                //can't flip when attached to something or when reversing
                return !Reverse && (LatchOntoAI == null || !LatchOntoAI.IsAttached);
            }
        }

        public bool Reverse { get; private set; }

        public EnemyAIController(Character c, string file, string seed) : base(c)
        {
            targetMemories = new Dictionary<AITarget, AITargetMemory>();
            steeringManager = outsideSteering;

            XDocument doc = XMLExtensions.TryLoadXml(file);
            if (doc == null || doc.Root == null) return;

            List<XElement> aiElements = new List<XElement>();
            List<float> aiCommonness = new List<float>();
            foreach (XElement element in doc.Root.Elements())
            {
                if (element.Name.ToString().ToLowerInvariant() != "ai") continue;                
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
            XElement aiElement = aiElements.Count == 1 ? 
                aiElements[0] : ToolBox.SelectWeightedRandom(aiElements, aiCommonness, random);
            
            combatStrength      = aiElement.GetAttributeFloat("combatstrength", 1.0f);
            attackWhenProvoked  = aiElement.GetAttributeBool("attackwhenprovoked", false);
            aggressiveBoarding  = aiElement.GetAttributeBool("aggressiveboarding", false);

            sight           = aiElement.GetAttributeFloat("sight", 0.0f);
            hearing         = aiElement.GetAttributeFloat("hearing", 0.0f);

            aggressionhurt = aiElement.GetAttributeFloat("aggressionhurt", 100f);
            aggressiongreed = aiElement.GetAttributeFloat("aggressiongreed", 10f);

            fleeHealthThreshold = aiElement.GetAttributeFloat("fleehealththreshold", 0.0f);

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
                    case "targetpriority":
                        targetingPriorities.Add(subElement.GetAttributeString("tag", "").ToLowerInvariant(), new TargetingPriority(subElement));
                        break;
                }
            }

            bool canBreakDoors = false;
            if (GetTargetingPriority("room")?.Priority > 0.0f)
            {
                AttackContext currentContext = Character.GetAttackContext();
                foreach (Limb limb in Character.AnimController.Limbs)
                {
                    if (limb.attack == null) { continue; }
                    if (!limb.attack.IsValidTarget(AttackTarget.Structure)) { continue; }
                    if (limb.attack.IsValidContext(currentContext) && limb.attack.StructureDamage > 0.0f)
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

            colliderSize = 0.1f;
            switch (Character.AnimController.Collider.BodyShape)
            {
                case PhysicsBody.Shape.Capsule:
                case PhysicsBody.Shape.HorizontalCapsule:
                case PhysicsBody.Shape.Circle:
                    colliderSize = Character.AnimController.Collider.radius * 2;
                    break;
                case PhysicsBody.Shape.Rectangle:
                    colliderSize = Math.Min(Character.AnimController.Collider.width, Character.AnimController.Collider.height);
                    break;
            }
        }
        
        private TargetingPriority GetTargetingPriority(string targetTag)
        {
            if (targetingPriorities.TryGetValue(targetTag, out TargetingPriority priority))
            {
                return priority;
            }
            return null;
        }

        public override void SelectTarget(AITarget target) => SelectTarget(target, 100);

        public void SelectTarget(AITarget target, float priority)
        {
            SelectedAiTarget = target;
            selectedTargetMemory = GetTargetMemory(target);
            selectedTargetMemory.Priority = priority;
            targetValue = priority;
        }
        
        public override void Update(float deltaTime)
        {
            if (DisableEnemyAI) { return; }
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

            UpdateTargetMemories(deltaTime);
            if (updateTargetsTimer > 0.0)
            {
                updateTargetsTimer -= deltaTime;
            }
            else
            {
                UpdateTargets(Character, out TargetingPriority targetingPriority);
                updateTargetsTimer = UpdateTargetsInterval;

                if (SelectedAiTarget == null)
                {
                    State = AIState.Idle;
                }
                else if (Character.Health < fleeHealthThreshold && SwarmBehavior == null)
                {
                    // Don't flee from damage if in a swarm.
                    State = AIState.Escape;
                }
                else if (targetingPriority != null)
                {
                    State = targetingPriority.State;
                }
            }

            if (SelectedAiTarget != null && (SelectedAiTarget.Entity == null || SelectedAiTarget.Entity.Removed))
            {
                State = AIState.Idle;
                return;
            }

            if (Character.Submarine == null)
            {
                if (steeringManager != outsideSteering) outsideSteering.Reset();
                steeringManager = outsideSteering;
            }
            else
            {
                if (steeringManager != insideSteering) insideSteering.Reset();
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
                    run = true;
                    UpdateEscape(deltaTime);
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
            if (Character.Submarine == null && 
                SimPosition.Y < ConvertUnits.ToSimUnits(Character.CharacterHealth.CrushDepth * 0.75f))
            {
                //steer straight up if very deep
                steeringManager.SteeringManual(deltaTime, Vector2.UnitY);
                return;
            }

            SteerInsideLevel(deltaTime);

            if (wallTarget != null) { return; }
            
            if (SelectedAiTarget != null)
            {
                Vector2 targetSimPos = Character.Submarine == null ? ConvertUnits.ToSimUnits(SelectedAiTarget.WorldPosition) : SelectedAiTarget.SimPosition;

                steeringManager.SteeringAvoid(deltaTime, colliderSize * 3.0f);
                steeringManager.SteeringSeek(targetSimPos);
            }
            else
            {
                //wander around randomly
                if (Character.Submarine == null)
                {
                    steeringManager.SteeringAvoid(deltaTime, colliderSize * 5.0f);
                }
                steeringManager.SteeringWander(0.5f);
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
                        var path = indoorSteering.PathFinder.FindPath(Character.SimPosition, gap.SimPosition, Character.Submarine);
                        if (!path.Unreachable)
                        {
                            if (escapePoint != Vector2.Zero)
                            {
                                // Ignore the gap if it's further away than the previously assigned escape point
                                if (Vector2.DistanceSquared(Character.SimPosition, gap.SimPosition) > Vector2.DistanceSquared(Character.SimPosition, escapePoint)) { continue; }
                            }
                            escapePoint = gap.SimPosition;
                        }
                    }
                }
            }
            else
            {
                SteerInsideLevel(deltaTime);
            }
            if (escapePoint != Vector2.Zero && Vector2.DistanceSquared(Character.SimPosition, escapePoint) > 1)
            {
                SteeringManager.SteeringSeek(escapePoint);
            }
            else
            {
                // If outside or near enough the escapePoint, steer away
                escapePoint = Vector2.Zero;
                Vector2 escapeDir = Vector2.Normalize(WorldPosition - SelectedAiTarget.WorldPosition);
                if (!MathUtils.IsValid(escapeDir)) escapeDir = Vector2.UnitY;
                SteeringManager.SteeringManual(deltaTime, escapeDir);
                SteeringManager.SteeringWander();
                SteeringManager.SteeringAvoid(deltaTime, colliderSize * 3.0f);
            }
        }

        #endregion

        #region Attack

        private void UpdateAttack(float deltaTime)
        {
            if (SelectedAiTarget == null)
            {
                State = AIState.Idle;
                return;
            }

            Vector2 attackWorldPos = SelectedAiTarget.WorldPosition;
            Vector2 attackSimPos = SelectedAiTarget.SimPosition;

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

            if (SelectedAiTarget.Entity is Character c)
            {
                //target the closest limb if the target is a character
                float closestDist = Vector2.DistanceSquared(SelectedAiTarget.WorldPosition, WorldPosition) * 10.0f;
                foreach (Limb limb in c.AnimController.Limbs)
                {
                    if (limb == null) continue;
                    float dist = Vector2.DistanceSquared(limb.WorldPosition, WorldPosition) / Math.Max(limb.AttackPriority, 0.1f);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        attackWorldPos = limb.WorldPosition;
                        attackSimPos = limb.SimPosition;
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
                attackSimPos = ConvertUnits.ToSimUnits(attackWorldPos);
            }
            else
            {
                attackSimPos = Character.GetRelativeSimPosition(SelectedAiTarget.Entity);
            }

            if (Math.Abs(Character.AnimController.movement.X) > 0.1f && !Character.AnimController.InWater &&
                (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer || Character.Controlled == Character))
            {
                Character.AnimController.TargetDir = Character.WorldPosition.X < attackWorldPos.X ? Direction.Right : Direction.Left;
            }

            if (aggressiveBoarding)
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
                    //steer through the door manually if it's open or broken
                    if (door?.LinkedGap?.FlowTargetHull != null && !door.LinkedGap.IsRoomToRoom && (door.IsOpen || door.Item.Condition <= 0.0f))
                    {
                        LatchOntoAI?.DeattachFromBody();
                        Character.AnimController.ReleaseStuckLimbs();
                        var velocity = Vector2.Normalize(door.LinkedGap.FlowTargetHull.WorldPosition - Character.WorldPosition);
                        steeringManager.SteeringManual(deltaTime, velocity);
                        return;
                    }
                }
            }

            bool canAttack = true;
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
                            }
                            else
                            {
                                UpdateFallBack(attackWorldPos, deltaTime);
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
                                        UpdateFallBack(attackWorldPos, deltaTime);
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
                                        }
                                        else
                                        {
                                            UpdateFallBack(attackWorldPos, deltaTime);
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
                        if (AttackingLimb.attack.SecondaryCoolDown <= 0)
                        {
                            // No (valid) secondary cooldown defined.
                            UpdateFallBack(attackWorldPos, deltaTime);
                            return;
                        }
                        else
                        {
                            if (AttackingLimb.attack.SecondaryCoolDownTimer <= 0)
                            {
                                // Don't allow attacking when the attack target has just changed.
                                if (_previousAiTarget != null && SelectedAiTarget != _previousAiTarget)
                                {
                                    UpdateFallBack(attackWorldPos, deltaTime);
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
                                        UpdateFallBack(attackWorldPos, deltaTime);
                                        return;
                                    }
                                }
                            }
                            else
                            {
                                // Cooldown not yet expired -> steer away from the target
                                UpdateFallBack(attackWorldPos, deltaTime);
                                return;
                            }
                        }
                        break;
                    case AIBehaviorAfterAttack.FallBack:
                    default:
                        UpdateFallBack(attackWorldPos, deltaTime);
                        return;
                }
            }

            if (canAttack)
            {
                if (AttackingLimb == null || _previousAiTarget != SelectedAiTarget)
                {
                    AttackingLimb = GetAttackLimb(attackWorldPos);
                }
                canAttack = AttackingLimb != null && AttackingLimb.attack.CoolDownTimer <= 0;
            }
            float distance = 0;
            if (canAttack)
            {
                // Check that we can reach the target
                distance = Vector2.Distance(AttackingLimb.WorldPosition, attackWorldPos);
                canAttack = distance < AttackingLimb.attack.Range;
                if (!canAttack && !IsCoolDownRunning)
                {
                    // If not, reset the attacking limb, if the cooldown is not running
                    // Don't use the property, because we don't want cancel reversing, if we are reversing.
                    _attackingLimb = null;
                }
            }

            // If the attacking limb is a hand or claw, for example, using it as the steering limb can end in the result where the character circles around the target. For example the Hammerhead steering with the claws when it should use the torso.
            // If we always use the main limb, this causes the character to seek the target with it's torso/head, when it should not. For example Mudraptor steering with it's belly, when it should use it's head.
            // So let's use the one that's closer to the attacking limb.
            Limb steeringLimb;
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
            if (steeringLimb != null)
            {
                Vector2 offset = Character.SimPosition - steeringLimb.SimPosition;
                // Offset so that we don't overshoot the movement
                Vector2 steerPos = attackSimPos + offset;
                SteeringManager.SteeringSeek(steerPos, 10);

                if (SteeringManager is IndoorsSteeringManager indoorsSteering)
                {
                    if (indoorsSteering.CurrentPath != null && !indoorsSteering.IsPathDirty)
                    {
                        if (indoorsSteering.CurrentPath.Unreachable)
                        {
                            if (selectedTargetMemory != null)
                            {
                                //wander around randomly and decrease the priority faster if no path is found
                                selectedTargetMemory.Priority -= deltaTime * memoryFadeTime * 10;
                            }
                            SteeringManager.SteeringWander();
                        }
                        else if (indoorsSteering.CurrentPath.Finished)
                        {
                            SteeringManager.SteeringManual(deltaTime, Vector2.Normalize(attackSimPos - steeringLimb.SimPosition));
                        }
                        else if (indoorsSteering.CurrentPath.CurrentNode?.ConnectedDoor != null)
                        {
                            wallTarget = null;
                            SelectedAiTarget = indoorsSteering.CurrentPath.CurrentNode.ConnectedDoor.Item.AiTarget;
                        }
                        else if (indoorsSteering.CurrentPath.NextNode?.ConnectedDoor != null)
                        {
                            wallTarget = null;
                            SelectedAiTarget = indoorsSteering.CurrentPath.NextNode.ConnectedDoor.Item.AiTarget;
                        }
                    }
                }
                else if (Character.CurrentHull == null)
                {
                    SteeringManager.SteeringAvoid(deltaTime, colliderSize * 1.5f);
                }
            }

            if (canAttack)
            {
                UpdateLimbAttack(deltaTime, AttackingLimb, attackSimPos, distance);
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

        private Limb GetAttackLimb(Vector2 attackWorldPos, Limb ignoredLimb = null)
        {
            AttackContext currentContext = Character.GetAttackContext();
            var target = wallTarget != null ? wallTarget.Structure : SelectedAiTarget?.Entity;
            Limb selectedLimb = null;
            float currentPriority = 0;
            foreach (Limb limb in Character.AnimController.Limbs)
            {
                if (limb == ignoredLimb) { continue; }
                if (limb.IsSevered || limb.IsStuck) { continue; }
                var attack = limb.attack;
                if (attack == null) { continue; }
                if (attack.CoolDownTimer > 0) { continue; }
                if (!attack.IsValidContext(currentContext)) { continue; }
                if (!attack.IsValidTarget(target)) { continue; }
                if (target is ISerializableEntity se && target is Character)
                {
                    // TODO: allow conditionals of which matching any is enough instead of having to fulfill all
                    if (attack.Conditionals.Any(c => !c.Matches(se))) { continue; }
                }
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
                        if (aggressiveBoarding && CanPassThroughHole(wall, i))
                        {
                            //aggressive boarders always target holes they can pass through
                            sectionIndex = i;
                            break;
                        }
                        else
                        {
                            //otherwise ignore and keep breaking other sections
                            continue;
                        }
                    }
                    if (wall.SectionDamage(i) > sectionDamage) sectionIndex = i;
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
            updateTargetsTimer = Math.Min(updateTargetsTimer, 0.1f);

            if (attackResult.Damage > 0.0f && attackWhenProvoked)
            {
                if (!(attacker is AICharacter) || (((AICharacter)attacker).AIController is HumanAIController))
                {
                    targetingPriorities["human"] = new TargetingPriority("human", AIState.Attack, 100.0f);
                    targetingPriorities["room"] = new TargetingPriority("room", AIState.Attack, 100.0f);
                }
            }
            
            LatchOntoAI?.DeattachFromBody();
            Character.AnimController.ReleaseStuckLimbs();

            if (attacker == null || attacker.AiTarget == null) return;
            AITargetMemory targetMemory = GetTargetMemory(attacker.AiTarget);
            targetMemory.Priority += GetRelativeDamage(attackResult.Damage, Character.Vitality) * aggressionhurt;

            // Reduce the cooldown so that the character can react
            // Only allow to react once. Otherwise would attack the target with only a fraction of cooldown
            if (SelectedAiTarget != attacker.AiTarget)
            {
                foreach (var limb in Character.AnimController.Limbs)
                {
                    if (limb.attack != null)
                    {
                        limb.attack.CoolDownTimer *= 0.1f;
                    }
                }
            }
        }

        // 10 dmg, 100 health -> 0.1
        private float GetRelativeDamage(float dmg, float vitality) => dmg / Math.Max(vitality, 1.0f);

        private void UpdateLimbAttack(float deltaTime, Limb limb, Vector2 attackSimPos, float distance = -1)
        {
            if (SelectedAiTarget == null) { return; }
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
                float prevHealth = damageTarget.Health;
                if (limb.UpdateAttack(deltaTime, attackSimPos, damageTarget, out AttackResult attackResult, distance))
                {
                    if (damageTarget.Health > 0)
                    {
                        // Managed to hit a living/non-destroyed target. Increase the priority more if the target is low in health -> dies easily/soon
                        selectedTargetMemory.Priority += GetRelativeDamage(attackResult.Damage, damageTarget.Health) * aggressiongreed;
                    }
                    else
                    {
                        selectedTargetMemory.Priority = 0;
                    }
                }
            }
        }

        private void UpdateFallBack(Vector2 attackWorldPos, float deltaTime)
        {
            Vector2 attackVector = attackWorldPos - WorldPosition;
            float dist = attackVector.Length();
            float desiredDist = colliderSize * 2.0f;
            if (dist < desiredDist)
            {
                Vector2 attackDir = Vector2.Normalize(-attackVector);
                if (!MathUtils.IsValid(attackDir)) attackDir = Vector2.UnitY;
                steeringManager.SteeringManual(deltaTime, attackDir * (1.0f - (dist / 500.0f)));
            }
            steeringManager.SteeringAvoid(deltaTime, colliderSize * 3.0f);
        }

        #endregion

        #region Eat

        private void UpdateEating(float deltaTime)
        {
            if (SelectedAiTarget == null)   //SelectedAiTarget.Entity is Character c && !c.IsDead
            {
                State = AIState.Idle;
                return;
            }
            Character targetChar = SelectedAiTarget.Entity as Character;

            Limb mouthLimb = Array.Find(Character.AnimController.Limbs, l => l != null && l.MouthPos.HasValue);
            if (mouthLimb == null) mouthLimb = Character.AnimController.GetLimb(LimbType.Head);
            if (mouthLimb == null)
            {
                DebugConsole.ThrowError("Character \"" + Character.SpeciesName + "\" failed to eat a target (a head or a limb with a mouthpos required)");
                State = AIState.Idle;
                return;
            }

            Vector2 mouthPos = Character.AnimController.GetMouthPosition().Value;
            //Vector2 attackSimPosition = Character.Submarine == null ? ConvertUnits.ToSimUnits(SelectedAiTarget.WorldPosition) : SelectedAiTarget.SimPosition;
            Vector2 attackSimPosition = Character.GetRelativeSimPosition(SelectedAiTarget.Entity);

            Vector2 limbDiff = attackSimPosition - mouthPos;
            float limbDist = limbDiff.Length();
            if (limbDist < 2.0f)
            {
                Character.SelectCharacter(SelectedAiTarget.Entity as Character);
                steeringManager.SteeringManual(deltaTime, Vector2.Normalize(limbDiff));
                Character.AnimController.Collider.ApplyForce(limbDiff * mouthLimb.Mass * 50.0f, mouthPos);
            }
            else
            {
                steeringManager.SteeringSeek(attackSimPosition - (mouthPos - SimPosition), 2);
            }
        }

        #endregion

        #region Targeting
        private bool IsLatchedOnSub => LatchOntoAI != null && LatchOntoAI.IsAttachedToSub;

        //goes through all the AItargets, evaluates how preferable it is to attack the target,
        //whether the Character can see/hear the target and chooses the most preferable target within
        //sight/hearing range
        public AITarget UpdateTargets(Character character, out TargetingPriority priority)
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
                    priority = null;
                    return SelectedAiTarget;
                }
            }
            AITarget newTarget = null;
            priority = null;
            selectedTargetMemory = null;
            targetValue = 0.0f;

            foreach (AITarget target in AITarget.List)
            {
                if (!target.Enabled) continue;
                if (Level.Loaded != null && target.WorldPosition.Y > Level.Loaded.Size.Y)
                {
                    continue;
                }
                if (target.Type == AITarget.TargetType.HumanOnly) { continue; }
                if (!TargetOutposts)
                {
                    if (target.Entity.Submarine != null && target.Entity.Submarine.IsOutpost) { continue; }
                }
                Character targetCharacter = target.Entity as Character;
                //ignore the aitarget if it is the Character itself
                if (targetCharacter == character) continue;

                float valueModifier = 1;
                string targetingTag = null;
                if (targetCharacter != null)
                {
                    if (targetCharacter.IsDead)
                    {
                        targetingTag = "dead";
                        if (targetCharacter.Submarine != Character.Submarine)
                        {
                            // In a different sub or the target is outside when we are inside or vice versa -> Ignore the target
                            continue;
                        }
                        else if (targetCharacter.CurrentHull != Character.CurrentHull)
                        {
                            // In the same sub, halve the priority, if not in the same hull.
                            valueModifier = 0.5f;
                        }
                    }
                    else if (targetCharacter.AIController is EnemyAIController enemy)
                    {
                        if (enemy.combatStrength > combatStrength)
                        {
                            targetingTag = "stronger";
                        }
                        else if (enemy.combatStrength < combatStrength)
                        {
                            targetingTag = "weaker";
                        }
                        if (State == AIState.Escape && targetingTag == "stronger")
                        {
                            // Frightened
                            valueModifier = 2;
                        }
                        else
                        {
                            if (targetCharacter.Submarine != Character.Submarine)
                            {
                                // In a different sub or the target is outside when we are inside or vice versa -> Ignore the target
                                continue;
                            }
                            else if (targetCharacter.CurrentHull != Character.CurrentHull)
                            {
                                // In the same sub, halve the priority, if not in the same hull.
                                valueModifier = 0.5f;
                            }
                        }
                    }
                    else if (targetCharacter.Submarine != null && Character.Submarine == null)
                    {
                        //target inside, AI outside -> we'll be attacking a wall between the characters so use the priority for attacking rooms
                        targetingTag = "room";
                    }
                    else if (targetingPriorities.ContainsKey(targetCharacter.SpeciesName.ToLowerInvariant()))
                    {
                        targetingTag = targetCharacter.SpeciesName.ToLowerInvariant();
                    }
                }
                else if (target.Entity != null)
                {
                    // Ignore the target if it's a room and the character is already inside a sub
                    if (character.CurrentHull != null && target.Entity is Hull) { continue; }
                    
                    Door door = null;
                    if (target.Entity is Item item)
                    {
                        //item inside and we're outside -> attack the hull
                        if (item.CurrentHull != null && character.CurrentHull == null)
                        {
                            targetingTag = "room";
                        }

                        door = item.GetComponent<Door>();
                        foreach (TargetingPriority prio in targetingPriorities.Values)
                        {
                            if (item.HasTag(prio.TargetTag))
                            {
                                targetingTag = prio.TargetTag;
                                break;
                            }
                        }

                        // Ignore the target if it's a decoy and the character is already inside a sub
                        if (character.CurrentHull != null && targetingTag == "decoy")
                        {
                            continue;
                        }
                    }
                    else if (target.Entity is Structure s)
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
                            // Ignore walls when inside.
                            continue;
                        }
                        valueModifier = 1;
                        float wallMaxHealth = 400;  // Anything more than this is ignored -> 200 = 1
                        // Prefer weaker targets.
                        valueModifier *= MathHelper.Lerp(1.5f, 0.5f, MathUtils.InverseLerp(0, 1, s.Health / wallMaxHealth));
                        if (aggressiveBoarding)
                        {
                            var hulls = s.Submarine.GetHulls(false);
                            for (int i = 0; i < s.Sections.Length; i++)
                            {
                                var section = s.Sections[i];
                                if (section.gap != null)
                                {
                                    if (CanPassThroughHole(s, i))
                                    {
                                        bool leadsInside = !section.gap.IsRoomToRoom && section.gap.FlowTargetHull != null && hulls.Any(h => h.Rect.Intersects(section.rect));
                                        valueModifier *= leadsInside ? 5 : 0;
                                    }
                                    else
                                    {
                                        // up to 100% priority increase for every gap in the wall
                                        valueModifier *= 1 + section.gap.Open;
                                    }
                                }
                            }
                        }
                        else
                        {
                            // Ignore disabled walls
                            bool isDisabled = true;
                            for (int i = 0; i < s.Sections.Length; i++)
                            {
                                if (!s.SectionBodyDisabled(i))
                                {
                                    isDisabled = false;
                                    break;
                                }
                            }
                            if (isDisabled)
                            {
                                valueModifier = 0;
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
                        bool isOutdoor = door.LinkedGap?.FlowTargetHull != null && !door.LinkedGap.IsRoomToRoom;
                        bool isOpen = door.IsOpen || door.Item.Condition <= 0.0f;
                        //increase priority if the character is outside and an aggressive boarder, and the door is from outside to inside
                        if (aggressiveBoarding)
                        {
                            if (character.CurrentHull == null)
                            {
                                valueModifier = isOutdoor ? 1 : 0;
                                valueModifier *= isOpen ? 5 : 1;
                            }
                            else
                            {
                                valueModifier = isOutdoor ? 0 : 1;
                                valueModifier *= isOpen ? 0 : 1;
                            }
                        }
                        else if (isOpen) //ignore broken and open doors
                        {
                            continue;
                        }
                    }
                    else if (target.Entity is IDamageable targetDamageable && targetDamageable.Health <= 0.0f)
                    {
                         continue;
                    }
                }

                if (targetingTag == null) continue;
                if (!targetingPriorities.ContainsKey(targetingTag)) continue;

                valueModifier *= targetingPriorities[targetingTag].Priority;

                if (valueModifier == 0.0f) continue;

                Vector2 toTarget = target.WorldPosition - character.WorldPosition;
                float dist = toTarget.Length();

                //if the target has been within range earlier, the character will notice it more easily
                //(i.e. remember where the target was)
                if (targetMemories.ContainsKey(target)) dist *= 0.5f;

                //ignore target if it's too far to see or hear
                if (dist > target.SightRange * sight && dist > target.SoundRange * hearing) continue;
                if (!target.IsWithinSector(WorldPosition)) continue;

                //if the target is very close, the distance doesn't make much difference 
                // -> just ignore the distance and attack whatever has the highest priority
                dist = Math.Max(dist, 100.0f);

                AITargetMemory targetMemory = GetTargetMemory(target);
                if (Character.CurrentHull != null && Math.Abs(toTarget.Y) > Character.CurrentHull.Size.Y)
                {
                    // Inside the sub, treat objects that are up or down, as they were farther away.
                    dist *= 3;
                }
                valueModifier *= targetMemory.Priority / (float)Math.Sqrt(dist);

                if (valueModifier > targetValue)
                {
                    newTarget = target;
                    selectedTargetMemory = targetMemory;
                    priority = targetingPriorities[targetingTag];
                    targetValue = valueModifier;
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
                memory = new AITargetMemory(10);
                targetMemories.Add(target, memory);
            }
            return memory;
        }

        private List<AITarget> removals = new List<AITarget>();
        private void UpdateTargetMemories(float deltaTime)
        {
            removals.Clear();
            foreach (var memory in targetMemories)
            {
                // Slowly decrease all memories
                memory.Value.Priority -= memoryFadeTime * deltaTime;
                // Remove targets that have no priority or have been removed
                if (memory.Value.Priority <= 1 || !AITarget.List.Contains(memory.Key))
                {
                    removals.Add(memory.Key);
                }
            }
            removals.ForEach(r => targetMemories.Remove(r));
        }

        #endregion

        protected override void OnStateChanged(AIState from, AIState to)
        {
            LatchOntoAI?.DeattachFromBody();
            Character.AnimController.ReleaseStuckLimbs();
            escapePoint = Vector2.Zero;
            wallTarget = null;
            AttackingLimb = null;
        }

        private void SteerInsideLevel(float deltaTime)
        {
            if (Level.Loaded == null) { return; } 
            
            Vector2 levelSimSize = new Vector2(
                ConvertUnits.ToSimUnits(Level.Loaded.Size.X),
                ConvertUnits.ToSimUnits(Level.Loaded.Size.Y));

            float margin = 10.0f;

            if (SimPosition.Y < 0.0f)
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
            return (int)Math.Ceiling(ConvertUnits.ToDisplayUnits(colliderSize) / Structure.WallSectionSize);
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
    }

    //the "memory" of the Character 
    //keeps track of how preferable it is to attack a specific target
    //(if the Character can't inflict much damage the target, the priority decreases
    //and if the target attacks the Character, the priority increases)
    class AITargetMemory
    {
        private float priority;
        
        public float Priority
        {
            get { return priority; }
            set { priority = MathHelper.Clamp(value, 1.0f, 100.0f); }
        }

        public AITargetMemory(float priority)
        {
            this.priority = priority;
        }
    }
}
