using Microsoft.Xna.Framework;
using System;

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
        
        public AICharacter(string file, Vector2 position, string seed, CharacterInfo characterInfo = null, bool isNetworkPlayer = false, RagdollParams ragdoll = null)
            : base(file, position, seed, characterInfo, isNetworkPlayer, ragdoll)
        {
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

            if (!Enabled) return;

            if (!IsRemotePlayer)
            {
                float characterDist = Vector2.DistanceSquared(cam.WorldViewCenter, WorldPosition);
#if SERVER
                if (GameMain.Server != null)
                {
                    //get the distance from the closest player to this character
                    foreach (Character c in CharacterList)
                    {
                        if (c != this && c.IsRemotePlayer)
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

            if (IsDead || Vitality <= 0.0f || IsUnconscious || Stun > 0.0f) return;
            if (!aiController.Enabled) return;
            if (GameMain.NetworkMember != null && !GameMain.NetworkMember.IsServer) return;
            if (Controlled == this) return;

            SoundUpdate(deltaTime);

            if (!IsRemotePlayer)
            {
                aiController.Update(deltaTime);
            }
        }
        partial void SoundUpdate(float deltaTime);
    }
}
