using Barotrauma.Networking;
using Lidgren.Network;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Voting
    {
        private bool allowSubVoting, allowModeVoting;

        public bool AllowVoteKick = true;

        public bool AllowEndVoting = true;

        private List<Pair<object, int>> GetVoteList(VoteType voteType, List<Client> voters)
        {
            List<Pair<object, int>> voteList = new List<Pair<object, int>>();

            foreach (Client voter in voters)
            {
                object vote = voter.GetVote<object>(voteType);
                if (vote == null) continue;

                var existingVotable = voteList.Find(v => v.First == vote || v.First.Equals(vote));
                if (existingVotable == null)
                {
                    voteList.Add(new Pair<object, int>(vote, 1));
                }
                else
                {
                    existingVotable.Second++;
                }
            }
            return voteList;
        }

        public T HighestVoted<T>(VoteType voteType, List<Client> voters)
        {
            if (voteType == VoteType.Sub && !AllowSubVoting) return default(T);
            if (voteType == VoteType.Mode && !AllowModeVoting) return default(T);

            List<Pair<object, int>> voteList = GetVoteList(voteType,voters);

            T selected = default(T);
            int highestVotes = 0;
            foreach (Pair<object, int> votable in voteList)
            {
                if (selected == null || votable.Second > highestVotes)
                {
                    highestVotes = votable.Second;
                    selected = (T)votable.First;
                }
            }

            return selected;            
        }

        public void ResetVotes(List<Client> connectedClients)
        {
            foreach (Client client in connectedClients)
            {
                client.ResetVotes();
            }

            GameMain.NetworkMember.EndVoteCount = 0;
            GameMain.NetworkMember.EndVoteMax = 0;

#if CLIENT
            UpdateVoteTexts(connectedClients, VoteType.Mode);
            UpdateVoteTexts(connectedClients, VoteType.Sub);
#endif
        }

        public void ServerRead(NetIncomingMessage inc, Client sender)
        {
            if (GameMain.Server == null || sender == null) return;

            byte voteTypeByte = inc.ReadByte();
            VoteType voteType = VoteType.Unknown;
            try
            {
                voteType = (VoteType)voteTypeByte;
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to cast vote type \"" + voteTypeByte + "\"", e);
                return;
            }

            switch (voteType)
            {
                case VoteType.Sub:
                    string subName = inc.ReadString();
                    Submarine sub = Submarine.SavedSubmarines.FirstOrDefault(s => s.Name == subName);
                    sender.SetVote(voteType, sub);
#if CLIENT
                    UpdateVoteTexts(GameMain.Server.ConnectedClients, voteType);
#endif
                    break;

                case VoteType.Mode:
                    string modeIdentifier = inc.ReadString();
                    GameModePreset mode = GameModePreset.List.Find(gm => gm.Identifier == modeIdentifier);
                    if (!mode.Votable) break;

                    sender.SetVote(voteType, mode);
#if CLIENT
                    UpdateVoteTexts(GameMain.Server.ConnectedClients, voteType);
#endif
                    break;
                case VoteType.EndRound:
                    if (!sender.HasSpawned) return;
                    sender.SetVote(voteType, inc.ReadBoolean());

                    GameMain.NetworkMember.EndVoteCount = GameMain.Server.ConnectedClients.Count(c => c.HasSpawned && c.GetVote<bool>(VoteType.EndRound));
                    GameMain.NetworkMember.EndVoteMax = GameMain.Server.ConnectedClients.Count(c => c.HasSpawned);

                    break;
                case VoteType.Kick:
                    byte kickedClientID = inc.ReadByte();

                    Client kicked = GameMain.Server.ConnectedClients.Find(c => c.ID == kickedClientID);
                    if (kicked != null && !kicked.HasKickVoteFrom(sender))
                    {
                        kicked.AddKickVote(sender);
                        Client.UpdateKickVotes(GameMain.Server.ConnectedClients);

                        GameMain.Server.SendChatMessage(sender.Name + " has voted to kick " + kicked.Name, ChatMessageType.Server, null);
                    }

                    break;
                case VoteType.StartRound:
                    bool ready = inc.ReadBoolean();
                    if (ready != sender.GetVote<bool>(VoteType.StartRound))
                    {
                        sender.SetVote(VoteType.StartRound, ready);
                        GameServer.Log(sender.Name + (ready ? " is ready to start the game." : " is not ready to start the game."), ServerLog.MessageType.ServerMessage);
#if CLIENT
                        UpdateVoteTexts(GameMain.Server.ConnectedClients, voteType);
#endif
                    }

                    break;
            }

            inc.ReadPadBits();

            GameMain.Server.UpdateVoteStatus();
        }

        public void ServerWrite(NetBuffer msg)
        {
            if (GameMain.Server == null) return;

            msg.Write(allowSubVoting);
            if (allowSubVoting)
            {
                List<Pair<object, int>> voteList = GetVoteList(VoteType.Sub, GameMain.Server.ConnectedClients);
                msg.Write((byte)voteList.Count);
                foreach (Pair<object, int> vote in voteList)
                {
                    msg.Write((byte)vote.Second);
                    msg.Write(((Submarine)vote.First).Name);
                }
            }
            msg.Write(AllowModeVoting);
            if (allowModeVoting)
            {
                List<Pair<object, int>> voteList = GetVoteList(VoteType.Mode, GameMain.Server.ConnectedClients);
                msg.Write((byte)voteList.Count);
                foreach (Pair<object, int> vote in voteList)
                {
                    msg.Write((byte)vote.Second);
                    msg.Write(((GameModePreset)vote.First).Identifier);
                }
            }
            msg.Write(AllowEndVoting);
            if (AllowEndVoting)
            {
                msg.Write((byte)GameMain.Server.ConnectedClients.Count(v => v.GetVote<bool>(VoteType.EndRound)));
                msg.Write((byte)GameMain.Server.ConnectedClients.Count);
            }

            msg.Write(AllowVoteKick);

            var readyClients =  GameMain.Server.ConnectedClients.FindAll(c => c.GetVote<bool>(VoteType.StartRound));
            msg.Write((byte)readyClients.Count);
            foreach (Client c in readyClients)
            {
                msg.Write(c.ID);
            }

            msg.WritePadBits();
        }

    }
}
