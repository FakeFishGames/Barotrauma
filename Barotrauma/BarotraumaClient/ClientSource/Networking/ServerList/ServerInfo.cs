#nullable enable

using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Barotrauma.Steam;

namespace Barotrauma.Networking
{
    sealed class ServerInfo : ISerializableEntity
    {
        public abstract class DataSource
        {
            public static Option<DataSource> Parse(XElement element)
                => ReflectionUtils.ParseDerived<DataSource, XElement>(element);
            public abstract void Write(XElement element);
        }

        public Endpoint Endpoint { get; private set; }

        public Option<DataSource> MetadataSource = Option<DataSource>.None();

        [Serialize("", IsPropertySaveable.Yes)]
        public string ServerName { get; set; } = "";
        
        [Serialize("", IsPropertySaveable.Yes)]
        public string ServerMessage { get; set; } = "";

        public int PlayerCount { get; set; }
        
        [Serialize(0, IsPropertySaveable.Yes)]
        public int MaxPlayers { get; set; }

        public bool GameStarted { get; set; }

        [Serialize(false, IsPropertySaveable.Yes)]
        public bool HasPassword { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier GameMode { get; set; }
        
        [Serialize(SelectionMode.Manual, IsPropertySaveable.Yes)]
        public SelectionMode ModeSelectionMode { get; set; }
        
        [Serialize(SelectionMode.Manual, IsPropertySaveable.Yes)]
        public SelectionMode SubSelectionMode { get; set; }
        
        [Serialize(false, IsPropertySaveable.Yes)]
        public bool AllowSpectating { get; set; }
        
        [Serialize(false, IsPropertySaveable.Yes)]
        public bool VoipEnabled { get; set; }
        
        [Serialize(false, IsPropertySaveable.Yes)]
        public bool KarmaEnabled { get; set; }
        
        [Serialize(false, IsPropertySaveable.Yes)]
        public bool FriendlyFireEnabled { get; set; }
        
        [Serialize(false, IsPropertySaveable.Yes)]
        public bool AllowRespawn { get; set; }
        
        [Serialize(YesNoMaybe.No, IsPropertySaveable.Yes)]
        public YesNoMaybe TraitorsEnabled { get; set; }
        
        [Serialize(PlayStyle.Casual, IsPropertySaveable.Yes)]
        public PlayStyle PlayStyle { get; set; }
        
        [Serialize("", IsPropertySaveable.Yes)]
        public LanguageIdentifier Language { get; set; }

        public Version GameVersion { get; set; } = new Version(0, 0, 0, 0);

        public Option<int> Ping = Option<int>.None();

        public bool Checked = false;

        public readonly struct ContentPackageInfo
        {
            public readonly string Name;
            public readonly string Hash;
            public readonly Option<ContentPackageId> Id;

            public ContentPackageInfo(string name, string hash, Option<ContentPackageId> id)
            {
                Name = name;
                Hash = hash;
                Id = id;
            }

            public ContentPackageInfo(ContentPackage pkg)
            {
                Name = pkg.Name;
                Hash = pkg.Hash.StringRepresentation;
                Id = pkg.UgcId;
            }
        }

        public ImmutableArray<ContentPackageInfo> ContentPackages;

        public bool IsModded => ContentPackages.Any(p => !GameMain.VanillaContent.NameMatches(p.Name));

        public ServerInfo(Endpoint endpoint)
        {
            SerializableProperties = SerializableProperty.GetProperties(this);
            Endpoint = endpoint;
            ContentPackages = ImmutableArray<ContentPackageInfo>.Empty;
        }

        public static ServerInfo FromServerConnection(NetworkConnection connection, ServerSettings serverSettings)
        {
            var serverInfo = new ServerInfo(connection.Endpoint)
            {
                GameMode = GameMain.NetLobbyScreen.SelectedMode?.Identifier ?? Identifier.Empty,
                GameStarted = Screen.Selected != GameMain.NetLobbyScreen,
                GameVersion = GameMain.Version,
                PlayerCount = GameMain.Client.ConnectedClients.Count,
                ContentPackages = ContentPackageManager.EnabledPackages.All.Select(p => new ContentPackageInfo(p)).ToImmutableArray(),
                Ping = GameMain.Client.Ping,
                
                // -------------------------------------
                // Settings that cannot be copied via
                // SerializableProperty because they do
                // not implement the attribute
                ServerName = serverSettings.ServerName,
                ServerMessage = serverSettings.ServerMessageText,
                // -------------------------------------
                // Settings that cannot be copied via
                // SerializableProperty due to name mismatch
                HasPassword = serverSettings.HasPassword,
                VoipEnabled = serverSettings.VoiceChatEnabled,
                FriendlyFireEnabled = serverSettings.AllowFriendlyFire,
                // -------------------------------------
                
                Checked = true
            };

            var serverInfoSerializableProperties
                = SerializableProperty.GetProperties(serverInfo);
            var serverSettingsSerializableProperties
                = SerializableProperty.GetProperties(serverSettings);

            var intersection = serverInfoSerializableProperties.Keys
                .Where(serverSettingsSerializableProperties.ContainsKey);

            foreach (var key in intersection)
            {
                var propToGet = serverSettingsSerializableProperties[key];
                var propToSet = serverInfoSerializableProperties[key];
                if (!propToGet.PropertyInfo.CanRead) { continue; }
                if (!propToSet.PropertyInfo.CanWrite) { continue; }
                propToSet.SetValue(
                    serverInfo,
                    propToGet.GetValue(serverSettings));
            }

            return serverInfo;
        }

        public void CreatePreviewWindow(GUIFrame frame)
        {
            frame.ClearChildren();

            var serverListScreen = GameMain.ServerListScreen;

            var title = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), frame.RectTransform), ServerName, font: GUIStyle.LargeFont)
            {
                ToolTip = ServerName,
                CanBeFocused = false
            };
            title.Text = ToolBox.LimitString(title.Text, title.Font, (int)(title.Rect.Width * 0.85f));

            bool isFavorite = serverListScreen.IsFavorite(this);

            static LocalizedString favoriteTickBoxToolTip(bool isFavorite)
                => TextManager.Get(isFavorite ? "RemoveFromFavorites" : "AddToFavorites");
            
            GUITickBox favoriteTickBox = new GUITickBox(new RectTransform(new Vector2(0.15f, 0.8f), title.RectTransform, Anchor.CenterRight), 
                "", null, "GUIServerListFavoriteTickBox")
            {
                UserData = this,
                Selected = isFavorite,
                ToolTip = favoriteTickBoxToolTip(isFavorite),
                OnSelected = tickbox =>
                {
                    ServerInfo info = (ServerInfo)tickbox.UserData;
                    if (tickbox.Selected)
                    {
                        GameMain.ServerListScreen.AddToFavoriteServers(info);
                    }
                    else
                    {
                        GameMain.ServerListScreen.RemoveFromFavoriteServers(info);
                    }
                    tickbox.ToolTip = favoriteTickBoxToolTip(tickbox.Selected);
                    return true;
                }
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), frame.RectTransform),
                TextManager.AddPunctuation(':', TextManager.Get("ServerListVersion"),
                    GameVersion == new Version(0, 0, 0, 0) ? TextManager.Get("Unknown") : GameVersion.ToString()))
            {
                CanBeFocused = false
            };

            PlayStyle playStyle = PlayStyle;
            Sprite? playStyleBannerSprite = GUIStyle.GetComponentStyle($"PlayStyleBanner.{playStyle}")?.GetSprite(GUIComponent.ComponentState.None);

            GUIComponent playStyleBanner;
            Color playStyleBannerColor;
            if (playStyleBannerSprite != null)
            {
                float playStyleBannerAspectRatio = (float)playStyleBannerSprite.SourceRect.Width / (float)playStyleBannerSprite.SourceRect.Height;
                playStyleBanner = new GUIImage(new RectTransform(new Vector2(1.0f, 1.0f / playStyleBannerAspectRatio), frame.RectTransform, scaleBasis: ScaleBasis.BothWidth),
                    playStyleBannerSprite, null, true);
                playStyleBannerColor = playStyleBannerSprite.SourceElement.GetAttributeColor("bannercolor", Color.Black);
            }
            else
            {
                playStyleBanner = new GUIFrame(new RectTransform((1.0f, 0.2f), frame.RectTransform), style: null)
                {
                    Color = Color.Black,
                    DisabledColor = Color.Black,
                    OutlineColor = Color.Black,
                    PressedColor = Color.Black,
                    SelectedColor = Color.Black,
                    HoverColor = Color.Black
                };
                playStyleBannerColor = Color.Black;
            }

            var playStyleName = new GUITextBlock(
                new RectTransform(new Vector2(0.15f, 0.0f), playStyleBanner.RectTransform)
                    { RelativeOffset = new Vector2(0.0f, 0.06f) },
                TextManager.AddPunctuation(':', TextManager.Get("serverplaystyle"),
                    TextManager.Get($"servertag.{playStyle}")), textColor: Color.White,
                font: GUIStyle.SmallFont, textAlignment: Alignment.Center,
                color: playStyleBannerColor, style: "GUISlopedHeader");
            playStyleName.RectTransform.NonScaledSize = (playStyleName.Font.MeasureString(playStyleName.Text) + new Vector2(20, 5) * GUI.Scale).ToPoint();
            playStyleName.RectTransform.IsFixedSize = true;

            var serverType = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), frame.RectTransform),
                Endpoint?.ServerTypeString ?? string.Empty,
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
            foreach (var tag in playStyleTags)
            {
                var playStyleIcon = GUIStyle.GetComponentStyle($"PlayStyleIcon.{tag}")
                    ?.GetSprite(GUIComponent.ComponentState.None);
                if (playStyleIcon is null) { continue; }

                new GUIImage(new RectTransform(Vector2.One, playStyleContainer.RectTransform),
                    playStyleIcon, scaleToFit: true)
                {
                    ToolTip = TextManager.Get($"servertagdescription.{tag}"),
                    Color = Color.White
                };
            }

            playStyleContainer.Recalculate();

            // -----------------------------------------------------------------------------

            const float elementHeight = 0.075f;

            // Spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.025f), content.RectTransform), style: null);

            var serverMsg = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.3f), content.RectTransform)) { ScrollBarVisible = true };
            var msgText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), serverMsg.Content.RectTransform), ServerMessage ?? string.Empty, font: GUIStyle.SmallFont, wrap: true) 
            { 
                CanBeFocused = false 
            };
            serverMsg.Content.RectTransform.SizeChanged += () => { msgText.CalculateHeightFromText(); };
            msgText.RectTransform.SizeChanged += () => { serverMsg.UpdateScrollBarSize(); };

            var languageLabel = new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), content.RectTransform), TextManager.Get("Language"));
            new GUITextBlock(new RectTransform(Vector2.One, languageLabel.RectTransform),
                ServerLanguageOptions.Options.FirstOrNull(o => o.Identifier == Language)?.Label ?? TextManager.Get("Unknown"),
                textAlignment: Alignment.Right);

            var gameMode = new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), content.RectTransform), TextManager.Get("GameMode"));
            new GUITextBlock(new RectTransform(Vector2.One, gameMode.RectTransform),
                TextManager.Get(GameMode.IsEmpty ? "Unknown" : "GameMode." + GameMode).Fallback(GameMode.Value),
                textAlignment: Alignment.Right);

            GUITextBlock playStyleText = new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), content.RectTransform), TextManager.Get("serverplaystyle"));
            new GUITextBlock(new RectTransform(Vector2.One, playStyleText.RectTransform), TextManager.Get("servertag." + playStyle), textAlignment: Alignment.Right);

            var subSelection = new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), content.RectTransform), TextManager.Get("ServerListSubSelection"));
            new GUITextBlock(new RectTransform(Vector2.One, subSelection.RectTransform), TextManager.Get(SubSelectionMode.ToString()), textAlignment: Alignment.Right);

            var modeSelection = new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), content.RectTransform), TextManager.Get("ServerListModeSelection"));
            new GUITextBlock(new RectTransform(Vector2.One, modeSelection.RectTransform), TextManager.Get(ModeSelectionMode.ToString()), textAlignment: Alignment.Right);

            if (gameMode.TextSize.X + gameMode.GetChild<GUITextBlock>().TextSize.X > gameMode.Rect.Width ||
                subSelection.TextSize.X + subSelection.GetChild<GUITextBlock>().TextSize.X > subSelection.Rect.Width ||
                modeSelection.TextSize.X + modeSelection.GetChild<GUITextBlock>().TextSize.X > modeSelection.Rect.Width)
            {
                gameMode.Font = subSelection.Font = modeSelection.Font = GUIStyle.SmallFont;
                gameMode.GetChild<GUITextBlock>().Font = subSelection.GetChild<GUITextBlock>().Font = modeSelection.GetChild<GUITextBlock>().Font = GUIStyle.SmallFont;
                playStyleText.Font = playStyleText.GetChild<GUITextBlock>().Font = GUIStyle.SmallFont;
            }

            var allowSpectating = new GUITickBox(new RectTransform(new Vector2(1, elementHeight), content.RectTransform), TextManager.Get("ServerListAllowSpectating"))
            {
                CanBeFocused = false
            };
            allowSpectating.Selected = AllowSpectating;

            var allowRespawn = new GUITickBox(new RectTransform(new Vector2(1, elementHeight), content.RectTransform), TextManager.Get("ServerSettingsAllowRespawning"))
            {
                CanBeFocused = false
            };
            allowRespawn.Selected = AllowRespawn;

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform),
                TextManager.Get("ServerListContentPackages"), textAlignment: Alignment.Center, font: GUIStyle.SubHeadingFont);

            var contentPackageList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.3f), frame.RectTransform))
            {
                ScrollBarVisible = true,
                OnSelected = (component, o) => false
            };
            if (ContentPackages.Length == 0)
            {
                new GUITextBlock(new RectTransform(Vector2.One, contentPackageList.Content.RectTransform), TextManager.Get("Unknown"), textAlignment: Alignment.Center)
                {
                    CanBeFocused = false
                };
            }
            else
            {
                foreach (var package in ContentPackages)
                {
                    var packageText = new GUITickBox(
                        new RectTransform(new Vector2(1.0f, 0.15f), contentPackageList.Content.RectTransform)
                            { MinSize = new Point(0, 15) },
                        package.Name)
                    {
                        CanBeFocused = false
                    };
                    if (!string.IsNullOrEmpty(package.Hash))
                    {
                        if (ContentPackageManager.AllPackages.Any(contentPackage => contentPackage.Hash.StringRepresentation == package.Hash))
                        {
                            packageText.TextColor = GUIStyle.Green;
                            packageText.Selected = true;
                        }
                        //workshop download link found
                        else if (package.Id.TryUnwrap(out var ugcId) && ugcId is SteamWorkshopId)
                        {
                            packageText.ToolTip = TextManager.GetWithVariable("ServerListIncompatibleContentPackageWorkshopAvailable", "[contentpackage]", package.Name);
                        }
                        else //no package or workshop download link found
                        {
                            packageText.TextColor = GameMain.VanillaContent.NameMatches(package.Name) ? GUIStyle.Red : GUIStyle.Yellow;
                            packageText.ToolTip = TextManager.GetWithVariables("ServerListIncompatibleContentPackage",
                                ("[contentpackage]", package.Name), ("[hash]", package.Hash));
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

        public IEnumerable<Identifier> GetPlayStyleTags()
        {
            yield return $"Karma.{KarmaEnabled}".ToIdentifier();
            yield return (TraitorsEnabled == YesNoMaybe.Yes ? $"Traitors.True" : $"Traitors.False").ToIdentifier();
            yield return $"VoIP.{VoipEnabled}".ToIdentifier();
            yield return $"FriendlyFire.{FriendlyFireEnabled}".ToIdentifier();
            yield return $"Modded.{ContentPackages.Any()}".ToIdentifier();
        }

        public void UpdateInfo(Func<string, string?> valueGetter)
        {
            ServerMessage = valueGetter("message") ?? "";
            if (Version.TryParse(valueGetter("version"), out var version))
            {
                GameVersion = version;
            }
            if (int.TryParse(valueGetter("playercount"), out int playerCount)) { PlayerCount = playerCount; }
            if (int.TryParse(valueGetter("maxplayernum"), out int maxPlayers)) { MaxPlayers = maxPlayers; }
            if (Enum.TryParse(valueGetter("modeselectionmode"), out SelectionMode modeSelectionMode)) { ModeSelectionMode = modeSelectionMode; }
            if (Enum.TryParse(valueGetter("subselectionmode"), out SelectionMode subSelectionMode)) { SubSelectionMode = subSelectionMode; }

            HasPassword = getBool("haspassword");
            GameStarted = getBool("gamestarted");
            KarmaEnabled = getBool("karmaenabled");
            FriendlyFireEnabled = getBool("friendlyfireenabled");
            AllowSpectating = getBool("allowspectating");
            AllowRespawn = getBool("allowrespawn");
            VoipEnabled = getBool("voicechatenabled");

            GameMode = valueGetter("gamemode")?.ToIdentifier() ?? Identifier.Empty;
            if (Enum.TryParse(valueGetter("traitors"), out YesNoMaybe traitorsEnabled)) { TraitorsEnabled = traitorsEnabled; }
            if (Enum.TryParse(valueGetter("playstyle"), out PlayStyle playStyle)) { PlayStyle = playStyle; }
            Language = valueGetter("language")?.ToLanguageIdentifier() ?? LanguageIdentifier.None;

            ContentPackages = ExtractContentPackageInfo(valueGetter).ToImmutableArray();
            
            bool getBool(string key)
            {
                string? data = valueGetter(key);
                return bool.TryParse(data, out var result) && result;
            }
        }

        private static ContentPackageInfo[] ExtractContentPackageInfo(Func<string, string?> valueGetter)
        {
            string? joinedNames = valueGetter("contentpackage");
            string? joinedHashes = valueGetter("contentpackagehash");
            string? joinedWorkshopIds = valueGetter("contentpackageid");
            
            string[] contentPackageNames = joinedNames.IsNullOrEmpty() ? Array.Empty<string>() : joinedNames.Split(',');
            string[] contentPackageHashes = joinedHashes.IsNullOrEmpty() ? Array.Empty<string>() : joinedHashes.Split(',');
            #warning TODO: genericize
            ulong[] contentPackageIds = joinedWorkshopIds.IsNullOrEmpty() ? new ulong[1] : SteamManager.ParseWorkshopIds(joinedWorkshopIds).ToArray();

            if (contentPackageNames.Length != contentPackageHashes.Length
                || contentPackageHashes.Length != contentPackageIds.Length)
            {
                return Array.Empty<ContentPackageInfo>();
            }

            return contentPackageNames
                .Zip(contentPackageHashes, (name, hash) => (name, hash))
                .Zip(contentPackageIds, (t1, id) =>
                    new ContentPackageInfo(
                        t1.name,
                        t1.hash,
                        Option<ContentPackageId>.Some(new SteamWorkshopId(id))))
                .ToArray();
        }

        public static Option<ServerInfo> FromXElement(XElement element)
        {
            string endpointStr
                = element.GetAttributeString("Endpoint", null)
                  ?? element.GetAttributeString("OwnerID", null)
                  ?? $"{element.GetAttributeString("IP", "")}:{element.GetAttributeInt("Port", 0)}";
            
            if (!Endpoint.Parse(endpointStr).TryUnwrap(out var endpoint)) { return Option<ServerInfo>.None(); }

            var gameVersionStr = element.GetAttributeString("GameVersion", "");
            if (!Version.TryParse(gameVersionStr, out var gameVersion)) { gameVersion = GameMain.Version; }
            var info = new ServerInfo(endpoint)
            {
                GameVersion = gameVersion
            };
            SerializableProperty.DeserializeProperties(info, element);

            info.MetadataSource = DataSource.Parse(element);
            
            return Option<ServerInfo>.Some(info);
        }

        public XElement ToXElement()
        {
            XElement element = new XElement(GetType().Name);

            element.SetAttributeValue("Endpoint", Endpoint.ToString());
            element.SetAttributeValue("GameVersion", GameVersion.ToString());

            SerializableProperty.SerializeProperties(this, element, saveIfDefault: true);

            if (MetadataSource.TryUnwrap(out var dataSource))
            {
                dataSource.Write(element);
            }

            return element;
        }

        public override bool Equals(object? obj)
        {
            return obj is ServerInfo other && Equals(other);
        }

        public bool Equals(ServerInfo other)
            => other.Endpoint == Endpoint;

        public override int GetHashCode() => Endpoint.GetHashCode();

        string ISerializableEntity.Name => "ServerInfo";
        public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; }
    }
}
