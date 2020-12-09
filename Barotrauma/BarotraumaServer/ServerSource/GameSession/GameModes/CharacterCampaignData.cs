using Barotrauma.Networking;

namespace Barotrauma
{
    partial class CharacterCampaignData
    {
        public bool HasSpawned;

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
            character.SpawnInventoryItems(inventory, itemData);
        }

        public void ApplyHealthData(CharacterInfo characterInfo, Character character)
        {            
            characterInfo.ApplyHealthData(character, healthData);
        }
    }
}
