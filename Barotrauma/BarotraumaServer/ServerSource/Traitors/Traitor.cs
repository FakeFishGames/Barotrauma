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
            GameMain.NetworkMember.CreateEntityEvent(Character, new object[] { NetEntityEvent.Type.Status });
        }

        public delegate void MessageSender(string message);
        public void Greet(GameServer server, string codeWords, string codeResponse, MessageSender messageSender)
        {
            string greetingMessage = TextManager.FormatServerMessage(Mission.StartText,
                ("[codewords]", codeWords),
                ("[coderesponse]", codeResponse));
            messageSender(greetingMessage);
            Client traitorClient = server.ConnectedClients.Find(c => c.Character == Character);
            Client ownerClient = server.ConnectedClients.Find(c => c.Connection == server.OwnerConnection);
            if (traitorClient != ownerClient && ownerClient != null && ownerClient.Character == null)
            {
                GameMain.Server.SendTraitorMessage(ownerClient, CurrentObjective.StartMessageServerText.Value, Mission.Identifier, TraitorMessageType.ServerMessageBox);
            }
        }

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
