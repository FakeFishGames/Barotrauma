using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    class CombatMission : Mission
    {
        public Submarine TeamASub = null;
        public List<Character> TeamACrew = new List<Character>();
        public Submarine TeamBSub = null;
        public List<Character> TeamBCrew = new List<Character>();

        int state = 0;
        string winner, loser;

        public override string SuccessMessage
        {
            get { return successMessage.Replace("[loser]",loser).Replace("[winner]",winner); }
        }

        public CombatMission(XElement element)
            : base(element)
        {
            
        }

        public override bool AssignTeamIDs(List<Client> clients,out int hostTeam)
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
            if (halfPlayers*2==randList.Count)
            {
                hostTeam = Rand.Range(1, 2);
            }
            else if (halfPlayers*2<randList.Count)
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
            if (GameMain.Server != null)
            {
                GameMain.Server.AllowRespawn = false;
            }

            Items.Components.Radar.StartMarker = Locations[0];
            Items.Components.Radar.EndMarker = Locations[1];
            TeamASub = Submarine.MainSubs[0];
            TeamBSub = Submarine.MainSubs[1];
            TeamBSub.SetPosition(Level.Loaded.EndPosition - new Vector2(0.0f, 2000.0f));
            TeamBSub.FlipX();

            foreach (Submarine submarine in Submarine.Loaded)
            {
                //hide all subs from radar to make sneak attacks possible
                submarine.OnRadar = false;
            }
        }

        public override void Update(float deltaTime)
        {
            if (TeamACrew.Count == 0 && TeamBCrew.Count == 0)
            {
                if (GameMain.Server != null)
                {
                    GameMain.Server.AllowRespawn = false;
                }

                foreach (Character character in Character.CharacterList)
                {
                    if (character.TeamID == 1)
                    {
                        TeamACrew.Add(character);
                    }
                    else if (character.TeamID == 2)
                    {
                        TeamBCrew.Add(character);
                    }
                }
            }

            bool ADead = TeamACrew.All(c => c.IsDead || c.IsUnconscious);
            bool BDead = TeamBCrew.All(c => c.IsDead || c.IsUnconscious);

            if (BDead && !ADead)
            {
                TeamBCrew.ForEach(c => { if (!c.IsDead) c.Kill(CauseOfDeath.Damage); }); //make sure nobody in this team can be revived because that would be pretty weird
                winner = Locations[0];
                loser = Locations[1];
                if (state==0)
                {
                    ShowMessage(1);
                    state = 1;
                }
            }
            if (ADead && !BDead)
            {
                TeamACrew.ForEach(c => { if (!c.IsDead) c.Kill(CauseOfDeath.Damage); }); //same as above
                winner = Locations[1];
                loser = Locations[0];
                if (state == 0)
                {
                    ShowMessage(0);
                    state = 1;
                }
            }
            
            if ((TeamBSub != null && TeamBSub.AtEndPosition && TeamBCrew.Any(c => c.Submarine == TeamBSub)) || (TeamASub != null && TeamASub.AtEndPosition && TeamBCrew.Any(c => c.Submarine == TeamASub)))
            {
                if (ADead && !BDead)
                {
                    //team B wins!
                    GameMain.GameSession.CrewManager.WinningTeam = 2;
                    if (GameMain.Server!=null) GameMain.Server.EndGame();
                }
            }

            if ((TeamASub != null && TeamASub.AtStartPosition && TeamACrew.Any(c => c.Submarine == TeamASub)) || (TeamBSub != null && TeamBSub.AtStartPosition && TeamACrew.Any(c => c.Submarine == TeamBSub)))
            {
                if (BDead && !ADead)
                {
                    //team A wins!
                    GameMain.GameSession.CrewManager.WinningTeam = 1;
                    if (GameMain.Server != null) GameMain.Server.EndGame();
                }
            }
        }

        public override void End()
        {
            bool ADead = TeamACrew.All(c => c.IsDead || c.IsUnconscious);
            bool BDead = TeamBCrew.All(c => c.IsDead || c.IsUnconscious);

            if (BDead && !ADead)
            {
                winner = Locations[0];
                loser = Locations[1];
            }
            if (ADead && !BDead)
            {
                winner = Locations[1];
                loser = Locations[0];
            }

            if ((TeamBSub != null && TeamBSub.AtEndPosition) || (TeamASub != null && TeamASub.AtEndPosition))
            {
                if (ADead && !BDead)
                {
                    //team B wins!
                    GiveReward();

                    completed = true;
                }
            }

            if ((TeamASub != null && TeamASub.AtStartPosition) || (TeamBSub != null && TeamBSub.AtStartPosition))
            {
                if (BDead && !ADead)
                {
                    //team A wins!
                    GiveReward();

                    completed = true;
                }
            }
        }
    }
}
