using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class Voting
    {
        private bool allowSubVoting, allowModeVoting;

        public bool AllowVoteKick = true;

        public bool AllowEndVoting = true;

        public bool AllowSubVoting
        {
            get { return allowSubVoting; }
            set 
            {
                if (value == allowSubVoting) return;
                allowSubVoting = value;
                GameMain.NetLobbyScreen.SubList.Enabled = value || GameMain.Server != null;
                GameMain.NetLobbyScreen.InfoFrame.FindChild("subvotes").Visible = value;

                if (GameMain.Server != null)
                {
                    UpdateVoteTexts(GameMain.Server.ConnectedClients, VoteType.Sub);
                    GameMain.Server.UpdateVoteStatus();
                }
                else
                {
                    GameMain.NetLobbyScreen.SubList.Deselect();
                }
            }
        }
        public bool AllowModeVoting
        {
            get { return allowModeVoting; }
            set
            {
                if (value == allowModeVoting) return;
                allowModeVoting = value;
                GameMain.NetLobbyScreen.ModeList.Enabled = value || GameMain.Server != null;
                GameMain.NetLobbyScreen.InfoFrame.FindChild("modevotes").Visible = value;
                if (GameMain.Server != null)
                {
                    UpdateVoteTexts(GameMain.Server.ConnectedClients, VoteType.Mode);
                    GameMain.Server.UpdateVoteStatus();
                }
                else
                {
                    GameMain.NetLobbyScreen.ModeList.Deselect();
                }
            }
        }

        public void UpdateVoteTexts(List<Client> clients, VoteType voteType)
        {
            GUIListBox listBox = (voteType == VoteType.Sub) ?
                GameMain.NetLobbyScreen.SubList : GameMain.NetLobbyScreen.ModeList;

            foreach (GUIComponent comp in listBox.children)
            {
                GUITextBlock voteText = comp.FindChild("votes") as GUITextBlock;
                if (voteText != null) comp.RemoveChild(voteText);
            }

            List<Pair<object, int>> voteList = GetVoteList(voteType, clients);
            foreach (Pair<object, int> votable in voteList)
            {
                SetVoteText(listBox, votable.First, votable.Second);
            }
        }

        private void SetVoteText(GUIListBox listBox, object userData, int votes)
        {
            if (userData == null) return;
            foreach (GUIComponent comp in listBox.children)
            {
                if (comp.UserData != userData) continue;
                GUITextBlock voteText = comp.FindChild("votes") as GUITextBlock;
                if (voteText == null)
                {
                    voteText = new GUITextBlock(new Rectangle(0, 0, 30, 0), "", GUI.Style, Alignment.Right, Alignment.Right, comp);
                    voteText.UserData = "votes";
                }

                voteText.Text = votes == 0 ? "" : votes.ToString();
            }
        }
        
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
                    voteList.Add(Pair<object, int>.Create(vote, 1));
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

            UpdateVoteTexts(connectedClients, VoteType.Mode);
            UpdateVoteTexts(connectedClients, VoteType.Sub);
        }

        public void ClientWrite(NetBuffer msg, VoteType voteType, object data)
        {
            if (GameMain.Server != null) return;

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

                    msg.Write(gameMode.Name);
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
            }

            msg.WritePadBits();
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
                    Submarine sub = Submarine.SavedSubmarines.Find(s => s.Name == subName);
                    sender.SetVote(voteType, sub);
                    UpdateVoteTexts(GameMain.Server.ConnectedClients, voteType);
                    break;

                case VoteType.Mode:
                    string modeName = inc.ReadString();
                    GameModePreset mode = GameModePreset.list.Find(gm => gm.Name == modeName);
                    sender.SetVote(voteType, mode);
                    UpdateVoteTexts(GameMain.Server.ConnectedClients, voteType);
                    break;
                case VoteType.EndRound:
                    if (sender.Character == null) return;
                    sender.SetVote(voteType, inc.ReadBoolean());

                    GameMain.NetworkMember.EndVoteCount = GameMain.Server.ConnectedClients.Count(c => c.Character != null && c.GetVote<bool>(VoteType.EndRound));
                    GameMain.NetworkMember.EndVoteMax = GameMain.Server.ConnectedClients.Count(c => c.Character != null);

                    break;
                case VoteType.Kick:
                    byte kickedClientID = inc.ReadByte();

                    Client kicked = GameMain.Server.ConnectedClients.Find(c => c.ID == kickedClientID);
                    if (kicked == null) return;

                    kicked.AddKickVote(sender);

                    GameMain.Server.SendChatMessage(sender.name + " has voted to kick " + kicked.name, ChatMessageType.Server, null);

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
                    msg.Write(((GameModePreset)vote.First).Name);
                }
            }
            msg.Write(AllowEndVoting);
            if (AllowEndVoting)
            {
                msg.Write((byte)GameMain.Server.ConnectedClients.Count(v => v.GetVote<bool>(VoteType.EndRound)));
                msg.Write((byte)GameMain.Server.ConnectedClients.Count);
            }

            msg.Write(AllowVoteKick);

            msg.WritePadBits();
        }

        public void ClientRead(NetIncomingMessage inc)
        {
            if (GameMain.Server != null) return;

             AllowSubVoting = inc.ReadBoolean();
            if (allowSubVoting)
            {
                foreach (Submarine sub in Submarine.SavedSubmarines)
                {
                    SetVoteText(GameMain.NetLobbyScreen.SubList, sub, 0);
                }
                int votableCount = inc.ReadByte();
                for (int i = 0; i < votableCount; i++)
                {
                    int votes = inc.ReadByte();
                    string subName = inc.ReadString();
                    Submarine sub = Submarine.SavedSubmarines.Find(sm => sm.Name == subName);
                    SetVoteText(GameMain.NetLobbyScreen.SubList, sub, votes);
                }
            }
            AllowModeVoting = inc.ReadBoolean();
            if (allowModeVoting)
            {
                int votableCount = inc.ReadByte();
                for (int i = 0; i < votableCount; i++)
                {
                    int votes = inc.ReadByte();
                    string modeName = inc.ReadString();
                    GameModePreset mode = GameModePreset.list.Find(m => m.Name == modeName);
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

            inc.ReadPadBits();
        }
        
    }
}
