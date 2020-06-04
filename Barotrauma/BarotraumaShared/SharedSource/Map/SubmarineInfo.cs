using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Barotrauma.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Barotrauma
{
    [Flags]
    public enum SubmarineTag
    {
        [Description("Shuttle")]
        Shuttle = 1,
        [Description("Hide in menus")]
        HideInMenus = 2
    }

    partial class SubmarineInfo : IDisposable
    {
        public const string SavePath = "Submarines";

        private static List<SubmarineInfo> savedSubmarines = new List<SubmarineInfo>();
        public static IEnumerable<SubmarineInfo> SavedSubmarines
        {
            get { return savedSubmarines; }
        }

        private Task hashTask;
        private Md5Hash hash;

        public readonly DateTime LastModifiedTime;

        public SubmarineTag Tags { get; private set; }

        public int RecommendedCrewSizeMin = 1, RecommendedCrewSizeMax = 2;
        public string RecommendedCrewExperience;

        public HashSet<string> RequiredContentPackages = new HashSet<string>();

        public string Name
        {
            get;
            set;
        }

        public string DisplayName
        {
            get;
            set;
        }

        public string Description
        {
            get;
            set;
        }

        public Version GameVersion
        {
            get;
            set;
        }

        public bool IsOutpost => Type == SubmarineType.Outpost;
        public bool IsWreck => Type == SubmarineType.Wreck;

        public bool IsPlayer => Type == SubmarineType.Player;

        public enum SubmarineType { Player, Outpost, Wreck }
        public SubmarineType Type { get; set; }

        public Md5Hash MD5Hash
        {
            get
            {
                if (hash == null)
                {
                    XDocument doc = OpenFile(FilePath);
                    StartHashDocTask(doc);
                    hashTask.Wait();
                    hashTask = null;
                }

                return hash;
            }
        }

        public Vector2 Dimensions
        {
            get;
            private set;
        }

        public string FilePath
        {
            get;
            set;
        }

        public XElement SubmarineElement
        {
            get;
            private set;
        }

        public override string ToString()
        {
            return "Barotrauma.SubmarineInfo (" + Name + ")";
        }

        public bool IsFileCorrupted
        {
            get;
            private set;
        }

        private bool? requiredContentPackagesInstalled;
        public bool RequiredContentPackagesInstalled
        {
            get
            {
                if (requiredContentPackagesInstalled.HasValue) { return requiredContentPackagesInstalled.Value; }
                return RequiredContentPackages.All(cp => GameMain.SelectedPackages.Any(cp2 => cp2.Name == cp));
            }
            set
            {
                requiredContentPackagesInstalled = value;
            }
        }

        private bool? subsLeftBehind;
        public bool SubsLeftBehind
        {
            get
            {
                if (subsLeftBehind.HasValue) { return subsLeftBehind.Value; }
                CheckSubsLeftBehind(SubmarineElement);
                return subsLeftBehind.Value;
            }
        }

        public bool LeftBehindSubDockingPortOccupied
        {
            get; private set;
        }

        //constructors & generation ----------------------------------------------------
        public SubmarineInfo()
        {
            FilePath = null;
            Name = DisplayName = TextManager.Get("UnspecifiedSubFileName");
            IsFileCorrupted = false;
            RequiredContentPackages = new HashSet<string>();
        }

        public SubmarineInfo(string filePath, string hash = "", XElement element = null, bool tryLoad = true)
        {
            FilePath = filePath;
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                LastModifiedTime = File.GetLastWriteTime(filePath);
            }
            try
            {
                Name = DisplayName = Path.GetFileNameWithoutExtension(filePath);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error loading submarine " + filePath + "!", e);
            }

            if (!string.IsNullOrWhiteSpace(hash))
            {
                this.hash = new Md5Hash(hash);
            }

            IsFileCorrupted = false;

            RequiredContentPackages = new HashSet<string>();

            if (element == null && tryLoad)
            {
                Reload();
            }
            else
            {
                SubmarineElement = element;
            }

            Name = SubmarineElement.GetAttributeString("name", null) ?? Name;

            Init();
        }

        public SubmarineInfo(Submarine sub) : this(sub.Info)
        {
            GameVersion = GameMain.Version;
            SubmarineElement = new XElement("Submarine");
            sub.SaveToXElement(SubmarineElement);
            Init();
        }

        public SubmarineInfo(SubmarineInfo original)
        {
            Name = original.Name;
            DisplayName = original.DisplayName;
            Description = original.Description;
            GameVersion = original.GameVersion;
            Type = original.Type;
            hash = !string.IsNullOrEmpty(original.FilePath) ? original.MD5Hash : null;
            Dimensions = original.Dimensions;
            FilePath = original.FilePath;
            RequiredContentPackages = new HashSet<string>(original.RequiredContentPackages);
            IsFileCorrupted = original.IsFileCorrupted;
            SubmarineElement = original.SubmarineElement;
            RecommendedCrewExperience = original.RecommendedCrewExperience;
            RecommendedCrewSizeMin = original.RecommendedCrewSizeMin;
            RecommendedCrewSizeMax = original.RecommendedCrewSizeMax;
            Tags = original.Tags;
#if CLIENT
            PreviewImage = original.PreviewImage != null ? new Sprite(original.PreviewImage.Texture, null, null) : null;
#endif
        }

        public void Reload()
        {
            XDocument doc = null;
            int maxLoadRetries = 4;
            for (int i = 0; i <= maxLoadRetries; i++)
            {
                doc = OpenFile(FilePath, out Exception e);
                if (e != null && !(e is System.IO.IOException)) { break; }
                if (doc != null || i == maxLoadRetries || !File.Exists(FilePath)) { break; }
                DebugConsole.NewMessage("Opening submarine file \"" + FilePath + "\" failed, retrying in 250 ms...");
                Thread.Sleep(250);
            }
            if (doc == null || doc.Root == null)
            {
                IsFileCorrupted = true;
                return;
            }
            if (hash == null)
            {
                StartHashDocTask(doc);
            }
            SubmarineElement = doc.Root;
        }

        private void Init()
        {
            DisplayName = TextManager.Get("Submarine.Name." + Name, true);
            if (string.IsNullOrEmpty(DisplayName)) { DisplayName = Name; }

            Description = TextManager.Get("Submarine.Description." + Name, true);
            if (string.IsNullOrEmpty(Description)) { Description = SubmarineElement.GetAttributeString("description", ""); }

            GameVersion = new Version(SubmarineElement.GetAttributeString("gameversion", "0.0.0.0"));
            if (Enum.TryParse(SubmarineElement.GetAttributeString("tags", ""), out SubmarineTag tags))
            {
                Tags = tags;
            }
            Dimensions = SubmarineElement.GetAttributeVector2("dimensions", Vector2.Zero);
            RecommendedCrewSizeMin = SubmarineElement.GetAttributeInt("recommendedcrewsizemin", 0);
            RecommendedCrewSizeMax = SubmarineElement.GetAttributeInt("recommendedcrewsizemax", 0);
            RecommendedCrewExperience = SubmarineElement.GetAttributeString("recommendedcrewexperience", "Unknown");

            //backwards compatibility (use text tags instead of the actual text)
            if (RecommendedCrewExperience == "Beginner")
            {
                RecommendedCrewExperience = "CrewExperienceLow";
            }
            else if (RecommendedCrewExperience == "Intermediate")
            {
                RecommendedCrewExperience = "CrewExperienceMid";
            }
            else if (RecommendedCrewExperience == "Experienced")
            {
                RecommendedCrewExperience = "CrewExperienceHigh";
            }

            RequiredContentPackages.Clear();
            string[] contentPackageNames = SubmarineElement.GetAttributeStringArray("requiredcontentpackages", new string[0]);
            foreach (string contentPackageName in contentPackageNames)
            {
                RequiredContentPackages.Add(contentPackageName);
            }

            InitProjectSpecific();
        }

        partial void InitProjectSpecific();

        public void Dispose()
        {
            if (savedSubmarines.Contains(this)) { savedSubmarines.Remove(this); }
        }

        public bool IsVanillaSubmarine()
        {
            var vanilla = GameMain.VanillaContent;
            if (vanilla != null)
            {
                var vanillaSubs = vanilla.GetFilesOfType(ContentType.Submarine);
                string pathToCompare = FilePath.Replace(@"\", @"/").ToLowerInvariant();
                if (vanillaSubs.Any(sub => sub.Replace(@"\", @"/").ToLowerInvariant() == pathToCompare))
                {
                    return true;
                }
            }
            return false;
        }

        public void StartHashDocTask(XDocument doc)
        {
            if (hash != null) { return; }
            if (hashTask != null) { return; }

            hashTask = new Task(() =>
            {
                hash = new Md5Hash(doc, FilePath);
            });
            hashTask.Start();
        }

        public bool HasTag(SubmarineTag tag)
        {
            return Tags.HasFlag(tag);
        }

        public void AddTag(SubmarineTag tag)
        {
            if (Tags.HasFlag(tag)) return;

            Tags |= tag;
        }

        public void RemoveTag(SubmarineTag tag)
        {
            if (!Tags.HasFlag(tag)) return;

            Tags &= ~tag;
        }

        public void CheckSubsLeftBehind(XElement element = null)
        {
            if (element == null) { element = SubmarineElement; }

            subsLeftBehind = false;
            LeftBehindSubDockingPortOccupied = false;
            foreach (XElement subElement in element.Elements())
            {
                if (!subElement.Name.ToString().Equals("linkedsubmarine", StringComparison.OrdinalIgnoreCase)) { continue; }
                if (subElement.Attribute("location") == null) { continue; }

                subsLeftBehind = true;
                ushort targetDockingPortID = (ushort)subElement.GetAttributeInt("originallinkedto", 0);
                XElement targetPortElement = targetDockingPortID == 0 ? null :
                    element.Elements().FirstOrDefault(e => e.GetAttributeInt("ID", 0) == targetDockingPortID);
                if (targetPortElement != null && targetPortElement.GetAttributeIntArray("linked", new int[0]).Length > 0)
                {
                    LeftBehindSubDockingPortOccupied = true;
                }
            }
        }


        //saving/loading ----------------------------------------------------
        public bool SaveAs(string filePath, System.IO.MemoryStream previewImage=null)
        {
            var newElement = new XElement(SubmarineElement.Name,
                SubmarineElement.Attributes().Where(a => !string.Equals(a.Name.LocalName, "previewimage", StringComparison.InvariantCultureIgnoreCase) &&
                                                         !string.Equals(a.Name.LocalName, "name", StringComparison.InvariantCultureIgnoreCase)),
                SubmarineElement.Elements());
            XDocument doc = new XDocument(newElement);

            doc.Root.Add(new XAttribute("name", Name));

            if (previewImage != null)
            {
                doc.Root.Add(new XAttribute("previewimage", Convert.ToBase64String(previewImage.ToArray())));
            }

            try
            {
                SaveUtil.CompressStringToFile(filePath, doc.ToString());
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Saving submarine \"" + filePath + "\" failed!", e);
                return false;
            }

            return true;
        }

        public static void AddToSavedSubs(SubmarineInfo subInfo)
        {
            savedSubmarines.Add(subInfo);
        }

        public static void RefreshSavedSub(string filePath)
        {
            string fullPath = Path.GetFullPath(filePath);
            for (int i = savedSubmarines.Count - 1; i >= 0; i--)
            {
                if (Path.GetFullPath(savedSubmarines[i].FilePath) == fullPath)
                {
                    savedSubmarines[i].Dispose();
                }
            }

            if (File.Exists(filePath))
            {
                var subInfo = new SubmarineInfo(filePath);
                if (!subInfo.IsFileCorrupted)
                {
                    savedSubmarines.Add(subInfo);
                }
                savedSubmarines = savedSubmarines.OrderBy(s => s.FilePath ?? "").ToList();
            }
        }

        public static void RefreshSavedSubs()
        {
            var contentPackageSubs = ContentPackage.GetFilesOfType(GameMain.Config.SelectedContentPackages, ContentType.Submarine);

            for (int i = savedSubmarines.Count - 1; i >= 0; i--)
            {
                if (File.Exists(savedSubmarines[i].FilePath) &&
                    savedSubmarines[i].LastModifiedTime == File.GetLastWriteTime(savedSubmarines[i].FilePath) &&
                    (Path.GetFullPath(Path.GetDirectoryName(savedSubmarines[i].FilePath)) == Path.GetFullPath(SavePath) ||
                    contentPackageSubs.Any(fp => Path.GetFullPath(fp.Path).CleanUpPath() == Path.GetFullPath(savedSubmarines[i].FilePath).CleanUpPath())))
                {
                    continue;
                }
                savedSubmarines[i].Dispose();
            }

            if (!Directory.Exists(SavePath))
            {
                try
                {
                    Directory.CreateDirectory(SavePath);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Directory \"" + SavePath + "\" not found and creating the directory failed.", e);
                    return;
                }
            }

            List<string> filePaths;
            string[] subDirectories;

            try
            {
                filePaths = Directory.GetFiles(SavePath).ToList();
                subDirectories = Directory.GetDirectories(SavePath).Where(s =>
                {
                    DirectoryInfo dir = new DirectoryInfo(s);
                    return (dir.Attributes & System.IO.FileAttributes.Hidden) == 0;
                }).ToArray();
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Couldn't open directory \"" + SavePath + "\"!", e);
                return;
            }

            foreach (string subDirectory in subDirectories)
            {
                try
                {
                    filePaths.AddRange(Directory.GetFiles(subDirectory).ToList());
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Couldn't open subdirectory \"" + subDirectory + "\"!", e);
                    return;
                }
            }

            foreach (ContentFile subFile in contentPackageSubs)
            {
                if (!filePaths.Any(fp => Path.GetFullPath(fp) == Path.GetFullPath(subFile.Path)))
                {
                    filePaths.Add(subFile.Path);
                }
            }

            filePaths.RemoveAll(p => savedSubmarines.Any(sub => sub.FilePath == p));

            foreach (string path in filePaths)
            {
                var subInfo = new SubmarineInfo(path);
                if (subInfo.IsFileCorrupted)
                {
#if CLIENT
                    if (DebugConsole.IsOpen) { DebugConsole.Toggle(); }
                    var deleteSubPrompt = new GUIMessageBox(
                        TextManager.Get("Error"),
                        TextManager.GetWithVariable("SubLoadError", "[subname]", subInfo.Name) + "\n" +
                        TextManager.GetWithVariable("DeleteFileVerification", "[filename]", subInfo.Name),
                        new string[] { TextManager.Get("Yes"), TextManager.Get("No") });

                    string filePath = path;
                    deleteSubPrompt.Buttons[0].OnClicked += (btn, userdata) =>
                    {
                        try
                        {
                            File.Delete(filePath);
                        }
                        catch (Exception e)
                        {
                            DebugConsole.ThrowError($"Failed to delete file \"{filePath}\".", e);
                        }
                        deleteSubPrompt.Close();
                        return true;
                    };
                    deleteSubPrompt.Buttons[1].OnClicked += deleteSubPrompt.Close;
#endif
                }
                else
                {
                    savedSubmarines.Add(subInfo);
                }
            }
        }

        static readonly string TempFolder = Path.Combine("Submarine", "Temp");

        public static XDocument OpenFile(string file)
        {
            return OpenFile(file, out _);
        }

        public static XDocument OpenFile(string file, out Exception exception)
        {
            XDocument doc = null;
            string extension = "";
            exception = null;

            try
            {
                extension = System.IO.Path.GetExtension(file);
            }
            catch
            {
                //no file extension specified: try using the default one
                file += ".sub";
            }

            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".sub";
                file += ".sub";
            }

            if (extension == ".sub")
            {
                System.IO.Stream stream = null;
                try
                {
                    stream = SaveUtil.DecompressFiletoStream(file);
                }
                catch (System.IO.FileNotFoundException e)
                {
                    exception = e;
                    DebugConsole.ThrowError("Loading submarine \"" + file + "\" failed! (File not found) " + Environment.StackTrace, e);
                    return null;
                }
                catch (Exception e)
                {
                    exception = e;
                    DebugConsole.ThrowError("Loading submarine \"" + file + "\" failed!", e);
                    return null;
                }

                try
                {
                    stream.Position = 0;
                    doc = XDocument.Load(stream); //ToolBox.TryLoadXml(file);
                    stream.Close();
                    stream.Dispose();
                }

                catch (Exception e)
                {
                    exception = e;
                    DebugConsole.ThrowError("Loading submarine \"" + file + "\" failed! (" + e.Message + ")");
                    return null;
                }
            }
            else if (extension == ".xml")
            {
                try
                {
                    ToolBox.IsProperFilenameCase(file);
                    doc = XDocument.Load(file, LoadOptions.SetBaseUri);
                }

                catch (Exception e)
                {
                    exception = e;
                    DebugConsole.ThrowError("Loading submarine \"" + file + "\" failed! (" + e.Message + ")");
                    return null;
                }
            }
            else
            {
                DebugConsole.ThrowError("Couldn't load submarine \"" + file + "! (Unrecognized file extension)");
                return null;
            }

            return doc;
        }
    }
}