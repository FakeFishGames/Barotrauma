using System;
using System.Collections;
using System.Collections.Generic;
using Barotrauma.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    partial class SaveUtil
    {
        private static readonly string LegacySaveFolder = Path.Combine("Data", "Saves");
        private static readonly string LegacyMultiplayerSaveFolder = Path.Combine(LegacySaveFolder, "Multiplayer");

#if OSX
        //"/*user*/Library/Application Support/Daedalic Entertainment GmbH/" on Mac
        public static string SaveFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Personal), 
            "Library",
            "Application Support",
            "Daedalic Entertainment GmbH",
            "Barotrauma");
#else
        //"C:/Users/*user*/AppData/Local/Daedalic Entertainment GmbH/" on Windows
        //"/home/*user*/.local/share/Daedalic Entertainment GmbH/" on Linux
        public static string SaveFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Daedalic Entertainment GmbH",
            "Barotrauma");
#endif

        public static string MultiplayerSaveFolder = Path.Combine(SaveFolder, "Multiplayer");

        public static readonly string SubmarineDownloadFolder = Path.Combine("Submarines", "Downloaded");
        public static readonly string CampaignDownloadFolder = Path.Combine("Data", "Saves", "Multiplayer_Downloaded");

        public delegate void ProgressDelegate(string sMessage);

        public static string TempPath
        {
#if SERVER
            get { return Path.Combine(SaveFolder, "temp_server"); }
#else
            get { return Path.Combine(SaveFolder, "temp"); }
#endif
        }

        public enum SaveType
        {
            Singleplayer,
            Multiplayer
        }

        public static void SaveGame(string filePath)
        {
            DebugConsole.Log("Saving the game to: " + filePath);
            Directory.CreateDirectory(TempPath);
            try
            {
                ClearFolder(TempPath, new string[] { GameMain.GameSession.SubmarineInfo.FilePath });
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to clear folder", e);
            }

            try
            {
                GameMain.GameSession.Save(Path.Combine(TempPath, "gamesession.xml"));
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error saving gamesession", e);
            }

            try
            {
                string mainSubPath = null;
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
                DebugConsole.ThrowError("Error saving submarine", e);
            }

            try
            {
                CompressDirectory(TempPath, filePath, null);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error compressing save file", e);
            }
        }

        public static void LoadGame(string filePath)
        {
            DebugConsole.Log("Loading save file: " + filePath);
            DecompressToDirectory(filePath, TempPath, null);

            XDocument doc = XMLExtensions.TryLoadXml(Path.Combine(TempPath, "gamesession.xml"));
            if (doc == null) { return; }

            if (!IsSaveFileCompatible(doc))
            {
                throw new Exception($"The save file \"{filePath}\" is not compatible with this version of Barotrauma.");
            }

            var ownedSubmarines = LoadOwnedSubmarines(doc, out SubmarineInfo selectedSub);
            GameMain.GameSession = new GameSession(selectedSub, ownedSubmarines, doc, filePath);
        }

        public static List<SubmarineInfo> LoadOwnedSubmarines(XDocument saveDoc, out SubmarineInfo selectedSub)
        {
            string subPath = Path.Combine(TempPath, saveDoc.Root.GetAttributeString("submarine", "")) + ".sub";
            selectedSub = new SubmarineInfo(subPath);

            List<SubmarineInfo> ownedSubmarines = null;
            var ownedSubsElement = saveDoc.Root.Element("ownedsubmarines");
            if (ownedSubsElement != null)
            {
                ownedSubmarines = new List<SubmarineInfo>();
                foreach (XElement subElement in ownedSubsElement.Elements())
                {
                    string subName = subElement.GetAttributeString("name", "");
                    string ownedSubPath = Path.Combine(TempPath, subName + ".sub");
                    ownedSubmarines.Add(new SubmarineInfo(ownedSubPath));
                }
            }
            return ownedSubmarines;
        }

        /*public static void LoadMultiplayerCampaignState(string filePath, MultiPlayerCampaign multiplayerCampaign)
        {
            DebugConsole.Log("Loading save file for an existing game session (" + filePath + ")");
            DecompressToDirectory(filePath, TempPath, null);
            XDocument doc = XMLExtensions.TryLoadXml(Path.Combine(TempPath, "gamesession.xml"));
            if (doc == null) { return; }
            gameSession.Load(doc.Root);
        }*/

        public static XDocument LoadGameSessionDoc(string filePath)
        {
            DebugConsole.Log("Loading game session doc: " + filePath);
            try
            {
                DecompressToDirectory(filePath, TempPath, null);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error decompressing " + filePath, e);
                return null;
            }

            return XMLExtensions.TryLoadXml(Path.Combine(TempPath, "gamesession.xml"));
        }

        public static bool IsSaveFileCompatible(XDocument saveDoc)
        {
            if (saveDoc?.Root?.Attribute("version") == null) { return false; }
            return true;
        }

        public static void DeleteSave(string filePath)
        {
            try
            {
                File.Delete(filePath);
            }

            catch (Exception e)
            {
                DebugConsole.ThrowError("ERROR: deleting save file \"" + filePath + "\" failed.", e);
            }

            //deleting a multiplayer save file -> also delete character data
            if (Path.GetFullPath(Path.GetDirectoryName(filePath)).Equals(Path.GetFullPath(MultiplayerSaveFolder)))
            {
                string characterDataSavePath = MultiPlayerCampaign.GetCharacterDataSavePath(filePath);
                if (File.Exists(characterDataSavePath))
                {
                    try
                    {
                        File.Delete(characterDataSavePath);
                    }
                    catch (Exception e)
                    {
                        DebugConsole.ThrowError("ERROR: deleting character data file \"" + characterDataSavePath + "\" failed.", e);
                    }
                }
            }
        }

        public static string GetSavePath(SaveType saveType, string saveName)
        {
            string folder = saveType == SaveType.Singleplayer ? SaveFolder : MultiplayerSaveFolder;
            return Path.Combine(folder, saveName);
        }

        public static IEnumerable<string> GetSaveFiles(SaveType saveType, bool includeInCompatible = true)
        {
            string folder = saveType == SaveType.Singleplayer ? SaveFolder : MultiplayerSaveFolder;
            if (!Directory.Exists(folder))
            {
                DebugConsole.Log("Save folder \"" + folder + " not found! Attempting to create a new folder...");
                try
                {
                    Directory.CreateDirectory(folder);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Failed to create the folder \"" + folder + "\"!", e);
                }
            }

            List<string> files = Directory.GetFiles(folder, "*.save", System.IO.SearchOption.TopDirectoryOnly).ToList();
            string legacyFolder = saveType == SaveType.Singleplayer ? LegacySaveFolder : LegacyMultiplayerSaveFolder;
            if (Directory.Exists(legacyFolder))
            {
                files.AddRange(Directory.GetFiles(legacyFolder, "*.save", System.IO.SearchOption.TopDirectoryOnly));
            }

            if (!includeInCompatible)
            {
                for (int i = files.Count - 1; i >= 0; i--)
                {
                    XDocument doc = LoadGameSessionDoc(files[i]);
                    if (!IsSaveFileCompatible(doc))
                    {
                        files.RemoveAt(i);
                    }
                }
            }
            return files;
        }

        public static string CreateSavePath(SaveType saveType, string fileName = "Save_Default")
        {
            fileName = ToolBox.RemoveInvalidFileNameChars(fileName);

            string folder = saveType == SaveType.Singleplayer ? SaveFolder : MultiplayerSaveFolder;
            if (fileName == "Save_Default")
            {
                fileName = TextManager.Get("SaveFile.DefaultName", true);
                if (fileName.Length == 0) fileName = "Save";
            }

            if (!Directory.Exists(folder))
            {
                DebugConsole.Log("Save folder \"" + folder + "\" not found. Created new folder");
                Directory.CreateDirectory(folder);
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
            // Write string to temporary file.
            string temp = Path.GetTempFileName();
            File.WriteAllText(temp, value);

            // B.
            // Read file into byte array buffer.
            byte[] b;
            using (FileStream f = File.Open(temp, System.IO.FileMode.Open))
            {
                b = new byte[f.Length];
                f.Read(b, 0, (int)f.Length);
            }

            // C.
            // Use GZipStream to write compressed bytes to target file.
            using (FileStream f2 = File.Open(fileName, System.IO.FileMode.Create))
            using (GZipStream gz = new GZipStream(f2, CompressionMode.Compress, false))
            {
                gz.Write(b, 0, b.Length);
            }
        }

        public static void CompressFile(string sDir, string sRelativePath, GZipStream zipStream)
        {
            //Compress file name
            char[] chars = sRelativePath.ToCharArray();
            zipStream.Write(BitConverter.GetBytes(chars.Length), 0, sizeof(int));
            foreach (char c in chars)
                zipStream.Write(BitConverter.GetBytes(c), 0, sizeof(char));

            //Compress file content
            byte[] bytes = File.ReadAllBytes(Path.Combine(sDir, sRelativePath));
            zipStream.Write(BitConverter.GetBytes(bytes.Length), 0, sizeof(int));
            zipStream.Write(bytes, 0, bytes.Length);
        }

        public static void CompressDirectory(string sInDir, string sOutFile, ProgressDelegate progress)
        {
            IEnumerable<string> sFiles = Directory.GetFiles(sInDir, "*.*", System.IO.SearchOption.AllDirectories);
            int iDirLen = sInDir[sInDir.Length - 1] == Path.DirectorySeparatorChar ? sInDir.Length : sInDir.Length + 1;

            using (FileStream outFile = File.Open(sOutFile, System.IO.FileMode.Create, System.IO.FileAccess.Write))
            using (GZipStream str = new GZipStream(outFile, CompressionMode.Compress))
                foreach (string sFilePath in sFiles)
                {
                    string sRelativePath = sFilePath.Substring(iDirLen);
                    progress?.Invoke(sRelativePath);
                    CompressFile(sInDir, sRelativePath, str);
                }
        }


        public static System.IO.Stream DecompressFiletoStream(string fileName)
        {
            using (FileStream originalFileStream = File.Open(fileName, System.IO.FileMode.Open))
            {
                System.IO.MemoryStream decompressedFileStream = new System.IO.MemoryStream();

                using (GZipStream decompressionStream = new GZipStream(originalFileStream, CompressionMode.Decompress))
                {
                    decompressionStream.CopyTo(decompressedFileStream);
                    return decompressedFileStream;
                }
            }
        }

        private static bool DecompressFile(bool writeFile, string sDir, GZipStream zipStream, ProgressDelegate progress, out string fileName)
        {
            fileName = null;
            
            //Decompress file name
            byte[] bytes = new byte[sizeof(int)];
            int Readed = zipStream.Read(bytes, 0, sizeof(int));
            if (Readed < sizeof(int))
                return false;

            int iNameLen = BitConverter.ToInt32(bytes, 0);
            if (iNameLen > 255)
            {
                throw new Exception("Failed to decompress \"" + sDir + "\" (file name length > 255). The file may be corrupted.");
            }

            bytes = new byte[sizeof(char)];
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < iNameLen; i++)
            {
                zipStream.Read(bytes, 0, sizeof(char));
                char c = BitConverter.ToChar(bytes, 0);
                sb.Append(c);
            }
            string sFileName = sb.ToString();
            
            fileName = sFileName;
            progress?.Invoke(sFileName);

            //Decompress file content
            bytes = new byte[sizeof(int)];
            zipStream.Read(bytes, 0, sizeof(int));
            int iFileLen = BitConverter.ToInt32(bytes, 0);

            bytes = new byte[iFileLen];
            zipStream.Read(bytes, 0, bytes.Length);

            string sFilePath = Path.Combine(sDir, sFileName);
            string sFinalDir = Path.GetDirectoryName(sFilePath);

            string sDirFull = (string.IsNullOrEmpty(sDir) ? Directory.GetCurrentDirectory() : Path.GetFullPath(sDir)).CleanUpPathCrossPlatform(correctFilenameCase: false);
            string sFinalDirFull = (string.IsNullOrEmpty(sFinalDir) ? Directory.GetCurrentDirectory() : Path.GetFullPath(sFinalDir)).CleanUpPathCrossPlatform(correctFilenameCase: false);
            
            if (!sFinalDirFull.StartsWith(sDirFull, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Error extracting \"{sFileName}\": cannot be extracted to parent directory");
            }

            if (!writeFile) { return true; }
            if (!Directory.Exists(sFinalDir))
                Directory.CreateDirectory(sFinalDir);

            int maxRetries = 4;
            for (int i = 0; i <= maxRetries; i++)
            {
                try
                {
                    using (FileStream outFile = File.Open(sFilePath, System.IO.FileMode.Create, System.IO.FileAccess.Write))
                    {
                        outFile.Write(bytes, 0, iFileLen);
                    }
                    break;
                }
                catch (System.IO.IOException e)
                {
                    if (i >= maxRetries || !File.Exists(sFilePath)) { throw; }
                    DebugConsole.NewMessage("Failed decompress file \"" + sFilePath + "\" {" + e.Message + "}, retrying in 250 ms...", Color.Red);
                    Thread.Sleep(250);
                }
            }
            return true;
        }

        public static void DecompressToDirectory(string sCompressedFile, string sDir, ProgressDelegate progress)
        {
            DebugConsole.Log("Decompressing " + sCompressedFile + " to " + sDir + "...");
            int maxRetries = 4;
            for (int i = 0; i <= maxRetries; i++)
            {
                try
                {
                    using (FileStream inFile = File.Open(sCompressedFile, System.IO.FileMode.Open, System.IO.FileAccess.Read))
                    using (GZipStream zipStream = new GZipStream(inFile, CompressionMode.Decompress, true))
                        while (DecompressFile(true, sDir, zipStream, progress, out _)) { };

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
            int maxRetries = 4;
            HashSet<string> paths = new HashSet<string>();
            for (int i = 0; i <= maxRetries; i++)
            {
                try
                {
                    using FileStream inFile = File.Open(sCompressedFile, System.IO.FileMode.Open, System.IO.FileAccess.Read);
                    using GZipStream zipStream = new GZipStream(inFile, CompressionMode.Decompress, true);
                    while (DecompressFile(false, "", zipStream, null, out string fileName))
                    {
                        paths.Add(fileName);
                    }
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

        public static void CopyFolder(string sourceDirName, string destDirName, bool copySubDirs, bool overwriteExisting = false)
        {
            // Get the subdirectories for the specified directory.
            DirectoryInfo dir = new DirectoryInfo(sourceDirName);

            if (!dir.Exists)
            {
                throw new System.IO.DirectoryNotFoundException(
                    "Source directory does not exist or could not be found: "
                    + sourceDirName);
            }

            IEnumerable<DirectoryInfo> dirs = dir.GetDirectories();
            // If the destination directory doesn't exist, create it.
            if (!Directory.Exists(destDirName))
            {
                Directory.CreateDirectory(destDirName);
            }

            // Get the files in the directory and copy them to the new location.
            IEnumerable<FileInfo> files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string tempPath = Path.Combine(destDirName, file.Name);
                if (!overwriteExisting && File.Exists(tempPath)) { continue; }
                file.CopyTo(tempPath, true);
            }

            // If copying subdirectories, copy them and their contents to new location.
            if (copySubDirs)
            {
                foreach (DirectoryInfo subdir in dirs)
                {
                    string tempPath = Path.Combine(destDirName, subdir.Name);
                    CopyFolder(subdir.FullName, tempPath, copySubDirs, overwriteExisting);
                }
            }
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

        public static void ClearFolder(string folderName, string[] ignoredFileNames = null)
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
                int maxRetries = 4;
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
    }
}
