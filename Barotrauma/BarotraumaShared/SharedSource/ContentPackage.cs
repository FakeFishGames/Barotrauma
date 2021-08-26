using System;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;
using Barotrauma.Extensions;
using Barotrauma.Steam;

namespace Barotrauma
{
    public enum ContentType
    {
        None, 
        Submarine,
        Jobs, 
        Item, 
        ItemAssembly,
        Character,
        Structure,
        Outpost,
        OutpostModule,
        OutpostConfig,
        BeaconStation,
        NPCSets,
        Factions,
        Text,
        ServerExecutable,
        LocationTypes,
        MapGenerationParameters,
        LevelGenerationParameters,
        CaveGenerationParameters,
        LevelObjectPrefabs,
        RandomEvents,
        Missions, 
        BackgroundCreaturePrefabs,
        Sounds,
        RuinConfig,
        Particles,
        Decals,
        NPCConversations,
        Afflictions,
        Tutorials,
        UIStyle,
        TraitorMissions,
        EventManagerSettings,
        Orders,
        SkillSettings,
        Wreck,
        Corpses,
        WreckAIConfig,
        UpgradeModules,
        MapCreature,
        EnemySubmarine,
        Talents,
        TalentTrees,
    }

    public class ContentPackage
    {
        public static string Folder = "Data/ContentPackages/";

        private static readonly List<ContentPackage> regularPackages = new List<ContentPackage>();
        public static IReadOnlyList<ContentPackage> RegularPackages
        {
            get { return regularPackages; }
        }

        private static readonly List<ContentPackage> corePackages = new List<ContentPackage>();
        public static IReadOnlyList<ContentPackage> CorePackages
        {
            get { return corePackages; }
        }

        public static IEnumerable<ContentPackage> AllPackages
        {
            get { return corePackages.Concat(regularPackages); }
        }
        
        //these types of files are included in the MD5 hash calculation,
        //meaning that the players must have the exact same files to play together
        public static HashSet<ContentType> MultiplayerIncompatibleContent { get; private set; } = new HashSet<ContentType>
        {
            ContentType.Jobs,
            ContentType.Item,
            ContentType.Character,
            ContentType.Structure,
            ContentType.LocationTypes,
            ContentType.NPCSets,
            ContentType.Factions,
            ContentType.MapGenerationParameters,
            ContentType.LevelGenerationParameters,
            ContentType.CaveGenerationParameters,
            ContentType.Missions,
            ContentType.LevelObjectPrefabs,
            ContentType.RuinConfig,
            ContentType.Outpost,
            ContentType.OutpostModule,
            ContentType.OutpostConfig,
            ContentType.Wreck,
            ContentType.WreckAIConfig,
            ContentType.BeaconStation,
            ContentType.Afflictions,
            ContentType.Orders,
            ContentType.Corpses,
            ContentType.UpgradeModules,
            ContentType.MapCreature,
            ContentType.EnemySubmarine,
            ContentType.Talents,
        };

        //at least one file of each these types is required in core content packages
        private static readonly HashSet<ContentType> corePackageRequiredFiles = new HashSet<ContentType>
        {
            ContentType.Jobs,
            ContentType.Item,
            ContentType.Character,
            ContentType.Structure,
            //TODO: there needs to be either outpost files or outpost generation parameters, both aren't required
            //ContentType.Outpost,
            //ContentType.OutpostGenerationParams,
            ContentType.Factions,
            ContentType.Wreck,
            ContentType.WreckAIConfig,
            ContentType.BeaconStation,
            ContentType.Text,
            ContentType.ServerExecutable,
            ContentType.LocationTypes,
            ContentType.MapGenerationParameters,
            ContentType.LevelGenerationParameters,
            ContentType.CaveGenerationParameters,
            ContentType.RandomEvents,
            ContentType.Missions,
            ContentType.RuinConfig,
            ContentType.Afflictions,
            ContentType.UIStyle,
            ContentType.EventManagerSettings,
            ContentType.Orders,
            ContentType.Corpses,
            ContentType.UpgradeModules,
            ContentType.EnemySubmarine,
            ContentType.Talents,
        };

        public static IEnumerable<ContentType> CorePackageRequiredFiles
        {
            get { return corePackageRequiredFiles; }
        }

        public static bool IngameModSwap = false;

        public string Name { get; set; } = string.Empty;

        public string Path
        {
            get;
            set;
        }

        public ulong SteamWorkshopId;
        public DateTime? InstallTime;

        public bool HideInWorkshopMenu
        {
            get;
            private set;
        }

        private Md5Hash md5Hash;
        public Md5Hash MD5hash
        {
            get 
            {
                if (md5Hash == null)
                {
                    //TODO: before re-enabling content package hash caching, make sure the hash gets recalculated when any file in the content package changes, not just when the filelist.xml changes.
                    /*md5Hash = Md5Hash.FetchFromCache(Path);
                    if (md5Hash == null)
                    {
                        CalculateHash();
                        md5Hash.SaveToCache(Path);
                    }*/
                    CalculateHash();
                }
                return md5Hash; 
            }
        }

        //core packages are content packages that are required for the game to work
        //e.g. they include the executable, some location types, level generation params and other files the game won't work without
        //one (and only one) core package must always be selected
        private bool isCorePackage;
        public bool IsCorePackage
        {
            get { return isCorePackage; }
            set
            {
                isCorePackage = value;
                if (isCorePackage && regularPackages.Contains(this))
                {
                    corePackages.AddOnMainThread(this);
                    regularPackages.RemoveOnMainThread(this);
                }
                else if (!isCorePackage && corePackages.Contains(this))
                {
                    regularPackages.AddOnMainThread(this);
                    corePackages.RemoveOnMainThread(this);
                }
            }
        }

        public Version GameVersion
        {
            get; set;
        }


        private readonly List<ContentFile> files;
        private readonly List<ContentFile> filesToAdd;
        private readonly List<ContentFile> filesToRemove;

        public IReadOnlyList<ContentFile> Files
        {
            get { return files; }
        }

        public IEnumerable<ContentFile> FilesUnsaved
        {
            get { return files.Where(f => !filesToRemove.Contains(f)).Concat(filesToAdd); }
        }

        public IReadOnlyList<ContentFile> FilesToAdd
        {
            get { return filesToAdd; }
        }
        
        public IReadOnlyList<ContentFile> FilesToRemove
        {
            get { return filesToRemove; }
        }

        public bool HasMultiplayerIncompatibleContent
        {
            get { return Files.Any(f => MultiplayerIncompatibleContent.Contains(f.Type)); }
        }

        public bool IsCorrupt
        {
            get;
            private set;
        }

        private ContentPackage()
        {
            files = new List<ContentFile>();
            filesToAdd = new List<ContentFile>();
            filesToRemove = new List<ContentFile>();
        }

        public ContentPackage(string filePath, string setPath = "")
            : this()
        {
            filePath = filePath.CleanUpPath();
            if (!string.IsNullOrEmpty(setPath)) { setPath = setPath.CleanUpPath(); }
            XDocument doc = XMLExtensions.TryLoadXml(filePath);

            Path = setPath == string.Empty ? filePath : setPath;

            if (doc?.Root == null)
            {
                DebugConsole.ThrowError("Couldn't load content package \"" + filePath + "\"!"); 
                IsCorrupt = true;
                return;
            }

            Name = doc.Root.GetAttributeString("name", "");
            HideInWorkshopMenu = doc.Root.GetAttributeBool("hideinworkshopmenu", false);
            isCorePackage = doc.Root.GetAttributeBool("corepackage", false);
            SteamWorkshopId = doc.Root.GetAttributeUInt64("steamworkshopid", 0);
            string workshopUrl = doc.Root.GetAttributeString("steamworkshopurl", "");
            if (!string.IsNullOrEmpty(workshopUrl))
            {
                SteamWorkshopId = SteamManager.GetWorkshopItemIDFromUrl(workshopUrl);
            }
            string versionStr = doc.Root.GetAttributeString("gameversion", "0.0.0.0");
            try
            {
                GameVersion = new Version(versionStr);
            }
            catch
            {
                DebugConsole.ThrowError($"Invalid version number in content package \"{Name}\" ({versionStr}).");
                GameVersion = GameMain.Version;
            }
            if (doc.Root.Attribute("installtime") != null)
            {
                InstallTime = ToolBox.Epoch.ToDateTime(doc.Root.GetAttributeUInt("installtime", 0));
            }
            
            List<string> errorMsgs = new List<string>();
            foreach (XElement subElement in doc.Root.Elements())
            {
                if (subElement.Name.ToString().Equals("executable", StringComparison.OrdinalIgnoreCase)) { continue; }
                if (!Enum.TryParse(subElement.Name.ToString(), true, out ContentType type))
                {
                    errorMsgs.Add("Error in content package \"" + Name + "\" - \"" + subElement.Name.ToString() + "\" is not a valid content type.");
                    type = ContentType.None;
                }
                files.Add(new ContentFile(subElement.GetAttributeString("file", ""), type, this));
            }

            if (Files.Count == 0)
            {
                //no files defined, find a submarine in here
                //because somehow people have managed to upload
                //mods without contentfile definitions
                string folder = System.IO.Path.GetDirectoryName(filePath);
                if (File.Exists(System.IO.Path.Combine(folder, Name+".sub")))
                {
                    files.Add(new ContentFile(System.IO.Path.Combine(folder, Name + ".sub"), ContentType.Submarine, this));
                }
                else
                {
                    errorMsgs.Add("Error in content package \"" + Name + "\" - no content files defined.");
                }
            }

            bool compatible = IsCompatible();
            //If we know that the package is not compatible, don't display error messages.
            if (compatible)
            {
                foreach (string errorMsg in errorMsgs)
                {
                    DebugConsole.ThrowError(errorMsg);
                }
            }
        }

        private bool? hasErrors;
        public bool HasErrors
        {
            get
            {
                if (!hasErrors.HasValue)
                {
                    hasErrors = !CheckErrors(out _);
                }
                return hasErrors.Value;
            }
        }

        private List<string> errorMessages;
        public IEnumerable<string> ErrorMessages
        {
            get
            {
                if (errorMessages == null) { CheckErrors(out _); }
                return errorMessages;
            }
        }

        public override string ToString()
        {
            return Name;
        }

        public bool IsCompatible()
        {
            if (Files.All(f => f.Type == ContentType.Submarine))
            {
                return true;
            }

            //content package compatibility checks were added in 0.8.9.1
            //v0.8.9.1 is not compatible with older content packages
            if (GameVersion < new Version(0, 8, 9, 1))
            {
                return false;
            }

            //do additional checks here if later versions add changes that break compatibility

            return true;
        }

        public bool ContainsRequiredCorePackageFiles()
        {
            return corePackageRequiredFiles.All(fileType => Files.Any(file => file.Type == fileType));
        }

        public bool ContainsRequiredCorePackageFiles(out List<ContentType> missingContentTypes)
        {
            missingContentTypes = new List<ContentType>();
            foreach (ContentType contentType in corePackageRequiredFiles)
            {
                if (!Files.Any(file => file.Type == contentType))
                {
                    missingContentTypes.Add(contentType);
                }
            }
            return missingContentTypes.Count == 0;
        }

        public bool CheckErrors(out List<string> errorMessages)
        {
            this.errorMessages = errorMessages = new List<string>();
            foreach (ContentFile file in Files)
            {
                switch (file.Type)
                {
                    case ContentType.ServerExecutable:
                    case ContentType.None:
                    case ContentType.Outpost:
                    case ContentType.OutpostModule:
                    case ContentType.Submarine:
                    case ContentType.Wreck:
                    case ContentType.BeaconStation:
                    case ContentType.EnemySubmarine:
                        break;
                    default:
                        try
                        {
                            XDocument.Load(file.Path);
                        }
                        catch (Exception e)
                        {
                            if (TextManager.Initialized)
                            {
                                errorMessages.Add(TextManager.GetWithVariables("xmlfileinvalid",
                                    new string[] { "[filepath]", "[errormessage]" },
                                    new string[] { file.Path, e.Message }));
                            }
                            else
                            {
                                errorMessages.Add($"XML File Invalid. PATH: {file.Path}, ERROR: {e.Message}");
#if DEBUG
                                throw;
#endif
                            }
                        }
                        break;
                }
            }

            if (IsCorePackage && !ContainsRequiredCorePackageFiles(out List<ContentType> missingContentTypes))
            {
                errorMessages.Add(TextManager.GetWithVariables("ContentPackageCantMakeCorePackage", 
                    new string[2] { "[packagename]", "[missingfiletypes]" },
                    new string[2] { Name, string.Join(", ", missingContentTypes) }, 
                    new bool[2] { false, true }));
            }
            VerifyFiles(out List<string> missingFileMessages);

            errorMessages.AddRange(missingFileMessages);
            hasErrors = errorMessages.Count > 0;
            return !hasErrors.Value;
        }

        /// <summary>
        /// Make sure all the files defined in the content package are present
        /// </summary>
        /// <returns></returns>
        public bool VerifyFiles(out List<string> errorMessages)
        {
            errorMessages = new List<string>();
            foreach (ContentFile file in Files)
            {
                //TODO: determine executable extension on platform and check for the presence of the executables
                if (file.Type == ContentType.ServerExecutable) { continue; }

                if (!File.Exists(file.Path))
                {
                    errorMessages.Add("File \"" + file.Path + "\" not found.");
                    continue;
                }
            }

            return errorMessages.Count == 0;
        }

        public static ContentPackage CreatePackage(string name, string path, bool corePackage)
        {
            ContentPackage newPackage = new ContentPackage()
            {
                Name = name,
                Path = path,
                isCorePackage = corePackage,
                GameVersion = GameMain.Version
            };

            return newPackage;
        }

        public ContentFile AddFile(string path, ContentType type)
        {
            if (Files.Concat(FilesToAdd).Any(file => file.Path == path && file.Type == type)) return null;

            ContentFile cf = new ContentFile(path, type)
            {
                ContentPackage = this
            };
            filesToAdd.Add(cf);

            return cf;
        }

        public void AddFile(ContentFile file)
        {
            if (filesToRemove.Contains(file)) { filesToRemove.Remove(file); }
            if (Files.Concat(FilesToAdd).Any(f => f.Path == file.Path && f.Type == file.Type)) return;

            filesToAdd.Add(file);
        }

        public void RemoveFile(ContentFile file)
        {
            if (filesToAdd.Contains(file)) { filesToAdd.Remove(file); }
            if (files.Contains(file) && !filesToRemove.Contains(file)) { filesToRemove.Add(file); }
        }

        public void Save(string filePath, bool reload = true)
        {
            var packagesToDeselect = corePackages.Concat(regularPackages).Where(p => p.Path.CleanUpPath() == Path.CleanUpPath()).ToList();
            bool refreshFiles = false;

            if (packagesToDeselect.Any())
            {
                foreach (var p in packagesToDeselect)
                {
                    if (p.IsCorePackage)
                    {
                        if (GameMain.Config.CurrentCorePackage == p)
                        {
                            refreshFiles = true;
                        }
                        corePackages.RemoveOnMainThread(p);
                    }
                    else
                    {
                        if (GameMain.Config.EnabledRegularPackages.Contains(p))
                        {
                            refreshFiles = true;
                        }
                        regularPackages.RemoveOnMainThread(p);
                    }
                }
                if (IsCorePackage)
                {
                    corePackages.AddOnMainThread(this);
                }
                else
                {
                    regularPackages.AddOnMainThread(this);
                }

                if (refreshFiles)
                {
                    GameMain.Config.DisableContentPackageItems(filesToRemove);
                    GameMain.Config.EnableContentPackageItems(filesToAdd);
                    GameMain.Config.RefreshContentPackageItems(filesToRemove.Concat(filesToAdd).Distinct());
                }
            }
            files.RemoveAll(f => filesToRemove.Contains(f));
            files.AddRange(filesToAdd);
            filesToRemove.Clear(); filesToAdd.Clear();

            XDocument doc = new XDocument();
            doc.Add(new XElement("contentpackage",
                new XAttribute("name", Name),
                new XAttribute("path", Path.CleanUpPathCrossPlatform(correctFilenameCase: false)),
                new XAttribute("corepackage", IsCorePackage)));

            doc.Root.Add(new XAttribute("gameversion", GameVersion.ToString()));

            if (SteamWorkshopId != 0)
            {
                doc.Root.Add(new XAttribute("steamworkshopid", SteamWorkshopId.ToString()));
            }

            if (InstallTime != null)
            {
                doc.Root.Add(new XAttribute("installtime", ToolBox.Epoch.FromDateTime(InstallTime.Value)));
            }

            foreach (ContentFile file in Files)
            {
                doc.Root.Add(new XElement(file.Type.ToString(), new XAttribute("file", file.Path.CleanUpPathCrossPlatform())));
            }

            doc.SaveSafe(filePath);
        }

        public void CalculateHash(bool logging = false)
        {
            List<byte[]> hashes = new List<byte[]>();

            if (logging)
            {
                DebugConsole.NewMessage("****************************** Calculating cp hash " + Name);
            }

            foreach (ContentFile file in Files)
            {
                if (!MultiplayerIncompatibleContent.Contains(file.Type)) { continue; }

                try
                {
                    var hash = CalculateFileHash(file);
                    if (logging)
                    {
                        var fileMd5 = new Md5Hash(hash);
                        DebugConsole.NewMessage("   " + file.Path + ": " + fileMd5.Hash);
                    }                    
                    hashes.Add(hash);
                }

                catch (Exception e)
                {
                    DebugConsole.ThrowError($"Error while calculating the MD5 hash of the content package \"{Name}\" (file path: {Path}). The content package may be corrupted. You may want to delete or reinstall the package.", e);
                    break;
                }             
            }
            
            byte[] bytes = new byte[hashes.Count * 16];
            for (int i = 0; i < hashes.Count; i++)
            {
                hashes[i].CopyTo(bytes, i * 16);
            }

            md5Hash = new Md5Hash(bytes);
            if (logging)
            {
                DebugConsole.NewMessage("****************************** Package hash: " + md5Hash.Hash);
            }
        }

        private byte[] CalculateFileHash(ContentFile file)
        {
            using (MD5 md5 = MD5.Create())
            {
                List<string> filePaths = new List<string> { file.Path };
                List<byte> data = new List<byte>();

                switch (file.Type)
                {
                    case ContentType.Character:
                        XDocument doc = XMLExtensions.TryLoadXml(file.Path);
                        var ragdollFolder = RagdollParams.GetFolder(doc, file.Path);
                        if (Directory.Exists(ragdollFolder))
                        {
                            Directory.GetFiles(ragdollFolder, "*.xml").ForEach(f => filePaths.Add(f));
                        }
                        var animationFolder = AnimationParams.GetFolder(doc, file.Path);
                        if (Directory.Exists(animationFolder))
                        {
                            Directory.GetFiles(animationFolder, "*.xml").ForEach(f => filePaths.Add(f));
                        }
                        break;
                }

                if (filePaths.Count > 1)
                {
                    using (MD5 tempMd5 = MD5.Create())
                    {
                        filePaths = filePaths.OrderBy(f => ToolBox.StringToUInt32Hash(f.CleanUpPathCrossPlatform(true).ToLowerInvariant(), tempMd5)).ToList();
                    }
                }

                foreach (string filePath in filePaths)
                {
                    if (!File.Exists(filePath)) continue;

                    using (var stream = File.OpenRead(filePath))
                    {
                        byte[] fileData = new byte[stream.Length];
                        stream.Read(fileData, 0, (int)stream.Length);
                        if (filePath.EndsWith(".xml", true, System.Globalization.CultureInfo.InvariantCulture))
                        {
                            string text = System.Text.Encoding.UTF8.GetString(fileData);
                            text = text.Replace("\n", "").Replace("\r", "").Replace("\\","/");
                            fileData = System.Text.Encoding.UTF8.GetBytes(text);
                        }
                        data.AddRange(fileData);
                    }
                }
                return md5.ComputeHash(data.ToArray());
            }
        }

        public static bool IsModFilePathAllowed(ContentFile contentFile)
        {
            string path = contentFile.Path;
            return IsModFilePathAllowed(path);
        }
        /// <summary>
        /// Returns whether mods are allowed to install a file into the specified path.
        /// Currently mods are only allowed to install files into the Mods folder.
        /// The only exception to this rule is the Vanilla content package.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static bool IsModFilePathAllowed(string path)
        {
            if (GameMain.VanillaContent.Files.Any(f => string.Equals(System.IO.Path.GetFullPath(f.Path).CleanUpPath(),
                                                                     System.IO.Path.GetFullPath(path).CleanUpPath(),
                                                                     StringComparison.InvariantCultureIgnoreCase)))
            {
                //file is in vanilla package, this is allowed
                return true;
            }

            while (true)
            {
                string temp = Barotrauma.IO.Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(temp)) { break; }
                path = temp;
            }
            return path == "Mods";
        }

        /// <summary>
        /// Returns all xml files from all the loaded content packages.
        /// </summary>
        public static IEnumerable<string> GetAllContentFiles(IEnumerable<ContentPackage> contentPackages)
        {
            return contentPackages.SelectMany(f => f.Files).Select(f => f.Path).Where(p => p.EndsWith(".xml"));
        }

        public static IEnumerable<ContentFile> GetFilesOfType(IEnumerable<ContentPackage> contentPackages, ContentType type)
        {
            return contentPackages.SelectMany(f => f.Files).Where(f => f.Type == type);
        }
        public static IEnumerable<ContentFile> GetFilesOfType(IEnumerable<ContentPackage> contentPackages, params ContentType[] types)
        {
            return contentPackages.SelectMany(f => f.Files).Where(f => types.Contains(f.Type));
        }

        public IEnumerable<string> GetFilesOfType(ContentType type)
        {
            return Files.Where(f => f.Type == type).Select(f => f.Path);
        }
        
        public static void AddPackage(ContentPackage newPackage)
        {
            if (corePackages.Concat(regularPackages).Any(p => p.Name.Equals(newPackage.Name, StringComparison.OrdinalIgnoreCase))) 
            {
                DebugConsole.ThrowError($"Attempted to add \"{newPackage.Name}\" more than once!\n{Environment.StackTrace}");
            }
            if (newPackage.IsCorePackage) 
            { 
                corePackages.AddOnMainThread(newPackage); 
            }
            else 
            { 
                regularPackages.AddOnMainThread(newPackage); 
            }
        }

        public static void RemovePackage(ContentPackage package)
        {
            if (package.IsCorePackage) { corePackages.RemoveOnMainThread(package); }
            else { regularPackages.RemoveOnMainThread(package); }
        }

        public static void LoadAll()
        {
            string folder = Folder;
            if (!Directory.Exists(folder))
            {
                try
                {
                    Directory.CreateDirectory(folder);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Failed to create directory \"" + folder + "\"", e);
                    return;
                }
            }

            IEnumerable<string> files = Directory.GetFiles(folder, "*.xml");

            corePackages.ClearOnMainThread();
            var prevRegularPackages = regularPackages.Select(p => p.Name.ToLowerInvariant()).ToList();
            regularPackages.ClearOnMainThread();

            foreach (string filePath in files)
            {
                var newPackage = new ContentPackage(filePath);
                if (!newPackage.IsCorrupt) { AddPackage(newPackage); }
            }

            IEnumerable<string> modDirectories = Directory.GetDirectories("Mods");
            foreach (string modDirectory in modDirectories)
            {
                if (Barotrauma.IO.Path.GetFileName(modDirectory.TrimEnd(Barotrauma.IO.Path.DirectorySeparatorChar)) == "ExampleMod") { continue; }
                string modFilePath = Barotrauma.IO.Path.Combine(modDirectory, Steam.SteamManager.MetadataFileName);
                string copyingFilePath = Barotrauma.IO.Path.Combine(modDirectory, Steam.SteamManager.CopyIndicatorFileName);
                if (File.Exists(copyingFilePath))
                {
                    //this mod didn't clean up its copying file; assume it's corrupted and delete it
                    Directory.Delete(modDirectory, true);
                }
                else if (File.Exists(modFilePath))
                {
                    var newPackage = new ContentPackage(modFilePath);
                    if (!newPackage.IsCorrupt)
                    {
                        AddPackage(newPackage);
                    }
                }
            }
            SortContentPackages(p => prevRegularPackages.IndexOf(p.Name.ToLowerInvariant()));
            GameMain.Config?.SortContentPackages();
        }

        public static void SortContentPackages<T>(Func<ContentPackage, T> order, bool refreshAll = false, GameSettings config = null)
        {
            var ordered = regularPackages
                .OrderBy(p => order(p))
                .ThenBy(p => regularPackages.IndexOf(p))
                .ToList();
            regularPackages.ClearOnMainThread(); regularPackages.AddRangeOnMainThread(ordered);
            (config ?? GameMain.Config)?.SortContentPackages(refreshAll);
        }

        public void Delete()
        {
            try
            {
                if (IsCorePackage)
                {
                    corePackages.RemoveOnMainThread(this);
                    if (GameMain.Config.CurrentCorePackage == this) { GameMain.Config.AutoSelectCorePackage(null); }
                }
                else
                {
                    regularPackages.RemoveOnMainThread(this);
                    if (GameMain.Config.EnabledRegularPackages.Contains(this)) { GameMain.Config.DisableRegularPackage(this); }
                }
                GameMain.Config.SaveNewPlayerConfig();
                File.Delete(Path);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to delete content package \"" + Name + "\".", e);
                return;
            }
        }
    }

    public class ContentFile
    {
        public string Path;
        public ContentType Type;

        public ContentPackage ContentPackage;

        public ContentFile(string path, ContentType type, ContentPackage contentPackage = null)
        {
            Path = path.CleanUpPath();

            Type = type;
            ContentPackage = contentPackage;
        }

        public override string ToString()
        {
            return Path;
        }
    }

}
