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

        public struct SubmarineVote
        {
            public Client VoteStarter;
            public SubmarineInfo Sub;
            public VoteType VoteType;
            public float Timer;
            public int DeliveryFee;
            public VoteState State;
        }

        public static SubmarineVote SubVote;

        private void StartSubmarineVote(IReadMessage inc, VoteType voteType, Client sender)
        {
            string subName = inc.ReadString();
            SubVote.Sub = SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.Name == subName);
            SubVote.DeliveryFee = voteType == VoteType.SwitchSub ? GameMain.GameSession.Map.DistanceToClosestLocationWithOutpost(GameMain.GameSession.Map.CurrentLocation, out Location endLocation) : 0;
            SubVote.VoteType = voteType;
            SubVote.State = VoteState.Started;
            SubVote.VoteStarter = sender;
            VoteRunning = true;
            sender.SetVote(voteType, 2);
        }

        public void StopSubmarineVote(bool passed)
        {
            VoteRunning = false;
            SubVote.State = passed ? VoteState.Passed : VoteState.Failed;

            GameMain.Server.UpdateVoteStatus();

            GameMain.NetworkMember.SubmarineVoteYesCount = GameMain.NetworkMember.SubmarineVoteNoCount = GameMain.NetworkMember.SubmarineVoteMax = 0;
            for (int i = 0; i < GameMain.NetworkMember.ConnectedClients.Count; i++)
            {
                GameMain.NetworkMember.ConnectedClients[i].SetVote(SubVote.VoteType, 0);
            }

            SubVote.Sub = null;
            SubVote.DeliveryFee = 0;
            SubVote.VoteType = VoteType.Unknown;
            SubVote.Timer = 0.0f;
            SubVote.State = VoteState.None;
            SubVote.VoteStarter = null;
        }

        public void ServerRead(IReadMessage inc, Client sender)
        {
            if (GameMain.Server == null || sender == null) { return; }

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
                    int equalityCheckVal = inc.ReadInt32();
                    string hash = equalityCheckVal > 0 ? string.Empty : inc.ReadString();
                    SubmarineInfo sub = equalityCheckVal > 0 ?
                        SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.Type == SubmarineType.Player && s.EqualityCheckVal == equalityCheckVal) :
                        SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.Type == SubmarineType.Player && s.MD5Hash.Hash == hash);
                    sender.SetVote(voteType, sub);
                    break;
                case VoteType.Mode:
                    string modeIdentifier = inc.ReadString();
                    GameModePreset mode = GameModePreset.List.Find(gm => gm.Identifier == modeIdentifier);
                    if (mode == null || !mode.Votable) { break; }
                    sender.SetVote(voteType, mode);
                    break;
                case VoteType.EndRound:
                    if (!sender.HasSpawned) { return; }
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
                        GameServer.Log(GameServer.ClientLogName(sender) + (ready ? " is ready to start the game." : " is not ready to start the game."), ServerLog.MessageType.ServerMessage);
                    }
                    break;

                case VoteType.PurchaseAndSwitchSub:
                case VoteType.PurchaseSub:
                case VoteType.SwitchSub:
                    bool startVote = inc.ReadBoolean();
                    if (startVote)
                    {
                        StartSubmarineVote(inc, voteType, sender);
                    }
                    else
                    {
                        sender.SetVote(voteType, (int)inc.ReadByte());
                    }

                    GameMain.Server.SubmarineVoteYesCount = GameMain.Server.ConnectedClients.Count(c => c.GetVote<int>(SubVote.VoteType) == 2);
                    GameMain.Server.SubmarineVoteNoCount = GameMain.Server.ConnectedClients.Count(c => c.GetVote<int>(SubVote.VoteType) == 1);
                    GameMain.Server.SubmarineVoteMax = GameMain.Server.ConnectedClients.Count(c => c.InGame);
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
                    msg.Write(((SubmarineInfo)vote.First).Name);
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

            msg.Write((byte)SubVote.State);
            if (SubVote.State != VoteState.None)
            {
                msg.Write((byte)SubVote.VoteType);

                if (SubVote.VoteType != VoteType.Unknown)
                {
                    var yesClients = GameMain.Server.ConnectedClients.FindAll(c => c.GetVote<int>(SubVote.VoteType) == 2);
                    msg.Write((byte)yesClients.Count);
                    foreach (Client c in yesClients)
                    {
                        msg.Write(c.ID);
                    }

                    var noClients = GameMain.Server.ConnectedClients.FindAll(c => c.GetVote<int>(SubVote.VoteType) == 1);
                    msg.Write((byte)noClients.Count);
                    foreach (Client c in noClients)
                    {
                        msg.Write(c.ID);
                    }

                    msg.Write((byte)GameMain.Server.SubmarineVoteMax);

                    switch (SubVote.State)
                    {
                        case VoteState.Started:
                            msg.Write(SubVote.Sub.Name);
                            msg.Write(SubVote.VoteStarter.ID);
                            msg.Write((byte)GameMain.Server.ServerSettings.SubmarineVoteTimeout);
                            break;
                        case VoteState.Running:
                            // Nothing specific
                            break;
                        case VoteState.Passed:
                        case VoteState.Failed:
                            msg.Write(SubVote.State == VoteState.Passed);
                            msg.Write(SubVote.Sub.Name);
                            if (SubVote.State == VoteState.Passed)
                            {
                                msg.Write((short)SubVote.DeliveryFee);
                            }
                            break;
                    }
                }
            }       

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
