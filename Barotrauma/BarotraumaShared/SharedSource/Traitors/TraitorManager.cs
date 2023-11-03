#nullable enable

using Barotrauma.Networking;
using System.Linq;

namespace Barotrauma
{
    sealed partial class TraitorManager
    {
        public struct TraitorResults : INetSerializableStruct
        {
            [NetworkSerialize]
            public byte VotedAsTraitorClientSessionId;

            [NetworkSerialize]
            public bool VotedCorrectTraitor;

            [NetworkSerialize]
            public bool ObjectiveSuccessful;

            [NetworkSerialize]
            public int MoneyPenalty;

            [NetworkSerialize]
            public Identifier TraitorEventIdentifier;

            public TraitorResults(Client? votedAsTraitor, TraitorEvent traitorEvent)
            {
                VotedAsTraitorClientSessionId = votedAsTraitor?.SessionId ?? 0;
                VotedCorrectTraitor = votedAsTraitor == traitorEvent.Traitor;
                if (traitorEvent.Prefab.AllowAccusingSecondaryTraitor && !VotedCorrectTraitor)
                {
                    VotedCorrectTraitor = traitorEvent.SecondaryTraitors.Contains(votedAsTraitor);
                }
                ObjectiveSuccessful = traitorEvent.CurrentState == TraitorEvent.State.Completed;
                MoneyPenalty = votedAsTraitor != null && !VotedCorrectTraitor ? 
                    traitorEvent.Prefab.MoneyPenaltyForUnfoundedTraitorAccusation : 
                    0;
                TraitorEventIdentifier = traitorEvent.Prefab.Identifier;
            }

            public Client? GetTraitorClient()
            {
                int sessionId = VotedAsTraitorClientSessionId;
                return GameMain.NetworkMember?.ConnectedClients?.FirstOrDefault(c => c.SessionId == sessionId);
            }
        }
    }
}