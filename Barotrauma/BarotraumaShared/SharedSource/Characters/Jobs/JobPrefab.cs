using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Xml.Linq;
using Barotrauma.Extensions;
using System;
using System.Linq;

namespace Barotrauma
{
    public class AutonomousObjective
    {
        public string identifier;
        public string option;
        public float priorityModifier;

        public AutonomousObjective(XElement element)
        {
            identifier = element.GetAttributeString("identifier", null);

            //backwards compatibility
            if (string.IsNullOrEmpty(identifier))
            {
                identifier = element.GetAttributeString("aitag", null);
            }

            option = element.GetAttributeString("option", null);
            priorityModifier = element.GetAttributeFloat("prioritymodifier", 1);
            priorityModifier = MathHelper.Max(priorityModifier, 0);
        }
    }

    partial class JobPrefab : IPrefab, IDisposable
    {
        public static readonly PrefabCollection<JobPrefab> Prefabs = new PrefabCollection<JobPrefab>();

        private bool disposed = false;
        public void Dispose()
        {
            if (disposed) { return; }
            disposed = true;
            Prefabs.Remove(this);
        }

        public static XElement NoJobElement;
        public static JobPrefab Get(string identifier)
        {
            if (Prefabs == null)
            {
                DebugConsole.ThrowError("Issue in the code execution order: job prefabs not loaded.");
                return null;
            }
            if (Prefabs.ContainsKey(identifier))
            {
                return Prefabs[identifier];
            }
            else
            {
                DebugConsole.ThrowError("Couldn't find a job prefab with the given identifier: " + identifier);
                return null;
            }
        }

        public readonly Dictionary<int, XElement> ItemSets = new Dictionary<int, XElement>();
        public readonly Dictionary<int, List<string>> ItemIdentifiers = new Dictionary<int, List<string>>();
        public readonly Dictionary<int, Dictionary<string, bool>> ShowItemPreview = new Dictionary<int, Dictionary<string, bool>>();
        public readonly List<SkillPrefab> Skills = new List<SkillPrefab>();
        public readonly List<AutonomousObjective> AutomaticOrders = new List<AutonomousObjective>();
        public readonly List<string> AppropriateOrders = new List<string>();

        [Serialize("1,1,1,1", false)]
        public Color UIColor
        {
            get;
            private set;
        }

        [Serialize("notfound", false)]
        public string Identifier
        {
            get;
            private set;
        }

        [Serialize("notfound", false)]
        public string Name
        {
            get;
            private set;
        }

        public string OriginalName { get { return Identifier; } }

        public ContentPackage ContentPackage { get; private set; }

        [Serialize("", false)]
        public string Description
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool OnlyJobSpecificDialog
        {
            get;
            private set;
        }

        //the number of these characters in the crew the player starts with in the single player campaign
        [Serialize(0, false)]
        public int InitialCount
        {
            get;
            private set;
        }

        //if set to true, a client that has chosen this as their preferred job will get it no matter what
        [Serialize(false, false)]
        public bool AllowAlways
        {
            get;
            private set;
        }

        //how many crew members can have the job (only one captain etc) 
        [Serialize(100, false)]
        public int MaxNumber
        {
            get;
            private set;
        }

        //how many crew members are REQUIRED to have the job 
        //(i.e. if one captain is required, one captain is chosen even if all the players have set captain to lowest preference)
        [Serialize(0, false)]
        public int MinNumber
        {
            get;
            private set;
        }

        [Serialize(0.0f, false)]
        public float MinKarma
        {
            get;
            private set;
        }

        [Serialize(10.0f, false)]
        public float Commonness
        {
            get;
            private set;
        }

        //how much the vitality of the character is increased/reduced from the default value
        [Serialize(0.0f, false)]
        public float VitalityModifier
        {
            get;
            private set;
        }

        public Sprite Icon;
        public string FilePath { get; private set; }

        public XElement Element { get; private set; }
        public XElement ClothingElement { get; private set; }
        public int Variants { get; private set; }

        public JobPrefab(XElement element, string filePath)
        {
            FilePath = filePath;
            SerializableProperty.DeserializeProperties(this, element);

            Name = TextManager.Get("JobName." + Identifier);
            Description = TextManager.Get("JobDescription." + Identifier);
            Identifier = Identifier.ToLowerInvariant();
            Element = element;

            int variant = 0;
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "itemset":
                        ItemSets.Add(variant, subElement);
                        ItemIdentifiers[variant] = new List<string>();
                        ShowItemPreview[variant] = new Dictionary<string, bool>();
                        loadItemIdentifiers(subElement, variant);
                        variant++;
                        break;
                    case "skills":
                        foreach (XElement skillElement in subElement.Elements())
                        {
                            Skills.Add(new SkillPrefab(skillElement));
                        }
                        break;
                    case "autonomousobjectives":
                        subElement.Elements().ForEach(order => AutomaticOrders.Add(new AutonomousObjective(order)));
                        break;
                    case "appropriateobjectives":
                    case "appropriateorders":
                        subElement.Elements().ForEach(order => AppropriateOrders.Add(order.GetAttributeString("identifier", "").ToLowerInvariant()));
                        break;
                    case "jobicon":
                        Icon = new Sprite(subElement.FirstElement());
                        break;
                }
            }

            void loadItemIdentifiers(XElement parentElement, int variant)
            {
                foreach (XElement itemElement in parentElement.GetChildElements("Item"))
                {
                    if (itemElement.Element("name") != null)
                    {
                        DebugConsole.ThrowError("Error in job config \"" + Name + "\" - use identifiers instead of names to configure the items.");
                        continue;
                    }

                    string itemIdentifier = itemElement.GetAttributeString("identifier", "");
                    if (string.IsNullOrWhiteSpace(itemIdentifier))
                    {
                        DebugConsole.ThrowError("Error in job config \"" + Name + "\" - item with no identifier.");
                    }
                    else
                    {
                        ItemIdentifiers[variant].Add(itemIdentifier);
                        ShowItemPreview[variant][itemIdentifier] = itemElement.GetAttributeBool("showpreview", true);
                    }
                    loadItemIdentifiers(itemElement, variant);
                }
            }

            Variants = variant;

            Skills.Sort((x,y) => y.LevelRange.X.CompareTo(x.LevelRange.X));

            ClothingElement = element.GetChildElement("PortraitClothing");
        }
        

        public static JobPrefab Random(Rand.RandSync sync = Rand.RandSync.Unsynced) => Prefabs.GetRandom(sync);

        public static void LoadAll(IEnumerable<ContentFile> files)
        {
            foreach (ContentFile file in files)
            {
                LoadFromFile(file);
            }
        }

        public static void LoadFromFile(ContentFile file)
        {
            XDocument doc = XMLExtensions.TryLoadXml(file.Path);
            if (doc == null) { return; }
            var mainElement = doc.Root.IsOverride() ? doc.Root.FirstElement() : doc.Root;
            if (doc.Root.IsOverride())
            {
                DebugConsole.ThrowError($"Error in '{file.Path}': Cannot override all job prefabs, because many of them are required by the main game! Please try overriding jobs one by one.");
            }
            foreach (XElement element in mainElement.Elements())
            {
                if (element.Name.ToString().Equals("nojob", StringComparison.OrdinalIgnoreCase)) { continue; }
                if (element.IsOverride())
                {
                    var job = new JobPrefab(element.FirstElement(), file.Path)
                    {
                        ContentPackage = file.ContentPackage
                    };
                    Prefabs.Add(job, true);
                }
                else
                {
                    var job = new JobPrefab(element, file.Path)
                    {
                        ContentPackage = file.ContentPackage
                    };
                    Prefabs.Add(job, false);
                }
            }
            NoJobElement = NoJobElement ?? mainElement.Element("NoJob");
            NoJobElement = NoJobElement ?? mainElement.Element("nojob");
        }

        public static void RemoveByFile(string filePath)
        {
            Prefabs.RemoveByFile(filePath);
        }
    }
}
