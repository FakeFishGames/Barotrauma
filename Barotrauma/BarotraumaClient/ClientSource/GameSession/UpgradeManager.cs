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

        /// <summary>
        /// Server has notified us that upgrades were reset.
        /// </summary>
        /// <param name="inc"></param>
        /// <see cref="UpgradeManager.SendUpgradeResetMessage"/>
        public void ClientRead(IReadMessage inc)
        {
            bool shouldReset = inc.ReadBoolean();
            int money = inc.ReadInt32();
            // uint length = inc.ReadUInt32();
            //
            // for (int i = 0; i < length; i++)
            // {
            //     string key = inc.ReadString();
            //     byte value = inc.ReadByte();
            //     Metadata.SetValue(key, value);
            // }

            Campaign.Money = money;

            if (shouldReset)
            {
                ResetUpgrades();
            }

            // spentMoney is local, so this message box should only appear for those who have spent money on upgrades
            if (spentMoney > 0)
            {
                GUIMessageBox msgBox = new GUIMessageBox(TextManager.Get("UpgradeRefundTitle"), TextManager.Get("UpgradeRefundBody"), new [] { TextManager.Get("Ok") });
                msgBox.Buttons[0].OnClicked += msgBox.Close;
            }

            spentMoney = 0;
            PendingUpgrades.Clear();
            PurchasedUpgrades.Clear();
            CanUpgrade = false;
        }
    }
}