using System.Collections.Generic;

namespace Barotrauma
{
    partial class CombatMission : Mission
    {
        private Submarine[] subs;

        private readonly LocalizedString[] descriptions;
        private static LocalizedString[] teamNames = { "Team A", "Team B" };

        public override bool AllowRespawn
        {
            get { return false; }
        }

        private CharacterTeamType Winner
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
                    descriptions[i] = descriptions[i].Replace("[location" + (n + 1) + "]", locations[n].Name);
                }
            }

            teamNames = new LocalizedString[]
            {
                TextManager.Get("MissionTeam1." + prefab.TextIdentifier).Fallback(prefab.ConfigElement.GetAttributeString("teamname1", "Team A")),
                TextManager.Get("MissionTeam2." + prefab.TextIdentifier).Fallback(prefab.ConfigElement.GetAttributeString("teamname2", "Team B"))
            };
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

        public bool IsInWinningTeam(Character character)
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

            subs[0].NeutralizeBallast(); 
            subs[0].TeamID = CharacterTeamType.Team1;
            subs[0].GetConnectedSubs().ForEach(s => s.TeamID = CharacterTeamType.Team1);

            subs[1].NeutralizeBallast();
            subs[1].TeamID = CharacterTeamType.Team2;
            subs[1].GetConnectedSubs().ForEach(s => s.TeamID = CharacterTeamType.Team2);
            subs[1].SetPosition(subs[1].FindSpawnPos(Level.Loaded.EndPosition));
            subs[1].FlipX();
#if SERVER
            crews = new List<Character>[] { new List<Character>(), new List<Character>() };
            roundEndTimer = RoundEndDuration;
#endif
        }

        protected override bool DetermineCompleted()
        {
            return Winner != CharacterTeamType.None;
        }
    }
}
