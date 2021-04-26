using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Barotrauma.IO;
using Barotrauma.Extensions;
using System.Diagnostics;
#if CLIENT
using Microsoft.Xna.Framework.Input;
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
        public const string SavePath = "config.xml";
        public const string PlayerSavePath = "config_player.xml";
        public const string VanillaContentPackagePath = "Data/ContentPackages/Vanilla";

        public int GraphicsWidth { get; set; }
        public int GraphicsHeight { get; set; }

        public bool VSyncEnabled { get; set; }

        public bool TextureCompressionEnabled { get; set; }

        public bool EnableSplashScreen { get; set; }

        public int ParticleLimit { get; set; }

        public float LightMapScale { get; set; }
        public bool ChromaticAberrationEnabled { get; set; }

        public bool PauseOnFocusLost { get; set; }
        public bool MuteOnFocusLost { get; set; }
        public bool DynamicRangeCompressionEnabled { get; set; }
        public bool VoipAttenuationEnabled { get; set; }
        public bool UseDirectionalVoiceChat { get; set; }
        public bool DisableVoiceChatFilters { get; set; }

        public IList<string> AudioDeviceNames;
        public IList<string> CaptureDeviceNames;

        public string AudioOutputDevice { get; set; }

        public enum VoiceMode
        {
            Disabled,
            PushToTalk,
            Activity
        };

        public VoiceMode VoiceSetting { get; set; }
        public string VoiceCaptureDevice { get; set; }

        public float NoiseGateThreshold { get; set; }

        public bool UseLocalVoiceByDefault { get; set; }

#if CLIENT
        public KeyOrMouse[] keyMapping;
        private KeyOrMouse[] inventoryKeyMapping;
        public static Dictionary<Keys, string> ConsoleKeybinds = new Dictionary<Keys, string>();
#endif

        private WindowMode windowMode;

        private LosMode losMode;

        public List<Pair<string, int>> jobPreferences;

        public string QuickStartSubmarineName;

#if USE_STEAM
        public bool RequireSteamAuthentication { get; set; }
        public bool UseSteamMatchmaking { get; set; }
#else
        public bool RequireSteamAuthentication
        {
            get { return false; }
            set { /*do nothing*/ }
        }
        public bool UseSteamMatchmaking
        {
            get { return false; }
            set { /*do nothing*/ }
        }
#endif
        public bool UseDualModeSockets { get; set; } = true;

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

        public List<Pair<string, int>> JobPreferences
        {
            get { return jobPreferences; }
            set { jobPreferences = value; }
        }

        public CharacterTeamType TeamPreference { get; set; }

        public bool AreJobPreferencesEqual(List<Pair<string, int>> compareTo)
        {
            if (jobPreferences == null || compareTo == null) return false;
            if (jobPreferences.Count != compareTo.Count) return false;

            for (int i = 0; i < jobPreferences.Count; i++)
            {
                if (jobPreferences[i].First != compareTo[i].First || jobPreferences[i].Second != compareTo[i].Second) return false;
            }

            return true;
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

        public bool EnableRadialDistortion { get; set; } = true;

        public bool CrewMenuOpen { get; set; } = true;
        public bool ChatOpen { get; set; } = true;

        public float CorpseDespawnDelay { get; set; } = 10.0f * 60.0f;

        /// <summary>
        /// How many corpses there can be in a sub before they start to get despawned
        /// </summary>
        public int CorpsesPerSubDespawnThreshold { get; set; }  = 10;

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
                GameMain.SoundManager?.SetCategoryGainMultiplier("music", musicVolume * 0.7f, 0);
#endif
            }
        }

        public float VoiceChatVolume
        {
            get { return voiceChatVolume; }
            set
            {
                voiceChatVolume = MathHelper.Clamp(value, 0.0f, 2.0f);
#if CLIENT
                GameMain.SoundManager?.SetCategoryGainMultiplier("voip", Math.Min(voiceChatVolume, 1.0f), 0);
#endif
            }
        }


        public int VoiceChatCutoffPrevention
        {
            get;
            set;
        }

        public const float MaxMicrophoneVolume = 10.0f;
        public float MicrophoneVolume
        {
            get { return microphoneVolume; }
            set
            {
                microphoneVolume = MathHelper.Clamp(value, 0.2f, MaxMicrophoneVolume);
            }
        }
        public string Language
        {
            get { return TextManager.Language; }
            set { TextManager.Language = value; }
        }

        public ContentPackage CurrentCorePackage { get; private set; }
        private readonly List<ContentPackage> enabledRegularPackages = new List<ContentPackage>();
        public IReadOnlyList<ContentPackage> EnabledRegularPackages
        {
            get { return enabledRegularPackages; }
        }

        public IEnumerable<ContentPackage> AllEnabledPackages
        {
            get
            {
                yield return CurrentCorePackage;
                foreach (var package in EnabledRegularPackages)
                {
                    yield return package;
                }
            }
        }

        public bool ContentPackageSelectionDirtyNotification
        {
            get;
            set;
        }

        public bool ContentPackageSelectionDirty
        {
            get;
            private set;
        }

        public XElement ServerFilterElement
        {
            get;
            private set;
        }

        public volatile bool SuppressModFolderWatcher;

        public volatile bool WaitingForAutoUpdate;

        public bool DisableInGameHints { get; set; }

#if DEBUG
        public bool AutomaticQuickStartEnabled { get; set; }
        public bool AutomaticCampaignLoadEnabled { get; set; }
        public bool TextManagerDebugModeEnabled { get; set; }

        public bool ModBreakerMode { get; set; }
#endif

        private static int ContentFileLoadOrder(ContentFile a)
        {
            switch (a.Type)
            {
                case ContentType.Text:
                    return -2;
                case ContentType.Afflictions:
                    return -1;
                case ContentType.ItemAssembly:
                    return 1;
                default:
                    return 0;
            }
        }

        public void SelectCorePackage(ContentPackage contentPackage, bool forceReloadAll = false)
        {
            if (!contentPackage.IsCorePackage) { return; }
            if (!contentPackage.ContainsRequiredCorePackageFiles(out _)) { return; }

            ContentPackage prevCorePackage = CurrentCorePackage;

            CurrentCorePackage = contentPackage;

            if (prevCorePackage != null)
            {
                List<ContentFile> filesToRemove = prevCorePackage.Files.Where(f1 => forceReloadAll ||
                    !contentPackage.Files.Any(f2 =>
                        Path.GetFullPath(f1.Path).CleanUpPath() == Path.GetFullPath(f2.Path).CleanUpPath())).ToList();

                List<ContentFile> filesToAdd = contentPackage.Files.Where(f1 => forceReloadAll ||
                    !prevCorePackage.Files.Any(f2 =>
                        Path.GetFullPath(f1.Path).CleanUpPath() == Path.GetFullPath(f2.Path).CleanUpPath())).ToList();

                DisableContentPackageItems(filesToRemove);
                EnableContentPackageItems(filesToAdd);

                RefreshContentPackageItems(filesToAdd.Concat(filesToRemove));
            }
            else
            {
                EnableContentPackageItems(contentPackage.Files);
                RefreshContentPackageItems(contentPackage.Files);
            }
        }

        public void AutoSelectCorePackage(IEnumerable<ContentPackage> toRemove)
        {
            SelectCorePackage(ContentPackage.CorePackages.Find(cpp =>
                (toRemove == null || !toRemove.Contains(cpp)) &&
                cpp.ContainsRequiredCorePackageFiles(out _)));
        }

        private List<Tuple<ContentPackage, bool>> backupModOrder;

        public void BackUpModOrder()
        {
            backupModOrder = new List<Tuple<ContentPackage, bool>>
            {
                new Tuple<ContentPackage, bool>(CurrentCorePackage, true)
            };
            for (int i = 0; i < ContentPackage.RegularPackages.Count; i++)
            {
                var p = ContentPackage.RegularPackages[i];
                backupModOrder.Add(new Tuple<ContentPackage, bool>(p, EnabledRegularPackages.Contains(p)));
            }
        }

        public void SwapPackages(ContentPackage corePackage, List<ContentPackage> regularPackages)
        {
            List<ContentPackage> packagesToDisable = new List<ContentPackage>();
            packagesToDisable.Add(CurrentCorePackage);
            packagesToDisable.AddRange(enabledRegularPackages.Where(p => p.HasMultiplayerIncompatibleContent));
            List<ContentPackage> packagesToEnable = new List<ContentPackage>();
            packagesToEnable.Add(corePackage);
            List<ContentPackage> regularPackagesToAdd = regularPackages.Where(p => p.HasMultiplayerIncompatibleContent).ToList();
            packagesToEnable.AddRange(regularPackagesToAdd);

            IEnumerable<ContentFile> filesOfDisabledPkgs = packagesToDisable.SelectMany(p => p.Files);
            IEnumerable<ContentFile> filesOfEnabledPkgs = packagesToEnable.SelectMany(p => p.Files);

            List<ContentFile> filesToDisable = filesOfDisabledPkgs.Where(f1 =>
                    !filesOfEnabledPkgs.Any(f2 =>
                        Path.GetFullPath(f1.Path).CleanUpPath() == Path.GetFullPath(f2.Path).CleanUpPath())).ToList();

            List<ContentFile> filesToEnable = filesOfEnabledPkgs.Where(f1 =>
                !filesOfDisabledPkgs.Any(f2 =>
                    Path.GetFullPath(f1.Path).CleanUpPath() == Path.GetFullPath(f2.Path).CleanUpPath())).ToList();

            CurrentCorePackage = corePackage;
            enabledRegularPackages.RemoveAll(p => p.HasMultiplayerIncompatibleContent); enabledRegularPackages.AddRange(regularPackagesToAdd);

            DisableContentPackageItems(filesToDisable);
            EnableContentPackageItems(filesToEnable);

            RefreshContentPackageItems(filesOfEnabledPkgs.Concat(filesToDisable));

            ContentPackage.SortContentPackages(p => -regularPackages.IndexOf(p), config: this);

#if DEBUG
            Debug.Assert(enabledRegularPackages.Count == enabledRegularPackages.Distinct().Count());
#endif
        }

        public void RestoreBackupPackages()
        {
            if (backupModOrder == null) { return; }

            SwapPackages(
                backupModOrder[0].Item1,
                backupModOrder.Skip(1).Where(p => p.Item2).Select(p => p.Item1).ToList());
            ContentPackage.SortContentPackages(p => backupModOrder.FindIndex(n => n.Item1 == p), config: this);

            backupModOrder = null;
        }

        public void EnableRegularPackage(ContentPackage contentPackage)
        {
            if (contentPackage.IsCorePackage) { return; }
            if (!enabledRegularPackages.Contains(contentPackage))
            {
                enabledRegularPackages.Add(contentPackage);
                SortContentPackages();

                EnableContentPackageItems(contentPackage.Files);
                RefreshContentPackageItems(contentPackage.Files);
            }
        }

        public void DisableRegularPackage(ContentPackage contentPackage)
        {
            if (contentPackage.IsCorePackage) { return; }
            if (enabledRegularPackages.Contains(contentPackage))
            {
                enabledRegularPackages.Remove(contentPackage);
                SortContentPackages();

                DisableContentPackageItems(contentPackage.Files);
                RefreshContentPackageItems(contentPackage.Files);
            }
        }

        public void SortContentPackages(bool refreshAll = false)
        {
            var previousEnabledRegularPackages = enabledRegularPackages.ToList();

            for (int i = enabledRegularPackages.Count - 1; i >= 0; i--)
            {
                var package = enabledRegularPackages[i];
                if (!ContentPackage.RegularPackages.Contains(package))
                {
                    ContentPackage replacement = ContentPackage.RegularPackages.Find(p => p.Name.Equals(package.Name, StringComparison.OrdinalIgnoreCase));
                    if (replacement != null)
                    {
                        enabledRegularPackages[i] = replacement;
                    }
                    else
                    {
                        DisableRegularPackage(package);
                    }
                }
            }

            if (CurrentCorePackage == null)
            {
                AutoSelectCorePackage(null);
            }
            else if (!ContentPackage.CorePackages.Contains(CurrentCorePackage))
            {
                ContentPackage replacement = ContentPackage.CorePackages.Find(p => p.Name.Equals(CurrentCorePackage.Name, StringComparison.OrdinalIgnoreCase));
                if (replacement != null)
                {
                    SelectCorePackage(replacement);
                }
                else
                {
                    AutoSelectCorePackage(null);
                }
            }

            var sortedSelected = enabledRegularPackages
                    .OrderBy(p => -ContentPackage.RegularPackages.IndexOf(p))
                    .ToList();
            if (previousEnabledRegularPackages.SequenceEqual(sortedSelected)) { return; }
            enabledRegularPackages.Clear(); enabledRegularPackages.AddRange(sortedSelected);

            CharacterPrefab.Prefabs.SortAll();
            AfflictionPrefab.Prefabs.SortAll();
            JobPrefab.Prefabs.SortAll();
            ItemPrefab.Prefabs.SortAll();
            CoreEntityPrefab.Prefabs.SortAll();
            ItemAssemblyPrefab.Prefabs.SortAll();
            StructurePrefab.Prefabs.SortAll();

#if CLIENT
            GameMain.DecalManager?.Prefabs.SortAll();
            GameMain.ParticleManager?.Prefabs.SortAll();
#endif

            if (refreshAll)
            {
                RefreshContentPackageItems(AllEnabledPackages.SelectMany(p => p.Files));
            }
        }

        public void EnableContentPackageItems(IEnumerable<ContentFile> unorderedFiles)
        {
            if (WaitingForAutoUpdate) { return; }
            IOrderedEnumerable<ContentFile> files = unorderedFiles.OrderBy(ContentFileLoadOrder);
            foreach (ContentFile file in files)
            {
                switch (file.Type)
                {
                    case ContentType.Character:
                        CharacterPrefab.LoadFromFile(file);
                        break;
                    case ContentType.Corpses:
                        CorpsePrefab.LoadFromFile(file);
                        break;
                    case ContentType.NPCConversations:
                        NPCConversation.LoadFromFile(file);
                        break;
                    case ContentType.Jobs:
                        JobPrefab.LoadFromFile(file);
                        break;
                    case ContentType.Item:
                        ItemPrefab.LoadFromFile(file);
                        break;
                    case ContentType.ItemAssembly:
                        new ItemAssemblyPrefab(file.Path);
                        break;
                    case ContentType.Structure:
                        StructurePrefab.LoadFromFile(file);
                        break;
                    case ContentType.Text:
                        TextManager.LoadTextPack(file.Path);
                        break;
#if CLIENT
                    case ContentType.Particles:
                        GameMain.ParticleManager?.LoadPrefabsFromFile(file);
                        break;
                    case ContentType.Decals:
                        GameMain.DecalManager?.LoadFromFile(file);
                        break;
#endif
                }

                UpdateContentPackageDirtyFlag(file);
            }
        }

        public void DisableContentPackageItems(IEnumerable<ContentFile> unorderedFiles)
        {
            if (WaitingForAutoUpdate) { return; }
            IOrderedEnumerable<ContentFile> files = unorderedFiles.OrderBy(ContentFileLoadOrder);
            foreach (ContentFile file in files)
            {
                switch (file.Type)
                {
                    case ContentType.Character:
                        CharacterPrefab.RemoveByFile(file.Path);
                        break;
                    case ContentType.Corpses:
                        CorpsePrefab.RemoveByFile(file.Path);
                        break;
                    case ContentType.NPCConversations:
                        NPCConversation.RemoveByFile(file.Path);
                        break;
                    case ContentType.Jobs:
                        JobPrefab.RemoveByFile(file.Path);
                        break;
                    case ContentType.Item:
                        ItemPrefab.RemoveByFile(file.Path);
                        break;
                    case ContentType.ItemAssembly:
                        ItemAssemblyPrefab.Remove(file.Path);
                        break;
                    case ContentType.Structure:
                        StructurePrefab.RemoveByFile(file.Path);
                        break;
                    case ContentType.Text:
                        TextManager.RemoveTextPack(file.Path);
                        break;
#if CLIENT
                    case ContentType.Particles:
                        GameMain.ParticleManager?.RemovePrefabsByFile(file.Path);
                        break;
                    case ContentType.Decals:
                        GameMain.DecalManager?.RemoveByFile(file.Path);
                        break;
#endif
                }

                UpdateContentPackageDirtyFlag(file);
            }
        }

        public void RefreshContentPackageItems(IEnumerable<ContentFile> files)
        {
            if (WaitingForAutoUpdate) { return; }
            if (files.Any(f => f.Type == ContentType.Afflictions)) { AfflictionPrefab.LoadAll(GameMain.Instance.GetFilesOfType(ContentType.Afflictions)); }
            if (files.Any(f => f.Type == ContentType.Submarine ||
                               f.Type == ContentType.Outpost ||
                               f.Type == ContentType.OutpostModule ||
                               f.Type == ContentType.Wreck ||
                               f.Type == ContentType.BeaconStation)) { SubmarineInfo.RefreshSavedSubs(); }
            if (files.Any(f => f.Type == ContentType.NPCSets)) { NPCSet.LoadSets(); }
            if (files.Any(f => f.Type == ContentType.OutpostConfig)) { OutpostGenerationParams.LoadPresets(); }
            if (files.Any(f => f.Type == ContentType.Factions)) { FactionPrefab.LoadFactions(); }
            if (files.Any(f => f.Type == ContentType.Item)) { ItemPrefab.InitFabricationRecipes(); }
            if (files.Any(f => f.Type == ContentType.RuinConfig)) { RuinGeneration.RuinGenerationParams.ClearAll(); }
            if (files.Any(f => f.Type == ContentType.RandomEvents ||
                               f.Type == ContentType.LocationTypes))
            {
                LocationType.List.Clear();
                EventSet.LoadPrefabs();
                LocationType.Init();
            }
            if (files.Any(f => f.Type == ContentType.Missions)) { MissionPrefab.Init(); }
            if (files.Any(f => f.Type == ContentType.LevelObjectPrefabs)) { LevelObjectPrefab.LoadAll(); }
            if (files.Any(f => f.Type == ContentType.MapGenerationParameters)) { MapGenerationParams.Init(); }
            if (files.Any(f => f.Type == ContentType.LevelGenerationParameters)) { LevelGenerationParams.LoadPresets(); }
            if (files.Any(f => f.Type == ContentType.CaveGenerationParameters)) { CaveGenerationParams.LoadPresets(); }
            if (files.Any(f => f.Type == ContentType.TraitorMissions)) { TraitorMissionPrefab.Init(); }
            if (files.Any(f => f.Type == ContentType.Orders)) { Order.Init(); }
            if (files.Any(f => f.Type == ContentType.EventManagerSettings)) { EventManagerSettings.Init(); }
            if (files.Any(f => f.Type == ContentType.WreckAIConfig)) { WreckAIConfig.LoadAll(); }
            if (files.Any(f => f.Type == ContentType.SkillSettings)) { SkillSettings.Load(GameMain.Instance.GetFilesOfType(ContentType.SkillSettings)); }

#if CLIENT
            if (files.Any(f => f.Type == ContentType.Tutorials)) { Tutorial.Init(); }
            if (files.Any(f => f.Type == ContentType.Sounds)) { SoundPlayer.Init().ForEach(_ => { return; }); }
#endif
        }

        private readonly static ContentType[] hotswappableContentTypes = new ContentType[]
        {
            ContentType.Character,
            ContentType.Corpses,
            ContentType.NPCConversations,
            ContentType.Jobs,
            ContentType.Orders,
            ContentType.EventManagerSettings,
            ContentType.Item,
            ContentType.ItemAssembly,
            ContentType.Structure,
            ContentType.Submarine,
            ContentType.Text,
            ContentType.Afflictions,
            ContentType.RuinConfig,
            ContentType.RandomEvents,
            ContentType.Missions,
            ContentType.LevelObjectPrefabs,
            ContentType.LocationTypes,
            ContentType.MapGenerationParameters,
            ContentType.LevelGenerationParameters,
            ContentType.CaveGenerationParameters,
            ContentType.Sounds,
            ContentType.Particles,
            ContentType.Decals,
            ContentType.Outpost,
            ContentType.OutpostModule,
            ContentType.OutpostConfig,
            ContentType.NPCSets,
            ContentType.Factions,
            ContentType.Wreck,
            ContentType.WreckAIConfig,
            ContentType.BeaconStation,
            ContentType.BackgroundCreaturePrefabs,
            ContentType.ServerExecutable,
            ContentType.TraitorMissions,
            ContentType.Tutorials,
            ContentType.SkillSettings,
            ContentType.None
        };

        private void UpdateContentPackageDirtyFlag(ContentFile file)
        {
            if (!hotswappableContentTypes.Contains(file.Type))
            {
                if (ContentPackage.MultiplayerIncompatibleContent.Contains(file.Type))
                {
                    ContentPackageSelectionDirty = true;
                }
                ContentPackageSelectionDirtyNotification = true;
            }
        }

        public string MasterServerUrl { get; set; }
        public string RemoteContentUrl { get; set; }
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

        private const float MinTextScale = 0.5f, MaxTextScale = 1.5f;
        public static float TextScale { get; set; }
        private bool textScaleDirty;

        public List<string> CompletedTutorialNames { get; private set; }
        /// <summary>
        /// Identifiers of hints the player has chosen not to see again
        /// </summary>
        public HashSet<string> IgnoredHints { get; private set; } = new HashSet<string>();
        public HashSet<string> EncounteredCreatures { get; private set; } = new HashSet<string>();
        public HashSet<string> KilledCreatures { get; private set; } = new HashSet<string>();

        public readonly HashSet<string> RecentlyEncounteredCreatures = new HashSet<string>();

        public static bool VerboseLogging { get; set; }
        public static bool SaveDebugConsoleLogs { get; set; }

        public bool CampaignDisclaimerShown, EditorDisclaimerShown;

        private static bool sendUserStatistics = true;
        public static bool SendUserStatistics
        {
            get
            {
                return false;
/*#if DEBUG
                return false;
#endif
                return sendUserStatistics;*/
            }
            set
            {
                sendUserStatistics = value;
                GameMain.Config.SaveNewPlayerConfig();
            }
        }
        public static bool ShowUserStatisticsPrompt { get; set; }

        public bool ShowLanguageSelectionPrompt { get; set; }

        public static bool ShowOffensiveServerPrompt { get; set; }

        private bool showTutorialSkipWarning = true;

        public static bool EnableSubmarineAutoSave { get; set; }
        public static int MaximumAutoSaves { get; set; }
        public static int AutoSaveIntervalSeconds { get; set; }
        public static Color SubEditorBackgroundColor { get; set; }
        public static int SubEditorMaxUndoBuffer { get; set; }

        public bool ShowTutorialSkipWarning
        {
            get { return showTutorialSkipWarning && CompletedTutorialNames.Count == 0; }
            set { showTutorialSkipWarning = value; }
        }

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

        private void LoadDefaultConfig(bool setLanguage = true, bool loadContentPackages = true)
        {
            XDocument doc = XMLExtensions.TryLoadXml(SavePath);
            if (doc == null)
            {
                GraphicsWidth = 1024;
                GraphicsHeight = 768;
                MasterServerUrl = "";
                SelectCorePackage(ContentPackage.CorePackages.FirstOrDefault());
                jobPreferences = new List<Pair<string, int>>();
                return;
            }

            bool resetLanguage = setLanguage || string.IsNullOrEmpty(Language);
            SetDefaultValues(resetLanguage);
#if CLIENT
            SetDefaultBindings(doc, legacy: false);
#endif

            MasterServerUrl = doc.Root.GetAttributeString("masterserverurl", MasterServerUrl);
            RemoteContentUrl = doc.Root.GetAttributeString("remotecontenturl", RemoteContentUrl);
            WasGameUpdated = doc.Root.GetAttributeBool("wasgameupdated", WasGameUpdated);
            VerboseLogging = doc.Root.GetAttributeBool("verboselogging", VerboseLogging);
            SaveDebugConsoleLogs = doc.Root.GetAttributeBool("savedebugconsolelogs", SaveDebugConsoleLogs);
            AutoUpdateWorkshopItems = doc.Root.GetAttributeBool("autoupdateworkshopitems", AutoUpdateWorkshopItems);

            LoadGeneralSettings(doc, resetLanguage);
            LoadGraphicSettings(doc);
            LoadAudioSettings(doc);
#if CLIENT
            LoadControls(doc);
            LoadSubEditorImages(doc);
#endif
            if (loadContentPackages)
            {
                LoadContentPackages(doc);
            }

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
                new XAttribute("remotecontenturl", RemoteContentUrl),
                new XAttribute("autocheckupdates", AutoCheckUpdates),
                new XAttribute("musicvolume", musicVolume),
                new XAttribute("soundvolume", soundVolume),
                new XAttribute("microphonevolume", microphoneVolume),
                new XAttribute("voicechatvolume", voiceChatVolume),
                new XAttribute("voicechatcutoffprevention", VoiceChatCutoffPrevention),
                new XAttribute("verboselogging", VerboseLogging),
                new XAttribute("savedebugconsolelogs", SaveDebugConsoleLogs),
                new XAttribute("submarineautosave", EnableSubmarineAutoSave),
                new XAttribute("maxautosaves", MaximumAutoSaves),
                new XAttribute("autosaveintervalseconds", AutoSaveIntervalSeconds),
                new XAttribute("subeditorbackground", XMLExtensions.ColorToString(SubEditorBackgroundColor)),
                new XAttribute("subeditorundobuffer", SubEditorMaxUndoBuffer),
                new XAttribute("enablesplashscreen", EnableSplashScreen),
                new XAttribute("usesteammatchmaking", UseSteamMatchmaking),
                new XAttribute("quickstartsub", QuickStartSubmarineName),
                new XAttribute("requiresteamauthentication", RequireSteamAuthentication),
                new XAttribute("aimassistamount", aimAssistAmount),
                new XAttribute("tutorialskipwarning", ShowTutorialSkipWarning));

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
                    new XAttribute("framelimit", Timing.FrameLimit),
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
                new XAttribute("chromaticaberration", ChromaticAberrationEnabled),
                new XAttribute("losmode", LosMode),
                new XAttribute("hudscale", HUDScale),
                new XAttribute("inventoryscale", InventoryScale));

            foreach (ContentPackage contentPackage in ContentPackage.CorePackages)
            {
                if (contentPackage.Path.Contains(VanillaContentPackagePath))
                {
                    doc.Root.Add(new XElement("contentpackages", new XElement("core", new XAttribute("name", contentPackage.Name))));
                    break;
                }
            }

#if CLIENT
            var keyMappingElement = new XElement("keymapping");
            doc.Root.Add(keyMappingElement);
            for (int i = 0; i < keyMapping.Length; i++)
            {
                KeyOrMouse bind = keyMapping[i];
                if (bind.MouseButton == MouseButton.None)
                {
                    keyMappingElement.Add(new XAttribute(((InputType)i).ToString(), bind.Key));
                }
                else
                {
                    keyMappingElement.Add(new XAttribute(((InputType)i).ToString(), bind.MouseButton));
                }
            }

            var inventoryKeyMappingElement = new XElement("inventorykeymapping");
            doc.Root.Add(inventoryKeyMappingElement);
            for (int i = 0; i < inventoryKeyMapping.Length; i++)
            {
                KeyOrMouse bind = inventoryKeyMapping[i];
                if (bind.MouseButton == MouseButton.None)
                {
                    inventoryKeyMappingElement.Add(new XAttribute($"slot{i}", bind.Key));
                }
                else
                {
                    inventoryKeyMappingElement.Add(new XAttribute($"slot{i}", bind.MouseButton));
                }
            }
#endif

            var gameplay = new XElement("gameplay");
            var jobPreferences = new XElement("jobpreferences");
            foreach (Pair<string, int> job in JobPreferences)
            {
                XElement jobElement = new XElement("job");
                jobElement.Add(new XAttribute("identifier", job.First));
                jobElement.Add(new XAttribute("variant", job.Second));
                jobPreferences.Add(jobElement);
            }
            gameplay.Add(jobPreferences);

            var teamPreference = new XElement("teampreference");
            teamPreference.Add(new XAttribute("team", TeamPreference.ToString()));
            gameplay.Add(teamPreference);

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

            System.Xml.XmlWriterSettings settings = new System.Xml.XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = true,
                NewLineOnAttributes = true
            };

            try
            {
                using (var writer = XmlWriter.Create(SavePath, settings))
                {
                    doc.WriteTo(writer);
                    writer.Flush();
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Saving game settings failed.", e);
                GameAnalyticsManager.AddErrorEventOnce("GameSettings.Save:SaveFailed", GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                    "Saving game settings failed.\n" + e.Message + "\n" + e.StackTrace.CleanupStackTrace());
            }
        }

#region Load PlayerConfig
        public void LoadPlayerConfig()
        {
            bool fileFound = LoadPlayerConfigInternal();
#if CLIENT
            CheckBindings(!fileFound);
#endif
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
            XDocument doc = XMLExtensions.LoadXml(PlayerSavePath);
            if (doc == null || doc.Root == null)
            {
                ShowUserStatisticsPrompt = true;
                ShowTutorialSkipWarning = true;
                return false;
            }
            LoadGeneralSettings(doc);
            LoadGraphicSettings(doc);
            LoadAudioSettings(doc);
#if CLIENT
            LoadControls(doc);
            LoadSubEditorImages(doc);
#endif
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

            if (doc.Root.Element("ignoredhints") is XElement ignoredHintsElement)
            {
                IgnoredHints = new HashSet<string>(ignoredHintsElement.GetAttributeStringArray("identifiers", new string[0], convertToLowerInvariant: true));
            }

            XElement encounters = doc.Root.Element("encountered");
            if (encounters != null)
            {
                EncounteredCreatures = new HashSet<string>(encounters.GetAttributeStringArray("creatures", new string[0], convertToLowerInvariant: true));
            }
            XElement kills = doc.Root.Element("killed");
            if (kills != null)
            {
                KilledCreatures = new HashSet<string>(kills.GetAttributeStringArray("creatures", new string[0], convertToLowerInvariant: true));
            }

            ServerFilterElement = doc.Root.Element("serverfilters");

            UnsavedSettings = false;
            textScaleDirty = false;
            return true;
        }

#endregion

#region Save PlayerConfig
        public bool SaveNewPlayerConfig()
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
                new XAttribute("submarineautosave", EnableSubmarineAutoSave),
                new XAttribute("subeditorundobuffer", SubEditorMaxUndoBuffer),
                new XAttribute("maxautosaves", MaximumAutoSaves),
                new XAttribute("autosaveintervalseconds", AutoSaveIntervalSeconds),
                new XAttribute("subeditorbackground", XMLExtensions.ColorToString(SubEditorBackgroundColor)),
                new XAttribute("enablesplashscreen", EnableSplashScreen),
                new XAttribute("usesteammatchmaking", UseSteamMatchmaking),
                new XAttribute("quickstartsub", QuickStartSubmarineName),
                new XAttribute("requiresteamauthentication", RequireSteamAuthentication),
                new XAttribute("autoupdateworkshopitems", AutoUpdateWorkshopItems),
                new XAttribute("pauseonfocuslost", PauseOnFocusLost),
                new XAttribute("aimassistamount", aimAssistAmount),
                new XAttribute("enablemouselook", EnableMouseLook),
                new XAttribute("radialdistortion", EnableRadialDistortion),
                new XAttribute("chatopen", ChatOpen),
                new XAttribute("crewmenuopen", CrewMenuOpen),
                new XAttribute("campaigndisclaimershown", CampaignDisclaimerShown),
                new XAttribute("editordisclaimershown", EditorDisclaimerShown),
                new XAttribute("tutorialskipwarning", ShowTutorialSkipWarning),
                new XAttribute("corpsedespawndelay", CorpseDespawnDelay),
                new XAttribute("corpsespersubdespawnthreshold", CorpsesPerSubDespawnThreshold),
                new XAttribute("usedualmodesockets", UseDualModeSockets),
                new XAttribute("disableingamehints", DisableInGameHints)
#if DEBUG
                , new XAttribute("automaticquickstartenabled", AutomaticQuickStartEnabled)
                , new XAttribute("automaticcampaignloadenabled", AutomaticCampaignLoadEnabled)
                , new XAttribute("textmanagerdebugmodeenabled", TextManagerDebugModeEnabled)
                , new XAttribute("modbreakermode", ModBreakerMode)
#endif
                );

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
                    new XAttribute("compresstextures", TextureCompressionEnabled),
                    new XAttribute("framelimit", Timing.FrameLimit),
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
                new XAttribute("voicechatcutoffprevention", VoiceChatCutoffPrevention),
                new XAttribute("microphonevolume", microphoneVolume),
                new XAttribute("muteonfocuslost", MuteOnFocusLost),
                new XAttribute("dynamicrangecompressionenabled", DynamicRangeCompressionEnabled),
                new XAttribute("voipattenuationenabled", VoipAttenuationEnabled),
                new XAttribute("usedirectionalvoicechat", UseDirectionalVoiceChat),
                new XAttribute("voicesetting", VoiceSetting),
                new XAttribute("audiooutputdevice", System.Xml.XmlConvert.EncodeName(AudioOutputDevice ?? "")),
                new XAttribute("voicecapturedevice", System.Xml.XmlConvert.EncodeName(VoiceCaptureDevice ?? "")),
                new XAttribute("noisegatethreshold", NoiseGateThreshold),
                new XAttribute("uselocalvoicebydefault", UseLocalVoiceByDefault));

            XElement gSettings = doc.Root.Element("graphicssettings");
            if (gSettings == null)
            {
                gSettings = new XElement("graphicssettings");
                doc.Root.Add(gSettings);
            }

            gSettings.ReplaceAttributes(
                new XAttribute("particlelimit", ParticleLimit),
                new XAttribute("lightmapscale", LightMapScale),
                new XAttribute("chromaticaberration", ChromaticAberrationEnabled),
                new XAttribute("losmode", LosMode),
                new XAttribute("hudscale", HUDScale),
                new XAttribute("inventoryscale", InventoryScale),
                new XAttribute("textscale", TextScale));

            XElement contentPackagesElement = new XElement("contentpackages");

            string corePackageName = (CurrentCorePackage ?? ContentPackage.CorePackages.FirstOrDefault()).Name;
            contentPackagesElement.Add(new XElement("core", new XAttribute("name", corePackageName)));

            XElement regularPackagesElement = new XElement("regular");
            foreach (ContentPackage package in ContentPackage.RegularPackages)
            {
                XElement packageElement = new XElement("package", new XAttribute("name", package.Name));
                if (EnabledRegularPackages.Contains(package)) { packageElement.Add(new XAttribute("enabled", "true")); }
                regularPackagesElement.Add(packageElement);
            }
            contentPackagesElement.Add(regularPackagesElement);

            doc.Root.Add(contentPackagesElement);

#if CLIENT
            var keyMappingElement = new XElement("keymapping");
            doc.Root.Add(keyMappingElement);
            for (int i = 0; i < keyMapping.Length; i++)
            {
                var key = keyMapping[i];
                if (key == null) { continue; }
                if (key.MouseButton == MouseButton.None)
                {
                    keyMappingElement.Add(new XAttribute(((InputType)i).ToString(), keyMapping[i].Key));
                }
                else
                {
                    keyMappingElement.Add(new XAttribute(((InputType)i).ToString(), keyMapping[i].MouseButton));
                }
            }

            var inventoryKeyMappingElement = new XElement("inventorykeymapping");
            doc.Root.Add(inventoryKeyMappingElement);
            for (int i = 0; i < inventoryKeyMapping.Length; i++)
            {
                KeyOrMouse bind = inventoryKeyMapping[i];
                if (bind.MouseButton == MouseButton.None)
                {
                    inventoryKeyMappingElement.Add(new XAttribute($"slot{i}", bind.Key));
                }
                else
                {
                    inventoryKeyMappingElement.Add(new XAttribute($"slot{i}", bind.MouseButton));
                }
            }

            var debugconsoleKeyMappingElement = new XElement("debugconsolemapping");
            doc.Root.Add(debugconsoleKeyMappingElement);
            foreach (var (key, command) in ConsoleKeybinds)
            {
                debugconsoleKeyMappingElement.Add(new XElement("Keybind", 
                    new XAttribute("key", key.ToString()),
                    new XAttribute("command", command)));
            }

            if (ServerFilterElement == null)
            {
                ShowOffensiveServerPrompt = true;
                ServerFilterElement = new XElement("serverfilters");
            }
            GameMain.ServerListScreen?.SaveServerFilters(ServerFilterElement);
            doc.Root.Add(ServerFilterElement);

            SubEditorScreen.ImageManager.Save(doc.Root);
#endif

            var gameplay = new XElement("gameplay");
            var jobPreferences = new XElement("jobpreferences");
            foreach (Pair<string, int> job in JobPreferences)
            {
                XElement jobElement = new XElement("job");
                jobElement.Add(new XAttribute("identifier", job.First));
                jobElement.Add(new XAttribute("variant", job.Second));
                jobPreferences.Add(jobElement);
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

            doc.Root.Add(new XElement("ignoredhints", new XAttribute("identifiers", string.Join(",", IgnoredHints).Trim().ToLowerInvariant())));

            doc.Root.Add(new XElement("encountered", new XAttribute("creatures", string.Join(",", EncounteredCreatures).Trim().ToLowerInvariant())));
            doc.Root.Add(new XElement("killed", new XAttribute("creatures", string.Join(",", KilledCreatures).Trim().ToLowerInvariant())));

            System.Xml.XmlWriterSettings settings = new System.Xml.XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = true,
                NewLineOnAttributes = true
            };

            try
            {
                using (var writer = XmlWriter.Create(PlayerSavePath, settings))
                {
                    doc.WriteTo(writer);
                    writer.Flush();
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Saving game settings failed.", e);
                GameAnalyticsManager.AddErrorEventOnce("GameSettings.Save:SaveFailed", GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                    "Saving game settings failed.\n" + e.Message + "\n" + e.StackTrace.CleanupStackTrace());
                return false;
            }

            return true;
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
            EnableSubmarineAutoSave = doc.Root.GetAttributeBool("submarineautosave", true);
            MaximumAutoSaves = doc.Root.GetAttributeInt("maxautosaves", 8);
            AutoSaveIntervalSeconds = doc.Root.GetAttributeInt("autosaveintervalseconds", 300);
            SubEditorBackgroundColor = doc.Root.GetAttributeColor("subeditorbackground", new Color(0.051f, 0.149f, 0.271f, 1.0f));
            SubEditorMaxUndoBuffer = doc.Root.GetAttributeInt("subeditorundobuffer", 32);
            UseSteamMatchmaking = doc.Root.GetAttributeBool("usesteammatchmaking", UseSteamMatchmaking);
            RequireSteamAuthentication = doc.Root.GetAttributeBool("requiresteamauthentication", RequireSteamAuthentication);
            EnableSplashScreen = doc.Root.GetAttributeBool("enablesplashscreen", EnableSplashScreen);
            PauseOnFocusLost = doc.Root.GetAttributeBool("pauseonfocuslost", PauseOnFocusLost);
            AimAssistAmount = doc.Root.GetAttributeFloat("aimassistamount", AimAssistAmount);
            EnableMouseLook = doc.Root.GetAttributeBool("enablemouselook", EnableMouseLook);
            EnableRadialDistortion = doc.Root.GetAttributeBool("radialdistortion", EnableRadialDistortion);
            CrewMenuOpen = doc.Root.GetAttributeBool("crewmenuopen", CrewMenuOpen);
            ChatOpen = doc.Root.GetAttributeBool("chatopen", ChatOpen);
            CorpseDespawnDelay = doc.Root.GetAttributeInt("corpsedespawndelay", 10 * 60);
            CorpsesPerSubDespawnThreshold = doc.Root.GetAttributeInt("corpsespersubdespawnthreshold", 5);
            CampaignDisclaimerShown = doc.Root.GetAttributeBool("campaigndisclaimershown", CampaignDisclaimerShown);
            EditorDisclaimerShown = doc.Root.GetAttributeBool("editordisclaimershown", EditorDisclaimerShown);
            ShowTutorialSkipWarning = doc.Root.GetAttributeBool("tutorialskipwarning", true);
            UseDualModeSockets = doc.Root.GetAttributeBool("usedualmodesockets", true);
            DisableInGameHints = doc.Root.GetAttributeBool("disableingamehints", DisableInGameHints);
#if DEBUG
            AutomaticQuickStartEnabled = doc.Root.GetAttributeBool("automaticquickstartenabled", AutomaticQuickStartEnabled);
            AutomaticCampaignLoadEnabled = doc.Root.GetAttributeBool("automaticcampaignloadenabled", AutomaticCampaignLoadEnabled);
            TextManagerDebugModeEnabled = doc.Root.GetAttributeBool("textmanagerdebugmodeenabled", TextManagerDebugModeEnabled);
            ModBreakerMode = doc.Root.GetAttributeBool("modbreakermode", ModBreakerMode);
#endif
            XElement gameplayElement = doc.Root.Element("gameplay");
            jobPreferences = new List<Pair<string, int>>();
            if (gameplayElement != null)
            {
                var preferencesElement = gameplayElement.Element("jobpreferences");
                if (preferencesElement != null)
                {
                    foreach (XElement ele in preferencesElement.Elements("job"))
                    {
                        string jobIdentifier = ele.GetAttributeString("identifier", "");
                        int outfitVariant = ele.GetAttributeInt("variant", 1);
                        if (string.IsNullOrEmpty(jobIdentifier)) continue;
                        jobPreferences.Add(new Pair<string, int>(jobIdentifier, outfitVariant));
                    }
                }

                var teamPreferenceElement = gameplayElement.Element("teampreference");
                if (teamPreferenceElement != null)
                {
                    TeamPreference = (CharacterTeamType)Enum.Parse(typeof(CharacterTeamType), teamPreferenceElement.GetAttributeString("team", CharacterTeamType.None.ToString()));
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
            TextureCompressionEnabled = graphicsMode.GetAttributeBool("compresstextures", TextureCompressionEnabled);
            Timing.FrameLimit = graphicsMode.GetAttributeInt("framelimit", 200);

            XElement graphicsSettings = doc.Root.Element("graphicssettings");
            ParticleLimit = graphicsSettings.GetAttributeInt("particlelimit", ParticleLimit);
            LightMapScale = MathHelper.Clamp(graphicsSettings.GetAttributeFloat("lightmapscale", LightMapScale), 0.1f, 1.0f);
            ChromaticAberrationEnabled = graphicsSettings.GetAttributeBool("chromaticaberration", ChromaticAberrationEnabled);
            HUDScale = graphicsSettings.GetAttributeFloat("hudscale", HUDScale);
            InventoryScale = graphicsSettings.GetAttributeFloat("inventoryscale", InventoryScale);
            TextScale = graphicsSettings.GetAttributeFloat("textscale", TextScale);
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
                VoiceChatCutoffPrevention = audioSettings.GetAttributeInt("voicechatcutoffprevention", VoiceChatCutoffPrevention);
                MuteOnFocusLost = audioSettings.GetAttributeBool("muteonfocuslost", MuteOnFocusLost);

                UseDirectionalVoiceChat = audioSettings.GetAttributeBool("usedirectionalvoicechat", UseDirectionalVoiceChat);
                VoiceCaptureDevice = System.Xml.XmlConvert.DecodeName(audioSettings.GetAttributeString("voicecapturedevice", VoiceCaptureDevice));
                AudioOutputDevice = System.Xml.XmlConvert.DecodeName(audioSettings.GetAttributeString("audiooutputdevice", AudioOutputDevice));
                NoiseGateThreshold = audioSettings.GetAttributeFloat("noisegatethreshold", NoiseGateThreshold);
                UseLocalVoiceByDefault = audioSettings.GetAttributeBool("uselocalvoicebydefault", UseLocalVoiceByDefault);
                MicrophoneVolume = audioSettings.GetAttributeFloat("microphonevolume", MicrophoneVolume);
                string voiceSettingStr = audioSettings.GetAttributeString("voicesetting", "");
                if (Enum.TryParse(voiceSettingStr, out VoiceMode voiceSetting))
                {
                    VoiceSetting = voiceSetting;
                }
            }
        }

        private void LoadContentPackages(XDocument doc)
        {
            CurrentCorePackage = null;
            enabledRegularPackages.Clear();

#if DEBUG && CLIENT
            if (ModBreakerMode)
            {
                CurrentCorePackage = ContentPackage.CorePackages.GetRandom();
                foreach (var regularPackage in ContentPackage.RegularPackages)
                {
                    if (Rand.Range(0.0, 1.0) <= 0.5)
                    {
                        enabledRegularPackages.Add(regularPackage);
                    }
                }
                ContentPackage.SortContentPackages(p =>
                {
                    return Rand.Int(int.MaxValue);
                }, config: this);

                if (CurrentCorePackage == null)
                {
                    CurrentCorePackage = ContentPackage.CorePackages.First();
                }

                TextManager.LoadTextPacks(AllEnabledPackages);
                return;
            }
#endif

            var contentPackagesElement = doc.Root.Element("contentpackages");
            if (contentPackagesElement != null)
            {
                string coreName = contentPackagesElement.Element("core")?.GetAttributeString("name", "");
                ContentPackage corePackage = ContentPackage.CorePackages.Find(p => p.Name.Equals(coreName, StringComparison.OrdinalIgnoreCase));
                if (corePackage != null)
                {
                    CurrentCorePackage = corePackage;
                }

                XElement regularElement = contentPackagesElement.Element("regular");

                List<XElement> subElements = regularElement?.Elements()?.ToList();
                if (subElements != null)
                {
                    foreach (var subElement in subElements)
                    {
                        if (!bool.TryParse(subElement.GetAttributeString("enabled", "false"), out bool enabled) || !enabled) { continue; }

                        string name = subElement.GetAttributeString("name", null);
                        if (string.IsNullOrEmpty(name)) { continue; }

                        var package = ContentPackage.RegularPackages.Find(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                        if (package == null) { continue; }
                        enabledRegularPackages.Add(package);
                    }

                    ContentPackage.SortContentPackages(p =>
                    {
                        int index = subElements.FindIndex(e =>
                        {
                            string name = e.GetAttributeString("name", null);
                            return p.Name.Equals(name, StringComparison.OrdinalIgnoreCase);
                        });
                        return index;
                    }, config: this);
                }
            }
            else
            {
                var enabledContentPackagePaths = new List<string>();
                foreach (XElement subElement in doc.Root.Elements())
                {
                    switch (subElement.Name.ToString().ToLowerInvariant())
                    {
                        case "contentpackage":
                            string path = subElement.GetAttributeString("path", "");
                            enabledContentPackagePaths.Add(path.CleanUpPath().ToLowerInvariant());
                            break;
                    }
                }

                foreach (string path in enabledContentPackagePaths)
                {
                    ContentPackage package = ContentPackage.AllPackages
                        .FirstOrDefault(p => p.Path.CleanUpPath().Equals(path, StringComparison.OrdinalIgnoreCase));
                    if (package == null) { continue; }
                    if (package.IsCorePackage) { CurrentCorePackage = package; }
                    else { enabledRegularPackages.Add(package); }
                }

                ContentPackage.SortContentPackages(p => enabledContentPackagePaths.IndexOf(p.Path.CleanUpPath().ToLowerInvariant()), config: this);
            }

            if (CurrentCorePackage == null)
            {
                CurrentCorePackage = ContentPackage.CorePackages.First();
            }

            TextManager.LoadTextPacks(AllEnabledPackages);
        }
#endregion

        public void ResetToDefault()
        {
            LoadDefaultConfig();
#if CLIENT
            CheckBindings(true);
#endif
            SaveNewPlayerConfig();
        }

        private void SetDefaultValues(bool resetLanguage = true)
        {
            GraphicsWidth = 0;
            GraphicsHeight = 0;
            VSyncEnabled = true;
            TextureCompressionEnabled = true;
            Timing.FrameLimit = 200;
#if DEBUG
            EnableSplashScreen = false;
#else
            EnableSplashScreen = true;
#endif
            ParticleLimit = 1500;
            LightMapScale = 0.5f;
            ChromaticAberrationEnabled = true;
            PauseOnFocusLost = true;
            MuteOnFocusLost = false;
            UseDirectionalVoiceChat = true;
            VoiceSetting = VoiceMode.Disabled;
            VoiceCaptureDevice = null;
            NoiseGateThreshold = -45;
            UseLocalVoiceByDefault = false;
            windowMode = WindowMode.BorderlessWindowed;
            losMode = LosMode.Transparent;
            UseSteamMatchmaking = true;
            RequireSteamAuthentication = true;
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
            EnableRadialDistortion = true;
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
            CorpseDespawnDelay = 10 * 60;
            CorpsesPerSubDespawnThreshold = 5;
            if (resetLanguage)
            {
                Language = "English";
            }
            MasterServerUrl = "http://www.undertowgames.com/baromaster";
            WasGameUpdated = false;
            VerboseLogging = false;
            SaveDebugConsoleLogs = false;
            AutoUpdateWorkshopItems = true;
            TextScale = 1;
            textScaleDirty = false;
            DisableInGameHints = false;
        }
    }
}
