using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Networking;

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

        /// <summary>
        /// Saves bots in multiplayer
        /// </summary>
        /// <param name="root"></param>
        public void SaveMultiplayer(XElement root)
        {
            XElement saveElement = new XElement("bots", new XAttribute("hasbots", HasBots));
            foreach (CharacterInfo info in characterInfos)
            {
                if (Level.Loaded != null)
                {
                    if (!info.IsNewHire && (info.Character == null || info.Character.IsDead)) { continue; }
                }

                XElement characterElement = info.Save(saveElement);
                if (info.InventoryData != null) { characterElement.Add(info.InventoryData); }
                if (info.HealthData != null) { characterElement.Add(info.HealthData); }
            }
            root.Add(saveElement);
        }
    }
}
