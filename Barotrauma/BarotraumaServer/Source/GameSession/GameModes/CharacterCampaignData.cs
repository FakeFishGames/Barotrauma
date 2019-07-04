using Barotrauma.Networking;

namespace Barotrauma
{
    partial class CharacterCampaignData
    {
        partial void InitProjSpecific(Client client)
        {
            ClientIP = client.Connection.IP.ToString();
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
                return ClientIP == client.Connection.IP.ToString();
            }
        }

        public void SpawnInventoryItems(CharacterInfo characterInfo, Inventory inventory)
        {
            characterInfo.SpawnInventoryItems(inventory, itemData);
        }
    }
}
