#nullable enable
using Barotrauma.Extensions;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class CombatMission
    {
        class KillCount
        {
            public readonly Character Victim;
            public readonly Client? VictimClient;
            public readonly Character? Killer;
            public readonly Client? KillerClient;
            public KillCount(Character victim, Character? killer)
            {
                Victim = victim;
                VictimClient = GameMain.Server.ConnectedClients.FirstOrDefault(c => victim.IsClientOwner(c));
                Killer = killer;
                if (killer != null)
                {
                    KillerClient = GameMain.Server.ConnectedClients.FirstOrDefault(c => killer.IsClientOwner(c));
                }
            }
        }

        const float RoundEndDuration = 5.0f;

        private readonly bool[] teamDead = new bool[2];

        /// <summary>
        /// Lists of characters currently alive in the teams
        /// </summary>
        private List<Character>[] crews;

        /// <summary>
        /// List of all kills (of the characters in either team) during the round
        /// </summary>
        private readonly List<KillCount> kills = new List<KillCount>();

        private float roundEndTimer;

        private float timeInTargetSubmarineTimer;

        public override LocalizedString Description
        {
            get
            {
                if (descriptions == null) { return ""; }
                
                //non-team-specific description
                return descriptions[0];
            }
        }

        protected override void UpdateMissionSpecific(float deltaTime)
        {
            CheckTeamCharacters();

            if (state == 0)
            {
                CheckWinCondition(deltaTime);
                for (int i = 0; i < teamDead.Length; i++)
                {
                    if (!teamDead[i] && teamDead[1 - i])
                    {
                        SetWinningTeam(i);
                        break;
                    }
                }
            }
            else
            {
                roundEndTimer -= deltaTime;
                if (roundEndTimer > 0.0f) { return; }
                
                if (teamDead[0] && teamDead[1])
                {
                    GameMain.GameSession.WinningTeam = CharacterTeamType.None;
                    GameMain.Server?.EndGame();
                }
                else if (GameMain.GameSession.WinningTeam != CharacterTeamType.None)
                {
                    GameMain.Server.EndGame();
                }                
            }
        }

        private void CheckTeamCharacters()
        {
            for (int i = 0; i < crews.Length; i++)
            {
                foreach (var character in crews[i])
                {
                    if (character.IsDead)
                    {
                        AddKill(character);
                    }
                }
            }

            crews[0].Clear();
            crews[1].Clear();
            foreach (Character character in Character.CharacterList)
            {
                if (character.IsDead) { continue; }
                if (character.TeamID == CharacterTeamType.Team1)
                {
                    crews[0].Add(character);
                }
                else if (character.TeamID == CharacterTeamType.Team2)
                {
                    crews[1].Add(character);
                }
                if (character.IsBot && character.AIController is HumanAIController humanAi)
                {
                    if (!humanAi.ObjectiveManager.HasOrder<AIObjectiveFightIntruders>(o => o.TargetCharactersInOtherSubs) &&
                        OrderPrefab.Prefabs.TryGet(Tags.AssaultEnemyOrder, out OrderPrefab? assaultOrder))
                    {
                        character.SetOrder(assaultOrder.CreateInstance(
                            OrderPrefab.OrderTargetType.Entity, orderGiver: null).WithManualPriority(CharacterInfo.HighestManualOrderPriority), 
                            isNewOrder: true, speak: false);
                    }
                }
            }
        }

        private void CheckWinCondition(float deltaTime)
        {
            switch (winCondition)
            {
                case WinCondition.LastManStanding:
                    if (crews[0].Count == 0 || crews[1].Count == 0)
                    {
                        //if there are no characters in either crew, end the round
                        teamDead[0] = teamDead[1] = true;
                        state = 1;
                    }
                    else
                    {
                        teamDead[0] = crews[0].All(c => c.IsDead || c.IsIncapacitated);
                        teamDead[1] = crews[1].All(c => c.IsDead || c.IsIncapacitated);
                        if (teamDead[0] && teamDead[1]) { state = 1; }
                    }
                    break;
                case WinCondition.KillCount:
                    //no need to do anything, kills are counted in AddKill
                    break;
                case WinCondition.ControlSubmarine:
                    CheckTargetSubmarineControl(deltaTime);
                    break;
            }
            CheckScore();
        }

        private void CheckScore()
        {
            for (int i = 0; i < crews.Length; i++)
            {
                if (Scores[i] >= WinScore)
                {
                    SetWinningTeam(i);
                    break;
                }
            }
        }

        private void CheckTargetSubmarineControl(float deltaTime)
        {
            if (targetSubmarine == null) { return; }

            //score updates at 1 second intervals, so the score represents the time in seconds
            timeInTargetSubmarineTimer += deltaTime;
            if (timeInTargetSubmarineTimer < 1.0f)
            {
                return;
            }
            timeInTargetSubmarineTimer = 0.0f;

            bool crew1InSubmarine = crews[0].Any(c => c.Submarine == targetSubmarine);
            bool crew2InSubmarine = crews[1].Any(c => c.Submarine == targetSubmarine);

            for (int i = 0; i < crews.Length; i++)
            {
                if (crews[i].Any(c => c.Submarine == targetSubmarine) &&
                    crews[1 - i].None(c => c.Submarine == targetSubmarine))
                {
                    Scores[i]++;
                    GameMain.Server?.UpdateMissionState(this);
                }
            }
        }

        public void AddToScore(CharacterTeamType team, int amount)
        {
            if (!HasWinScore) { return; }
            int index;
            switch (team)
            {
                case CharacterTeamType.Team1:
                    index = 0;
                    break;
                case CharacterTeamType.Team2:
                    index = 1;
                    break;
                default:
                    DebugConsole.AddSafeError($"Attempted to increase the score of an invalid team ({team}).");
                    return;
            }
            Scores[index] = MathHelper.Clamp(Scores[index] + amount, 0, WinScore);            
            GameMain.Server?.UpdateMissionState(this);
        }

        private void AddKill(Character character)
        {
            kills.Add(new KillCount(character, character.CauseOfDeath?.Killer));
            if (winCondition == WinCondition.KillCount)
            {
                Scores[character.TeamID == CharacterTeamType.Team1 ? 1 : 0] += PointsPerKill;
            }
            GameMain.Server?.UpdateMissionState(this);
        }

        private void SetWinningTeam(int teamIndex)
        {
            //state 1 = team 1 won, 2 = team 2 won
            State = teamIndex + 1;
            GameMain.GameSession.WinningTeam = teamIndex == 0 ? CharacterTeamType.Team1 : CharacterTeamType.Team2;
        }

        public override void ServerWrite(IWriteMessage msg)
        {
            base.ServerWrite(msg);
            msg.WriteUInt16((ushort)Scores[0]);
            msg.WriteUInt16((ushort)Scores[1]);

            IEnumerable<Client> uniqueClients = kills
                .Select(k => k.VictimClient)
                .Union(kills.Select(k => k.KillerClient))
                .NotNull();
            msg.WriteVariableUInt32((uint)uniqueClients.Count());
            foreach (Client client in uniqueClients)
            {
                msg.WriteByte(client.SessionId);
                msg.WriteVariableUInt32((uint)kills.Count(k => k.VictimClient == client));
                msg.WriteVariableUInt32((uint)kills.Count(k => k.KillerClient == client));
            }

            IEnumerable<CharacterInfo> uniqueBots = kills
                .Select(k => k.Killer)
                .Union(kills.Select(k => k.Victim))
                .NotNull()
                .Where(c => c.Info != null && c.IsBot)
                .Select(c => c.Info);
            msg.WriteVariableUInt32((uint)uniqueBots.Count());
            foreach (CharacterInfo botInfo in uniqueBots)
            {
                msg.WriteUInt16(botInfo.ID);
                msg.WriteVariableUInt32((uint)kills.Count(k => k.Victim?.Info == botInfo));
                msg.WriteVariableUInt32((uint)kills.Count(k => k.Killer?.Info == botInfo));
            }
        }
    }
}
