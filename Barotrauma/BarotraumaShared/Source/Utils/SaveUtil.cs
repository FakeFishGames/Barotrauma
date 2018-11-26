using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class SaveUtil
    {
        public static string SaveFolder = "Data" + Path.DirectorySeparatorChar + "Saves";
        public static string MultiplayerSaveFolder = "Data" + Path.DirectorySeparatorChar + "Saves" + Path.DirectorySeparatorChar + "Multiplayer";

        public delegate void ProgressDelegate(string sMessage);

        public static string TempPath
        {
            get { return Path.Combine(SaveFolder, "temp"); }
        }
        
        public enum SaveType
        {
            Singleplayer,
            Multiplayer
        }

        public static void SaveGame(string filePath)
        {
            string tempPath = Path.Combine(SaveFolder, "temp");

            Directory.CreateDirectory(tempPath);
            try
            {
                ClearFolder(tempPath, new string[] { GameMain.GameSession.Submarine.FilePath });
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to clear folder", e);
            }

            try
            {
                if (Submarine.MainSub != null)
                {
                    string subPath = Path.Combine(tempPath, Submarine.MainSub.Name + ".sub");
                    if (Submarine.Loaded.Contains(Submarine.MainSub))
                    {
                        Submarine.MainSub.FilePath = subPath;
                        Submarine.MainSub.SaveAs(Submarine.MainSub.FilePath);
                    }
                    else if (Submarine.MainSub.FilePath != subPath)
                    {
                        File.Copy(Submarine.MainSub.FilePath, subPath);
                        Submarine.MainSub.FilePath = subPath;
                    }
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error saving submarine", e);
            }

            try
            {
                GameMain.GameSession.Save(Path.Combine(tempPath, "gamesession.xml"));
            }

            catch (Exception e)
            {
                DebugConsole.ThrowError("Error saving gamesession", e);
            }

            try
            {
                CompressDirectory(tempPath, filePath, null);
            }

            catch (Exception e)
            {
                DebugConsole.ThrowError("Error compressing save file", e);
            }
        }

        public static void LoadGame(string filePath)
        {
            DecompressToDirectory(filePath, TempPath, null);

            XDocument doc = XMLExtensions.TryLoadXml(Path.Combine(TempPath, "gamesession.xml"));

            string subPath = Path.Combine(TempPath, doc.Root.GetAttributeString("submarine", "")) + ".sub";
            Submarine selectedSub = new Submarine(subPath, "");
            GameMain.GameSession = new GameSession(selectedSub, filePath, doc);
        }

        public static void LoadGame(string filePath, GameSession gameSession)
        {
            DecompressToDirectory(filePath, TempPath, null);
            XDocument doc = XMLExtensions.TryLoadXml(Path.Combine(TempPath, "gamesession.xml"));
            gameSession.Load(doc.Root);
        }

        public static XDocument LoadGameSessionDoc(string filePath)
        {
            string tempPath = Path.Combine(SaveFolder, "temp");

            try
            {
                DecompressToDirectory(filePath, tempPath, null);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error decompressing " + filePath, e);
                return null;
            }

            return XMLExtensions.TryLoadXml(Path.Combine(tempPath, "gamesession.xml"));
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

        public static string[] GetSaveFiles(SaveType saveType)
        {
            string folder = saveType == SaveType.Singleplayer ? SaveFolder : MultiplayerSaveFolder;

            if (!Directory.Exists(folder))
            {
                DebugConsole.Log("Save folder \"" + folder + " not found! Attempting to create a new folder");
                try
                {
                    Directory.CreateDirectory(folder);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Failed to create the folder \"" + folder + "\"!", e);
                }
            }

            string[] files = Directory.GetFiles(folder, "*.save");

            /*for (int i = 0; i < files.Length; i++)
            {
                files[i] = Path.GetFileNameWithoutExtension(files[i]);
            }*/

            return files;
        }
        
        public static string CreateSavePath(SaveType saveType, string fileName = "Save")
        {
            string folder = saveType == SaveType.Singleplayer ? SaveFolder : MultiplayerSaveFolder;

            if (!Directory.Exists(folder))
            {
                DebugConsole.ThrowError("Save folder \"" + folder + "\" not found. Created new folder");
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
            using (FileStream f = new FileStream(temp, FileMode.Open))
            {
                b = new byte[f.Length];
                f.Read(b, 0, (int)f.Length);
            }

            // C.
            // Use GZipStream to write compressed bytes to target file.
            using (FileStream f2 = new FileStream(fileName, FileMode.Create))
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
            string[] sFiles = Directory.GetFiles(sInDir, "*.*", SearchOption.AllDirectories);
            int iDirLen = sInDir[sInDir.Length - 1] == Path.DirectorySeparatorChar ? sInDir.Length : sInDir.Length + 1;

            using (FileStream outFile = new FileStream(sOutFile, FileMode.Create, FileAccess.Write, FileShare.None))
            using (GZipStream str = new GZipStream(outFile, CompressionMode.Compress))
                foreach (string sFilePath in sFiles)
                {
                    string sRelativePath = sFilePath.Substring(iDirLen);
                    if (progress != null)
                        progress(sRelativePath);
                    CompressFile(sInDir, sRelativePath, str);
                }
        }


        public static Stream DecompressFiletoStream(string fileName)
        {
            if (!File.Exists(fileName))
            {
                DebugConsole.ThrowError("File \"" + fileName + " doesn't exist!");
                return null;
            }

            using (FileStream originalFileStream = new FileStream(fileName, FileMode.Open))
            {
                MemoryStream decompressedFileStream = new MemoryStream();

                using (GZipStream decompressionStream = new GZipStream(originalFileStream, CompressionMode.Decompress))
                {
                    decompressionStream.CopyTo(decompressedFileStream);
                    return decompressedFileStream;
                }
            }
        }

        public static bool DecompressFile(string sDir, GZipStream zipStream, ProgressDelegate progress)
        {
            //Decompress file name
            byte[] bytes = new byte[sizeof(int)];
            int Readed = zipStream.Read(bytes, 0, sizeof(int));
            if (Readed < sizeof(int))
                return false;

            int iNameLen = BitConverter.ToInt32(bytes, 0);
            if (iNameLen > 255)
            {
                throw new Exception("Failed to decompress \""+sDir+"\" (file name length > 255). The file may be corrupted.");
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
            if (progress != null)
                progress(sFileName);

            //Decompress file content
            bytes = new byte[sizeof(int)];
            zipStream.Read(bytes, 0, sizeof(int));
            int iFileLen = BitConverter.ToInt32(bytes, 0);

            bytes = new byte[iFileLen];
            zipStream.Read(bytes, 0, bytes.Length);

            string sFilePath = Path.Combine(sDir, sFileName);
            string sFinalDir = Path.GetDirectoryName(sFilePath);
            if (!Directory.Exists(sFinalDir))
                Directory.CreateDirectory(sFinalDir);

            using (FileStream outFile = new FileStream(sFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                outFile.Write(bytes, 0, iFileLen);

            return true;
        }

        public static void DecompressToDirectory(string sCompressedFile, string sDir, ProgressDelegate progress)
        {
            using (FileStream inFile = new FileStream(sCompressedFile, FileMode.Open, FileAccess.Read, FileShare.None))
            using (GZipStream zipStream = new GZipStream(inFile, CompressionMode.Decompress, true))
                while (DecompressFile(sDir, zipStream, progress)) ;
        }
        
        public static void ClearFolder(string FolderName, string[] ignoredFiles = null)
        {
            DirectoryInfo dir = new DirectoryInfo(FolderName);

            foreach (FileInfo fi in dir.GetFiles())
            {
                if (ignoredFiles != null)
                {
                    bool ignore = false;
                    foreach (string ignoredFile in ignoredFiles)
                    {
                        if (Path.GetFullPath(fi.FullName).Equals(Path.GetFullPath(ignoredFile)))
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
                ClearFolder(di.FullName, ignoredFiles);
                di.Delete();
            }
        }
    }
}
