using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    internal class CoOpMode : MissionMode
    {
        private float endRoundTimer;

        public CoOpMode(GameModePreset preset, IEnumerable<MissionPrefab> missionPrefabs) : base(preset, ValidateMissionPrefabs(missionPrefabs, MissionPrefab.CoOpMissionClasses)) { }

        public CoOpMode(GameModePreset preset, MissionType missionType, string seed) : base(preset, ValidateMissionType(missionType, MissionPrefab.CoOpMissionClasses), seed) { }

#if CLIENT
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            if (!IsSinglePlayer || !GameMain.GameSession.CrewManager.IsSinglePlayer || !GameMain.GameSession.IsRunning || GameMain.GameSession.RoundEnding) return;

            bool isCrewDead = GameMain.GameSession.CrewManager.CharacterInfos.All(c => (c.Character == null || c.Character.IsDead || c.Character.IsIncapacitated));

            bool subAtLevelEnd = false;
            if (Submarine.MainSub != null)
            {
                if (Level.Loaded?.EndOutpost != null)
                {
                    Character player = Character.Controlled;
                    bool playerInsideOutpost = player != null && !player.IsDead && !player.IsUnconscious && player.Submarine == Level.Loaded.EndOutpost;

                    // Level finished if the sub is docked to the outpost or very close to the outpost and someone from the crew made it inside the outpost.
                    subAtLevelEnd = Submarine.MainSub.DockedTo.Contains(Level.Loaded.EndOutpost) || (Submarine.MainSub.AtEndExit && playerInsideOutpost);
                }
                else
                {
                    subAtLevelEnd = Submarine.MainSub.AtEndExit;
                }
            }

            float endRoundDelay = 1;
            if (isCrewDead)
            {
#if !DEBUG
                endRoundDelay = 10;
                endRoundTimer += deltaTime;
#endif
            }
            else if (subAtLevelEnd)
            {
                endRoundDelay = 5;
                endRoundTimer += deltaTime;
            }
            else
            {
                endRoundTimer = 0;
            }

            if (endRoundTimer >= endRoundDelay)
            {
                End();
            }
        }

        public override void End(CampaignMode.TransitionType transitionType = CampaignMode.TransitionType.None)
        {
            base.End(transitionType);

            if (!IsSinglePlayer || !GameMain.GameSession.CrewManager.IsSinglePlayer || !GameMain.GameSession.IsRunning || GameMain.GameSession.RoundEnding) return;

            endRoundTimer = 0;
            CoroutineManager.StartCoroutine(EndRound());
        }

        private static IEnumerable<CoroutineStatus> EndRound()
        {
            GameMain.GameSession?.EndRound("");

            CameraTransition EndCinematic = new(GameMain.GameSession.Submarine, GameMain.GameScreen.Cam, Alignment.CenterLeft, Alignment.CenterRight);
            while (EndCinematic.Running && Screen.Selected == GameMain.GameScreen)
            {
                yield return CoroutineStatus.Running;
            }

            GameMain.MainMenuScreen.Select();

            yield return CoroutineStatus.Success;
        }
#endif
    }
}
