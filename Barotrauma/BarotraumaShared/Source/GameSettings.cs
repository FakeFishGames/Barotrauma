using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System.Xml;
using System.IO;
using Barotrauma.Extensions;
#if CLIENT
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Tutorials;
#endif
using System;

namespace Barotrauma
{
    public enum WindowMode
    {
        Windowed, Fullscreen, BorderlessWindowed
    }

    public enum LosMode
    {
        None,
        Transparent,
        Opaque
    }

    public partial class GameSettings
    {    
        const string savePath = "config.xml";
        const string playerSavePath = "config_player.xml";
        const string vanillaContentPackagePath = "Data/ContentPackages/Vanilla";

        public int GraphicsWidth { get; set; }
        public int GraphicsHeight { get; set; }

        public bool VSyncEnabled { get; set; }

        public bool EnableSplashScreen { get; set; }

        public int ParticleLimit { get; set; }

        public float LightMapScale { get; set; }
        public bool SpecularityEnabled { get; set; }
        public bool ChromaticAberrationEnabled { get; set; }

        public bool PauseOnFocusLost { get; set; }
        public bool MuteOnFocusLost { get; set; }
        public bool DynamicRangeCompressionEnabled { get; set; }
        public bool VoipAttenuationEnabled { get; set; }
        public bool UseDirectionalVoiceChat { get; set; }

        public enum VoiceMode
        {
            Disabled,
            PushToTalk,
            Activity
        };

        public VoiceMode VoiceSetting { get; set; }
        public string VoiceCaptureDevice { get; set; }

        public float NoiseGateThreshold { get; set; }

        private KeyOrMouse[] keyMapping;

        private WindowMode windowMode;

        private LosMode losMode;

        public List<string> jobPreferences;

        private bool useSteamMatchmaking;
        private bool requireSteamAuthentication;
        public string QuickStartSubmarineName;

#if DEBUG
        //steam functionality can be enabled/disabled in debug builds
        public bool RequireSteamAuthentication
        {
            get { return requireSteamAuthentication && Steam.SteamManager.USE_STEAM; }
            set { requireSteamAuthentication = value; }
        }
        public bool UseSteamMatchmaking
        {
            get { return useSteamMatchmaking && Steam.SteamManager.USE_STEAM; }
            set { useSteamMatchmaking = value; }
        }
#else
        public bool RequireSteamAuthentication
        {
            get { return requireSteamAuthentication && Steam.SteamManager.USE_STEAM; }
            set { requireSteamAuthentication = value; }
        }
        public bool UseSteamMatchmaking
        {
            get { return useSteamMatchmaking && Steam.SteamManager.USE_STEAM; }
            set { useSteamMatchmaking = value; }
        }
#endif

        public bool AutoUpdateWorkshopItems;

        public WindowMode WindowMode
        {
            get { return windowMode; }
            set
            {
#if (OSX)
                // Fullscreen doesn't work on macOS, so just force any usage of it to borderless windowed.
                if (value == WindowMode.Fullscreen)
                {
                    windowMode = WindowMode.BorderlessWindowed;
                    return;
                }
#endif
                windowMode = value;
            }
        }

        public List<string> JobPreferences
        {
            get { return jobPreferences; }
            set { jobPreferences = value; }
        }

        public int CharacterHeadIndex { get; set; }
        public int CharacterHairIndex { get; set; }
        public int CharacterBeardIndex { get; set; }
        public int CharacterMoustacheIndex { get; set; }
        public int CharacterFaceAttachmentIndex { get; set; }

        public Gender CharacterGender { get; set; }
        public Race CharacterRace { get; set; }

        private float aimAssistAmount;
        public float AimAssistAmount
        {
            get { return aimAssistAmount; }
            set { aimAssistAmount = MathHelper.Clamp(value, 0.0f, 5.0f); }
        }

        public bool EnableMouseLook { get; set; } = true;

        public bool CrewMenuOpen { get; set; } = true;
        public bool ChatOpen { get; set; } = true;

        private string overrideSaveFolder, overrideMultiplayerSaveFolder;

        private bool unsavedSettings;
        public bool UnsavedSettings
        {
            get
            {
                return unsavedSettings;
            }
            private set
            {
                unsavedSettings = value;
#if CLIENT
                if (applyButton != null)
                {
                    applyButton.Enabled = unsavedSettings;
                    applyButton.Text = TextManager.Get(unsavedSettings ? "ApplySettingsButtonUnsavedChanges" : "ApplySettingsButton");
                }
#endif
            }
        }

        private float soundVolume, musicVolume, voiceChatVolume, microphoneVolume;

        public float SoundVolume
        {
            get { return soundVolume; }
            set
            {
                soundVolume = MathHelper.Clamp(value, 0.0f, 1.0f);
#if CLIENT
                if (GameMain.SoundManager != null)
                {
                    GameMain.SoundManager.SetCategoryGainMultiplier("default", soundVolume, 0);
                    GameMain.SoundManager.SetCategoryGainMultiplier("ui", soundVolume, 0);
                    GameMain.SoundManager.SetCategoryGainMultiplier("waterambience", soundVolume, 0);
                }
#endif
            }
        }

        public float MusicVolume
        {
            get { return musicVolume; }
            set
            {
                musicVolume = MathHelper.Clamp(value, 0.0f, 1.0f);
#if CLIENT
                GameMain.SoundManager?.SetCategoryGainMultiplier("music", musicVolume, 0);
#endif
            }
        }

        public float VoiceChatVolume
        {
            get { return voiceChatVolume; }
            set
            {
                voiceChatVolume = MathHelper.Clamp(value, 0.0f, 1.0f);
#if CLIENT
                GameMain.SoundManager?.SetCategoryGainMultiplier("voip", voiceChatVolume * 30.0f, 0);
#endif
            }
        }

        public float MicrophoneVolume
        {
            get { return microphoneVolume; }
            set
            {
                microphoneVolume = MathHelper.Clamp(value, 0.2f, 10.0f);
            }
        }
        public string Language
        {
            get { return TextManager.Language; }
            set { TextManager.Language = value; }
        }

        public readonly List<ContentPackage> SelectedContentPackages = new List<ContentPackage>();

        public void SelectContentPackage(ContentPackage contentPackage)
        {
            if (!SelectedContentPackages.Contains(contentPackage))
            {
                SelectedContentPackages.Add(contentPackage);
                ContentPackage.SortContentPackages();
            }
        }

        public void DeselectContentPackage(ContentPackage contentPackage)
        {
            if (SelectedContentPackages.Contains(contentPackage))
            {
                SelectedContentPackages.Remove(contentPackage);
                ContentPackage.SortContentPackages();
            }
        }

        private HashSet<string> selectedContentPackagePaths = new HashSet<string>();

        public string MasterServerUrl { get; set; }
        public bool AutoCheckUpdates { get; set; }
        public bool WasGameUpdated { get; set; }

        private string playerName;
        public string PlayerName
        {
            get
            {
                return string.IsNullOrWhiteSpace(playerName) ? Steam.SteamManager.GetUsername() : playerName;
            }
            set
            {
                if (playerName != value)
                {
                    playerName = value;
                }
            }
        }

        public LosMode LosMode
        {
            get { return losMode; }
            set { losMode = value; }
        }

        private const float MinHUDScale = 0.75f, MaxHUDScale = 1.25f;
        public static float HUDScale { get; set; }
        private const float MinInventoryScale = 0.75f, MaxInventoryScale = 1.25f;
        public static float InventoryScale { get; set; }

        public List<string> CompletedTutorialNames { get; private set; }

        public static bool VerboseLogging { get; set; }
        public static bool SaveDebugConsoleLogs { get; set; }

        public bool CampaignDisclaimerShown, EditorDisclaimerShown;

        private static bool sendUserStatistics = true;
        public static bool SendUserStatistics
        {
            get
            {
#if DEBUG
                return false;
#endif
                return sendUserStatistics;
            }
            set
            {
                sendUserStatistics = value;
                GameMain.Config.SaveNewPlayerConfig();
            }
        }
        public static bool ShowUserStatisticsPrompt { get; set; }

        public bool ShowLanguageSelectionPrompt { get; set; }

        public GameSettings()
        {
            ContentPackage.LoadAll();
            CompletedTutorialNames = new List<string>();

            LoadDefaultConfig();

            if (WasGameUpdated)
            {
                UpdaterUtil.CleanOldFiles();
                WasGameUpdated = false;
                SaveNewDefaultConfig();
            }

            LoadPlayerConfig();
        }

        public void SetDefaultBindings(XDocument doc = null, bool legacy = false)
        {
            keyMapping = new KeyOrMouse[Enum.GetNames(typeof(InputType)).Length];
            keyMapping[(int)InputType.Run] = new KeyOrMouse(Keys.LeftShift);
            keyMapping[(int)InputType.Attack] = new KeyOrMouse(2);
            keyMapping[(int)InputType.Crouch] = new KeyOrMouse(Keys.LeftControl);
            keyMapping[(int)InputType.Grab] = new KeyOrMouse(Keys.G);
            keyMapping[(int)InputType.Health] = new KeyOrMouse(Keys.H);
            keyMapping[(int)InputType.Ragdoll] = new KeyOrMouse(Keys.Space);
            keyMapping[(int)InputType.Aim] = new KeyOrMouse(1);

            keyMapping[(int)InputType.InfoTab] = new KeyOrMouse(Keys.Tab);
            keyMapping[(int)InputType.Chat] = new KeyOrMouse(Keys.T);
            keyMapping[(int)InputType.RadioChat] = new KeyOrMouse(Keys.R);
            keyMapping[(int)InputType.CrewOrders] = new KeyOrMouse(Keys.C);

            keyMapping[(int)InputType.Voice] = new KeyOrMouse(Keys.V);

            if (Language == "French")
            {
                keyMapping[(int)InputType.Up] = new KeyOrMouse(Keys.Z);
                keyMapping[(int)InputType.Down] = new KeyOrMouse(Keys.S);
                keyMapping[(int)InputType.Left] = new KeyOrMouse(Keys.Q);
                keyMapping[(int)InputType.Right] = new KeyOrMouse(Keys.D);

                keyMapping[(int)InputType.SelectNextCharacter] = new KeyOrMouse(Keys.X);
                keyMapping[(int)InputType.SelectPreviousCharacter] = new KeyOrMouse(Keys.W);
            }
            else
            {
                keyMapping[(int)InputType.Up] = new KeyOrMouse(Keys.W);
                keyMapping[(int)InputType.Down] = new KeyOrMouse(Keys.S);
                keyMapping[(int)InputType.Left] = new KeyOrMouse(Keys.A);
                keyMapping[(int)InputType.Right] = new KeyOrMouse(Keys.D);

                keyMapping[(int)InputType.SelectNextCharacter] = new KeyOrMouse(Keys.Z);
                keyMapping[(int)InputType.SelectPreviousCharacter] = new KeyOrMouse(Keys.X);
            }

            if (legacy)
            {
                keyMapping[(int)InputType.Use] = new KeyOrMouse(0);
                keyMapping[(int)InputType.Shoot] = new KeyOrMouse(0);
                keyMapping[(int)InputType.Select] = new KeyOrMouse(Keys.E);
                keyMapping[(int)InputType.Deselect] = new KeyOrMouse(Keys.E);
            }
            else
            {
                keyMapping[(int)InputType.Use] = new KeyOrMouse(Keys.E);
                keyMapping[(int)InputType.Select] = new KeyOrMouse(0);
                // shoot and deselect are handled in CheckBindings() so that we don't override the legacy settings.
            }
            if (doc != null)
            {
                LoadControls(doc);
            }
        }

        public void CheckBindings(bool useDefaults)
        {
            foreach (InputType inputType in Enum.GetValues(typeof(InputType)))
            {
                var binding = keyMapping[(int)inputType];
                if (binding == null)
                {
                    switch (inputType)
                    {
                        case InputType.Deselect:
                            if (useDefaults)
                            {
                                binding = new KeyOrMouse(1);
                            }
                            else
                            {
                                // Legacy support
                                var selectKey = keyMapping[(int)InputType.Select];
                                if (selectKey != null && selectKey.Key != Keys.None)
                                {
                                    binding = new KeyOrMouse(selectKey.Key);
                                }
                            }
                            break;
                        case InputType.Shoot:
                            if (useDefaults)
                            {
                                binding = new KeyOrMouse(0);
                            }
                            else
                            {
                                // Legacy support
                                var useKey = keyMapping[(int)InputType.Use];
                                if (useKey != null && useKey.MouseButton.HasValue)
                                {
                                    binding = new KeyOrMouse(useKey.MouseButton.Value);
                                }
                            }
                            break;
                        default:
                            break;
                    }
                    if (binding == null)
                    {
                        DebugConsole.ThrowError("Key binding for the input type \"" + inputType + " not set!");
                        binding = new KeyOrMouse(Keys.D1);
                    }
                    keyMapping[(int)inputType] = binding;
                }
            }
        }

        private void LoadDefaultConfig(bool setLanguage = true)
        {
            XDocument doc = XMLExtensions.TryLoadXml(savePath);
            if (doc == null)
            {
                GraphicsWidth = 1024;
                GraphicsHeight = 768;
                MasterServerUrl = "";
                SelectContentPackage(ContentPackage.List.Any() ? ContentPackage.List[0] : new ContentPackage(""));
                jobPreferences = new List<string>();
                foreach (string job in JobPrefab.List.Keys)
                {
                    jobPreferences.Add(job);
                }
                return;
            }

            bool resetLanguage = setLanguage || string.IsNullOrEmpty(Language);
            SetDefaultValues(resetLanguage);
            SetDefaultBindings(doc, legacy: false);

            MasterServerUrl = doc.Root.GetAttributeString("masterserverurl", MasterServerUrl);
            WasGameUpdated = doc.Root.GetAttributeBool("wasgameupdated", WasGameUpdated);
            VerboseLogging = doc.Root.GetAttributeBool("verboselogging", VerboseLogging);
            SaveDebugConsoleLogs = doc.Root.GetAttributeBool("savedebugconsolelogs", SaveDebugConsoleLogs);
            AutoUpdateWorkshopItems = doc.Root.GetAttributeBool("autoupdateworkshopitems", AutoUpdateWorkshopItems);

            LoadGeneralSettings(doc, resetLanguage);
            LoadGraphicSettings(doc);
            LoadAudioSettings(doc);
            LoadControls(doc);
            LoadContentPackages(doc);

#if DEBUG
            WindowMode = WindowMode.Windowed;
#endif

            UnsavedSettings = false;
        }

        private void SaveNewDefaultConfig()
        {
            XDocument doc = new XDocument();

            if (doc.Root == null)
            {
                doc.Add(new XElement("config"));
            }

            doc.Root.Add(
                new XAttribute("language", TextManager.Language),
                new XAttribute("masterserverurl", MasterServerUrl),
                new XAttribute("autocheckupdates", AutoCheckUpdates),
                new XAttribute("musicvolume", musicVolume),
                new XAttribute("soundvolume", soundVolume),
                new XAttribute("microphonevolume", microphoneVolume),
                new XAttribute("voicechatvolume", voiceChatVolume),
                new XAttribute("verboselogging", VerboseLogging),
                new XAttribute("savedebugconsolelogs", SaveDebugConsoleLogs),
                new XAttribute("enablesplashscreen", EnableSplashScreen),
                new XAttribute("usesteammatchmaking", useSteamMatchmaking),
                new XAttribute("quickstartsub", QuickStartSubmarineName),
                new XAttribute("requiresteamauthentication", requireSteamAuthentication),
                new XAttribute("aimassistamount", aimAssistAmount));

            if (!ShowUserStatisticsPrompt)
            {
                doc.Root.Add(new XAttribute("senduserstatistics", sendUserStatistics));
            }

            if (WasGameUpdated)
            {
                doc.Root.Add(new XAttribute("wasgameupdated", true));
            }

            XElement gMode = doc.Root.Element("graphicsmode");
            if (gMode == null)
            {
                gMode = new XElement("graphicsmode");
                doc.Root.Add(gMode);
            }
            if (GraphicsWidth == 0 || GraphicsHeight == 0)
            {
                gMode.ReplaceAttributes(new XAttribute("displaymode", windowMode));
            }
            else
            {
                gMode.ReplaceAttributes(
                    new XAttribute("width", GraphicsWidth),
                    new XAttribute("height", GraphicsHeight),
                    new XAttribute("vsync", VSyncEnabled),
                    new XAttribute("displaymode", windowMode));
            }

            XElement gSettings = doc.Root.Element("graphicssettings");
            if (gSettings == null)
            {
                gSettings = new XElement("graphicssettings");
                doc.Root.Add(gSettings);
            }

            gSettings.ReplaceAttributes(
                new XAttribute("particlelimit", ParticleLimit),
                new XAttribute("lightmapscale", LightMapScale),
                new XAttribute("specularity", SpecularityEnabled),
                new XAttribute("chromaticaberration", ChromaticAberrationEnabled),
                new XAttribute("losmode", LosMode),
                new XAttribute("hudscale", HUDScale),
                new XAttribute("inventoryscale", InventoryScale));

            foreach (ContentPackage contentPackage in SelectedContentPackages)
            {
                if (contentPackage.Path.Contains(vanillaContentPackagePath))
                {
                    doc.Root.Add(new XElement("contentpackage", new XAttribute("path", contentPackage.Path)));
                    break;
                }
            }

            var keyMappingElement = new XElement("keymapping");
            doc.Root.Add(keyMappingElement);
            for (int i = 0; i < keyMapping.Length; i++)
            {
                if (keyMapping[i].MouseButton == null)
                {
                    keyMappingElement.Add(new XAttribute(((InputType)i).ToString(), keyMapping[i].Key));
                }
                else
                {
                    keyMappingElement.Add(new XAttribute(((InputType)i).ToString(), keyMapping[i].MouseButton));
                }
            }

            var gameplay = new XElement("gameplay");
            var jobPreferences = new XElement("jobpreferences");
            foreach (string jobName in JobPreferences)
            {
                jobPreferences.Add(new XElement("job", new XAttribute("identifier", jobName)));
            }
            gameplay.Add(jobPreferences);
            doc.Root.Add(gameplay);

            var playerElement = new XElement("player",
                new XAttribute("name", playerName ?? ""),
                new XAttribute("headindex", CharacterHeadIndex),
                new XAttribute("gender", CharacterGender),
                new XAttribute("race", CharacterRace),
                new XAttribute("hairindex", CharacterHairIndex),
                new XAttribute("beardindex", CharacterBeardIndex),
                new XAttribute("moustacheindex", CharacterMoustacheIndex),
                new XAttribute("faceattachmentindex", CharacterFaceAttachmentIndex));
            doc.Root.Add(playerElement);

            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = true,
                NewLineOnAttributes = true
            };

            try
            {
                using (var writer = XmlWriter.Create(savePath, settings))
                {
                    doc.WriteTo(writer);
                    writer.Flush();
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Saving game settings failed.", e);
                GameAnalyticsManager.AddErrorEventOnce("GameSettings.Save:SaveFailed", GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                    "Saving game settings failed.\n" + e.Message + "\n" + e.StackTrace);
            }
        }

        #region Load PlayerConfig
        public void LoadPlayerConfig()
        {
            bool fileFound = LoadPlayerConfigInternal();
            CheckBindings(!fileFound);
            if (!fileFound)
            {
                ShowLanguageSelectionPrompt = true;
                ShowUserStatisticsPrompt = true;
                SaveNewPlayerConfig();
            }
        }

        // TODO: DRY
        /// <summary>
        /// Returns false if no player config file was found, in which case a new file is created.
        /// </summary>
        private bool LoadPlayerConfigInternal()
        {
            XDocument doc = XMLExtensions.LoadXml(playerSavePath);
            if (doc == null || doc.Root == null)
            {
                ShowUserStatisticsPrompt = true;
                return false;
            }
            LoadGeneralSettings(doc);
            LoadGraphicSettings(doc);
            LoadAudioSettings(doc);
            LoadControls(doc);
            LoadContentPackages(doc);

            //allow overriding the save paths in the config file
            if (doc.Root.Attribute("overridesavefolder") != null)
            {
                overrideSaveFolder = SaveUtil.SaveFolder = doc.Root.GetAttributeString("overridesavefolder", "");
                overrideMultiplayerSaveFolder = SaveUtil.MultiplayerSaveFolder = Path.Combine(overrideSaveFolder, "Multiplayer");
            }
            if (doc.Root.Attribute("overridemultiplayersavefolder") != null)
            {
                overrideMultiplayerSaveFolder = SaveUtil.MultiplayerSaveFolder = doc.Root.GetAttributeString("overridemultiplayersavefolder", "");
            }

            XElement tutorialsElement = doc.Root.Element("tutorials");
            if (tutorialsElement != null)
            {
                foreach (XElement element in tutorialsElement.Elements())
                {
                    CompletedTutorialNames.Add(element.GetAttributeString("name", ""));
                }
            }

            UnsavedSettings = false;
            return true;
        }

        public void ReloadContentPackages()
        {
            LoadContentPackages(selectedContentPackagePaths);
        }

        private void LoadContentPackages(IEnumerable<string> contentPackagePaths)
        {
            var missingPackagePaths = new List<string>();
            var incompatiblePackages = new List<ContentPackage>();
            var invalidPackages = new List<ContentPackage>();
            SelectedContentPackages.Clear();
            foreach (string path in contentPackagePaths)
            {
                var matchingContentPackage = ContentPackage.List.Find(cp => System.IO.Path.GetFullPath(cp.Path) == path);

                if (matchingContentPackage == null)
                {
                    missingPackagePaths.Add(path);
                }
                else if (!matchingContentPackage.IsCompatible())
                {
                    incompatiblePackages.Add(matchingContentPackage);
                }
                else if (!matchingContentPackage.CheckValidity(out List<string> errorMessages))
                {
                    invalidPackages.Add(matchingContentPackage);
                }
                else
                {
                    SelectedContentPackages.Add(matchingContentPackage);
                }
            }

            ContentPackage.SortContentPackages();
            TextManager.LoadTextPacks(SelectedContentPackages);

            foreach (ContentPackage contentPackage in SelectedContentPackages)
            {
                foreach (ContentFile file in contentPackage.Files)
                {
                    ToolBox.IsProperFilenameCase(file.Path);
                }
            }

            EnsureCoreContentPackageSelected();

            //save to get rid of the invalid selected packages in the config file
            if (missingPackagePaths.Count > 0 || incompatiblePackages.Count > 0 || invalidPackages.Count > 0) { SaveNewPlayerConfig(); }

            //display error messages after all content packages have been loaded
            //to make sure the package that contains text files has been loaded before we attempt to use TextManager
            foreach (string missingPackagePath in missingPackagePaths)
            {
                DebugConsole.ThrowError(TextManager.GetWithVariable("ContentPackageNotFound", "[packagepath]", missingPackagePath));
            }
            foreach (ContentPackage invalidPackage in invalidPackages)
            {
                DebugConsole.ThrowError(TextManager.GetWithVariable("InvalidContentPackage", "[packagename]", invalidPackage.Name), createMessageBox: true);
            }
            foreach (ContentPackage incompatiblePackage in incompatiblePackages)
            {
                DebugConsole.ThrowError(TextManager.GetWithVariables(incompatiblePackage.GameVersion <= new Version(0, 0, 0, 0) ? "IncompatibleContentPackageUnknownVersion" : "IncompatibleContentPackage",
                    new string[3] { "[packagename]", "[packageversion]", "[gameversion]" }, new string[3] { incompatiblePackage.Name, incompatiblePackage.GameVersion.ToString(), GameMain.Version.ToString() }),
                    createMessageBox: true);
            }
        }

        public void EnsureCoreContentPackageSelected()
        {
            if (SelectedContentPackages.Any(cp => cp.CorePackage)) { return; }

            if (GameMain.VanillaContent != null)
            {
                SelectContentPackage(GameMain.VanillaContent);
            }
            else
            {
                var availablePackage = ContentPackage.List.FirstOrDefault(cp => cp.IsCompatible() && cp.CorePackage);
                if (availablePackage != null)
                {
                    SelectContentPackage(availablePackage);
                }
            }
        }

        #endregion

        #region Save PlayerConfig
        public void SaveNewPlayerConfig()
        {
            XDocument doc = new XDocument();
            UnsavedSettings = false;

            if (doc.Root == null)
            {
                doc.Add(new XElement("config"));
            }

            doc.Root.Add(
                new XAttribute("language", TextManager.Language),
                new XAttribute("masterserverurl", MasterServerUrl),
                new XAttribute("autocheckupdates", AutoCheckUpdates),
                new XAttribute("musicvolume", musicVolume),
                new XAttribute("soundvolume", soundVolume),
                new XAttribute("verboselogging", VerboseLogging),
                new XAttribute("savedebugconsolelogs", SaveDebugConsoleLogs),
                new XAttribute("enablesplashscreen", EnableSplashScreen),
                new XAttribute("usesteammatchmaking", useSteamMatchmaking),
                new XAttribute("quickstartsub", QuickStartSubmarineName),
                new XAttribute("requiresteamauthentication", requireSteamAuthentication),
                new XAttribute("autoupdateworkshopitems", AutoUpdateWorkshopItems),
                new XAttribute("pauseonfocuslost", PauseOnFocusLost),
                new XAttribute("aimassistamount", aimAssistAmount),
                new XAttribute("enablemouselook", EnableMouseLook),
                new XAttribute("chatopen", ChatOpen),
                new XAttribute("crewmenuopen", CrewMenuOpen),
                new XAttribute("campaigndisclaimershown", CampaignDisclaimerShown),
                new XAttribute("editordisclaimershown", EditorDisclaimerShown));

            if (!string.IsNullOrEmpty(overrideSaveFolder))
            {
                doc.Root.Add(new XAttribute("overridesavefolder", overrideSaveFolder));
            }
            if (!string.IsNullOrEmpty(overrideMultiplayerSaveFolder))
            {
                doc.Root.Add(new XAttribute("overridemultiplayersavefolder", overrideMultiplayerSaveFolder));
            }

            if (!ShowUserStatisticsPrompt)
            {
                doc.Root.Add(new XAttribute("senduserstatistics", sendUserStatistics));
            }

            XElement gMode = doc.Root.Element("graphicsmode");
            if (gMode == null)
            {
                gMode = new XElement("graphicsmode");
                doc.Root.Add(gMode);
            }

            if (GraphicsWidth == 0 || GraphicsHeight == 0)
            {
                gMode.ReplaceAttributes(new XAttribute("displaymode", windowMode));
            }
            else
            {
                gMode.ReplaceAttributes(
                    new XAttribute("width", GraphicsWidth),
                    new XAttribute("height", GraphicsHeight),
                    new XAttribute("vsync", VSyncEnabled),
                    new XAttribute("displaymode", windowMode));
            }

            XElement audio = doc.Root.Element("audio");
            if (audio == null)
            {
                audio = new XElement("audio");
                doc.Root.Add(audio);
            }
            audio.ReplaceAttributes(
                new XAttribute("musicvolume", musicVolume),
                new XAttribute("soundvolume", soundVolume),
                new XAttribute("voicechatvolume", voiceChatVolume),
                new XAttribute("microphonevolume", microphoneVolume),
                new XAttribute("muteonfocuslost", MuteOnFocusLost),
                new XAttribute("dynamicrangecompressionenabled", DynamicRangeCompressionEnabled),
                new XAttribute("voipattenuationenabled", VoipAttenuationEnabled),
                new XAttribute("usedirectionalvoicechat", UseDirectionalVoiceChat),
                new XAttribute("voicesetting", VoiceSetting),
                new XAttribute("voicecapturedevice", VoiceCaptureDevice ?? ""),
                new XAttribute("noisegatethreshold", NoiseGateThreshold));

            XElement gSettings = doc.Root.Element("graphicssettings");
            if (gSettings == null)
            {
                gSettings = new XElement("graphicssettings");
                doc.Root.Add(gSettings);
            }

            gSettings.ReplaceAttributes(
                new XAttribute("particlelimit", ParticleLimit),
                new XAttribute("lightmapscale", LightMapScale),
                new XAttribute("specularity", SpecularityEnabled),
                new XAttribute("chromaticaberration", ChromaticAberrationEnabled),
                new XAttribute("losmode", LosMode),
                new XAttribute("hudscale", HUDScale),
                new XAttribute("inventoryscale", InventoryScale));

            foreach (ContentPackage contentPackage in SelectedContentPackages)
            {
                doc.Root.Add(new XElement("contentpackage",
                    new XAttribute("path", contentPackage.Path)));
            }

            var keyMappingElement = new XElement("keymapping");
            doc.Root.Add(keyMappingElement);
            for (int i = 0; i < keyMapping.Length; i++)
            {
                if (keyMapping[i].MouseButton == null)
                {
                    keyMappingElement.Add(new XAttribute(((InputType)i).ToString(), keyMapping[i].Key));
                }
                else
                {
                    keyMappingElement.Add(new XAttribute(((InputType)i).ToString(), keyMapping[i].MouseButton));
                }
            }

            var gameplay = new XElement("gameplay");
            var jobPreferences = new XElement("jobpreferences");
            foreach (string jobName in JobPreferences)
            {
                jobPreferences.Add(new XElement("job", new XAttribute("identifier", jobName)));
            }
            gameplay.Add(jobPreferences);
            doc.Root.Add(gameplay);

            var playerElement = new XElement("player",
                new XAttribute("name", playerName ?? ""),
                new XAttribute("headindex", CharacterHeadIndex),
                new XAttribute("gender", CharacterGender),
                new XAttribute("race", CharacterRace),
                new XAttribute("hairindex", CharacterHairIndex),
                new XAttribute("beardindex", CharacterBeardIndex),
                new XAttribute("moustacheindex", CharacterMoustacheIndex),
                new XAttribute("faceattachmentindex", CharacterFaceAttachmentIndex));
            doc.Root.Add(playerElement);

#if CLIENT
            if (Tutorial.Tutorials != null)
            {
                foreach (Tutorial tutorial in Tutorial.Tutorials)
                {
                    if (tutorial.Completed && !CompletedTutorialNames.Contains(tutorial.Identifier))
                    {
                        CompletedTutorialNames.Add(tutorial.Identifier);
                    }
                }
            }
#endif
            var tutorialElement = new XElement("tutorials");
            foreach (string tutorialName in CompletedTutorialNames)
            {
                tutorialElement.Add(new XElement("Tutorial", new XAttribute("name", tutorialName)));
            }
            doc.Root.Add(tutorialElement);

            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = true,
                NewLineOnAttributes = true
            };

            try
            {
                using (var writer = XmlWriter.Create(playerSavePath, settings))
                {
                    doc.WriteTo(writer);
                    writer.Flush();
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Saving game settings failed.", e);
                GameAnalyticsManager.AddErrorEventOnce("GameSettings.Save:SaveFailed", GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                    "Saving game settings failed.\n" + e.Message + "\n" + e.StackTrace);
            }
        }
        #endregion

        #region Loading Configs
        private void LoadGeneralSettings(XDocument doc, bool setLanguage = true)
        {
            if (setLanguage)
            {
                Language = doc.Root.GetAttributeString("language", Language);
            }
            AutoCheckUpdates = doc.Root.GetAttributeBool("autocheckupdates", AutoCheckUpdates);
            sendUserStatistics = doc.Root.GetAttributeBool("senduserstatistics", sendUserStatistics);
            QuickStartSubmarineName = doc.Root.GetAttributeString("quickstartsub", QuickStartSubmarineName);
            useSteamMatchmaking = doc.Root.GetAttributeBool("usesteammatchmaking", useSteamMatchmaking);
            requireSteamAuthentication = doc.Root.GetAttributeBool("requiresteamauthentication", requireSteamAuthentication);
            EnableSplashScreen = doc.Root.GetAttributeBool("enablesplashscreen", EnableSplashScreen);
            PauseOnFocusLost = doc.Root.GetAttributeBool("pauseonfocuslost", PauseOnFocusLost);
            AimAssistAmount = doc.Root.GetAttributeFloat("aimassistamount", AimAssistAmount);
            EnableMouseLook = doc.Root.GetAttributeBool("enablemouselook", EnableMouseLook);
            CrewMenuOpen = doc.Root.GetAttributeBool("crewmenuopen", CrewMenuOpen);
            ChatOpen = doc.Root.GetAttributeBool("chatopen", ChatOpen);
            CampaignDisclaimerShown = doc.Root.GetAttributeBool("campaigndisclaimershown", CampaignDisclaimerShown);
            EditorDisclaimerShown = doc.Root.GetAttributeBool("editordisclaimershown", EditorDisclaimerShown);
            XElement gameplayElement = doc.Root.Element("gameplay");
            if (gameplayElement != null)
            {
                jobPreferences = new List<string>();
                foreach (XElement ele in gameplayElement.Element("jobpreferences").Elements("job"))
                {
                    string jobIdentifier = ele.GetAttributeString("identifier", "");
                    if (string.IsNullOrEmpty(jobIdentifier)) continue;
                    jobPreferences.Add(jobIdentifier);
                }
            }

            XElement playerElement = doc.Root.Element("player");
            if (playerElement != null)
            {
                playerName = playerElement.GetAttributeString("name", playerName);
                CharacterHeadIndex = playerElement.GetAttributeInt("headindex", CharacterHeadIndex);
                if (Enum.TryParse(playerElement.GetAttributeString("gender", "none"), true, out Gender g))
                {
                    CharacterGender = g;
                }
                if (Enum.TryParse(playerElement.GetAttributeString("race", "white"), true, out Race r))
                {
                    CharacterRace = r;
                }
                else
                {
                    CharacterRace = Race.White;
                }
                CharacterHairIndex = playerElement.GetAttributeInt("hairindex", CharacterHairIndex);
                CharacterBeardIndex = playerElement.GetAttributeInt("beardindex", CharacterBeardIndex);
                CharacterMoustacheIndex = playerElement.GetAttributeInt("moustacheindex", CharacterMoustacheIndex);
                CharacterFaceAttachmentIndex = playerElement.GetAttributeInt("faceattachmentindex", CharacterFaceAttachmentIndex);
            }
        }

        private void LoadGraphicSettings(XDocument doc)
        {
            XElement graphicsMode = doc.Root.Element("graphicsmode");
            GraphicsWidth = graphicsMode.GetAttributeInt("width", GraphicsWidth);
            GraphicsHeight = graphicsMode.GetAttributeInt("height", GraphicsHeight);
            VSyncEnabled = graphicsMode.GetAttributeBool("vsync", VSyncEnabled);

            XElement graphicsSettings = doc.Root.Element("graphicssettings");
            ParticleLimit = graphicsSettings.GetAttributeInt("particlelimit", ParticleLimit);
            LightMapScale = MathHelper.Clamp(graphicsSettings.GetAttributeFloat("lightmapscale", LightMapScale), 0.1f, 1.0f);
            SpecularityEnabled = graphicsSettings.GetAttributeBool("specularity", SpecularityEnabled);
            ChromaticAberrationEnabled = graphicsSettings.GetAttributeBool("chromaticaberration", ChromaticAberrationEnabled);
            HUDScale = graphicsSettings.GetAttributeFloat("hudscale", HUDScale);
            InventoryScale = graphicsSettings.GetAttributeFloat("inventoryscale", InventoryScale);
            var losModeStr = graphicsSettings.GetAttributeString("losmode", "Transparent");
            if (!Enum.TryParse(losModeStr, out losMode))
            {
                losMode = LosMode.Transparent;
            }
#if CLIENT
            if (GraphicsWidth == 0 || GraphicsHeight == 0)
            {
                GraphicsWidth = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Width;
                GraphicsHeight = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Height;
            }
#endif
            var windowModeStr = graphicsMode.GetAttributeString("displaymode", "Fullscreen");
            if (!Enum.TryParse(windowModeStr, out windowMode))
            {
                windowMode = WindowMode.Fullscreen;
            }
        }

        private void LoadAudioSettings(XDocument doc)
        {
            XElement audioSettings = doc.Root.Element("audio");
            if (audioSettings != null)
            {
                SoundVolume = audioSettings.GetAttributeFloat("soundvolume", SoundVolume);
                MusicVolume = audioSettings.GetAttributeFloat("musicvolume", MusicVolume);
                DynamicRangeCompressionEnabled = audioSettings.GetAttributeBool("dynamicrangecompressionenabled", DynamicRangeCompressionEnabled);
                VoipAttenuationEnabled = audioSettings.GetAttributeBool("voipattenuationenabled", VoipAttenuationEnabled);
                VoiceChatVolume = audioSettings.GetAttributeFloat("voicechatvolume", VoiceChatVolume);
                MuteOnFocusLost = audioSettings.GetAttributeBool("muteonfocuslost", MuteOnFocusLost);

                UseDirectionalVoiceChat = audioSettings.GetAttributeBool("usedirectionalvoicechat", UseDirectionalVoiceChat);
                VoiceCaptureDevice = audioSettings.GetAttributeString("voicecapturedevice", VoiceCaptureDevice);
                NoiseGateThreshold = audioSettings.GetAttributeFloat("noisegatethreshold", NoiseGateThreshold);
                MicrophoneVolume = audioSettings.GetAttributeFloat("microphonevolume", MicrophoneVolume);
                var voiceSetting = VoiceMode.Disabled;
                string voiceSettingStr = audioSettings.GetAttributeString("voicesetting", "");
                if (Enum.TryParse(voiceSettingStr, out voiceSetting))
                {
                    VoiceSetting = voiceSetting;
                }
            }
        }

        private void LoadControls(XDocument doc)
        {
            XElement keyMapping = doc.Root.Element("keymapping");
            if (keyMapping != null)
            {
                LoadKeyBinds(keyMapping);
            }
        }

        private void LoadContentPackages(XDocument doc)
        {
            selectedContentPackagePaths = new HashSet<string>();
            foreach (XElement subElement in doc.Root.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "contentpackage":
                        string path = Path.GetFullPath(subElement.GetAttributeString("path", ""));
                        selectedContentPackagePaths.Add(path);
                        break;
                }
            }
            LoadContentPackages(selectedContentPackagePaths);
        }
        #endregion

        private void LoadKeyBinds(XElement element)
        {
            foreach (XAttribute attribute in element.Attributes())
            {
                if (!Enum.TryParse(attribute.Name.ToString(), true, out InputType inputType)) { continue; }

                if (int.TryParse(attribute.Value.ToString(), out int mouseButton))
                {
                    keyMapping[(int)inputType] = new KeyOrMouse(mouseButton);
                }
                else
                {
                    if (Enum.TryParse(attribute.Value.ToString(), true, out Keys key))
                    {
                        keyMapping[(int)inputType] = new KeyOrMouse(key);
                    }
                }
            }
        }

        public void ResetToDefault()
        {
            LoadDefaultConfig();
            CheckBindings(true);
            SaveNewPlayerConfig();
        }

        public KeyOrMouse KeyBind(InputType inputType)
        {
            return keyMapping[(int)inputType];
        }

        private void SetDefaultValues(bool resetLanguage = true)
        {
            GraphicsWidth = 0;
            GraphicsHeight = 0;
            VSyncEnabled = true;
#if DEBUG
            EnableSplashScreen = false;
#else
            EnableSplashScreen = true;
#endif
            ParticleLimit = 1500;
            LightMapScale = 0.5f;
            SpecularityEnabled = false;
            ChromaticAberrationEnabled = true;
            PauseOnFocusLost = true;
            MuteOnFocusLost = false;
            UseDirectionalVoiceChat = true;
            VoiceSetting = VoiceMode.Disabled;
            VoiceCaptureDevice = null;
            NoiseGateThreshold = -45;
            windowMode = WindowMode.BorderlessWindowed;
            losMode = LosMode.Transparent;
            useSteamMatchmaking = true;
            requireSteamAuthentication = true;
            QuickStartSubmarineName = string.Empty;
            CharacterHeadIndex = 1;
            CharacterHairIndex = -1;
            CharacterBeardIndex = -1;
            CharacterMoustacheIndex = -1;
            CharacterFaceAttachmentIndex = -1;
            CharacterGender = Gender.None;
            CharacterRace = Race.White;
            aimAssistAmount = 0.5f;
            EnableMouseLook = true;
            CrewMenuOpen = true;
            ChatOpen = true;
            soundVolume = 0.5f;
            musicVolume = 0.3f;
            DynamicRangeCompressionEnabled = true;
            VoipAttenuationEnabled = true;
            voiceChatVolume = 0.5f;
            microphoneVolume = 5.0f;
            AutoCheckUpdates = true;
            playerName = string.Empty;
            HUDScale = 1;
            InventoryScale = 1;
            AutoUpdateWorkshopItems = true;
            CampaignDisclaimerShown = false;
            if (resetLanguage)
            {
                Language = "English";
            }
            MasterServerUrl = "http://www.undertowgames.com/baromaster";
            WasGameUpdated = false;
            VerboseLogging = false;
            SaveDebugConsoleLogs = false;
            AutoUpdateWorkshopItems = true;
        }
    }
}
