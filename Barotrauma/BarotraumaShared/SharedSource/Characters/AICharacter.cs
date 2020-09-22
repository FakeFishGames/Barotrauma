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
            : base(speciesName, position, seed, characterInfo, isNetworkPlayer, ragdoll)
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
                float characterDist = float.MaxValue;
#if CLIENT
                characterDist = Vector2.DistanceSquared(cam.GetPosition(), WorldPosition);
#elif SERVER
                if (GameMain.Server != null)
                {
                    characterDist = GetClosestDistance();
                }
#endif

                if (characterDist > EnableSimplePhysicsDistSqr)
                {
                    AnimController.SimplePhysicsEnabled = true;
                }
                else if (characterDist < DisableSimplePhysicsDistSqr)
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

#if SERVER
        // Gets the closest distance, either an active player character or spectator
        private float GetClosestDistance()
        {
            float minDist = float.MaxValue;

            for (int i = 0; i < GameMain.Server.ConnectedClients.Count; i++)
            {
                var spectatePos = GameMain.Server.ConnectedClients[i].SpectatePos;
                if (spectatePos != null)
                {
                    float dist = Vector2.DistanceSquared(spectatePos.Value, WorldPosition);

                    if (dist < minDist)
                    {
                        minDist = dist;
                    }
                    if (dist < DisableSimplePhysicsDistSqr)
                    {
                        return dist;
                    }
                }
            }

            foreach (Character c in CharacterList)
            {
                if (c != this && c.IsRemotePlayer)
                {
                    float dist = Vector2.DistanceSquared(c.WorldPosition, WorldPosition);

                    if (dist < minDist)
                    {
                        minDist = dist;
                    }
                    if (dist < DisableSimplePhysicsDistSqr)
                    {
                        return dist;
                    }
                }
            }

            return minDist;
        }
#endif
    }
}
