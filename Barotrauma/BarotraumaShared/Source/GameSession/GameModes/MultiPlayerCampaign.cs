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

        private List<CharacterCampaignData> characterData = new List<CharacterCampaignData>();

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
#if SERVER
            if (GameMain.Server != null)
            {
                CargoManager.OnItemsChanged += () => { LastUpdateID++; };
                Map.OnLocationSelected += (loc, connection) => { LastUpdateID++; };
            }
#endif
        }

        public override void Start()
        {
            base.Start();            
            lastUpdateID++;
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

#if CLIENT
            success = success || (GameMain.Server.Character != null && !GameMain.Server.Character.IsDead);
#endif

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
            
            foreach (Client c in GameMain.Server.ConnectedClients)
            {
                if (c.HasSpawned)
                {
                    //client has spawned this round -> remove old data (and replace with new one if the client still has an alive character)
                    characterData.RemoveAll(cd => cd.MatchesClient(c));
                }
                
                if (c.Character?.Info != null && !c.Character.IsDead)
                {
                    characterData.Add(new CharacterCampaignData(c));
                }
            }

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
            CheatsEnabled = element.GetAttributeBool("cheatsenabled", false);
            if (CheatsEnabled)
            {
                DebugConsole.CheatsEnabled = true;
                if (GameMain.Config.UseSteam && !SteamAchievementManager.CheatsEnabled)
                {
                    SteamAchievementManager.CheatsEnabled = true;
#if CLIENT
                    new GUIMessageBox("Cheats enabled", "Cheat commands have been enabled on the server. You will not receive Steam Achievements until you restart the game.");       
#else
                    DebugConsole.NewMessage("Cheat commands have been enabled.", Color.Red);
#endif
                }
            }

            characterData.Clear();
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
                    case "characterdata":
                        foreach (XElement characterDataElement in subElement.Elements())
                        {
                            characterData.Add(new CharacterCampaignData(characterDataElement));
                        }
                        break;
                }
            }
        }

        public override void Save(XElement element)
        {
            XElement modeElement = new XElement("MultiPlayerCampaign",
                new XAttribute("money", Money),
                new XAttribute("cheatsenabled", CheatsEnabled));
            Map.Save(modeElement);
            element.Add(modeElement);

            XElement characterDataElement = new XElement("CharacterData");
            foreach (CharacterCampaignData cd in characterData)
            {
                characterDataElement.Add(cd.Save());
            }
            modeElement.Add(characterDataElement);

            lastSaveID++;
        }
    }
}
