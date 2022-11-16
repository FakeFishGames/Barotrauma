using Barotrauma.Networking;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class Voting
    {
        public enum VoteState { None = 0, Started = 1, Running = 2, Passed = 3, Failed = 4 };

        private IReadOnlyDictionary<T, int> GetVoteCounts<T>(VoteType voteType, IEnumerable<Client> voters)
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
            if (voteType == VoteType.Sub && !GameMain.NetworkMember.ServerSettings.AllowSubVoting) { return default; }
            if (voteType == VoteType.Mode && !GameMain.NetworkMember.ServerSettings.AllowModeVoting) { return default; }

            IReadOnlyDictionary<T, int> voteList = GetVoteCounts<T>(voteType, voters);

            T selected = default;
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
    }
}
