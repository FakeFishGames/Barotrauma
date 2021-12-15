using Microsoft.Xna.Framework;

namespace Barotrauma
{
    partial class AICharacter : Character
    {        
        private AIController aiController;
        
        public override AIController AIController
        {
            get { return aiController; }
        }

        public AICharacter(CharacterPrefab prefab, string speciesName, Vector2 position, string seed, CharacterInfo characterInfo = null, ushort id = Entity.NullEntityID, bool isNetworkPlayer = false, RagdollParams ragdoll = null)
            : base(prefab, speciesName, position, seed, characterInfo, id: id, isRemotePlayer: isNetworkPlayer, ragdollParams: ragdoll)
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
                if (characterDistSqr > MathUtils.Pow2(Params.DisableDistance * 0.5f))
                {
                    AnimController.SimplePhysicsEnabled = true;
                }
                else if (characterDistSqr < MathUtils.Pow2(Params.DisableDistance * 0.5f * 0.9f))
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
