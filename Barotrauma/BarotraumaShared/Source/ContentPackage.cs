using Facepunch.Steamworks;
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
        Buffs,
        Tutorials,
        UIStyle
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
            ContentType.Afflictions
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
            ContentType.BackgroundCreaturePrefabs,
            ContentType.RuinConfig,
            ContentType.NPCConversations,
            ContentType.Afflictions,
            ContentType.UIStyle
        };

        public static IEnumerable<ContentType> CorePackageRequiredFiles
        {
            get { return corePackageRequiredFiles; }
        }

        public string Name { get; set; }

        public string Path
        {
            get;
            private set;
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
                if (md5Hash == null) CalculateHash();
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
                Files.Add(new ContentFile(subElement.GetAttributeString("file", ""), type));
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

        /// <summary>
        /// Make sure all the files defined in the content package are present
        /// </summary>
        /// <returns></returns>
        public bool VerifyFiles(out List<string> errorMessages)
        {
            errorMessages = new List<string>();
            foreach (ContentFile file in Files)
            {
#if SERVER
                //dedicated server doesn't care if the client executable is present or not
                if (file.Type == ContentType.Executable) { continue; }
#endif

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
                new XAttribute("path", Path),
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
                doc.Root.Add(new XElement(file.Type.ToString(), new XAttribute("file", file.Path)));
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
                if (!multiplayerIncompatibleContent.Contains(file.Type)) continue;

                try
                {
                    var hash = CalculateFileHash(file);
                    if (logging)
                    {
                        var fileMd5 = new Md5Hash(hash);
                        DebugConsole.NewMessage("   " + file.Path + ": " + fileMd5.ShortHash);
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
                DebugConsole.NewMessage("****************************** Package hash: " + md5Hash.ShortHash);
            }
        }

        private byte[] CalculateFileHash(ContentFile file)
        {
            var md5 = MD5.Create();

            List<string> filePaths = new List<string> { file.Path };
            List<byte> data = new List<byte>();

            switch (file.Type)
            {
                case ContentType.Character:
                    XDocument doc = XMLExtensions.TryLoadXml(file.Path);
                    var rootElement = doc.Root;
                    var element = rootElement.IsOverride() ? rootElement.GetFirstChild() : rootElement;
                    var speciesName = element.GetAttributeString("name", "");
                    var ragdollFolder = RagdollParams.GetFolder(speciesName);
                    if (Directory.Exists(ragdollFolder))
                    {
                        Directory.GetFiles(ragdollFolder).Where(f => f.EndsWith(".xml", true, System.Globalization.CultureInfo.InvariantCulture))
                            .ForEach(f => filePaths.Add(f));
                    }
                    var animationFolder = AnimationParams.GetFolder(speciesName);
                    if (Directory.Exists(animationFolder))
                    {
                        Directory.GetFiles(animationFolder).Where(f => f.EndsWith(".xml", true, System.Globalization.CultureInfo.InvariantCulture))
                            .ForEach(f => filePaths.Add(f));
                    }
                    break;
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
                        text = text.Replace("\n", "").Replace("\r", "");
                        fileData = System.Text.Encoding.UTF8.GetBytes(text);
                    }
                    data.AddRange(fileData);
                }
            }
            return md5.ComputeHash(data.ToArray());
        }
        
        public static string GetFileExtension(ContentType contentType)
        {
            switch (contentType)
            {
                case ContentType.Executable:
                case ContentType.ServerExecutable:
                    return ".exe";
                default:
                    return ".xml";
            }
        }

        public static bool IsModFilePathAllowed(ContentFile contentFile)
        {
            string path = contentFile.Path;
            while (true)
            {
                string temp = System.IO.Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(temp)) { break; }
                path = temp;
            }
            switch (contentFile.Type)
            {
                case ContentType.Submarine:
                    return path == "Submarines";
                default:
                    return path == "Mods";
            }
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

        public static IEnumerable<string> GetFilesOfType(IEnumerable<ContentPackage> contentPackages, ContentType type)
        {
            return contentPackages.SelectMany(f => f.Files).Where(f => f.Type == type).Select(f => f.Path);
        }

        public IEnumerable<string> GetFilesOfType(ContentType type)
        {
            return Files.Where(f => f.Type == type).Select(f => f.Path);
        }
        
        public static void LoadAll(string folder)
        {
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
                ContentPackage package = new ContentPackage(filePath);
                List.Add(package);                               
            }
        }

        public static void SortContentPackages()
        {
            List = List
                .OrderByDescending(p => p.CorePackage)
                .ThenByDescending(p => GameMain.Config?.SelectedContentPackages.Contains(p))
                .ThenBy(p => GameMain.Config?.SelectedContentPackages.IndexOf(p))
                .ToList();

            if (GameMain.Config != null)
            {
                var reportList = List.Where(p => GameMain.Config.SelectedContentPackages.Contains(p));
                DebugConsole.NewMessage($"Content package load order: { new string(reportList.SelectMany(cp => cp.Name + "  |  ").ToArray()) }");
            }
        }
    }

    public class ContentFile
    {
        public readonly string Path;
        public ContentType Type;

        public Workshop.Item WorkShopItem;

        public ContentFile(string path, ContentType type, Workshop.Item workShopItem = null)
        {
            Path = path;

#if OSX || LINUX
            Path = Path.Replace("\\", "/");
#endif

            Type = type;
            WorkShopItem = workShopItem;
        }

        public override string ToString()
        {
            return Path;
        }
    }

}
