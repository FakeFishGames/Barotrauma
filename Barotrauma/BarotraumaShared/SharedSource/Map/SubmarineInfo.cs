using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel;
#if DEBUG
using System.IO;
#else
using Barotrauma.IO;
#endif
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

    public enum SubmarineType { Player, Outpost, OutpostModule, Wreck, BeaconStation, EnemySubmarine, Ruin }
    public enum SubmarineClass { Undefined, Scout, Attack, Transport, DeepDiver }

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

        /// <summary>
        /// A random int that gets assigned when saving the sub. Used in mp campaign to verify that sub files match
        /// </summary>
        public int EqualityCheckVal { get; private set; }

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

        public int Price
        {
            get;
            set;
        }

        public bool InitialSuppliesSpawned
        {
            get;
            set;
        }

        public Version GameVersion
        {
            get;
            set;
        }

        public SubmarineType Type { get; set; }

        public SubmarineClass SubmarineClass;

        public OutpostModuleInfo OutpostModuleInfo { get; set; }

        public bool IsOutpost => Type == SubmarineType.Outpost || Type == SubmarineType.OutpostModule;

        public bool IsWreck => Type == SubmarineType.Wreck;
        public bool IsBeacon => Type == SubmarineType.BeaconStation;
        public bool IsPlayer => Type == SubmarineType.Player;
        public bool IsRuin => Type == SubmarineType.Ruin;

        public bool IsCampaignCompatible => IsPlayer && !HasTag(SubmarineTag.Shuttle) && !HasTag(SubmarineTag.HideInMenus) && SubmarineClass != SubmarineClass.Undefined;
        public bool IsCampaignCompatibleIgnoreClass => IsPlayer && !HasTag(SubmarineTag.Shuttle) && !HasTag(SubmarineTag.HideInMenus);

        public Md5Hash MD5Hash
        {
            get
            {
                if (hash == null)
                {
                    if (hashTask == null)
                    {
                        XDocument doc = OpenFile(FilePath);
                        StartHashDocTask(doc);
                    }
                    hashTask.Wait();
                    hashTask = null;
                }

                return hash;
            }
        }

        public bool CalculatingHash
        {
            get { return hashTask != null && !hashTask.IsCompleted; }
        }

        public Vector2 Dimensions
        {
            get;
            private set;
        }

        public int CargoCapacity
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
                return RequiredContentPackages.All(cp => GameMain.Config.AllEnabledPackages.Any(cp2 => cp2.Name == cp));
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
        
        public readonly List<ushort> LeftBehindDockingPortIDs = new List<ushort>();
        public readonly List<ushort> BlockedDockingPortIDs = new List<ushort>();

        public bool LeftBehindSubDockingPortOccupied
        {
            get; private set;
        }

        public OutpostGenerationParams OutpostGenerationParams;

        public readonly Dictionary<string, List<Character>> OutpostNPCs = new Dictionary<string, List<Character>>();

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
            Price = original.Price;
            InitialSuppliesSpawned = original.InitialSuppliesSpawned;
            GameVersion = original.GameVersion;
            Type = original.Type;
            SubmarineClass = original.SubmarineClass;
            hash = !string.IsNullOrEmpty(original.FilePath) ? original.MD5Hash : null;
            Dimensions = original.Dimensions;
            CargoCapacity = original.CargoCapacity;
            FilePath = original.FilePath;
            RequiredContentPackages = new HashSet<string>(original.RequiredContentPackages);
            IsFileCorrupted = original.IsFileCorrupted;
            SubmarineElement = original.SubmarineElement;
            EqualityCheckVal = original.EqualityCheckVal;
            RecommendedCrewExperience = original.RecommendedCrewExperience;
            RecommendedCrewSizeMin = original.RecommendedCrewSizeMin;
            RecommendedCrewSizeMax = original.RecommendedCrewSizeMax;
            Tags = original.Tags;
            if (original.OutpostModuleInfo != null)
            {
                OutpostModuleInfo = new OutpostModuleInfo(original.OutpostModuleInfo);
            }
#if CLIENT
            PreviewImage = original.PreviewImage != null ? new Sprite(original.PreviewImage) : null;
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

            EqualityCheckVal = SubmarineElement.GetAttributeInt("checkval", 0);

            Price = SubmarineElement.GetAttributeInt("price", 1000);

            InitialSuppliesSpawned = SubmarineElement.GetAttributeBool("initialsuppliesspawned", false);

            GameVersion = new Version(SubmarineElement.GetAttributeString("gameversion", "0.0.0.0"));
            if (Enum.TryParse(SubmarineElement.GetAttributeString("tags", ""), out SubmarineTag tags))
            {
                Tags = tags;
            }
            Dimensions = SubmarineElement.GetAttributeVector2("dimensions", Vector2.Zero);
            CargoCapacity = SubmarineElement.GetAttributeInt("cargocapacity", -1);
            RecommendedCrewSizeMin = SubmarineElement.GetAttributeInt("recommendedcrewsizemin", 0);
            RecommendedCrewSizeMax = SubmarineElement.GetAttributeInt("recommendedcrewsizemax", 0);
            RecommendedCrewExperience = SubmarineElement.GetAttributeString("recommendedcrewexperience", "Unknown");

            if (SubmarineElement?.Attribute("type") != null)
            {
                if (Enum.TryParse(SubmarineElement.GetAttributeString("type", ""), out SubmarineType type))
                {
                    Type = type;
                    if (Type == SubmarineType.OutpostModule)
                    {
                        OutpostModuleInfo = new OutpostModuleInfo(this, SubmarineElement);
                    }
                }
            }

            if (Type == SubmarineType.Player)
            {
                if (SubmarineElement?.Attribute("class") != null)
                {
                    if (Enum.TryParse(SubmarineElement.GetAttributeString("class", "Undefined"), out SubmarineClass submarineClass))
                    {
                        SubmarineClass = submarineClass;
                    }
                }
            }
            else
            {
                SubmarineClass = SubmarineClass.Undefined;
            }

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
#if CLIENT
            PreviewImage?.Remove();
            PreviewImage = null;
#endif
            if (savedSubmarines.Contains(this)) { savedSubmarines.Remove(this); }
        }

        public bool IsVanillaSubmarine()
        {
            var vanilla = GameMain.VanillaContent;
            if (vanilla != null)
            {
                var vanillaSubs = vanilla.GetFilesOfType(ContentType.Submarine)
                    .Concat(vanilla.GetFilesOfType(ContentType.Wreck))
                    .Concat(vanilla.GetFilesOfType(ContentType.BeaconStation))
                    .Concat(vanilla.GetFilesOfType(ContentType.EnemySubmarine))
                    .Concat(vanilla.GetFilesOfType(ContentType.Outpost))
                    .Concat(vanilla.GetFilesOfType(ContentType.OutpostModule));
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
            LeftBehindDockingPortIDs.Clear();
            BlockedDockingPortIDs.Clear();
            foreach (XElement subElement in element.Elements())
            {
                if (!subElement.Name.ToString().Equals("linkedsubmarine", StringComparison.OrdinalIgnoreCase)) { continue; }
                if (subElement.Attribute("location") == null) { continue; }

                subsLeftBehind = true;
                ushort targetDockingPortID = (ushort)subElement.GetAttributeInt("originallinkedto", 0);
                LeftBehindDockingPortIDs.Add(targetDockingPortID);
                XElement targetPortElement = targetDockingPortID == 0 ? null :
                    element.Elements().FirstOrDefault(e => e.GetAttributeInt("ID", 0) == targetDockingPortID);
                if (targetPortElement != null && targetPortElement.GetAttributeIntArray("linked", new int[0]).Length > 0)
                {
                    BlockedDockingPortIDs.Add(targetDockingPortID);
                    LeftBehindSubDockingPortOccupied = true;
                }
            }
        }

        /// <summary>
        /// Calculated from <see cref="SubmarineElement"/>. Can be used when the sub hasn't been loaded and we can't access <see cref="Submarine.RealWorldCrushDepth"/>.
        /// </summary>
        public float GetRealWorldCrushDepth()
        {
            if (SubmarineElement == null) { return Level.DefaultRealWorldCrushDepth; }
            bool structureCrushDepthsDefined = false;
            float realWorldCrushDepth = float.PositiveInfinity;
            foreach (var structureElement in SubmarineElement.GetChildElements("structure"))
            {
                string name = structureElement.Attribute("name")?.Value ?? "";
                string identifier = structureElement.GetAttributeString("identifier", "");
                var structurePrefab = Structure.FindPrefab(name, identifier);
                if (structurePrefab == null || !structurePrefab.Body) { continue; }
                if (!structureCrushDepthsDefined && structureElement.Attribute("crushdepth") != null)
                {
                    structureCrushDepthsDefined = true;
                }
                float structureCrushDepth = structureElement.GetAttributeFloat("crushdepth", float.PositiveInfinity);
                realWorldCrushDepth = Math.Min(structureCrushDepth, realWorldCrushDepth);
            }
            if (!structureCrushDepthsDefined)
            {
                realWorldCrushDepth = Level.DefaultRealWorldCrushDepth;
            }
            realWorldCrushDepth *= GetRealWorldCrushDepthMultiplier();
            return realWorldCrushDepth;
        }

        /// <summary>
        /// Based on <see cref="SubmarineClass"/>
        /// </summary>
        public float GetRealWorldCrushDepthMultiplier()
        {
            if (SubmarineClass == SubmarineClass.DeepDiver)
            {
                return 1.2f;
            }
            else
            {
                return 1.0f;
            }
        }

        //saving/loading ----------------------------------------------------
        public bool SaveAs(string filePath, System.IO.MemoryStream previewImage = null)
        {
            var newElement = new XElement(
                SubmarineElement.Name, 
                SubmarineElement.Attributes()
                    .Where(a => 
                        !string.Equals(a.Name.LocalName, "previewimage", StringComparison.InvariantCultureIgnoreCase) &&
                        !string.Equals(a.Name.LocalName, "name", StringComparison.InvariantCultureIgnoreCase)), 
                SubmarineElement.Elements());

            if (Type == SubmarineType.OutpostModule)
            {
                OutpostModuleInfo.Save(newElement);
                OutpostModuleInfo = new OutpostModuleInfo(this, newElement);
            }
            XDocument doc = new XDocument(newElement);

            doc.Root.Add(new XAttribute("name", Name));
            if (previewImage != null)
            {
                doc.Root.Add(new XAttribute("previewimage", Convert.ToBase64String(previewImage.ToArray())));
            }
            try
            {
                SaveUtil.CompressStringToFile(filePath, doc.ToString());
                Md5Hash.RemoveFromCache(filePath);
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
            var contentPackageSubs = ContentPackage.GetFilesOfType(
                GameMain.Config.AllEnabledPackages, 
                ContentType.Submarine, ContentType.Outpost, ContentType.OutpostModule,
                ContentType.Wreck, ContentType.BeaconStation, ContentType.EnemySubmarine);

            for (int i = savedSubmarines.Count - 1; i >= 0; i--)
            {
                if (File.Exists(savedSubmarines[i].FilePath))
                {
                    bool isDownloadedSub = Path.GetFullPath(Path.GetDirectoryName(savedSubmarines[i].FilePath)) == Path.GetFullPath(SaveUtil.SubmarineDownloadFolder);
                    bool isInSubmarinesFolder = Path.GetFullPath(Path.GetDirectoryName(savedSubmarines[i].FilePath)) == Path.GetFullPath(SavePath);
                    bool isInContentPackage = contentPackageSubs.Any(fp => Path.GetFullPath(fp.Path).CleanUpPath() == Path.GetFullPath(savedSubmarines[i].FilePath).CleanUpPath());
                    if (isDownloadedSub) { continue; }
                    if (savedSubmarines[i].LastModifiedTime == File.GetLastWriteTime(savedSubmarines[i].FilePath) && (isInSubmarinesFolder || isInContentPackage)) { continue; }
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
                    return !dir.Attributes.HasFlag(System.IO.FileAttributes.Hidden) && !dir.Name.StartsWith(".");
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
                System.IO.Stream stream;
                try
                {
                    stream = SaveUtil.DecompressFiletoStream(file);
                }
                catch (System.IO.FileNotFoundException e)
                {
                    exception = e;
                    DebugConsole.ThrowError("Loading submarine \"" + file + "\" failed! (File not found) " + Environment.StackTrace.CleanupStackTrace(), e);
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
                    using (var reader = XMLExtensions.CreateReader(stream))
                    {
                        doc = XDocument.Load(reader);
                    }
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
                    using var stream = File.Open(file, System.IO.FileMode.Open, System.IO.FileAccess.Read);
                    using var reader = XMLExtensions.CreateReader(stream);
                    doc = XDocument.Load(reader);
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