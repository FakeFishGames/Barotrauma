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

        public void SpawnInventoryItems(CharacterInfo characterInfo, Inventory inventory)
        {
            characterInfo.SpawnInventoryItems(inventory, itemData);
        }
    }
}
