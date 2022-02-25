using Barotrauma.IO;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
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

            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "campaignsettings":
                        Settings = new CampaignSettings(subElement);
#if CLIENT
                        GameMain.NetworkMember.ServerSettings.MaxMissionCount = Settings.MaxMissionCount;
                        GameMain.NetworkMember.ServerSettings.RadiationEnabled = Settings.RadiationEnabled;
#endif
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
                    case "stats":
                        LoadStats(subElement);
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

            CampaignMetadata ??= new CampaignMetadata(this);
            UpgradeManager ??= new UpgradeManager(this);

            InitCampaignData();
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
            
            List<SubmarineInfo> availableSubs =
                SubmarineInfo.SavedSubmarines
                    .Where(s =>
                        s.IsCampaignCompatible
                        && isSubmarineVisible(s))
                    .ToList();

            if (!availableSubs.Any())
            {
                //None of the available subs were marked as campaign-compatible, just include all visible subs
                availableSubs.AddRange(
                    SubmarineInfo.SavedSubmarines
                        .Where(isSubmarineVisible));
            }

            if (!availableSubs.Any())
            {
                //No subs are visible at all! Just make the selected one available
                availableSubs.Add(GameMain.NetLobbyScreen.SelectedSub);
            }

            return availableSubs;
        }

    }
}
