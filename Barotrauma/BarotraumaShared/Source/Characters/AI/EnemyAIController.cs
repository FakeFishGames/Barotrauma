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
        private const float UpdateTargetsInterval = 0.5f;

        private const float RaycastInterval = 1.0f;

        private bool attackWhenProvoked;
        
        //the preference to attack a specific type of target (-1.0 - 1.0)
        //0.0 = doesn't attack targets of the type
        //positive values = attacks targets of this type
        //negative values = escapes targets of this type        
        private float attackRooms, attackHumans, attackWeaker, attackStronger, eatDeadPriority;

        //determines which characters are considered weaker/stronger
        private float combatStrength;

        private SteeringManager outsideSteering, insideSteering;

        private float updateTargetsTimer;

        private float raycastTimer;
                
        //a "cooldown time" after an attack during which the Character doesn't try to attack again
        private float attackCoolDown;
        private float coolDownTimer;

        private bool attachToWalls;
        
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

        public AITarget SelectedAiTarget
        {
            get { return selectedAiTarget; }
        }

        public float AttackHumans
        {
            get { return attackHumans; }
        }

        public float AttackRooms
        {
            get { return attackRooms; }
        }
                        
        public EnemyAIController(Character c, string file) : base(c)
        {
            targetMemories = new Dictionary<AITarget, AITargetMemory>();

            XDocument doc = ToolBox.TryLoadXml(file);
            if (doc == null || doc.Root == null) return;

            XElement aiElement = doc.Root.Element("ai");
            if (aiElement == null) return;

            attackRooms     = ToolBox.GetAttributeFloat(aiElement, 0.0f, "attackrooms", "attackpriorityrooms") / 100.0f;
            attackHumans    = ToolBox.GetAttributeFloat(aiElement, 0.0f, "attackhumans", "attackpriorityhumans") / 100.0f;
            attackWeaker    = ToolBox.GetAttributeFloat(aiElement, 0.0f, "attackweaker", "attackpriorityweaker") / 100.0f;
            attackStronger  = ToolBox.GetAttributeFloat(aiElement, 0.0f, "attackstronger", "attackprioritystronger") / 100.0f;
            eatDeadPriority = ToolBox.GetAttributeFloat(aiElement, "eatpriority", 0.0f) / 100.0f;

            combatStrength = ToolBox.GetAttributeFloat(aiElement, "combatstrength", 1.0f);

            attackCoolDown  = ToolBox.GetAttributeFloat(aiElement, "attackcooldown", 5.0f);

            sight           = ToolBox.GetAttributeFloat(aiElement, "sight", 0.0f);
            hearing         = ToolBox.GetAttributeFloat(aiElement, "hearing", 0.0f);

            attackWhenProvoked = ToolBox.GetAttributeBool(aiElement, "attackwhenprovoked", false);

            fleeHealthThreshold = ToolBox.GetAttributeFloat(aiElement, "fleehealththreshold", 0.0f);

            attachToWalls = ToolBox.GetAttributeBool(aiElement, "attachtowalls", false);

            outsideSteering = new SteeringManager(this);
            insideSteering = new IndoorsSteeringManager(this, false);

            steeringManager = outsideSteering;

            state = AIState.None;
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
                UpdateTargets(Character);
                updateTargetsTimer = UpdateTargetsInterval;

                if (selectedAiTarget == null)
                {
                    state = AIState.None;
                }
                else if ((selectedAiTarget.Entity is Character) && ((Character)selectedAiTarget.Entity).IsDead)
                {
                    if (state != AIState.Eat)
                    {
                        eatTimer = 0.0f;
                        state = AIState.Eat;
                    }
                }
                else
                {
                    state = (targetValue < 0.0f || Character.Health < fleeHealthThreshold) ? AIState.Escape : AIState.Attack;
                }
                //if (coolDownTimer >= 0.0f) return;
            }

            steeringManager = Character.Submarine == null ? outsideSteering : insideSteering;

            bool run = false;
            switch (state)
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
            attackingLimb = null;
            coolDownTimer -= deltaTime;

            if (Character.Submarine == null && SimPosition.Y < ConvertUnits.ToSimUnits(SubmarineBody.DamageDepth * 0.5f))
            {
                //steer straight up if very deep
                steeringManager.SteeringManual(deltaTime, Vector2.UnitY);
                return;
            }

            if (attachToWalls && Character.Submarine == null && Level.Loaded != null)
            {
                raycastTimer -= deltaTime;
                //check if there are any walls nearby the character could attach to
                if (raycastTimer < 1.0f)
                {
                    wallAttackPos = Vector2.Zero;

                    var cells = Level.Loaded.GetCells(WorldPosition, 1);
                    if (cells.Count > 0)
                    {
                        Body closestBody = Submarine.CheckVisibility(Character.SimPosition, ConvertUnits.ToSimUnits(cells[0].Center));
                        if (closestBody != null && closestBody.UserData is Voronoi2.VoronoiCell)
                        {
                            wallAttackPos = Submarine.LastPickedPosition;
                        }
                    }
                    raycastTimer = RaycastInterval;
                }                
            }
            else
            {
                wallAttackPos = Vector2.Zero;
            }

            if (wallAttackPos == Vector2.Zero)
            {
                //wander around randomly
                steeringManager.SteeringAvoid(deltaTime, 0.1f);
                steeringManager.SteeringWander(0.5f);
                return;
            }

            float dist = Vector2.Distance(SimPosition, wallAttackPos);
            if (dist < Math.Max(Math.Max(Character.AnimController.Collider.radius, Character.AnimController.Collider.width), Character.AnimController.Collider.height) * 1.2f)
            {
                //close enough to a wall -> attach
                Character.AnimController.Collider.MoveToPos(wallAttackPos, 1.0f);
                steeringManager.Reset();                    

            }
            else
            {
                //move closer to the wall
                steeringManager.SteeringAvoid(deltaTime, 0.1f);
                steeringManager.SteeringSeek(wallAttackPos);
            }
        }

        #endregion

        #region Escape

        private void UpdateEscape(float deltaTime)
        {
            SteeringManager.SteeringManual(deltaTime, Vector2.Normalize(SimPosition - selectedAiTarget.SimPosition) * 5);
            SteeringManager.SteeringWander(1.0f);
            SteeringManager.SteeringAvoid(deltaTime, 2f);
        }

        #endregion

        #region Attack

        private void UpdateAttack(float deltaTime)
        {
            if (selectedAiTarget == null || selectedAiTarget.Entity == null || selectedAiTarget.Entity.Removed)
            {
                state = AIState.None;
                return;
            }

            selectedTargetMemory.Priority -= deltaTime;

            Vector2 attackSimPosition = Character.Submarine == null ? ConvertUnits.ToSimUnits(selectedAiTarget.WorldPosition) : selectedAiTarget.SimPosition;
            if (wallAttackPos != Vector2.Zero && targetEntity != null)
            {
                attackSimPosition = wallAttackPos;

                if (selectedAiTarget.Entity != null && Character.Submarine == null && selectedAiTarget.Entity.Submarine != null) attackSimPosition += ConvertUnits.ToSimUnits(selectedAiTarget.Entity.Submarine.Position);
            }
            else if (selectedAiTarget.Entity is Character)
            {
                //target the closest limb if the target is a character
                float closestDist = Vector2.DistanceSquared(selectedAiTarget.SimPosition, SimPosition);
                foreach (Limb limb in ((Character)selectedAiTarget.Entity).AnimController.Limbs)
                {
                    if (limb == null) continue;
                    float dist = Vector2.DistanceSquared(limb.SimPosition, SimPosition);
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

            //steeringManager.SteeringAvoid(deltaTime, 1.0f);

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

                if (steeringManager is IndoorsSteeringManager)
                {
                    var indoorsSteering = (IndoorsSteeringManager)steeringManager;
                    if (indoorsSteering.CurrentPath != null)
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
                    }
                }

                if (attackingLimb != null) UpdateLimbAttack(deltaTime, attackingLimb, attackSimPosition);
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
                if (selectedAiTarget.Entity.Submarine!=null && Character.Submarine == null) wallAttackPos -= ConvertUnits.ToSimUnits(selectedAiTarget.Entity.Submarine.Position);
            
            }
            else
            {
                int sectionIndex = wall.FindSectionIndex(ConvertUnits.ToDisplayUnits(Submarine.LastPickedPosition));

                float sectionDamage = wall.SectionDamage(sectionIndex);
                for (int i = sectionIndex - 2; i <= sectionIndex + 2; i++)
                {
                    if (wall.SectionBodyDisabled(i))
                    {
                        sectionIndex = i;
                        break;
                    }
                    if (wall.SectionDamage(i) > sectionDamage) sectionIndex = i;
                }
                wallAttackPos = wall.SectionPosition(sectionIndex);
                //if (wall.Submarine != null) wallAttackPos += wall.Submarine.Position;
                wallAttackPos = ConvertUnits.ToSimUnits(wallAttackPos);
            }
            
            targetEntity = closestBody.UserData as IDamageable;            
        }

        public override void OnAttacked(IDamageable attacker, float amount)
        {
            updateTargetsTimer = Math.Min(updateTargetsTimer, 0.1f);
            coolDownTimer *= 0.1f;

            if (amount > 0.0f && attackWhenProvoked)
            {
                if (!(attacker is AICharacter) || (((AICharacter)attacker).AIController is HumanAIController))
                {
                    attackHumans = 100.0f;
                    attackRooms = 100.0f;
                }
            }

            if (attacker == null || attacker.AiTarget == null) return;
            AITargetMemory targetMemory = FindTargetMemory(attacker.AiTarget);
            targetMemory.Priority += amount;
        }

        private void UpdateLimbAttack(float deltaTime, Limb limb, Vector2 attackPosition)
        {
            var damageTarget = (wallAttackPos != Vector2.Zero && targetEntity != null) ? targetEntity : selectedAiTarget.Entity as IDamageable;

            limb.UpdateAttack(deltaTime, attackPosition, damageTarget);

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

            if (dist < ConvertUnits.ToSimUnits(500.0f))
            {
                steeringManager.SteeringSeek(attackPosition, -0.8f);
                steeringManager.SteeringManual(deltaTime, Vector2.Normalize(Character.SimPosition - attackPosition) * (1.0f - (dist / 500.0f)));
            }

            steeringManager.SteeringAvoid(deltaTime, 1.0f);
        }

        #endregion

        #region Eat

        private void UpdateEating(float deltaTime)
        {
            if (selectedAiTarget == null || selectedAiTarget.Entity == null || selectedAiTarget.Entity.Removed)
            {
                state = AIState.None;
                return;
            }

            Limb mouthLimb = Array.Find(Character.AnimController.Limbs, l => l != null && l.MouthPos.HasValue);
            if (mouthLimb == null) mouthLimb = Character.AnimController.GetLimb(LimbType.Head);

            if (mouthLimb == null)
            {
                DebugConsole.ThrowError("Character \"" + Character.SpeciesName + "\" failed to eat a target (a head or a limb with a mouthpos required)");
                state = AIState.None;
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
                    targetCharacter.AnimController.MainLimb.AddDamage(targetCharacter.SimPosition, DamageType.None, Rand.Range(10.0f, 25.0f), 10.0f, false);

                    //keep severing joints until there is only one limb left
                    LimbJoint[] nonSeveredJoints = Array.FindAll(targetCharacter.AnimController.LimbJoints, l => !l.IsSevered && l.CanBeSevered);
                    if (nonSeveredJoints.Length == 0)
                    {
                        //only one limb left, the character is now full eaten
                        Entity.Spawner.AddToRemoveQueue(targetCharacter);
                        selectedAiTarget = null;
                        state = AIState.None;
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
                steeringManager.SteeringSeek(attackSimPosition + (mouthPos - SimPosition), 3);
            }
        }
        
        #endregion

        #region Targeting

        //goes through all the AItargets, evaluates how preferable it is to attack the target,
        //whether the Character can see/hear the target and chooses the most preferable target within
        //sight/hearing range
        public void UpdateTargets(Character character)
        {
            var prevAiTarget = selectedAiTarget;

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

                float valueModifier = 0.0f;
                float dist = 0.0f;
                

                Character targetCharacter = target.Entity as Character;

                //ignore the aitarget if it is the Character itself
                if (targetCharacter == character) continue;

                if (targetCharacter != null)
                {
                    if (targetCharacter.IsDead)
                    {
                        if (eatDeadPriority == 0.0f) continue;
                        valueModifier = eatDeadPriority;
                    }
                    else if (targetCharacter.SpeciesName == "human")
                    {
                        if (attackHumans == 0.0f) continue;
                        valueModifier = attackHumans;
                    }
                    else
                    {
                        EnemyAIController enemy = targetCharacter.AIController as EnemyAIController;
                        if (enemy != null)
                        {
                            if (enemy.combatStrength > combatStrength)
                            {
                                valueModifier = attackStronger;
                            }
                            else if (enemy.combatStrength < combatStrength)
                            {
                                valueModifier = attackWeaker;
                            }
                            else
                            {
                                continue;
                            }
                        }
                    }                
                }
                else if (target.Entity!=null && attackRooms != 0.0f)
                {
                    IDamageable targetDamageable = target.Entity as IDamageable;
                    if (targetDamageable != null && targetDamageable.Health <= 0.0f) continue;

                    //skip the target if it's a room and the character is already inside a sub
                    if (character.AnimController.CurrentHull != null && target.Entity is Hull) continue;

                    valueModifier = attackRooms;
                }

                if (valueModifier == 0.0f) continue;

                dist = Vector2.Distance(character.WorldPosition, target.WorldPosition);

                //if the target has been within range earlier, the character will notice it more easily
                //(i.e. remember where the target was)
                if (targetMemories.ContainsKey(target)) dist *= 0.5f;

                //ignore target if it's too far to see or hear
                if (dist > target.SightRange * sight && dist > target.SoundRange * hearing) continue;

                AITargetMemory targetMemory = FindTargetMemory(target);
                valueModifier = valueModifier * targetMemory.Priority / dist;

                if (Math.Abs(valueModifier) > Math.Abs(targetValue))
                {                  
                    Vector2 rayStart = character.AnimController.Limbs[0].SimPosition;
                    Vector2 rayEnd = target.SimPosition;

                    if (target.Entity.Submarine != null && character.Submarine==null)
                    {
                        rayStart -= ConvertUnits.ToSimUnits(target.Entity.Submarine.Position);
                    }

                    Body closestBody = Submarine.CheckVisibility(rayStart, rayEnd);
                    Structure closestStructure = (closestBody == null) ? null : closestBody.UserData as Structure;
                    
                    if (selectedAiTarget == null || Math.Abs(valueModifier) > Math.Abs(targetValue))
                    {
                        selectedAiTarget = target;
                        selectedTargetMemory = targetMemory;

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
