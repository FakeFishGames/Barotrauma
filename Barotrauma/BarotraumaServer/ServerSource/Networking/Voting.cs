using Barotrauma.Networking;
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
            public int DeliveryFee;

            public SubmarineVote(Client starter, SubmarineInfo subInfo, bool transferItems, int deliveryFee, VoteType voteType)
            {
                Sub = subInfo;
                TransferItems = transferItems;
                DeliveryFee = deliveryFee;
                VoteType = voteType;
                State = VoteState.Started;
                VoteStarter = starter;
            }

            public void Finish(Voting voting, bool passed)
            {
                if (passed)
                {
                    GameMain.Server?.SwitchSubmarine();
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
                    if (fromWallet.TryDeduct(TransferAmount))
                    {
                        Wallet toWallet = To == null ? (GameMain.GameSession.GameMode as MultiPlayerCampaign)?.Bank : To.Character?.Wallet;
                        toWallet.Give(TransferAmount);
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
                voteType == VoteType.SwitchSub ? GameMain.GameSession.Map.DistanceToClosestLocationWithOutpost(GameMain.GameSession.Map.CurrentLocation, out Location endLocation) : 0,
                voteType);
            StartOrEnqueueVote(subVote);
            GameMain.Server.UpdateVoteStatus(checkActiveVote: false);
        }

        public void StopSubmarineVote(bool passed)
        {
            if (!(ActiveVote is SubmarineVote)) { return; }
            StopActiveVote(passed);
        }

        public void StopMoneyTransferVote(bool passed)
        {
            if (!(ActiveVote is TransferVote)) { return; }
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

        private void StartOrEnqueueVote(IVote vote)
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

            if (ActiveVote.Timer >= GameMain.NetworkMember.ServerSettings.VoteTimeout)
            {
                var inGameClients = GameMain.Server.ConnectedClients.Where(c => c.InGame);
                var eligibleClients = inGameClients.Where(c => c != ActiveVote.VoteStarter);

                // Do not take unanswered into account for total
                int yes = eligibleClients.Count(c => c.GetVote<int>(ActiveVote.VoteType) == 2);
                int no = eligibleClients.Count(c => c.GetVote<int>(ActiveVote.VoteType) == 1);
                int total = Math.Max(yes + no, 1);

                bool passed = 
                    yes / (float)total >= GameMain.NetworkMember.ServerSettings.VoteRequiredRatio || 
                    inGameClients.Count() == 1; 

                ActiveVote.Finish(this, passed);
            }
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
                        SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.Type == SubmarineType.Player && s.MD5Hash.StringRepresentation == hash);
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
                                    GameMain.Server.ConnectedClients.Find(c => c.ID == fromClientId),
                                    amount,
                                    GameMain.Server.ConnectedClients.Find(c => c.ID == toClientId)));
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

            GameMain.Server.UpdateVoteStatus();
        }

        public void ServerWrite(IWriteMessage msg)
        {
            if (GameMain.Server == null) { return; }

            msg.Write(GameMain.Server.ServerSettings.AllowSubVoting);
            if (GameMain.Server.ServerSettings.AllowSubVoting)
            {
                IReadOnlyDictionary<SubmarineInfo, int> voteList = GetVoteCounts<SubmarineInfo>(VoteType.Sub, GameMain.Server.ConnectedClients);
                msg.Write((byte)voteList.Count);
                foreach (KeyValuePair<SubmarineInfo, int> vote in voteList)
                {
                    msg.Write((byte)vote.Value);
                    msg.Write(vote.Key.Name);
                }
            }
            msg.Write(GameMain.Server.ServerSettings.AllowModeVoting);
            if (GameMain.Server.ServerSettings.AllowModeVoting)
            {
                IReadOnlyDictionary<GameModePreset, int> voteList = GetVoteCounts<GameModePreset>(VoteType.Mode, GameMain.Server.ConnectedClients);
                msg.Write((byte)voteList.Count);
                foreach (KeyValuePair<GameModePreset, int> vote in voteList)
                {
                    msg.Write((byte)vote.Value);
                    msg.Write(vote.Key.Identifier);
                }
            }
            msg.Write(GameMain.Server.ServerSettings.AllowEndVoting);
            if (GameMain.Server.ServerSettings.AllowEndVoting)
            {
                msg.Write((byte)GameMain.Server.ConnectedClients.Count(c => c.HasSpawned && c.GetVote<bool>(VoteType.EndRound)));
                msg.Write((byte)GameMain.Server.ConnectedClients.Count(c => c.HasSpawned));
            }

            msg.Write(GameMain.Server.ServerSettings.AllowVoteKick);

            msg.Write((byte)(ActiveVote?.State ?? VoteState.None));
            if (ActiveVote != null)
            {
                msg.Write((byte)ActiveVote.VoteType);
                if (ActiveVote.State != VoteState.None && ActiveVote.VoteType != VoteType.Unknown)
                {
                    var eligibleClients = GameMain.Server.ConnectedClients.Where(c => c.InGame && c != ActiveVote.VoteStarter);

                    var yesClients = eligibleClients.Where(c => c.GetVote<int>(ActiveVote.VoteType) == 2);
                    msg.Write((byte)yesClients.Count());
                    foreach (Client c in yesClients)
                    {
                        msg.Write(c.ID);
                    }

                    var noClients = eligibleClients.Where(c => c.GetVote<int>(ActiveVote.VoteType) == 1);
                    msg.Write((byte)noClients.Count());
                    foreach (Client c in noClients)
                    {
                        msg.Write(c.ID);
                    }

                    msg.Write((byte)eligibleClients.Count());

                    switch (ActiveVote.State)
                    {
                        case VoteState.Started:
                            msg.Write(ActiveVote.VoteStarter.ID);
                            msg.Write((byte)GameMain.Server.ServerSettings.VoteTimeout);

                            switch (ActiveVote.VoteType)
                            {
                                case VoteType.PurchaseSub:
                                case VoteType.PurchaseAndSwitchSub:
                                case VoteType.SwitchSub:
                                    SubmarineVote vote = ActiveVote as SubmarineVote;
                                    msg.Write(vote.Sub.Name);
                                    msg.Write(vote.TransferItems);
                                    break;
                                case VoteType.TransferMoney:
                                    var transferVote = (ActiveVote as TransferVote);
                                    msg.Write(transferVote.From?.ID ?? 0);
                                    msg.Write(transferVote.To?.ID ?? 0);
                                    msg.Write(transferVote.TransferAmount);
                                    break;
                            }

                            break;
                        case VoteState.Running:
                            // Nothing specific
                            break;
                        case VoteState.Passed:
                        case VoteState.Failed:
                            msg.Write(ActiveVote.State == VoteState.Passed);
                            switch (ActiveVote.VoteType)
                            {
                                case VoteType.PurchaseSub:
                                case VoteType.PurchaseAndSwitchSub:
                                case VoteType.SwitchSub:
                                    var subVote = ActiveVote as SubmarineVote;
                                    msg.Write(subVote.Sub.Name);
                                    msg.Write(subVote.TransferItems);
                                    msg.Write((short)subVote.DeliveryFee);
                                    break;
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
