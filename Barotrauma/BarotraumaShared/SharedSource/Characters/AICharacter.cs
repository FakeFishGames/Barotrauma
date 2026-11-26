using Microsoft.Xna.Framework;
using System;

namespace Barotrauma
{
    public partial class AICharacter : Character
    {        
        private AIController aiController;
        
        public override AIController AIController
        {
            get { return aiController; }
        }

        public AICharacter(CharacterPrefab prefab, Vector2 position, string seed, CharacterInfo characterInfo = null, ushort id = Entity.NullEntityID, bool isNetworkPlayer = false, RagdollParams ragdoll = null, bool spawnInitialItems = true)
            : base(prefab, position, seed, characterInfo, id: id, isRemotePlayer: isNetworkPlayer, ragdollParams: ragdoll, spawnInitialItems)
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
            if (IsDead || IsUnconscious || IsIncapacitated ||
                //only check "real" stuns here, ignoring ragdolling, so the AI can run and decide whether to ragdoll or unragdoll
                CharacterHealth.Stun > 0.0f)
            {
                //don't enable simple physics on dead/incapacitated characters
                //the ragdoll controls the movement of incapacitated characters instead of the collider,
                //but in simple physics mode the ragdoll would get disabled, causing the character to not move at all
                AnimController.SimplePhysicsEnabled = false;
                return;
            }

            if (IsRemotePlayer)
            {
                //remotely controlled characters should never be in simple-physics mode
                AnimController.SimplePhysicsEnabled = false;
            }
            else
            {
                if (Ragdoll.ForceSimplePhysics)
                {
                    AnimController.SimplePhysicsEnabled = true;
                }
                else
                {
                    float simplePhysicsDistance = CurrentHull == null ? Params.SimplePhysicsDistanceOutdoors : Params.SimplePhysicsDistanceIndoors;
#if SERVER
                    //server bases the simple physics distance on the distance to the closest player
                    float characterDistSqr = GetDistanceSqrToClosestPlayer();
#else
                    //clients base the simple physics distance on the distance to controlled character or camera
                    float characterDistSqr = Vector2.Distance(Controlled?.WorldPosition ?? Screen.Selected.Cam.WorldViewCenter, WorldPosition);
#endif
#if CLIENT
                    //on-screen characters must never be in simple-physics mode client-side, no matter how far from the character or camera
                    if (!IsOffscreen(distance: 0.0f))
                    {
                        AnimController.SimplePhysicsEnabled = false;
                    }
                    else
#endif
                    if (characterDistSqr > MathUtils.Pow2(simplePhysicsDistance))
                    {
                        AnimController.SimplePhysicsEnabled = true;
                    }
                    //don't re-enable until the character comes to 90% of the distance threshold,
                    //to avoid toggling simple physics on and off when the character is moving around the threshold distance
                    else if (characterDistSqr < MathUtils.Pow2(simplePhysicsDistance * 0.9f))
                    {
                        if (Params.SimplePhysicsDistanceOffscreen > 0.0f && GameMain.NetworkMember is not { IsServer: true })
                        {
                            //same thing here: use a higher threshold for disabling simple physics than for enabling it,
                            //to avoid toggling when the character is moving around the threshold distance
                            if (IsOffscreen(distance: Params.SimplePhysicsDistanceOffscreen * 1.5f))
                            {
                                AnimController.SimplePhysicsEnabled = true;

                            }
                            else if (!IsOffscreen(distance: Params.SimplePhysicsDistanceOffscreen))
                            {
                                AnimController.SimplePhysicsEnabled = false;
                            }
                        }
                        else
                        {
                            AnimController.SimplePhysicsEnabled = false;
                        }
                    }
                }

                bool IsOffscreen(float distance)
                {
                    return 
                        Math.Abs(WorldPosition.X - cam.WorldViewCenter.X) > cam.WorldView.Width / 2 + distance ||
                        Math.Abs(WorldPosition.Y - cam.WorldViewCenter.Y) > cam.WorldView.Height / 2 + distance;
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
