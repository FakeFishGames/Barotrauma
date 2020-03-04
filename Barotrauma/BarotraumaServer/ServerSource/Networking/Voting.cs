using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Voting
    {
        public bool AllowSubVoting
        {
            get { return allowSubVoting; }
            set {  allowSubVoting = value; }
        }
        public bool AllowModeVoting
        {
            get { return allowModeVoting; }
            set { allowModeVoting = value; }
        }

        public void ServerRead(IReadMessage inc, Client sender)
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
                    break;

                case VoteType.Mode:
                    string modeIdentifier = inc.ReadString();
                    GameModePreset mode = GameModePreset.List.Find(gm => gm.Identifier == modeIdentifier);
                    if (!mode.Votable) break;

                    sender.SetVote(voteType, mode);
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
                    if (kicked != null && kicked.Connection != GameMain.Server.OwnerConnection && !kicked.HasKickVoteFrom(sender))
                    {
                        kicked.AddKickVote(sender);
                        Client.UpdateKickVotes(GameMain.Server.ConnectedClients);
                        GameMain.Server.SendChatMessage($"ServerMessage.HasVotedToKick~[initiator]={sender.Name}~[target]={kicked.Name}", ChatMessageType.Server, null);
                    }

                    break;
                case VoteType.StartRound:
                    bool ready = inc.ReadBoolean();
                    if (ready != sender.GetVote<bool>(VoteType.StartRound))
                    {
                        sender.SetVote(VoteType.StartRound, ready);
                        GameServer.Log(sender.Name + (ready ? " is ready to start the game." : " is not ready to start the game."), ServerLog.MessageType.ServerMessage);
                    }

                    break;
            }

            inc.ReadPadBits();

            GameMain.Server.UpdateVoteStatus();
        }

        public void ServerWrite(IWriteMessage msg)
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
                msg.Write((byte)GameMain.Server.ConnectedClients.Count(c => c.HasSpawned && c.GetVote<bool>(VoteType.EndRound)));
                msg.Write((byte)GameMain.Server.ConnectedClients.Count(c => c.HasSpawned));
            }

            msg.Write(AllowVoteKick);

            var readyClients = GameMain.Server.ConnectedClients.FindAll(c => c.GetVote<bool>(VoteType.StartRound));
            msg.Write((byte)readyClients.Count);
            foreach (Client c in readyClients)
            {
                msg.Write(c.ID);
            }

            msg.WritePadBits();
        }
    }
}
