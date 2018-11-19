using Facepunch.Steamworks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace Barotrauma
{
    public enum ContentType
    {
        None, 
        Jobs, 
        Item, 
        Character, 
        Structure, 
        Executable, 
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
        Hair
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
            ContentType.Afflictions
        };
        
        private string name;
        public string Name
        {
            get { return name; }
            set { name = value; }
        }

        public string Path
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
            private set;
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

        public ContentPackage(string filePath)
            : this()
        {
            XDocument doc = XMLExtensions.TryLoadXml(filePath);

            Path = filePath;

            if (doc?.Root == null)
            {
                DebugConsole.ThrowError("Couldn't load content package \"" + filePath + "\"!");
                return;
            }

            name = doc.Root.GetAttributeString("name", "");

            CorePackage = doc.Root.GetAttributeBool("corepackage", false);

            foreach (XElement subElement in doc.Root.Elements())
            {
                if (!Enum.TryParse(subElement.Name.ToString(), true, out ContentType type))
                {
                    DebugConsole.ThrowError("Error in content package \"" + name + "\" - \"" + subElement.Name.ToString() + "\" is not a valid content type.");
                    continue;
                }
                Files.Add(new ContentFile(subElement.GetAttributeString("file", ""), type));
            }
        }

        public override string ToString()
        {
            return name;
        }

        public static ContentPackage CreatePackage(string name, string path)
        {
            ContentPackage newPackage = new ContentPackage()
            {
                name = name,
                Path = path
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
                new XAttribute("name", name),
                new XAttribute("path", Path),
                new XAttribute("corepackage", CorePackage)));

            foreach (ContentFile file in Files)
            {
                doc.Root.Add(new XElement(file.Type.ToString(), new XAttribute("file", file.Path)));
            }

            doc.Save(filePath);
        }

        private void CalculateHash()
        {
            List<byte[]> hashes = new List<byte[]>();
            
            var md5 = MD5.Create();
            foreach (ContentFile file in Files)
            {
                if (!multiplayerIncompatibleContent.Contains(file.Type)) continue;

                try 
                {
                    using (var stream = File.OpenRead(file.Path))
                    {
                        byte[] data = new byte[stream.Length];
                        stream.Read(data, 0, (int)stream.Length);
                        if (file.Path.EndsWith(".xml", true, System.Globalization.CultureInfo.InvariantCulture))
                        {
                            string text = System.Text.Encoding.UTF8.GetString(data);
                            text = text.Replace("\n", "").Replace("\r", "");
                            data = System.Text.Encoding.UTF8.GetBytes(text);
                        }
                        hashes.Add(md5.ComputeHash(data));
                    }
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
        }

        /// <summary>
        /// Returns all xml files.
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
    }

    public class ContentFile
    {
        public readonly string Path;
        public ContentType Type;

        public Workshop.Item WorkShopItem;

        public ContentFile(string path, ContentType type, Workshop.Item workShopItem = null)
        {
            Path = path;
            Type = type;
            WorkShopItem = workShopItem;
        }

        public override string ToString()
        {
            return Path;
        }
    }

}
