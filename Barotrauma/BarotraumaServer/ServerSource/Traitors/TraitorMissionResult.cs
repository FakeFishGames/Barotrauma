using Barotrauma.Networking;

namespace Barotrauma
{
    partial class TraitorMissionResult
    {
        public TraitorMissionResult(Traitor.TraitorMission mission)
        {
            MissionIdentifier = mission.Identifier;
            EndMessage = mission.GlobalEndMessage;
            Success = mission.IsCompleted;
            foreach (Traitor traitor in mission.Traitors.Values)
            {
                Characters.Add(traitor.Character);
            }
        }

        public void ServerWrite(IWriteMessage msg)
        {
            msg.WriteIdentifier(MissionIdentifier);
            msg.WriteString(EndMessage);
            msg.WriteBoolean(Success);
            msg.WriteByte((byte)Characters.Count);
            foreach (Character character in Characters)
            {
                msg.WriteUInt16(character.ID);
            }
        }
    }
}
