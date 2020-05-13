using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace Barotrauma
{
    public static class UpdaterUtil
    {
        public const string Version = "1.1";

        public static void SaveFileList(string filePath)
        {
            XDocument doc = new XDocument(CreateFileList());

            doc.Save(filePath);
        }

        public static XElement CreateFileList()
        {
            XElement root = new XElement("filelist");
            string currentDir = Directory.GetCurrentDirectory();

            string[] files = Directory.GetFiles(currentDir, "*", SearchOption.AllDirectories);
            
            foreach (string file in files)
            {
                XElement fileElement = new XElement("file");
                fileElement.Add(new XAttribute("path", GetRelativePath(file, currentDir)));
                fileElement.Add(new XAttribute("md5", GetFileMd5Hash(file)));

                root.Add(fileElement);
            }

            return root;
        }

        public static List<string> GetFileList(XDocument fileListDoc)
        {
            List<string> fileList = new List<string>();

            XElement fileListElement = fileListDoc.Root;

            if (fileListElement == null)
            {
                throw new Exception("Received list of new files was corrupted");
            }

            foreach (XElement file in fileListElement.Elements())
            {
                string filePath = file.GetAttributeString("path", "");

                fileList.Add(filePath);
            }

            return fileList;
        }

        public static List<string> GetRequiredFiles(XDocument fileListDoc)
        {
            List<string> requiredFiles = new List<string>();

            XElement fileList = fileListDoc.Root;

            if (fileList==null)
            {
                throw new Exception("Received list of new files was corrupted");
            }

            foreach (XElement file in fileList.Elements())
            {
                string filePath = file.GetAttributeString("path", "");

                if (!File.Exists(filePath))
                {
                    requiredFiles.Add(filePath);
                    continue;
                }

                string md5 = file.GetAttributeString("md5", "");

                if (GetFileMd5Hash(filePath) != md5)
                {
                    requiredFiles.Add(filePath);
                }
            }

            return requiredFiles;
        }

        private static string GetFileMd5Hash(string filePath)
        {
            Md5Hash md5Hash = null;
            var md5 = MD5.Create();
            using (var stream = File.OpenRead(filePath))
            {
                md5Hash = new Md5Hash(md5.ComputeHash(stream));
            }

            return md5Hash.Hash;
        }

        public static string GetRelativePath(string filespec, string folder)
        {
            Uri pathUri = new Uri(filespec);
            // Folders must end in a slash
            if (!folder.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                folder += Path.DirectorySeparatorChar;
            }
            Uri folderUri = new Uri(folder);
            return Uri.UnescapeDataString(folderUri.MakeRelativeUri(pathUri).ToString().Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>
        /// moves the files in the updatefolder to the install folder
        /// if there's an existing file with the same name in the install folder and it can't be removed,
        /// it will be renamed as "OLD_[filename]"
        /// </summary>
        /// <param name="updateFileFolder"></param>
        public static void InstallUpdatedFiles(string updateFileFolder)
        {
            string[] files = Directory.GetFiles(updateFileFolder, "*", SearchOption.AllDirectories);

            string currentDir = Directory.GetCurrentDirectory();

            foreach (string file in files)
            {
                string fileRelPath = GetRelativePath(file, updateFileFolder);

                if (File.Exists(fileRelPath))
                {
                    try
                    {
                        File.Delete(fileRelPath);
                    }

                    //couldn't delete file, probably because it's already in use
                    catch
                    {
                        string oldFileName =  Path.Combine(currentDir, Path.GetDirectoryName(fileRelPath), "OLD_"+Path.GetFileName(fileRelPath));

                        if (File.Exists(oldFileName)) File.Delete(oldFileName);
                        
                        File.Move(fileRelPath, oldFileName);
                    }
                }

                string directoryName = Path.GetDirectoryName(fileRelPath);
                if (!string.IsNullOrWhiteSpace(directoryName))
                {
                    Directory.CreateDirectory(directoryName);
                }


                System.Diagnostics.Debug.WriteLine("moving: "+file+"  ->    "+fileRelPath);
                File.Move(file, fileRelPath);
            }

            Directory.Delete(updateFileFolder, true);
        }

        public static void CleanUnnecessaryFiles(List<string> filesToKeep)
        {
            string currentDir = Directory.GetCurrentDirectory();

            string[] files = Directory.GetFiles(currentDir, "*", SearchOption.AllDirectories);

            foreach (string file in files)
            {
                string relativePath = GetRelativePath(file, currentDir);

                string dirRoot = relativePath.Split(Path.DirectorySeparatorChar).First();
                if (dirRoot != "Content") continue;

                if (filesToKeep.Contains(relativePath)) continue;

                if (Path.GetFileName(file).Split('_').First() == "OLD") continue;

                System.Diagnostics.Debug.WriteLine("deleting file "+file);

                try
                {
                    File.Delete(file);
                }

                catch (Exception e)
                {
                    System.Diagnostics.Debug.WriteLine("Could not delete file \"" + file + "\" (" + e.Message + ")");
                    continue;
                }
            }
        }


        public static void CleanOldFiles()
        {
            string currentDir = Directory.GetCurrentDirectory();

            string[] files = Directory.GetFiles(currentDir, "*", SearchOption.AllDirectories);

            foreach (string file in files)
            {
                if (Path.GetFileName(file).Split('_').First() != "OLD") continue;

                System.Diagnostics.Debug.WriteLine("deleting file " + file);

                try
                {
                    File.Delete(file);
                }

                catch (Exception e)
                {
                    System.Diagnostics.Debug.WriteLine("Could not delete file \"" + file + "\" (" + e.Message + ")");
                    continue;
                }
            }
        }
    }
}
