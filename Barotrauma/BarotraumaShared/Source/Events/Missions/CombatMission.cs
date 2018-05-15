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
        private int winner = -1;

        private string[] descriptions;

        private static string[] teamNames = { "Team A", "Team B" };

        private bool initialized = false;

        public override bool AllowRespawn
        {
            get { return false; }
        }

        public override string Description
        {
            get
            {
                if (descriptions == null) return "";

                if (GameMain.NetworkMember == null || GameMain.NetworkMember.Character == null)
                {
                    //non-team-specific description
                    return descriptions[0];
                }

                //team specific
                return descriptions[GameMain.NetworkMember.Character.TeamID];
            }
        }


        public override string SuccessMessage
        {
            get 
            {
                if (winner == -1) return "";

                return base.SuccessMessage
                    .Replace("[loser]", teamNames[1 - winner])
                    .Replace("[winner]", teamNames[winner]);
            }
        }

        public CombatMission(MissionPrefab prefab, Location[] locations)
            : base(prefab, locations)
        {
            descriptions = new string[]
            {
                prefab.XmlConfig.GetAttributeString("descriptionneutral", ""),
                prefab.XmlConfig.GetAttributeString("description1", ""),
                prefab.XmlConfig.GetAttributeString("description2", "")
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
                prefab.XmlConfig.GetAttributeString("teamname1", "Team A"),
                prefab.XmlConfig.GetAttributeString("teamname2", "Team B")
            };
        }

        public static string GetTeamName(int teamID)
        {
            //team IDs start from 1, while teamName array starts from 0
            teamID--;

            if (teamID < 0 || teamID >= teamNames.Length)
            {
                return "Team " + teamID;
            }

            return teamNames[teamID];
        }

        public override bool AssignTeamIDs(List<Client> clients, out byte hostTeam)
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
                    randList[i].TeamID = 1;
                }
                else
                {
                    randList[i].TeamID = 2;
                }
            }
            if (halfPlayers * 2 == randList.Count)
            {
                hostTeam = (byte)Rand.Range(1, 2);
            }
            else if (halfPlayers * 2 < randList.Count)
            {
                hostTeam = 1;
            }
            else
            {
                hostTeam = 2;
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
            subs[0].TeamID = 1; subs[1].TeamID = 2;
            subs[1].SetPosition(Level.Loaded.EndPosition - new Vector2(0.0f, 2000.0f));
            subs[1].FlipX();

            crews = new List<Character>[] { new List<Character>(), new List<Character>() };

            foreach (Submarine submarine in Submarine.Loaded)
            {
                //hide all subs from radar to make sneak attacks possible
                submarine.OnRadar = false;
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
                    if (character.TeamID == 1)
                    {
                        crews[0].Add(character);
                    }
                    else if (character.TeamID == 2)
                    {
                        crews[1].Add(character);
                    }
                }

                if (GameMain.Client != null)
                {
                    //no characters in one of the teams, the client may not have received all spawn messages yet
                    if (crews[0].Count == 0 || crews[1].Count == 0) return;
                }

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
                    if (!teamDead[i] && teamDead[1-i])
                    {
                        //make sure nobody in the other team can be revived because that would be pretty weird
                        crews[1-i].ForEach(c => { if (!c.IsDead) c.Kill(CauseOfDeathType.Unknown, null); });

                        winner = i;

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
                if (winner>=0 && subs[winner] != null && 
                    (winner == 0 && subs[winner].AtStartPosition) || (winner == 1 && subs[winner].AtEndPosition) &&
                    crews[winner].Any(c => !c.IsDead && c.Submarine == subs[winner]))
                {
#if CLIENT
                    GameMain.GameSession.CrewManager.WinningTeam = winner+1;
#endif
                    if (GameMain.Server != null) GameMain.Server.EndGame();
                }
            }

            if (teamDead[0] && teamDead[1])
            {
#if CLIENT
                GameMain.GameSession.CrewManager.WinningTeam = 0;
#endif
                winner = -1;
                if (GameMain.Server != null) GameMain.Server.EndGame();
            }
        }

        public override void End()
        {
            if (GameMain.NetworkMember == null) return;            
            
            if (winner > -1)
            {
                GiveReward();
                completed = true;
            }
        }
    }
}
