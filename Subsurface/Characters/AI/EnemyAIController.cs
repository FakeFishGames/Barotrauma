using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Xml.Linq;
using FarseerPhysics;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using FarseerPhysics.Dynamics;

namespace Subsurface
{
    
    class EnemyAIController : AIController
    {        //the preference to attack a specific type of target (-1.0 - 1.0)
        //0.0 = doesn't attack targets of the type
        //positive values = attacks targets of this type
        //negative values = escapes targets of this type        
        private float attackRooms;
        private float attackHumans;
        private float attackWeaker;
        private float attackStronger;



        private float updateTargetsTimer;
        private const float UpdateTargetsInterval = 5.0f;

        private float raycastTimer;
        private const float RaycastInterval = 1.0f;

        private Vector2 prevPosition;
        private float distanceAccumulator;

        //a timer for attacks such as biting that last for a specific amount of time
        //the duration is determined by the attackDuration of the attacking limb
        private float attackTimer;
        
        //a "cooldown time" after an attack during which the character doesn't try to attack again
        private float attackCoolDown;
        private float coolDownTimer;
        
        //a point in a wall which the character is currently targeting
        private Vector2 wallAttackPos;
        //the entity (a wall) which the character is targeting
        private IDamageable targetEntity;

        //the limb selected for the current attack
        private Limb attackingLimb;
        
        private AITarget selectedTarget;
        private AITargetMemory selectedTargetMemory;
        private float targetValue;
        
        private Dictionary<AITarget, AITargetMemory> targetMemories;

        //the eyesight of the NPC (0.0 = blind, 1.0 = sees every target within sightRange)
        private float sight;
        //how far the NPC can hear targets from (0.0 = deaf, 1.0 = hears every target within soundRange)
        private float hearing;
                        
        public EnemyAIController(Character c, string file) : base(c)
        {
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

            state = AiState.None;
        }
        
        public override void Update(float deltaTime)
        {
            UpdateDistanceAccumulator();

            character.animController.IgnorePlatforms = (-character.animController.TargetMovement.Y > Math.Abs(character.animController.TargetMovement.X));

            if (updateTargetsTimer > 0.0)
            {
                updateTargetsTimer -= deltaTime;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("updatetargets");
                UpdateTargets(character);
                updateTargetsTimer = UpdateTargetsInterval;

                if (selectedTarget == null)
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
            //UpdateSteeringWander(deltaTime, 0.8f);
            steeringManager.SteeringWander(0.8f);            
            steeringManager.SteeringAvoid(deltaTime, 1.0f);

            attackingLimb = null;
            attackTimer = 0.0f;

            coolDownTimer -= deltaTime;  
        }

        private void UpdateDistanceAccumulator()
        {
            Limb limb = character.animController.limbs[0];
            distanceAccumulator += (limb.SimPosition - prevPosition).Length();

            prevPosition = limb.body.Position;
        }
        
        private void UpdateAttack(float deltaTime)
        {

            if (selectedTarget == null) 
            {
                state = AiState.None;
                return;
            }
            
            selectedTargetMemory.Priority -= deltaTime;
            
            Vector2 attackPosition = selectedTarget.Position;
            if (wallAttackPos != Vector2.Zero) attackPosition = wallAttackPos;

            if (coolDownTimer>0.0f)
            {
                UpdateCoolDown(attackPosition, deltaTime);
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

            steeringManager.SteeringSeek(attackPosition);
            
            //check if any of the limbs is close enough to attack the target
            if (attackingLimb == null)
            {
                foreach (Limb limb in character.animController.limbs)
                {
                    if (limb.attack==null || limb.attack.type == Attack.Type.None) continue;
                    if (Vector2.Distance(limb.SimPosition, attackPosition) > limb.attack.range) continue;
                                        
                    attackingLimb = limb;
                    break;   
                }
                return;
            }

            UpdateLimbAttack(deltaTime, attackingLimb, attackPosition);
                  
        }

        private void UpdateCoolDown(Vector2 attackPosition, float deltaTime)
        {
            coolDownTimer -= deltaTime;
            attackingLimb = null;

            //System.Diagnostics.Debug.WriteLine("cooldown");

            if (selectedTarget.entity is Hull ||
                Vector2.Distance(attackPosition, character.animController.limbs[0].SimPosition) < ConvertUnits.ToSimUnits(500.0f))
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
            //check if there's a wall between the target and the character   
            Vector2 rayStart = character.animController.limbs[0].SimPosition;
            Vector2 rayEnd = selectedTarget.Position;
            Body closestBody = Map.CheckVisibility(rayStart, rayEnd);

            if (Map.LastPickedFraction == 1.0f || closestBody == null)
            {
                wallAttackPos = Vector2.Zero;
                return;
            }
            
            Structure wall = closestBody.UserData as Structure;
            if (wall == null)
            {
                wallAttackPos = Map.LastPickedPosition;
            }
            else
            {
                int sectionIndex = wall.FindSectionIndex(ConvertUnits.ToDisplayUnits(Map.LastPickedPosition));

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
                wallAttackPos = ConvertUnits.ToSimUnits(wallAttackPos);
            }
            
            targetEntity = closestBody.UserData as IDamageable;            
        }

        private void UpdateLimbAttack(float deltaTime, Limb limb, Vector2 attackPosition)
        {
            IDamageable damageTarget = null;

            switch (limb.attack.type)
            {
                case Attack.Type.PinchCW:
                case Attack.Type.PinchCCW:

                    float dir = (limb.attack.type == Attack.Type.PinchCW) ? 1.0f : -1.0f;
                    float dist = Vector2.Distance(limb.SimPosition, attackPosition);

                    if (wallAttackPos != Vector2.Zero && targetEntity != null)
                    {
                        damageTarget = targetEntity as IDamageable;
                    }                     
                    else
                    {
                        damageTarget = selectedTarget.entity as IDamageable;
                    }
                    
                    attackTimer += deltaTime*0.05f;

                    if (damageTarget == null)
                    {
                        attackTimer = limb.attack.duration;
                        break;
                    }

                    if (dist < limb.attack.range * 0.5f)
                    {
                        attackTimer += deltaTime;
                        limb.body.ApplyTorque(limb.Mass * 50.0f * character.animController.Dir * dir);

                        limb.attack.DoDamage(damageTarget, limb.SimPosition, deltaTime, (limb.soundTimer <= 0.0f));

                        limb.soundTimer = Limb.SoundInterval;
                    }
                    else
                    {
                        //limb.body.ApplyTorque(limb.Mass * -20.0f * character.animController.Dir * dir);
                    }

                    limb.body.ApplyLinearImpulse(limb.Mass * 10.0f *
                        Vector2.Normalize(attackPosition - limb.SimPosition));

                    steeringManager.SteeringSeek(attackPosition + (limb.SimPosition-Position), 5.0f);

                    break;
                default:
                    attackTimer = limb.attack.duration;
                    break;
            }

            if (attackTimer >= limb.attack.duration)
            {
                attackTimer = 0.0f;
                if (Vector2.Distance(limb.SimPosition, attackPosition)<5.0) coolDownTimer = attackCoolDown;
                
            }
        }
        
        //goes through all the AItargets, evaluates how preferable it is to attack the target,
        //whether the character can see/hear the target and chooses the most preferable target within
        //sight/hearing range
        public void UpdateTargets(Character character)
        {
            if (distanceAccumulator<5.0f && Game1.random.Next(1,3)==1)
            {
                selectedTarget = null;
                character.animController.TargetMovement = -character.animController.TargetMovement;
                state = AiState.None;
                return;
            }
            distanceAccumulator = 0.0f;

            selectedTarget = null;
            selectedTargetMemory = null;
            targetValue = 0.0f;

            UpdateTargetMemories();
            
            foreach (AITarget target in AITarget.list)
            {
                float valueModifier = 0.0f;
                float dist = 0.0f;
                
                IDamageable targetDamageable = target.entity as IDamageable;
                if (targetDamageable!=null && targetDamageable.Health <= 0.0f) continue;

                Character targetCharacter = target.entity as Character;

                //ignore the aitarget if it is the character itself
                if (targetCharacter == character) continue;
                                
                if (targetCharacter!=null)
                {
                    if (attackHumans == 0.0f || targetCharacter.speciesName != "human") continue;
                    
                    valueModifier = attackHumans;                  
                }
                else if (target.entity!=null && attackRooms!=0.0f)
                {
                    //skip the target if it's the room the character is inside of
                    if (character.animController.CurrentHull != null && character.animController.CurrentHull == target.entity as Hull) continue;

                    valueModifier = attackRooms;
                }

                dist = Vector2.Distance(
                    character.animController.limbs[0].SimPosition,
                    target.Position);
                dist = ConvertUnits.ToDisplayUnits(dist);

                AITargetMemory targetMemory = FindTargetMemory(target);

                valueModifier = valueModifier * targetMemory.Priority / dist;
                //dist -= targetMemory.Priority;

                if (Math.Abs(valueModifier) > Math.Abs(targetValue) && (dist < target.SightRange * sight || dist < target.SoundRange * hearing))
                {                  
                    Vector2 rayStart = character.animController.limbs[0].SimPosition;
                    Vector2 rayEnd = target.Position;

                    Body closestBody = Map.CheckVisibility(rayStart, rayEnd);
                    Structure closestStructure = (closestBody == null) ? null : closestBody.UserData as Structure;
                    
                    //if (targetCharacter != null)
                    //{
                    //    //if target is a character that isn't visible, ignore
                    //    if (closestStructure != null) continue;

                    //    //prefer targets with low health
                    //    valueModifier = valueModifier / targetCharacter.Health;
                    //}
                    //else
                    //{
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

                    //}
                    


                    //float newTargetValue = valueModifier/dist;
                    if (selectedTarget == null || Math.Abs(valueModifier) > Math.Abs(targetValue))
                    {
                        selectedTarget = target;
                        selectedTargetMemory = targetMemory;

                        targetValue = valueModifier;
                        Debug.WriteLine(selectedTarget.entity+": "+targetValue);
                    }
                }
            }
          
            //selectedTarget = bestTarget;
            //selectedTargetMemory = targetMemory;
            //this.targetValue = bestTargetValue;  
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
                if (memory.Value.Priority == 0.0f || !AITarget.list.Contains(memory.Key)) toBeRemoved.Add(memory.Key);
            }

            foreach (AITarget target in toBeRemoved)
            {
                targetMemories.Remove(target);
            }
        }

        public override void FillNetworkData(NetOutgoingMessage message)
        {
            message.Write((byte)state);

            message.Write(wallAttackPos.X);
            message.Write(wallAttackPos.Y);

            message.Write(steeringManager.WanderAngle);
            message.Write(updateTargetsTimer);
            message.Write(raycastTimer);
            message.Write(coolDownTimer);

            message.Write(targetEntity==null ? -1 : (targetEntity as Entity).ID);
        }

        public override void ReadNetworkData(NetIncomingMessage message)
        {
            state = (AiState)(message.ReadByte());

            wallAttackPos.X = message.ReadFloat();
            wallAttackPos.Y = message.ReadFloat();

            steeringManager.WanderAngle = message.ReadFloat();
            updateTargetsTimer = message.ReadFloat();
            raycastTimer = message.ReadFloat();
            coolDownTimer = message.ReadFloat();

            int targetID = message.ReadInt32();
            
            if (targetID>-1)            
                targetEntity = Entity.FindEntityByID(targetID) as IDamageable;            
            
        }
    }

    //the "memory" of the character 
    //keeps track of how preferable it is to attack a specific target
    //(if the character can't inflict much damage the target, the priority decreases
    //and if the target attacks the character, the priority increases)
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
