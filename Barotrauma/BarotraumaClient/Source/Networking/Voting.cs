using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

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
                GameMain.NetLobbyScreen.InfoFrame.FindChild("subvotes", true).Visible = value;

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

                GameMain.NetLobbyScreen.InfoFrame.FindChild("modevotes", true).Visible = value;

                //gray out modes that can't be voted
                foreach (GUITextBlock comp in GameMain.NetLobbyScreen.ModeList.Content.Children)
                {
                    comp.TextColor =
                        new Color(comp.TextColor.R, comp.TextColor.G, comp.TextColor.B, 
                            !allowModeVoting || ((GameModePreset)comp.UserData).Votable ? (byte)255 : (byte)100);
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
                        if (comp.FindChild("votes") is GUITextBlock voteText) comp.RemoveChild(voteText);
                    }

                    if (clients == null) return;
                    
                    List<Pair<object, int>> voteList = GetVoteList(voteType, clients);
                    foreach (Pair<object, int> votable in voteList)
                    {
                        SetVoteText(listBox, votable.First, votable.Second);
                    }                    
                    break;
                case VoteType.StartRound:
                    if (clients == null) return;
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

        public void ClientWrite(NetBuffer msg, VoteType voteType, object data)
        {
            msg.Write((byte)voteType);

            switch (voteType)
            {
                case VoteType.Sub:
                    Submarine sub = data as Submarine;
                    if (sub == null) return;

                    msg.Write(sub.Name);
                    break;
                case VoteType.Mode:
                    GameModePreset gameMode = data as GameModePreset;
                    if (gameMode == null) return;
                    msg.Write(gameMode.Identifier);
                    break;
                case VoteType.EndRound:
                    if (!(data is bool)) return;
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
            }

            msg.WritePadBits();
        }
        
        public void ClientRead(NetBuffer inc)
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
                    List<Submarine> serversubs = new List<Submarine>();
                    foreach (GUIComponent item in GameMain.NetLobbyScreen?.SubList?.Content?.Children)
                    {
                        if (item.UserData != null && item.UserData is Submarine) serversubs.Add(item.UserData as Submarine);
                    }
                    Submarine sub = serversubs.FirstOrDefault(sm => sm.Name == subName);
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
