#nullable enable
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using Barotrauma.IO;
using Microsoft.Xna.Framework;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Barotrauma.Networking;

namespace Barotrauma
{
    public readonly struct CampaignDataPath
    {
        public readonly string LoadPath;
        public readonly string SavePath;

        public CampaignDataPath(string loadPath, string savePath)
        {
            if (IsBackupPath(savePath, out _))
            {
                throw new ArgumentException("Save path cannot be a backup path.", nameof(savePath));
            }

            LoadPath = loadPath;
            SavePath = savePath;
        }

        /// <summary>
        /// Empty path used for non-campaign game sessions.
        /// </summary>
        public static readonly CampaignDataPath Empty = new CampaignDataPath(loadPath: string.Empty, savePath: string.Empty);

        /// <summary>
        /// Creates a CampaignDataPath with the same load and save path.
        /// </summary>
        public static CampaignDataPath CreateRegular(string savePath)
            => new CampaignDataPath(savePath, savePath);

        public static bool IsBackupPath(string path, out uint foundIndex)
        {
            string extension = Path.GetExtension(path);
            bool startsWith = extension.StartsWith(SaveUtil.BackupExtension, StringComparison.OrdinalIgnoreCase);

            if (!startsWith)
            {
                foundIndex = 0;
                return false;
            }

            bool hasIndex = SaveUtil.TryGetBackupIndexFromFileName(path, out foundIndex);
            return hasIndex;
        }
    }

    static class SaveUtil
    {
        public const string GameSessionFileName = "gamesession.xml";

        private static readonly string LegacySaveFolder = Path.Combine("Data", "Saves");
        private static readonly string LegacyMultiplayerSaveFolder = Path.Combine(LegacySaveFolder, "Multiplayer");

#if OSX
        //"/*user*/Library/Application Support/Daedalic Entertainment GmbH/" on Mac
        public static readonly string DefaultSaveFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Personal), 
            "Library",
            "Application Support",
            "Daedalic Entertainment GmbH",
            "Barotrauma");
#else
        //"C:/Users/*user*/AppData/Local/Daedalic Entertainment GmbH/" on Windows
        //"/home/*user*/.local/share/Daedalic Entertainment GmbH/" on Linux
        public static readonly string DefaultSaveFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Daedalic Entertainment GmbH",
            "Barotrauma");
#endif

        public static string DefaultMultiplayerSaveFolder = Path.Combine(DefaultSaveFolder, "Multiplayer");

        public static readonly string SubmarineDownloadFolder = Path.Combine("Submarines", "Downloaded");
        public static readonly string CampaignDownloadFolder = Path.Combine("Data", "Saves", "Multiplayer_Downloaded");

        public const string BackupExtension = ".bk";

        /// <summary>
        /// .save.bk
        /// </summary>
        public const string FullBackupExtension = $".save{BackupExtension}";

        /// <summary>
        /// .save.bk0
        /// </summary>
        public const string BackupExtensionFormat = $"{FullBackupExtension}{{0}}";

        /// <summary>
        /// .xml.bk
        /// </summary>
        public const string BackupCharacterDataExtensionStart = $".xml{BackupExtension}";

        /// <summary>
        /// .xml.bk0
        /// </summary>
        public const string BackupCharacterDataFormat = $"{BackupCharacterDataExtensionStart}{{0}}";

        public static int MaxBackupCount = 3;

        public static string TempPath
        {
#if SERVER
            get { return Path.Combine(GetSaveFolder(SaveType.Singleplayer), "temp_server"); }
#else
            get { return Path.Combine(GetSaveFolder(SaveType.Singleplayer), "temp"); }
#endif
        }

        public enum SaveType
        {
            Singleplayer,
            Multiplayer
        }

        /// <summary>
        /// Saves the game to a file.
        /// </summary>
        /// <param name="filePath">The path to the save file. </param>
        /// <param name="isSavingOnLoading">
        /// Indicates if the save is happening during loading in multiplayer
        /// to ensure the campaign ID matches the one in the save file.
        /// Used to work around some quirks with the backup system.
        /// </param>
        public static void SaveGame(CampaignDataPath filePath, bool isSavingOnLoading = false)
        {
            if (!isSavingOnLoading && File.Exists(filePath.SavePath))
            {
                BackupSave(filePath.SavePath);
            }

            DebugConsole.Log("Saving the game to: " + filePath);
            Directory.CreateDirectory(TempPath, catchUnauthorizedAccessExceptions: true);
            try
            {
                ClearFolder(TempPath, new string[] { GameMain.GameSession.SubmarineInfo.FilePath });
            }
            catch (Exception e)
            {
                LogErrorAndSendToClients("Failed to clear folder", e);
                return;
            }

            try
            {
                GameMain.GameSession.Save(Path.Combine(TempPath, GameSessionFileName), isSavingOnLoading);

                if (!isSavingOnLoading)
                {
                    // Reset the campaign data path, since if we had a different load path, it would be invalid now
                    GameMain.GameSession.DataPath = CampaignDataPath.CreateRegular(filePath.SavePath);
                }
            }
            catch (Exception e)
            {
                LogErrorAndSendToClients("Error saving gamesession", e);
                return;
            }

            try
            {
                string? mainSubPath = null;
                if (GameMain.GameSession.SubmarineInfo != null)
                {
                    mainSubPath = Path.Combine(TempPath, GameMain.GameSession.SubmarineInfo.Name + ".sub");
                    GameMain.GameSession.SubmarineInfo.SaveAs(mainSubPath);
                    for (int i = 0; i < GameMain.GameSession.OwnedSubmarines.Count; i++)
                    {
                        if (GameMain.GameSession.OwnedSubmarines[i].Name == GameMain.GameSession.SubmarineInfo.Name)
                        {
                            GameMain.GameSession.OwnedSubmarines[i] = GameMain.GameSession.SubmarineInfo;
                        }
                    }
                }

                if (GameMain.GameSession.OwnedSubmarines != null)
                {
                    for (int i = 0; i < GameMain.GameSession.OwnedSubmarines.Count; i++)
                    {
                        SubmarineInfo storedInfo = GameMain.GameSession.OwnedSubmarines[i];
                        string subPath = Path.Combine(TempPath, storedInfo.Name + ".sub");
                        if (mainSubPath == subPath) { continue; }
                        storedInfo.SaveAs(subPath);
                    }
                }
            }
            catch (Exception e)
            {
                LogErrorAndSendToClients("Error saving submarine", e);
                return;
            }

            try
            {
                CompressDirectory(TempPath, filePath.SavePath);
            }
            catch (Exception e)
            {
                LogErrorAndSendToClients("Error compressing save file", e);
            }

            void LogErrorAndSendToClients(string errorMsg, Exception e)
            {
                DebugConsole.ThrowError(errorMsg, e);
#if SERVER
                if (GameMain.Server != null)
                {
                    foreach (var client in GameMain.Server.ConnectedClients)
                    {
                        GameMain.Server.SendDirectChatMessage(errorMsg + '\n' + e.StackTrace.CleanupStackTrace(), client, ChatMessageType.Error);
                    }
                }
#endif
            }
        }

        public static void LoadGame(CampaignDataPath path)
        {
            //ensure there's no gamesession/sub loaded because it'd lead to issues when starting a new one (e.g. trying to determine which level to load based on the placement of the sub)
            //can happen if a gamesession is interrupted ungracefully (exception during loading)
            Submarine.Unload();
            GameMain.GameSession = null;
            DebugConsole.Log("Loading save file: " + path.LoadPath);
            DecompressToDirectory(path.LoadPath, TempPath);

            XDocument doc = XMLExtensions.TryLoadXml(Path.Combine(TempPath, GameSessionFileName));
            if (doc == null) { return; }

            if (!IsSaveFileCompatible(doc))
            {
                throw new Exception($"The save file \"{path.LoadPath}\" is not compatible with this version of Barotrauma.");
            }

            var ownedSubmarines = LoadOwnedSubmarines(doc, out SubmarineInfo selectedSub);
            GameMain.GameSession = new GameSession(selectedSub, ownedSubmarines, doc, path);
        }

        public static List<SubmarineInfo> LoadOwnedSubmarines(XDocument saveDoc, out SubmarineInfo selectedSub)
        {
            string subPath = Path.Combine(TempPath, saveDoc.Root.GetAttributeString("submarine", "")) + ".sub";
            selectedSub = new SubmarineInfo(subPath);

            List<SubmarineInfo> ownedSubmarines = new List<SubmarineInfo>();

            var ownedSubsElement = saveDoc.Root?.Element("ownedsubmarines");
            if (ownedSubsElement == null) { return ownedSubmarines; }

            foreach (var subElement in ownedSubsElement.Elements())
            {
                string subName = subElement.GetAttributeString("name", "");
                string ownedSubPath = Path.Combine(TempPath, subName + ".sub");
                if (!File.Exists(ownedSubPath))
                {
                    DebugConsole.ThrowError($"Could not find the submarine \"{subName}\" ({ownedSubPath})! The save file may be corrupted. Removing the submarine from owned submarines...");
                }
                else
                {
                    ownedSubmarines.Add(new SubmarineInfo(ownedSubPath));
                }
            }
            return ownedSubmarines;
        }

        public static bool IsSaveFileCompatible(XDocument? saveDoc)
            => IsSaveFileCompatible(saveDoc?.Root);

        public static bool IsSaveFileCompatible(XElement? saveDocRoot)
        {
            if (saveDocRoot?.Attribute("version") == null) { return false; }
            return true;
        }

        public static void DeleteSave(string filePath)
        {
            try
            {
                File.Delete(filePath, catchUnauthorizedAccessExceptions: false);

                string[] backups = GetBackupPaths(Path.GetDirectoryName(filePath) ?? "", Path.GetFileNameWithoutExtension(filePath));
                foreach (string backup in backups)
                {
                    File.Delete(backup, catchUnauthorizedAccessExceptions: false);
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("ERROR: deleting save file \"" + filePath + "\" failed.", e);
            }

            //deleting a multiplayer save file -> also delete character data
            var fullPath = Path.GetFullPath(Path.GetDirectoryName(filePath) ?? "");

            if (fullPath.Equals(Path.GetFullPath(DefaultMultiplayerSaveFolder)) ||
                fullPath == Path.GetFullPath(GetSaveFolder(SaveType.Multiplayer)))
            {
                string characterDataSavePath = MultiPlayerCampaign.GetCharacterDataSavePath(filePath);
                if (File.Exists(characterDataSavePath))
                {
                    try
                    {
                        File.Delete(characterDataSavePath, catchUnauthorizedAccessExceptions: false);
                    }
                    catch (Exception e)
                    {
                        DebugConsole.ThrowError("ERROR: deleting character data file \"" + characterDataSavePath + "\" failed.", e);
                    }
                }
            }
        }

        public static string GetSaveFolder(SaveType saveType)
        {
            string folder = string.Empty;

            if (!string.IsNullOrEmpty(GameSettings.CurrentConfig.SavePath))
            {
                folder = GameSettings.CurrentConfig.SavePath;
                if (saveType == SaveType.Multiplayer)
                {
                    folder = Path.Combine(folder, "Multiplayer");
                }
                if (!Directory.Exists(folder))
                {
                    DebugConsole.AddWarning($"Could not find the custom save folder \"{folder}\", creating the folder...");
                    try
                    {
                        Directory.CreateDirectory(folder, catchUnauthorizedAccessExceptions: false);
                    }
                    catch (Exception e)
                    {
                        DebugConsole.ThrowError($"Could not find the custom save folder \"{folder}\". Using the default save path instead.", e);
                        folder = string.Empty;
                    }
                }
            }
            if (string.IsNullOrEmpty(folder))
            {
                folder = saveType == SaveType.Singleplayer ? DefaultSaveFolder : DefaultMultiplayerSaveFolder;
            }
            return folder;
        }

        public static IReadOnlyList<CampaignMode.SaveInfo> GetSaveFiles(SaveType saveType, bool includeInCompatible = true, bool logLoadErrors = true)
        {
            string defaultFolder = saveType == SaveType.Singleplayer ? DefaultSaveFolder : DefaultMultiplayerSaveFolder;
            if (!Directory.Exists(defaultFolder))
            {
                DebugConsole.Log("Save folder \"" + defaultFolder + " not found! Attempting to create a new folder...");
                try
                {
                    Directory.CreateDirectory(defaultFolder, catchUnauthorizedAccessExceptions: false);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Failed to create the folder \"" + defaultFolder + "\"!", e);
                }
            }

            List<string> files = Directory.GetFiles(defaultFolder, "*.save", System.IO.SearchOption.TopDirectoryOnly).ToList();

            var folder = GetSaveFolder(saveType);
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                files.AddRange(Directory.GetFiles(folder, "*.save", System.IO.SearchOption.TopDirectoryOnly));
            }

            string legacyFolder = saveType == SaveType.Singleplayer ? LegacySaveFolder : LegacyMultiplayerSaveFolder;
            if (Directory.Exists(legacyFolder))
            {
                files.AddRange(Directory.GetFiles(legacyFolder, "*.save", System.IO.SearchOption.TopDirectoryOnly));
            }

            files = files.Distinct().ToList();

            List<CampaignMode.SaveInfo> saveInfos = new List<CampaignMode.SaveInfo>();   
            foreach (string file in files)
            {
                var docRoot = ExtractGameSessionRootElementFromSaveFile(file, logLoadErrors);
                if (!includeInCompatible && !IsSaveFileCompatible(docRoot))
                {
                    continue;
                }
                if (docRoot == null)
                {
                    saveInfos.Add(new CampaignMode.SaveInfo(
                        FilePath: file,
                        SaveTime: Option.None,
                        SubmarineName: "",
                        RespawnMode: RespawnMode.None,
                        EnabledContentPackageNames: ImmutableArray<string>.Empty));
                }
                else
                {
                    List<string> enabledContentPackageNames = new List<string>();

                    //backwards compatibility
                    string enabledContentPackagePathsStr = docRoot.GetAttributeStringUnrestricted("selectedcontentpackages", string.Empty);
                    foreach (string packagePath in enabledContentPackagePathsStr.Split('|'))
                    {
                        if (string.IsNullOrEmpty(packagePath)) { continue; }
                        //change paths to names
                        string fileName = Path.GetFileNameWithoutExtension(packagePath);
                        if (fileName == "filelist")
                        { 
                            enabledContentPackageNames.Add(Path.GetFileName(Path.GetDirectoryName(packagePath) ?? ""));
                        }
                        else
                        {
                            enabledContentPackageNames.Add(fileName);
                        }
                    }

                    string enabledContentPackageNamesStr = docRoot.GetAttributeStringUnrestricted("selectedcontentpackagenames", string.Empty);
                    //split on pipes, excluding pipes preceded by \
                    foreach (string packageName in Regex.Split(enabledContentPackageNamesStr, @"(?<!(?<!\\)*\\)\|"))
                    {
                        if (string.IsNullOrEmpty(packageName)) { continue; }                        
                        enabledContentPackageNames.Add(packageName.Replace(@"\|", "|"));                        
                    }

                    saveInfos.Add(new CampaignMode.SaveInfo(
                        FilePath: file,
                        SaveTime: docRoot.GetAttributeDateTime("savetime"),
                        SubmarineName: docRoot.GetAttributeStringUnrestricted("submarine", ""),
                        RespawnMode: docRoot.GetAttributeEnum("respawnmode", RespawnMode.None),
                        EnabledContentPackageNames: enabledContentPackageNames.ToImmutableArray()));
                }
            }
            
            return saveInfos;
        }

        public static string CreateSavePath(SaveType saveType, string fileName = "Save_Default")
        {
            fileName = ToolBox.RemoveInvalidFileNameChars(fileName);

            string folder = GetSaveFolder(saveType);
            if (fileName == "Save_Default")
            {
                fileName = TextManager.Get("SaveFile.DefaultName").Value;
                if (fileName.Length == 0) fileName = "Save";
            }

            if (!Directory.Exists(folder))
            {
                DebugConsole.Log("Save folder \"" + folder + "\" not found. Created new folder");
                Directory.CreateDirectory(folder, catchUnauthorizedAccessExceptions: true);
            }

            string extension = ".save";
            string pathWithoutExtension = Path.Combine(folder, fileName);

            if (!File.Exists(pathWithoutExtension + extension))
            {
                return pathWithoutExtension + extension;
            }

            int i = 0;
            while (File.Exists(pathWithoutExtension + " " + i + extension))
            {
                i++;
            }

            return pathWithoutExtension + " " + i + extension;
        }

        public static void CompressStringToFile(string fileName, string value)
        {
            // A.
            // Convert the string to its byte representation.
            byte[] b = Encoding.UTF8.GetBytes(value);

            // B.
            // Use GZipStream to write compressed bytes to target file.
            using FileStream f2 = File.Open(fileName, System.IO.FileMode.Create)
                ?? throw new Exception($"Failed to create file \"{fileName}\"");;
            using GZipStream gz = new GZipStream(f2, CompressionMode.Compress, false);
            gz.Write(b, 0, b.Length);
        }

        private static void CompressFile(string sDir, string sRelativePath, GZipStream zipStream)
        {
            //Compress file name
            if (sRelativePath.Length > 255)
            {
                throw new Exception(
                    $"Failed to compress \"{sDir}\" (file name length > 255).");
            }
            // File name length is encoded as a 32-bit little endian integer here
            zipStream.WriteByte((byte)sRelativePath.Length);
            zipStream.WriteByte(0);
            zipStream.WriteByte(0);
            zipStream.WriteByte(0);
            // File name content is encoded as little-endian UTF-16
            var strBytes = Encoding.Unicode.GetBytes(sRelativePath.CleanUpPathCrossPlatform(correctFilenameCase: false));
            zipStream.Write(strBytes, 0, strBytes.Length);

            //Compress file content
            byte[] bytes = File.ReadAllBytes(Path.Combine(sDir, sRelativePath));
            zipStream.Write(BitConverter.GetBytes(bytes.Length), 0, sizeof(int));
            zipStream.Write(bytes, 0, bytes.Length);
        }

        public static void CompressDirectory(string sInDir, string sOutFile)
        {
            IEnumerable<string> sFiles = Directory.GetFiles(sInDir, "*.*", System.IO.SearchOption.AllDirectories);
            int iDirLen = sInDir[^1] == Path.DirectorySeparatorChar ? sInDir.Length : sInDir.Length + 1;

            using var outFile = File.Open(sOutFile, System.IO.FileMode.Create, System.IO.FileAccess.Write)
                ?? throw new Exception($"Failed to create file \"{sOutFile}\"");
            using GZipStream str = new GZipStream(outFile, CompressionMode.Compress);
            foreach (string sFilePath in sFiles)
            {
                string sRelativePath = sFilePath.Substring(iDirLen);
                CompressFile(sInDir, sRelativePath, str);
            }
        }


        public static System.IO.Stream DecompressFileToStream(string fileName)
        {
            using FileStream originalFileStream = File.Open(fileName, System.IO.FileMode.Open, System.IO.FileAccess.Read)
                ?? throw new Exception($"Failed to open file \"{fileName}\"");
            System.IO.MemoryStream streamToReturn = new System.IO.MemoryStream();

            using GZipStream gzipStream = new GZipStream(originalFileStream, CompressionMode.Decompress);
            gzipStream.CopyTo(streamToReturn);
            
            streamToReturn.Position = 0;
            return streamToReturn;
        }

        private static bool IsExtractionPathValid(string rootDir, string fileDir)
        {
            string getFullPath(string dir)
                => (string.IsNullOrEmpty(dir)
                        ? Directory.GetCurrentDirectory()
                        : Path.GetFullPath(dir))
                    .CleanUpPathCrossPlatform(correctFilenameCase: false);
            
            string rootDirFull = getFullPath(rootDir);
            string fileDirFull = getFullPath(fileDir);

            return fileDirFull.StartsWith(rootDirFull, StringComparison.OrdinalIgnoreCase);
        }

        private static bool DecompressFile(System.IO.BinaryReader reader, [NotNullWhen(returnValue: true)]out string? fileName, [NotNullWhen(returnValue: true)]out byte[]? fileContent)
        {
            fileName = null;
            fileContent = null;

            if (reader.PeekChar() < 0) { return false; }
            
            //Decompress file name
            int nameLen = reader.ReadInt32();
            if (nameLen > 255)
            {
                throw new Exception(
                    $"Failed to decompress (file name length > 255). The file may be corrupted.");
            }

            byte[] strBytes = reader.ReadBytes(nameLen * sizeof(char));
            string sFileName = Encoding.Unicode.GetString(strBytes)
                .Replace('\\', '/');

            fileName = sFileName;

            //Decompress file content
            int contentLen = reader.ReadInt32();
            fileContent = reader.ReadBytes(contentLen);

            return true;
        }

        public static void DecompressToDirectory(string sCompressedFile, string sDir)
        {
            DebugConsole.Log("Decompressing " + sCompressedFile + " to " + sDir + "...");
            const int maxRetries = 4;
            for (int i = 0; i <= maxRetries; i++)
            {
                try
                {
                    using var memStream = DecompressFileToStream(sCompressedFile);
                    using var reader = new System.IO.BinaryReader(memStream);
                    while (DecompressFile(reader, out var fileName, out var contentBytes))
                    {
                        string sFilePath = Path.Combine(sDir, fileName);
                        string sFinalDir = Path.GetDirectoryName(sFilePath) ?? "";

                        if (!IsExtractionPathValid(sDir, sFinalDir))
                        {
                            throw new InvalidOperationException(
                                $"Error extracting \"{fileName}\": cannot be extracted to parent directory");
                        }

                        Directory.CreateDirectory(sFinalDir);
                        using var outFile = File.Open(sFilePath, System.IO.FileMode.Create, System.IO.FileAccess.Write)
                            ?? throw new Exception($"Failed to create file \"{sFilePath}\"");
                        outFile.Write(contentBytes, 0, contentBytes.Length);
                    }
                    break;
                }
                catch (System.IO.IOException e)
                {
                    if (i >= maxRetries || !File.Exists(sCompressedFile)) { throw; }
                    DebugConsole.NewMessage("Failed decompress file \"" + sCompressedFile + "\" {" + e.Message + "}, retrying in 250 ms...", Color.Red);
                    Thread.Sleep(250);
                }
            }
        }

        public static IEnumerable<string> EnumerateContainedFiles(string sCompressedFile)
        {
            const int maxRetries = 4;
            HashSet<string> paths = new HashSet<string>();
            for (int i = 0; i <= maxRetries; i++)
            {
                try
                {
                    paths.Clear();
                    using var memStream = DecompressFileToStream(sCompressedFile);
                    using var reader = new System.IO.BinaryReader(memStream);
                    while (DecompressFile(reader, out var fileName, out _))
                    {
                        paths.Add(fileName);
                    }
                    break;
                }
                catch (System.IO.IOException e)
                {
                    if (i >= maxRetries || !File.Exists(sCompressedFile)) { throw; }

                    DebugConsole.NewMessage(
                        $"Failed to decompress file \"{sCompressedFile}\" for enumeration {{{e.Message}}}, retrying in 250 ms...",
                        Color.Red);
                    Thread.Sleep(250);
                }
            }

            return paths;
        }

        /// <summary>
        /// Extracts the save file (including all the subs in it) to a temporary folder and returns the game session document.
        /// If you only need the gamesession doc, use <see cref="ExtractGameSessionRootElementFromSaveFile"/> instead.
        /// </summary>
        /// <param name="savePath"></param>
        /// <returns></returns>
        public static XDocument? DecompressSaveAndLoadGameSessionDoc(string savePath)
        {
            DebugConsole.Log("Loading game session doc: " + savePath);
            try
            {
                DecompressToDirectory(savePath, TempPath);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error decompressing " + savePath, e);
                return null;
            }
            return XMLExtensions.TryLoadXml(Path.Combine(TempPath, "gamesession.xml"));
        }

        /// <summary>
        /// Extract *only* the root element of the gamesession.xml file in the given save.
        /// For performance reasons, none of its child elements are returned.
        /// </summary>
        public static XElement? ExtractGameSessionRootElementFromSaveFile(string savePath, bool logLoadErrors = true)
        {
            const int maxRetries = 4;
            for (int i = 0; i <= maxRetries; i++)
            {
                try
                {
                    using var memStream = DecompressFileToStream(savePath);
                    using var reader = new System.IO.BinaryReader(memStream);
                    while (DecompressFile(reader, out var fileName, out var fileContent))
                    {
                        if (fileName != GameSessionFileName) { continue; }

                        // Found the file! Here's a quick byte-wise parser to find the root element
                        int tagOpenerStartIndex = -1;
                        for (int j = 0; j < fileContent.Length; j++)
                        {
                            if (fileContent[j] == '<')
                            {
                                // Found a tag opener: return null if we had already found one
                                if (tagOpenerStartIndex >= 0) { return null; }
                                tagOpenerStartIndex = j;
                            }
                            else if (j > 0 && fileContent[j] == '?' && fileContent[j - 1] == '<')
                            {
                                // Found the XML version element, skip this
                                tagOpenerStartIndex = -1;
                            }
                            else if (fileContent[j] == '>')
                            {
                                // Found a tag closer, if we know where the tag opener is then we've found the root element
                                if (tagOpenerStartIndex < 0) { continue; }

                                string elemStr = Encoding.UTF8.GetString(fileContent.AsSpan()[tagOpenerStartIndex..j]) + "/>";
                                try
                                {
                                    return XElement.Parse(elemStr);
                                }
                                catch (Exception e)
                                {
                                    DebugConsole.NewMessage(
                                        $"Failed to parse gamesession root in \"{savePath}\": {{{e.Message}}}.",
                                        Color.Red);
                                    // Parsing the element failed! Return null instead of crashing here
                                    return null;
                                }
                            }
                        }
                    }
                    break;
                }
                catch (System.IO.IOException e)
                {
                    if (i >= maxRetries || !File.Exists(savePath)) { throw; }

                    DebugConsole.NewMessage(
                        $"Failed to decompress file \"{savePath}\" for root extraction ({e.Message}), retrying in 250 ms...",
                        Color.Red);
                    Thread.Sleep(250);
                }
                catch (System.IO.InvalidDataException e)
                {
                    if (logLoadErrors)
                    {
                        DebugConsole.ThrowError($"Failed to decompress file \"{savePath}\" for root extraction.", e);
                    }
                    return null;
                }
            }
            return null;
        }

        public static void DeleteDownloadedSubs()
        {
            if (Directory.Exists(SubmarineDownloadFolder)) 
            { 
                ClearFolder(SubmarineDownloadFolder);
            }
        }

        public static void CleanUnnecessarySaveFiles()
        {
            if (Directory.Exists(CampaignDownloadFolder)) 
            { 
                ClearFolder(CampaignDownloadFolder);
                Directory.Delete(CampaignDownloadFolder);
            }
            if (Directory.Exists(TempPath)) 
            { 
                ClearFolder(TempPath);
                Directory.Delete(TempPath);
            }
        }

        public static void ClearFolder(string folderName, string[]? ignoredFileNames = null)
        {
            DirectoryInfo dir = new DirectoryInfo(folderName);

            foreach (FileInfo fi in dir.GetFiles())
            {
                if (ignoredFileNames != null)
                {
                    bool ignore = false;
                    foreach (string ignoredFile in ignoredFileNames)
                    {
                        if (Path.GetFileName(fi.FullName).Equals(Path.GetFileName(ignoredFile)))
                        {
                            ignore = true;
                            break;
                        }
                    }
                    if (ignore) continue;
                }
                fi.IsReadOnly = false;
                fi.Delete();
            }

            foreach (DirectoryInfo di in dir.GetDirectories())
            {
                ClearFolder(di.FullName, ignoredFileNames);
                const int maxRetries = 4;
                for (int i = 0; i <= maxRetries; i++)
                {
                    try
                    {
                        di.Delete();
                        break;
                    }
                    catch (System.IO.IOException)
                    {
                        if (i >= maxRetries) { throw; }
                        Thread.Sleep(250);
                    }
                }
            }
        }

#region Backup saves

        [NetworkSerialize]
        public readonly record struct BackupIndexData(uint Index,
                                                      Identifier LocationNameIdentifier,
                                                      int LocationNameFormatIndex,
                                                      Identifier LocationType,
                                                      LevelData.LevelType LevelType,
                                                      SerializableDateTime SaveTime) : INetSerializableStruct;

        public static string FormatBackupExtension(uint index) => string.Format(BackupExtensionFormat, index);
        public static string FormatBackupCharacterDataExtension(uint index) => string.Format(BackupCharacterDataFormat, index);

        public static void BackupSave(string savePath)
        {
            string path = Path.GetDirectoryName(savePath) ?? "";
            string fileName = Path.GetFileNameWithoutExtension(savePath);
            string characterDataSavePath = MultiPlayerCampaign.GetCharacterDataSavePath(savePath);
            string characterDataFileName = Path.GetFileNameWithoutExtension(characterDataSavePath);

            ImmutableArray<BackupIndexData> indexData = GetIndexData(path, fileName);

            uint freeIndex = GetFreeIndex(indexData);

            string newBackupPath = Path.Combine(path, $".{fileName}{FormatBackupExtension(freeIndex)}");
            string newCharacterDataBackupPath = Path.Combine(path, $".{characterDataFileName}{FormatBackupCharacterDataExtension(freeIndex)}");

            try
            {
                BackupFile(savePath, newBackupPath);
                if (File.Exists(characterDataSavePath))
                {
                    BackupFile(characterDataSavePath, newCharacterDataBackupPath);
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to create a backup of the save file.", e);
            }

            static uint GetFreeIndex(IEnumerable<BackupIndexData> indexData)
            {
                if (!indexData.Any()) { return 0; }

                if (indexData.Count() < MaxBackupCount)
                {
                    uint highestIndex = indexData.Max(static b => b.Index);
                    uint nextIndex = highestIndex + 1;

                    if (indexData.Any(b => b.Index == nextIndex))
                    {
                        for (uint i = 0; i < MaxBackupCount; i++)
                        {
                            if (indexData.All(b => b.Index != i)) { return i; }
                        }

                        // this should theoretically never happen
                        throw new InvalidOperationException("Failed to find a free index for the backup.");
                    }

                    return nextIndex;
                }

                BackupIndexData oldestBackup = indexData.OrderBy(static b => b.SaveTime).First();
                return oldestBackup.Index;
            }

            static void BackupFile(string sourcePath, string destPath)
            {
                // Overwriting a file that is marked as hidden will cause an exception.
                DeleteIfExists(destPath);
                System.IO.File.Copy(sourcePath, destPath, overwrite: true);
                SetHidden(destPath);
            }
        }

        public static void DeleteIfExists(string filePath)
        {
            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
            }
        }

        public static ImmutableArray<BackupIndexData> GetIndexData(string fullPath)
        {
            string path = Path.GetDirectoryName(fullPath) ?? "";
            string fileName = Path.GetFileNameWithoutExtension(fullPath);
            return GetIndexData(path, fileName);
        }

        private static readonly System.IO.EnumerationOptions BackupEnumerationOptions = new System.IO.EnumerationOptions
        {
            MatchType = System.IO.MatchType.Win32,
            AttributesToSkip = System.IO.FileAttributes.System,
            IgnoreInaccessible = true
        };

        private static string[] GetBackupPaths(string path, string baseName)
        {
            try
            {
                return System.IO.Directory.GetFiles(path, $".{baseName}{FullBackupExtension}*", BackupEnumerationOptions);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to get backup paths.", e);
            }
            return Array.Empty<string>();
        }

        public static bool TryGetBackupIndexFromFileName(string filePath, out uint index)
        {
            string extension = Path.GetExtension(filePath);
            if (extension.Length < BackupExtension.Length)
            {
                DebugConsole.ThrowError($"The file name \"{filePath}\" does not have a valid backup extension.");
                index = 0;
                return false;
            }

            string indexStr = extension[BackupExtension.Length..];
            bool result = uint.TryParse(indexStr, out index);
            if (!result)
            {
                DebugConsole.ThrowError($"Failed to parse the backup index from the file name \"{filePath}\".");
            }

            return result;
        }

        private static ImmutableArray<BackupIndexData> GetIndexData(string path, string baseName)
        {
            var builder = ImmutableArray.CreateBuilder<BackupIndexData>();

            string[] foundBackups = GetBackupPaths(path, baseName);

            foreach (string backupPath in foundBackups)
            {
                if (!TryGetBackupIndexFromFileName(backupPath, out uint index)) { continue; }

                var gameSession = ExtractGameSessionRootElementFromSaveFile(backupPath, logLoadErrors: false);

                if (gameSession is null)
                {
                    DebugConsole.AddWarning($"Failed to load gamesession root from \"{backupPath}\". Skipping this backup.");
                    continue;
                }

                SerializableDateTime saveTime =
                    gameSession.GetAttributeDateTime("savetime")
                               .Fallback(SerializableDateTime.FromUtcUnixTime(0L));

                Identifier locationNameIdentifier = gameSession.GetAttributeIdentifier("currentlocation", Identifier.Empty);
                int locationNameFormatIndex = gameSession.GetAttributeInt("currentlocationnameformatindex", -1);
                Identifier locationType = gameSession.GetAttributeIdentifier("locationtype", Identifier.Empty);

                LevelData.LevelType levelType = gameSession.GetAttributeEnum("nextleveltype", LevelData.LevelType.LocationConnection);

                builder.Add(new BackupIndexData(index, locationNameIdentifier, locationNameFormatIndex, locationType, levelType, saveTime));
            }

            return builder.ToImmutable();
        }

        public static string GetBackupPath(string savePath, uint index)
        {
            string path = Path.GetDirectoryName(savePath) ?? "";
            string fileName = Path.GetFileNameWithoutExtension(savePath);
            return Path.Combine(path, $".{fileName}{FormatBackupExtension(index)}");
        }

        private static void SetHidden(string filePath)
        {
            try
            {
                System.IO.File.SetAttributes(filePath, System.IO.File.GetAttributes(filePath) | System.IO.FileAttributes.Hidden);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to set the backup file as hidden.", e);
            }
        }
#endregion
    }
}
