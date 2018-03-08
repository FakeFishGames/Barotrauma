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
            if (CampaignID > 127)
            {
                currentCampaignID = 0;
                CampaignID = 0;
            }
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
                (GameMain.Server.ConnectedClients.Any(c => c.InGame && c.Character != null && !c.Character.IsDead) ||
                (GameMain.Server.Character != null && !GameMain.Server.Character.IsDead)) && (!GameMain.NilMod.RoundEnded || Submarine.MainSub.AtEndPosition);

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
                //Character is inside of a submarine and still alive in some form
                if (c.Submarine != null)
                {
                    CheckSubInventory(c.Inventory.Items);
                }
                //Not on the submarine or dead, just remove everything.
                else
                {
                    foreach (Item item in c.Inventory.Items)
                    {
                        if (item != null)
                        {
                            item.Remove();
                        }
                    }
                }
            }

            //Code for removing items from the level which started in a players inventory, makes a bit of a mess though.
            for (int i = Item.ItemList.ToArray().Length - 1; i >= 0; i--)
            {
                if (Item.ItemList[i] == null) continue;
                if (Item.ItemList[i].Submarine == null) continue;
                if (Item.ItemList[i] != null)
                {
                    if (Item.ItemList[i].HasTag("Starter_Item") && Item.ItemList[i].ContainedItems != null)
                    {
                        CheckSubInventory(Item.ItemList[i].ContainedItems);
                        Item.ItemList[i].Remove();
                    }
                    else if (Item.ItemList[i].HasTag("Starter_Item"))
                    {
                        Item.ItemList[i].Remove();
                    }
                }
            }

#if CLIENT
            GameMain.GameSession.CrewManager.EndRound();
            if (GameSession.inGameInfo != null) GameSession.inGameInfo.ResetGUIListData();
#endif

            if (success)
            {
                GameMain.NilMod.CampaignStart = false;
                //Make the save last longer if its being successful.
                if (GameMain.NilMod.CampaignFails > 0)
                {
                    GameMain.NilMod.CampaignFails -= GameMain.NilMod.CampaignSuccessFailReduction;
                    if (GameMain.NilMod.CampaignFails < 0) GameMain.NilMod.CampaignFails = 0;
                }
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

                //Repair submarine walls
                foreach (Structure w in Structure.WallList)
                {
                    for (int i = 0; i < w.SectionCount; i++)
                    {
                        w.AddDamage(i, -100000.0f);
                    }
                }

                //Remove water, replenish oxygen, Extinguish fires
                foreach (Hull hull in Hull.hullList)
                {
                    hull.OxygenPercentage = 100.0f;
                    hull.WaterVolume = 0f;

                    for (int i = hull.FireSources.Count - 1; i >= 0; i--)
                    {
                        hull.FireSources[i].Remove();
                    }
                }

                //Repair devices, electricals and shutdown reactors.
                foreach (Item it in Item.ItemList)
                {
                    if (it.GetComponent<Barotrauma.Items.Components.Powered>() != null ||
                        it.GetComponent<Barotrauma.Items.Components.Reactor>() != null ||
                        it.GetComponent<Barotrauma.Items.Components.Engine>() != null ||
                        it.GetComponent<Barotrauma.Items.Components.Steering>() != null ||
                        it.GetComponent<Barotrauma.Items.Components.Radar>() != null ||
                        it.GetComponent<Barotrauma.Items.Components.MiniMap>() != null ||
                        it.GetComponent<Barotrauma.Items.Components.Door>() != null ||
                        it.GetComponent<Barotrauma.Items.Components.RelayComponent>() != null) it.Condition = it.Prefab.Health;

                    if (it.GetComponent<Barotrauma.Items.Components.Reactor>() != null)
                    {
                        //Compatability for BTE.
                        if (it.Prefab.Name == "Diesel-Electric Generator") continue;
                        Barotrauma.Items.Components.Reactor reactor = it.GetComponent<Barotrauma.Items.Components.Reactor>();
                        reactor.AutoTemp = false;
                        reactor.FissionRate = 0;
                        reactor.CoolingRate = 100;
                        reactor.Temperature = 0;
                    }

                    if (it.GetComponent<Barotrauma.Items.Components.PowerContainer>() != null)
                    {
                        var powerContainer = it.GetComponent<Barotrauma.Items.Components.PowerContainer>();
                        powerContainer.Charge = Math.Min(powerContainer.Capacity * 0.9f, powerContainer.Charge);
                    }
                }

                Money += GameMain.NilMod.CampaignSurvivalReward;

                SaveUtil.SaveGame(GameMain.GameSession.SavePath);
                lastSaveID += 1;
            }
            else
            {
                GameMain.NilMod.CampaignFails += 1;

                if (GameMain.NilMod.CampaignDefaultSaveName != "" && GameMain.Client == null)
                {
                    if (GameMain.NilMod.CampaignFails > GameMain.NilMod.CampaignMaxFails)
                    {
                        GameMain.NilMod.CampaignFails = 0;
                        CoroutineManager.StartCoroutine(ResetCampaignMode(), "ResetCampaign");

                        foreach (Client c in GameMain.Server.ConnectedClients)
                        {
                            NilMod.NilModEventChatter.SendServerMessage("Campaign Info: No more chances remain, the campaign is lost!", c);
                            NilMod.NilModEventChatter.SendServerMessage("Starting a new campaign...", c);
                        }
                        GameMain.NetworkMember.AddChatMessage("Campaign Info: No more chances remain, the campaign is lost!", ChatMessageType.Server, "", null);
                        GameMain.NetworkMember.AddChatMessage("Starting a new campaign...", ChatMessageType.Server, "", null);
                    }
                    else
                    {
                        if ((GameMain.NilMod.CampaignMaxFails - GameMain.NilMod.CampaignFails) < 3)
                        {
                            foreach (Client c in GameMain.Server.ConnectedClients)
                            {
                                NilMod.NilModEventChatter.SendServerMessage("Campaign Info: There are " + (GameMain.NilMod.CampaignMaxFails - GameMain.NilMod.CampaignFails) + " Attempts remaining on this save unless you start pulling off some success!", c);
                            }
                            GameMain.NetworkMember.AddChatMessage("Campaign: There are " + (GameMain.NilMod.CampaignMaxFails - GameMain.NilMod.CampaignFails) + " Attempts remaining on this save unless you start pulling off some success!", ChatMessageType.Server, "", null);
                        }
                        //Reload the game and such
                        SaveUtil.LoadGame(GameMain.GameSession.SavePath);
#if CLIENT
                        GameMain.NetLobbyScreen.modeList.Select(2, true);
#endif
                        GameMain.GameSession.Map.SelectRandomLocation(true);
                        LastSaveID += 1;
                    }
                }
            }

            //If its campaign start, add the starter items to the buy menu
            if (GameMain.NilMod.CampaignAutoPurchase)
            {
                var campaign = ((MultiPlayerCampaign)GameMain.GameSession.GameMode);

                if (GameMain.NilMod.CampaignStart)
                {
                    AutoPurchaseNew();
                }
                //If its a round that wasn't the first, buy the mid-round items!
                else
                {
                    AutoPurchaseExisting();
                }
            }
        }

        public void CheckSubInventory(Item[] items)
        {
            foreach (Item item in items)
            {
                if (item != null)
                {
                    if (!item.HasTag("Starter_Item") && item.ContainedItems != null)
                    {
                        CheckSubInventory(item.ContainedItems);
                        item.Drop();
                        item.FindHull();
                    }
                    else if (!item.HasTag("Starter_Item"))
                    {
                        item.Drop();
                        item.FindHull();
                    }
                    else if (item.HasTag("Starter_Item") && item.ContainedItems != null)
                    {
                        CheckSubInventory(item.ContainedItems);
                        item.Remove();
                    }
                    else
                    {
                        item.Remove();
                    }
                }
            }
        }

        private IEnumerable<object> ResetCampaignMode()
        {
            float waittime = 0f;
            GameMain.NetLobbyScreen.SelectedModeIndex = 0;
#if Client
            GameMain.NetLobbyScreen.modeList.Select(0, true);
#endif

            GameMain.NetLobbyScreen.ToggleCampaignMode(false);
            SaveUtil.DeleteSave(GameMain.GameSession.SavePath);

            GameMain.NilMod.CampaignStart = true;

            if (GameMain.Server.AutoRestart)
            {
                GameMain.Server.AutoRestartTimer += 11f;
            }

            while (waittime < 11f)
            {
                waittime += CoroutineManager.DeltaTime;
                yield return CoroutineStatus.Running;
            }

            StartCampaignSetup(true);
            GameMain.NetLobbyScreen.SelectedModeIndex = 2;
            SetDelegates();

            if (GameMain.Server.AutoRestart)
            {
                GameMain.Server.AutoRestartTimer = GameMain.Server.AutoRestartInterval;
            }

            yield return CoroutineStatus.Success;
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

        public void AutoPurchaseNew()
        {
            if (GameMain.Client != null) return;
            if (GameMain.NilMod.CampaignAutoPurchase)
            {
                var campaign = ((MultiPlayerCampaign)GameMain.GameSession.GameMode);

                int totalcost = 0;
                int totalitems = 0;
                foreach (CampaignPurchase cp in GameMain.NilMod.ServerNewCampaignAutobuy)
                {
                    if (!cp.isvalid) continue;

                    ItemPrefab prefab = (ItemPrefab)ItemPrefab.Find(cp.itemprefab);
                    for (int i = 0; i < cp.count; i++)
                    {
                        if (campaign.Money >= prefab.Price)

                            totalitems += 1;
                        totalcost += prefab.Price;
                        campaign.CargoManager.PurchaseItem(prefab);
                    }
                }

                GameMain.Server.ServerLog.WriteLine("AUTOBUY: Added " + totalitems + " Items costing "
                    + totalcost + " money.", ServerLog.MessageType.ServerMessage);
                campaign.LastUpdateID++;
            }
        }

        public void AutoPurchaseExisting()
        {
            if (GameMain.Client != null) return;
            var campaign = ((MultiPlayerCampaign)GameMain.GameSession.GameMode);

            int totalitems = 0;
            int totalcost = 0;
            foreach (CampaignPurchase cp in GameMain.NilMod.ServerExistingCampaignAutobuy)
            {
                if (!cp.isvalid) continue;

                ItemPrefab prefab = (ItemPrefab)ItemPrefab.Find(cp.itemprefab);
                for (int i = 0; i < cp.count; i++)
                {
                    if (campaign.Money >= prefab.Price)
                    {
                        totalitems += 1;
                        totalcost += prefab.Price;
                        campaign.CargoManager.PurchaseItem(prefab);
                    }
                }
            }
            GameMain.Server.ServerLog.WriteLine("AUTOBUY: Added " + totalitems + " Items costing "
                        + totalcost + " money.", ServerLog.MessageType.ServerMessage);
            LastUpdateID++;
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
                DebugConsole.ThrowError("Client \"" + sender.Name + "\" does not have a permission to manage the campaign");
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
