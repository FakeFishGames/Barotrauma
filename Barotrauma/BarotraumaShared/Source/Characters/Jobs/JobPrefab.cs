using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Xml.Linq;
using Barotrauma.Extensions;
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

    partial class JobPrefab
    {
        public static Dictionary<string, JobPrefab> List;

        public static XElement NoJobElement;
        public static JobPrefab Get(string identifier)
        {
            if (List == null)
            {
                DebugConsole.ThrowError("Issue in the code execution order: job prefabs not loaded.");
                return null;
            }
            if (List.TryGetValue(identifier, out JobPrefab job))
            {
                return job;
            }
            else
            {
                DebugConsole.ThrowError("Couldn't find a job prefab with the given identifier: " + identifier);
                return null;
            }
        }

        public readonly Dictionary<int, XElement> ItemSets = new Dictionary<int, XElement>();
        public readonly Dictionary<int, List<string>> ItemNames = new Dictionary<int, List<string>>();
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

        public XElement Element { get; private set; }
        public XElement ClothingElement { get; private set; }

        public JobPrefab(XElement element)
        {
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
                        var itemNames = new List<string>();
                        loadItemNames(subElement, itemNames);
                        ItemNames.Add(variant++, itemNames);
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
                }
            }

            void loadItemNames(XElement parentElement, List<string> itemNames)
            {
                foreach (XElement itemElement in parentElement.GetChildElements("Item"))
                {
                    if (itemElement.Element("name") != null)
                    {
                        DebugConsole.ThrowError("Error in job config \"" + Name + "\" - use identifiers instead of names to configure the items.");
                        itemNames.Add(itemElement.GetAttributeString("name", ""));
                        continue;
                    }

                    string itemIdentifier = itemElement.GetAttributeString("identifier", "");
                    if (string.IsNullOrWhiteSpace(itemIdentifier))
                    {
                        DebugConsole.ThrowError("Error in job config \"" + Name + "\" - item with no identifier.");
                        itemNames.Add("");
                    }
                    else
                    {
                        var prefab = MapEntityPrefab.Find(null, itemIdentifier) as ItemPrefab;
                        if (prefab == null)
                        {
                            DebugConsole.ThrowError("Error in job config \"" + Name + "\" - item prefab \"" + itemIdentifier + "\" not found.");
                            itemNames.Add("");
                        }
                        else
                        {
                            itemNames.Add(prefab.Name);
                        }
                    }
                    loadItemNames(itemElement, itemNames);
                }
            }

            Skills.Sort((x,y) => y.LevelRange.X.CompareTo(x.LevelRange.X));

            ClothingElement = element.GetChildElement("PortraitClothing");
        }
        
        public class OutfitPreview
        {
            /// <summary>
            /// Pair.First = sprite, Pair.Second = draw offset
            /// </summary>
            public readonly List<Pair<Sprite, Vector2>> Sprites;
            public Vector2 Dimensions;

            public OutfitPreview()
            {
                Sprites = new List<Pair<Sprite, Vector2>>();
                Dimensions = Vector2.One;
            }

            public void AddSprite(Sprite sprite, Vector2 drawOffset)
            {
                Sprites.Add(new Pair<Sprite, Vector2>(sprite, drawOffset));
            }
        }

        public List<OutfitPreview> GetJobOutfitSprites(Gender gender, out Vector2 maxDimensions)
        {
            List<OutfitPreview> outfitPreviews = new List<OutfitPreview>();
            maxDimensions = Vector2.One;
             
            var equipIdentifiers = Element.GetChildElements("ItemSet").Elements().Where(e => e.GetAttributeBool("outfit", false)).Select(e => e.GetAttributeString("identifier", ""));

            var outfitPrefabs = MapEntityPrefab.List.FindAll(me => me is ItemPrefab itemPrefab && equipIdentifiers.Contains(itemPrefab.Identifier));
            if (!outfitPrefabs.Any()) { return null; }

            for (int i = 0; i < outfitPrefabs.Count; i++)
            {
                var outfitPreview = new OutfitPreview();

                if (!ItemSets.TryGetValue(i, out var itemSetElement)) { continue; }
                var previewElement = itemSetElement.GetChildElement("PreviewSprites");
                if (previewElement == null)
                {
#if CLIENT
                    if (outfitPrefabs[i] is ItemPrefab prefab && prefab.InventoryIcon != null)
                    {
                        outfitPreview.AddSprite(prefab.InventoryIcon, Vector2.Zero);
                        outfitPreview.Dimensions = prefab.InventoryIcon.SourceRect.Size.ToVector2();
                        maxDimensions.X = MathHelper.Max(maxDimensions.X, outfitPreview.Dimensions.X);
                        maxDimensions.Y = MathHelper.Max(maxDimensions.Y, outfitPreview.Dimensions.Y);
                    }
#endif
                    outfitPreviews.Add(outfitPreview);
                    continue;
                }

                var children = previewElement.Elements().ToList();
                for (int n = 0; n < children.Count; n++)
                {
                    XElement spriteElement = children[n];
                    string spriteTexture = spriteElement.GetAttributeString("texture", "").Replace("[GENDER]", (gender == Gender.Female) ? "female" : "male");
                    var sprite = new Sprite(spriteElement, file: spriteTexture);
                    sprite.size = new Vector2(sprite.SourceRect.Width, sprite.SourceRect.Height);
                    outfitPreview.AddSprite(sprite, children[n].GetAttributeVector2("offset", Vector2.Zero));
                }

                outfitPreview.Dimensions = previewElement.GetAttributeVector2("dims", Vector2.One);
                maxDimensions.X = MathHelper.Max(maxDimensions.X, outfitPreview.Dimensions.X);
                maxDimensions.Y = MathHelper.Max(maxDimensions.Y, outfitPreview.Dimensions.Y);

                outfitPreviews.Add(outfitPreview);
            }

            return outfitPreviews;
        }

        public static JobPrefab Random(Rand.RandSync sync = Rand.RandSync.Unsynced) => List.Values.GetRandom(sync);

        public static void LoadAll(IEnumerable<string> filePaths)
        {
            List = new Dictionary<string, JobPrefab>();

            foreach (string filePath in filePaths)
            {
                XDocument doc = XMLExtensions.TryLoadXml(filePath);
                if (doc == null) { continue; }
                var mainElement = doc.Root.IsOverride() ? doc.Root.FirstElement() : doc.Root;            
                if (doc.Root.IsOverride())
                {
                    DebugConsole.ThrowError($"Error in '{filePath}': Cannot override all job prefabs, because many of them are required by the main game! Please try overriding jobs one by one.");
                }
                foreach (XElement element in mainElement.Elements())
                {
                    if (element.Name.ToString().ToLowerInvariant() == "nojob") { continue; }
                    if (element.IsOverride())
                    {
                        var job = new JobPrefab(element.FirstElement());
                        if (List.TryGetValue(job.Identifier, out JobPrefab duplicate))
                        {
                            DebugConsole.NewMessage($"Overriding the job '{duplicate.Identifier}' with another defined in '{filePath}'", Color.Yellow);
                            List.Remove(duplicate.Identifier);
                        }
                        List.Add(job.Identifier, job);
                    }
                    else
                    {
                        if (List.TryGetValue(element.GetAttributeString("identifier", "").ToLowerInvariant(), out JobPrefab duplicate))
                        {
                            DebugConsole.ThrowError($"Error in '{filePath}': Duplicate job definition found for: '{duplicate.Identifier}'. Use the <override> XML element as the parent of job element's definition to override the existing job.");
                        }
                        else
                        {
                            var job = new JobPrefab(element);
                            List.Add(job.Identifier, job);
                        }
                    }
                }
                NoJobElement = NoJobElement ?? mainElement.Element("NoJob");
                NoJobElement = NoJobElement ?? mainElement.Element("nojob");
            }
        }
    }
}
