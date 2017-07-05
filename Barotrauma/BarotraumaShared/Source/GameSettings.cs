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
            XDocument doc = ToolBox.TryLoadXml(filePath);

            if (doc == null)
            {
                DebugConsole.ThrowError("No config file found");

                MasterServerUrl = "";
                
                SelectedContentPackage = ContentPackage.list.Any() ? ContentPackage.list[0] : new ContentPackage("");

                return;
            }
            
            MasterServerUrl = ToolBox.GetAttributeString(doc.Root, "masterserverurl", "");

            AutoCheckUpdates = ToolBox.GetAttributeBool(doc.Root, "autocheckupdates", true);
            WasGameUpdated = ToolBox.GetAttributeBool(doc.Root, "wasgameupdated", false);

            VerboseLogging = ToolBox.GetAttributeBool(doc.Root, "verboselogging", false);

            InitProjSpecific(doc);

            foreach (XElement subElement in doc.Root.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "contentpackage":
                        string path = ToolBox.GetAttributeString(subElement, "path", "");


                        SelectedContentPackage = ContentPackage.list.Find(cp => cp.Path == path);

                        if (SelectedContentPackage == null) SelectedContentPackage = new ContentPackage(path);
                        break;
                }
            }
        }

        partial void InitProjSpecific(XDocument doc);
    }
}
