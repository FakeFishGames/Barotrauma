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

        private float[] columnRelativeWidth;

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

            menu = new GUIFrame(new RectTransform(new Point(width, height), GUI.Canvas, Anchor.Center));

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), menu.RectTransform, Anchor.TopCenter),
                TextManager.Get("JoinServer"), textAlignment: Alignment.Center, font: GUI.LargeFont);

            var paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.87f), menu.RectTransform, Anchor.Center) { RelativeOffset = new Vector2(0.0f, 0.03f) }, style: null);

            //-------------------------------------------------------------------------------------
            //left column
            //-------------------------------------------------------------------------------------

            var leftColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.35f, 1.0f), paddedFrame.RectTransform, Anchor.TopLeft));

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), TextManager.Get("YourName"));
            clientNameBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), "")
            {
                Text = GameMain.Config.DefaultPlayerName
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), TextManager.Get("ServerIP"));
            ipBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), "");

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), style: null);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), TextManager.Get("FilterServers"));
            searchBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), "");
            searchBox.OnTextChanged += (txtBox, txt) => { FilterServers(); return true; };
            filterPassword = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), TextManager.Get("FilterPassword"));
            filterPassword.OnSelected += (tickBox) => { FilterServers(); return true; };
            filterFull = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), TextManager.Get("FilterFullServers"));
            filterFull.OnSelected += (tickBox) => { FilterServers(); return true; };
            filterEmpty = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform), TextManager.Get("FilterEmptyServers"));
            filterEmpty.OnSelected += (tickBox) => { FilterServers(); return true; };

            //-------------------------------------------------------------------------------------
            //right column
            //-------------------------------------------------------------------------------------

            var rightColumn = new GUILayoutGroup(new RectTransform(new Vector2(1.0f - leftColumn.RectTransform.RelativeSize.X - 0.05f, 1.0f),
                paddedFrame.RectTransform, Anchor.TopRight))
            {
                RelativeSpacing = 0.02f,
                Stretch = true
            };

            var columnHeaderContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.03f), rightColumn.RectTransform), isHorizontal: true);
            serverList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.85f), rightColumn.RectTransform, Anchor.Center))
            {
                OnSelected = SelectServer
            };

            columnRelativeWidth = new float[] { 0.15f, 0.5f, 0.15f, 0.2f };
            string[] columnHeaders = new string[]
            {
                TextManager.Get("Password") ,
                TextManager.Get("ServerListName"),
                TextManager.Get("ServerListPlayers"),
                TextManager.Get("ServerListRoundStarted")
            };
            System.Diagnostics.Debug.Assert(columnRelativeWidth.Length == columnHeaders.Length);
            
            for (int i = 0; i < columnHeaders.Length; i++)
            {
                new GUITextBlock(new RectTransform(new Vector2(columnRelativeWidth[i], 1.0f), columnHeaderContainer.RectTransform),
                    columnHeaders[i], font: GUI.SmallFont);
            }
            var buttonContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.06f), rightColumn.RectTransform), style: null);
            
            new GUIButton(new RectTransform(new Vector2(0.25f, 1.0f), buttonContainer.RectTransform, Anchor.BottomLeft),
                TextManager.Get("ServerListRefresh"))
            {
                OnClicked = RefreshServers
            };

            joinButton = new GUIButton(new RectTransform(new Vector2(0.25f, 1.0f), buttonContainer.RectTransform, Anchor.BottomRight),
                TextManager.Get("ServerListJoin"))
            {
                OnClicked = JoinServer
            };

            //--------------------------------------------------------

            GUIButton button = new GUIButton(new RectTransform(new Vector2(0.13f, 0.06f), paddedFrame.RectTransform, Anchor.BottomLeft),
                TextManager.Get("Back"))
            {
                OnClicked = GameMain.MainMenuScreen.SelectTab
            };
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
            serverList.Content.RemoveChild(serverList.Content.FindChild("noresults"));
            
            foreach (GUIComponent child in serverList.Content.Children)
            {
                if (!(child.UserData is ServerInfo)) continue;
                ServerInfo serverInfo = (ServerInfo)child.UserData;

                child.Visible =
                    serverInfo.ServerName.ToLowerInvariant().Contains(searchBox.Text.ToLowerInvariant()) &&
                    (!filterPassword.Selected || !serverInfo.HasPassword) &&
                    (!filterFull.Selected || serverInfo.PlayerCount < serverInfo.MaxPlayers) &&
                    (!filterEmpty.Selected || serverInfo.PlayerCount > 0);
            }

            if (serverList.Content.Children.All(c => !c.Visible))
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), serverList.Content.RectTransform),
                    TextManager.Get("NoMatchingServers"))
                {
                    UserData = "noresults"
                };
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

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), serverList.Content.RectTransform),
                TextManager.Get("RefreshingServerList"));
            
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
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), serverList.Content.RectTransform),
                    TextManager.Get("NoServers"));
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

                string ip               = arguments[0];
                string port             = arguments[1];
                string serverName       = arguments[2];
                bool gameStarted        = arguments.Length > 3 && arguments[3] == "1";
                string currPlayersStr   = (arguments.Length > 4) ? arguments[4] : "";
                string maxPlayersStr    = (arguments.Length > 5) ? arguments[5] : "";                
                bool hasPassWord        = arguments.Length > 6 && arguments[6] == "1";

                int.TryParse(currPlayersStr, out int playerCount);
                int.TryParse(maxPlayersStr, out int maxPlayers);

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

                var serverFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.06f), serverList.Content.RectTransform) { MinSize = new Point(0, 20) },
                    style: "InnerFrame", color: Color.White * 0.5f)
                {
                    UserData = serverInfo
                };
                var serverContent = new GUILayoutGroup(new RectTransform(Vector2.One, serverFrame.RectTransform), isHorizontal: true);

                var passwordBox = new GUITickBox(new RectTransform(new Vector2(columnRelativeWidth[0], 1.0f), serverContent.RectTransform), label: "")
                {
                    Selected = hasPassWord,
                    Enabled = false,
                    UserData = "password"
                };

                new GUITextBlock(new RectTransform(new Vector2(columnRelativeWidth[1], 1.0f), serverContent.RectTransform), serverName);
                new GUITextBlock(new RectTransform(new Vector2(columnRelativeWidth[2], 1.0f), serverContent.RectTransform), 
                    playerCount + "/" + maxPlayers);

                var gameStartedBox = new GUITickBox(new RectTransform(new Vector2(columnRelativeWidth[3], 1.0f), serverContent.RectTransform, Anchor.TopRight),
                    label: "")
                {
                    IgnoreLayoutGroups = true,
                    Selected = gameStarted,
                    Enabled = false
                };
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
                    new GUIMessageBox(TextManager.Get("MasterServerErrorLabel"), TextManager.Get("MasterServerTimeOutError"));
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
                        new GUIMessageBox(TextManager.Get("MasterServerErrorLabel"),
                           TextManager.Get("MasterServerError404")
                                .Replace("[masterserverurl]", NetConfig.MasterServerUrl)
                                .Replace("[statuscode]", masterServerResponse.StatusCode.ToString())
                                .Replace("[statusdescription]", masterServerResponse.StatusDescription));
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
            
            GUI.Draw((float)deltaTime, spriteBatch);

            spriteBatch.End();
        }

        public override void AddToGUIUpdateList()
        {
            menu.AddToGUIUpdateList();
        }
        
    }
}
