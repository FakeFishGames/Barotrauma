#nullable enable
using Barotrauma.Networking;
using System.Linq;

namespace Barotrauma
{
    partial class UpgradeManager
    {
        partial void UpgradeNPCSpeak(string text, bool isSinglePlayer, Character? character)
        {
            if (Level.Loaded?.StartOutpost?.Info?.OutpostNPCs == null) { return; }

            if (character != null)
            {
                character.Speak(text, ChatMessageType.Default);
                return;
            }

            foreach (Character npc in Level.Loaded.StartOutpost.Info.OutpostNPCs.SelectMany(kpv => kpv.Value))
            {
                if (npc.CampaignInteractionType == CampaignMode.InteractionType.Upgrade)
                {
                    npc.Speak(text, ChatMessageType.Default);
                    break;
                }
            }
        }
    }
}