using Barotrauma.Networking;
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

        public string ClientEndPoint
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
        private XElement healthData;

        partial void InitProjSpecific(Client client);
        public CharacterCampaignData(Client client)
        {
            Name = client.Name;
            InitProjSpecific(client);

            healthData = new XElement("health");
            client.Character.CharacterHealth.Save(healthData);
            if (client.Character.Inventory != null)
            {
                itemData = new XElement("inventory");
                client.Character.SaveInventory(client.Character.Inventory, itemData);
            }
        }
        
        public CharacterCampaignData(XElement element)
        {
            Name = element.GetAttributeString("name", "Unnamed");
            ClientEndPoint = element.GetAttributeString("endpoint", null) ?? element.GetAttributeString("ip", "");
            string steamID = element.GetAttributeString("steamid", "");
            if (!string.IsNullOrEmpty(steamID))
            {
                ulong.TryParse(steamID, out ulong parsedID);
                SteamID = parsedID;
            }

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "character":
                    case "characterinfo":
                        CharacterInfo = new CharacterInfo(subElement);
                        break;
                    case "inventory":
                        itemData = subElement;
                        break;
                    case "health":
                        healthData = subElement;
                        break;
                }
            }
        }

        public void Refresh(Character character)
        {
            healthData = new XElement("health");
            character.CharacterHealth.Save(healthData);
            if (character.Inventory != null)
            {
                itemData = new XElement("inventory");
                character.SaveInventory(character.Inventory, itemData);
            }
        }

        public XElement Save()
        {
            XElement element = new XElement("CharacterCampaignData", 
                new XAttribute("name", Name),
                new XAttribute("endpoint", ClientEndPoint),
                new XAttribute("steamid", SteamID));

            CharacterInfo?.Save(element);
            if (itemData != null) { element.Add(itemData); }
            if (healthData != null) { element.Add(healthData); }

            return element;
        }        
    }
}
