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
        
        /// <summary>
        /// Sends a message to all clients telling them that all upgrades on the submarine were reset.
        /// </summary>
        /// <remarks>
        /// <param name="newUpgrades"/> is supposed to have a list of reloaded metadata but seeing as
        /// this method is currently only used when switching submarines and that disables the repair NPC
        /// until the next round so currently there's no need for it as we get the new values from the save
        /// file anyways.
        /// </remarks>
        /// <see cref="UpgradeManager.ClientRead"/>
        private void SendUpgradeResetMessage(Dictionary<string, int> newUpgrades)
        {
            foreach (Client c in GameMain.Server.ConnectedClients)
            {
                IWriteMessage outmsg = new WriteOnlyMessage();
                outmsg.Write((byte)ServerPacketHeader.RESET_UPGRADES);
                outmsg.Write(true);
                outmsg.Write(Campaign.Money);
                // outmsg.Write((uint)newUpgrades.Count);
                // foreach (var (key, value) in newUpgrades)
                // {
                //     outmsg.Write(key);
                //     outmsg.Write((byte)value);
                // }
                GameMain.Server?.ServerPeer?.Send(outmsg, c.Connection, DeliveryMethod.Reliable);
            }
        }
    }
}