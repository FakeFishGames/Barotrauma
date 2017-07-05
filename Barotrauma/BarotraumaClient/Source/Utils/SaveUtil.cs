using System;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace Barotrauma
{
    public partial class SaveUtil
    {
        public static void SaveGame(string fileName)
        {
            fileName = Path.Combine(SaveFolder, fileName);

            string tempPath = Path.Combine(SaveFolder, "temp");
            
            Directory.CreateDirectory(tempPath);
            try
            {
                ClearFolder(tempPath, new string[] { GameMain.GameSession.Submarine.FilePath });
            }
            catch
            {

            }
            
            try 
            {
                if (Submarine.MainSub != null && Submarine.Loaded.Contains(Submarine.MainSub))
                {
                    Submarine.MainSub.FilePath = Path.Combine(tempPath, Submarine.MainSub.Name + ".sub");
                    Submarine.MainSub.SaveAs(Submarine.MainSub.FilePath);                
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
                CompressDirectory(tempPath, fileName+".save", null);
            }

            catch (Exception e)
            {
                DebugConsole.ThrowError("Error compressing save file", e);
            }
        }

        public static void LoadGame(string fileName)
        {
            string filePath = Path.Combine(SaveFolder, fileName+".save");

            DecompressToDirectory(filePath, TempPath, null);

            XDocument doc = ToolBox.TryLoadXml(Path.Combine(TempPath, "gamesession.xml"));

            string subPath = Path.Combine(TempPath, ToolBox.GetAttributeString(doc.Root, "submarine", ""))+".sub";
            Submarine selectedMap = new Submarine(subPath, "");// Submarine.Load();
            GameMain.GameSession = new GameSession(selectedMap, fileName, doc);

            //Directory.Delete(tempPath, true);
        }

        public static XDocument LoadGameSessionDoc(string fileName)
        {
            string filePath = Path.Combine(SaveFolder, fileName + ".save");

            string tempPath = Path.Combine(SaveFolder, "temp");

            try
            {
                DecompressToDirectory(filePath, tempPath, null);
            }
            catch
            {
                return null;
            }

            return ToolBox.TryLoadXml(Path.Combine(tempPath, "gamesession.xml"));            
        }

        public static void DeleteSave(string fileName)
        {
            fileName = Path.Combine(SaveFolder, fileName + ".save");

            try
            {
                File.Delete(fileName);
            }

            catch (Exception e)
            {
                DebugConsole.ThrowError("ERROR: deleting save file \""+fileName+" failed.", e);
            }

        }

        public static string[] GetSaveFiles()
        {
            if (!Directory.Exists(SaveFolder))
            {
                DebugConsole.ThrowError("Save folder \"" + SaveFolder + " not found! Attempting to create a new folder");
                try
                {
                    Directory.CreateDirectory(SaveFolder);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Failed to create the folder \"" + SaveFolder + "\"!", e);
                }
            }

            string[] files = Directory.GetFiles(SaveFolder, "*.save");

            for (int i = 0; i < files.Length; i++)
            {
                files[i] = Path.GetFileNameWithoutExtension(files[i]);
            }

            return files;
        }

        public static string CreateSavePath(string fileName="Save")
        {
            if (!Directory.Exists(SaveFolder))
            {
                DebugConsole.ThrowError("Save folder \""+SaveFolder+"\" not found. Created new folder");
                Directory.CreateDirectory(SaveFolder);
            }

            string extension = ".save";
            string pathWithoutExtension = Path.Combine(SaveFolder, fileName);

            int i = 0;
            while (File.Exists(pathWithoutExtension + " " + i + extension))
            {
                i++;
            }

            return fileName + " " + i;
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
        
        private static void ClearFolder(string FolderName, string[] ignoredFiles = null)
        {
            DirectoryInfo dir = new DirectoryInfo(FolderName);

            foreach (FileInfo fi in dir.GetFiles())
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
