using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{

    public partial class GameSettings
    {
        
        public ContentPackage SelectedContentPackage { get; set; }

        public string   MasterServerUrl { get; set; }
        public bool     AutoCheckUpdates { get; set; }
        public bool     WasGameUpdated { get; set; }

        public static bool VerboseLogging { get; set; }

        public GameSettings(string filePath)
        {
            ContentPackage.LoadAll(ContentPackage.Folder);

            Load(filePath);
        }

        public void Load(string filePath)
        {
            XDocument doc = XMLExtensions.TryLoadXml(filePath);

            if (doc == null)
            {
                DebugConsole.ThrowError("No config file found");

                MasterServerUrl = "";
                
                SelectedContentPackage = ContentPackage.list.Any() ? ContentPackage.list[0] : new ContentPackage("");

                return;
            }
            
            MasterServerUrl = doc.Root.GetAttributeString("masterserverurl", "");

            AutoCheckUpdates = doc.Root.GetAttributeBool("autocheckupdates", true);
            WasGameUpdated = doc.Root.GetAttributeBool("wasgameupdated", false);

            VerboseLogging = doc.Root.GetAttributeBool("verboselogging", false);

            InitProjSpecific(doc);

            foreach (XElement subElement in doc.Root.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "contentpackage":
                        string path = subElement.GetAttributeString("path", "");


                        SelectedContentPackage = ContentPackage.list.Find(cp => cp.Path == path);

                        if (SelectedContentPackage == null) SelectedContentPackage = new ContentPackage(path);
                        break;
                }
            }
        }

        partial void InitProjSpecific(XDocument doc);
    }
}
