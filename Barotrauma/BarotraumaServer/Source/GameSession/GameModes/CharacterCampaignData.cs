using Barotrauma.Items.Components;
using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class CharacterCampaignData
    {
        partial void InitProjSpecific(Client client)
        {
            ClientIP = client.Connection.RemoteEndPoint.Address.ToString();
            SteamID = client.SteamID;
            CharacterInfo = client.CharacterInfo;
        }

        public bool MatchesClient(Client client)
        {
            if (SteamID > 0)
            {
                return SteamID == client.SteamID;
            }
            else
            {
                return ClientIP == client.Connection.RemoteEndPoint.Address.ToString();
            }
        }

        public void SpawnInventoryItems(Inventory inventory)
        {
            foreach (XElement itemElement in itemData.Elements())
            {
                var newItem = Item.Load(itemElement, inventory.Owner.Submarine);
                int slotIndex = itemElement.GetAttributeInt("i", 0);
                if (newItem == null) continue;

                Entity.Spawner.CreateNetworkEvent(newItem, false);
                inventory.TryPutItem(newItem, slotIndex, false, false, null);

                int itemContainerIndex = 0;
                var itemContainers = newItem.GetComponents<ItemContainer>();
                foreach (XElement childInvElement in itemElement.Elements())
                {
                    if (childInvElement.ToString().ToLowerInvariant() != "inventory") continue;
                    if (itemContainerIndex >= itemContainers.Count) break;
                    SpawnInventoryItems(itemContainers[itemContainerIndex].Inventory);
                    itemContainerIndex++;
                }
            }
        }
    }
}
