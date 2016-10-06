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
        private Submarine[] subs;
        private List<Character>[] crews;

        private int state = 0;
        private int winner = -1;

        public override string SuccessMessage
        {
            get 
            {
                if (winner == -1) return "";

                return successMessage
                    .Replace("[loser]", Locations[1 - winner]
                    .Replace("[winner]", Locations[winner])); 
            }
        }

        public CombatMission(XElement element)
            : base(element)
        {
        }

        public override bool AssignTeamIDs(List<Client> clients, out int hostTeam)
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
                hostTeam = Rand.Range(1, 2);
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
            if (GameMain.Server != null)
            {
                GameMain.Server.AllowRespawn = false;
            }

            if (GameMain.NetworkMember == null)
            {
                DebugConsole.ThrowError("Combat missions cannot be played in the single player mode.");
                return;
            }

            Items.Components.Radar.StartMarker = Locations[0];
            Items.Components.Radar.EndMarker = Locations[1];

            subs = new Submarine[] { Submarine.MainSubs[0], Submarine.MainSubs[1] };
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
            if (crews[0].Count == 0 && crews[1].Count == 0)
            {
                if (GameMain.Server != null)
                {
                    GameMain.Server.AllowRespawn = false;
                }

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
            }
            
            if (state == 0)
            {
                bool[] teamDead = 
                { 
                    crews[0].All(c => c.IsDead || c.IsUnconscious),
                    crews[1].All(c => c.IsDead || c.IsUnconscious)
                };

                for (int i = 0; i < teamDead.Length; i++)
                {
                    if (!teamDead[i] && teamDead[1-i])
                    {
                        //make sure nobody in the other team can be revived because that would be pretty weird
                        crews[1-i].ForEach(c => { if (!c.IsDead) c.Kill(CauseOfDeath.Damage); });

                        winner = i;

                        ShowMessage(i);
                        state = 1;
                        break;
                    }
                }
            }
            else
            {
                if (subs[winner] != null && 
                    (winner == 0 && subs[winner].AtStartPosition) || (winner == 1 && subs[winner].AtEndPosition) &&
                    crews[winner].Any(c => !c.IsDead && c.Submarine == subs[winner]))
                {
                    GameMain.GameSession.CrewManager.WinningTeam = winner+1;
                    if (GameMain.Server != null) GameMain.Server.EndGame();
                }
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
