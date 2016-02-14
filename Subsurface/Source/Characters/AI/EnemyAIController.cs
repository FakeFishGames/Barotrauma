using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Xml.Linq;
using FarseerPhysics;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    
    class EnemyAIController : AIController
    {
        private const float UpdateTargetsInterval = 5.0f;

        private const float RaycastInterval = 1.0f;

        private bool attackWhenProvoked;
        
        //the preference to attack a specific type of target (-1.0 - 1.0)
        //0.0 = doesn't attack targets of the type
        //positive values = attacks targets of this type
        //negative values = escapes targets of this type        
        private float attackRooms, attackHumans, attackWeaker, attackStronger;

        private float updateTargetsTimer;

        private float raycastTimer;

        private Vector2 prevPosition;
        private float distanceAccumulator;

        //a timer for attacks such as biting that last for a specific amount of time
        //the duration is determined by the attackDuration of the attacking limb
        private float attackTimer;
        
        //a "cooldown time" after an attack during which the Character doesn't try to attack again
        private float attackCoolDown;
        private float coolDownTimer;
        
        //a point in a wall which the Character is currently targeting
        private Vector2 wallAttackPos;
        //the entity (a wall) which the Character is targeting
        private IDamageable targetEntity;

        //the limb selected for the current attack
        private Limb attackingLimb;
        
        private AITarget selectedAiTarget;
        private AITargetMemory selectedTargetMemory;
        private float targetValue;
        
        private Dictionary<AITarget, AITargetMemory> targetMemories;

        //the eyesight of the NPC (0.0 = blind, 1.0 = sees every target within sightRange)
        private float sight;
        //how far the NPC can hear targets from (0.0 = deaf, 1.0 = hears every target within soundRange)
        private float hearing;

        public AITarget SelectedAiTarget
        {
            get { return selectedAiTarget; }
        }
                        
        public EnemyAIController(Character c, string file) : base(c)
        {
            if (GameMain.Client != null && GameMain.Server == null) c.Enabled = false;

            targetMemories = new Dictionary<AITarget, AITargetMemory>();

            XDocument doc = ToolBox.TryLoadXml(file);
            if (doc == null) return;

            XElement aiElement = doc.Root.Element("ai");
            if (aiElement == null) return;

            attackRooms     = ToolBox.GetAttributeFloat(aiElement, "attackrooms", 0.0f) / 100.0f;
            attackHumans    = ToolBox.GetAttributeFloat(aiElement, "attackhumans", 0.0f) / 100.0f;
            attackWeaker    = ToolBox.GetAttributeFloat(aiElement, "attackweaker", 0.0f) / 100.0f;
            attackStronger  = ToolBox.GetAttributeFloat(aiElement, "attackstronger", 0.0f) / 100.0f;

            attackCoolDown  = ToolBox.GetAttributeFloat(aiElement, "attackcooldown", 5.0f);

            sight           = ToolBox.GetAttributeFloat(aiElement, "sight", 0.0f);
            hearing         = ToolBox.GetAttributeFloat(aiElement, "hearing", 0.0f);

            attackWhenProvoked = ToolBox.GetAttributeBool(aiElement, "attackwhenprovoked", false);

            steeringManager = new SteeringManager(this);

            state = AiState.None;
        }

        public override void SelectTarget(AITarget target)
        {
            selectedAiTarget = target;
            selectedTargetMemory = FindTargetMemory(target);

            targetValue = 100.0f;
        }
        
        public override void Update(float deltaTime)
        {
            UpdateDistanceAccumulator();

            Character.AnimController.IgnorePlatforms = (-Character.AnimController.TargetMovement.Y > Math.Abs(Character.AnimController.TargetMovement.X));

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
                    state = AiState.None;
                }
                else
                {
                    state = (targetValue > 0.0f) ? AiState.Attack : AiState.Escape;
                }
                //if (coolDownTimer >= 0.0f) return;
            }        

            switch (state)
            {
                case AiState.None:
                    UpdateNone(deltaTime);
                    break;
                case AiState.Attack:
                    UpdateAttack(deltaTime);
                    break;
            }

            steeringManager.Update();
        }

        private void UpdateNone(float deltaTime)
        {
            //wander around randomly
            if (Character.Submarine==null && SimPosition.Y < ConvertUnits.ToSimUnits(SubmarineBody.DamageDepth*0.5f))
            {
                steeringManager.SteeringManual(deltaTime, Vector2.UnitY);
            }
            else
            {
                steeringManager.SteeringWander(0.8f);            
                steeringManager.SteeringAvoid(deltaTime, 1.0f);
            }

            attackingLimb = null;
            attackTimer = 0.0f;

            coolDownTimer -= deltaTime;  
        }

        private void UpdateDistanceAccumulator()
        {
            Limb limb = Character.AnimController.Limbs[0];
            distanceAccumulator += (limb.SimPosition - prevPosition).Length();

            prevPosition = limb.body.SimPosition;
        }
        
        private void UpdateAttack(float deltaTime)
        {
            if (selectedAiTarget == null) 
            {
                state = AiState.None;
                return;
            }
            
            selectedTargetMemory.Priority -= deltaTime;
            
            Vector2 attackSimPosition = Character.Submarine==null ? ConvertUnits.ToSimUnits(selectedAiTarget.WorldPosition) : selectedAiTarget.SimPosition;
            if (wallAttackPos != Vector2.Zero && targetEntity != null)
            {
                attackSimPosition = wallAttackPos;

                if (selectedAiTarget.Entity != null && Character.Submarine==null && selectedAiTarget.Entity.Submarine != null) attackSimPosition += ConvertUnits.ToSimUnits(selectedAiTarget.Entity.Submarine.Position);
            }

            if (coolDownTimer>0.0f)
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

            steeringManager.SteeringAvoid(deltaTime, 1.0f);
            steeringManager.SteeringSeek(attackSimPosition);
            
            //check if any of the limbs is close enough to attack the target
            if (attackingLimb == null)
            {
                foreach (Limb limb in Character.AnimController.Limbs)
                {
                    if (limb.attack==null || limb.attack.Type == AttackType.None) continue;
                    if (ConvertUnits.ToDisplayUnits(Vector2.Distance(limb.SimPosition, attackSimPosition)) > limb.attack.Range) continue;
                                        
                    attackingLimb = limb;
                    break;   
                }
                return;
            }

            UpdateLimbAttack(deltaTime, attackingLimb, attackSimPosition);
                  
        }

        private void UpdateCoolDown(Vector2 attackPosition, float deltaTime)
        {
            coolDownTimer -= deltaTime;
            attackingLimb = null;
            
            if (selectedAiTarget.Entity is Hull ||
                Vector2.Distance(attackPosition, Character.AnimController.Limbs[0].SimPosition) < ConvertUnits.ToSimUnits(500.0f))
            {
                steeringManager.SteeringSeek(attackPosition, -0.8f);
                steeringManager.SteeringAvoid(deltaTime, 1.0f);
            }
            else
            {
                steeringManager.SteeringSeek(attackPosition, -0.5f);
                steeringManager.SteeringAvoid(deltaTime, 1.0f);
            }
        }

        private void GetTargetEntity()
        {
            targetEntity = null;
            
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
                if (selectedAiTarget.Entity.Submarine!=null) wallAttackPos -= ConvertUnits.ToSimUnits(selectedAiTarget.Entity.Submarine.Position);
            
            }
            else
            {
                int sectionIndex = wall.FindSectionIndex(ConvertUnits.ToDisplayUnits(Submarine.LastPickedPosition));

                float sectionDamage = wall.SectionDamage(sectionIndex);
                for (int i = sectionIndex - 2; i <= sectionIndex + 2; i++)
                {
                    if (wall.SectionHasHole(i))
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

            if (amount > 0.1f && attackWhenProvoked)
            {
                attackHumans = 100.0f;
                attackRooms = 100.0f;
            }

            if (attacker==null || attacker.AiTarget==null) return;
            AITargetMemory targetMemory = FindTargetMemory(attacker.AiTarget);
            targetMemory.Priority += amount;
        }

        private void UpdateLimbAttack(float deltaTime, Limb limb, Vector2 attackPosition)
        {
            IDamageable damageTarget = null;

            switch (limb.attack.Type)
            {
                case AttackType.PinchCW:
                case AttackType.PinchCCW:

                    float dir = (limb.attack.Type == AttackType.PinchCW) ? 1.0f : -1.0f;

                    damageTarget = (wallAttackPos != Vector2.Zero && targetEntity != null) ? targetEntity : selectedAiTarget.Entity as IDamageable;
                                        
                    attackTimer += deltaTime*0.05f;

                    if (damageTarget == null)
                    {
                        attackTimer = limb.attack.Duration;
                        break;
                    }

                    float dist = ConvertUnits.ToDisplayUnits(Vector2.Distance(limb.SimPosition, attackPosition));
                    if (dist < limb.attack.Range * 0.5f)
                    {
                        attackTimer += deltaTime;
                        limb.body.ApplyTorque(limb.Mass * 50.0f * Character.AnimController.Dir * dir);

                        if (attackTimer >= limb.attack.Duration)
                        {
                            limb.attack.DoDamage(Character, damageTarget, limb.WorldPosition, 1.0f, (limb.soundTimer <= 0.0f));

                            limb.soundTimer = Limb.SoundInterval;
                        }
                    }

                    Vector2 diff = attackPosition - limb.SimPosition;
                    if (diff.LengthSquared() > 0.00001f)
                    {
                        limb.body.ApplyLinearImpulse(limb.Mass * 10.0f *
                            Vector2.Normalize(attackPosition - limb.SimPosition));
                    }

                    steeringManager.SteeringSeek(attackPosition + (limb.SimPosition-SimPosition), 5.0f);

                    break;
                case AttackType.Hit:
                    damageTarget = (wallAttackPos != Vector2.Zero && targetEntity != null) ? targetEntity : selectedAiTarget.Entity as IDamageable;
                                        
                    if (damageTarget == null)
                    {
                        attackTimer = limb.attack.Duration;
                        break;
                    }

                    if (ConvertUnits.ToDisplayUnits(Vector2.Distance(limb.SimPosition, attackPosition)) < limb.attack.Range)
                    {
                        attackTimer += deltaTime;
                        limb.body.ApplyForce(limb.Mass * limb.attack.Force * Vector2.Normalize(attackPosition - limb.SimPosition));
                    }
                    
                    steeringManager.SteeringSeek(attackPosition + (limb.SimPosition-SimPosition), 5.0f);
                    break;
                default:
                    attackTimer = limb.attack.Duration;
                    break;
            }

            if (attackTimer >= limb.attack.Duration)
            {
                wallAttackPos = Vector2.Zero;
                attackTimer = 0.0f;
                if (Vector2.Distance(limb.SimPosition, attackPosition)<5.0) coolDownTimer = attackCoolDown;
                
            }
        }
        
        //goes through all the AItargets, evaluates how preferable it is to attack the target,
        //whether the Character can see/hear the target and chooses the most preferable target within
        //sight/hearing range
        public void UpdateTargets(Character character)
        {
            if (distanceAccumulator<5.0f && Rand.Range(1,3)==1)
            {
                selectedAiTarget = null;
                character.AnimController.TargetMovement = -character.AnimController.TargetMovement;
                state = AiState.None;
                return;
            }
            distanceAccumulator = 0.0f;

            selectedAiTarget = null;
            selectedTargetMemory = null;
            targetValue = 0.0f;

            UpdateTargetMemories();
            
            foreach (AITarget target in AITarget.List)
            {
                float valueModifier = 0.0f;
                float dist = 0.0f;
                
                IDamageable targetDamageable = target.Entity as IDamageable;
                if (targetDamageable!=null && targetDamageable.Health <= 0.0f) continue;

                Character targetCharacter = target.Entity as Character;

                //ignore the aitarget if it is the Character itself
                if (targetCharacter == character) continue;
                                
                if (targetCharacter!=null)
                {
                    if (attackHumans == 0.0f || targetCharacter.SpeciesName != "human") continue;
                    
                    valueModifier = attackHumans;                  
                }
                else if (target.Entity!=null && attackRooms!=0.0f)
                {
                    //skip the target if it's the room the Character is inside of
                    if (character.AnimController.CurrentHull != null && character.AnimController.CurrentHull == target.Entity as Hull) continue;

                    valueModifier = attackRooms;
                }

                dist = Vector2.Distance(
                    character.WorldPosition,
                    target.WorldPosition);

                AITargetMemory targetMemory = FindTargetMemory(target);

                valueModifier = valueModifier * targetMemory.Priority / dist;
                //dist -= targetMemory.Priority;

                if (Math.Abs(valueModifier) > Math.Abs(targetValue) && (dist < target.SightRange * sight || dist < target.SoundRange * hearing))
                {                  
                    Vector2 rayStart = character.AnimController.Limbs[0].SimPosition;
                    Vector2 rayEnd = target.SimPosition;

                    if (target.Entity.Submarine != null && character.Submarine==null)
                    {
                        rayStart -= ConvertUnits.ToSimUnits(target.Entity.Submarine.Position);
                    }

                    Body closestBody = Submarine.CheckVisibility(rayStart, rayEnd);
                    Structure closestStructure = (closestBody == null) ? null : closestBody.UserData as Structure;

                    if (targetDamageable != null)
                    {
                        valueModifier = valueModifier / targetDamageable.Health;                            
                    }
                    else if (closestStructure!=null)
                    {
                        valueModifier = valueModifier / (closestStructure as IDamageable).Health;
                    }
                    else
                    {
                        valueModifier = valueModifier / 1000.0f;
                    }

                    if (selectedAiTarget == null || Math.Abs(valueModifier) > Math.Abs(targetValue))
                    {
                        selectedAiTarget = target;
                        selectedTargetMemory = targetMemory;

                        targetValue = valueModifier;
                    }
                }
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
                if (memory.Value.Priority == 0.0f || !AITarget.List.Contains(memory.Key)) toBeRemoved.Add(memory.Key);
            }

            foreach (AITarget target in toBeRemoved)
            {
                targetMemories.Remove(target);
            }
        }

        public override void DebugDraw(SpriteBatch spriteBatch)
        {
            if (Character.IsDead) return;

            Vector2 pos = Character.WorldPosition;
            pos.Y = -pos.Y;

            if (selectedAiTarget!=null)
            {
                GUI.DrawLine(spriteBatch, pos, new Vector2(selectedAiTarget.WorldPosition.X, -selectedAiTarget.WorldPosition.Y), Color.Red);

                if (wallAttackPos!=Vector2.Zero)
                {
                    GUI.DrawRectangle(spriteBatch, ConvertUnits.ToDisplayUnits(new Vector2(wallAttackPos.X, -wallAttackPos.Y)) - new Vector2(10.0f, 10.0f), new Vector2(20.0f, 20.0f), Color.Red, false);
                }

                spriteBatch.DrawString(GUI.Font, targetValue.ToString(), pos - Vector2.UnitY*20.0f, Color.Red);

            }

            spriteBatch.DrawString(GUI.Font, targetValue.ToString(), pos - Vector2.UnitY * 80.0f, Color.Red);

            spriteBatch.DrawString(GUI.Font, "updatetargets: "+updateTargetsTimer, pos - Vector2.UnitY * 100.0f, Color.Red);
            spriteBatch.DrawString(GUI.Font, "cooldown: " + coolDownTimer, pos - Vector2.UnitY * 120.0f, Color.Red);
        }

        public override void FillNetworkData(NetBuffer message)
        {
            message.Write((byte)state);

            bool wallAttack = (wallAttackPos != Vector2.Zero && state == AiState.Attack);

            message.Write(wallAttack);

            //if (wallAttack)
            //{
            //    Vector2 relativeWallAttackPos = wallAttackPos - Submarine.Loaded.SimPosition;

            //    message.WriteRangedSingle(MathHelper.Clamp(relativeWallAttackPos.X, -50.0f, 50.0f), -50.0f, 50.0f, 10);
            //    message.WriteRangedSingle(MathHelper.Clamp(relativeWallAttackPos.Y, -50.0f, 50.0f), -50.0f, 50.0f, 10);
            //}

            //message.Write(Velocity.X);
            //message.Write(Velocity.Y);

            //message.Write(Character.AnimController.RefLimb.SimPosition.X);
            //message.Write(Character.AnimController.RefLimb.SimPosition.Y);


            message.Write(MathUtils.AngleToByte(steeringManager.WanderAngle));
            //message.WriteRangedSingle(MathHelper.Clamp(updateTargetsTimer,0.0f, UpdateTargetsInterval), 0.0f, UpdateTargetsInterval, 8);
            //message.WriteRangedSingle(MathHelper.Clamp(raycastTimer, 0.0f, RaycastInterval), 0.0f, RaycastInterval, 8);
            //message.WriteRangedSingle(MathHelper.Clamp(coolDownTimer, 0.0f, attackCoolDown * 2.0f), 0.0f, attackCoolDown * 2.0f, 8);

            message.Write(targetEntity==null ? (ushort)0 : (targetEntity as Entity).ID);
        }

        public override void ReadNetworkData(NetIncomingMessage message)
        {
            AiState newState = AiState.None;
            Vector2 newWallAttackPos = Vector2.Zero;
            float wanderAngle;

            ushort targetID;

            try
            {

                newState = (AiState)(message.ReadByte());

                //bool wallAttack = message.ReadBoolean();

                //if (wallAttack)
                //{
                //    newWallAttackPos = new Vector2(
                //        message.ReadRangedSingle(-50.0f, 50.0f, 10),
                //        message.ReadRangedSingle(-50.0f, 50.0f, 10));

                //    newWallAttackPos += Submarine.Loaded.SimPosition;
                //}

                //newVelocity = new Vector2(message.ReadFloat(), message.ReadFloat());

                //targetPosition = new Vector2(message.ReadFloat(), message.ReadFloat());   

                wanderAngle = MathUtils.ByteToAngle(message.ReadByte());
                //updateTargetsTimer = message.ReadRangedSingle(0.0f, UpdateTargetsInterval, 8);
                //raycastTimer = message.ReadRangedSingle(0.0f, RaycastInterval, 8);
                //coolDownTimer = message.ReadRangedSingle(0.0f, attackCoolDown*2.0f, 8);

                targetID = message.ReadUInt16();
            }

            catch { return; }

            //wallAttackPos = newWallAttackPos;

            steeringManager.WanderAngle = wanderAngle;
            //this.updateTargetsTimer = updateTargetsTimer;
            //this.raycastTimer = raycastTimer;
            //this.coolDownTimer = coolDownTimer;

            if (targetID > 0) targetEntity = Entity.FindEntityByID(targetID) as IDamageable;            
            
        }
    }

    //the "memory" of the Character 
    //keeps track of how preferable it is to attack a specific target
    //(if the Character can't inflict much damage the target, the priority decreases
    //and if the target attacks the Character, the priority increases)
    class AITargetMemory
    {
        //private AITarget target;
        private float priority;

        //public AITarget Target
        //{
        //    get { return target; }
        //}

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
