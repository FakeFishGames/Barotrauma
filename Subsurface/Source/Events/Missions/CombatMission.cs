using System;
using Barotrauma;

using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
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
        string winner; string loser;

        public override string SuccessMessage
        {
            get { return successMessage.Replace("[loser]",loser).Replace("[winner]",winner); }
        }

        public CombatMission(XElement element)
            : base(element)
        {
            
        }

        public override bool AssignClientIDs(List<Client> clients)
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
                    randList[i].TeamID = 0;
                }
                else
                {
                    randList[i].TeamID = 1;
                }
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
            
            for (int i = 0; i < Character.CharacterList.Count; i++)
            {
                if (Character.CharacterList[i].TeamID==0)
                {
                    TeamACrew.Add(Character.CharacterList[i]);
                }
                else
                {
                    TeamBCrew.Add(Character.CharacterList[i]);
                }
            }
        }

        public override void Update(float deltaTime)
        {
            if (TeamACrew.Count == 0 && TeamBCrew.Count == 0)
            {
                for (int i = 0; i < Character.CharacterList.Count; i++)
                {
                    if (Character.CharacterList[i].TeamID == 0)
                    {
                        TeamACrew.Add(Character.CharacterList[i]);
                    }
                    else
                    {
                        TeamBCrew.Add(Character.CharacterList[i]);
                    }
                }
            }

            if (GameMain.Server != null)
            {
                GameMain.Server.AllowRespawn = false;
            }

            bool ADead = true;
            foreach (Character c in TeamACrew)
            {
                if (!c.IsDead)
                {
                    ADead = false; break;
                }
            }
            bool BDead = true;
            foreach (Character c in TeamBCrew)
            {
                if (!c.IsDead)
                {
                    BDead = false; break;
                }
            }

            if (BDead && !ADead)
            {
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
                winner = Locations[1];
                loser = Locations[0];
                if (state == 0)
                {
                    ShowMessage(0);
                    state = 1;
                }
            }

            if ((TeamBSub != null && TeamBSub.AtEndPosition) || (TeamASub != null && TeamASub.AtEndPosition))
            {
                if (ADead && !BDead)
                {
                    //team B wins!
                    if (GameMain.Server!=null) GameMain.Server.EndGame();
                }
            }

            if ((TeamASub != null && TeamASub.AtStartPosition) || (TeamBSub != null && TeamBSub.AtStartPosition))
            {
                if (BDead && !ADead)
                {
                    //team A wins!
                    if (GameMain.Server != null) GameMain.Server.EndGame();
                }
            }
        }

        public override void End()
        {
            bool ADead = true;
            foreach (Character c in TeamACrew)
            {
                if (!c.IsDead && !c.IsUnconscious)
                {
                    ADead = false; break;
                }
            }
            bool BDead = true;
            foreach (Character c in TeamBCrew)
            {
                if (!c.IsDead && !c.IsUnconscious)
                {
                    BDead = false; break;
                }
            }

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
