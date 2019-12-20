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
        public bool ChromaticAberrationEnabled { get; set; }

        public bool PauseOnFocusLost { get; set; }
        public bool MuteOnFocusLost { get; set; }
        public bool DynamicRangeCompressionEnabled { get; set; }
        public bool VoipAttenuationEnabled { get; set; }
        public bool UseDirectionalVoiceChat { get; set; }

        public IList<string> CaptureDeviceNames;

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

        public readonly List<ContentPackage> SelectedContentPackages = new List<ContentPackage>();

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

        public volatile bool SuppressModFolderWatcher;


        private FileSystemWatcher modsFolderWatcher;

        private int ContentFileLoadOrder(ContentFile a)
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
            ContentPackage otherCorePackage = SelectedContentPackages.Where(cp => cp.CorePackage).First();

            SelectedContentPackages.Remove(otherCorePackage);
            SelectedContentPackages.Add(contentPackage);

            ContentPackage.SortContentPackages();

            List<ContentFile> filesToRemove = otherCorePackage.Files.Where(f1 => forceReloadAll ||
                !contentPackage.Files.Any(f2 =>
                    Path.GetFullPath(f1.Path).CleanUpPath() == Path.GetFullPath(f2.Path).CleanUpPath())).ToList();

            List<ContentFile> filesToAdd = contentPackage.Files.Where(f1 => forceReloadAll ||
                !otherCorePackage.Files.Any(f2 =>
                    Path.GetFullPath(f1.Path).CleanUpPath() == Path.GetFullPath(f2.Path).CleanUpPath())).ToList();

            bool shouldRefreshSubs = false;
            bool shouldRefreshFabricationRecipes = false;
            bool shouldRefreshSoundPlayer = false;
            bool shouldRefreshRuinGenerationParams = false;
            bool shouldRefreshScriptedEventSets = false;
            bool shouldRefreshMissionPrefabs = false;
            bool shouldRefreshLevelObjectPrefabs = false;
            bool shouldRefreshLocationTypes = false;
            bool shouldRefreshMapGenerationParams = false;
            bool shouldRefreshLevelGenerationParams = false;
            bool shouldRefreshAfflictions = false;

            DisableContentPackageItems(filesToRemove.OrderBy(ContentFileLoadOrder),
                                            ref shouldRefreshSubs,
                                            ref shouldRefreshFabricationRecipes,
                                            ref shouldRefreshSoundPlayer,
                                            ref shouldRefreshRuinGenerationParams,
                                            ref shouldRefreshScriptedEventSets,
                                            ref shouldRefreshMissionPrefabs,
                                            ref shouldRefreshLevelObjectPrefabs,
                                            ref shouldRefreshLocationTypes,
                                            ref shouldRefreshMapGenerationParams,
                                            ref shouldRefreshLevelGenerationParams,
                                            ref shouldRefreshAfflictions);

            EnableContentPackageItems(filesToAdd.OrderBy(ContentFileLoadOrder),
                                            ref shouldRefreshSubs,
                                            ref shouldRefreshFabricationRecipes,
                                            ref shouldRefreshSoundPlayer,
                                            ref shouldRefreshRuinGenerationParams,
                                            ref shouldRefreshScriptedEventSets,
                                            ref shouldRefreshMissionPrefabs,
                                            ref shouldRefreshLevelObjectPrefabs,
                                            ref shouldRefreshLocationTypes,
                                            ref shouldRefreshMapGenerationParams,
                                            ref shouldRefreshLevelGenerationParams,
                                            ref shouldRefreshAfflictions);

            if (shouldRefreshAfflictions) { AfflictionPrefab.LoadAll(GameMain.Instance.GetFilesOfType(ContentType.Afflictions)); }
            if (shouldRefreshSubs) { Submarine.RefreshSavedSubs(); }
            if (shouldRefreshFabricationRecipes) { ItemPrefab.InitFabricationRecipes(); }
            if (shouldRefreshRuinGenerationParams) { RuinGeneration.RuinGenerationParams.ClearAll(); }
            if (shouldRefreshScriptedEventSets) { ScriptedEventSet.LoadPrefabs(); }
            if (shouldRefreshMissionPrefabs) { MissionPrefab.Init(); }
            if (shouldRefreshLevelObjectPrefabs) { LevelObjectPrefab.LoadAll(); }
            if (shouldRefreshLocationTypes) { LocationType.Init(); }
            if (shouldRefreshMapGenerationParams) { MapGenerationParams.Init(); }
            if (shouldRefreshLevelGenerationParams) { LevelGenerationParams.LoadPresets(); }

#if CLIENT
            if (shouldRefreshSoundPlayer) { SoundPlayer.Init().ForEach(_ => { return; }); }
#endif

        }
        public void SelectContentPackage(ContentPackage contentPackage)
        {
            if (!SelectedContentPackages.Contains(contentPackage))
            {
                SelectedContentPackages.Add(contentPackage);
                ContentPackage.SortContentPackages();

                bool shouldRefreshSubs = false;
                bool shouldRefreshFabricationRecipes = false;
                bool shouldRefreshSoundPlayer = false;
                bool shouldRefreshRuinGenerationParams = false;
                bool shouldRefreshScriptedEventSets = false;
                bool shouldRefreshMissionPrefabs = false;
                bool shouldRefreshLevelObjectPrefabs = false;
                bool shouldRefreshLocationTypes = false;
                bool shouldRefreshMapGenerationParams = false;
                bool shouldRefreshLevelGenerationParams = false;
                bool shouldRefreshAfflictions = false;

                EnableContentPackageItems(contentPackage.Files.OrderBy(ContentFileLoadOrder),
                                            ref shouldRefreshSubs,
                                            ref shouldRefreshFabricationRecipes,
                                            ref shouldRefreshSoundPlayer,
                                            ref shouldRefreshRuinGenerationParams,
                                            ref shouldRefreshScriptedEventSets,
                                            ref shouldRefreshMissionPrefabs,
                                            ref shouldRefreshLevelObjectPrefabs,
                                            ref shouldRefreshLocationTypes,
                                            ref shouldRefreshMapGenerationParams,
                                            ref shouldRefreshLevelGenerationParams,
                                            ref shouldRefreshAfflictions);

                if (shouldRefreshAfflictions) { AfflictionPrefab.LoadAll(GameMain.Instance.GetFilesOfType(ContentType.Afflictions)); }
                if (shouldRefreshSubs) { Submarine.RefreshSavedSubs(); }
                if (shouldRefreshFabricationRecipes) { ItemPrefab.InitFabricationRecipes(); }
                if (shouldRefreshRuinGenerationParams) { RuinGeneration.RuinGenerationParams.ClearAll(); }
                if (shouldRefreshScriptedEventSets) { ScriptedEventSet.LoadPrefabs(); }
                if (shouldRefreshMissionPrefabs) { MissionPrefab.Init(); }
                if (shouldRefreshLevelObjectPrefabs) { LevelObjectPrefab.LoadAll(); }
                if (shouldRefreshLocationTypes) { LocationType.Init(); }
                if (shouldRefreshMapGenerationParams) { MapGenerationParams.Init(); }
                if (shouldRefreshLevelGenerationParams) { LevelGenerationParams.LoadPresets(); }

#if CLIENT
                if (shouldRefreshSoundPlayer) { SoundPlayer.Init().ForEach(_ => { return; }); }
#endif
            }
        }

        public void DeselectContentPackage(ContentPackage contentPackage)
        {
            if (SelectedContentPackages.Contains(contentPackage))
            {
                SelectedContentPackages.Remove(contentPackage);
                ContentPackage.SortContentPackages();

                bool shouldRefreshSubs = false;
                bool shouldRefreshFabricationRecipes = false;
                bool shouldRefreshSoundPlayer = false;
                bool shouldRefreshRuinGenerationParams = false;
                bool shouldRefreshScriptedEventSets = false;
                bool shouldRefreshMissionPrefabs = false;
                bool shouldRefreshLevelObjectPrefabs = false;
                bool shouldRefreshLocationTypes = false;
                bool shouldRefreshMapGenerationParams = false;
                bool shouldRefreshLevelGenerationParams = false;
                bool shouldRefreshAfflictions = false;

                DisableContentPackageItems(contentPackage.Files.OrderBy(ContentFileLoadOrder),
                                            ref shouldRefreshSubs,
                                            ref shouldRefreshFabricationRecipes,
                                            ref shouldRefreshSoundPlayer,
                                            ref shouldRefreshRuinGenerationParams,
                                            ref shouldRefreshScriptedEventSets,
                                            ref shouldRefreshMissionPrefabs,
                                            ref shouldRefreshLevelObjectPrefabs,
                                            ref shouldRefreshLocationTypes,
                                            ref shouldRefreshMapGenerationParams,
                                            ref shouldRefreshLevelGenerationParams,
                                            ref shouldRefreshAfflictions);

                if (shouldRefreshAfflictions) { AfflictionPrefab.LoadAll(GameMain.Instance.GetFilesOfType(ContentType.Afflictions)); }
                if (shouldRefreshSubs) { Submarine.RefreshSavedSubs(); }
                if (shouldRefreshFabricationRecipes) { ItemPrefab.InitFabricationRecipes(); }
                if (shouldRefreshRuinGenerationParams) { RuinGeneration.RuinGenerationParams.ClearAll(); }
                if (shouldRefreshScriptedEventSets) { ScriptedEventSet.LoadPrefabs(); }
                if (shouldRefreshMissionPrefabs) { MissionPrefab.Init(); }
                if (shouldRefreshLevelObjectPrefabs) { LevelObjectPrefab.LoadAll(); }
                if (shouldRefreshLocationTypes) { LocationType.Init(); }
                if (shouldRefreshMapGenerationParams) { MapGenerationParams.Init(); }
                if (shouldRefreshLevelGenerationParams) { LevelGenerationParams.LoadPresets(); }

#if CLIENT
                if (shouldRefreshSoundPlayer) { SoundPlayer.Init().ForEach(_ => { return; }); }
#endif
            }
        }


        private void EnableContentPackageItems(IOrderedEnumerable<ContentFile> files,
                                                ref bool shouldRefreshSubs,
                                                ref bool shouldRefreshFabricationRecipes,
                                                ref bool shouldRefreshSoundPlayer,
                                                ref bool shouldRefreshRuinGenerationParams,
                                                ref bool shouldRefreshScriptedEventSets,
                                                ref bool shouldRefreshMissionPrefabs,
                                                ref bool shouldRefreshLevelObjectPrefabs,
                                                ref bool shouldRefreshLocationTypes,
                                                ref bool shouldRefreshMapGenerationParams,
                                                ref bool shouldRefreshLevelGenerationParams,
                                                ref bool shouldRefreshAfflictions)
        {
            foreach (ContentFile file in files)
            {
                switch (file.Type)
                {
                    case ContentType.Character:
                        CharacterPrefab.LoadFromFile(file);
                        break;
                    case ContentType.NPCConversations:
                        NPCConversation.LoadFromFile(file);
                        break;
                    case ContentType.Jobs:
                        JobPrefab.LoadFromFile(file);
                        break;
                    case ContentType.Item:
                        ItemPrefab.LoadFromFile(file);
                        shouldRefreshFabricationRecipes = true;
                        break;
                    case ContentType.ItemAssembly:
                        new ItemAssemblyPrefab(file.Path);
                        break;
                    case ContentType.Structure:
                        StructurePrefab.LoadFromFile(file);
                        break;
                    case ContentType.Submarine:
                        shouldRefreshSubs = true;
                        break;
                    case ContentType.Text:
                        TextManager.LoadTextPack(file.Path);
                        break;
                    case ContentType.Afflictions:
                        shouldRefreshAfflictions = true;
                        break;
                    case ContentType.RuinConfig:
                        shouldRefreshRuinGenerationParams = true;
                        break;
                    case ContentType.RandomEvents:
                        shouldRefreshScriptedEventSets = true;
                        break;
                    case ContentType.Missions:
                        shouldRefreshMissionPrefabs = true;
                        break;
                    case ContentType.LevelObjectPrefabs:
                        shouldRefreshLevelObjectPrefabs = true;
                        break;
                    case ContentType.LocationTypes:
                        shouldRefreshLocationTypes = true;
                        break;
                    case ContentType.MapGenerationParameters:
                        shouldRefreshMapGenerationParams = true;
                        break;
                    case ContentType.LevelGenerationParameters:
                        shouldRefreshLevelGenerationParams = true;
                        break;
#if CLIENT
                    case ContentType.Sounds:
                        shouldRefreshSoundPlayer = true;
                        break;
                    case ContentType.Particles:
                        GameMain.ParticleManager.LoadPrefabsFromFile(file);
                        break;
                    case ContentType.Decals:
                        GameMain.DecalManager.LoadFromFile(file);
                        break;
#endif
                }

                UpdateContentPackageDirtyFlag(file);
            }
        }

        private void DisableContentPackageItems(IOrderedEnumerable<ContentFile> files,
                                                ref bool shouldRefreshSubs,
                                                ref bool shouldRefreshFabricationRecipes,
                                                ref bool shouldRefreshSoundPlayer,
                                                ref bool shouldRefreshRuinGenerationParams,
                                                ref bool shouldRefreshScriptedEventSets,
                                                ref bool shouldRefreshMissionPrefabs,
                                                ref bool shouldRefreshLevelObjectPrefabs,
                                                ref bool shouldRefreshLocationTypes,
                                                ref bool shouldRefreshMapGenerationParams,
                                                ref bool shouldRefreshLevelGenerationParams,
                                                ref bool shouldRefreshAfflictions)
        {
            foreach (ContentFile file in files)
            {
                switch (file.Type)
                {
                    case ContentType.Character:
                        CharacterPrefab.RemoveByFile(file.Path);
                        break;
                    case ContentType.NPCConversations:
                        NPCConversation.RemoveByFile(file.Path);
                        break;
                    case ContentType.Jobs:
                        JobPrefab.RemoveByFile(file.Path);
                        break;
                    case ContentType.Item:
                        ItemPrefab.RemoveByFile(file.Path);
                        shouldRefreshFabricationRecipes = true;
                        break;
                    case ContentType.ItemAssembly:
                        ItemAssemblyPrefab.Remove(file.Path);
                        break;
                    case ContentType.Structure:
                        StructurePrefab.RemoveByFile(file.Path);
                        break;
                    case ContentType.Submarine:
                        shouldRefreshSubs = true;
                        break;
                    case ContentType.Text:
                        TextManager.RemoveTextPack(file.Path);
                        break;
                    case ContentType.Afflictions:
                        shouldRefreshAfflictions = true;
                        break;
                    case ContentType.RuinConfig:
                        shouldRefreshRuinGenerationParams = true;
                        break;
                    case ContentType.RandomEvents:
                        shouldRefreshScriptedEventSets = true;
                        break;
                    case ContentType.Missions:
                        shouldRefreshMissionPrefabs = true;
                        break;
                    case ContentType.LevelObjectPrefabs:
                        shouldRefreshLevelObjectPrefabs = true;
                        break;
                    case ContentType.LocationTypes:
                        shouldRefreshLocationTypes = true;
                        break;
                    case ContentType.MapGenerationParameters:
                        shouldRefreshMapGenerationParams = true;
                        break;
                    case ContentType.LevelGenerationParameters:
                        shouldRefreshLevelGenerationParams = true;
                        break;
#if CLIENT
                    case ContentType.Sounds:
                        shouldRefreshSoundPlayer = true;
                        break;
                    case ContentType.Particles:
                        GameMain.ParticleManager.RemovePrefabsByFile(file.Path);
                        break;
                    case ContentType.Decals:
                        GameMain.DecalManager.RemoveByFile(file.Path);
                        break;
#endif
                }

                UpdateContentPackageDirtyFlag(file);
            }
        }

        private void UpdateContentPackageDirtyFlag(ContentFile file)
        {
            switch (file.Type)
            {
                case ContentType.Character:
                case ContentType.NPCConversations:
                case ContentType.Jobs:
                case ContentType.Item:
                case ContentType.ItemAssembly:
                case ContentType.Structure:
                case ContentType.Submarine:
                case ContentType.Text:
                case ContentType.Afflictions:
                case ContentType.RuinConfig:
                case ContentType.RandomEvents:
                case ContentType.Missions:
                case ContentType.LevelObjectPrefabs:
                case ContentType.LocationTypes:
                case ContentType.MapGenerationParameters:
                case ContentType.LevelGenerationParameters:
                case ContentType.Sounds:
                case ContentType.Particles:
                case ContentType.Decals:
                case ContentType.Outpost:
                case ContentType.BackgroundCreaturePrefabs:
                case ContentType.ServerExecutable:
                case ContentType.None:
                    break; //do nothing here if the content type is supported
                default:
                    ContentPackageSelectionDirty = true;
                    ContentPackageSelectionDirtyNotification = true;
                    break;
            }
        }

        public void ReorderSelectedContentPackages<T>(Func<ContentPackage, T> orderFunction)
        {
            ContentPackage.List = ContentPackage.List
                                    .OrderByDescending(p => p.CorePackage)
                                    .ThenBy(orderFunction)
                                    .ToList();

            ContentPackage.SortContentPackages();

            CharacterPrefab.Prefabs.SortAll();
            AfflictionPrefab.Prefabs.SortAll();
            JobPrefab.Prefabs.SortAll();
            ItemPrefab.Prefabs.SortAll();
            CoreEntityPrefab.Prefabs.SortAll();
            ItemAssemblyPrefab.Prefabs.SortAll();
            StructurePrefab.Prefabs.SortAll();

            Submarine.RefreshSavedSubs();
            ItemPrefab.InitFabricationRecipes();
            RuinGeneration.RuinGenerationParams.ClearAll();
            ScriptedEventSet.LoadPrefabs();
            MissionPrefab.Init();
            LevelObjectPrefab.LoadAll();
            LocationType.Init();
            MapGenerationParams.Init();
            LevelGenerationParams.LoadPresets();

#if CLIENT
            GameMain.DecalManager.Prefabs.SortAll();
            GameMain.ParticleManager.Prefabs.SortAll();
            SoundPlayer.Init().ForEach(_ => { return; });
#endif
        }


        private HashSet<string> selectedContentPackagePaths = new HashSet<string>();

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

        public List<string> CompletedTutorialNames { get; private set; } = new List<string>();

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

        private bool showTutorialSkipWarning = true;
        public bool ShowTutorialSkipWarning
        {
            get { return showTutorialSkipWarning && CompletedTutorialNames.Count == 0; }
            set { showTutorialSkipWarning = value; }
        }

        public GameSettings()
        {
            ContentPackage.LoadAll();

            LoadDefaultConfig();

            if (WasGameUpdated)
            {
                UpdaterUtil.CleanOldFiles();
                WasGameUpdated = false;
                SaveNewDefaultConfig();
            }

            LoadPlayerConfig();

#if WINDOWS
            //TODO: enable on *nix when we move to .NET Core, it's implemented there
            modsFolderWatcher = new FileSystemWatcher("Mods");
            modsFolderWatcher.Filter = "*";
            modsFolderWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName;
            modsFolderWatcher.Created += OnModFolderUpdate;
            modsFolderWatcher.Deleted += OnModFolderUpdate;
            modsFolderWatcher.Renamed += OnModFolderUpdate;
            modsFolderWatcher.EnableRaisingEvents = true;
#endif
        }

#if WINDOWS
        private void OnModFolderUpdate(object sender, FileSystemEventArgs e)
        {
            if (SuppressModFolderWatcher || GameMain.Client != null) { return; }
            switch (e.ChangeType)
            {
                case WatcherChangeTypes.Created:
                    {
                        string cpPath = Path.GetFullPath(Path.Combine(e.FullPath, Steam.SteamManager.MetadataFileName)).CleanUpPath();
                        if (File.Exists(cpPath) && !ContentPackage.List.Any(cp => Path.GetFullPath(cp.Path).CleanUpPath() == cpPath))
                        {
                            var cp = new ContentPackage(cpPath);
                            ContentPackage.List.Add(cp);
                        }
                    }
                    break;
                case WatcherChangeTypes.Deleted:
                    {
                        string cpPath = Path.GetFullPath(Path.Combine(e.FullPath, Steam.SteamManager.MetadataFileName)).CleanUpPath();
                        var toRemove = ContentPackage.List.Where(cp => Path.GetFullPath(cp.Path).CleanUpPath() == cpPath).ToList();
                        var packagesToDeselect = GameMain.Config.SelectedContentPackages.Where(p => toRemove.Contains(p)).ToList();
                        foreach (var cp in packagesToDeselect)
                        {
                            if (cp.CorePackage)
                            {
                                GameMain.Config.SelectCorePackage(ContentPackage.List.Find(cpp => cpp.CorePackage && !toRemove.Contains(cpp)));
                            }
                            else
                            {
                                GameMain.Config.DeselectContentPackage(cp);
                            }
                        }

                        foreach (var cp in toRemove)
                        {
                            ContentPackage.List.Remove(cp);
                        }
                    }
                    break;
                case WatcherChangeTypes.Renamed:
                    {
                        RenamedEventArgs renameArgs = e as RenamedEventArgs;

                        string cpPath = Path.GetFullPath(Path.Combine(renameArgs.OldFullPath, Steam.SteamManager.MetadataFileName)).CleanUpPath();
                        var toRemove = ContentPackage.List.Where(cp => Path.GetFullPath(cp.Path).CleanUpPath() == cpPath).ToList();
                        foreach (var cp in toRemove)
                        {
                            GameMain.Config.DeselectContentPackage(cp);
                            ContentPackage.List.Remove(cp);
                        }

                        cpPath = Path.GetFullPath(Path.Combine(renameArgs.FullPath, Steam.SteamManager.MetadataFileName)).CleanUpPath();
                        if (File.Exists(cpPath) && !ContentPackage.List.Any(cp => Path.GetFullPath(cp.Path).CleanUpPath() == cpPath))
                        {
                            var cp = new ContentPackage(cpPath);
                            ContentPackage.List.Add(cp);
                        }
                    }
                    break;
            }
        }
#endif

        public void SetDefaultBindings(XDocument doc = null, bool legacy = false)
        {
            keyMapping = new KeyOrMouse[Enum.GetNames(typeof(InputType)).Length];
            keyMapping[(int)InputType.Run] = new KeyOrMouse(Keys.LeftShift);
            keyMapping[(int)InputType.Attack] = new KeyOrMouse(MouseButton.MiddleMouse);
            keyMapping[(int)InputType.Crouch] = new KeyOrMouse(Keys.LeftControl);
            keyMapping[(int)InputType.Grab] = new KeyOrMouse(Keys.G);
            keyMapping[(int)InputType.Health] = new KeyOrMouse(Keys.H);
            keyMapping[(int)InputType.Ragdoll] = new KeyOrMouse(Keys.Space);
            keyMapping[(int)InputType.Aim] = new KeyOrMouse(MouseButton.SecondaryMouse);

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
                keyMapping[(int)InputType.Use] = new KeyOrMouse(MouseButton.PrimaryMouse);
                keyMapping[(int)InputType.Shoot] = new KeyOrMouse(MouseButton.PrimaryMouse);
                keyMapping[(int)InputType.Select] = new KeyOrMouse(Keys.E);
                keyMapping[(int)InputType.Deselect] = new KeyOrMouse(Keys.E);
            }
            else
            {
                keyMapping[(int)InputType.Use] = new KeyOrMouse(Keys.E);
                keyMapping[(int)InputType.Select] = new KeyOrMouse(MouseButton.PrimaryMouse);
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
                                binding = new KeyOrMouse(MouseButton.SecondaryMouse);
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
                                binding = new KeyOrMouse(MouseButton.PrimaryMouse);
                            }
                            else
                            {
                                // Legacy support
                                var useKey = keyMapping[(int)InputType.Use];
                                if (useKey != null && useKey.MouseButton != MouseButton.None)
                                {
                                    binding = new KeyOrMouse(useKey.MouseButton);
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
                jobPreferences = new List<Pair<string, int>>();
                return;
            }

            bool resetLanguage = setLanguage || string.IsNullOrEmpty(Language);
            SetDefaultValues(resetLanguage);
            SetDefaultBindings(doc, legacy: false);

            MasterServerUrl = doc.Root.GetAttributeString("masterserverurl", MasterServerUrl);
            RemoteContentUrl = doc.Root.GetAttributeString("remotecontenturl", RemoteContentUrl);
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
                new XAttribute("remotecontenturl", RemoteContentUrl),
                new XAttribute("autocheckupdates", AutoCheckUpdates),
                new XAttribute("musicvolume", musicVolume),
                new XAttribute("soundvolume", soundVolume),
                new XAttribute("microphonevolume", microphoneVolume),
                new XAttribute("voicechatvolume", voiceChatVolume),
                new XAttribute("verboselogging", VerboseLogging),
                new XAttribute("savedebugconsolelogs", SaveDebugConsoleLogs),
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
                if (keyMapping[i].MouseButton == MouseButton.None)
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
                ShowTutorialSkipWarning = true;
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
                ShowTutorialSkipWarning = true;
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
            var packagesWithErrors = new List<ContentPackage>();
            SelectedContentPackages.Clear();
            foreach (string path in contentPackagePaths)
            {
                var matchingContentPackage = ContentPackage.List.Find(cp => System.IO.Path.GetFullPath(cp.Path).CleanUpPath() == path.CleanUpPath());

                if (matchingContentPackage == null)
                {
                    missingPackagePaths.Add(path);
                }
                else if (!matchingContentPackage.IsCompatible())
                {
                    DebugConsole.NewMessage(
                        $"Content package \"{matchingContentPackage.Name}\" is not compatible with this version of Barotrauma (game version: {GameMain.Version}, content package version: {matchingContentPackage.GameVersion})",
                        Color.Red);
                    incompatiblePackages.Add(matchingContentPackage);
                }
                else
                {
                    if (!matchingContentPackage.CheckErrors(out List<string> errorMessages))
                    {
                        DebugConsole.NewMessage(
                        $"Errors found in content package \"{matchingContentPackage.Name}\": " + string.Join(", ", errorMessages),
                        Color.Red);
                        packagesWithErrors.Add(matchingContentPackage);
                    }
                    //add content packages with errors as they are generally able to load most of their assets
                    SelectedContentPackages.Add(matchingContentPackage);
                }
            }

            EnsureCoreContentPackageSelected(gameLoaded: false);

            ContentPackage.SortContentPackages();
            TextManager.LoadTextPacks(SelectedContentPackages);

            foreach (ContentPackage contentPackage in SelectedContentPackages)
            {
                foreach (ContentFile file in contentPackage.Files)
                {
                    ToolBox.IsProperFilenameCase(file.Path);
                }
            }

            //save to get rid of the invalid selected packages in the config file
            if (missingPackagePaths.Count > 0 || incompatiblePackages.Count > 0 || packagesWithErrors.Count > 0) { SaveNewPlayerConfig(); }

            //display error messages after all content packages have been loaded
            //to make sure the package that contains text files has been loaded before we attempt to use TextManager
            foreach (string missingPackagePath in missingPackagePaths)
            {
                DebugConsole.ThrowError(TextManager.GetWithVariable("ContentPackageNotFound", "[packagepath]", missingPackagePath));
            }
            foreach (ContentPackage invalidPackage in packagesWithErrors)
            {
                DebugConsole.ThrowError(TextManager.GetWithVariable("ContentPackageHasErrors", "[packagename]", invalidPackage.Name), createMessageBox: true);
            }
            foreach (ContentPackage incompatiblePackage in incompatiblePackages)
            {
                DebugConsole.ThrowError(TextManager.GetWithVariables(incompatiblePackage.GameVersion <= new Version(0, 0, 0, 0) ? "IncompatibleContentPackageUnknownVersion" : "IncompatibleContentPackage",
                    new string[3] { "[packagename]", "[packageversion]", "[gameversion]" }, new string[3] { incompatiblePackage.Name, incompatiblePackage.GameVersion.ToString(), GameMain.Version.ToString() }),
                    createMessageBox: true);
            }
        }

        public void EnsureCoreContentPackageSelected(bool gameLoaded=true)
        {
            if (SelectedContentPackages.Any(cp => cp.CorePackage)) { return; }

            if (GameMain.VanillaContent != null)
            {
                if (gameLoaded)
                {
                    SelectContentPackage(GameMain.VanillaContent);
                }
                else
                {
                    SelectedContentPackages.Add(GameMain.VanillaContent);
                }
            }
            else
            {
                var availablePackage = ContentPackage.List.FirstOrDefault(cp => cp.IsCompatible() && cp.CorePackage);
                if (availablePackage != null)
                {
                    if (gameLoaded)
                    {
                        SelectContentPackage(availablePackage);
                    }
                    else
                    {
                        SelectedContentPackages.Add(availablePackage);
                    }
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
                new XAttribute("usesteammatchmaking", UseSteamMatchmaking),
                new XAttribute("quickstartsub", QuickStartSubmarineName),
                new XAttribute("requiresteamauthentication", RequireSteamAuthentication),
                new XAttribute("autoupdateworkshopitems", AutoUpdateWorkshopItems),
                new XAttribute("pauseonfocuslost", PauseOnFocusLost),
                new XAttribute("aimassistamount", aimAssistAmount),
                new XAttribute("enablemouselook", EnableMouseLook),
                new XAttribute("chatopen", ChatOpen),
                new XAttribute("crewmenuopen", CrewMenuOpen),
                new XAttribute("campaigndisclaimershown", CampaignDisclaimerShown),
                new XAttribute("editordisclaimershown", EditorDisclaimerShown),
                new XAttribute("tutorialskipwarning", ShowTutorialSkipWarning));

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
            UseSteamMatchmaking = doc.Root.GetAttributeBool("usesteammatchmaking", UseSteamMatchmaking);
            RequireSteamAuthentication = doc.Root.GetAttributeBool("requiresteamauthentication", RequireSteamAuthentication);
            EnableSplashScreen = doc.Root.GetAttributeBool("enablesplashscreen", EnableSplashScreen);
            PauseOnFocusLost = doc.Root.GetAttributeBool("pauseonfocuslost", PauseOnFocusLost);
            AimAssistAmount = doc.Root.GetAttributeFloat("aimassistamount", AimAssistAmount);
            EnableMouseLook = doc.Root.GetAttributeBool("enablemouselook", EnableMouseLook);
            CrewMenuOpen = doc.Root.GetAttributeBool("crewmenuopen", CrewMenuOpen);
            ChatOpen = doc.Root.GetAttributeBool("chatopen", ChatOpen);
            CampaignDisclaimerShown = doc.Root.GetAttributeBool("campaigndisclaimershown", CampaignDisclaimerShown);
            EditorDisclaimerShown = doc.Root.GetAttributeBool("editordisclaimershown", EditorDisclaimerShown);
            ShowTutorialSkipWarning = doc.Root.GetAttributeBool("tutorialskipwarning", true);
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
                string voiceSettingStr = audioSettings.GetAttributeString("voicesetting", "");
                if (Enum.TryParse(voiceSettingStr, out VoiceMode voiceSetting))
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

                if (int.TryParse(attribute.Value.ToString(), out int mouseButtonInt))
                {
                    keyMapping[(int)inputType] = new KeyOrMouse((MouseButton)mouseButtonInt);
                }
                else if (Enum.TryParse(attribute.Value.ToString(), true, out MouseButton mouseButton))
                {
                    keyMapping[(int)inputType] = new KeyOrMouse(mouseButton);
                }
                else if (Enum.TryParse(attribute.Value.ToString(), true, out Keys key))
                {
                    keyMapping[(int)inputType] = new KeyOrMouse(key);
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
        public string KeyBindText(InputType inputType)
        {
            KeyOrMouse bind = keyMapping[(int)inputType];

            if (bind.MouseButton != MouseButton.None)
            {
                switch (bind.MouseButton)
                {
                    case MouseButton.PrimaryMouse:
                        return PlayerInput.MouseButtonsSwapped() ? TextManager.Get("input.rightmouse") : TextManager.Get("input.leftmouse");
                    case MouseButton.SecondaryMouse:
                        return PlayerInput.MouseButtonsSwapped() ? TextManager.Get("input.leftmouse") : TextManager.Get("input.rightmouse");
                    default:
                        return TextManager.Get("input." + bind.MouseButton.ToString().ToLowerInvariant());

                }
            }

            return bind.ToString();
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
            ChromaticAberrationEnabled = true;
            PauseOnFocusLost = true;
            MuteOnFocusLost = false;
            UseDirectionalVoiceChat = true;
            VoiceSetting = VoiceMode.Disabled;
            VoiceCaptureDevice = null;
            NoiseGateThreshold = -45;
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
