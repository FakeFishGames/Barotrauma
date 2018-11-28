using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Lidgren.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    class CharacterCampaignData
    {
        public readonly CharacterInfo CharacterInfo;

        public readonly string Name;

        public readonly bool IsHostCharacter;

        public readonly string ClientIP;
        public readonly ulong SteamID;

        private XElement itemData;

        public CharacterCampaignData(Client client)
        {
            Name = client.Name;
            ClientIP = client.Connection.RemoteEndPoint.Address.ToString();
            SteamID = client.SteamID;
            CharacterInfo = client.CharacterInfo;

            if (client.Character.Inventory != null)
            {
                itemData = new XElement("inventory");
                SaveInventory(client.Character.Inventory, itemData);
            }
        }

        public CharacterCampaignData(GameServer server)
        {
            Name = server.Character.Name;
            CharacterInfo = server.Character.Info;
            IsHostCharacter = true;

            if (server.Character.Inventory != null)
            {
                itemData = new XElement("inventory");
                SaveInventory(server.Character.Inventory, itemData);
            }
        }

        private void SaveInventory(Inventory inventory, XElement parentElement)
        {
            var items = Array.FindAll(inventory.Items, i => i != null).Distinct();
            foreach (Item item in items)
            {
                item.Submarine = inventory.Owner.Submarine;
                var itemElement = item.Save(parentElement);
                itemElement.Add(new XAttribute("i",  Array.IndexOf(inventory.Items, item)));

                foreach (ItemContainer container in item.GetComponents<ItemContainer>())
                {
                    XElement childInvElement = new XElement("inventory");
                    itemElement.Add(childInvElement);
                    SaveInventory(container.Inventory, childInvElement);
                }
            }
        }

        public CharacterCampaignData(XElement element)
        {
            Name            = element.GetAttributeString("name", "Unnamed");
            IsHostCharacter = element.GetAttributeBool("host", false);
            if (!IsHostCharacter)
            {
                ClientIP        = element.GetAttributeString("ip", "");
                string steamID  = element.GetAttributeString("steamid", "");
                if (!string.IsNullOrEmpty(steamID))
                {
                    ulong.TryParse(steamID, out SteamID);
                }
            }

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "character":
                    case "characterinfo":
                        CharacterInfo = new CharacterInfo(subElement);
                        CharacterInfo.PickedItemIDs.Clear();
                        break;
                    case "inventory":
                        itemData = subElement;
                        break;
                }
            }
        }

        public bool MatchesClient(Client client)
        {
            if (IsHostCharacter) return false;
            if (SteamID > 0)
            {
                return SteamID == client.SteamID;
            }
            else
            {
                return ClientIP == client.Connection.RemoteEndPoint.Address.ToString();
            }
        }

        public XElement Save()
        {
            XElement element = new XElement("CharacterCampaignData",
                new XAttribute("name", Name));

            if (IsHostCharacter)
            {
                element.Add(new XAttribute("host", true));
            }
            else
            {
                element.Add(new XAttribute("ip", ClientIP));
                element.Add(new XAttribute("steamid", SteamID));
            }

            CharacterInfo?.Save(element);

            if (itemData != null)
            {
                element.Add(itemData);
            }

            return element;
        }

        public void SpawnInventoryItems(Inventory inventory)
        {
            SpawnInventoryItems(inventory, itemData);
        }

        private void SpawnInventoryItems(Inventory inventory, XElement element)
        {
            foreach (XElement itemElement in element.Elements())
            {
                var newItem = Item.Load(itemElement, inventory.Owner.Submarine);
                int slotIndex = itemElement.GetAttributeInt("i", 0);
                if (newItem == null) continue;

                Entity.Spawner.CreateNetworkEvent(newItem, false);
                inventory.TryPutItem(newItem, slotIndex, false, false, null);

                int itemContainerIndex = 0;
                var itemContainers = newItem.GetComponents<ItemContainer>().ToList();
                foreach (XElement childInvElement in itemElement.Elements())
                {
                    if (itemContainerIndex >= itemContainers.Count) break;
                    if (childInvElement.Name.ToString().ToLowerInvariant() != "inventory") continue;
                    SpawnInventoryItems(itemContainers[itemContainerIndex].Inventory, childInvElement);
                    itemContainerIndex++;
                }
            }
        }        
    }
}
