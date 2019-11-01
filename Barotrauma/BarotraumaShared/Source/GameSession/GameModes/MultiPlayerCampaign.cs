using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;
using System.IO;

namespace Barotrauma
{
    partial class MultiPlayerCampaign : CampaignMode
    {
        private UInt16 lastUpdateID;
        public UInt16 LastUpdateID
        {
            get
            {
#if SERVER
                if (GameMain.Server != null && lastUpdateID < 1) lastUpdateID++;
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
                if (GameMain.Server != null && lastSaveID < 1) lastSaveID++;
#endif
                return lastSaveID;
            }
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
        
        public override void Start()
        {
            base.Start();            
            if (GameMain.NetworkMember.IsServer) lastUpdateID++;
        }
        
        public override void End(string endMessage = "")
        {
            isRunning = false;

#if CLIENT
            if (GameMain.Client != null)
            {
                GameMain.GameSession.EndRound("");
                GameMain.GameSession.CrewManager.EndRound();
                return;                
            }
#endif

#if SERVER
            lastUpdateID++;

            bool success =
                GameMain.Server.ConnectedClients.Any(c => c.InGame && c.Character != null && !c.Character.IsDead);
            
            success = success || (GameMain.Server.Character != null && !GameMain.Server.Character.IsDead);

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
            
            //client character has spawned this round -> remove old data (and replace with an up-to-date one if the client still has an alive character)
            characterData.RemoveAll(cd => cd.HasSpawned);
            
            foreach (Client c in GameMain.Server.ConnectedClients)
            {
                if (c.Character?.Info != null && !c.Character.IsDead)
                {
                    c.CharacterInfo = c.Character.Info;
                    characterData.Add(new CharacterCampaignData(c));
                }
            }

            //remove all items that are in someone's inventory
            foreach (Character c in Character.CharacterList)
            {
                c.Inventory?.DeleteAllItems();
            }
            
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
                    map.MoveToNextLocation();

                    //select a random location to make sure we've got some destination
                    //to head towards even if the host/clients don't select anything
                    map.SelectRandomLocation(true);
                }
                map.ProgressWorld();

                SaveUtil.SaveGame(GameMain.GameSession.SavePath);
            }
#endif
        }

        partial void SetDelegates();

        public static MultiPlayerCampaign LoadNew(XElement element)
        {
            MultiPlayerCampaign campaign = new MultiPlayerCampaign(GameModePreset.List.Find(gm => gm.Identifier == "multiplayercampaign"), null);
            campaign.Load(element);
            campaign.SetDelegates();
            
            return campaign;
        }

        public static string GetCharacterDataSavePath(string savePath)
        {
            return Path.Combine(SaveUtil.MultiplayerSaveFolder, Path.GetFileNameWithoutExtension(savePath) + "_CharacterData.xml");
        }

        public string GetCharacterDataSavePath()
        {
            return GetCharacterDataSavePath(GameMain.GameSession.SavePath);
        }

        public void Load(XElement element)
        {
            Money = element.GetAttributeInt("money", 0);
            CheatsEnabled = element.GetAttributeBool("cheatsenabled", false);
            if (CheatsEnabled)
            {
                DebugConsole.CheatsEnabled = true;
                if (Steam.SteamManager.USE_STEAM && !SteamAchievementManager.CheatsEnabled)
                {
                    SteamAchievementManager.CheatsEnabled = true;
#if CLIENT
                    new GUIMessageBox("Cheats enabled", "Cheat commands have been enabled on the server. You will not receive Steam Achievements until you restart the game.");       
#else
                    DebugConsole.NewMessage("Cheat commands have been enabled.", Color.Red);
#endif
                }
            }

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "map":
                        if (map == null)
                        {
                            //map not created yet, loading this campaign for the first time
                            map = Map.LoadNew(subElement);
                        }
                        else
                        {
                            //map already created, update it
                            //if we're not downloading the initial save file (LastSaveID > 0), 
                            //show notifications about location type changes
                            map.Load(subElement, LastSaveID > 0);
                        }
                        break;
                }
            }
#if SERVER
            characterData.Clear();
            string characterDataPath = GetCharacterDataSavePath();
            var characterDataDoc = XMLExtensions.TryLoadXml(characterDataPath);
            if (characterDataDoc?.Root == null) return;
            foreach (XElement subElement in characterDataDoc.Root.Elements())
            {
                characterData.Add(new CharacterCampaignData(subElement));
            }
#endif
        }

    }
}
