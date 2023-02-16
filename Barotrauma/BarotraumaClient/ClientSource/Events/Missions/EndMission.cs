using Barotrauma.Networking;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Barotrauma
{
    partial class EndMission : Mission
    {
        public override bool DisplayAsCompleted => false;

        public override bool DisplayAsFailed => false;

        partial void OnStateChangedProjSpecific()
        {
            SoundPlayer.ForceMusicUpdate();
            if (Phase == MissionPhase.NoItemsDestroyed)
            {
                CoroutineManager.Invoke(() =>
                {
                    if (boss != null && !boss.Removed)
                    {
                        new CameraTransition(boss, GameMain.GameScreen.Cam, null, Alignment.Center, panDuration: 8, fadeOut: false, startZoom: 1.0f, endZoom: 0.3f * GUI.yScale)
                        {
                            RunWhilePaused = false,
                            EndWaitDuration = 3.0f
                        };
                    }
                }, delay: 3.0f);
            }
            else if (Phase == MissionPhase.AllItemsDestroyed)
            {
                CoroutineManager.StartCoroutine(wakeUpCoroutine(), name: "EndMission.wakeUpCoroutine");
            }
            else if (Phase == MissionPhase.BossKilled)
            {
                if (!string.IsNullOrEmpty(endCinematicSound))
                {
                    SoundPlayer.PlaySound(endCinematicSound);
                }
                CoroutineManager.Invoke(() =>
                {
                    new CameraTransition(boss, GameMain.GameScreen.Cam, null, Alignment.Center, panDuration: 3, fadeOut: false, endZoom: 0.1f * GUI.yScale)
                    {
                        RunWhilePaused = false,
                        EndWaitDuration = float.PositiveInfinity
                    };
                }, delay: 3.0f);
            }

            IEnumerable<CoroutineStatus> wakeUpCoroutine()
            {
                yield return new WaitForSeconds(wakeUpCinematicDelay);
                if (boss != null && !boss.Removed)
                {
                    new CameraTransition(boss, GameMain.GameScreen.Cam, null, Alignment.Center, panDuration: 5.0f, fadeOut: false, losFadeIn: false, startZoom: 1.0f, endZoom: 0.4f * GUI.yScale)
                    {
                        RunWhilePaused = false,
                        EndWaitDuration = cameraWaitDuration
                    };
                }
                yield return new WaitForSeconds(bossWakeUpDelay);
                if (boss != null && !boss.Removed)
                {
                    foreach (var limb in boss.AnimController.Limbs)
                    {
                        if (!limb.FreezeBlinkState) { continue; }                        
                        limb.FreezeBlinkState = false;
                        if (limb.LightSource is Lights.LightSource light)
                        {
                            light.Enabled = true;
                        }
                    }
                }
            }
        }

        partial void UpdateProjSpecific()
        {
            if (Phase is MissionPhase.Initial or MissionPhase.NoItemsDestroyed or MissionPhase.SomeItemsDestroyed)
            {
                // Put asleep.
                // Have to set the light every frame (or at least periodically), because light.Enabled is changed when Character.IsVisible changes (off/on screen). See GameScreen.Draw(). 
                foreach (var limb in boss.AnimController.Limbs)
                {
                    if (limb.Params.BlinkFrequency > 0)
                    {
                        limb.FreezeBlinkState = true;
                        limb.BlinkPhase = -limb.Params.BlinkHoldTime;
                        if (limb.LightSource is Lights.LightSource light)
                        {
                            light.Enabled = false;
                        }
                    }
                }
            }

#if DEBUG
            if (PlayerInput.KeyHit(Microsoft.Xna.Framework.Input.Keys.O))
            {
                State = 0;
            }
            if (PlayerInput.KeyHit(Microsoft.Xna.Framework.Input.Keys.Y))
            {
                destructibleItems.ForEach(it => it.Condition = 0.0f);
            }
            if (PlayerInput.KeyHit(Microsoft.Xna.Framework.Input.Keys.U))
            {
                boss?.SetAllDamage(20000.0f, 0.0f, 0.0f);
            }
#endif
        }

        public override void ClientReadInitial(IReadMessage msg)
        {
            base.ClientReadInitial(msg);

            boss = Character.ReadSpawnData(msg);

            byte minionCount = msg.ReadByte();
            List<Character> minionList = new List<Character>();
            for (int i = 0; i < minionCount; i++)
            {
                var minion = Character.ReadSpawnData(msg);
                if (minion == null)
                {
                    throw new System.Exception($"Error in EndMission.ClientReadInitial: failed to create a minion (mission: {Prefab.Identifier}, index: {i})");
                }
                minionList.Add(minion);
            }
            minions = minionList.ToImmutableArray();
            if (minions.Length != minionCount)
            {
                throw new System.Exception("Error in EndMission.ClientReadInitial: minion count does not match the server count (" + minionCount + " != " + minions.Length + "mission: " + Prefab.Identifier + ")");
            }
        }
    }
}
