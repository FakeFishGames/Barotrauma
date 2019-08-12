using Barotrauma.Networking;
using Lidgren.Network;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Traitor
    {
        public readonly Character Character;

        public string Role { get; private set; }
        public TraitorMission Mission { get; private set; }
        public Objective CurrentObjective => Mission.GetCurrentObjective(this);

        public Traitor(TraitorMission mission, string role, Character character)
        {
            Mission = mission;
            Role = role;
            Character = character;
            Character.IsTraitor = true;
            GameMain.NetworkMember.CreateEntityEvent(Character, new object[] { NetEntityEvent.Type.Status });
        }

        public void Greet(GameServer server, string codeWords, string codeResponse)
        {
            string greetingMessage = TextManager.FormatServerMessage(Mission.StartText, new string[] {
                "[codewords]", "[coderesponse]"
            }, new string[] {
                codeWords, codeResponse
            });

            SendChatMessage(greetingMessage);
            SendChatMessageBox(greetingMessage);

            Client traitorClient = server.ConnectedClients.Find(c => c.Character == Character);
            Client ownerClient = server.ConnectedClients.Find(c => c.Connection == server.OwnerConnection);
            if (traitorClient != ownerClient && ownerClient != null && ownerClient.Character == null)
            {
                GameMain.Server.SendTraitorMessage(ownerClient, CurrentObjective.StartMessageServerText, isObjective: false, createMessageBox: true);
            }
        }

        public void SendChatMessage(string serverText)
        {
            serverText = string.Join("   -   ", serverText, serverText, serverText, serverText);

            Client traitorClient = GameMain.Server.ConnectedClients.Find(c => c.Character == Character);
            GameMain.Server.SendTraitorMessage(traitorClient, serverText, isObjective: false, createMessageBox: false);
        }

        public void SendChatMessageBox(string serverText)
        {
            serverText = string.Join("   -   ", serverText, serverText, serverText, serverText);

            Client traitorClient = GameMain.Server.ConnectedClients.Find(c => c.Character == Character);
            GameMain.Server.SendTraitorMessage(traitorClient, serverText, isObjective: false, createMessageBox: true);
        }

        public void UpdateCurrentObjective(string objectiveText)
        {
            Client traitorClient = GameMain.Server.ConnectedClients.Find(c => c.Character == Character);
            Character.TraitorCurrentObjective = objectiveText;
            GameMain.Server.SendTraitorMessage(traitorClient, Character.TraitorCurrentObjective, isObjective: true, createMessageBox: false);
        }
    }
}
