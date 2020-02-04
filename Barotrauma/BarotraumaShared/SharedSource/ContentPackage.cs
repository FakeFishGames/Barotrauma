using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;
using Barotrauma.Extensions;

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
        Text,
        Executable,
        ServerExecutable,
        LocationTypes,
        MapGenerationParameters,
        LevelGenerationParameters,
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
        SkillSettings
    }

    public class ContentPackage
    {
        public static string Folder = "Data/ContentPackages/";

        public static List<ContentPackage> List = new List<ContentPackage>();
        
        //these types of files are included in the MD5 hash calculation,
        //meaning that the players must have the exact same files to play together
        private static HashSet<ContentType> multiplayerIncompatibleContent = new HashSet<ContentType>
        {
            ContentType.Jobs,
            ContentType.Item,
            ContentType.Character,
            ContentType.Structure,
            ContentType.LocationTypes,
            ContentType.MapGenerationParameters,
            ContentType.LevelGenerationParameters,
            ContentType.Missions,
            ContentType.LevelObjectPrefabs,
            ContentType.RuinConfig,
            ContentType.Outpost,
            ContentType.Afflictions,
            ContentType.Orders
        };

        //at least one file of each these types is required in core content packages
        private static HashSet<ContentType> corePackageRequiredFiles = new HashSet<ContentType>
        {
            ContentType.Jobs,
            ContentType.Item,
            ContentType.Character,
            ContentType.Structure,
            ContentType.Outpost,
            ContentType.Text,
            ContentType.Executable,
            ContentType.ServerExecutable,
            ContentType.LocationTypes,
            ContentType.MapGenerationParameters,
            ContentType.LevelGenerationParameters,
            ContentType.RandomEvents,
            ContentType.Missions,
            ContentType.RuinConfig,
            ContentType.Afflictions,
            ContentType.UIStyle,
            ContentType.EventManagerSettings,
            ContentType.Orders
        };

        public static IEnumerable<ContentType> CorePackageRequiredFiles
        {
            get { return corePackageRequiredFiles; }
        }

        public static bool IngameModSwap = false;
        
        public string Name { get; set; }

        public string Path
        {
            get;
            set;
        }

        public string SteamWorkshopUrl;
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
                    md5Hash = Md5Hash.FetchFromCache(Path);
                    if (md5Hash == null)
                    {
                        CalculateHash();
                        md5Hash.SaveToCache(Path);
                    }
                }
                return md5Hash; 
            }
        }

        //core packages are content packages that are required for the game to work
        //e.g. they include the executable, some location types, level generation params and other files the game won't work without
        //one (and only one) core package must always be selected
        public bool CorePackage
        {
            get;
            set;
        }

        public Version GameVersion
        {
            get; set;
        }

        public List<ContentFile> Files;

        public bool HasMultiplayerIncompatibleContent
        {
            get { return Files.Any(f => multiplayerIncompatibleContent.Contains(f.Type)); }
        }

        private ContentPackage()
        {
            Files = new List<ContentFile>();
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
                return;
            }

            Name = doc.Root.GetAttributeString("name", "");
            HideInWorkshopMenu = doc.Root.GetAttributeBool("hideinworkshopmenu", false);
            CorePackage = doc.Root.GetAttributeBool("corepackage", false);
            SteamWorkshopUrl = doc.Root.GetAttributeString("steamworkshopurl", "");
            GameVersion = new Version(doc.Root.GetAttributeString("gameversion", "0.0.0.0"));
            if (doc.Root.Attribute("installtime") != null)
            {
                InstallTime = ToolBox.Epoch.ToDateTime(doc.Root.GetAttributeUInt("installtime", 0));
            }
            
            List<string> errorMsgs = new List<string>();
            foreach (XElement subElement in doc.Root.Elements())
            {
                if (!Enum.TryParse(subElement.Name.ToString(), true, out ContentType type))
                {
                    errorMsgs.Add("Error in content package \"" + Name + "\" - \"" + subElement.Name.ToString() + "\" is not a valid content type.");
                    type = ContentType.None;
                }
                Files.Add(new ContentFile(subElement.GetAttributeString("file", ""), type, this));
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
                    case ContentType.Executable:
                    case ContentType.ServerExecutable:
                    case ContentType.None:
                    case ContentType.Outpost:
                    case ContentType.Submarine:
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

            if (CorePackage && !ContainsRequiredCorePackageFiles(out List<ContentType> missingContentTypes))
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
                if (file.Type == ContentType.Executable) { continue; }
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
                CorePackage = corePackage,
                GameVersion = GameMain.Version
            };

            return newPackage;
        }

        public ContentFile AddFile(string path, ContentType type)
        {
            if (Files.Find(file => file.Path == path && file.Type == type) != null) return null;

            ContentFile cf = new ContentFile(path, type);
            Files.Add(cf);

            return cf;
        }

        public void RemoveFile(ContentFile file)
        {
            Files.Remove(file);
        }

        public void Save(string filePath)
        {
            XDocument doc = new XDocument();
            doc.Add(new XElement("contentpackage",
                new XAttribute("name", Name),
                new XAttribute("path", Path.CleanUpPathCrossPlatform(correctFilenameCase: false)),
                new XAttribute("corepackage", CorePackage)));


            doc.Root.Add(new XAttribute("gameversion", GameVersion.ToString()));

            if (!string.IsNullOrEmpty(SteamWorkshopUrl))
            {
                doc.Root.Add(new XAttribute("steamworkshopurl", SteamWorkshopUrl));
            }

            if (InstallTime != null)
            {
                doc.Root.Add(new XAttribute("installtime", ToolBox.Epoch.FromDateTime(InstallTime.Value)));
            }

            foreach (ContentFile file in Files)
            {
                doc.Root.Add(new XElement(file.Type.ToString(), new XAttribute("file", file.Path.CleanUpPathCrossPlatform())));
            }

            doc.Save(filePath);
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
                if (!multiplayerIncompatibleContent.Contains(file.Type)) { continue; }

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
                    DebugConsole.ThrowError("Error while calculating content package hash: ", e);
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
                        var rootElement = doc.Root;
                        var element = rootElement.IsOverride() ? rootElement.FirstElement() : rootElement;
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
                        filePaths = filePaths.OrderBy(f => ToolBox.StringToUInt32Hash(f.CleanUpPathCrossPlatform(true), tempMd5)).ToList();
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
        /// Are mods allowed to install a file into the specified path. If a content package XML includes files
        /// with a prohibited path, they are treated as references to external files. For example, a mod could include
        /// some vanilla files in the XML, in which case the game will simply use the vanilla files present in the game folder.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static bool IsModFilePathAllowed(string path)
        {
            while (true)
            {
                string temp = System.IO.Path.GetDirectoryName(path);
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

        public IEnumerable<string> GetFilesOfType(ContentType type)
        {
            return Files.Where(f => f.Type == type).Select(f => f.Path);
        }
        
        public static void LoadAll()
        {
            string folder = ContentPackage.Folder;
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

            string[] files = Directory.GetFiles(folder, "*.xml");

            List.Clear();

            foreach (string filePath in files)
            {
                List.Add(new ContentPackage(filePath));
            }

            string[] modDirectories = Directory.GetDirectories("Mods");
            foreach (string modDirectory in modDirectories)
            {
                if (System.IO.Path.GetFileName(modDirectory.TrimEnd(System.IO.Path.DirectorySeparatorChar)) == "ExampleMod") { continue; }
                string modFilePath = System.IO.Path.Combine(modDirectory, Steam.SteamManager.MetadataFileName);
                if (File.Exists(modFilePath))
                {
                    List.Add(new ContentPackage(modFilePath));
                }
            }

            List = List
                .OrderByDescending(p => p.CorePackage)
                .ThenByDescending(p => GameMain.Config?.SelectedContentPackages.Contains(p))
                .ThenBy(p => GameMain.Config?.SelectedContentPackages.IndexOf(p))
                .ToList();
        }

        public static void SortContentPackages()
        {
            List = List
                .OrderByDescending(p => p.CorePackage)
                .ThenBy(p => List.IndexOf(p))
                .ToList();

            if (GameMain.Config != null)
            {
                var sortedSelected = GameMain.Config.SelectedContentPackages
                    .OrderByDescending(p => p.CorePackage)
                    .ThenBy(p => List.IndexOf(p))
                    .ToList();
                GameMain.Config.SelectedContentPackages.Clear(); GameMain.Config.SelectedContentPackages.AddRange(sortedSelected);

                var reportList = List.Where(p => GameMain.Config.SelectedContentPackages.Contains(p));
                DebugConsole.NewMessage($"Content package load order: { string.Join("  |  ", reportList.Select(cp => cp.Name)) }");
            }
        }

        public void Delete()
        {
            try
            {
                GameMain.Config.DeselectContentPackage(this);
                GameMain.Config.SaveNewPlayerConfig();
                List.Remove(this);
                File.Delete(Path);
                SortContentPackages();
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
