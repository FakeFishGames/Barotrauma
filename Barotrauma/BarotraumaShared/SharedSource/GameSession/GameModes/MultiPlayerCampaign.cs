using Barotrauma.IO;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class MultiPlayerCampaign : CampaignMode
    {
        public const int MinimumInitialMoney = 500;

        private UInt16 lastUpdateID;
        public UInt16 LastUpdateID
        {
            get
            {
#if SERVER
                if (GameMain.Server != null && lastUpdateID < 1) { lastUpdateID++; }
#endif
                return lastUpdateID;
            }
            set { lastUpdateID = value; }
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
                lastUpdateID++; 
#endif
                lastSaveID = value; 
            }
        }
        
        private static byte currentCampaignID;

        public byte CampaignID
        {
            get; set;
        }

        private MultiPlayerCampaign() : base(GameModePreset.MultiPlayerCampaign)
        {
            currentCampaignID++;
            CampaignID = currentCampaignID;
            CampaignMetadata = new CampaignMetadata(this);
            UpgradeManager = new UpgradeManager(this);
            InitCampaignData();
        }

        public static MultiPlayerCampaign StartNew(string mapSeed, SubmarineInfo selectedSub, CampaignSettings settings)
        {
            MultiPlayerCampaign campaign = new MultiPlayerCampaign();
            //only the server generates the map, the clients load it from a save file
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
            {
                campaign.map = new Map(campaign, mapSeed, settings);
                campaign.Settings = settings;
            }
            campaign.InitProjSpecific();
            return campaign;
        }

        public static MultiPlayerCampaign LoadNew(XElement element)
        {
            MultiPlayerCampaign campaign = new MultiPlayerCampaign();
            campaign.Load(element);
            campaign.InitProjSpecific();
            campaign.IsFirstRound = false;
            return campaign;
        }

        partial void InitProjSpecific();
        
        public static string GetCharacterDataSavePath(string savePath)
        {
            return Path.Combine(SaveUtil.MultiplayerSaveFolder, Path.GetFileNameWithoutExtension(savePath) + "_CharacterData.xml");
        }

        public string GetCharacterDataSavePath()
        {
            return GetCharacterDataSavePath(GameMain.GameSession.SavePath);
        }

        /// <summary>
        /// Loads the campaign from an XML element. Creates the map if it hasn't been created yet, otherwise updates the state of the map.
        /// </summary>
        private void Load(XElement element)
        {
            Money = element.GetAttributeInt("money", 0);
            PurchasedLostShuttles = element.GetAttributeBool("purchasedlostshuttles", false);
            PurchasedHullRepairs = element.GetAttributeBool("purchasedhullrepairs", false);
            PurchasedItemRepairs = element.GetAttributeBool("purchaseditemrepairs", false);
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

#if SERVER
            List<SubmarineInfo> availableSubs = new List<SubmarineInfo>();
            List<SubmarineInfo> sourceList = new List<SubmarineInfo>();
            sourceList.AddRange(SubmarineInfo.SavedSubmarines);
#endif

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "campaignsettings":
                        Settings = new CampaignSettings(subElement);
                        break;
                    case "map":
                        if (map == null)
                        {
                            //map not created yet, loading this campaign for the first time
                            map = Map.Load(this, subElement, Settings);
                        }
                        else
                        {
                            //map already created, update it
                            //if we're not downloading the initial save file (LastSaveID > 0), 
                            //show notifications about location type changes
                            map.LoadState(subElement, LastSaveID > 0);
                        }
                        break;
                    case "metadata":
                        CampaignMetadata = new CampaignMetadata(this, subElement);
                        break;
                    case "upgrademanager":
                    case "pendingupgrades":
                        UpgradeManager = new UpgradeManager(this, subElement, isSingleplayer: false);
                        break;
                    case "bots" when GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer:
                        CrewManager.HasBots = subElement.GetAttributeBool("hasbots", false);
                        CrewManager.AddCharacterElements(subElement);
                        CrewManager.ActiveOrdersElement = subElement.GetChildElement("activeorders");
                        break;
                    case "cargo":
                        CargoManager?.LoadPurchasedItems(subElement);
                        break;
                    case "pets":
                        petsElement = subElement;
                        break;
#if SERVER
                    case "availablesubs":
                        foreach (XElement availableSub in subElement.Elements())
                        {
                            string subName = availableSub.GetAttributeString("name", "");
                            SubmarineInfo matchingSub = sourceList.Find(s => s.Name == subName);
                            if (matchingSub != null) { availableSubs.Add(matchingSub); }
                        }
                        break;
                    case "savedexperiencepoints":
                        foreach (XElement savedExp in subElement.Elements())
                        {
                            savedExperiencePoints.Add(new SavedExperiencePoints(savedExp));
                        }
                        break;
#endif
                }
            }

            CampaignMetadata ??= new CampaignMetadata(this);
            UpgradeManager ??= new UpgradeManager(this);

            InitCampaignData();
#if SERVER
            // Fallback if using a save with no available subs assigned, use vanilla submarines
            if (availableSubs.Count == 0)
            {
                GameMain.NetLobbyScreen.CampaignSubmarines.AddRange(sourceList.FindAll(s => s.IsCampaignCompatible && s.IsVanillaSubmarine()));
            }

            GameMain.NetLobbyScreen.CampaignSubmarines = availableSubs;

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
                foreach (XElement subElement in characterDataDoc.Root.Elements())
                {
                    characterData.Add(new CharacterCampaignData(subElement));
                }
            }
#endif
        }

    }
}
