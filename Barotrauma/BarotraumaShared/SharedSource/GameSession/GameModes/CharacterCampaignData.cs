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
        public XElement OrderData { get; private set; }

        public void Refresh(Character character)
        {
            healthData = new XElement("health");
            character.CharacterHealth.Save(healthData);
            if (character.Inventory != null)
            {
                itemData = new XElement("inventory");
                Character.SaveInventory(character.Inventory, itemData);
            }
            OrderData = new XElement("orders");
            CharacterInfo.SaveOrderData(character.Info, OrderData);
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
            if (OrderData != null) { element.Add(OrderData); }

            return element;
        }        
    }
}
