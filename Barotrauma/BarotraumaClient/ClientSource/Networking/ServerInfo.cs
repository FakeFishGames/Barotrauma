using Barotrauma.Steam;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Barotrauma.Networking
{
    class ServerInfo
    {
        public string IP;
        public string Port;
        public string QueryPort;

        public Steamworks.Data.NetPingLocation? PingLocation;
        public UInt64 LobbyID;
        public UInt64 OwnerID;
        public bool OwnerVerified;

        private string serverName;
        public string ServerName
        {
            get { return serverName; }
            set
            {
                serverName = value;
                if (serverName.Length > NetConfig.ServerNameMaxLength) { ServerName = ServerName.Substring(0, NetConfig.ServerNameMaxLength); }
            }
        }

        public string ServerMessage;
        public bool GameStarted;
        public int PlayerCount;
        public int MaxPlayers;
        public bool HasPassword;

        public bool PingChecked;
        public int Ping = -1;

        //null value means that the value isn't known (the server may be using 
        //an old version of the game that didn't report these values or the FetchRules query to Steam may not have finished yet)
        public bool? UsingWhiteList;
        public SelectionMode? ModeSelectionMode;
        public SelectionMode? SubSelectionMode;
        public bool? AllowSpectating;
        public bool? VoipEnabled;
        public bool? KarmaEnabled;
        public bool? FriendlyFireEnabled;
        public bool? AllowRespawn;
        public YesNoMaybe? TraitorsEnabled;
        public Identifier GameMode;
        public PlayStyle? PlayStyle;

        public bool Recent;
        public bool Favorite;

        public bool? RespondedToSteamQuery = null;

        public Steamworks.Friend? SteamFriend;
        public Steamworks.SteamMatchmakingPingResponse MatchmakingPingResponse;

        public string GameVersion;
        public List<string> ContentPackageNames
        {
            get;
            private set;
        } = new List<string>();
        public List<string> ContentPackageHashes
        {
            get;
            private set;
        } = new List<string>();
        public List<ulong> ContentPackageWorkshopIds
        {
            get;
            private set;
        } = new List<ulong>();

        public void CreatePreviewWindow(GUIFrame frame)
        {
            if (frame == null) { return; }

            frame.ClearChildren();

            var title = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), frame.RectTransform), ServerName, font: GUIStyle.LargeFont)
            {
                ToolTip = ServerName,
                CanBeFocused = false
            };
            title.Text = ToolBox.LimitString(title.Text, title.Font, (int)(title.Rect.Width * 0.85f));

            GUITickBox favoriteTickBox = new GUITickBox(new RectTransform(new Vector2(0.15f, 0.8f), title.RectTransform, Anchor.CenterRight), 
                "", null, "GUIServerListFavoriteTickBox")
            {
                Selected = Favorite,
                ToolTip = TextManager.Get(Favorite ? "removefromfavorites" : "addtofavorites"),
                OnSelected = (tickbox) =>
                {
                    if (tickbox.Selected)
                    {
                        GameMain.ServerListScreen.AddToFavoriteServers(this);
                    }
                    else
                    {
                        GameMain.ServerListScreen.RemoveFromFavoriteServers(this);
                    }
                    tickbox.ToolTip = TextManager.Get(tickbox.Selected ? "removefromfavorites" : "addtofavorites");
                    return true;
                }
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), frame.RectTransform),
                TextManager.AddPunctuation(':', TextManager.Get("ServerListVersion"),
                    string.IsNullOrEmpty(GameVersion) ? TextManager.Get("Unknown") : GameVersion))
            {
                CanBeFocused = false
            };

            bool hidePlaystyleBanner = !PlayStyle.HasValue;
            if (!hidePlaystyleBanner)
            {
                PlayStyle playStyle = PlayStyle ?? Networking.PlayStyle.Serious;
                Sprite playStyleBannerSprite = ServerListScreen.PlayStyleBanners[(int)playStyle];
                float playStyleBannerAspectRatio = playStyleBannerSprite.SourceRect.Width / playStyleBannerSprite.SourceRect.Height;
                var playStyleBanner = new GUIImage(new RectTransform(new Point(frame.Rect.Width, (int)(frame.Rect.Width / playStyleBannerAspectRatio)), frame.RectTransform),
                                                   playStyleBannerSprite, null, true);

                var playStyleName = new GUITextBlock(
                    new RectTransform(new Vector2(0.15f, 0.0f), playStyleBanner.RectTransform)
                        { RelativeOffset = new Vector2(0.0f, 0.06f) },
                    TextManager.AddPunctuation(':', TextManager.Get("serverplaystyle"),
                        TextManager.Get("servertag." + playStyle)), textColor: Color.White,
                    font: GUIStyle.SmallFont, textAlignment: Alignment.Center,
                    color: ServerListScreen.PlayStyleColors[(int)playStyle], style: "GUISlopedHeader");
                playStyleName.RectTransform.NonScaledSize = (playStyleName.Font.MeasureString(playStyleName.Text) + new Vector2(20, 5) * GUI.Scale).ToPoint();
                playStyleName.RectTransform.IsFixedSize = true;
            }

            var serverType = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), frame.RectTransform),
                TextManager.Get((OwnerID != 0 || LobbyID != 0) ? "SteamP2PServer" : "DedicatedServer"),
                textAlignment: Alignment.TopLeft)
            {
                CanBeFocused = false
            };
            serverType.RectTransform.MinSize = new Point(0, (int)(serverType.Rect.Height * 1.5f));

            var content = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.6f), frame.RectTransform))
            {
                Stretch = true
            };
            // playstyle tags -----------------------------------------------------------------------------

            var playStyleContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.15f), content.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.01f,
                CanBeFocused = true
            };

            var playStyleTags = GetPlayStyleTags();
            foreach (string tag in playStyleTags)
            {
                if (!ServerListScreen.PlayStyleIcons.ContainsKey(tag)) { continue; }

                new GUIImage(new RectTransform(Vector2.One, playStyleContainer.RectTransform),
                    ServerListScreen.PlayStyleIcons[tag], scaleToFit: true)
                {
                    ToolTip = TextManager.Get("servertagdescription." + tag),
                    Color = ServerListScreen.PlayStyleIconColors[tag]
                };
            }

            playStyleContainer.Recalculate();

            // -----------------------------------------------------------------------------

            float elementHeight = 0.075f;

            // Spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.025f), content.RectTransform), style: null);

            var serverMsg = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.3f), content.RectTransform)) { ScrollBarVisible = true };
            var msgText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), serverMsg.Content.RectTransform), ServerMessage, font: GUIStyle.SmallFont, wrap: true) 
            { 
                CanBeFocused = false 
            };
            serverMsg.Content.RectTransform.SizeChanged += () => { msgText.CalculateHeightFromText(); };
            msgText.RectTransform.SizeChanged += () => { serverMsg.UpdateScrollBarSize(); };

            var gameMode = new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), content.RectTransform), TextManager.Get("GameMode"));
            new GUITextBlock(new RectTransform(Vector2.One, gameMode.RectTransform),
                TextManager.Get(GameMode.IsEmpty ? "Unknown" : "GameMode." + GameMode).Fallback(GameMode.Value),
                textAlignment: Alignment.Right);

            GUITextBlock playStyleText = null;
            if (hidePlaystyleBanner && PlayStyle.HasValue)
            {
                PlayStyle playStyle = PlayStyle.Value;
                playStyleText = new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), content.RectTransform), TextManager.Get("serverplaystyle"));
                new GUITextBlock(new RectTransform(Vector2.One, playStyleText.RectTransform), TextManager.Get("servertag." + playStyle), textAlignment: Alignment.Right);
            }

            var subSelection = new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), content.RectTransform), TextManager.Get("ServerListSubSelection"));
            new GUITextBlock(new RectTransform(Vector2.One, subSelection.RectTransform), TextManager.Get(!SubSelectionMode.HasValue ? "Unknown" : SubSelectionMode.Value.ToString()), textAlignment: Alignment.Right);

            var modeSelection = new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), content.RectTransform), TextManager.Get("ServerListModeSelection"));
            new GUITextBlock(new RectTransform(Vector2.One, modeSelection.RectTransform), TextManager.Get(!ModeSelectionMode.HasValue ? "Unknown" : ModeSelectionMode.Value.ToString()), textAlignment: Alignment.Right);

            if (gameMode.TextSize.X + gameMode.GetChild<GUITextBlock>().TextSize.X > gameMode.Rect.Width ||
                subSelection.TextSize.X + subSelection.GetChild<GUITextBlock>().TextSize.X > subSelection.Rect.Width ||
                modeSelection.TextSize.X + modeSelection.GetChild<GUITextBlock>().TextSize.X > modeSelection.Rect.Width)
            {
                gameMode.Font = subSelection.Font = modeSelection.Font = GUIStyle.SmallFont;
                gameMode.GetChild<GUITextBlock>().Font = subSelection.GetChild<GUITextBlock>().Font = modeSelection.GetChild<GUITextBlock>().Font = GUIStyle.SmallFont;
                if (playStyleText != null)
                {
                    playStyleText.Font = playStyleText.GetChild<GUITextBlock>().Font = GUIStyle.SmallFont;
                }
            }

            var allowSpectating = new GUITickBox(new RectTransform(new Vector2(1, elementHeight), content.RectTransform), TextManager.Get("ServerListAllowSpectating"))
            {
                CanBeFocused = false
            };
            if (!AllowSpectating.HasValue)
                new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.8f), allowSpectating.Box.RectTransform, Anchor.Center), "?", textAlignment: Alignment.Center);
            else
                allowSpectating.Selected = AllowSpectating.Value;

            var allowRespawn = new GUITickBox(new RectTransform(new Vector2(1, elementHeight), content.RectTransform), TextManager.Get("ServerSettingsAllowRespawning"))
            {
                CanBeFocused = false
            };
            if (!AllowRespawn.HasValue)
                new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.8f), allowRespawn.Box.RectTransform, Anchor.Center), "?", textAlignment: Alignment.Center);
            else
                allowRespawn.Selected = AllowRespawn.Value;

            /*var voipEnabledTickBox = new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), bodyContainer.RectTransform), TextManager.Get("serversettingsvoicechatenabled"))
            {
                CanBeFocused = false
            };
            if (!VoipEnabled.HasValue)
                new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.8f), voipEnabledTickBox.Box.RectTransform, Anchor.Center), "?", textAlignment: Alignment.Center);
            else
                voipEnabledTickBox.Selected = VoipEnabled.Value;*/

            var usingWhiteList = new GUITickBox(new RectTransform(new Vector2(1, elementHeight), content.RectTransform), TextManager.Get("ServerListUsingWhitelist"))
            {
                CanBeFocused = false
            };
            if (!UsingWhiteList.HasValue)
                new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.8f), usingWhiteList.Box.RectTransform, Anchor.Center), "?", textAlignment: Alignment.Center);
            else
                usingWhiteList.Selected = UsingWhiteList.Value;

            content.RectTransform.SizeChanged += () =>
            {
                GUITextBlock.AutoScaleAndNormalize(allowSpectating.TextBlock, allowRespawn.TextBlock, usingWhiteList.TextBlock);
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform),
                TextManager.Get("ServerListContentPackages"), textAlignment: Alignment.Center, font: GUIStyle.SubHeadingFont);

            var contentPackageList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.3f), frame.RectTransform))
            {
                ScrollBarVisible = true,
                OnSelected = (component, o) => false
            };
            if (ContentPackageNames.Count == 0)
            {
                new GUITextBlock(new RectTransform(Vector2.One, contentPackageList.Content.RectTransform), TextManager.Get("Unknown"), textAlignment: Alignment.Center)
                {
                    CanBeFocused = false
                };
            }
            else
            {
                for (int i = 0; i < ContentPackageNames.Count; i++)
                {
                    var packageText = new GUITickBox(
                        new RectTransform(new Vector2(1.0f, 0.15f), contentPackageList.Content.RectTransform)
                            { MinSize = new Point(0, 15) },
                        ContentPackageNames[i])
                    {
                        Enabled = false
                    };
                    packageText.Box.Enabled = true;
                    packageText.TextBlock.Enabled = true;
                    if (i < ContentPackageHashes.Count)
                    {
                        if (ContentPackageManager.AllPackages.Any(contentPackage => contentPackage.Hash.StringRepresentation == ContentPackageHashes[i]))
                        {
                            packageText.TextColor = GUIStyle.Green;
                            packageText.Selected = true;
                        }
                        //workshop download link found
                        else if (i < ContentPackageWorkshopIds.Count && ContentPackageWorkshopIds[i] != 0)
                        {
                            packageText.ToolTip = TextManager.GetWithVariable("ServerListIncompatibleContentPackageWorkshopAvailable", "[contentpackage]", ContentPackageNames[i]);
                        }
                        else //no package or workshop download link found (TODO: update text to say that they could be downloaded through the server)
                        {
                            packageText.TextColor = GameMain.VanillaContent.NameMatches(ContentPackageNames[i]) ? GUIStyle.Red : GUIStyle.Yellow;
                            packageText.ToolTip = TextManager.GetWithVariables("ServerListIncompatibleContentPackage",
                                ("[contentpackage]", ContentPackageNames[i]), ("[hash]", ContentPackageHashes[i]));
                        }
                    }
                }
            }

            // -----------------------------------------------------------------------------

            foreach (GUIComponent c in content.Children)
            {
                if (c is GUITextBlock textBlock) { textBlock.Padding = Vector4.Zero; }
            }
        }

        public IEnumerable<string> GetPlayStyleTags()
        {
            List<string> tags = new List<string>();
            if (KarmaEnabled.HasValue)
            {
                tags.Add(KarmaEnabled.Value ? "karma.true" : "karma.false");
            }
            if (TraitorsEnabled.HasValue)
            {
                tags.Add(TraitorsEnabled.Value == YesNoMaybe.Maybe ? 
                    "traitors.maybe" :
                    (TraitorsEnabled.Value == YesNoMaybe.Yes ? "traitors.true" : "traitors.false"));
            }
            if (VoipEnabled.HasValue)
            {
                tags.Add(VoipEnabled.Value ? "voip.true" : "voip.false");
            }
            if (FriendlyFireEnabled.HasValue)
            {
                tags.Add(FriendlyFireEnabled.Value ? "friendlyfire.true" : "friendlyfire.false");
            }
            if (ContentPackageNames.Count > 0)
            {
                tags.Add(ContentPackageNames.Count > 1 || !GameMain.VanillaContent.NameMatches(ContentPackageNames[0]) ? "modded.true" : "modded.false");
            }
            return tags;
        }

        public static ServerInfo FromXElement(XElement element)
        {
            ServerInfo info = new ServerInfo()
            {
                ServerName = element.GetAttributeString("ServerName", ""),
                ServerMessage = element.GetAttributeString("ServerMessage", ""),
                IP = element.GetAttributeString("IP", ""),
                Port = element.GetAttributeString("Port", ""),
                QueryPort = element.GetAttributeString("QueryPort", ""),
                OwnerID = element.GetAttributeSteamID("OwnerID",0)
            };

            info.RespondedToSteamQuery = null;

            info.GameMode = element.GetAttributeIdentifier("GameMode", Identifier.Empty);
            info.GameVersion = element.GetAttributeString("GameVersion", "");

            int maxPlayersElement = element.GetAttributeInt("MaxPlayers", 0);

            if (maxPlayersElement > NetConfig.MaxPlayers)
            {
                /*DebugConsole.IsOpen = true;
                DebugConsole.NewMessage($"Setting the maximum amount of players to {maxPlayersElement} failed due to exceeding the limit of {NetConfig.MaxPlayers} players per server. Using the maximum of {NetConfig.MaxPlayers} instead.", Color.Red);*/
                maxPlayersElement = NetConfig.MaxPlayers;
            }

            info.MaxPlayers = maxPlayersElement;

            if (Enum.TryParse(element.GetAttributeString("PlayStyle", ""), out PlayStyle playStyleTemp)) { info.PlayStyle = playStyleTemp; }
            if (bool.TryParse(element.GetAttributeString("UsingWhiteList", ""), out bool whitelistTemp)) { info.UsingWhiteList = whitelistTemp; }
            if (Enum.TryParse(element.GetAttributeString("TraitorsEnabled", ""), out YesNoMaybe traitorsTemp)) { info.TraitorsEnabled = traitorsTemp; }
            if (Enum.TryParse(element.GetAttributeString("SubSelectionMode", ""), out SelectionMode subSelectionTemp)) { info.SubSelectionMode = subSelectionTemp; }
            if (Enum.TryParse(element.GetAttributeString("ModeSelectionMode", ""), out SelectionMode modeSelectionTemp)) { info.ModeSelectionMode = modeSelectionTemp; }
            if (bool.TryParse(element.GetAttributeString("VoipEnabled", ""), out bool voipTemp)) { info.VoipEnabled = voipTemp; }
            if (bool.TryParse(element.GetAttributeString("KarmaEnabled", ""), out bool karmaTemp)) { info.KarmaEnabled = karmaTemp; }
            if (bool.TryParse(element.GetAttributeString("FriendlyFireEnabled", ""), out bool friendlyFireTemp)) { info.FriendlyFireEnabled = friendlyFireTemp; }

            info.HasPassword = element.GetAttributeBool("HasPassword", false);

            return info;
        }

        public void QueryLiveInfo(Action<Networking.ServerInfo> onServerRulesReceived, Action<Networking.ServerInfo> onQueryDone)
        {
            if (!SteamManager.IsInitialized) { return; }

            if (int.TryParse(QueryPort, out int parsedPort) && IPAddress.TryParse(IP, out IPAddress parsedIP))
            {
                if (MatchmakingPingResponse?.QueryActive ?? false)
                {
                    MatchmakingPingResponse.Cancel();
                }

                MatchmakingPingResponse = new Steamworks.SteamMatchmakingPingResponse(
                    (server) =>
                    {
                        ServerName = server.Name;
                        RespondedToSteamQuery = true;
                        PlayerCount = server.Players;
                        MaxPlayers = server.MaxPlayers;
                        HasPassword = server.Passworded;
                        PingChecked = true;
                        Ping = server.Ping;
                        LobbyID = 0;
                        TaskPool.Add("QueryServerRules (QueryLiveInfo)", server.QueryRulesAsync(),
                        (t) =>
                        {
                            onQueryDone(this);
                            if (t.Status == TaskStatus.Faulted)
                            {
                                TaskPool.PrintTaskExceptions(t, "Failed to retrieve rules for " + ServerName);
                                return;
                            }

                            t.TryGetResult(out Dictionary<string, string> rules);
                            SteamManager.AssignServerRulesToServerInfo(rules, this);

                            onServerRulesReceived(this);
                        });
                    },
                    () =>
                    {
                        RespondedToSteamQuery = false;
                    });

                MatchmakingPingResponse.HQueryPing(parsedIP, parsedPort);
            }
            else if (OwnerID != 0)
            {
                if (SteamFriend == null)
                {
                    SteamFriend = new Steamworks.Friend(OwnerID);
                }
                if (LobbyID == 0)
                {
                    TaskPool.Add("RequestSteamP2POwnerInfo", SteamFriend?.RequestInfoAsync(),
                        (t) =>
                        {
                            onQueryDone(this);
                            if ((SteamFriend?.IsPlayingThisGame ?? false) && ((SteamFriend?.GameInfo?.Lobby?.Id ?? 0) != 0))
                            {
                                LobbyID = SteamFriend?.GameInfo?.Lobby?.Id.Value ?? 0;
                                Steamworks.SteamMatchmaking.OnLobbyDataChanged += UpdateInfoFromSteamworksLobby;
                                SteamFriend?.GameInfo?.Lobby?.Refresh();
                            }
                            else
                            {
                                RespondedToSteamQuery = false;
                            }
                        });
                }
                else
                {
                    onQueryDone(this);
                }
            }
        }

        private void UpdateInfoFromSteamworksLobby(Steamworks.Data.Lobby lobby)
        {
            if (lobby.Id != LobbyID) { return; }
            Steamworks.SteamMatchmaking.OnLobbyDataChanged -= UpdateInfoFromSteamworksLobby;
            if (string.IsNullOrWhiteSpace(lobby.GetData("haspassword"))) { return; }
            bool.TryParse(lobby.GetData("haspassword"), out bool hasPassword);
            int.TryParse(lobby.GetData("playercount"), out int currPlayers);
            int.TryParse(lobby.GetData("maxplayernum"), out int maxPlayers);
            UInt64 ownerId = SteamManager.SteamIDStringToUInt64(lobby.GetData("lobbyowner"));

            if (OwnerID != ownerId) { return; }

            ServerName = lobby.GetData("name");
            IP = "";
            Port = "";
            QueryPort = "";
            PlayerCount = currPlayers;
            MaxPlayers = maxPlayers;
            HasPassword = hasPassword;
            RespondedToSteamQuery = true;
            LobbyID = lobby.Id;
            OwnerID = ownerId;
            PingChecked = false;
            OwnerVerified = true;

            SteamManager.AssignLobbyDataToServerInfo(lobby, this);
        }

        public XElement ToXElement()
        {
            if (OwnerID == 0 && string.IsNullOrEmpty(Port))
            {
                return null; //can't save this one since it's not set up correctly
            }

            XElement element = new XElement("ServerInfo");

            element.SetAttributeValue("ServerName", ServerName);
            element.SetAttributeValue("ServerMessage", ServerMessage);
            if (OwnerID == 0)
            {
                element.SetAttributeValue("IP", IP);
                element.SetAttributeValue("Port", Port);
                element.SetAttributeValue("QueryPort", QueryPort);
            }
            else
            {
                element.SetAttributeValue("OwnerID", SteamManager.SteamIDUInt64ToString(OwnerID));
            }

            element.SetAttributeValue("GameMode", GameMode);
            element.SetAttributeValue("GameVersion", GameVersion ?? "");
            element.SetAttributeValue("MaxPlayers", MaxPlayers);
            if (PlayStyle.HasValue) { element.SetAttributeValue("PlayStyle", PlayStyle.Value.ToString()); }
            if (UsingWhiteList.HasValue) { element.SetAttributeValue("UsingWhiteList", UsingWhiteList.Value.ToString()); }
            if (TraitorsEnabled.HasValue) { element.SetAttributeValue("TraitorsEnabled", TraitorsEnabled.Value.ToString()); }
            if (SubSelectionMode.HasValue) { element.SetAttributeValue("SubSelectionMode", SubSelectionMode.Value.ToString()); }
            if (ModeSelectionMode.HasValue) { element.SetAttributeValue("ModeSelectionMode", ModeSelectionMode.Value.ToString()); }
            if (VoipEnabled.HasValue) { element.SetAttributeValue("VoipEnabled", VoipEnabled.Value.ToString()); }
            if (KarmaEnabled.HasValue) { element.SetAttributeValue("KarmaEnabled", KarmaEnabled.Value.ToString()); }
            if (FriendlyFireEnabled.HasValue) { element.SetAttributeValue("FriendlyFireEnabled", FriendlyFireEnabled.Value.ToString()); }
            element.SetAttributeValue("HasPassword", HasPassword.ToString());

            return element;
        }

        public override bool Equals(object obj)
        {
            return obj is ServerInfo other ? Equals(other) : base.Equals(obj);
        }

        public bool Equals(ServerInfo other)
        {
            return
                other.OwnerID == OwnerID &&
                (other.LobbyID == LobbyID || other.LobbyID == 0 || LobbyID == 0) &&
                ((OwnerID == 0) ? (other.IP == IP && other.Port == Port) : true);
        }

        public bool MatchesByEndpoint(ServerInfo other)
        {
            return OwnerID == other.OwnerID && (OwnerID != 0 ? true : (IP == other.IP && Port == other.Port));
        }
    }
}
