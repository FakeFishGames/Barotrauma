using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Voting
    {
        private bool allowSubVoting, allowModeVoting;

        public bool AllowVoteKick = true;

        public bool AllowEndVoting = true;

        public bool VoteRunning = false;

        public enum VoteState { None = 0, Started = 1, Running = 2, Passed = 3, Failed = 4 };

        private IReadOnlyDictionary<T, int> GetVoteCounts<T>(VoteType voteType, List<Client> voters)
        {
            Dictionary<T, int> voteList = new Dictionary<T, int>();

            foreach (Client voter in voters)
            {
                T vote = voter.GetVote<T>(voteType);
                if (vote == null) continue;

                if (!voteList.ContainsKey(vote))
                {
                    voteList.Add(vote, 1);
                }
                else
                {
                    voteList[vote]++;
                }
            }
            return voteList;
        }

        public T HighestVoted<T>(VoteType voteType, List<Client> voters)
        {
            if (voteType == VoteType.Sub && !AllowSubVoting) return default(T);
            if (voteType == VoteType.Mode && !AllowModeVoting) return default(T);

            IReadOnlyDictionary<T, int> voteList = GetVoteCounts<T>(voteType, voters);

            T selected = default(T);
            int highestVotes = 0;
            foreach (KeyValuePair<T, int> votable in voteList)
            {
                if (voteType == VoteType.Sub
                    && votable.Key is SubmarineInfo subInfo
                    && GameMain.NetworkMember.ServerSettings.HiddenSubs.Contains(subInfo.Name))
                {
                    //This sub is hidden so it can't be voted for, skip
                    continue;
                }
                if (selected == null || votable.Value > highestVotes)
                {
                    highestVotes = votable.Value;
                    selected = votable.Key;
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
