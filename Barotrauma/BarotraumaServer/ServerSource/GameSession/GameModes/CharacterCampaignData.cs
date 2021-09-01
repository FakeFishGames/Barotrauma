using Barotrauma.Networking;

namespace Barotrauma
{
    partial class CharacterCampaignData
    {
        public bool HasSpawned;

        public bool HasItemData
        {
            get { return itemData != null; }
        }

        partial void InitProjSpecific(Client client)
        {
            ClientEndPoint = client.Connection.EndPointString;
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
                return ClientEndPoint == client.Connection.EndPointString;
            }
        }

        public bool IsDuplicate(CharacterCampaignData other)
        {
            return other.SteamID == SteamID && other.ClientEndPoint == ClientEndPoint;
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
            character.SpawnInventoryItems(inventory, itemData);
        }

        public void ApplyHealthData(Character character)
        {            
            CharacterInfo.ApplyHealthData(character, healthData);
        }

        public void ApplyOrderData(Character character)
        {
            CharacterInfo.ApplyOrderData(character, OrderData);
        } 
    }
}
