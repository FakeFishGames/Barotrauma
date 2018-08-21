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
        public CharacterInfo CharacterInfo
        {
            get;
            private set;
        }

        public readonly string Name;

        public string ClientIP
        {
            get;
            private set;
        }
        public ulong SteamID
        {
            get;
            private set;
        }

        private XElement itemData;

        partial void InitProjSpecific(Client client);
        public CharacterCampaignData(Client client)
        {
            Name = client.Name;
            InitProjSpecific(client);

            if (client.Character.Inventory != null)
            {
                itemData = new XElement("inventory");
                SaveInventory(client.Character.Inventory, itemData);
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
            Name = element.GetAttributeString("name", "Unnamed");
            ClientIP = element.GetAttributeString("ip", "");
            string steamID = element.GetAttributeString("steamid", "");
            if (!string.IsNullOrEmpty(steamID))
            {
                ulong parsedID;
                ulong.TryParse(steamID, out parsedID);
                SteamID = parsedID;
            }

            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() == "characterinfo")
                {
                    CharacterInfo = new CharacterInfo(subElement);
                    CharacterInfo.PickedItemIDs.Clear();
                    break;
                }
            }
        }

        public XElement Save()
        {
            XElement element = new XElement("CharacterCampaignData", 
                new XAttribute("name", Name),
                new XAttribute("ip", ClientIP),
                new XAttribute("steamid", SteamID));

            CharacterInfo?.Save(element);

            return element;
        }
    }
}
