using Microsoft.Xna.Framework;

namespace Barotrauma
{
    partial class AICharacter : Character
    {
        //characters that are further than this from the camera (and all clients)
        //have all their limb physics bodies disabled
        const float EnableSimplePhysicsDist = 10000.0f;        
        const float DisableSimplePhysicsDist = EnableSimplePhysicsDist * 0.9f;

        const float EnableSimplePhysicsDistSqr = EnableSimplePhysicsDist * EnableSimplePhysicsDist;
        const float DisableSimplePhysicsDistSqr = DisableSimplePhysicsDist * DisableSimplePhysicsDist;
        
        private AIController aiController;
        
        public override AIController AIController
        {
            get { return aiController; }
        }
        
        public AICharacter(string file, Vector2 position, CharacterInfo characterInfo = null, bool isNetworkPlayer = false)
            : base(file, position, characterInfo, isNetworkPlayer)
        {

        }
        partial void InitProjSpecific();

        public void SetAI(AIController aiController)
        {
            this.aiController = aiController;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            base.Update(deltaTime, cam);

            if (!Enabled) return;

            if (!IsRemotePlayer)
            {
                float characterDist = Vector2.DistanceSquared(cam.WorldViewCenter, WorldPosition);
                if (GameMain.Server != null)
                {
                    //get the distance from the closest player to this character
                    foreach (Character c in CharacterList)
                    {
                        if (c != this && (c.IsRemotePlayer || c == GameMain.Server.Character))
                        {
                            float dist = Vector2.DistanceSquared(c.WorldPosition, WorldPosition);
                            if (dist < characterDist)
                            {
                                characterDist = dist;
                                if (characterDist < DisableSimplePhysicsDistSqr) break;
                            }
                        }
                    }
                }

                if (characterDist > EnableSimplePhysicsDistSqr)
                {
                    AnimController.SimplePhysicsEnabled = true;
                }
                else if (characterDist < DisableSimplePhysicsDistSqr)
                {
                    AnimController.SimplePhysicsEnabled = false;
                }
            }

            if (IsDead || Health <= 0.0f || IsUnconscious || Stun > 0.0f) return;
            if (Controlled == this || !aiController.Enabled) return;
            
            SoundUpdate(deltaTime);

            if (!IsRemotePlayer)
            {
                aiController.Update(deltaTime);
            }
        }
        partial void SoundUpdate(float deltaTime);

        public override void AddDamage(CauseOfDeath causeOfDeath, float amount, Character attacker)
        {
            base.AddDamage(causeOfDeath, amount, attacker);

            if (attacker != null) aiController.OnAttacked(attacker, amount);
        }

        public override AttackResult ApplyAttack(Character attacker, Vector2 worldPosition, Attack attack, float deltaTime, bool playSound = false, Limb limb = null)
        {
            AttackResult result = base.ApplyAttack(attacker, worldPosition, attack, deltaTime, playSound, limb);

            aiController.OnAttacked(attacker, result.Damage + result.Bleeding);

            return result;
        }
    }
}
