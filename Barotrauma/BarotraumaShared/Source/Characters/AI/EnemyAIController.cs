using Barotrauma.Items.Components;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class EnemyAIController : AIController
    {
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

        private const float UpdateTargetsInterval = 0.5f;

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
                
        //a "cooldown time" after an attack during which the Character doesn't try to attack again
        private float attackCoolDown;
        private float coolDownTimer;

        private Pair<Structure, int> selectedWallSection;

        private bool aggressiveBoarding;

        private LatchOntoAI latchOntoAI;
        
        //a point in a wall which the Character is currently targeting
        private Vector2 wallAttackPos;
        //the entity (a wall) which the Character is targeting
        private IDamageable targetEntity;

        //the limb selected for the current attack
        private Limb attackingLimb;

        //flee when the health is below this value
        private float fleeHealthThreshold;
        
        private AITarget selectedAiTarget;
        private AITargetMemory selectedTargetMemory;
        private float targetValue;

        private float eatTimer;
        
        private Dictionary<AITarget, AITargetMemory> targetMemories;

        //the eyesight of the NPC (0.0 = blind, 1.0 = sees every target within sightRange)
        private float sight;
        //how far the NPC can hear targets from (0.0 = deaf, 1.0 = hears every target within soundRange)
        private float hearing;

        private float colliderSize;

        public AITarget SelectedAiTarget
        {
            get { return selectedAiTarget; }
        }

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
                        
        public EnemyAIController(Character c, string file) : base(c)
        {
            targetMemories = new Dictionary<AITarget, AITargetMemory>();

            XDocument doc = XMLExtensions.TryLoadXml(file);
            if (doc == null || doc.Root == null) return;

            XElement aiElement = doc.Root.Element("ai");
            if (aiElement == null) return;
            
            combatStrength = aiElement.GetAttributeFloat("combatstrength", 1.0f);
            attackCoolDown  = aiElement.GetAttributeFloat("attackcooldown", 5.0f);
            attackWhenProvoked = aiElement.GetAttributeBool("attackwhenprovoked", false);
            aggressiveBoarding = aiElement.GetAttributeBool("aggressiveboarding", false);

            sight           = aiElement.GetAttributeFloat("sight", 0.0f);
            hearing         = aiElement.GetAttributeFloat("hearing", 0.0f);

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

            outsideSteering = new SteeringManager(this);

            bool canBreakDoors = false;
            if (GetTargetingPriority("room")?.Priority > 0.0f)
            {
                foreach (Limb limb in Character.AnimController.Limbs)
                {
                    if (limb.attack != null && limb.attack.StructureDamage > 0.0f)
                    {
                        canBreakDoors = true;
                        break;
                    }
                }
            }

            insideSteering = new IndoorsSteeringManager(this, false, canBreakDoors);
            steeringManager = outsideSteering;
            State = AIState.None;

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
            TargetingPriority priority = null;
            if (targetingPriorities.TryGetValue(targetTag, out priority))
            {
                return priority;
            }

            return null;
        }

        public override void SelectTarget(AITarget target)
        {
            selectedAiTarget = target;
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

                if (selectedAiTarget == null)
                {
                    State = AIState.None;
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

            if (selectedAiTarget != null && (selectedAiTarget.Entity == null || selectedAiTarget.Entity.Removed))
            {
                State = AIState.None;
                return;
            }

            if (Character.Submarine == null)
            {
                steeringManager = outsideSteering;
            }
            else
            {
                steeringManager = insideSteering;
            }

            bool run = false;
            switch (State)
            {
                case AIState.None:
                    UpdateNone(deltaTime);
                    break;
                case AIState.Attack:
                    run = coolDownTimer <= 0.0f;
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

            if (run)
            {
                steeringManager.Update(Character.AnimController.InWater ? 
                    Character.AnimController.SwimSpeedMultiplier : Character.AnimController.RunSpeedMultiplier);                
            }
            else
            {
                steeringManager.Update();
            }
        }

        #region Idle

        private void UpdateNone(float deltaTime)
        {
            coolDownTimer -= deltaTime;

            if (Character.Submarine == null && SimPosition.Y < ConvertUnits.ToSimUnits(SubmarineBody.DamageDepth * 0.5f))
            {
                //steer straight up if very deep
                steeringManager.SteeringManual(deltaTime, Vector2.UnitY);
                return;
            }

            if (wallAttackPos != Vector2.Zero) return;
            
            if (selectedAiTarget != null)
            {
                Vector2 targetSimPos = Character.Submarine == null ? ConvertUnits.ToSimUnits(selectedAiTarget.WorldPosition) : selectedAiTarget.SimPosition;

                steeringManager.SteeringAvoid(deltaTime, colliderSize * 3.0f, 1.0f);
                steeringManager.SteeringSeek(targetSimPos, 1.0f);
            }
            else
            {
                //wander around randomly
                if (Character.Submarine == null)
                {
                    steeringManager.SteeringAvoid(deltaTime, colliderSize * 5.0f, 1.0f);
                }
                steeringManager.SteeringWander(0.5f);
            }          
        }

        #endregion

        #region Escape

        private void UpdateEscape(float deltaTime)
        {
            if (selectedAiTarget == null)
            {
                State = AIState.None;
                return;
            }

            SteeringManager.SteeringManual(deltaTime, Vector2.Normalize(SimPosition - selectedAiTarget.SimPosition) * 2);
            SteeringManager.SteeringWander(1.0f);
            if (Character.CurrentHull == null)
            {
                SteeringManager.SteeringAvoid(deltaTime, colliderSize * 3.0f, 5f);
            }
        }

        #endregion

        #region Attack

        private void UpdateAttack(float deltaTime)
        {
            if (selectedAiTarget == null)
            {
                State = AIState.None;
                return;
            }

            selectedTargetMemory.Priority -= deltaTime;

            Vector2 attackSimPosition = Character.Submarine == null ? ConvertUnits.ToSimUnits(selectedAiTarget.WorldPosition) : selectedAiTarget.SimPosition;

            if (Character.Submarine != null && selectedAiTarget.Entity.Submarine != null && Character.Submarine != selectedAiTarget.Entity.Submarine)
            {
                attackSimPosition = ConvertUnits.ToSimUnits(selectedAiTarget.WorldPosition - Character.Submarine.Position);
            }

            if (wallAttackPos != Vector2.Zero && targetEntity != null)
            {
                attackSimPosition = wallAttackPos;
                if (Character.Submarine == null && selectedAiTarget.Entity?.Submarine != null) attackSimPosition += ConvertUnits.ToSimUnits(selectedAiTarget.Entity.Submarine.Position);
            }
            else if (selectedAiTarget.Entity is Character)
            {
                //target the closest limb if the target is a character
                float closestDist = Vector2.DistanceSquared(selectedAiTarget.SimPosition, SimPosition) * 10.0f;
                foreach (Limb limb in ((Character)selectedAiTarget.Entity).AnimController.Limbs)
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

            if (aggressiveBoarding)
            {
                //targeting a wall section that can be passed through -> steer manually through the hole
                if (selectedWallSection != null && CanPassThroughHole(selectedWallSection.First, selectedWallSection.Second))
                {
                    WallSection section = selectedWallSection.First.GetSection(selectedWallSection.Second);
                    Hull targetHull = section.gap?.FlowTargetHull;
                    if (targetHull != null && !section.gap.IsRoomToRoom)
                    {
                        Vector2 targetPos = selectedWallSection.First.SectionPosition(selectedWallSection.Second, true);
                        if (selectedWallSection.First.IsHorizontal)
                        {
                            targetPos.Y = targetHull.WorldRect.Y - targetHull.Rect.Height / 2;
                        }
                        else
                        {
                            targetPos.X = targetHull.WorldRect.Center.X;
                        }

                        latchOntoAI?.DeattachFromBody();
                        if (steeringManager is IndoorsSteeringManager)
                        {
                            steeringManager.SteeringManual(deltaTime, targetPos - Character.WorldPosition);
                        }
                        else
                        {
                            steeringManager.SteeringSeek(ConvertUnits.ToSimUnits(targetPos), 10.0f);
                        }
                        return;
                    }
                }
                else if (selectedAiTarget.Entity is Item)
                {
                    var door = ((Item)selectedAiTarget.Entity).GetComponent<Door>();
                    //steer through the door manually if it's open or broken
                    if (door?.LinkedGap?.FlowTargetHull != null && !door.LinkedGap.IsRoomToRoom && (door.IsOpen || door.Item.Condition <= 0.0f))
                    {
                        if (door.LinkedGap.IsHorizontal)
                        {
                            if (Character.WorldPosition.Y < door.Item.WorldRect.Y && Character.WorldPosition.Y > door.Item.WorldRect.Y - door.Item.Rect.Height)
                            {
                                steeringManager.SteeringManual(deltaTime, Vector2.UnitX * (door.LinkedGap.FlowTargetHull.WorldPosition.X - Character.WorldPosition.X));
                                return;
                            }
                        }
                        else
                        {
                            if (Character.WorldPosition.X < door.Item.WorldRect.X && Character.WorldPosition.X > door.Item.WorldRect.Right)
                            {
                                steeringManager.SteeringManual(deltaTime, Vector2.UnitY * (door.LinkedGap.FlowTargetHull.WorldPosition.Y - Character.WorldPosition.Y));
                                return;
                            }
                        }
                    }
                }
            }

            if (coolDownTimer > 0.0f)
            {
                UpdateCoolDown(attackSimPosition, deltaTime);
                return;
            }

            if (raycastTimer > 0.0)
            {
                raycastTimer -= deltaTime;
            }
            else
            {
                GetTargetEntity();
                raycastTimer = RaycastInterval;
            }
            
            Limb attackLimb = attackingLimb;
            //check if any of the limbs is close enough to attack the target
            if (attackingLimb == null)
            {
                foreach (Limb limb in Character.AnimController.Limbs)
                {
                    if (limb.attack == null) continue;
                    attackLimb = limb;

                    if (ConvertUnits.ToDisplayUnits(Vector2.Distance(limb.SimPosition, attackSimPosition)) > limb.attack.Range) continue;

                    attackingLimb = limb;
                    break;
                }

                if (Character.IsRemotePlayer)
                {
                    if (!Character.IsKeyDown(InputType.Attack)) return;
                }
            }

            if (attackLimb != null)
            {
                steeringManager.SteeringSeek(attackSimPosition - (attackLimb.SimPosition - SimPosition), 3);
                if (Character.CurrentHull == null)
                {
                    SteeringManager.SteeringAvoid(deltaTime, colliderSize * 1.5f, 1.0f);
                }

                if (steeringManager is IndoorsSteeringManager)
                {
                    var indoorsSteering = (IndoorsSteeringManager)steeringManager;
                    if (indoorsSteering.CurrentPath != null && !indoorsSteering.IsPathDirty)
                    {
                        if (indoorsSteering.CurrentPath.Unreachable)
                        {
                            //wander around randomly and decrease the priority faster if no path is found
                            if (selectedTargetMemory != null) selectedTargetMemory.Priority -= deltaTime * 10.0f;
                            steeringManager.SteeringWander();
                        }
                        else if (indoorsSteering.CurrentPath.Finished)
                        {                            
                            steeringManager.SteeringManual(deltaTime, attackSimPosition - attackLimb.SimPosition);
                        }
                        else if (indoorsSteering.CurrentPath.CurrentNode?.ConnectedDoor != null)
                        {
                            wallAttackPos = Vector2.Zero;
                            selectedAiTarget = indoorsSteering.CurrentPath.CurrentNode.ConnectedDoor.Item.AiTarget;
                        }
                        else if (indoorsSteering.CurrentPath.NextNode?.ConnectedDoor != null)
                        {
                            wallAttackPos = Vector2.Zero;
                            selectedAiTarget = indoorsSteering.CurrentPath.NextNode.ConnectedDoor.Item.AiTarget;
                        }
                    }
                }
                
                if (attackingLimb != null)
                {
                    UpdateLimbAttack(deltaTime, attackingLimb, attackSimPosition);
                }
            }
        }

        private void GetTargetEntity()
        {
            targetEntity = null;

            if (Character.AnimController.CurrentHull != null)
            {
                wallAttackPos = Vector2.Zero;
                return;
            }
            
            //check if there's a wall between the target and the Character   
            Vector2 rayStart = Character.SimPosition;
            Vector2 rayEnd = selectedAiTarget.SimPosition;

            if (selectedAiTarget.Entity.Submarine!=null && Character.Submarine==null)
            {
                rayStart -= ConvertUnits.ToSimUnits(selectedAiTarget.Entity.Submarine.Position);
            }

            Body closestBody = Submarine.CheckVisibility(rayStart, rayEnd);

            if (Submarine.LastPickedFraction == 1.0f || closestBody == null)
            {
                wallAttackPos = Vector2.Zero;
                return;
            }

            Structure wall = closestBody.UserData as Structure;
            if (wall == null)
            {
                wallAttackPos = Submarine.LastPickedPosition;
                latchOntoAI?.SetAttachTarget(closestBody, selectedAiTarget.Entity.Submarine, wallAttackPos);
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

                Vector2 sectionPos = wall.SectionPosition(sectionIndex);
                selectedWallSection = new Pair<Structure, int>(wall, sectionIndex);

                wallAttackPos = Submarine.LastPickedPosition;
                if (wall.IsHorizontal)
                    wallAttackPos.X = ConvertUnits.ToSimUnits(sectionPos.X);
                else
                    wallAttackPos.Y = ConvertUnits.ToSimUnits(sectionPos.Y);

                Vector2 attachPos = wallAttackPos;
                latchOntoAI?.SetAttachTarget(wall.Submarine.PhysicsBody.FarseerBody, wall.Submarine, attachPos);
            }
            
            targetEntity = closestBody.UserData as IDamageable;            
        }

        public override void OnAttacked(Character attacker, AttackResult attackResult)
        {
            updateTargetsTimer = Math.Min(updateTargetsTimer, 0.1f);
            coolDownTimer *= 0.1f;

            if (attackResult.Damage > 0.0f && attackWhenProvoked)
            {
                if (!(attacker is AICharacter) || (((AICharacter)attacker).AIController is HumanAIController))
                {
                    targetingPriorities["human"] = new TargetingPriority("human", AIState.Attack, 100.0f);
                    targetingPriorities["room"] = new TargetingPriority("room", AIState.Attack, 100.0f);
                }
            }
            
            latchOntoAI?.DeattachFromBody();

            if (attacker == null || attacker.AiTarget == null) return;
            AITargetMemory targetMemory = FindTargetMemory(attacker.AiTarget);
            targetMemory.Priority += attackResult.Damage / Math.Max(Character.Vitality, 1.0f);
        }

        private void UpdateLimbAttack(float deltaTime, Limb limb, Vector2 attackPosition)
        {
            var damageTarget = (wallAttackPos != Vector2.Zero && targetEntity != null) ? targetEntity : selectedAiTarget.Entity as IDamageable;

            if (damageTarget == null) return;

            float prevHealth = damageTarget.Health;
            limb.UpdateAttack(deltaTime, attackPosition, damageTarget);

            if (damageTarget.Health < prevHealth)
            {
                //managed to do damage to the target -> increase priority
                selectedTargetMemory.Priority += 10.0f;
            }

            if (limb.AttackTimer >= limb.attack.Duration)
            {
                wallAttackPos = Vector2.Zero;
                limb.AttackTimer = 0.0f;
                coolDownTimer = attackCoolDown;                
            }
        }

        private void UpdateCoolDown(Vector2 attackPosition, float deltaTime)
        {
            coolDownTimer -= deltaTime;
            attackingLimb = null;

            float dist = Vector2.Distance(attackPosition, Character.SimPosition);

            float desiredDist = colliderSize * 2.0f;
            if (dist < desiredDist)
            {
                //steeringManager.SteeringSeek(attackPosition, -0.8f);
                steeringManager.SteeringManual(deltaTime, Vector2.Normalize(Character.SimPosition - attackPosition) * (1.0f - (dist / desiredDist)));
            }

            steeringManager.SteeringAvoid(deltaTime, colliderSize * 3.0f, 1.0f);
        }

        #endregion

        #region Eat

        private void UpdateEating(float deltaTime)
        {
            if (selectedAiTarget == null)
            {
                State = AIState.None;
                return;
            }

            Limb mouthLimb = Array.Find(Character.AnimController.Limbs, l => l != null && l.MouthPos.HasValue);
            if (mouthLimb == null) mouthLimb = Character.AnimController.GetLimb(LimbType.Head);

            if (mouthLimb == null)
            {
                DebugConsole.ThrowError("Character \"" + Character.SpeciesName + "\" failed to eat a target (a head or a limb with a mouthpos required)");
                State = AIState.None;
                return;
            }

            Character targetCharacter = selectedAiTarget.Entity as Character;
            float eatSpeed = Character.Mass / targetCharacter.Mass * 0.1f;

            eatTimer += deltaTime * eatSpeed;

            Vector2 mouthPos = mouthLimb.SimPosition;
            if (mouthLimb.MouthPos.HasValue)
            {
                float cos = (float)Math.Cos(mouthLimb.Rotation);
                float sin = (float)Math.Sin(mouthLimb.Rotation);

                mouthPos += new Vector2(
                     mouthLimb.MouthPos.Value.X * cos - mouthLimb.MouthPos.Value.Y * sin,
                     mouthLimb.MouthPos.Value.X * sin + mouthLimb.MouthPos.Value.Y * cos);
            }

            Vector2 attackSimPosition = Character.Submarine == null ? ConvertUnits.ToSimUnits(selectedAiTarget.WorldPosition) : selectedAiTarget.SimPosition;
            
            Vector2 limbDiff = attackSimPosition - mouthPos;
            float limbDist = limbDiff.Length();
            if (limbDist < 1.0f)
            {
                //pull the target character to the position of the mouth
                //(+ make the force fluctuate to waggle the character a bit)
                targetCharacter.AnimController.MainLimb.MoveToPos(mouthPos, (float)(Math.Sin(eatTimer) + 10.0f));
                targetCharacter.AnimController.MainLimb.body.SmoothRotate(mouthLimb.Rotation);
                targetCharacter.AnimController.Collider.MoveToPos(mouthPos, (float)(Math.Sin(eatTimer) + 10.0f));

                //pull the character's mouth to the target character (again with a fluctuating force)
                float pullStrength = (float)(Math.Sin(eatTimer) * Math.Max(Math.Sin(eatTimer * 0.5f), 0.0f));
                steeringManager.SteeringManual(deltaTime, limbDiff * pullStrength);
                mouthLimb.body.ApplyForce(limbDiff * mouthLimb.Mass * 50.0f * pullStrength);

                if (eatTimer % 1.0f < 0.5f && (eatTimer - deltaTime * eatSpeed) % 1.0f > 0.5f)
                {
                    //apply damage to the target character to get some blood particles flying 
                    targetCharacter.AnimController.MainLimb.AddDamage(targetCharacter.SimPosition, Rand.Range(10.0f, 25.0f), 10.0f, 0.0f, false);

                    //keep severing joints until there is only one limb left
                    LimbJoint[] nonSeveredJoints = Array.FindAll(targetCharacter.AnimController.LimbJoints, l => !l.IsSevered && l.CanBeSevered);
                    if (nonSeveredJoints.Length == 0)
                    {
                        //only one limb left, the character is now full eaten
                        Entity.Spawner.AddToRemoveQueue(targetCharacter);
                        selectedAiTarget = null;
                        State = AIState.None;
                    }
                    else //sever a random joint
                    {
                        targetCharacter.AnimController.SeverLimbJoint(nonSeveredJoints[Rand.Int(nonSeveredJoints.Length)]);
                    }
                }
            }
            else if (limbDist < 2.0f)
            {
                steeringManager.SteeringManual(deltaTime, limbDiff);
                Character.AnimController.Collider.ApplyForce(limbDiff * mouthLimb.Mass * 50.0f, mouthPos);
            }
            else
            {
                steeringManager.SteeringSeek(attackSimPosition - (mouthPos - SimPosition), 3);
            }
        }
        
        #endregion

        #region Targeting

        //goes through all the AItargets, evaluates how preferable it is to attack the target,
        //whether the Character can see/hear the target and chooses the most preferable target within
        //sight/hearing range
        public void UpdateTargets(Character character, out TargetingPriority targetingPriority)
        {
            var prevAiTarget = selectedAiTarget;

            targetingPriority = null;
            selectedAiTarget = null;
            selectedTargetMemory = null;
            targetValue = 0.0f;

            UpdateTargetMemories();

            foreach (AITarget target in AITarget.List)
            {
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
                        /*if (GetTargetingPriority("dead") == 0.0f) continue;
                        valueModifier = eatDeadPriority;*/
                        targetingTag = "dead";
                    }
                    /*else if (targetCharacter.SpeciesName == "human")
                    {
                        if (attackHumans == 0.0f) continue;
                        valueModifier = attackHumans;                        
                    }*/
                    else if (targetingPriorities.ContainsKey(targetCharacter.SpeciesName.ToLowerInvariant()))
                    {
                        targetingTag = targetCharacter.SpeciesName.ToLowerInvariant();
                    }
                    else
                    {
                        EnemyAIController enemy = targetCharacter.AIController as EnemyAIController;
                        if (enemy != null)
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
                    if (target.Entity is Item)
                    {
                        Item item = (Item)target.Entity;

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

                AITargetMemory targetMemory = FindTargetMemory(target);
                valueModifier = valueModifier * targetMemory.Priority / dist;

                if (valueModifier > targetValue)
                {                  
                    Vector2 rayStart = character.AnimController.Limbs[0].SimPosition;
                    Vector2 rayEnd = target.SimPosition;

                    if (target.Entity.Submarine != null && character.Submarine==null)
                    {
                        rayStart -= ConvertUnits.ToSimUnits(target.Entity.Submarine.Position);
                    }

                    Body closestBody = Submarine.CheckVisibility(rayStart, rayEnd);
                    Structure closestStructure = (closestBody == null) ? null : closestBody.UserData as Structure;
                    
                    if (selectedAiTarget == null || valueModifier > targetValue)
                    {
                        selectedAiTarget = target;
                        selectedTargetMemory = targetMemory;
                        targetingPriority = targetingPriorities[targetingTag];

                        targetValue = valueModifier;
                    }
                }
            }

            if (selectedAiTarget != prevAiTarget)
            {
                wallAttackPos = Vector2.Zero;
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

            memory = new AITargetMemory(100.0f);
            targetMemories.Add(target, memory);

            return memory;
        }

        //go through all the targetmemories and delete ones that don't
        //have a corresponding AItarget or whose priority is 0.0f
        private void UpdateTargetMemories()
        {
            List<AITarget> toBeRemoved = new List<AITarget>();
            foreach(KeyValuePair<AITarget, AITargetMemory> memory in targetMemories)
            {
                memory.Value.Priority += 0.5f;
                if (Math.Abs(memory.Value.Priority) < 1.0f || !AITarget.List.Contains(memory.Key)) toBeRemoved.Add(memory.Key);
            }

            foreach (AITarget target in toBeRemoved)
            {
                targetMemories.Remove(target);
            }
        }

        #endregion

        protected override void OnStateChanged(AIState from, AIState to)
        {
            latchOntoAI?.DeattachFromBody();
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
