using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using System.Xml.Linq;
using Lidgren.Network;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class MultiPlayerCampaign : CampaignMode
    {
        private UInt16 lastUpdateID;
        public UInt16 LastUpdateID
        {
            get { if (GameMain.Server != null && lastUpdateID < 1) lastUpdateID++; return lastUpdateID; }
            set { lastUpdateID = value; }
        }

        private UInt16 lastSaveID;
        public UInt16 LastSaveID
        {
            get { if (GameMain.Server != null && lastSaveID < 1) lastSaveID++; return lastSaveID; }
            set { lastSaveID = value; }
        }
        
        public UInt16 PendingSaveID
        {
            get;
            set;
        }

        private static byte currentCampaignID;

        public byte CampaignID
        {
            get; private set;
        }

        public MultiPlayerCampaign(GameModePreset preset, object param) : 
            base(preset, param)
        {
            currentCampaignID++;
            CampaignID = currentCampaignID;
        }
        
        private void SetDelegates()
        {
            if (GameMain.Server != null)
            {
                CargoManager.OnItemsChanged += () => { LastUpdateID++; };
                Map.OnLocationSelected += (loc, connection) => { LastUpdateID++; };
            }
        }

        public override void Start()
        {
            base.Start();

            if (GameMain.Server != null)
            {
                CargoManager.CreateItems();
            }

            lastUpdateID++;
        }


        public override void End(string endMessage = "")
        {
            isRunning = false;

            if (GameMain.Client != null)
            {
                GameMain.GameSession.EndRound("");
#if CLIENT
                GameMain.GameSession.CrewManager.EndRound();
#endif
                return;                
            }
            
            lastUpdateID++;

            bool success = 
                GameMain.Server.ConnectedClients.Any(c => c.InGame && c.Character != null && !c.Character.IsDead) ||
                (GameMain.Server.Character != null && !GameMain.Server.Character.IsDead);

            /*if (success)
            {
                if (subsToLeaveBehind == null || leavingSub == null)
                {
                    DebugConsole.ThrowError("Leaving submarine not selected -> selecting the closest one");

                    leavingSub = GetLeavingSub();

                    subsToLeaveBehind = GetSubsToLeaveBehind(leavingSub);
                }
            }*/

            GameMain.GameSession.EndRound("");

            //TODO: save player inventories between mp campaign rounds

            //remove all items that are in someone's inventory
            foreach (Character c in Character.CharacterList)
            {
                if (c.Inventory == null) continue;
                foreach (Item item in c.Inventory.Items)
                {
                    if (item != null) item.Remove();
                }
            }

#if CLIENT
            GameMain.GameSession.CrewManager.EndRound();
#endif

            if (success)
            {
                bool atEndPosition = Submarine.MainSub.AtEndPosition;

                /*if (leavingSub != Submarine.MainSub && !leavingSub.DockedTo.Contains(Submarine.MainSub))
                {
                    Submarine.MainSub = leavingSub;

                    GameMain.GameSession.Submarine = leavingSub;

                    foreach (Submarine sub in subsToLeaveBehind)
                    {
                        MapEntity.mapEntityList.RemoveAll(e => e.Submarine == sub && e is LinkedSubmarine);
                        LinkedSubmarine.CreateDummy(leavingSub, sub);
                    }
                }*/

                if (atEndPosition)
                {
                    Map.MoveToNextLocation();

                    //select a random location to make sure we've got some destination
                    //to head towards even if the host/clients don't select anything
                    map.SelectRandomLocation(true);
                }

                SaveUtil.SaveGame(GameMain.GameSession.SavePath);
            }
        }

        public static MultiPlayerCampaign LoadNew(XElement element)
        {
            MultiPlayerCampaign campaign = new MultiPlayerCampaign(GameModePreset.list.Find(gm => gm.Name == "Campaign"), null);
            campaign.Load(element);            
            campaign.SetDelegates();
            
            return campaign;
        }

        public void Load(XElement element)
        {
            Money = element.GetAttributeInt("money", 0);

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "map":
                        if (map == null)
                        {
                            map = Map.LoadNew(subElement);
                        }
                        else
                        {
                            map.Load(subElement);
                        }
                        break;
                }
            }
        }

        public override void Save(XElement element)
        {
            XElement modeElement = new XElement("MultiPlayerCampaign");
            modeElement.Add(new XAttribute("money", Money));            
            Map.Save(modeElement);
            element.Add(modeElement);

            lastSaveID++;
        }

        public void ServerWrite(NetBuffer msg, Client c)
        {
            System.Diagnostics.Debug.Assert(map.Locations.Count < UInt16.MaxValue);

            msg.Write(CampaignID);
            msg.Write(lastUpdateID);
            msg.Write(lastSaveID);
            msg.Write(map.Seed);
            msg.Write(map.CurrentLocationIndex == -1 ? UInt16.MaxValue : (UInt16)map.CurrentLocationIndex);
            msg.Write(map.SelectedLocationIndex == -1 ? UInt16.MaxValue : (UInt16)map.SelectedLocationIndex);

            msg.Write(Money);

            msg.Write((UInt16)CargoManager.PurchasedItems.Count);
            foreach (ItemPrefab ip in CargoManager.PurchasedItems)
            {
                msg.Write((UInt16)MapEntityPrefab.List.IndexOf(ip));
            }
        }
        
        public void ServerRead(NetBuffer msg, Client sender)
        {
            UInt16 selectedLocIndex = msg.ReadUInt16();
            UInt16 purchasedItemCount = msg.ReadUInt16();

            List<ItemPrefab> purchasedItems = new List<ItemPrefab>();
            for (int i = 0; i < purchasedItemCount; i++)
            {
                UInt16 itemPrefabIndex = msg.ReadUInt16();
                purchasedItems.Add(MapEntityPrefab.List[itemPrefabIndex] as ItemPrefab);
            }

            if (!sender.HasPermission(ClientPermissions.ManageCampaign))
            {
                DebugConsole.ThrowError("Client \""+sender.Name+"\" does not have a permission to manage the campaign");
                return;
            }

            Map.SelectLocation(selectedLocIndex == UInt16.MaxValue ? -1 : selectedLocIndex);

            List<ItemPrefab> currentItems = new List<ItemPrefab>(CargoManager.PurchasedItems);
            foreach (ItemPrefab ip in currentItems)
            {
                CargoManager.SellItem(ip);
            }

            foreach (ItemPrefab ip in purchasedItems)
            {
                CargoManager.PurchaseItem(ip);
            }
        }
    }
}
