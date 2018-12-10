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
                client.Character.SaveInventory(client.Character.Inventory, itemData);
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
                server.Character.SaveInventory(server.Character.Inventory, itemData);
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

        public void SpawnInventoryItems(CharacterInfo characterInfo, Inventory inventory)
        {
            characterInfo.SpawnInventoryItems(inventory, itemData);
        }       
    }
}
