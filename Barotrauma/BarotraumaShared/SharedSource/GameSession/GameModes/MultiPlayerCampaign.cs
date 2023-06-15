﻿using Barotrauma.Extensions;
using Barotrauma.IO;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class MultiPlayerCampaign : CampaignMode
    {
        [Flags]
        public enum NetFlags : UInt16
        {
            Misc = 0x1,
            MapAndMissions = 0x2,
            UpgradeManager = 0x4,
            SubList = 0x8,
            ItemsInBuyCrate = 0x10,
            ItemsInSellFromSubCrate = 0x20,
            PurchasedItems = 0x80,
            SoldItems = 0x100,
            Reputation = 0x200,
            CharacterInfo = 0x800
        }

        private readonly Dictionary<NetFlags, UInt16> lastUpdateID;

        public UInt16 GetLastUpdateIdForFlag(NetFlags flag)
        {
            if (!ValidateFlag(flag)) { return 0; }
            return lastUpdateID[flag];
        }
        public void SetLastUpdateIdForFlag(NetFlags flag, UInt16 id)
        {
            if (!ValidateFlag(flag)) { return; }
            lastUpdateID[flag] = id;
        }

        public void IncrementLastUpdateIdForFlag(NetFlags flag)
        {
            if (!ValidateFlag(flag)) { return; }
            if (!lastUpdateID.ContainsKey(flag)) { lastUpdateID[flag] = 0; }
            lastUpdateID[flag]++;
        }
        public void IncrementAllLastUpdateIds()
        {
            foreach (NetFlags flag in Enum.GetValues(typeof(NetFlags)))
            {
                if (!lastUpdateID.ContainsKey(flag)) { lastUpdateID[flag] = 0; }
                lastUpdateID[flag]++;
            }
        }

        private static bool ValidateFlag(NetFlags flag)
        {
            if (MathHelper.IsPowerOfTwo((int)flag)) { return true; }
#if DEBUG
            throw new InvalidOperationException($"\"{flag}\" is not a valid campaign update flag.");
#else
            return false;
#endif       
        }


        private UInt16 lastSaveID;
        public UInt16 LastSaveID
        {
            get
            {
#if SERVER
                if (GameMain.Server != null && lastSaveID < 1) { lastSaveID++; }
#endif
                return lastSaveID;
            }
            set
            {
#if SERVER
                //trigger a campaign update to notify the clients of the changed save ID
                IncrementLastUpdateIdForFlag(NetFlags.Misc);
#endif
                lastSaveID = value; 
            }
        }
        
        private static byte currentCampaignID;

        public byte CampaignID
        {
            get; set;
        }

        private MultiPlayerCampaign(CampaignSettings settings) : base(GameModePreset.MultiPlayerCampaign, settings)
        {
            currentCampaignID++;
            lastUpdateID = new Dictionary<NetFlags, ushort>();
            foreach (NetFlags flag in Enum.GetValues(typeof(NetFlags)))
            {
#if SERVER
                //server starts from a higher ID to ensure we send the initial state
                lastUpdateID[flag] = 1;
#else
                lastUpdateID[flag] = 0;
#endif
            }
            CampaignID = currentCampaignID;
            UpgradeManager = new UpgradeManager(this);
            InitFactions();
        }

        public static MultiPlayerCampaign StartNew(string mapSeed, CampaignSettings settings)
        {
            MultiPlayerCampaign campaign = new MultiPlayerCampaign(settings);
            //only the server generates the map, the clients load it from a save file
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
            {
                campaign.Settings = settings;
                campaign.map = new Map(campaign, mapSeed);
            }
            campaign.InitProjSpecific();
            return campaign;
        }

        public static MultiPlayerCampaign LoadNew(XElement element)
        {
            MultiPlayerCampaign campaign = new MultiPlayerCampaign(CampaignSettings.Empty);
            campaign.Load(element);
            campaign.InitProjSpecific();
            campaign.IsFirstRound = false;
            return campaign;
        }

        partial void InitProjSpecific();
                
        public static string GetCharacterDataSavePath(string savePath)
        {
            return Path.Combine(Path.GetDirectoryName(savePath), Path.GetFileNameWithoutExtension(savePath) + "_CharacterData.xml");
        }

        public static string GetCharacterDataSavePath()
        {
            return GetCharacterDataSavePath(GameMain.GameSession.SavePath);
        }

        /// <summary>
        /// Loads the campaign from an XML element. Creates the map if it hasn't been created yet, otherwise updates the state of the map.
        /// </summary>
        private void Load(XElement element)
        {
            PurchasedLostShuttlesInLatestSave = element.GetAttributeBool("purchasedlostshuttles", false);
            PurchasedHullRepairsInLatestSave = element.GetAttributeBool("purchasedhullrepairs", false);
            PurchasedItemRepairsInLatestSave = element.GetAttributeBool("purchaseditemrepairs", false);
            CheatsEnabled = element.GetAttributeBool("cheatsenabled", false);
            if (CheatsEnabled)
            {
                DebugConsole.CheatsEnabled = true;
#if USE_STEAM
                if (!SteamAchievementManager.CheatsEnabled)
                {
                    SteamAchievementManager.CheatsEnabled = true;
#if CLIENT
                    new GUIMessageBox("Cheats enabled", "Cheat commands have been enabled on the server. You will not receive Steam Achievements until you restart the game.");       
#else
                    DebugConsole.NewMessage("Cheat commands have been enabled.", Color.Red);
#endif
                }
#endif
            }

            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case CampaignSettings.LowerCaseSaveElementName:
                        Settings = new CampaignSettings(subElement);
#if CLIENT
                        GameMain.NetworkMember.ServerSettings.CampaignSettings = Settings;
#endif
                        break;
                    case "map":
                        if (map == null)
                        {
                            //map not created yet, loading this campaign for the first time
                            map = Map.Load(this, subElement);
                        }
                        else
                        {
                            //map already created, update it
                            //if we're not downloading the initial save file (LastSaveID > 0), 
                            //show notifications about location type changes
                            map.LoadState(this, subElement, LastSaveID > 0);
                        }
                        break;
                    case "metadata":
                        var prevReputations = Factions.ToDictionary(k => k, v => v.Reputation.Value);
                        CampaignMetadata.Load(subElement);
                        foreach (var faction in Factions)
                        {
                            if (!MathUtils.NearlyEqual(prevReputations[faction], faction.Reputation.Value))
                            {
                                faction.Reputation.OnReputationValueChanged?.Invoke(faction.Reputation);
                                Reputation.OnAnyReputationValueChanged.Invoke(faction.Reputation);
                            }
                        }
                        break;
                    case "upgrademanager":
                    case "pendingupgrades":
                        UpgradeManager = new UpgradeManager(this, subElement, isSingleplayer: false);
                        break;
                    case "bots" when GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer:
                        CrewManager.HasBots = subElement.GetAttributeBool("hasbots", false);
                        CrewManager.AddCharacterElements(subElement);
                        ActiveOrdersElement = subElement.GetChildElement("activeorders");
                        break;
                    case "cargo":
                        CargoManager?.LoadPurchasedItems(subElement);
                        break;
                    case "pets":
                        petsElement = subElement;
                        break;
                    case "stats":
                        LoadStats(subElement);
                        break;
                    case "eventmanager":
                        GameMain.GameSession.EventManager.Load(subElement);
                        break;
                    case Wallet.LowerCaseSaveElementName:
                        Bank = new Wallet(Option<Character>.None(), subElement);
                        break;
#if SERVER
                    case "savedexperiencepoints":
                        foreach (XElement savedExp in subElement.Elements())
                        {
                            savedExperiencePoints.Add(new SavedExperiencePoints(savedExp));
                        }
                        break;
#endif
                }
            }

            int oldMoney = element.GetAttributeInt("money", 0);
            if (oldMoney > 0)
            {
                Bank = new Wallet(Option<Character>.None())
                {
                    Balance = oldMoney
                };
            }

            UpgradeManager ??= new UpgradeManager(this);

#if SERVER
            characterData.Clear();
            string characterDataPath = GetCharacterDataSavePath();
            if (!File.Exists(characterDataPath))
            {
                DebugConsole.ThrowError($"Failed to load the character data for the campaign. Could not find the file \"{characterDataPath}\".");
            }
            else
            {
                var characterDataDoc = XMLExtensions.TryLoadXml(characterDataPath);
                if (characterDataDoc?.Root == null) { return; }
                foreach (var subElement in characterDataDoc.Root.Elements())
                {
                    characterData.Add(new CharacterCampaignData(subElement));
                }
            }
#endif
        }
        
        public static List<SubmarineInfo> GetCampaignSubs()
        {
            bool isSubmarineVisible(SubmarineInfo s)
                => !GameMain.NetworkMember.ServerSettings.HiddenSubs.Any(h
                    => s.Name.Equals(h, StringComparison.OrdinalIgnoreCase));

            var availableSubs = SubmarineInfo.SavedSubmarines;
#if CLIENT
            if (GameMain.Client != null)
            {
                availableSubs = GameMain.Client.ServerSubmarines;
            }
#endif

            List<SubmarineInfo> campaignSubs =
                availableSubs
                    .Where(s =>
                        s.IsCampaignCompatible
                        && isSubmarineVisible(s))
                    .ToList();

            if (!campaignSubs.Any())
            {
                //None of the available subs were marked as campaign-compatible, just include all visible subs
                campaignSubs.AddRange(availableSubs.Where(isSubmarineVisible));
            }

            if (!campaignSubs.Any())
            {
                //No subs are visible at all! Just make the selected one available
                campaignSubs.Add(GameMain.NetLobbyScreen.SelectedSub);
            }

            return campaignSubs;
        }

        private static void WriteItems(IWriteMessage msg, Dictionary<Identifier, List<PurchasedItem>> purchasedItems)
        {
            msg.WriteByte((byte)purchasedItems.Count);
            foreach (var storeItems in purchasedItems)
            {
                msg.WriteIdentifier(storeItems.Key);
                msg.WriteUInt16((UInt16)storeItems.Value.Count);
                foreach (var item in storeItems.Value)
                {
                    msg.WriteIdentifier(item.ItemPrefabIdentifier);
                    msg.WriteRangedInteger(item.Quantity, 0, CargoManager.MaxQuantity);
                }
            }
        }

        private static Dictionary<Identifier, List<PurchasedItem>> ReadPurchasedItems(IReadMessage msg, Client sender)
        {
            var items = new Dictionary<Identifier, List<PurchasedItem>>();
            byte storeCount = msg.ReadByte();
            for (int i = 0; i < storeCount; i++)
            {
                Identifier storeId = msg.ReadIdentifier();
                items.Add(storeId, new List<PurchasedItem>());
                UInt16 itemCount = msg.ReadUInt16();
                for (int j = 0; j < itemCount; j++)
                {
                    Identifier itemId = msg.ReadIdentifier();
                    int quantity = msg.ReadRangedInteger(0, CargoManager.MaxQuantity);
                    items[storeId].Add(new PurchasedItem(itemId, quantity, sender));
                }
            }
            return items;
        }

        private static void WriteItems(IWriteMessage msg, Dictionary<Identifier, List<SoldItem>> soldItems)
        {
            msg.WriteByte((byte)soldItems.Count);
            foreach (var storeItems in soldItems)
            {
                msg.WriteIdentifier(storeItems.Key);
                msg.WriteUInt16((UInt16)storeItems.Value.Count);
                foreach (var item in storeItems.Value)
                {
                    msg.WriteIdentifier(item.ItemPrefab.Identifier);
                    msg.WriteUInt16((UInt16)item.ID);
                    msg.WriteBoolean(item.Removed);
                    msg.WriteByte(item.SellerID);
                    msg.WriteByte((byte)item.Origin);
                }
            }
        }

        private static Dictionary<Identifier, List<SoldItem>> ReadSoldItems(IReadMessage msg)
        {
            var soldItems = new Dictionary<Identifier, List<SoldItem>>();
            byte storeCount = msg.ReadByte();
            for (int i = 0; i < storeCount; i++)
            {
                Identifier storeId = msg.ReadIdentifier();
                soldItems.Add(storeId, new List<SoldItem>());
                UInt16 itemCount = msg.ReadUInt16();
                for (int j = 0; j < itemCount; j++)
                {
                    Identifier prefabId = msg.ReadIdentifier();
                    UInt16 itemId = msg.ReadUInt16();
                    bool removed = msg.ReadBoolean();
                    byte sellerId = msg.ReadByte();
                    byte origin = msg.ReadByte();
                    soldItems[storeId].Add(new SoldItem(ItemPrefab.Prefabs[prefabId], itemId, removed, sellerId, (SoldItem.SellOrigin)origin));
                }
            }
            return soldItems;
        }
    }
}
