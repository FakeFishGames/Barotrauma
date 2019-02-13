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
                
        private bool IsCoolDownRunning => attackingLimb != null && attackingLimb.attack.CoolDownTimer > 0;
        
        private bool aggressiveBoarding;

        private LatchOntoAI latchOntoAI;

        //a point in a wall which the Character is currently targeting
        private WallTarget wallTarget;

        //the limb selected for the current attack
        private Limb attackingLimb;

        //flee when the health is below this value
        private float fleeHealthThreshold;
        
        private AITargetMemory selectedTargetMemory;
        private float targetValue;

        private float eatTimer;
        
        private Dictionary<AITarget, AITargetMemory> targetMemories;

        //the eyesight of the NPC (0.0 = blind, 1.0 = sees every target within sightRange)
        private float sight;
        //how far the NPC can hear targets from (0.0 = deaf, 1.0 = hears every target within soundRange)
        private float hearing;

        private float colliderSize;

        private readonly float aggressiongreed;
        private readonly float aggressionhurt;

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

        public Limb AttackingLimb
        {
            get { return attackingLimb; }
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
                return latchOntoAI == null || !latchOntoAI.IsAttached;
            }
        }

        public override bool CanFlip
        {
            get
            {
                //can't flip when attached to something
                return latchOntoAI == null || !latchOntoAI.IsAttached;
            }
        }

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
                        latchOntoAI = new LatchOntoAI(subElement, this);
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
        
        public TargetingPriority GetTargetingPriority(string targetTag)
        {
            if (targetingPriorities.TryGetValue(targetTag, out TargetingPriority priority))
            {
                return priority;
            }
            return null;
        }

        public override void SelectTarget(AITarget target)
        {
            SelectedAiTarget = target;
            selectedTargetMemory = FindTargetMemory(target);

            targetValue = 100.0f;
        }
        
        public override void Update(float deltaTime)
        {
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

            if (Character.AnimController is HumanoidAnimController)
            {
                if (Math.Abs(Character.AnimController.movement.X) > 0.1f && !Character.AnimController.InWater)
                {
                    Character.AnimController.TargetDir = Character.AnimController.movement.X > 0.0f ? Direction.Right : Direction.Left;
                }
            }

            if (updateTargetsTimer > 0.0)
            {
                updateTargetsTimer -= deltaTime;
            }
            else
            {
                TargetingPriority targetingPriority = null;
                UpdateTargets(Character, out targetingPriority);
                updateTargetsTimer = UpdateTargetsInterval;

                if (SelectedAiTarget == null)
                {
                    State = AIState.Idle;
                }
                else if (Character.Health < fleeHealthThreshold)
                {
                    State = AIState.Escape;
                }
                else
                {
                    State = targetingPriority.State;
                }
            }

            latchOntoAI?.Update(this, deltaTime);

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

            // Just some debug code that makes the characters to follow the mouse cursor 
            //run = true;
            //Vector2 mousePos = ConvertUnits.ToSimUnits(Screen.Selected.Cam.ScreenToWorld(PlayerInput.MousePosition));
            //steeringManager.SteeringSeek(mousePos, Character.AnimController.GetCurrentSpeed(run));

            steeringManager.Update(Character.AnimController.GetCurrentSpeed(run));
        }

        #region Idle

        private void UpdateIdle(float deltaTime)
        {
            float speed = Character.AnimController.GetCurrentSpeed(false);
            if (Character.Submarine == null && SimPosition.Y < ConvertUnits.ToSimUnits(Character.CharacterHealth.CrushDepth * 0.75f))
            {
                //steer straight up if very deep
                steeringManager.SteeringManual(deltaTime, Vector2.UnitY * speed);
                return;
            }

            if (wallTarget != null) return;
            
            if (SelectedAiTarget != null)
            {
                Vector2 targetSimPos = Character.Submarine == null ? ConvertUnits.ToSimUnits(SelectedAiTarget.WorldPosition) : SelectedAiTarget.SimPosition;

                steeringManager.SteeringAvoid(deltaTime, colliderSize * 3.0f, speed);
                steeringManager.SteeringSeek(targetSimPos, speed);
            }
            else
            {
                //wander around randomly
                if (Character.Submarine == null)
                {
                    steeringManager.SteeringAvoid(deltaTime, colliderSize * 5.0f, speed);
                }
                steeringManager.SteeringWander(speed / 2);
            }          
        }

        #endregion

        #region Escape

        private void UpdateEscape(float deltaTime)
        {
            if (SelectedAiTarget == null || SelectedAiTarget.Entity == null || SelectedAiTarget.Entity.Removed)
            {
                State = AIState.Idle;
                return;
            }

            Vector2 escapeDir = Vector2.Normalize(SimPosition - SelectedAiTarget.SimPosition);
            if (!MathUtils.IsValid(escapeDir)) escapeDir = Vector2.UnitY;
            float speed = Character.AnimController.GetCurrentSpeed(useMaxSpeed: true);
            SteeringManager.SteeringManual(deltaTime, escapeDir * speed);
            SteeringManager.SteeringWander(speed);
            if (Character.CurrentHull == null)
            {
                SteeringManager.SteeringAvoid(deltaTime, colliderSize * 3.0f, speed);
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

            float speed = Character.AnimController.GetCurrentSpeed(true);

            selectedTargetMemory.Priority -= deltaTime * 0.1f;

            Vector2 attackSimPosition = Character.Submarine == null ? ConvertUnits.ToSimUnits(SelectedAiTarget.WorldPosition) : SelectedAiTarget.SimPosition;

            if (Character.Submarine != null && SelectedAiTarget.Entity.Submarine != null && Character.Submarine != SelectedAiTarget.Entity.Submarine)
            {
                attackSimPosition = ConvertUnits.ToSimUnits(SelectedAiTarget.WorldPosition - Character.Submarine.Position);
            }

            if (SelectedAiTarget.Entity is Item item)
            {
                // If the item is held by a character, attack the character instead.
                var pickable = item.components.Select(c => c as Pickable).FirstOrDefault();
                if (pickable != null)
                {
                    var target = pickable.Picker?.AiTarget;
                    if (target != null)
                    {
                        SelectedAiTarget = target;
                    }
                }
            }

            if (wallTarget != null)
            {
                attackSimPosition = ConvertUnits.ToSimUnits(wallTarget.Position);
                if (Character.Submarine == null && SelectedAiTarget.Entity?.Submarine != null) attackSimPosition += ConvertUnits.ToSimUnits(SelectedAiTarget.Entity.Submarine.Position);
            }
            else if (SelectedAiTarget.Entity is Character)
            {
                //target the closest limb if the target is a character
                float closestDist = Vector2.DistanceSquared(SelectedAiTarget.SimPosition, SimPosition) * 10.0f;
                foreach (Limb limb in ((Character)SelectedAiTarget.Entity).AnimController.Limbs)
                {
                    if (limb == null) continue;
                    float dist = Vector2.DistanceSquared(limb.SimPosition, SimPosition) / Math.Max(limb.AttackPriority, 0.1f);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        attackSimPosition = limb.SimPosition;
                    }
                }
            }

            if (Math.Abs(Character.AnimController.movement.X) > 0.1f && !Character.AnimController.InWater)
            {
                Character.AnimController.TargetDir = Character.SimPosition.X < attackSimPosition.X ? Direction.Right : Direction.Left;
            }

            if (raycastTimer > 0.0)
            {
                raycastTimer -= deltaTime;
            }
            else
            {
                UpdateWallTarget();
                raycastTimer = RaycastInterval;
            }

            if (aggressiveBoarding)
            {
                //targeting a wall section that can be passed through -> steer manually through the hole
                if (wallTarget != null && wallTarget.SectionIndex > -1 && CanPassThroughHole(wallTarget.Structure, wallTarget.SectionIndex))
                {
                    WallSection section = wallTarget.Structure.GetSection(wallTarget.SectionIndex);
                    Hull targetHull = section.gap?.FlowTargetHull;
                    if (targetHull != null && !section.gap.IsRoomToRoom)
                    {
                        Vector2 targetPos = wallTarget.Structure.SectionPosition(wallTarget.SectionIndex, true);
                        if (wallTarget.Structure.IsHorizontal)
                        {
                            targetPos.Y = targetHull.WorldRect.Y - targetHull.Rect.Height / 2;
                        }
                        else
                        {
                            targetPos.X = targetHull.WorldRect.Center.X;
                        }

                        latchOntoAI?.DeattachFromBody();
                        Character.AnimController.ReleaseStuckLimbs();
                        if (steeringManager is IndoorsSteeringManager)
                        {
                            steeringManager.SteeringManual(deltaTime, targetPos - Character.WorldPosition);
                        }
                        else
                        {
                            steeringManager.SteeringSeek(ConvertUnits.ToSimUnits(targetPos), speed);
                        }
                        return;
                    }
                }
                else if (SelectedAiTarget.Entity is Item)
                {
                    var door = ((Item)SelectedAiTarget.Entity).GetComponent<Door>();
                    //steer through the door manually if it's open or broken
                    if (door?.LinkedGap?.FlowTargetHull != null && !door.LinkedGap.IsRoomToRoom && (door.IsOpen || door.Item.Condition <= 0.0f))
                    {
                        if (door.LinkedGap.IsHorizontal)
                        {
                            if (Character.WorldPosition.Y < door.Item.WorldRect.Y && Character.WorldPosition.Y > door.Item.WorldRect.Y - door.Item.Rect.Height)
                            {
                                var velocity = Vector2.UnitX * (door.LinkedGap.FlowTargetHull.WorldPosition.X - Character.WorldPosition.X) * speed;
                                steeringManager.SteeringManual(deltaTime, velocity);
                                return;
                            }
                        }
                        else
                        {
                            if (Character.WorldPosition.X < door.Item.WorldRect.X && Character.WorldPosition.X > door.Item.WorldRect.Right)
                            {
                                var velocity = Vector2.UnitY * (door.LinkedGap.FlowTargetHull.WorldPosition.Y - Character.WorldPosition.Y) * speed;
                                steeringManager.SteeringManual(deltaTime, velocity);
                                return;
                            }
                        }
                    }
                }
            }

            bool canAttack = true;
            if (IsCoolDownRunning)
            {
                switch (attackingLimb.attack.AfterAttack)
                {
                    case AIBehaviorAfterAttack.Pursue:
                    case AIBehaviorAfterAttack.PursueIfCanAttack:
                        if (attackingLimb.attack.SecondaryCoolDown <= 0)
                        {
                            // No (valid) secondary cooldown defined.
                            if (attackingLimb.attack.AfterAttack == AIBehaviorAfterAttack.Pursue)
                            {
                                canAttack = false;
                            }
                            else
                            {
                                UpdateFallBack(attackSimPosition, deltaTime);
                                return;
                            }
                        }
                        else
                        {
                            if (attackingLimb.attack.SecondaryCoolDownTimer <= 0)
                            {
                                // Don't allow attacking when the attack target has changed.
                                if (SelectedAiTarget != _previousAiTarget)
                                {
                                    canAttack = false;
                                    attackingLimb = null;
                                    return;
                                }
                                else
                                {
                                    // If the secondary cooldown is defined and expired, check if we can switch the attack
                                    var previousLimb = attackingLimb;
                                    var newLimb = GetAttackLimb(attackSimPosition, previousLimb);
                                    if (newLimb != null)
                                    {
                                        attackingLimb = newLimb;
                                    }
                                    else
                                    {
                                        // No new limb was found.
                                        if (attackingLimb.attack.AfterAttack == AIBehaviorAfterAttack.Pursue)
                                        {
                                            canAttack = false;
                                        }
                                        else
                                        {
                                            UpdateFallBack(attackSimPosition, deltaTime);
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
                    case AIBehaviorAfterAttack.FallBack:
                    default:
                        UpdateFallBack(attackSimPosition, deltaTime);
                        return;

                }
            }

            if (attackingLimb == null)
            {
                attackingLimb = GetAttackLimb(attackSimPosition);
            }
            if (canAttack)
            {
                canAttack = attackingLimb != null && attackingLimb.attack.CoolDownTimer <= 0;
            }
            float distance = 0;
            if (canAttack)
            {
                // Check that we can reach the target
                distance = ConvertUnits.ToDisplayUnits(Vector2.Distance(attackingLimb.SimPosition, attackSimPosition));
                canAttack = distance < attackingLimb.attack.Range;
            }

            Limb steeringLimb = Character.AnimController.MainLimb;
            if (steeringLimb != null)
            {
                Vector2 steeringVector = attackSimPosition - steeringLimb.SimPosition;
                Vector2 targetingVector = Vector2.Normalize(steeringVector) * attackingLimb.attack.Range;
                // Offset the position a bit so that we don't overshoot the movement.
                Vector2 steerPos = attackSimPosition + targetingVector;
                steeringManager.SteeringSeek(steerPos, speed);
                if (Character.CurrentHull == null)
                {
                    SteeringManager.SteeringAvoid(deltaTime, colliderSize * 1.5f, speed);
                }

                if (steeringManager is IndoorsSteeringManager indoorsSteering)
                {
                    if (indoorsSteering.CurrentPath != null && !indoorsSteering.IsPathDirty)
                    {
                        if (indoorsSteering.CurrentPath.Unreachable)
                        {
                            //wander around randomly and decrease the priority faster if no path is found
                            if (selectedTargetMemory != null) selectedTargetMemory.Priority -= deltaTime * 10.0f;
                            steeringManager.SteeringWander(speed);
                        }
                        else if (indoorsSteering.CurrentPath.Finished)
                        {
                            steeringManager.SteeringManual(deltaTime, steeringVector);
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
            }

            if (canAttack)
            {
                UpdateLimbAttack(deltaTime, attackingLimb, attackSimPosition, distance);
            }
        }

        private Limb GetAttackLimb(Vector2 attackSimPosition, Limb ignoredLimb = null)
        {
            AttackContext currentContext = Character.GetAttackContext();
            var target = wallTarget != null ? wallTarget.Structure : SelectedAiTarget.Entity;
            var limbs = Character.AnimController.Limbs
                .Where(l =>
                    l != ignoredLimb &&
                    l.attack != null &&
                    !l.IsSevered &&
                    !l.IsStuck &&
                    l.attack.IsValidContext(currentContext) &&
                    l.attack.IsValidTarget(target) &&
                    l.attack.Conditionals.All(c => (target is ISerializableEntity se && c.Matches(se)) || !(target is ISerializableEntity) || !(target is Character)))
                .OrderByDescending(l => l.attack.Priority)
                .ThenBy(l => ConvertUnits.ToDisplayUnits(Vector2.Distance(l.SimPosition, attackSimPosition)));
            // TODO: priority should probably not override the distance -> use values instead of booleans
            return limbs.FirstOrDefault();
        }

        private void UpdateWallTarget()
        {
            wallTarget = null;

            if (Character.AnimController.CurrentHull != null)
            {            
                return;
            }
            
            //check if there's a wall between the target and the Character   
            Vector2 rayStart = Character.SimPosition;
            Vector2 rayEnd = SelectedAiTarget.SimPosition;

            if (SelectedAiTarget.Entity.Submarine != null && Character.Submarine == null)
            {
                rayStart -= ConvertUnits.ToSimUnits(SelectedAiTarget.Entity.Submarine.Position);
            }

            Body closestBody = Submarine.CheckVisibility(rayStart, rayEnd);
            if (Submarine.LastPickedFraction == 1.0f || closestBody == null)
            {
                return;
            }

            Structure wall = closestBody.UserData as Structure;
            if (wall?.Submarine == null)
            {
                return;
                /*if (selectedAiTarget.Entity.Submarine != null)
                {
                    wallTarget = new WallTarget(ConvertUnits.ToDisplayUnits(Submarine.LastPickedPosition), selectedAiTarget.Entity.Submarine);
                    latchOntoAI?.SetAttachTarget(closestBody, selectedAiTarget.Entity.Submarine, Submarine.LastPickedPosition);
                }*/
                //if (selectedAiTarget.Entity.Submarine != null && Character.Submarine == null) wallAttackPos += ConvertUnits.ToSimUnits(selectedAiTarget.Entity.Submarine.Position);
            }
            else
            {
                int sectionIndex = wall.FindSectionIndex(ConvertUnits.ToDisplayUnits(Submarine.LastPickedPosition));
                int passableHoleCount = GetMinimumPassableHoleCount();

                float sectionDamage = wall.SectionDamage(sectionIndex);
                for (int i = sectionIndex - 2; i <= sectionIndex + 2; i++)
                {
                    if (wall.SectionBodyDisabled(i))
                    {
                        if (aggressiveBoarding && CanPassThroughHole(wall, i)) //aggressive boarders always target holes they can pass through
                        {
                            sectionIndex = i;
                            break;
                        }
                        else //otherwise ignore and keep breaking other sections
                        {
                            continue;
                        }
                    }
                    if (wall.SectionDamage(i) > sectionDamage) sectionIndex = i;
                }
                
                Vector2 sectionPos = ConvertUnits.ToSimUnits(wall.SectionPosition(sectionIndex));
                Vector2 attachTargetNormal;
                if (wall.IsHorizontal)
                {
                    attachTargetNormal = new Vector2(0.0f, Math.Sign(Character.WorldPosition.Y - wall.WorldPosition.Y));
                    sectionPos.Y += ConvertUnits.ToSimUnits((wall.BodyHeight <= 0.0f ? wall.Rect.Height : wall.BodyHeight) / 2) * attachTargetNormal.Y;
                }
                else
                {
                    attachTargetNormal = new Vector2(Math.Sign(Character.WorldPosition.X - wall.WorldPosition.X), 0.0f);
                    sectionPos.X += ConvertUnits.ToSimUnits((wall.BodyWidth <= 0.0f ? wall.Rect.Width : wall.BodyWidth) / 2) * attachTargetNormal.X;
                }
                wallTarget = new WallTarget(ConvertUnits.ToDisplayUnits(sectionPos), wall, sectionIndex);
                latchOntoAI?.SetAttachTarget(wall.Submarine.PhysicsBody.FarseerBody, wall.Submarine, sectionPos, attachTargetNormal);
            }         
        }

        public override void OnAttacked(Character attacker, AttackResult attackResult)
        {
            updateTargetsTimer = Math.Min(updateTargetsTimer, 0.1f);
            
            // Reduce the cooldown so that the character can react
            foreach (var limb in Character.AnimController.Limbs)
            {
                if (limb.attack != null)
                {
                    limb.attack.CoolDownTimer *= 0.1f;
                    // secondary cooldown?
                }
            }

            if (attackResult.Damage > 0.0f && attackWhenProvoked)
            {
                if (!(attacker is AICharacter) || (((AICharacter)attacker).AIController is HumanAIController))
                {
                    targetingPriorities["human"] = new TargetingPriority("human", AIState.Attack, 100.0f);
                    targetingPriorities["room"] = new TargetingPriority("room", AIState.Attack, 100.0f);
                }
            }
            
            latchOntoAI?.DeattachFromBody();
            Character.AnimController.ReleaseStuckLimbs();

            if (attacker == null || attacker.AiTarget == null) return;
            AITargetMemory targetMemory = FindTargetMemory(attacker.AiTarget);
            targetMemory.Priority += GetRelativeDamage(attackResult.Damage, Character.Vitality) * aggressionhurt; ;
        }

        // 10 dmg, 100 health -> 0.1
        private float GetRelativeDamage(float dmg, float vitality) => dmg / Math.Max(vitality, 1.0f);

        private void UpdateLimbAttack(float deltaTime, Limb limb, Vector2 attackPosition, float distance = -1)
        {
            var damageTarget = wallTarget != null ? wallTarget.Structure : SelectedAiTarget.Entity as IDamageable;
            if (damageTarget == null) return;

            float prevHealth = damageTarget.Health;
            if (limb.UpdateAttack(deltaTime, attackPosition, damageTarget, out AttackResult attackResult, distance))
            {
                if (damageTarget.Health > 0)
                {
                    // Managed to hit a living/non-destroyed target. Increase the priority more if the target is low in health -> dies easily/soon
                    selectedTargetMemory.Priority += GetRelativeDamage(attackResult.Damage, damageTarget.Health) * aggressiongreed;
                }
            }

            if (!limb.attack.IsRunning)
            {
                wallTarget = null;
            }
        }

        private void UpdateFallBack(Vector2 attackPosition, float deltaTime)
        {
            float dist = Vector2.Distance(attackPosition, Character.SimPosition);
            float desiredDist = colliderSize * 2.0f;
            if (dist < desiredDist)
            {
                Vector2 attackDir = Vector2.Normalize(Character.SimPosition - attackPosition);
                if (!MathUtils.IsValid(attackDir)) attackDir = Vector2.UnitY;
                steeringManager.SteeringManual(deltaTime, attackDir * (1.0f - (dist / 500.0f)));
            }

            steeringManager.SteeringAvoid(deltaTime, colliderSize * 3.0f, Character.AnimController.GetCurrentSpeed(false));
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

            Limb mouthLimb = Array.Find(Character.AnimController.Limbs, l => l != null && l.MouthPos.HasValue);
            if (mouthLimb == null) mouthLimb = Character.AnimController.GetLimb(LimbType.Head);
            if (mouthLimb == null)
            {
                DebugConsole.ThrowError("Character \"" + Character.SpeciesName + "\" failed to eat a target (a head or a limb with a mouthpos required)");
                State = AIState.Idle;
                return;
            }

            Vector2 mouthPos = Character.AnimController.GetMouthPosition().Value;
            Vector2 attackSimPosition = Character.Submarine == null ? ConvertUnits.ToSimUnits(SelectedAiTarget.WorldPosition) : SelectedAiTarget.SimPosition;

            Vector2 limbDiff = attackSimPosition - mouthPos;
            float limbDist = limbDiff.Length();
            if (limbDist < 2.0f)
            {
                Character.SelectCharacter(SelectedAiTarget.Entity as Character);
                steeringManager.SteeringManual(deltaTime, limbDiff);
                Character.AnimController.Collider.ApplyForce(limbDiff * mouthLimb.Mass * 50.0f, mouthPos);
            }
            else
            {
                steeringManager.SteeringSeek(attackSimPosition - (mouthPos - SimPosition), Character.AnimController.GetCurrentSpeed(useMaxSpeed: true));
            }
        }
        
        #endregion

        #region Targeting

        //goes through all the AItargets, evaluates how preferable it is to attack the target,
        //whether the Character can see/hear the target and chooses the most preferable target within
        //sight/hearing range
        public void UpdateTargets(Character character, out TargetingPriority targetingPriority)
        {
            var prevAiTarget = SelectedAiTarget;

            targetingPriority = null;
            SelectedAiTarget = null;
            selectedTargetMemory = null;
            targetValue = 0.0f;

            UpdateTargetMemories();

            foreach (AITarget target in AITarget.List)
            {
                if (!target.Enabled) continue;
                if (Level.Loaded != null && target.WorldPosition.Y > Level.Loaded.Size.Y)
                {
                    continue;
                }

                float valueModifier = 1.0f;
                float dist = 0.0f;

                Character targetCharacter = target.Entity as Character;

                //ignore the aitarget if it is the Character itself
                if (targetCharacter == character) continue;

                string targetingTag = null;
                if (targetCharacter != null)
                {
                    if (targetCharacter.Submarine != null && Character.Submarine == null)
                    {
                        //target inside, AI outside -> we'll be attacking a wall between the characters so use the priority for attacking rooms
                        targetingTag = "room";
                    }
                    else if (targetCharacter.IsDead)
                    {
                        targetingTag = "dead";
                    }
                    else if (targetingPriorities.ContainsKey(targetCharacter.SpeciesName.ToLowerInvariant()))
                    {
                        targetingTag = targetCharacter.SpeciesName.ToLowerInvariant();
                    }
                    else
                    {
                        if (targetCharacter.AIController is EnemyAIController enemy)
                        {
                            if (enemy.combatStrength > combatStrength)
                            {
                                targetingTag = "stronger";
                            }
                            else if (enemy.combatStrength < combatStrength)
                            {
                                targetingTag = "weaker";
                            }
                        }
                    }
                }
                else if (target.Entity != null)
                {
                    //skip the target if it's a room and the character is already inside a sub
                    if (character.CurrentHull != null && target.Entity is Hull) continue;
                    
                    //multiply the priority of the target if it's a door from outside to inside and the AI is an aggressive boarder
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
                    }
                    else
                    {
                        targetingTag = "room";
                    }

                    if (door != null)
                    { 
                        //increase priority if the character is outside and an aggressive boarder, and the door is from outside to inside
                        if (character.CurrentHull == null && aggressiveBoarding && !door.LinkedGap.IsRoomToRoom)
                        {
                            valueModifier = door.IsOpen ? 5.0f : 3.0f;
                        }
                        else if (door.IsOpen || door.Item.Condition <= 0.0f) //ignore broken and open doors
                        {
                            continue;
                        }
                    }
                    else
                    {
                        IDamageable targetDamageable = target.Entity as IDamageable;
                        if (targetDamageable != null && targetDamageable.Health <= 0.0f) continue;
                    }
                }

                if (targetingTag == null) continue;
                if (!targetingPriorities.ContainsKey(targetingTag)) continue;

                valueModifier *= targetingPriorities[targetingTag].Priority;

                if (valueModifier == 0.0f) continue;

                dist = Vector2.Distance(character.WorldPosition, target.WorldPosition);

                //if the target has been within range earlier, the character will notice it more easily
                //(i.e. remember where the target was)
                if (targetMemories.ContainsKey(target)) dist *= 0.5f;

                //ignore target if it's too far to see or hear
                if (dist > target.SightRange * sight && dist > target.SoundRange * hearing) continue;
                if (!target.IsWithinSector(WorldPosition)) continue;

                //if the target is very close, the distance doesn't make much difference 
                // -> just ignore the distance and attack whatever has the highest priority
                dist = Math.Max(dist, 100.0f);

                AITargetMemory targetMemory = FindTargetMemory(target);
                valueModifier = valueModifier * targetMemory.Priority / (float)Math.Sqrt(dist);

                if (valueModifier > targetValue)
                {
                    SelectedAiTarget = target;
                    selectedTargetMemory = targetMemory;
                    targetingPriority = targetingPriorities[targetingTag];
                    targetValue = valueModifier;
                }
            }

            if (SelectedAiTarget != prevAiTarget)
            {
                wallTarget = null;
            }           
        }

        //find the targetMemory that corresponds to some AItarget or create if there isn't one yet
        private AITargetMemory FindTargetMemory(AITarget target)
        {
            AITargetMemory memory = null;
            if (targetMemories.TryGetValue(target, out memory))
            {
                return memory;
            }

            memory = new AITargetMemory(10.0f);
            targetMemories.Add(target, memory);

            return memory;
        }

        //go through all the targetmemories and delete ones that don't
        //have a corresponding AItarget or whose priority is 0.0f
        private void UpdateTargetMemories()
        {
            List<AITarget> toBeRemoved = null;
            foreach (KeyValuePair<AITarget, AITargetMemory> memory in targetMemories)
            {
                memory.Value.Priority += 0.1f;
                if (Math.Abs(memory.Value.Priority) < 1.0f || !AITarget.List.Contains(memory.Key))
                {
                    if (toBeRemoved == null) toBeRemoved = new List<AITarget>();
                    toBeRemoved.Add(memory.Key);
                }
            }

            if (toBeRemoved != null)
            {
                foreach (AITarget target in toBeRemoved)
                {
                    targetMemories.Remove(target);
                }
            }
        }

        #endregion

        protected override void OnStateChanged(AIState from, AIState to)
        {
            latchOntoAI?.DeattachFromBody();
            Character.AnimController.ReleaseStuckLimbs();
        }

        private int GetMinimumPassableHoleCount()
        {
            return (int)Math.Ceiling(ConvertUnits.ToDisplayUnits(colliderSize)  / Structure.WallSectionSize);
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
