using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Subsurface.Networking;
using System.IO;
using System.Xml.Linq;

namespace Subsurface
{
    class MainMenuScreen : Screen
    {
        public enum Tabs { Main = 0, NewGame = 1, LoadGame = 2, HostServer = 3 }

        private GUIFrame[] menuTabs;
        private GUIListBox mapList;

        private GUIListBox saveList;

        private GUITextBox saveNameBox, seedBox;

        private GUITextBox clientNameBox, ipBox;

        private GUITextBox serverNameBox, portBox;

        private Game1 game;

        int selectedTab;

        public MainMenuScreen(Game1 game)
        {
            menuTabs = new GUIFrame[Enum.GetValues(typeof(Tabs)).Length];

            Rectangle panelRect = new Rectangle(                
                Game1.GraphicsWidth / 2 - 250,
                Game1.GraphicsHeight/ 2 - 250,
                500, 500);

            menuTabs[(int)Tabs.Main] = new GUIFrame(panelRect, GUI.style);
            //menuTabs[(int)Tabs.Main].Padding = GUI.style.smallPadding;

            GUIButton button = new GUIButton(new Rectangle(0, 0, 0, 30), "Tutorial", Alignment.CenterX, GUI.style, menuTabs[(int)Tabs.Main]);
            button.OnClicked = TutorialButtonClicked;

            button = new GUIButton(new Rectangle(0, 70, 0, 30), "New Game", Alignment.CenterX, GUI.style, menuTabs[(int)Tabs.Main]);
            button.UserData = (int)Tabs.NewGame;
            button.OnClicked = SelectTab;
            //button.Enabled = false;

            button = new GUIButton(new Rectangle(0, 130, 0, 30), "Load Game", Alignment.CenterX, GUI.style, menuTabs[(int)Tabs.Main]);
            button.UserData = (int)Tabs.LoadGame;
            button.OnClicked = SelectTab;
            //button.Enabled = false;

            button = new GUIButton(new Rectangle(0, 200, 0, 30), "Join Server", Alignment.CenterX, GUI.style, menuTabs[(int)Tabs.Main]);
            //button.UserData = (int)Tabs.JoinServer;
            button.OnClicked = JoinServerClicked;

            button = new GUIButton(new Rectangle(0, 260, 0, 30), "Host Server", Alignment.CenterX, GUI.style, menuTabs[(int)Tabs.Main]);
            button.UserData = (int)Tabs.HostServer;
            button.OnClicked = SelectTab;
            //button.Enabled = false;

            button = new GUIButton(new Rectangle(0, 330, 0, 30), "Quit", Alignment.CenterX, GUI.style, menuTabs[(int)Tabs.Main]);
            button.OnClicked = QuitClicked;

            //----------------------------------------------------------------------

            menuTabs[(int)Tabs.NewGame] = new GUIFrame(panelRect, GUI.style);
            //menuTabs[(int)Tabs.NewGame].Padding = GUI.style.smallPadding;

            new GUITextBlock(new Rectangle(0, -20, 0, 30), "New Game", null, null, Alignment.CenterX, GUI.style, menuTabs[(int)Tabs.NewGame]);

            new GUITextBlock(new Rectangle(0, 30, 0, 30), "Selected submarine:", null, null, Alignment.Left, GUI.style, menuTabs[(int)Tabs.NewGame]);
            mapList = new GUIListBox(new Rectangle(0, 60, 200, 360), GUI.style, menuTabs[(int)Tabs.NewGame]);

            foreach (Submarine sub in Submarine.SavedSubmarines)
            {
                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(0, 0, 0, 25),
                    sub.Name, 
                    GUI.style,
                    Alignment.Left, Alignment.Left, mapList);
                textBlock.Padding = new Vector4(10.0f, 0.0f, 0.0f, 0.0f);
                textBlock.UserData = sub;
            }
            if (Submarine.SavedSubmarines.Count > 0) mapList.Select(Submarine.SavedSubmarines[0]);

            new GUITextBlock(new Rectangle((int)(mapList.Rect.Width + 20), 30, 100, 20),
                "Save name: ", GUI.style, Alignment.Left, Alignment.Left, menuTabs[(int)Tabs.NewGame]);

            saveNameBox = new GUITextBox(new Rectangle((int)(mapList.Rect.Width + 20), 60, 180, 20),
                Alignment.TopLeft, GUI.style, menuTabs[(int)Tabs.NewGame]);
            saveNameBox.Text = SaveUtil.CreateSavePath();

            new GUITextBlock(new Rectangle((int)(mapList.Rect.Width + 20), 90, 100, 20),
                "Map Seed: ", GUI.style, Alignment.Left, Alignment.Left, menuTabs[(int)Tabs.NewGame]);

            seedBox = new GUITextBox(new Rectangle((int)(mapList.Rect.Width + 20), 120, 180, 20),
                Alignment.TopLeft, GUI.style, menuTabs[(int)Tabs.NewGame]);
            seedBox.Text = ToolBox.RandomSeed(8);


            button = new GUIButton(new Rectangle(0, 0, 100, 30), "Start", Alignment.BottomRight, GUI.style,  menuTabs[(int)Tabs.NewGame]);
            button.OnClicked = StartGame;

            //----------------------------------------------------------------------

            menuTabs[(int)Tabs.LoadGame] = new GUIFrame(panelRect, GUI.style);
            //menuTabs[(int)Tabs.LoadGame].Padding = GUI.style.smallPadding;


            menuTabs[(int)Tabs.HostServer] = new GUIFrame(panelRect, GUI.style);
            //menuTabs[(int)Tabs.JoinServer].Padding = GUI.style.smallPadding;

            new GUITextBlock(new Rectangle(0, 0, 0, 30), "Host Server", GUI.style, Alignment.CenterX, Alignment.CenterX, menuTabs[(int)Tabs.HostServer]);

            new GUITextBlock(new Rectangle(0, 30, 0, 30), "Server Name:", GUI.style, Alignment.CenterX, Alignment.CenterX, menuTabs[(int)Tabs.HostServer]);
            serverNameBox = new GUITextBox(new Rectangle(0, 60, 200, 30), Color.White, Color.Black, Alignment.CenterX, Alignment.CenterX, null, menuTabs[(int)Tabs.HostServer]);

            new GUITextBlock(new Rectangle(0, 100, 0, 30), "Server port:", GUI.style, Alignment.CenterX, Alignment.CenterX, menuTabs[(int)Tabs.HostServer]);
            portBox = new GUITextBox(new Rectangle(0, 130, 200, 30), Color.White, Color.Black, Alignment.CenterX, Alignment.CenterX, null, menuTabs[(int)Tabs.HostServer]);
            portBox.Text = NetworkMember.DefaultPort.ToString();
            portBox.ToolTip = "Server port";

            GUIButton hostButton = new GUIButton(new Rectangle(0, 0, 200, 30), "Start", Alignment.BottomCenter, GUI.style, menuTabs[(int)Tabs.HostServer]);
            hostButton.OnClicked = HostServerClicked;

            //----------------------------------------------------------------------
            for (int i = 1; i < 4; i++ )
            {
                button = new GUIButton(new Rectangle(-20, -20, 100, 30), "Back", Alignment.TopLeft, GUI.style, menuTabs[i]);
                button.OnClicked = PreviousTab;
            }

            this.game = game;

        }

        public bool SelectTab(GUIButton button, object obj)
        {
            selectedTab = (int)obj;

            if (selectedTab == (int)Tabs.LoadGame) UpdateLoadScreen();

            this.Select();
            return true;
        }

        private bool TutorialButtonClicked(GUIButton button, object obj)
        {
            TutorialMode.Start();

            return true;
        }

        private bool JoinServerClicked(GUIButton button, object obj)
        {            
            Game1.ServerListScreen.Select();
            return true;
        }

        private bool HostServerClicked(GUIButton button, object obj)
        {
            string name = serverNameBox.Text;
            if (string.IsNullOrEmpty(name))
            {
                serverNameBox.Flash();
                return false;
            }

            int port;
            if (!int.TryParse(portBox.Text, out port) || port < 0 || port > 65535)
            {
                portBox.Text = NetworkMember.DefaultPort.ToString();
                portBox.Flash();

                return false;
            }

            Game1.NetworkMember = new GameServer(name, port);
            
            Game1.NetLobbyScreen.IsServer = true;
            Game1.NetLobbyScreen.Select();
            return true;
        }

        private bool QuitClicked(GUIButton button, object obj)
        {
            game.Exit();
            return true;
        }

        private void UpdateLoadScreen()
        {
            menuTabs[(int)Tabs.LoadGame].ClearChildren();

            new GUITextBlock(new Rectangle(0, 0, 0, 30), "Load Game", GUI.style, Alignment.CenterX, Alignment.CenterX, menuTabs[(int)Tabs.LoadGame]);

            string[] saveFiles = SaveUtil.GetSaveFiles();

            saveList = new GUIListBox(new Rectangle(0, 60, 200, 360), Color.White, GUI.style, menuTabs[(int)Tabs.LoadGame]);
            saveList.OnSelected = SelectSaveFile;

            foreach (string saveFile in saveFiles)
            {
                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(0, 0, 0, 25),
                    saveFile,
                    GUI.style,
                    Alignment.Left,
                    Alignment.Left,
                    saveList);
                textBlock.Padding = new Vector4(10.0f, 0.0f, 0.0f, 0.0f);
                textBlock.UserData = saveFile;
            }

            var button = new GUIButton(new Rectangle(0, 0, 100, 30), "Start", Alignment.Right | Alignment.Bottom, GUI.style, menuTabs[(int)Tabs.LoadGame]);
            button.OnClicked = LoadGame;

            button = new GUIButton(new Rectangle(-20, -20, 100, 30), "Back", Alignment.TopLeft, GUI.style, menuTabs[(int)Tabs.LoadGame]);
            button.OnClicked = PreviousTab;
        }

        private bool SelectSaveFile(object obj)
        {
            string fileName = (string)obj;
            
            XDocument doc = SaveUtil.LoadGameSessionDoc(fileName);

            if (doc==null)
            {
                DebugConsole.ThrowError("Error loading save file ''"+fileName+"''. The file may be corrupted.");
                return false;
            }

            RemoveSaveFrame();

            string saveTime = ToolBox.GetAttributeString(doc.Root, "savetime", "unknown");

            XElement modeElement = null;
            foreach (XElement element in doc.Root.Elements())
            {
                if (element.Name.ToString().ToLower() != "gamemode") continue;

                modeElement = element;
                break;
            }

            string mapseed = ToolBox.GetAttributeString(modeElement, "mapseed", "unknown");

            GUIFrame saveFileFrame = new GUIFrame(new Rectangle((int)(saveList.Rect.Width + 20), 60, 200, 200), Color.Black*0.4f, GUI.style, menuTabs[(int)Tabs.LoadGame]);
            saveFileFrame.UserData = "savefileframe";
            saveFileFrame.Padding = new Vector4(20.0f, 20.0f, 20.0f, 20.0f);

            new GUITextBlock(new Rectangle(0,0,0,20), fileName, GUI.style, saveFileFrame);

            new GUITextBlock(new Rectangle(0, 30, 0, 20), "Last saved: ", GUI.style, saveFileFrame).Font = GUI.SmallFont;
            new GUITextBlock(new Rectangle(15, 45, 0, 20), saveTime, GUI.style, saveFileFrame).Font = GUI.SmallFont;

            new GUITextBlock(new Rectangle(0, 65, 0, 20), "Map seed: ", GUI.style, saveFileFrame).Font = GUI.SmallFont;
            new GUITextBlock(new Rectangle(15, 80, 0, 20), mapseed, GUI.style, saveFileFrame).Font = GUI.SmallFont;

            var deleteSaveButton = new GUIButton(new Rectangle(0, 0, 100, 20), "Delete", Alignment.BottomCenter, GUI.style, saveFileFrame);
            deleteSaveButton.UserData = fileName;
            deleteSaveButton.OnClicked = DeleteSave;

            return true;
        }

        private bool DeleteSave(GUIButton button, object obj)
        {
            string saveFile = obj as string;

            if (obj == null) return false;

            SaveUtil.DeleteSave(saveFile);

            UpdateLoadScreen();

            return true;
        }

        private void RemoveSaveFrame()
        {
            GUIComponent prevFrame = null;
            foreach (GUIComponent child in menuTabs[(int)Tabs.LoadGame].children)
            {
                if (child.UserData as string != "savefileframe") continue;

                prevFrame = child;
                break;
            }
            menuTabs[(int)Tabs.LoadGame].RemoveChild(prevFrame);
        }

        public override void Update(double deltaTime)
        {
            menuTabs[selectedTab].Update((float)deltaTime);

            Game1.TitleScreen.Position.Y = MathHelper.Lerp(Game1.TitleScreen.Position.Y, -870.0f, 0.1f);
        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            graphics.Clear(Color.CornflowerBlue);

            Game1.TitleScreen.Draw(spriteBatch, graphics, -1.0f, (float)deltaTime);

            //Game1.GameScreen.DrawMap(graphics, spriteBatch);
            
            spriteBatch.Begin();

            menuTabs[selectedTab].Draw(spriteBatch);

            GUI.Draw((float)deltaTime, spriteBatch, null);

            spriteBatch.DrawString(GUI.Font, "Subsurface v"+Game1.Version, new Vector2(10, Game1.GraphicsHeight-20), Color.White);

            spriteBatch.End();
        }

        private bool StartGame(GUIButton button, object obj)
        {
            if (string.IsNullOrEmpty(saveNameBox.Text)) return false;

            string[] existingSaveFiles = SaveUtil.GetSaveFiles();

            if (Array.Find(existingSaveFiles, s => s == saveNameBox.Text)!=null)
            {
                new GUIMessageBox("Save name already in use", "Please choose another name for the save file");
                return false;
            }

            Submarine selectedSub = mapList.SelectedData as Submarine;
            if (selectedSub == null) return false;

            Game1.GameSession = new GameSession(selectedSub, saveNameBox.Text, GameModePreset.list.Find(gm => gm.Name == "Single Player"));
            (Game1.GameSession.gameMode as SinglePlayerMode).GenerateMap(seedBox.Text);

            Game1.LobbyScreen.Select();

            new GUIMessageBox("Welcome to Subsurface!", "Please note that the single player mode is very unfinished at the moment; "+
            "for example, the NPCs don't have an AI yet and there are only a couple of different quests to complete. The multiplayer "+
            "mode should be much more enjoyable to play at the moment, so my recommendation is to try out and get a hang of the game mechanics "+
            "in the single player mode and then move on to multiplayer. Have fun!\n - Regalis, the main dev of Subsurface", 400, 350);

            return true;
        }

        private bool PreviousTab(GUIButton button, object obj)
        {
            selectedTab = (int)Tabs.Main;

            return true;
        }

        private bool LoadGame(GUIButton button, object obj)
        {

            string saveFile = saveList.SelectedData as string;
            if (string.IsNullOrWhiteSpace(saveFile)) return false;

            try
            {
                SaveUtil.LoadGame(saveFile);                
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Loading map ''"+saveFile+"'' failed", e);
                return false;
            }


            Game1.LobbyScreen.Select();

            return true;
        }


        private bool JoinServer(GUIButton button, object obj)
        {
            if (string.IsNullOrEmpty(clientNameBox.Text)) return false;
            if (string.IsNullOrEmpty(ipBox.Text)) return false;

            Game1.NetworkMember = new GameClient(clientNameBox.Text);
            Game1.Client.ConnectToServer(ipBox.Text);

            return true;
            //{
            //    Game1.NetLobbyScreen.Select();
            //    return true;
            //}
            //else
            //{
            //    Game1.NetworkMember = null;
            //    return false;
            //}
        }
        
    }
}
