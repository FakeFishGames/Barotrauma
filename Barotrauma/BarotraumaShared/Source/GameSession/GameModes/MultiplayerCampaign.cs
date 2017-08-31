using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    class MultiplayerCampaign : CampaignMode
    {

        public MultiplayerCampaign(GameModePreset preset, object param) : 
            base(preset, param)
        {
        }

#if CLIENT
        public static void StartCampaignSetup()
        {
            var setupBox = new GUIMessageBox("Campaign Setup", "", new string [0], 500, 500);
            setupBox.InnerFrame.Padding = new Vector4(20.0f, 80.0f, 20.0f, 20.0f);

            var newCampaignContainer = new GUIFrame(new Rectangle(0,40,0,0), null, setupBox.InnerFrame);
            var loadCampaignContainer = new GUIFrame(new Rectangle(0, 40, 0, 0), null, setupBox.InnerFrame);

            var campaignSetupUI = new CampaignSetupUI(true, newCampaignContainer, loadCampaignContainer);

            var newCampaignButton = new GUIButton(new Rectangle(0,0,120,20), "New campaign", "", setupBox.InnerFrame);
            newCampaignButton.OnClicked += (btn, obj) =>
            {
                newCampaignContainer.Visible = true;
                loadCampaignContainer.Visible = false;
                return true;
            };

            var loadCampaignButton = new GUIButton(new Rectangle(130, 0, 120, 20), "Load campaign", "", setupBox.InnerFrame);
            loadCampaignButton.OnClicked += (btn, obj) =>
            {
                newCampaignContainer.Visible = false;
                loadCampaignContainer.Visible = true;
                return true;
            };

            loadCampaignContainer.Visible = false;

            campaignSetupUI.StartNewGame = (Submarine sub, string saveName, string mapSeed) =>
            {
                GameMain.GameSession = new GameSession(sub, saveName, GameModePreset.list.Find(g => g.Name == "Campaign"));
                var campaign = ((MultiplayerCampaign)GameMain.GameSession.GameMode);
                campaign.GenerateMap(mapSeed);
                setupBox.Close();

                GameMain.NetLobbyScreen.ToggleCampaignMode(true);
            };

            campaignSetupUI.LoadGame = (string fileName) =>
            {
                SaveUtil.LoadGame(fileName);
                setupBox.Close();

                GameMain.NetLobbyScreen.ToggleCampaignMode(true);
            };
        }

#endif


        public override void End(string endMessage = "")
        {
            isRunning = false;

            bool success = 
                GameMain.Server.ConnectedClients.Any(c => c.inGame && c.Character != null && !c.Character.IsDead) ||
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
                }

                SaveUtil.SaveGame(GameMain.GameSession.SaveFile);
            }


            if (!success)
            {
               /* var summaryScreen = GUIMessageBox.VisibleBox;

                if (summaryScreen != null)
                {
                    summaryScreen = summaryScreen.children[0];
                    summaryScreen.RemoveChild(summaryScreen.children.Find(c => c is GUIButton));

                    var okButton = new GUIButton(new Rectangle(-120, 0, 100, 30), "Load game", Alignment.BottomRight, "", summaryScreen);
                    okButton.OnClicked += GameMain.GameSession.LoadPrevious;
                    okButton.OnClicked += (GUIButton button, object obj) => { GUIMessageBox.MessageBoxes.Remove(GUIMessageBox.VisibleBox); return true; };

                    var quitButton = new GUIButton(new Rectangle(0, 0, 100, 30), "Quit", Alignment.BottomRight, "", summaryScreen);
                    quitButton.OnClicked += GameMain.LobbyScreen.QuitToMainMenu;
                    quitButton.OnClicked += (GUIButton button, object obj) => { GUIMessageBox.MessageBoxes.Remove(GUIMessageBox.VisibleBox); return true; };
                }*/
            }
            
            for (int i = Character.CharacterList.Count - 1; i >= 0; i--)
            {
                Character.CharacterList[i].Remove();
            }

            Submarine.Unload();
        }

        public static MultiplayerCampaign Load(XElement element)
        {
            MultiplayerCampaign campaign = new MultiplayerCampaign(GameModePreset.list.Find(gm => gm.Name == "Campaign"), null);

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "map":
                        campaign.map = Map.Load(subElement);
                        break;
                }
            }

            campaign.Money = ToolBox.GetAttributeInt(element, "money", 0);

            //backwards compatibility with older save files
            if (campaign.map == null)
            {
                string mapSeed = ToolBox.GetAttributeString(element, "mapseed", "a");
                campaign.GenerateMap(mapSeed);
                campaign.map.SetLocation(ToolBox.GetAttributeInt(element, "currentlocation", 0));
            }

            //campaign.savedOnStart = true;
            return campaign;
        }

        public override void Save(XElement element)
        {
            XElement modeElement = new XElement("MultiPlayerCampaign");
            modeElement.Add(new XAttribute("money", Money));            
            Map.Save(modeElement);
            element.Add(modeElement);
        }
    }
}
