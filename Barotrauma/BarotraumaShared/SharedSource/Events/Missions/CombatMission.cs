using Barotrauma.Items.Components;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class CombatMission : Mission
    {
        private Submarine[] subs;
        private List<Character>[] crews;

        private readonly string[] descriptions;
        private static string[] teamNames = { "Team A", "Team B" };

        public override bool AllowRespawn
        {
            get { return false; }
        }

        private Character.TeamType Winner
        {
            get
            {
                if (GameMain.GameSession?.WinningTeam == null) { return Character.TeamType.None; } 
                return GameMain.GameSession.WinningTeam.Value;
            }
        }

        public override string SuccessMessage
        {
            get 
            {
                if (Winner == Character.TeamType.None || string.IsNullOrEmpty(base.SuccessMessage)) { return ""; }

                //disable success message for now if it hasn't been translated
                if (!TextManager.ContainsTag("MissionSuccess." + Prefab.TextIdentifier)) { return ""; }

                var loser = Winner == Character.TeamType.Team1 ? 
                    Character.TeamType.Team2 : 
                    Character.TeamType.Team1;

                return base.SuccessMessage
                    .Replace("[loser]", GetTeamName(loser))
                    .Replace("[winner]", GetTeamName(Winner));
            }
        }

        public CombatMission(MissionPrefab prefab, Location[] locations)
            : base(prefab, locations)
        {
            descriptions = new string[]
            {
                TextManager.Get("MissionDescriptionNeutral." + prefab.TextIdentifier, true) ?? prefab.ConfigElement.GetAttributeString("descriptionneutral", ""),
                TextManager.Get("MissionDescription1." + prefab.TextIdentifier, true) ?? prefab.ConfigElement.GetAttributeString("description1", ""),
                TextManager.Get("MissionDescription2." + prefab.TextIdentifier, true) ?? prefab.ConfigElement.GetAttributeString("description2", "")
            };

            for (int i = 0; i < descriptions.Length; i++)
            {
                for (int n = 0; n < 2; n++)
                {
                    descriptions[i] = descriptions[i].Replace("[location" + (n + 1) + "]", locations[n].Name);
                }
            }

            teamNames = new string[]
            {
                TextManager.Get("MissionTeam1." + prefab.TextIdentifier, true) ?? prefab.ConfigElement.GetAttributeString("teamname1", "Team A"),
                TextManager.Get("MissionTeam2." + prefab.TextIdentifier, true) ?? prefab.ConfigElement.GetAttributeString("teamname2", "Team B")
            };
        }

        public static string GetTeamName(Character.TeamType teamID)
        {
            if (teamID == Character.TeamType.Team1)
            {
                return teamNames.Length > 0 ? teamNames[0] : "Team 1";
            }
            else if (teamID == Character.TeamType.Team2)
            {
                return teamNames.Length > 1 ? teamNames[1] : "Team 2";
            }

            return "Invalid Team";
        }

        public bool IsInWinningTeam(Character character)
        {
            return character != null && 
                Winner != Character.TeamType.None &&
                Winner == character.TeamID;
        }
        
        public override void Start(Level level)
        {
            if (GameMain.NetworkMember == null)
            {
                DebugConsole.ThrowError("Combat missions cannot be played in the single player mode.");
                return;
            }
            
            subs = new Submarine[] { Submarine.MainSubs[0], Submarine.MainSubs[1] };
            subs[0].TeamID = Character.TeamType.Team1; subs[1].TeamID = Character.TeamType.Team2;
            subs[1].SetPosition(subs[1].FindSpawnPos(Level.Loaded.EndPosition));
            subs[1].FlipX();

            //prevent wifi components from communicating between subs
            List<WifiComponent> wifiComponents = new List<WifiComponent>();
            foreach (Item item in Item.ItemList)
            {
                wifiComponents.AddRange(item.GetComponents<WifiComponent>());
            }
            foreach (WifiComponent wifiComponent in wifiComponents)
            {
                for (int i = 0; i < 2; i++)
                {
                    if (wifiComponent.Item.Submarine == subs[i] || subs[i].DockedTo.Contains(wifiComponent.Item.Submarine))
                    {
                        wifiComponent.TeamID = subs[i].TeamID;
                    }
                }
            }

            crews = new List<Character>[] { new List<Character>(), new List<Character>() };

            foreach (Submarine submarine in Submarine.Loaded)
            {
                //hide all subs from sonar to make sneak attacks possible
                submarine.ShowSonarMarker = false;
            }
        }

        public override void End()
        {
            if (GameMain.NetworkMember == null) return;

            if (Winner != Character.TeamType.None)
            {
                GiveReward();
                completed = true;
            }
        }
    }
}
