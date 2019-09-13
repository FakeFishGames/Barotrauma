using Barotrauma.Steam;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Xml.Linq;

namespace Barotrauma.Networking
{
    class ServerInfo
    {
        public string IP;
        public string Port;
        public string QueryPort;

        public UInt64 LobbyID;
        public UInt64 OwnerID;

        public string ServerName;
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
        public string GameMode;
        public PlayStyle? PlayStyle;

        public bool Recent;
        public bool Favorite;

        public bool? RespondedToSteamQuery = null;

        public Facepunch.Steamworks.SteamFriend SteamFriend;
        public Facepunch.Steamworks.ISteamMatchmakingPingResponse MatchmakingPingResponse;
        public int? PingHQuery;

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
        public List<string> ContentPackageWorkshopUrls
        {
            get;
            private set;
        } = new List<string>();
        
        public bool ContentPackagesMatch(IEnumerable<ContentPackage> myContentPackages)
        {
            //make sure we have all the packages the server requires
            foreach (string hash in ContentPackageHashes)
            {
                if (!myContentPackages.Any(myPackage => myPackage.MD5hash.Hash == hash)) { return false; }
            }            

            //make sure the server isn't missing any of our packages that cause multiplayer incompatibility
            foreach (ContentPackage myPackage in myContentPackages)
            {
                if (myPackage.HasMultiplayerIncompatibleContent)
                {
                    if (!ContentPackageHashes.Any(hash => hash == myPackage.MD5hash.Hash)) { return false; }
                }
            }

            return true;
        }

        public bool ContentPackagesMatch(IEnumerable<string> myContentPackageHashes)
        {
            HashSet<string> contentPackageHashes = new HashSet<string>(ContentPackageHashes);
            return contentPackageHashes.SetEquals(myContentPackageHashes);
        }

        public void CreatePreviewWindow(GUIFrame frame)
        {
            frame.ClearChildren();

            if (frame == null) return;

            var previewContainer =  new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), frame.RectTransform, Anchor.Center))
            {
                Stretch = true
            };

            PlayStyle playStyle = PlayStyle.HasValue ? PlayStyle.Value : Networking.PlayStyle.Serious;

            Sprite playStyleBannerSprite = GameMain.ServerListScreen.PlayStyleBanners[(int)playStyle];
            float playStyleBannerAspectRatio = playStyleBannerSprite.SourceRect.Width / (playStyleBannerSprite.SourceRect.Height * 0.625f);
            var playStyleBanner = new GUIImage(new RectTransform(new Vector2(1.0f, 1.0f / playStyleBannerAspectRatio), previewContainer.RectTransform, Anchor.TopCenter, scaleBasis: ScaleBasis.BothWidth),
                                               playStyleBannerSprite, null, true);

            GUITickBox favoriteTickBox = new GUITickBox(new RectTransform(new Vector2(0.05f, 0.05f), frame.RectTransform, Anchor.TopRight) { RelativeOffset = new Vector2(0.05f, 0.01f) }, "", null, "GUIServerListFavoriteTickBox")
            {
                Selected = Favorite,
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
                    return true;
                }
            };

            var playStyleName = new GUITextBlock(new RectTransform(new Vector2(0.15f, 0.0f), playStyleBanner.RectTransform) { RelativeOffset = new Vector2(0.01f, 0.06f) },
                TextManager.AddPunctuation(':', TextManager.Get("serverplaystyle"), TextManager.Get("servertag."+ playStyle)), textColor: Color.White, 
                font: GUI.SmallFont, textAlignment: Alignment.Center, 
                color: GameMain.ServerListScreen.PlayStyleColors[(int)playStyle], style: "GUISlopedHeader");
            playStyleName.RectTransform.NonScaledSize = (playStyleName.Font.MeasureString(playStyleName.Text) + new Vector2(20, 5) * GUI.Scale).ToPoint();
            playStyleName.RectTransform.IsFixedSize = true;

            var columnContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.45f), previewContainer.RectTransform), isHorizontal: true)
            {
                Stretch = true
            };

            // left column -----------------------------------------------------------------------------

            var leftColumnHolder = new GUILayoutGroup(new RectTransform(new Vector2(0.75f, 1.0f), columnContainer.RectTransform), childAnchor: Anchor.Center)
            {
                Stretch = true
            };

            var leftColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 1.0f), leftColumnHolder.RectTransform))
            {
                Stretch = true
            };

            float elementHeight = 0.075f;

            var title = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), leftColumn.RectTransform), ServerName, font: GUI.LargeFont);
            title.Text = ToolBox.LimitString(title.Text, title.Font, title.Rect.Width);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), leftColumn.RectTransform),
                TextManager.AddPunctuation(':', TextManager.Get("ServerListVersion"), string.IsNullOrEmpty(GameVersion) ? TextManager.Get("Unknown") : GameVersion));

            var serverMsg = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.5f), leftColumn.RectTransform)) { ScrollBarVisible = true };
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), serverMsg.Content.RectTransform), ServerMessage, font: GUI.SmallFont, wrap: true) { CanBeFocused = false };

            var gameMode = new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), leftColumn.RectTransform), TextManager.Get("GameMode"));
            new GUITextBlock(new RectTransform(Vector2.One, gameMode.RectTransform),
                TextManager.Get(string.IsNullOrEmpty(GameMode) ? "Unknown" : "GameMode." + GameMode, returnNull: true) ?? GameMode,
                textAlignment: Alignment.Right);

            /*var traitors = new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), leftColumn.RectTransform), TextManager.Get("Traitors"));
            new GUITextBlock(new RectTransform(Vector2.One, traitors.RectTransform), TextManager.Get(!TraitorsEnabled.HasValue ? "Unknown" : TraitorsEnabled.Value.ToString()), textAlignment: Alignment.Right);*/

            var subSelection = new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), leftColumn.RectTransform), TextManager.Get("ServerListSubSelection"));
            new GUITextBlock(new RectTransform(Vector2.One, subSelection.RectTransform), TextManager.Get(!SubSelectionMode.HasValue ? "Unknown" : SubSelectionMode.Value.ToString()), textAlignment: Alignment.Right);

            var modeSelection = new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), leftColumn.RectTransform), TextManager.Get("ServerListModeSelection"));
            new GUITextBlock(new RectTransform(Vector2.One, modeSelection.RectTransform), TextManager.Get(!ModeSelectionMode.HasValue ? "Unknown" : ModeSelectionMode.Value.ToString()), textAlignment: Alignment.Right);

            var allowSpectating = new GUITickBox(new RectTransform(new Vector2(1, elementHeight), leftColumn.RectTransform), TextManager.Get("ServerListAllowSpectating"))
            {
                CanBeFocused = false
            };
            if (!AllowSpectating.HasValue)
                new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.8f), allowSpectating.Box.RectTransform, Anchor.Center), "?", textAlignment: Alignment.Center);
            else
                allowSpectating.Selected = AllowSpectating.Value;

            var allowRespawn = new GUITickBox(new RectTransform(new Vector2(1, elementHeight), leftColumn.RectTransform), TextManager.Get("ServerSettingsAllowRespawning"))
            {
                CanBeFocused = false
            };
            if (!AllowRespawn.HasValue)
                new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.8f), allowRespawn.Box.RectTransform, Anchor.Center), "?", textAlignment: Alignment.Center);
            else
                allowRespawn.Selected = AllowRespawn.Value;

            /*var voipEnabledTickBox = new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), leftColumn.RectTransform), TextManager.Get("serversettingsvoicechatenabled"))
            {
                CanBeFocused = false
            };
            if (!VoipEnabled.HasValue)
                new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.8f), voipEnabledTickBox.Box.RectTransform, Anchor.Center), "?", textAlignment: Alignment.Center);
            else
                voipEnabledTickBox.Selected = VoipEnabled.Value;*/

            var usingWhiteList = new GUITickBox(new RectTransform(new Vector2(1, elementHeight), leftColumn.RectTransform), TextManager.Get("ServerListUsingWhitelist"))
            {
                CanBeFocused = false
            };
            if (!UsingWhiteList.HasValue)
                new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.8f), usingWhiteList.Box.RectTransform, Anchor.Center), "?", textAlignment: Alignment.Center);
            else
                usingWhiteList.Selected = UsingWhiteList.Value;


            leftColumn.RectTransform.SizeChanged += () =>
            {
                GUITextBlock.AutoScaleAndNormalize(allowSpectating.TextBlock, allowRespawn.TextBlock, usingWhiteList.TextBlock);
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), leftColumn.RectTransform),
                TextManager.Get("ServerListContentPackages"));

            var contentPackageList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.3f), leftColumn.RectTransform)) { ScrollBarVisible = true };
            if (ContentPackageNames.Count == 0)
            {
                new GUITextBlock(new RectTransform(Vector2.One, contentPackageList.Content.RectTransform), TextManager.Get("Unknown"), textAlignment: Alignment.Center)
                {
                    CanBeFocused = false
                };
            }
            else
            {
                List<string> availableWorkshopUrls = new List<string>();
                for (int i = 0; i < ContentPackageNames.Count; i++)
                {
                    var packageText = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.15f), contentPackageList.Content.RectTransform) { MinSize = new Point(0, 15) },
                        ContentPackageNames[i])
                    {
                        Enabled = false
                    };
                    if (i < ContentPackageHashes.Count)
                    {
                        if (GameMain.Config.SelectedContentPackages.Any(cp => cp.MD5hash.Hash == ContentPackageHashes[i]))
                        {
                            packageText.Selected = true;
                            continue;
                        }

                        //matching content package found, but it hasn't been enabled
                        if (ContentPackage.List.Any(cp => cp.MD5hash.Hash == ContentPackageHashes[i]))
                        {
                            packageText.TextColor = Color.Orange;
                            packageText.ToolTip = TextManager.GetWithVariable("ServerListContentPackageNotEnabled", "[contentpackage]", ContentPackageNames[i]);
                        }
                        //workshop download link found
                        else if (i < ContentPackageWorkshopUrls.Count && !string.IsNullOrEmpty(ContentPackageWorkshopUrls[i]))
                        {
                            availableWorkshopUrls.Add(ContentPackageWorkshopUrls[i]);
                            packageText.TextColor = Color.Yellow;
                            packageText.ToolTip = TextManager.GetWithVariable("ServerListIncompatibleContentPackageWorkshopAvailable", "[contentpackage]", ContentPackageNames[i]);
                        }
                        else //no package or workshop download link found, tough luck
                        {
                            packageText.TextColor = Color.Red;
                            packageText.ToolTip = TextManager.GetWithVariables("ServerListIncompatibleContentPackage",
                                new string[2] { "[contentpackage]", "[hash]" }, new string[2] { ContentPackageNames[i], ContentPackageHashes[i] });
                        }
                    }
                }
                if (availableWorkshopUrls.Count > 0)
                {
                    var workshopBtn = new GUIButton(new RectTransform(new Vector2(1.0f, 0.1f), leftColumn.RectTransform), TextManager.Get("ServerListSubscribeMissingPackages"))
                    {
                        ToolTip = TextManager.Get(SteamManager.IsInitialized ? "ServerListSubscribeMissingPackagesTooltip" : "ServerListSubscribeMissingPackagesTooltipNoSteam"),
                        Enabled = SteamManager.IsInitialized,
                        OnClicked = (btn, userdata) =>
                        {
                            GameMain.SteamWorkshopScreen.SubscribeToPackages(availableWorkshopUrls);
                            GameMain.SteamWorkshopScreen.Select();
                            return true;
                        }
                    };
                    workshopBtn.TextBlock.AutoScale = true;
                }
            }
            // right column -----------------------------------------------------------------------------

            var rightColumnBackground = new GUIFrame(new RectTransform(new Vector2(0.2f, 1.0f), columnContainer.RectTransform), style: null)
            {
                Color = Color.Black * 0.25f
            };

            var rightColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 1.0f), rightColumnBackground.RectTransform, Anchor.Center));

            var playStyleTags = GetPlayStyleTags();
            foreach (string tag in playStyleTags)
            {
                if (!GameMain.ServerListScreen.PlayStyleIcons.ContainsKey(tag)) { continue; }

                new GUIImage(new RectTransform(Vector2.One, rightColumn.RectTransform, scaleBasis: ScaleBasis.BothWidth),
                    GameMain.ServerListScreen.PlayStyleIcons[tag], scaleToFit: true)
                {
                    ToolTip = TextManager.Get("servertagdescription." + tag),
                    Color = GameMain.ServerListScreen.PlayStyleIconColors[tag]
                };
            }

            /*var playerCount = new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), columnRight.RectTransform), TextManager.Get("ServerListPlayers"));
            new GUITextBlock(new RectTransform(Vector2.One, playerCount.RectTransform), PlayerCount + "/" + MaxPlayers, textAlignment: Alignment.Right);


            new GUITickBox(new RectTransform(new Vector2(1, elementHeight), columnRight.RectTransform), "Round running")
            {
                Selected = GameStarted,
                CanBeFocused = false
            };*/


            // -----------------------------------------------------------------------------

            foreach (GUIComponent c in leftColumn.Children)
            {
                if (c is GUITextBlock textBlock) textBlock.Padding = Vector4.Zero;
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
                tags.Add(ContentPackageNames.Count > 1 || ContentPackageNames[0] != GameMain.VanillaContent?.Name ? "modded.true" : "modded.false");
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

            info.GameMode = element.GetAttributeString("GameMode", "");
            info.GameVersion = element.GetAttributeString("GameVersion", "");
            info.MaxPlayers = element.GetAttributeInt("MaxPlayers", 0);

            if (Enum.TryParse(element.GetAttributeString("PlayStyle", ""), out PlayStyle playStyleTemp)) { info.PlayStyle = playStyleTemp; }
            if (bool.TryParse(element.GetAttributeString("UsingWhiteList", ""), out bool whitelistTemp)) { info.UsingWhiteList = whitelistTemp; }
            if (Enum.TryParse(element.GetAttributeString("TraitorsEnabled", ""), out YesNoMaybe traitorsTemp)) { info.TraitorsEnabled = traitorsTemp; }
            if (Enum.TryParse(element.GetAttributeString("SubSelectionMode", ""), out SelectionMode subSelectionTemp)) { info.SubSelectionMode = subSelectionTemp; }
            if (Enum.TryParse(element.GetAttributeString("ModeSelectionMode", ""), out SelectionMode modeSelectionTemp)) { info.ModeSelectionMode = subSelectionTemp; }
            if (bool.TryParse(element.GetAttributeString("VoipEnabled", ""), out bool voipTemp)) { info.VoipEnabled = voipTemp; }
            if (bool.TryParse(element.GetAttributeString("KarmaEnabled", ""), out bool karmaTemp)) { info.KarmaEnabled = karmaTemp; }
            if (bool.TryParse(element.GetAttributeString("FriendlyFireEnabled", ""), out bool friendlyFireTemp)) { info.FriendlyFireEnabled = friendlyFireTemp; }

            info.HasPassword = element.GetAttributeBool("HasPassword", false);

            return info;
        }

        public void QueryLiveInfo(Action<Networking.ServerInfo> onServerRulesReceived)
        {
            if (!SteamManager.IsInitialized) { return; }

            if (int.TryParse(QueryPort, out _))
            {
                if (PingHQuery != null)
                {
                    SteamManager.Instance.ServerList.CancelHQuery(PingHQuery.Value);
                    PingHQuery = null;
                }

                MatchmakingPingResponse = new Facepunch.Steamworks.ISteamMatchmakingPingResponse(
                    SteamManager.Instance.Client,
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
                        server.FetchRules();
                        server.OnReceivedRules += (bool received) => { SteamManager.OnReceivedRules(server, this, received); onServerRulesReceived?.Invoke(this); };
                    },
                    () =>
                    {
                        RespondedToSteamQuery = false;
                    });

                PingHQuery = SteamManager.Instance.ServerList.HQueryPing(MatchmakingPingResponse, IPAddress.Parse(IP), int.Parse(QueryPort));
            }
            else if (OwnerID != 0)
            {
                if (SteamFriend == null)
                {
                    SteamFriend = SteamManager.Instance.Friends.Get(OwnerID);
                }
                if (LobbyID == 0)
                {
                    SteamFriend.Refresh();
                    if (SteamFriend.IsPlayingThisGame && SteamFriend.ServerLobbyId != 0)
                    {
                        LobbyID = SteamFriend.ServerLobbyId;
                        SteamManager.Instance.LobbyList.SetManualLobbyDataCallback(LobbyID, (lobby) =>
                        {
                            SteamManager.Instance.LobbyList.SetManualLobbyDataCallback(LobbyID, null);
                            if (OwnerID != lobby.Owner) { return; }
                            if (string.IsNullOrWhiteSpace(lobby.GetData("haspassword"))) { return; }
                            bool.TryParse(lobby.GetData("haspassword"), out bool hasPassword);
                            int.TryParse(lobby.GetData("playercount"), out int currPlayers);
                            int.TryParse(lobby.GetData("maxplayernum"), out int maxPlayers);
                            //UInt64.TryParse(lobby.GetData("connectsteamid"), out ulong connectSteamId);
                            string ip = lobby.GetData("hostipaddress");
                            if (string.IsNullOrWhiteSpace(ip)) { ip = ""; }

                            ServerName = lobby.Name;
                            Port = "";
                            QueryPort = "";
                            IP = ip;
                            PlayerCount = currPlayers;
                            MaxPlayers = maxPlayers;
                            HasPassword = hasPassword;
                            RespondedToSteamQuery = true;
                            LobbyID = lobby.LobbyID;
                            OwnerID = lobby.Owner;
                            PingChecked = false;
                            SteamManager.AssignLobbyDataToServerInfo(lobby, this);

                            onServerRulesReceived?.Invoke(this);
                        });
                        SteamManager.Instance.LobbyList.RequestLobbyData(LobbyID);
                    }
                    else
                    {
                        RespondedToSteamQuery = false;
                    }
                }
            }
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
            if (LobbyID == 0 || OwnerID == 0)
            {
                element.SetAttributeValue("IP", IP);
                element.SetAttributeValue("Port", Port);
                element.SetAttributeValue("QueryPort", QueryPort);
            }
            else
            {
                element.SetAttributeValue("OwnerID", SteamManager.SteamIDUInt64ToString(OwnerID));
            }

            element.SetAttributeValue("GameMode", GameMode ?? "");
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
    }
}
