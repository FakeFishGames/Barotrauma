﻿using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Voting
    {
        public interface IVote
        {
            public Client VoteStarter { get; }
            public VoteType VoteType { get; }
            public float Timer { get; set; }

            public VoteState State { get; set; }

            public void Finish(Voting voting, bool passed);
        }

        public class SubmarineVote : IVote
        {
            public Client VoteStarter { get; }
            public VoteType VoteType { get; }
            public float Timer { get; set; }

            public VoteState State { get; set; }

            public SubmarineInfo Sub;
            public bool TransferItems;

            public SubmarineVote(Client starter, SubmarineInfo subInfo, bool transferItems, VoteType voteType)
            {
                Sub = subInfo;
                TransferItems = transferItems;
                VoteType = voteType;
                State = VoteState.Started;
                VoteStarter = starter;
            }

            public void Finish(Voting voting, bool passed)
            {
                if (passed)
                {
                    if (GameMain.Server != null && !GameMain.Server.TrySwitchSubmarine())
                    {
                        passed = false;
                        State = VoteState.Failed;
                    }
                }
                else
                {
                    voting.RegisterRejectedVote(this);
                }
                voting.StopSubmarineVote(passed);                
            }
        }

        public class TransferVote : IVote
        {
            public Client VoteStarter { get; }
            public VoteType VoteType { get; }
            public float Timer { get; set; }

            public VoteState State { get; set; }

            //null = bank
            public readonly Client From, To;
            public readonly int TransferAmount;

            public TransferVote(Client starter, Client from, int transferAmount, Client to)
            {
                VoteStarter = starter;
                From = from;
                To = to;
                TransferAmount = transferAmount;
                State = VoteState.Started;
                VoteType = VoteType.TransferMoney;
            }

            public void Finish(Voting voting, bool passed)
            {
                if (passed)
                {
                    Wallet fromWallet = From == null ? (GameMain.GameSession.GameMode as MultiPlayerCampaign)?.Bank : From.Character?.Wallet;
                    if (fromWallet != null && fromWallet.TryDeduct(TransferAmount))
                    {
                        Wallet toWallet = To == null ? (GameMain.GameSession.GameMode as MultiPlayerCampaign)?.Bank : To.Character?.Wallet;
                        toWallet?.Give(TransferAmount);
                    }
                }
                else
                {
                    voting.RegisterRejectedVote(this);
                }
                voting.StopMoneyTransferVote(passed);
            }
        }

        public static IVote ActiveVote;

        private static readonly Queue<IVote> pendingVotes = new Queue<IVote>();

        private readonly TimeSpan rejectedVoteCooldown = new TimeSpan(0, 1, 0);

        private readonly Dictionary<Client, (VoteType voteType, DateTime time)> rejectedVoteTimes = new Dictionary<Client, (VoteType voteType, DateTime time)>();

        private void StartSubmarineVote(SubmarineInfo subInfo, bool transferItems, VoteType voteType, Client sender)
        {
            var subVote = new SubmarineVote(
                sender,
                subInfo,
                transferItems,
                voteType);
            StartOrEnqueueVote(subVote);
            GameMain.Server.UpdateVoteStatus(checkActiveVote: false);
        }

        public void StopSubmarineVote(bool passed)
        {
            if (ActiveVote is not SubmarineVote) { return; }
            StopActiveVote(passed);
        }

        public void StopMoneyTransferVote(bool passed)
        {
            if (ActiveVote is not TransferVote) { return; }
            StopActiveVote(passed);
        }

        public void StopActiveVote(bool passed)
        {
            ActiveVote.State = passed ? VoteState.Passed : VoteState.Failed;
            GameMain.Server.UpdateVoteStatus(checkActiveVote: false);

            for (int i = 0; i < GameMain.NetworkMember.ConnectedClients.Count; i++)
            {
                GameMain.NetworkMember.ConnectedClients[i].SetVote(ActiveVote.VoteType, 0);
            }

            ActiveVote = null;
            if (pendingVotes.Any())
            {
                ActiveVote = pendingVotes.Dequeue();
                ActiveVote.VoteStarter?.SetVote(ActiveVote.VoteType, 2);
            }
        }

        public void StartTransferVote(Client starter, Client from, int transferAmount, Client to)
        {
            if (ShouldRejectVote(starter, VoteType.TransferMoney))
            {
                return;
            }
            StartOrEnqueueVote(new TransferVote(starter, from, transferAmount, to));
            GameMain.Server.UpdateVoteStatus(checkActiveVote: false);
        }

        private static void StartOrEnqueueVote(IVote vote)
        {
            if (ActiveVote == null)
            {
                ActiveVote = vote;
            }
            else
            {
                pendingVotes.Enqueue(vote);
            }
        }

        private bool ShouldRejectVote(Client sender, VoteType voteType)
        {
            if (rejectedVoteTimes.ContainsKey(sender))
            {
                TimeSpan remainingCooldown = (rejectedVoteTimes[sender].time + rejectedVoteCooldown) - DateTime.Now;
                if (rejectedVoteTimes[sender].voteType == voteType &&
                    remainingCooldown.TotalSeconds > 0)
                {
                    GameMain.Server.SendDirectChatMessage(
                        TextManager.FormatServerMessage("voterejectedpleasewait", ("[time]", ((int)remainingCooldown.TotalSeconds).ToString())),
                        sender, ChatMessageType.ServerMessageBox);
                    return true;
                }
            }
            return false;
        }

        protected void RegisterRejectedVote(IVote vote)
        {
            if (vote.VoteStarter != null)
            {
                rejectedVoteTimes[vote.VoteStarter] = (vote.VoteType, DateTime.Now);
            }
        }

        public void Update(float deltaTime)
        {
            if (ActiveVote == null) { return; }
            
            ActiveVote.Timer += deltaTime;

            var inGameClients = GameMain.Server.ConnectedClients.Where(c => c.InGame);
            if (ActiveVote.Timer >= GameMain.NetworkMember.ServerSettings.VoteTimeout || inGameClients.Count() == 1)
            {
                var eligibleClients = inGameClients.Where(c => c != ActiveVote.VoteStarter);

                // Do not take unanswered into account for total
                int yes = eligibleClients.Count(c => c.GetVote<int>(ActiveVote.VoteType) == 2);
                int no = eligibleClients.Count(c => c.GetVote<int>(ActiveVote.VoteType) == 1);
                int total = yes + no;

                bool passed = false;
                //total can be zero if the client who initiated the vote has left
                if (total > 0)
                {
                    passed = 
                        yes / (float)total >= GameMain.NetworkMember.ServerSettings.VoteRequiredRatio ||
                        inGameClients.Count() == 1;
                }
                ActiveVote.Finish(this, passed);
            }
        }

        public static void ResetVotes(IEnumerable<Client> connectedClients, bool resetKickVotes)
        {
            foreach (Client client in connectedClients)
            {
                client.ResetVotes(resetKickVotes);
            }
        }

        public void ServerRead(IReadMessage inc, Client sender, DoSProtection dosProtection)
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
                        SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.Type == SubmarineType.Player && s.MD5Hash.StringRepresentation == hash);
                    sender.SetVote(voteType, sub);
                    break;
                case VoteType.Mode:
                    string modeIdentifier = inc.ReadString();
                    GameModePreset mode = GameModePreset.List.Find(gm => gm.Identifier == modeIdentifier);
                    if (mode == null || !mode.Votable) { break; }
                    var prevHighestVoted = HighestVoted<GameModePreset>(VoteType.Mode, GameMain.Server.ConnectedClients);
                    sender.SetVote(voteType, mode);
                    var newHighestVoted = HighestVoted<GameModePreset>(VoteType.Mode, GameMain.Server.ConnectedClients);
                    if (prevHighestVoted != newHighestVoted)
                    {
                        GameMain.NetLobbyScreen.SelectedModeIdentifier = mode.Identifier;
                        GameMain.NetLobbyScreen.LastUpdateID++;
                    }
                    break;
                case VoteType.EndRound:
                    if (!sender.HasSpawned) { return; }
                    sender.SetVote(voteType, inc.ReadBoolean());
                    break;
                case VoteType.Kick:
                    byte kickedClientID = inc.ReadByte();
                    if ((DateTime.Now - sender.JoinTime).TotalSeconds < GameMain.Server.ServerSettings.DisallowKickVoteTime)
                    {
                        GameMain.Server.SendDirectChatMessage($"ServerMessage.kickvotedisallowed", sender);
                    }
                    else
                    {
                        Client kicked = GameMain.Server.ConnectedClients.Find(c => c.SessionId == kickedClientID);
                        if (kicked != null && kicked.Connection != GameMain.Server.OwnerConnection && !kicked.HasKickVoteFrom(sender))
                        {
                            kicked.AddKickVote(sender);
                            Client.UpdateKickVotes(GameMain.Server.ConnectedClients);
                            GameMain.Server.SendChatMessage($"ServerMessage.HasVotedToKick~[initiator]={sender.Name}~[target]={kicked.Name}", ChatMessageType.Server, null);
                        }
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
                case VoteType.TransferMoney:
                    bool startVote = inc.ReadBoolean();
                    if (startVote)
                    {
                        if (voteType == VoteType.TransferMoney)
                        {
                            int amount = inc.ReadInt32();
                            int fromClientId = inc.ReadByte();
                            int toClientId = inc.ReadByte();
                            if (!ShouldRejectVote(sender, voteType))
                            {
                                pendingVotes.Enqueue(new TransferVote(sender,
                                    GameMain.Server.ConnectedClients.Find(c => c.SessionId == fromClientId),
                                    amount,
                                    GameMain.Server.ConnectedClients.Find(c => c.SessionId == toClientId)));
                            }
                        }
                        else
                        {
                            string subName = inc.ReadString();
                            SubmarineInfo subInfo = GameMain.GameSession.OwnedSubmarines.FirstOrDefault(s => s.Name == subName) ?? SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.Name == subName);
                            bool transferItems = inc.ReadBoolean();
                            if (!ShouldRejectVote(sender, voteType))
                            {
                                if (GameMain.GameSession?.Campaign is MultiPlayerCampaign campaign && 
                                    (campaign.CanPurchaseSub(subInfo, sender) || GameMain.GameSession.IsSubmarineOwned(subInfo)))
                                {
                                    StartSubmarineVote(subInfo, transferItems, voteType, sender);
                                }
                            }
                        }
                    }
                    else
                    {
                        sender.SetVote(voteType, (int)inc.ReadByte());
                    }
                    break;
            }

            inc.ReadPadBits();

            using (dosProtection.Pause(sender))
            {
                GameMain.Server.UpdateVoteStatus();
            }
        }

        public void ServerWrite(IWriteMessage msg)
        {
            if (GameMain.Server == null) { return; }

            msg.WriteBoolean(GameMain.Server.ServerSettings.AllowSubVoting);
            if (GameMain.Server.ServerSettings.AllowSubVoting)
            {
                IReadOnlyDictionary<SubmarineInfo, int> voteList = GetVoteCounts<SubmarineInfo>(VoteType.Sub, GameMain.Server.ConnectedClients);
                msg.WriteByte((byte)voteList.Count);
                foreach (KeyValuePair<SubmarineInfo, int> vote in voteList)
                {
                    msg.WriteByte((byte)vote.Value);
                    msg.WriteString(vote.Key.Name);
                }
            }
            msg.WriteBoolean(GameMain.Server.ServerSettings.AllowModeVoting);
            if (GameMain.Server.ServerSettings.AllowModeVoting)
            {
                IReadOnlyDictionary<GameModePreset, int> voteList = GetVoteCounts<GameModePreset>(VoteType.Mode, GameMain.Server.ConnectedClients);
                msg.WriteByte((byte)voteList.Count);
                foreach (KeyValuePair<GameModePreset, int> vote in voteList)
                {
                    msg.WriteByte((byte)vote.Value);
                    msg.WriteIdentifier(vote.Key.Identifier);
                }
            }
            msg.WriteBoolean(GameMain.Server.ServerSettings.AllowEndVoting);
            if (GameMain.Server.ServerSettings.AllowEndVoting)
            {
                msg.WriteByte((byte)GameMain.Server.ConnectedClients.Count(c => c.HasSpawned && c.GetVote<bool>(VoteType.EndRound)));
                msg.WriteByte((byte)GameMain.Server.ConnectedClients.Count(c => c.HasSpawned));
            }

            msg.WriteBoolean(GameMain.Server.ServerSettings.AllowVoteKick);

            msg.WriteByte((byte)(ActiveVote?.State ?? VoteState.None));
            if (ActiveVote != null)
            {
                msg.WriteByte((byte)ActiveVote.VoteType);
                if (ActiveVote.State != VoteState.None && ActiveVote.VoteType != VoteType.Unknown)
                {
                    var eligibleClients = GameMain.Server.ConnectedClients.Where(c => c.InGame && c != ActiveVote.VoteStarter);

                    var yesClients = eligibleClients.Where(c => c.GetVote<int>(ActiveVote.VoteType) == 2);
                    msg.WriteByte((byte)yesClients.Count());
                    foreach (Client c in yesClients)
                    {
                        msg.WriteByte(c.SessionId);
                    }

                    var noClients = eligibleClients.Where(c => c.GetVote<int>(ActiveVote.VoteType) == 1);
                    msg.WriteByte((byte)noClients.Count());
                    foreach (Client c in noClients)
                    {
                        msg.WriteByte(c.SessionId);
                    }

                    msg.WriteByte((byte)eligibleClients.Count());

                    switch (ActiveVote.State)
                    {
                        case VoteState.Started:
                            msg.WriteByte(ActiveVote.VoteStarter.SessionId);
                            msg.WriteByte((byte)GameMain.Server.ServerSettings.VoteTimeout);

                            switch (ActiveVote.VoteType)
                            {
                                case VoteType.PurchaseSub:
                                case VoteType.PurchaseAndSwitchSub:
                                case VoteType.SwitchSub:
                                    SubmarineVote vote = ActiveVote as SubmarineVote;
                                    msg.WriteString(vote.Sub.Name);
                                    msg.WriteBoolean(vote.TransferItems);
                                    break;
                                case VoteType.TransferMoney:
                                    var transferVote = (ActiveVote as TransferVote);
                                    msg.WriteByte(transferVote.From?.SessionId ?? 0);
                                    msg.WriteByte(transferVote.To?.SessionId ?? 0);
                                    msg.WriteInt32(transferVote.TransferAmount);
                                    break;
                            }

                            break;
                        case VoteState.Running:
                            // Nothing specific
                            break;
                        case VoteState.Passed:
                        case VoteState.Failed:
                            msg.WriteBoolean(ActiveVote.State == VoteState.Passed);
                            switch (ActiveVote.VoteType)
                            {
                                case VoteType.PurchaseSub:
                                case VoteType.PurchaseAndSwitchSub:
                                case VoteType.SwitchSub:
                                    var subVote = ActiveVote as SubmarineVote;
                                    msg.WriteString(subVote.Sub.Name);
                                    msg.WriteBoolean(subVote.TransferItems);
                                    break;
                            }
                            break;
                    }                    
                }
            }            

            var readyClients = GameMain.Server.ConnectedClients.Where(c => c.GetVote<bool>(VoteType.StartRound));
            msg.WriteByte((byte)readyClients.Count());
            foreach (Client c in readyClients)
            {
                msg.WriteByte(c.SessionId);
            }

            msg.WritePadBits();
        }
    }
}
