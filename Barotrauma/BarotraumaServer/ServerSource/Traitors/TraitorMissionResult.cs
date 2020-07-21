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
            msg.Write(MissionIdentifier);
            msg.Write(EndMessage);
            msg.Write(Success);
            msg.Write((byte)Characters.Count);
            foreach (Character character in Characters)
            {
                msg.Write(character.ID);
            }
        }
    }
}
