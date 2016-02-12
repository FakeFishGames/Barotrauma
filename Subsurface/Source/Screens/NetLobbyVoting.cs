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

        public void RegisterVote(NetIncomingMessage inc, List<Client> connectedClients)
        {
            byte voteTypeByte = inc.ReadByte();
            VoteType voteType = VoteType.Unknown;
            try
            {
                voteType = (VoteType)voteTypeByte;
            }
            catch
            {
                return;
            }
            Client sender = connectedClients.Find(x => x.Connection == inc.SenderConnection);
            switch (voteType)
            {
                case VoteType.Sub:
                    string subName = inc.ReadString();
                    Submarine sub = Submarine.SavedSubmarines.Find(s => s.Name == subName);
                    sender.SetVote(voteType, sub);
                    UpdateVoteTexts(connectedClients, voteType);
                    break;

                case VoteType.Mode:
                    string modeName = inc.ReadString();
                    GameModePreset mode = GameModePreset.list.Find(gm => gm.Name == modeName);
                    sender.SetVote(voteType, mode);
                    UpdateVoteTexts(connectedClients, voteType);
                    break;
                case VoteType.EndRound:
                    if (sender.Character == null) return;
                    sender.SetVote(voteType, inc.ReadBoolean());

                    GameMain.NetworkMember.EndVoteCount = connectedClients.Count(c => c.Character != null && c.GetVote<bool>(VoteType.EndRound));
                    GameMain.NetworkMember.EndVoteMax = connectedClients.Count(c => c.Character != null);

                    break;
            }

            GameMain.Server.UpdateVoteStatus();
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

                voteText.Text = votes.ToString();
            }
        }
        
        private List<Pair<object, int>> GetVoteList(VoteType voteType, List<Client> voters)
        {
            List<Pair<object, int>> voteList = new List<Pair<object, int>>();

            foreach (Client voter in voters)
            {
                object vote = voter.GetVote<object>(voteType);
                if (vote == null) continue;

                var existingVotable = voteList.Find(v => v.First == vote);
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
        
        public void WriteData(NetOutgoingMessage msg, List<Client> voters)
        {
            msg.Write(allowSubVoting);

            if (allowSubVoting)
            {
                List<Pair<object, int>> voteList = GetVoteList(VoteType.Sub, voters);
                msg.Write((byte)voteList.Count);
                foreach (Pair<object, int> vote in voteList)
                {
                    if (vote.Second < 1 || vote.First==null) continue;
                    msg.Write((byte)vote.Second);
                    msg.Write(((Submarine)vote.First).Name);
                }
            }


            msg.Write(AllowModeVoting);
            if (allowModeVoting)
            {
                List<Pair<object, int>> voteList = GetVoteList(VoteType.Mode, voters);
                msg.Write((byte)voteList.Count);
                foreach (Pair<object, int> vote in voteList)
                {
                    if (vote.Second < 1 || vote.First == null) continue;
                    msg.Write((byte)vote.Second);
                    msg.Write(((GameModePreset)vote.First).Name);
                }
            }

            msg.Write(AllowEndVoting);
            if (AllowEndVoting)
            {
                msg.Write((byte)voters.Count);
                msg.Write((byte)voters.Count(v => v.GetVote<bool>(VoteType.EndRound)));
            }           

        }

        public void ReadData(NetIncomingMessage msg)
        {
            AllowSubVoting = msg.ReadBoolean();
            if (allowSubVoting)
            {
                int votableCount = msg.ReadByte();
                for (int i = 0; i < votableCount; i++)
                {
                    int votes = msg.ReadByte();
                    string subName = msg.ReadString();
                    Submarine sub = Submarine.SavedSubmarines.Find(sm => sm.Name == subName);
                    SetVoteText(GameMain.NetLobbyScreen.SubList, sub, votes);
                }
            }

            AllowModeVoting = msg.ReadBoolean();
            if (allowModeVoting)
            {
                int votableCount = msg.ReadByte();
                for (int i = 0; i < votableCount; i++)
                {
                    int votes = msg.ReadByte();
                    string modeName = msg.ReadString();
                    GameModePreset mode = GameModePreset.list.Find(m => m.Name == modeName);
                    SetVoteText(GameMain.NetLobbyScreen.SubList, mode, votes);
                }
            }

            AllowEndVoting = msg.ReadBoolean();
            if (AllowEndVoting)
            {
                GameMain.NetworkMember.EndVoteCount = msg.ReadByte();
                GameMain.NetworkMember.EndVoteMax = msg.ReadByte();
            }
        }
    }
}
