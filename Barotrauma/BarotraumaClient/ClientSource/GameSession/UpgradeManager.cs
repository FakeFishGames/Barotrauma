#nullable enable
using System;
using System.Linq;
using Barotrauma.Networking;

namespace Barotrauma
{
    partial class UpgradeManager
    {
        partial void UpgradeNPCSpeak(string text, bool isSinglePlayer, Character? character)
        {
            if (Level.Loaded?.StartOutpost?.Info?.OutpostNPCs == null) { return; }

            if (character != null)
            {
                Speak(character);
                return;
            }

            foreach (Character npc in Level.Loaded.StartOutpost.Info.OutpostNPCs.SelectMany(kpv => kpv.Value))
            {
                if (npc.CampaignInteractionType == CampaignMode.InteractionType.Upgrade)
                {
                    Speak(npc);
                    break;
                }
            }

            void Speak(Character npc)
            {
                ChatMessage message = ChatMessage.Create(npc.Name, text, ChatMessageType.Default, npc);
                if (!isSinglePlayer)
                {
                    GameMain.Client?.AddChatMessage(message);
                }
                else
                {
                    GameMain.GameSession?.CrewManager?.AddSinglePlayerChatMessage(message);
                }
            }
        }
    }
}