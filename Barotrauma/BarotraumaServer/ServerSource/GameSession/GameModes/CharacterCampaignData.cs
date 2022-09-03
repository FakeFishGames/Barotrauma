using Barotrauma.Networking;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class CharacterCampaignData
    {
        public bool HasSpawned;

        public bool HasItemData
        {
            get { return itemData != null; }
        }

        public CharacterCampaignData(Client client)
        {
            Name = client.Name;
            ClientAddress = client.Connection.Endpoint.Address;
            AccountId = client.AccountId;
            CharacterInfo = client.CharacterInfo;

            healthData = new XElement("health");
            client.Character?.CharacterHealth?.Save(healthData);
            if (client.Character?.Inventory != null)
            {
                itemData = new XElement("inventory");
                Character.SaveInventory(client.Character.Inventory, itemData);
            }
            OrderData = new XElement("orders");
            if (client.CharacterInfo != null)
            {
                CharacterInfo.SaveOrderData(client.CharacterInfo, OrderData);
            }

            if (client.Character?.Wallet.Save() is { } walletSave)
            {
                WalletData = walletSave;
            }
        }

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
            WalletData = character.Wallet.Save();
        }

        public CharacterCampaignData(XElement element)
        {
            Name = element.GetAttributeString("name", "Unnamed");
            string clientEndPointStr = element.GetAttributeString("address", null)
                                       ?? element.GetAttributeString("endpoint", null)
                                       ?? element.GetAttributeString("ip", "");
            ClientAddress = Address.Parse(clientEndPointStr).Fallback(new UnknownAddress());
            string accountIdStr = element.GetAttributeString("accountid", null)
                               ?? element.GetAttributeString("steamid", "");
            AccountId = Networking.AccountId.Parse(accountIdStr);

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
                    case "orders":
                        OrderData = subElement;
                        break;
                    case Wallet.LowerCaseSaveElementName:
                        WalletData = subElement;
                        break;
                }
            }
        }

        public bool MatchesClient(Client client)
        {
            if (AccountId.TryUnwrap(out var accountId)
                && client.AccountId.TryUnwrap(out var clientId))
            {
                return accountId == clientId;
            }
            else
            {
                return ClientAddress == client.Connection.Endpoint.Address;
            }
        }

        public bool IsDuplicate(CharacterCampaignData other)
        {
            return AccountId == other.AccountId && other.ClientAddress == ClientAddress;
        }

        public void SpawnInventoryItems(Character character, Inventory inventory)
        {
            if (character == null)
            {
                throw new System.InvalidOperationException($"Failed to spawn inventory items. Character was null.");
            }
            if (itemData == null)
            {
                throw new System.InvalidOperationException($"Failed to spawn inventory items for the character \"{character.Name}\". No saved inventory data.");
            }
            character.SpawnInventoryItems(inventory, itemData.FromContent(ContentPath.Empty));
        }

        public void ApplyHealthData(Character character)
        {
            CharacterInfo.ApplyHealthData(character, healthData);
        }

        public void ApplyOrderData(Character character)
        {
            CharacterInfo.ApplyOrderData(character, OrderData);
        }

        public void ApplyWalletData(Character character)
        {
            character.Wallet = new Wallet(Option<Character>.Some(character), WalletData);
        }

        public XElement Save()
        {
            XElement element = new XElement("CharacterCampaignData",
                new XAttribute("name", Name),
                new XAttribute("address", ClientAddress),
                new XAttribute("accountid", AccountId.TryUnwrap(out var accountId) ? accountId.StringRepresentation : ""));

            CharacterInfo?.Save(element);
            if (itemData != null) { element.Add(itemData); }
            if (healthData != null) { element.Add(healthData); }
            if (OrderData != null) { element.Add(OrderData); }
            if (WalletData != null) { element.Add(WalletData); }

            return element;
        }
    }
}
