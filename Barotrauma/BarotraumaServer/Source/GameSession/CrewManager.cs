using Barotrauma.Networking;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class CrewManager
    {
        partial void CreateRandomConversation()
        {
            List<Character> availableSpeakers = Character.CharacterList.FindAll(c =>
                c.AIController is HumanAIController &&
                !c.IsDead &&
                c.SpeechImpediment <= 100.0f);

            foreach (Client client in GameMain.Server.ConnectedClients)
            {
                if (client.Character != null) availableSpeakers.Remove(client.Character);
            }

            pendingConversationLines.AddRange(NPCConversation.CreateRandom(availableSpeakers));
        }
    }
}
