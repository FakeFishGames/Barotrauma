using Barotrauma.Networking;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class Voting
    {
        private bool allowSubVoting, allowModeVoting;

        public bool AllowVoteKick = true;

        public bool AllowEndVoting = true;

        public bool VoteRunning = false;

        public enum VoteState { None = 0, Started = 1, Running = 2, Passed = 3, Failed = 4 };

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

            List<Pair<object, int>> voteList = GetVoteList(voteType, voters);

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
    }
}
