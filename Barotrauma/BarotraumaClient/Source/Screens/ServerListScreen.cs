using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class ServerListScreen : Screen
    {
        struct ServerInfo
        {
            public string IP;
            public string Port;
            public string ServerName;
            public bool GameStarted;
            public int PlayerCount;
            public int MaxPlayers;
            public bool HasPassword;           
        }

        //how often the client is allowed to refresh servers
        private TimeSpan AllowedRefreshInterval = new TimeSpan(0, 0, 3);

        private GUIFrame menu;

        private GUIListBox serverList;

        private GUIButton joinButton;

        private GUITextBox clientNameBox, ipBox;

        private bool masterServerResponded;
        private IRestResponse masterServerResponse;

        private int[] columnX;

        //filters
        private GUITextBox searchBox;
        private GUITickBox filterPassword;
        private GUITickBox filterFull;
        private GUITickBox filterEmpty;

        //a timer for 
        private DateTime refreshDisableTimer;
        private bool waitingForRefresh;

        public ServerListScreen()
        {
            int width = Math.Min(GameMain.GraphicsWidth - 160, 1000);
            int height = Math.Min(GameMain.GraphicsHeight - 160, 700);

            Rectangle panelRect = new Rectangle(0, 0, width, height);

            menu = new GUIFrame(panelRect, null, Alignment.Center, "");
            menu.Padding = new Vector4(40.0f, 40.0f, 40.0f, 20.0f);

            new GUITextBlock(new Rectangle(0, -25, 0, 30), TextManager.Get("JoinServer"), "", Alignment.CenterX, Alignment.CenterX, menu, false, GUI.LargeFont);

            new GUITextBlock(new Rectangle(0, 30, 0, 30), TextManager.Get("YourName"), "", menu);
            clientNameBox = new GUITextBox(new Rectangle(0, 60, 200, 30), "", menu);
            clientNameBox.Text = GameMain.Config.DefaultPlayerName;

            new GUITextBlock(new Rectangle(0, 100, 0, 30), TextManager.Get("ServerIP"), "", menu);
            ipBox = new GUITextBox(new Rectangle(0, 130, 200, 30), "", menu)
            {
                //max IPv6 address length + port
                MaxTextLength = 45 + 6
            };

            int middleX = (int)(width * 0.35f);

            serverList = new GUIListBox(new Rectangle(middleX, 60, 0, height - 160), "", menu);
            serverList.OnSelected = SelectServer;

            float[] columnRelativeX = new float[] { 0.15f, 0.5f, 0.15f, 0.2f };
            columnX = new int[columnRelativeX.Length];
            for (int n = 0; n < columnX.Length; n++)
            {
                columnX[n] = (int)(columnRelativeX[n] * serverList.Rect.Width);
                if (n > 0) columnX[n] += columnX[n - 1];
            }

            ScalableFont font = GUI.SmallFont; // serverList.Rect.Width < 400 ? GUI.SmallFont : GUI.Font;

            new GUITextBlock(new Rectangle(middleX, 30, 0, 30), TextManager.Get("Password"), "", menu).Font = font;

            new GUITextBlock(new Rectangle(middleX + columnX[0], 30, 0, 30), TextManager.Get("ServerListName"), "", menu).Font = font;
            new GUITextBlock(new Rectangle(middleX + columnX[1], 30, 0, 30), TextManager.Get("ServerListPlayers"), "", menu).Font = font;
            new GUITextBlock(new Rectangle(middleX + columnX[2], 30, 0, 30), TextManager.Get("ServerListRoundStarted"), "", menu).Font = font;

            joinButton = new GUIButton(new Rectangle(-170, 0, 150, 30), TextManager.Get("ServerListRefresh"), Alignment.BottomRight, "", menu);
            joinButton.OnClicked = RefreshServers;

            joinButton = new GUIButton(new Rectangle(0,0,150,30), TextManager.Get("ServerListJoin"), Alignment.BottomRight, "", menu);
            joinButton.OnClicked = JoinServer;

            //--------------------------------------------------------

            int y = 180;

            new GUITextBlock(new Rectangle(0, y, 200, 30), TextManager.Get("FilterServers"), "", menu);
            searchBox = new GUITextBox(new Rectangle(0, y + 30, 200, 30), "", menu);
            searchBox.OnTextChanged += (txtBox, txt) => { FilterServers(); return true; };
            filterPassword = new GUITickBox(new Rectangle(0, y + 60, 30, 30), TextManager.Get("FilterPassword"), Alignment.TopLeft, menu);
            filterPassword.OnSelected += (tickBox) => { FilterServers(); return true; };
            filterFull = new GUITickBox(new Rectangle(0, y + 90, 30, 30), TextManager.Get("FilterFullServers"), Alignment.TopLeft, menu);
            filterFull.OnSelected += (tickBox) => { FilterServers(); return true; };
            filterEmpty = new GUITickBox(new Rectangle(0, y + 120, 30, 30), TextManager.Get("FilterEmptyServers"), Alignment.TopLeft, menu);
            filterEmpty.OnSelected += (tickBox) => { FilterServers(); return true; };

            //--------------------------------------------------------

            GUIButton button = new GUIButton(new Rectangle(-20, -20, 100, 30), TextManager.Get("Back"), Alignment.TopLeft, "", menu);
            button.OnClicked = GameMain.MainMenuScreen.SelectTab;
            button.SelectedColor = button.Color;

            refreshDisableTimer = DateTime.Now;
        }

        public override void Select()
        {
            base.Select();
            RefreshServers(null, null);
        }

        private void FilterServers()
        {
            serverList.RemoveChild(serverList.FindChild("noresults"));
            
            foreach (GUIComponent child in serverList.children)
            {
                if (!(child.UserData is ServerInfo)) continue;
                ServerInfo serverInfo = (ServerInfo)child.UserData;

                child.Visible =
                    serverInfo.ServerName.ToLowerInvariant().Contains(searchBox.Text.ToLowerInvariant()) &&
                    (!filterPassword.Selected || !serverInfo.HasPassword) &&
                    (!filterFull.Selected || serverInfo.PlayerCount < serverInfo.MaxPlayers) &&
                    (!filterEmpty.Selected || serverInfo.PlayerCount > 0);
            }

            if (serverList.children.All(c => !c.Visible))
            {
                new GUITextBlock(new Rectangle(0, 0, 0, 20), TextManager.Get("NoMatchingServers"), "", serverList).UserData = "noresults";
            }
        }

        private bool SelectServer(GUIComponent component, object obj)
        {
            if (obj == null || waitingForRefresh) return false;

            ServerInfo serverInfo;
            try
            {
                serverInfo = (ServerInfo)obj;
            }
            catch (InvalidCastException)
            {
                return false;
            }

            ipBox.Text = serverInfo.IP + ":" + serverInfo.Port;

            return true;
        }

        private bool RefreshServers(GUIButton button, object obj)
        {
            if (waitingForRefresh) return false;
            serverList.ClearChildren();

            new GUITextBlock(new Rectangle(0, 0, 0, 20), TextManager.Get("RefreshingServerList"), "", serverList);
            
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
            
            CoroutineManager.StartCoroutine(SendMasterServerRequest());

            waitingForRefresh = false;

            refreshDisableTimer = DateTime.Now + AllowedRefreshInterval;

            yield return CoroutineStatus.Success;
        }

        private void UpdateServerList(string masterServerData)
        {
            serverList.ClearChildren();

            if (string.IsNullOrWhiteSpace(masterServerData))
            {
                new GUITextBlock(new Rectangle(0, 0, 0, 20), TextManager.Get("NoServers"), "", serverList);
                return;
            }

            if (masterServerData.Substring(0, 5).ToLowerInvariant() == "error")
            {
                DebugConsole.ThrowError("Error while connecting to master server (" + masterServerData + ")!");

                return;
            }

            string[] lines = masterServerData.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                string[] arguments = lines[i].Split('|');
                if (arguments.Length < 3) continue;

                string ip = arguments[0];
                string port = arguments[1];
                string serverName = arguments[2];
                bool gameStarted = arguments.Length > 3 && arguments[3] == "1";
                string currPlayersStr = (arguments.Length > 4) ? arguments[4] : "";
                string maxPlayersStr = (arguments.Length > 5) ? arguments[5] : "";                
                bool hasPassWord = arguments.Length > 6 && arguments[6] == "1";

                int playerCount = 0, maxPlayers = 1;
                int.TryParse(currPlayersStr, out playerCount);
                int.TryParse(maxPlayersStr, out maxPlayers);

                var serverInfo = new ServerInfo()
                {
                    IP = ip,
                    Port = port,
                    ServerName = serverName,
                    GameStarted = gameStarted,
                    PlayerCount = playerCount,
                    MaxPlayers = maxPlayers,
                    HasPassword = hasPassWord
                };

                var serverFrame = new GUIFrame(new Rectangle(0, 0, 0, 30), (i % 2 == 0) ? Color.Transparent : Color.White * 0.2f, "ListBoxElement", serverList);
                serverFrame.UserData = serverInfo;

                var passwordBox = new GUITickBox(new Rectangle(columnX[0] / 2, 0, 20, 20), "", Alignment.CenterLeft, serverFrame);
                passwordBox.Selected = hasPassWord;
                passwordBox.Enabled = false;
                passwordBox.UserData = "password";

                new GUITextBlock(new Rectangle(columnX[0], 0, 0, 0), serverName, "", Alignment.TopLeft, Alignment.CenterLeft, serverFrame);
                new GUITextBlock(new Rectangle(columnX[1], 0, 0, 0), playerCount + "/" + maxPlayers, "", Alignment.TopLeft, Alignment.CenterLeft, serverFrame);

                var gameStartedBox = new GUITickBox(new Rectangle(columnX[2] + (columnX[3] - columnX[2]) / 2, 0, 20, 20), "", Alignment.CenterRight, serverFrame);
                gameStartedBox.Selected = gameStarted;
                gameStartedBox.Enabled = false;
            }

            FilterServers();
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
            request.AddParameter("gamename", "barotrauma");
            request.AddParameter("action", "listservers");
            
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
                    if (string.IsNullOrEmpty(GameMain.SteamVersionUrl))
                    {
                        //Steam version is out and could not reach the master server
                        // -> assume legacy master server has been deprecated
                        new GUIMessageBox(TextManager.Get("MasterServerErrorLabel"), TextManager.Get("MasterServerTimeOutError"));
                    }
                    else
                    {
                        ShowMasterServerDeprecatedMessage();
                    }
                    yield return CoroutineStatus.Success;
                }
                yield return CoroutineStatus.Running;
            }

            if (masterServerResponse.ErrorException != null)
            {
                serverList.ClearChildren();
                new GUIMessageBox(TextManager.Get("MasterServerErrorLabel"), TextManager.Get("MasterServerErrorException").Replace("[error]", masterServerResponse.ErrorException.ToString()));
            }

            else if (masterServerResponse.StatusCode != System.Net.HttpStatusCode.OK)
            {
                serverList.ClearChildren();
                
                switch (masterServerResponse.StatusCode)
                {
                    case System.Net.HttpStatusCode.NotFound:
                        //Steam version is out and server file wasn't found on the legacy master server 
                        // -> assume legacy master server has been deprecated
                        if (string.IsNullOrEmpty(GameMain.SteamVersionUrl))
                        {
                            new GUIMessageBox(TextManager.Get("MasterServerErrorLabel"),
                               TextManager.Get("MasterServerError404")
                                    .Replace("[masterserverurl]", NetConfig.MasterServerUrl)
                                    .Replace("[statuscode]", masterServerResponse.StatusCode.ToString())
                                    .Replace("[statusdescription]", masterServerResponse.StatusDescription));
                        }
                        else
                        {
                            ShowMasterServerDeprecatedMessage();
                        }
                        break;
                    case System.Net.HttpStatusCode.ServiceUnavailable:
                        new GUIMessageBox(TextManager.Get("MasterServerErrorLabel"), 
                            TextManager.Get("MasterServerErrorUnavailable")
                                .Replace("[masterserverurl]", NetConfig.MasterServerUrl)
                                .Replace("[statuscode]", masterServerResponse.StatusCode.ToString())
                                .Replace("[statusdescription]", masterServerResponse.StatusDescription));
                        break;
                    default:
                        new GUIMessageBox(TextManager.Get("MasterServerErrorLabel"),
                            TextManager.Get("MasterServerError404")
                                .Replace("[masterserverurl]", NetConfig.MasterServerUrl)
                                .Replace("[statuscode]", masterServerResponse.StatusCode.ToString())
                                .Replace("[statusdescription]", masterServerResponse.StatusDescription));
                        break;
                }
                
            }
            else
            {
                UpdateServerList(masterServerResponse.Content);
            }

            yield return CoroutineStatus.Success;
        }

        private void ShowMasterServerDeprecatedMessage()
        {
            serverList.ClearChildren();
            new GUITextBlock(new Rectangle(0, 0, (int)(serverList.Rect.Width * 0.8f), (int)(serverList.Rect.Height * 0.8f)),
                "This version of Barotrauma is no longer supported and the legacy server list is no longer available.",
                alignment: Alignment.Center, textAlignment: Alignment.Center,
                style: "", parent: serverList, wrap: true)
            {
                CanBeFocused = false
            };
        }

        private void MasterServerCallBack(IRestResponse response)
        {
            masterServerResponse = response;
            masterServerResponded = true;
        }

        private bool JoinServer(GUIButton button, object obj)
        {
            if (string.IsNullOrWhiteSpace(clientNameBox.Text))
            {
                clientNameBox.Flash();
                return false;
            }

            GameMain.Config.DefaultPlayerName = clientNameBox.Text;

            string ip = ipBox.Text;

            if (string.IsNullOrWhiteSpace(ip))
            {
                ipBox.Flash();
                return false;
            }

            CoroutineManager.StartCoroutine(ConnectToServer(ip));

            return true;
        }

        /*public void JoinServer(string ip, bool hasPassword, string msg = "Password required")
        {
            CoroutineManager.StartCoroutine(ConnectToServer(ip));
        }*/

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

            spriteBatch.Begin(SpriteSortMode.Immediate, null, null, null, GameMain.ScissorTestEnable);

            menu.Draw(spriteBatch);
            
            GUI.Draw((float)deltaTime, spriteBatch, null);

            spriteBatch.End();
        }

        public override void AddToGUIUpdateList()
        {
            menu.AddToGUIUpdateList();
        }

        public override void Update(double deltaTime)
        {
            menu.Update((float)deltaTime);
        }
    }
}
