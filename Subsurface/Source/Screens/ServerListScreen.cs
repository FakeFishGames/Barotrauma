using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Networking;
using System.Collections.Generic;
using RestSharp;

namespace Barotrauma
{
    class ServerListScreen : Screen
    {
        //how often the client is allowed to refresh servers
        private TimeSpan AllowedRefreshInterval = new TimeSpan(0,0,3);

        private GUIFrame menu;

        private GUIListBox serverList;

        private GUIButton joinButton;

        private GUITextBox clientNameBox, ipBox;

        //private RestRequestAsyncHandle restRequestHandle;
        private bool masterServerResponded;

        private int[] columnX;

        //a timer for 
        private DateTime refreshDisableTimer;
        private bool waitingForRefresh;

        public ServerListScreen()
        {
            int width = Math.Min(GameMain.GraphicsWidth - 160, 1000);
            int height = Math.Min(GameMain.GraphicsHeight - 160, 700);

            Rectangle panelRect = new Rectangle(0, 0, width, height);

            menu = new GUIFrame(panelRect, null, Alignment.Center, GUI.Style);
            menu.Padding = new Vector4(40.0f, 40.0f, 40.0f, 20.0f);

            new GUITextBlock(new Rectangle(0, -25, 0, 30), "Join Server", GUI.Style, Alignment.CenterX, Alignment.CenterX, menu, false, GUI.LargeFont);

            new GUITextBlock(new Rectangle(0, 30, 0, 30), "Your Name:", GUI.Style, menu);
            clientNameBox = new GUITextBox(new Rectangle(0, 60, 200, 30), GUI.Style, menu);

            new GUITextBlock(new Rectangle(0, 100, 0, 30), "Server IP:", GUI.Style, menu);
            ipBox = new GUITextBox(new Rectangle(0, 130, 200, 30), GUI.Style, menu);

            int middleX = (int)(width * 0.4f);

            serverList = new GUIListBox(new Rectangle(middleX,60,0,height-160), GUI.Style, menu);
            serverList.OnSelected = SelectServer;

            float[] columnRelativeX = new float[] { 0.15f, 0.55f, 0.15f, 0.15f };
            columnX = new int[columnRelativeX.Length];
            for (int n = 0; n < columnX.Length; n++)
            {
                columnX[n] = (int)(columnRelativeX[n] * serverList.Rect.Width);
                if (n > 0) columnX[n] += columnX[n - 1];
            }

            SpriteFont font = GUI.SmallFont; // serverList.Rect.Width < 400 ? GUI.SmallFont : GUI.Font;

            new GUITextBlock(new Rectangle(middleX, 30, 0, 30), "Password", GUI.Style, menu).Font = font;

            new GUITextBlock(new Rectangle(middleX + columnX[0], 30, 0, 30), "Name", GUI.Style, menu).Font = font;
            new GUITextBlock(new Rectangle(middleX + columnX[1], 30, 0, 30), "Players", GUI.Style, menu).Font = font;
            new GUITextBlock(new Rectangle(middleX + columnX[2], 30, 0, 30), "Round started", GUI.Style, menu).Font = font;

            joinButton = new GUIButton(new Rectangle(-170, 0, 150, 30), "Refresh", Alignment.BottomRight, GUI.Style, menu);
            joinButton.OnClicked = RefreshServers;

            joinButton = new GUIButton(new Rectangle(0,0,150,30), "Join", Alignment.BottomRight, GUI.Style, menu);
            joinButton.OnClicked = JoinServer;

            GUIButton button = new GUIButton(new Rectangle(-20, -20, 100, 30), "Back", Alignment.TopLeft, GUI.Style, menu);
            button.OnClicked = GameMain.MainMenuScreen.SelectTab;
            button.SelectedColor = button.Color;

            refreshDisableTimer = DateTime.Now;
        }

        public override void Select()
        {
            base.Select();


            RefreshServers(null, null);
        }

        private bool SelectServer(GUIComponent component, object obj)
        {
            string ip = obj as string;
            if (string.IsNullOrWhiteSpace(ip)) return false;

            ipBox.Text = ip;

            return true;
        }

        private bool RefreshServers(GUIButton button, object obj)
        {
            if (waitingForRefresh) return false;
            serverList.ClearChildren();

            new GUITextBlock(new Rectangle(0, 0, 0, 20), "Refreshing server list...", GUI.Style, serverList);
            
            CoroutineManager.StartCoroutine(WaitForRefresh());

            return true;
        }

        private IEnumerable<object> WaitForRefresh()
        {
            waitingForRefresh = true;
            if (refreshDisableTimer > DateTime.Now)
            {
                yield return new WaitForSeconds((float)(refreshDisableTimer - DateTime.Now).TotalSeconds);
            }

            //CoroutineManager.StartCoroutine(UpdateServerList());
            CoroutineManager.StartCoroutine(SendMasterServerRequest());

            waitingForRefresh = false;

            refreshDisableTimer = DateTime.Now + AllowedRefreshInterval;

            yield return CoroutineStatus.Success;
        }

        private void UpdateServerList(string masterServerData)
        {
            serverList.ClearChildren();
            
            //string masterServerData = GetMasterServerData();

            if (string.IsNullOrWhiteSpace(masterServerData))
            {
                new GUITextBlock(new Rectangle(0, 0, 0, 20), "Couldn't find any servers", GUI.Style, serverList);

                return;
            }

            if (masterServerData.Substring(0, 5).ToLowerInvariant() == "error")
            {
                DebugConsole.ThrowError("Error while connecting to master server ("+masterServerData+")!");

                return;
            }

            string[] lines = masterServerData.Split('\n');

            for (int i = 0; i<lines.Length; i++)
            {
                string[] arguments = lines[i].Split('|');
                if (arguments.Length < 3) continue;

                string IP = arguments[0];
                string port = arguments[1];
                string serverName = arguments[2];
                string gameStarted = (arguments.Length > 3) ? arguments[3] : "";
                string currPlayersStr = (arguments.Length > 4) ? arguments[4] : "";
                string maxPlayersStr = (arguments.Length > 5) ? arguments[5] : "";


                string hasPassWordStr = (arguments.Length > 6) ? arguments[6] : "";

                var serverFrame = new GUIFrame(new Rectangle(0, 0, 0, 20), (i % 2 == 0) ? Color.Transparent : Color.White * 0.2f, null, serverList);
                serverFrame.UserData = IP + ":" + port;
                serverFrame.HoverColor = Color.Gold * 0.2f;
                serverFrame.SelectedColor = Color.Gold * 0.5f;

                var passwordBox = new GUITickBox(new Rectangle(columnX[0] / 2, 0, 20, 20), "", Alignment.TopLeft, serverFrame);
                passwordBox.Selected = hasPassWordStr == "1";
                passwordBox.Enabled = false;
                passwordBox.UserData = "password";

                new GUITextBlock(new Rectangle(columnX[0], 0, 0, 0), serverName, GUI.Style, serverFrame);

                int playerCount = 0, maxPlayers = 1;
                int.TryParse(currPlayersStr, out playerCount);
                int.TryParse(maxPlayersStr, out maxPlayers);

                new GUITextBlock(new Rectangle(columnX[1], 0, 0, 0), playerCount + "/" + maxPlayers, GUI.Style, serverFrame);

                var gameStartedBox = new GUITickBox(new Rectangle(columnX[2] + (columnX[3] - columnX[2])/ 2, 0, 20, 20), "", Alignment.TopLeft, serverFrame);
                gameStartedBox.Selected = gameStarted == "1";
                gameStartedBox.Enabled = false;
            
            }
        }

        private IEnumerable<object> SendMasterServerRequest()
        {
            RestClient client = null;
            try
            {
                client = new RestClient(NetConfig.MasterServerUrl);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error while connecting to master server", e);                
            }

            if (client == null) yield return CoroutineStatus.Success;


            var request = new RestRequest("masterserver2.php", Method.GET);
            request.AddParameter("gamename", "barotrauma"); // adds to POST or URL querystring based on Method
            request.AddParameter("action", "listservers"); // adds to POST or URL querystring based on Method


            // easily add HTTP Headers
            //request.AddHeader("header", "value");

            //// add files to upload (works with compatible verbs)
            //request.AddFile(path);

            // execute the request
            masterServerResponded = false;
            var restRequestHandle = client.ExecuteAsync(request, response => MasterServerCallBack(response));

            DateTime timeOut = DateTime.Now + new TimeSpan(0, 0, 8);
            while (!masterServerResponded)
            {
                if (DateTime.Now > timeOut)
                { 
                    serverList.ClearChildren();
                    restRequestHandle.Abort();
                    DebugConsole.ThrowError("Couldn't connect to master server (request timed out)");
                }
                yield return CoroutineStatus.Running;
            }

            yield return CoroutineStatus.Success;

        }

        private void MasterServerCallBack(IRestResponse response)
        {
            masterServerResponded = true;

            if (response.ErrorException!=null)
            {
                serverList.ClearChildren();  
                DebugConsole.ThrowError("Error while connecting to master server", response.ErrorException);
                return;
            }

            if (response.StatusCode!= System.Net.HttpStatusCode.OK)
            {
                serverList.ClearChildren();  

                switch (response.StatusCode)
                {
                    case System.Net.HttpStatusCode.NotFound:
                        DebugConsole.ThrowError("Error while connecting to master server (404 - \"" + NetConfig.MasterServerUrl + "\" not found)");
                        break;
                    case System.Net.HttpStatusCode.ServiceUnavailable:
                        DebugConsole.ThrowError("Error while connecting to master server (505 - Service Unavailable)");
                        DebugConsole.ThrowError("The master server may be down for maintenance or temporarily overloaded. Please try again after in a few moments.");
                        break;
                    default:
                        DebugConsole.ThrowError("Error while connecting to master server (" +response.StatusCode+": "+response.StatusDescription+")");
                        break;
                }

                return;
            }

            UpdateServerList(response.Content);
        }

        private bool JoinServer(GUIButton button, object obj)
        {
            if (string.IsNullOrWhiteSpace(clientNameBox.Text))
            {
                clientNameBox.Flash();
                return false;
            }

            string ip = ipBox.Text;

            if (string.IsNullOrWhiteSpace(ip))
            {
                ipBox.Flash();
                return false;
            }

            CoroutineManager.StartCoroutine(ConnectToServer(ip));


            return true;
        }

        public void JoinServer(string ip, bool hasPassword, string msg = "Password required")
        {
            CoroutineManager.StartCoroutine(ConnectToServer(ip));
        }

        private IEnumerable<object> ConnectToServer(string ip)
        {
            try
            {
                GameMain.NetworkMember = new GameClient(clientNameBox.Text);
                GameMain.Client.ConnectToServer(ip);             
            }

            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to start the client", e);
            }


            yield return CoroutineStatus.Success;
        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            graphics.Clear(Color.CornflowerBlue);

            GameMain.TitleScreen.DrawLoadingText = false;
            GameMain.TitleScreen.Draw(spriteBatch, graphics, (float)deltaTime);

            spriteBatch.Begin();

            menu.Draw(spriteBatch);

            //if (previewPlayer!=null) previewPlayer.Draw(spriteBatch);

            GUI.Draw((float)deltaTime, spriteBatch, null);

            spriteBatch.End();
        }

        public override void AddToGUIUpdateList()
        {
            menu.AddToGUIUpdateList();
        }

        public override void Update(double deltaTime)
        {
            //GameMain.TitleScreen.Update();

            menu.Update((float)deltaTime);

            //GUI.Update((float)deltaTime);
        }
    }
}
