using Barotrauma.Extensions;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class CombatMission : Mission
    {
        private Submarine[] subs;

        private readonly LocalizedString[] descriptions;
        private static LocalizedString[] teamNames = { "Team A", "Team B" };

        private readonly bool allowRespawning;

        enum WinCondition
        {
            /// <summary>
            /// The winner is the team with the last living player(s)
            /// </summary>
            LastManStanding,
            /// <summary>
            /// The team who reaches a specific number of kills (determined by WinScore) is the winner
            /// </summary>
            KillCount,
            /// <summary>
            /// The team who controls a specific submarine (can be a ruin, outpost or a beacon station too) for some time (determined by WinScore) is the winner
            /// </summary>
            ControlSubmarine
        }

        private readonly WinCondition winCondition;

        public override bool AllowRespawning
        {
            get => allowRespawning;
        }

        private Submarine targetSubmarine;

        private LocalizedString targetSubmarineSonarLabel;

        /// <summary>
        /// Which type of submarine the team needs to stay in control of to win
        /// </summary>
        public TagAction.SubType TargetSubmarineType { get; set; }

        public readonly int PointsPerKill;

        /// <summary>
        /// The score required to win the mission.
        /// </summary>
        public int WinScore => GameMain.NetworkMember?.ServerSettings.WinScorePvP ?? 10;

        /// <summary>
        /// Is the winner determined by some kind of a scoring mechanism?
        /// </summary>
        public bool HasWinScore => 
            winCondition != WinCondition.LastManStanding || PointsPerKill != 0;

        /// <summary>
        /// Scores of both teams. What the scoring represents depends on how the mission is configured (kills, time in control of a beacon station?)
        /// </summary>
        public readonly int[] Scores = new int[2];

        public static CharacterTeamType Winner
        {
            get
            {
                if (GameMain.GameSession?.WinningTeam == null) { return CharacterTeamType.None; } 
                return GameMain.GameSession.WinningTeam.Value;
            }
        }

        public override LocalizedString SuccessMessage
        {
            get 
            {
                if (Winner == CharacterTeamType.None || base.SuccessMessage.IsNullOrEmpty()) { return ""; }

                //disable success message for now if it hasn't been translated
                if (!TextManager.ContainsTag("MissionSuccess." + Prefab.TextIdentifier)) { return ""; }

                var loser = Winner == CharacterTeamType.Team1 ? 
                    CharacterTeamType.Team2 : 
                    CharacterTeamType.Team1;

                return base.SuccessMessage
                    .Replace("[loser]", GetTeamName(loser))
                    .Replace("[winner]", GetTeamName(Winner));
            }
        }

        public CombatMission(MissionPrefab prefab, Location[] locations, Submarine sub)
            : base(prefab, locations, sub)
        {
            allowRespawning = prefab.ConfigElement.GetAttributeBool(nameof(AllowRespawning), false);

            winCondition = prefab.ConfigElement.GetAttributeEnum(nameof(WinCondition),
                allowRespawning ? WinCondition.KillCount : WinCondition.LastManStanding);

            PointsPerKill = prefab.ConfigElement.GetAttributeInt(nameof(PointsPerKill), 0);

            TargetSubmarineType = prefab.ConfigElement.GetAttributeEnum(nameof(TargetSubmarineType), TagAction.SubType.Any);

            string sonarTag = prefab.ConfigElement.GetAttributeString(nameof(targetSubmarineSonarLabel), string.Empty);
            if (!sonarTag.IsNullOrEmpty())
            {
                targetSubmarineSonarLabel = TextManager.Get(sonarTag);
            }

            if (allowRespawning && winCondition == WinCondition.LastManStanding)
            {
                DebugConsole.ThrowError($"Error in mission {prefab.Identifier}: win condition cannot be \"last man standing\" when respawning is enabled.",
                    contentPackage: prefab.ContentPackage);
            }

            descriptions = new LocalizedString[]
            {
                TextManager.Get("MissionDescriptionNeutral." + prefab.TextIdentifier).Fallback(prefab.ConfigElement.GetAttributeString("descriptionneutral", "")),
                TextManager.Get("MissionDescription1." + prefab.TextIdentifier).Fallback(prefab.ConfigElement.GetAttributeString("description1", "")),
                TextManager.Get("MissionDescription2." + prefab.TextIdentifier).Fallback(prefab.ConfigElement.GetAttributeString("description2", ""))
            };

            for (int i = 0; i < descriptions.Length; i++)
            {
                for (int n = 0; n < 2; n++)
                {
                    descriptions[i] = 
                        descriptions[i]
                            .Replace($"[location{n + 1}]", locations[n].DisplayName)
                            .Replace("[winscore]", WinScore.ToString());
                }
            }

            teamNames = new LocalizedString[]
            {
                TextManager.Get("MissionTeam1." + prefab.TextIdentifier).Fallback(TextManager.Get(prefab.ConfigElement.GetAttributeString("teamname1", "missionteam1.pvpmission"))),
                TextManager.Get("MissionTeam2." + prefab.TextIdentifier).Fallback(TextManager.Get(prefab.ConfigElement.GetAttributeString("teamname2", "missionteam2.pvpmission"))),
            };

            if (winCondition == WinCondition.KillCount && PointsPerKill == 0)
            {
                DebugConsole.AddWarning($"Potential error in mission {Prefab.Identifier}: win condition is kill count, but {nameof(PointsPerKill)} is set to 0.");
            }
        }

        public static LocalizedString GetTeamName(CharacterTeamType teamID)
        {
            if (teamID == CharacterTeamType.Team1)
            {
                return teamNames.Length > 0 ? teamNames[0] : "Team 1";
            }
            else if (teamID == CharacterTeamType.Team2)
            {
                return teamNames.Length > 1 ? teamNames[1] : "Team 2";
            }

            return "Invalid Team";
        }

        public static bool IsInWinningTeam(Character character)
        {
            return character != null &&
                Winner != CharacterTeamType.None &&
                Winner == character.TeamID;
        }

        protected override void StartMissionSpecific(Level level)
        {
            if (GameMain.NetworkMember == null)
            {
                DebugConsole.ThrowError("Combat missions cannot be played in the single player mode.");
                return;
            }
            
            subs = new Submarine[] { Submarine.MainSubs[0], Submarine.MainSubs[1] };

            if (Prefab.LoadSubmarines)
            {
                subs[0].NeutralizeBallast(); 
                subs[0].TeamID = CharacterTeamType.Team1;
                subs[0].GetConnectedSubs().ForEach(s => s.TeamID = CharacterTeamType.Team1);

                subs[1].NeutralizeBallast();
                subs[1].TeamID = CharacterTeamType.Team2;
                subs[1].GetConnectedSubs().ForEach(s => s.TeamID = CharacterTeamType.Team2);
                GameSession.PlaceSubAtInitialPosition(subs[1], level, placeAtStart: false);
                subs[1].FlipX();
            }
#if SERVER
            crews = new List<Character>[] { new List<Character>(), new List<Character>() };
            roundEndTimer = RoundEndDuration;
#endif

            if (TargetSubmarineType != TagAction.SubType.Any)
            {
                targetSubmarine = Submarine.Loaded.FirstOrDefault(s => TagAction.SubmarineTypeMatches(s, TargetSubmarineType));
                if (targetSubmarine == null)
                {
                    DebugConsole.ThrowError($"Error in mission {Prefab.Identifier}: could not find a submarine of the type {TargetSubmarineType}.",
                        contentPackage: Prefab.ContentPackage);
                }
            }
        }

        protected override bool DetermineCompleted()
        {
            return Winner != CharacterTeamType.None;
        }
    }
}
