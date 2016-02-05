using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    public enum ContentType
    {
        None, Jobs, Item, Character, Structure, Executable, RandomEvents
    }

    public class ContentPackage
    {

        public static string Folder = "Data/ContentPackages/";

        public static List<ContentPackage> list = new List<ContentPackage>();


        string name;

        public string Name
        {
            get { return name; }
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

        public List<ContentFile> files;

        private ContentPackage()
        {
            files = new List<ContentFile>();
        }

        public ContentPackage(string filePath)
            : this()
        {
            XDocument doc = ToolBox.TryLoadXml(filePath);

            Path = filePath;

            if (doc==null)
            {
                DebugConsole.ThrowError("Couldn't load content package ''"+filePath+"''!");
                return;
            }


            name = ToolBox.GetAttributeString(doc.Root, "name", "");
            
            foreach (XElement subElement in doc.Root.Elements())
            {
                ContentType type = (ContentType)Enum.Parse(typeof(ContentType), subElement.Name.ToString(), true);
                files.Add(new ContentFile(ToolBox.GetAttributeString(subElement, "file", ""), type));                
            }
        }

        public override string ToString()
        {
            return name;
        }

        public static ContentPackage CreatePackage(string name)
        {
            ContentPackage newPackage = new ContentPackage("Content/Data/"+name);
            newPackage.name = name;
            newPackage.Path = Folder + name;
            list.Add(newPackage);

            return newPackage;
        }

        public ContentFile AddFile(string path, ContentType type)
        {
            if (files.Find(file => file.path == path && file.type == type) != null) return null;

            ContentFile cf = new ContentFile(path, type);
            files.Add(cf);

            return cf;
        }

        public void RemoveFile(ContentFile file)
        {
            files.Remove(file);
        }

        public void Save(string filePath)
        {
            XDocument doc = new XDocument();
            doc.Add(new XElement("contentpackage", 
                new XAttribute("name", name), 
                new XAttribute("path", Path)));

            //doc.Root.Add(
            //    new XElement("jobs", new XAttribute("file", JobFile)),
            //    new XElement("structures", new XAttribute("file", StructureFile)));

            foreach (ContentFile file in files)
            {
                doc.Root.Add(new XElement(file.type.ToString(), new XAttribute("file", file.path)));
            }

            //foreach (string itemFile in itemFiles)
            //{
            //    doc.Root.Add(new XElement("item", new XAttribute("file", itemFile)));
            //}
            doc.Save(System.IO.Path.Combine(filePath, name+".xml"));
        }

        private void CalculateHash()
        {
            List<byte[]> hashes = new List<byte[]>();

            //foreach (ContentFile file in files)
            //{
            //    if (file.path.EndsWith(".xml", true, System.Globalization.CultureInfo.InvariantCulture))
            //    {
            //        XDocument doc = ToolBox.TryLoadXml(file.path);
            //        sb.Append(doc.ToString());
            //    }          
            //}
            var md5 = MD5.Create();
            foreach (ContentFile file in files)
            {
                if (file.type == ContentType.Executable) continue;

                try 
                {
                    using (var stream = File.OpenRead(file.path))
                    {
                        hashes.Add(md5.ComputeHash(stream));
                    }               
                }

                catch (Exception e)
                {
                    DebugConsole.ThrowError("Error while calculating content package hash: ", e);
                }
             
            }

            //string str = sb.ToString();
            byte[] bytes = new byte[hashes.Count()*16];
            for (int i = 0; i < hashes.Count; i++ )
            {
                hashes[i].CopyTo(bytes, i*16);
            }
                //System.Buffer.BlockCopy(str.ToCharArray(), 0, bytes, 0, bytes.Length);

            md5Hash = new Md5Hash(bytes);
        }

        public List<string> GetFilesOfType(ContentType type)
        {
            List<ContentFile> contentFiles = files.FindAll(f => f.type == type);

            List<string> filePaths = new List<string>();
            foreach (ContentFile contentFile in contentFiles)
            {
                filePaths.Add(contentFile.path);
            }
            return filePaths;
        }

        public static void LoadAll(string folder)
        {
            if (!Directory.Exists(folder))
            {
                try
                {
                    Directory.CreateDirectory(folder);
                }
                catch
                {
                    return;
                }
            }

            string[] files = Directory.GetFiles(folder, "*.xml");

            list.Clear();

            foreach (string filePath in files)
            {
                ContentPackage package = new ContentPackage(filePath);
                list.Add(package);
            }
        }
    }

    public class ContentFile
    {
        public string path;
        public ContentType type;

        public ContentFile(string path, ContentType type)
        {
            Directory.GetCurrentDirectory();
            //Path.get
            this.path = path;
            this.type = type;
        }

        public override string ToString()
        {
            return path;
        }
    }

}
