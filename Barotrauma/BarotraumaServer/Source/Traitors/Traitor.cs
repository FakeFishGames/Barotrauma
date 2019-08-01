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
            var greetingChatMsg = ChatMessage.Create(null, greetingMessage, ChatMessageType.Server, null);
            var greetingMsgBox = ChatMessage.Create(null, greetingMessage, ChatMessageType.ServerMessageBox, null);

            Client traitorClient = server.ConnectedClients.Find(c => c.Character == Character);
            GameMain.Server.SendDirectChatMessage(greetingChatMsg, traitorClient);
            GameMain.Server.SendDirectChatMessage(greetingMsgBox, traitorClient);

            Client ownerClient = server.ConnectedClients.Find(c => c.Connection == server.OwnerConnection);
            if (traitorClient != ownerClient && ownerClient != null && ownerClient.Character == null)
            {
                var ownerMsg = ChatMessage.Create(
                    null,//TextManager.Get("NewTraitor"),
                    CurrentObjective.StartMessageServerText,
                    ChatMessageType.ServerMessageBox,
                    null
                );
                GameMain.Server.SendDirectChatMessage(ownerMsg, ownerClient);
            }
        }
    }
}
