using Microsoft.Xna.Framework;
using System;

namespace Barotrauma
{
    partial class AICharacter : Character
    {
        //characters that are further than this from the camera (and all clients)
        //have all their limb physics bodies disabled
        const float EnableSimplePhysicsDist = 6000.0f;        
        const float DisableSimplePhysicsDist = EnableSimplePhysicsDist * 0.9f;

        const float EnableSimplePhysicsDistSqr = EnableSimplePhysicsDist * EnableSimplePhysicsDist;
        const float DisableSimplePhysicsDistSqr = DisableSimplePhysicsDist * DisableSimplePhysicsDist;
        
        private AIController aiController;
        
        public override AIController AIController
        {
            get { return aiController; }
        }

        public AICharacter(string speciesName, Vector2 position, string seed, CharacterInfo characterInfo = null, bool isNetworkPlayer = false, RagdollParams ragdoll = null)
            : base(speciesName, position, seed, characterInfo, id: Entity.NullEntityID, isRemotePlayer: isNetworkPlayer, ragdollParams: ragdoll)
        {
            InitProjSpecific();
        }

        partial void InitProjSpecific();

        public void SetAI(AIController aiController)
        {
            if (AIController != null)
            {
                OnAttacked -= AIController.OnAttacked;
            }

            this.aiController = aiController;
            if (aiController != null)
            {
                OnAttacked += aiController.OnAttacked;
            }
        }

        public override void Update(float deltaTime, Camera cam)
        {
            base.Update(deltaTime, cam);

            if (!Enabled) { return; }
            if (!IsRemotePlayer && AIController is EnemyAIController enemyAi)
            {
                enemyAi.PetBehavior?.Update(deltaTime);
            }
            if (IsDead || Vitality <= 0.0f || Stun > 0.0f || IsIncapacitated)
            {
                //don't enable simple physics on dead/incapacitated characters
                //the ragdoll controls the movement of incapacitated characters instead of the collider,
                //but in simple physics mode the ragdoll would get disabled, causing the character to not move at all
                AnimController.SimplePhysicsEnabled = false;
                return;
            }

            if (!IsRemotePlayer && !(AIController is HumanAIController))
            {
                float characterDistSqr = GetDistanceSqrToClosestPlayer();
                if (characterDistSqr > EnableSimplePhysicsDistSqr)
                {
                    AnimController.SimplePhysicsEnabled = true;
                }
                else if (characterDistSqr < DisableSimplePhysicsDistSqr)
                {
                    AnimController.SimplePhysicsEnabled = false;
                }
            }

            if (GameMain.NetworkMember != null && !GameMain.NetworkMember.IsServer) { return; }
            if (Controlled == this) { return; }

            if (!IsRemotelyControlled && aiController != null && aiController.Enabled)
            {
                aiController.Update(deltaTime);
            }
        }
    }
}
