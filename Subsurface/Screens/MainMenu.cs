using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Subsurface.Networking;
using System.IO;

namespace Subsurface
{
    class MainMenuScreen : Screen
    {
        public enum Tabs { Main = 0, NewGame = 1, LoadGame = 2, JoinServer = 3, HostServer = 4 }

        private GUIFrame[] menuTabs;
        private GUIListBox mapList;

        private GUIListBox saveList;

        private GUITextBox seedBox;

        private GUITextBox nameBox, ipBox;

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

            GUIButton button = new GUIButton(new Rectangle(0, 0, 0, 30), "New Game", Alignment.CenterX, GUI.style, menuTabs[(int)Tabs.Main]);
            button.UserData = (int)Tabs.NewGame;
            button.OnClicked = SelectTab;
            //button.Enabled = false;

            button = new GUIButton(new Rectangle(0, 60, 0, 30), "Load Game", Alignment.CenterX, GUI.style, menuTabs[(int)Tabs.Main]);
            button.UserData = (int)Tabs.LoadGame;
            button.OnClicked = SelectTab;
            //button.Enabled = false;

            button = new GUIButton(new Rectangle(0, 120, 0, 30), "Join Server", Alignment.CenterX, GUI.style, menuTabs[(int)Tabs.Main]);
            button.UserData = (int)Tabs.JoinServer;
            button.OnClicked = SelectTab;

            button = new GUIButton(new Rectangle(0, 180, 0, 30), "Host Server", Alignment.CenterX, GUI.style, menuTabs[(int)Tabs.Main]);
            button.UserData = (int)Tabs.HostServer;
            button.OnClicked = SelectTab;
            //button.Enabled = false;

            button = new GUIButton(new Rectangle(0, 240, 0, 30), "Quit", Alignment.CenterX, GUI.style, menuTabs[(int)Tabs.Main]);
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
                "Map Seed: ", GUI.style, Alignment.Left, Alignment.TopLeft, menuTabs[(int)Tabs.NewGame]);

            seedBox = new GUITextBox(new Rectangle((int)(mapList.Rect.Width + 20), 60, 180, 20),
                Alignment.TopLeft, GUI.style, menuTabs[(int)Tabs.NewGame]);
            seedBox.Text = ToolBox.RandomSeed(8);


            button = new GUIButton(new Rectangle(0, 0, 100, 30), "Start", Alignment.BottomRight, GUI.style,  menuTabs[(int)Tabs.NewGame]);
            button.OnClicked = StartGame;

            //----------------------------------------------------------------------

            menuTabs[(int)Tabs.LoadGame] = new GUIFrame(panelRect, GUI.style);
            //menuTabs[(int)Tabs.LoadGame].Padding = GUI.style.smallPadding;

            new GUITextBlock(new Rectangle(0, 0, 0, 30), "Load Game", GUI.style, Alignment.CenterX, Alignment.CenterX, menuTabs[(int)Tabs.LoadGame]);

            if (!Directory.Exists(SaveUtil.SaveFolder))
            {
                DebugConsole.ThrowError("Save folder ''"+SaveUtil.SaveFolder+" not found! Attempting to create a new folder");
                try
                {
                    Directory.CreateDirectory(SaveUtil.SaveFolder);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Failed to create the folder ''"+SaveUtil.SaveFolder+"''!", e);
                }
            }

            string[] saveFiles = Directory.GetFiles(SaveUtil.SaveFolder, "*.save");

            //new GUITextBlock(new Rectangle(0, 30, 0, 30), "Selected map:", Color.Transparent, Color.Black, Alignment.Left, menuTabs[(int)Tabs.NewGame]);
            saveList = new GUIListBox(new Rectangle(0, 60, 200, 360), Color.White, GUI.style, menuTabs[(int)Tabs.LoadGame]);

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

            button = new GUIButton(new Rectangle(0, 0, 100, 30), "Start",Alignment.Right | Alignment.Bottom, GUI.style, menuTabs[(int)Tabs.LoadGame]);
            button.OnClicked = LoadGame;

            //----------------------------------------------------------------------

            menuTabs[(int)Tabs.JoinServer] = new GUIFrame(panelRect, GUI.style);
            //menuTabs[(int)Tabs.JoinServer].Padding = GUI.style.smallPadding;

            new GUITextBlock(new Rectangle(0, 0, 0, 30), "Join Server", GUI.style, Alignment.CenterX, Alignment.CenterX, menuTabs[(int)Tabs.JoinServer]);

            new GUITextBlock(new Rectangle(0, 30, 0, 30), "Name:", GUI.style, Alignment.CenterX, Alignment.CenterX, menuTabs[(int)Tabs.JoinServer]);
            nameBox = new GUITextBox(new Rectangle(0, 60, 200, 30), Color.White, Color.Black, Alignment.CenterX, Alignment.CenterX, null, menuTabs[(int)Tabs.JoinServer]);

            new GUITextBlock(new Rectangle(0, 100, 0, 30), "Server IP:", GUI.style, Alignment.CenterX, Alignment.CenterX, menuTabs[(int)Tabs.JoinServer]);
            ipBox = new GUITextBox(new Rectangle(0, 130, 200, 30), Color.White, Color.Black, Alignment.CenterX, Alignment.CenterX, null, menuTabs[(int)Tabs.JoinServer]);
            
            GUIButton joinButton = new GUIButton(new Rectangle(0, 0, 200, 30), "Join", Alignment.BottomCenter, GUI.style, menuTabs[(int)Tabs.JoinServer]);
            joinButton.OnClicked = JoinServer;

            //----------------------------------------------------------------------

            menuTabs[(int)Tabs.HostServer] = new GUIFrame(panelRect, GUI.style);
            //menuTabs[(int)Tabs.JoinServer].Padding = GUI.style.smallPadding;

            new GUITextBlock(new Rectangle(0, 0, 0, 30), "Host Server", GUI.style, Alignment.CenterX, Alignment.CenterX, menuTabs[(int)Tabs.HostServer]);

            new GUITextBlock(new Rectangle(0, 30, 0, 30), "Name:", GUI.style, Alignment.CenterX, Alignment.CenterX, menuTabs[(int)Tabs.HostServer]);
            serverNameBox = new GUITextBox(new Rectangle(0, 60, 200, 30), Color.White, Color.Black, Alignment.CenterX, Alignment.CenterX, null, menuTabs[(int)Tabs.HostServer]);

            new GUITextBlock(new Rectangle(0, 100, 0, 30), "Server port:", GUI.style, Alignment.CenterX, Alignment.CenterX, menuTabs[(int)Tabs.HostServer]);
            portBox = new GUITextBox(new Rectangle(0, 130, 200, 30), Color.White, Color.Black, Alignment.CenterX, Alignment.CenterX, null, menuTabs[(int)Tabs.HostServer]);
            portBox.Text = NetworkMember.DefaultPort.ToString();
            portBox.ToolTip = "Server port";

            GUIButton hostButton = new GUIButton(new Rectangle(0, 0, 200, 30), "Start", Alignment.BottomCenter, GUI.style, menuTabs[(int)Tabs.HostServer]);
            hostButton.OnClicked = HostServerClicked;

            //----------------------------------------------------------------------
            for (int i = 1; i < 5; i++ )
            {
                button = new GUIButton(new Rectangle(-20, -20, 100, 30), "Back", Alignment.TopLeft, GUI.style, menuTabs[i]);
                button.OnClicked = PreviousTab;
            }

            this.game = game;

        }

        public bool SelectTab(GUIButton button, object obj)
        {
            selectedTab = (int)obj;

            this.Select();
            return true;
        }

        private bool HostServerClicked(GUIButton button, object obj)
        {
            string name = serverNameBox.Text;
            if (string.IsNullOrEmpty(name)) name = "Server";

            int port;
            if (!int.TryParse(portBox.Text, out port))
            {
                DebugConsole.ThrowError("ERROR: "+portBox.Text+" is not a valid port. Using the default port "+NetworkMember.DefaultPort);
                port = NetworkMember.DefaultPort;
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

            spriteBatch.End();
        }

        private bool StartGame(GUIButton button, object obj)
        {
            Submarine selectedMap = mapList.SelectedData as Submarine;
            if (selectedMap == null) return false;

            Game1.GameSession = new GameSession(selectedMap, GameModePreset.list.Find(gm => gm.Name == "Single Player"));
            (Game1.GameSession.gameMode as SinglePlayerMode).GenerateMap(seedBox.Text);

            Game1.LobbyScreen.Select();

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
            if (string.IsNullOrEmpty(nameBox.Text)) return false;
            if (string.IsNullOrEmpty(ipBox.Text)) return false;

            Game1.NetworkMember = new GameClient(nameBox.Text);
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
