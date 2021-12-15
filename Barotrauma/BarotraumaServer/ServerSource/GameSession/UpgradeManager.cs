#nullable enable
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Networking;

namespace Barotrauma
{
    partial class UpgradeManager
    {
        partial void UpgradeNPCSpeak(string text, bool isSinglePlayer, Character? character)
        {
            if (Level.Loaded?.StartOutpost?.Info?.OutpostNPCs == null) { return; }

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