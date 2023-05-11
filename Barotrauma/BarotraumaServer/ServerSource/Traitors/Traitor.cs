using Barotrauma.Networking;

namespace Barotrauma
{
    partial class Traitor
    {
        public readonly Character Character;

        public string Role { get; }
        public TraitorMission Mission { get; }
        public Objective CurrentObjective => Mission.GetCurrentObjective(this);

        public Traitor(TraitorMission mission, string role, Character character)
        {
            Mission = mission;
            Role = role;
            Character = character;
            Character.IsTraitor = true;
            GameMain.NetworkMember.CreateEntityEvent(Character, new Character.CharacterStatusEventData());
        }

        public delegate void MessageSender(string message);

        public void SendChatMessage(string serverText, Identifier iconIdentifier)
        {
            Client traitorClient = GameMain.Server.ConnectedClients.Find(c => c.Character == Character);
            GameMain.Server.SendTraitorMessage(traitorClient, serverText, iconIdentifier, TraitorMessageType.Server);
        }

        public void SendChatMessageBox(string serverText, Identifier iconIdentifier)
        {
            Client traitorClient = GameMain.Server.ConnectedClients.Find(c => c.Character == Character);
            GameMain.Server.SendTraitorMessage(traitorClient, serverText, iconIdentifier, TraitorMessageType.ServerMessageBox);
        }

        public void UpdateCurrentObjective(string objectiveText, Identifier iconIdentifier)
        {
            Client traitorClient = GameMain.Server.ConnectedClients.Find(c => c.Character == Character);
            Character.TraitorCurrentObjective = objectiveText;
            GameMain.Server.SendTraitorMessage(traitorClient, Character.TraitorCurrentObjective.Value, iconIdentifier, TraitorMessageType.Objective);
        }
    }
}
