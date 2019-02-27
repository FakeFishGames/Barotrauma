using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class CombatMission : Mission
    {
        private Submarine[] subs;
        private List<Character>[] crews;

        private int state = 0;
        private Character.TeamType winner = Character.TeamType.None;

        private string[] descriptions;

        private static string[] teamNames = { "Team A", "Team B" };

        private bool initialized = false;

        public override bool AllowRespawn
        {
            get { return false; }
        }

        public Character.TeamType? Winner
        {
            get { return winner; }
        }

        public override string Description
        {
            get
            {
                if (descriptions == null) return "";

#if CLIENT
                if (GameMain.Client == null || 
                    GameMain.Client.Character == null)
                {
                    //non-team-specific description
                    return descriptions[0];
                }

                //team specific
                return descriptions[GameMain.Client.Character.TeamID == Character.TeamType.Team1 ? 1 : 2];
#elif SERVER
                //non-team-specific description
                return descriptions[0];
#endif
            }
        }


        public override string SuccessMessage
        {
            get 
            {
                if (winner == Character.TeamType.None) { return ""; }

                var loser = winner == Character.TeamType.Team1 ? 
                    Character.TeamType.Team2 : 
                    Character.TeamType.Team1;

                return base.SuccessMessage
                    .Replace("[loser]", GetTeamName(loser))
                    .Replace("[winner]", GetTeamName(winner));
            }
        }

        public CombatMission(MissionPrefab prefab, Location[] locations)
            : base(prefab, locations)
        {
            descriptions = new string[]
            {
                TextManager.Get("MissionDescriptionNeutral." + prefab.Identifier, true) ?? prefab.ConfigElement.GetAttributeString("descriptionneutral", ""),
                TextManager.Get("MissionDescription1." + prefab.Identifier, true) ?? prefab.ConfigElement.GetAttributeString("description1", ""),
                TextManager.Get("MissionDescription2." + prefab.Identifier, true) ?? prefab.ConfigElement.GetAttributeString("description2", "")
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
                TextManager.Get("MissionTeam1." + prefab.Identifier, true) ?? prefab.ConfigElement.GetAttributeString("teamname1", "Team A"),
                TextManager.Get("MissionTeam2." + prefab.Identifier, true) ?? prefab.ConfigElement.GetAttributeString("teamname2", "Team B")
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
            return character != null && winner != Character.TeamType.None && character.TeamID == winner;
        }

        public override bool AssignTeamIDs(List<Client> clients)
        {
            List<Client> randList = new List<Client>(clients);
            for (int i = 0; i < randList.Count; i++)
            {
                Client a = randList[i];
                int oi = Rand.Range(0, randList.Count - 1);
                Client b = randList[oi];
                randList[i] = b;
                randList[oi] = a;
            }
            int halfPlayers = randList.Count / 2;
            for (int i = 0; i < randList.Count; i++)
            {
                if (i < halfPlayers)
                {
                    randList[i].TeamID = Character.TeamType.Team1;
                }
                else
                {
                    randList[i].TeamID = Character.TeamType.Team2;
                }
            }
            return true;
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

        public override void Update(float deltaTime)
        {
            if (!initialized)
            {
                crews[0].Clear();
                crews[1].Clear();
                foreach (Character character in Character.CharacterList)
                {
                    if (character.TeamID == Character.TeamType.Team1)
                    {
                        crews[0].Add(character);
                    }
                    else if (character.TeamID == Character.TeamType.Team2)
                    {
                        crews[1].Add(character);
                    }
                }

#if CLIENT
                if (GameMain.Client != null)
                {
                    //no characters in one of the teams, the client may not have received all spawn messages yet
                    if (crews[0].Count == 0 || crews[1].Count == 0) return;
                }
#endif

                initialized = true;
            }
            
            bool[] teamDead = 
            { 
                crews[0].All(c => c.IsDead || c.IsUnconscious),
                crews[1].All(c => c.IsDead || c.IsUnconscious)
            };

            if (state == 0)
            {
                for (int i = 0; i < teamDead.Length; i++)
                {
                    if (!teamDead[i] && teamDead[1 - i])
                    {
                        //make sure nobody in the other team can be revived because that would be pretty weird
                        crews[1 - i].ForEach(c => { if (!c.IsDead) c.Kill(CauseOfDeathType.Unknown, null); });

                        winner = i == 0 ? Character.TeamType.Team1 : Character.TeamType.Team2;

#if CLIENT
                        ShowMessage(i);
#endif
                        state = 1;
                        break;
                    }
                }
            }
            else
            {
                if (winner != Character.TeamType.None)
                {
#if CLIENT
                    GameMain.GameSession.CrewManager.WinningTeam = winner;
#endif
#if SERVER
                    if (GameMain.Server != null) GameMain.Server.EndGame();
#endif
                }
            }

            if (teamDead[0] && teamDead[1])
            {
#if CLIENT
                GameMain.GameSession.CrewManager.WinningTeam = Character.TeamType.None;
#endif
                winner = Character.TeamType.None;
#if SERVER
                if (GameMain.Server != null) GameMain.Server.EndGame();
#endif
            }
        }

        public override void End()
        {
            if (GameMain.NetworkMember == null) return;            
            
            if (winner != Character.TeamType.None)
            {
                GiveReward();
                completed = true;
            }
        }
    }
}
