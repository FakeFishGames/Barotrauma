using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    partial class Voting
    {
        public bool AllowSubVoting
        {
            get { return allowSubVoting; }
            set
            {
                if (value == allowSubVoting) return;
                allowSubVoting = value;
                GameMain.NetLobbyScreen.SubList.Enabled = value ||
                    (GameMain.Client != null && GameMain.Client.HasPermission(ClientPermissions.SelectSub));
                GameMain.NetLobbyScreen.Frame.FindChild("subvotes", true).Visible = value;

                UpdateVoteTexts(null, VoteType.Sub);
                GameMain.NetLobbyScreen.SubList.Deselect();
            }
        }
        public bool AllowModeVoting
        {
            get { return allowModeVoting; }
            set
            {
                if (value == allowModeVoting) return;
                allowModeVoting = value;
                GameMain.NetLobbyScreen.ModeList.Enabled = 
                    value || 
                    (GameMain.Client != null && GameMain.Client.HasPermission(ClientPermissions.SelectMode));

                GameMain.NetLobbyScreen.Frame.FindChild("modevotes", true).Visible = value;

                // Disable modes that cannot be voted on
                foreach (var guiComponent in GameMain.NetLobbyScreen.ModeList.Content.Children)
                {
                    if (guiComponent is GUIFrame frame)
                    {
                        frame.CanBeFocused = !allowModeVoting || ((GameModePreset) frame.UserData).Votable;
                    }
                }
                
                UpdateVoteTexts(null, VoteType.Mode);
                GameMain.NetLobbyScreen.ModeList.Deselect();
            }
        }

        public void UpdateVoteTexts(List<Client> clients, VoteType voteType)
        {
            switch (voteType)
            {
                case VoteType.Sub:
                case VoteType.Mode:
                    GUIListBox listBox = (voteType == VoteType.Sub) ?
                        GameMain.NetLobbyScreen.SubList : GameMain.NetLobbyScreen.ModeList;

                    foreach (GUIComponent comp in listBox.Content.Children)
                    {
                        if (comp.FindChild("votes") is GUITextBlock voteText) { comp.RemoveChild(voteText); }
                    }

                    if (clients == null) { return; }
                    
                    List<Pair<object, int>> voteList = GetVoteList(voteType, clients);
                    foreach (Pair<object, int> votable in voteList)
                    {
                        SetVoteText(listBox, votable.First, votable.Second);
                    }                    
                    break;
                case VoteType.StartRound:
                    if (clients == null) { return; }
                    foreach (Client client in clients)
                    {
                        var clientReady = GameMain.NetLobbyScreen.PlayerList.Content.FindChild(client)?.FindChild("clientready");
                        if (clientReady != null)
                        {
                            clientReady.Visible = client.GetVote<bool>(VoteType.StartRound);
                        }
                    }
                    break;
            }
        }

        private void SetVoteText(GUIListBox listBox, object userData, int votes)
        {
            if (userData == null) return;
            foreach (GUIComponent comp in listBox.Content.Children)
            {
                if (comp.UserData != userData) continue;
                GUITextBlock voteText = comp.FindChild("votes") as GUITextBlock;
                if (voteText == null)
                {
                    voteText = new GUITextBlock(new RectTransform(new Point(30, comp.Rect.Height), comp.RectTransform, Anchor.CenterRight),
                        "", textAlignment: Alignment.CenterRight)
                    {
                        UserData = "votes"
                    };
                }

                voteText.Text = votes == 0 ? "" : votes.ToString();
            }
        }

        public void ClientWrite(IWriteMessage msg, VoteType voteType, object data)
        {
            msg.Write((byte)voteType);

            switch (voteType)
            {
                case VoteType.Sub:                    
                    SubmarineInfo sub = data as SubmarineInfo;
                    if (sub == null) { return; }
                    msg.Write(sub.EqualityCheckVal);
                    break;
                case VoteType.Mode:
                    GameModePreset gameMode = data as GameModePreset;
                    if (gameMode == null) { return; }
                    msg.Write(gameMode.Identifier);
                    break;
                case VoteType.EndRound:
                    if (!(data is bool)) { return; }
                    msg.Write((bool)data);
                    break;
                case VoteType.Kick:
                    Client votedClient = data as Client;
                    if (votedClient == null) return;

                    msg.Write(votedClient.ID);
                    break;
                case VoteType.StartRound:
                    if (!(data is bool)) return;
                    msg.Write((bool)data);
                    break;

                case VoteType.PurchaseAndSwitchSub:
                case VoteType.PurchaseSub:
                case VoteType.SwitchSub:
                    if (!VoteRunning)
                    {
                        SubmarineInfo voteSub = data as SubmarineInfo;
                        if (voteSub == null) return;
                        msg.Write(true);
                        msg.Write(voteSub.Name);
                    }
                    else
                    {
                        if (!(data is int)) { return; }
                        msg.Write(false);
                        msg.Write((int)data);
                    }

                    break;
            }

            msg.WritePadBits();
        }
        
        public void ClientRead(IReadMessage inc)
        {
            AllowSubVoting = inc.ReadBoolean();
            if (allowSubVoting)
            {
                UpdateVoteTexts(null, VoteType.Sub);
                int votableCount = inc.ReadByte();
                for (int i = 0; i < votableCount; i++)
                {
                    int votes = inc.ReadByte();
                    string subName = inc.ReadString();
                    List<SubmarineInfo> serversubs = new List<SubmarineInfo>();
                    foreach (GUIComponent item in GameMain.NetLobbyScreen?.SubList?.Content?.Children)
                    {
                        if (item.UserData != null && item.UserData is SubmarineInfo) { serversubs.Add(item.UserData as SubmarineInfo); }
                    }
                    SubmarineInfo sub = serversubs.FirstOrDefault(s => s.Name == subName);
                    SetVoteText(GameMain.NetLobbyScreen.SubList, sub, votes);
                }
            }
            AllowModeVoting = inc.ReadBoolean();
            if (allowModeVoting)
            {
                UpdateVoteTexts(null, VoteType.Mode);
                int votableCount = inc.ReadByte();
                for (int i = 0; i < votableCount; i++)
                {
                    int votes = inc.ReadByte();
                    string modeIdentifier = inc.ReadString();
                    GameModePreset mode = GameModePreset.List.Find(m => m.Identifier == modeIdentifier);
                    SetVoteText(GameMain.NetLobbyScreen.ModeList, mode, votes);
                }
            }
            AllowEndVoting = inc.ReadBoolean();
            if (AllowEndVoting)
            {
                GameMain.NetworkMember.EndVoteCount = inc.ReadByte();
                GameMain.NetworkMember.EndVoteMax = inc.ReadByte();
            }
            AllowVoteKick = inc.ReadBoolean();

            byte subVoteStateByte = inc.ReadByte();
            VoteState subVoteState = VoteState.None;
            try
            {
                subVoteState = (VoteState)subVoteStateByte;
            }
            catch (System.Exception e)
            {
                DebugConsole.ThrowError("Failed to cast vote type \"" + subVoteStateByte + "\"", e);
            }

            if (subVoteState != VoteState.None)
            {
                byte voteTypeByte = inc.ReadByte();
                VoteType voteType = VoteType.Unknown;

                try
                {
                    voteType = (VoteType)voteTypeByte;
                }
                catch (System.Exception e)
                {
                    DebugConsole.ThrowError("Failed to cast vote type \"" + voteTypeByte + "\"", e);
                }

                if (voteType != VoteType.Unknown)
                {
                    byte yesClientCount = inc.ReadByte();
                    for (int i = 0; i < yesClientCount; i++)
                    {
                        byte clientID = inc.ReadByte();
                        var matchingClient = GameMain.NetworkMember.ConnectedClients.Find(c => c.ID == clientID);
                        matchingClient?.SetVote(voteType, 2);
                    }

                    byte noClientCount = inc.ReadByte();
                    for (int i = 0; i < noClientCount; i++)
                    {
                        byte clientID = inc.ReadByte();
                        var matchingClient = GameMain.NetworkMember.ConnectedClients.Find(c => c.ID == clientID);
                        matchingClient?.SetVote(voteType, 1);
                    }

                    GameMain.NetworkMember.SubmarineVoteYesCount = yesClientCount;
                    GameMain.NetworkMember.SubmarineVoteNoCount = noClientCount;
                    GameMain.NetworkMember.SubmarineVoteMax = inc.ReadByte();

                    switch (subVoteState)
                    {
                        case VoteState.Started:
                            Client myClient = GameMain.NetworkMember.ConnectedClients.Find(c => c.ID == GameMain.Client.ID);
                            if (!myClient.InGame)
                            {
                                VoteRunning = true;
                                return;
                            }

                            string subName1 = inc.ReadString();
                            SubmarineInfo info = GameMain.Client.ServerSubmarines.FirstOrDefault(s => s.Name == subName1);

                            if (info == null)
                            {
                                DebugConsole.ThrowError("Failed to find a matching submarine, vote aborted");
                                return;
                            }

                            VoteRunning = true;
                            byte starterID = inc.ReadByte();
                            Client starterClient = GameMain.NetworkMember.ConnectedClients.Find(c => c.ID == starterID);
                            float timeOut = inc.ReadByte();
                            GameMain.Client.ShowSubmarineChangeVoteInterface(starterClient, info, voteType, timeOut);
                            break;
                        case VoteState.Running:
                            // Nothing specific
                            break;
                        case VoteState.Passed:
                        case VoteState.Failed:
                            VoteRunning = false;

                            bool passed = inc.ReadBoolean();
                            string subName2 = inc.ReadString();
                            SubmarineInfo subInfo = GameMain.Client.ServerSubmarines.FirstOrDefault(s => s.Name == subName2);

                            if (subInfo == null)
                            {
                                DebugConsole.ThrowError("Failed to find a matching submarine, vote aborted");
                                return;
                            }

                            if (GameMain.Client.VotingInterface != null)
                            {
                                GameMain.Client.VotingInterface.EndVote(passed, yesClientCount, noClientCount);
                            }
                            else if (GameMain.Client.ConnectedClients.Count > 1)
                            {
                                GameMain.NetworkMember.AddChatMessage(VotingInterface.GetSubmarineVoteResultMessage(subInfo, voteType, yesClientCount.ToString(), noClientCount.ToString(), passed), ChatMessageType.Server);
                            }

                            if (passed)
                            {
                                int deliveryFee = inc.ReadInt16();
                                switch (voteType)
                                {
                                    case VoteType.PurchaseAndSwitchSub:
                                        GameMain.GameSession.PurchaseSubmarine(subInfo);
                                        GameMain.GameSession.SwitchSubmarine(subInfo, 0);
                                        break;
                                    case VoteType.PurchaseSub:
                                        GameMain.GameSession.PurchaseSubmarine(subInfo);
                                        break;
                                    case VoteType.SwitchSub:
                                        GameMain.GameSession.SwitchSubmarine(subInfo, deliveryFee);
                                        break;
                                }

                                SubmarineSelection.ContentRefreshRequired = true;
                            }
                            break;
                    }
                }
            }       

            GameMain.NetworkMember.ConnectedClients.ForEach(c => c.SetVote(VoteType.StartRound, false));
            byte readyClientCount = inc.ReadByte();
            for (int i = 0; i < readyClientCount; i++)
            {
                byte clientID = inc.ReadByte();
                var matchingClient = GameMain.NetworkMember.ConnectedClients.Find(c => c.ID == clientID);
                matchingClient?.SetVote(VoteType.StartRound, true);
            }
            UpdateVoteTexts(GameMain.NetworkMember.ConnectedClients, VoteType.StartRound);

            inc.ReadPadBits();
        }
    }
}
