using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    public class AutonomousObjective
    {
        public readonly Identifier Identifier;
        public readonly Identifier Option;
        public readonly float PriorityModifier;
        public readonly bool IgnoreAtOutpost;

        public AutonomousObjective(XElement element)
        {
            Identifier = element.GetAttributeIdentifier("identifier", Identifier.Empty);

            //backwards compatibility
            if (Identifier == Identifier.Empty)
            {
                Identifier = element.GetAttributeIdentifier("aitag", Identifier.Empty);
            }

            Option = element.GetAttributeIdentifier("option", Identifier.Empty);
            PriorityModifier = element.GetAttributeFloat("prioritymodifier", 1);
            PriorityModifier = MathHelper.Max(PriorityModifier, 0);
            IgnoreAtOutpost = element.GetAttributeBool("ignoreatoutpost", false);
        }
    }

    class ItemRepairPriority : Prefab
    {
        public static readonly PrefabCollection<ItemRepairPriority> Prefabs = new PrefabCollection<ItemRepairPriority>();

        public readonly float Priority;

        public ItemRepairPriority(XElement element, JobsFile file) : base(file, element.GetAttributeIdentifier("tag", Identifier.Empty))
        {
            Priority = element.GetAttributeFloat("priority", -1f);
            if (Priority < 0)
            {
                DebugConsole.AddWarning($"The 'priority' attribute is missing from the the item repair priorities definition in {element} of {file.Path}.");
            }
        }

        public override void Dispose() { }
    }

    class JobVariant
    {
        public JobPrefab Prefab;
        public int Variant;
        public JobVariant(JobPrefab prefab, int variant)
        {
            Prefab = prefab;
            Variant = variant;
        }
    }

    partial class JobPrefab : PrefabWithUintIdentifier
    {
        public static readonly PrefabCollection<JobPrefab> Prefabs = new PrefabCollection<JobPrefab>();

        private bool disposed = false;
        public override void Dispose()
        {
            if (disposed) { return; }
            disposed = true;
            Prefabs.Remove(this);
        }

        private static readonly Dictionary<Identifier, float> _itemRepairPriorities = new Dictionary<Identifier, float>();
        /// <summary>
        /// Tag -> priority.
        /// </summary>
        public static IReadOnlyDictionary<Identifier, float> ItemRepairPriorities => _itemRepairPriorities;

        public static ContentXElement NoJobElement;
        public static JobPrefab Get(string identifier)
        {
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

        public class PreviewItem
        {
            public readonly Identifier ItemIdentifier;
            public readonly bool ShowPreview;

            public PreviewItem(Identifier itemIdentifier, bool showPreview)
            {
                ItemIdentifier = itemIdentifier;
                ShowPreview = showPreview;
            }
        }

        public readonly Dictionary<int, ContentXElement> ItemSets = new Dictionary<int, ContentXElement>();
        public readonly ImmutableDictionary<int, ImmutableArray<PreviewItem>> PreviewItems;
        public readonly List<SkillPrefab> Skills = new List<SkillPrefab>();
        public readonly List<AutonomousObjective> AutonomousObjectives = new List<AutonomousObjective>();
        public readonly List<Identifier> AppropriateOrders = new List<Identifier>();

        [Serialize("1,1,1,1", IsPropertySaveable.No)]
        public Color UIColor
        {
            get;
            private set;
        }

        public readonly LocalizedString Name;

        [Serialize(AIObjectiveIdle.BehaviorType.Passive, IsPropertySaveable.No)]
        public AIObjectiveIdle.BehaviorType IdleBehavior
        {
            get;
            private set;
        }

        public readonly LocalizedString Description;

        [Serialize(false, IsPropertySaveable.No)]
        public bool OnlyJobSpecificDialog
        {
            get;
            private set;
        }

        //the number of these characters in the crew the player starts with in the single player campaign
        [Serialize(0, IsPropertySaveable.No)]
        public int InitialCount
        {
            get;
            private set;
        }

        //if set to true, a client that has chosen this as their preferred job will get it no matter what
        [Serialize(false, IsPropertySaveable.No)]
        public bool AllowAlways
        {
            get;
            private set;
        }

        //how many crew members can have the job (only one captain etc) 
        [Serialize(100, IsPropertySaveable.No)]
        public int MaxNumber
        {
            get;
            private set;
        }

        //how many crew members are REQUIRED to have the job 
        //(i.e. if one captain is required, one captain is chosen even if all the players have set captain to lowest preference)
        [Serialize(0, IsPropertySaveable.No)]
        public int MinNumber
        {
            get;
            private set;
        }

        [Serialize(0.0f, IsPropertySaveable.No)]
        public float MinKarma
        {
            get;
            private set;
        }

        [Serialize(1.0f, IsPropertySaveable.No)]
        public float PriceMultiplier
        {
            get;
            private set;
        }

        // TODO: not used
        [Serialize(10.0f, IsPropertySaveable.No)]
        public float Commonness
        {
            get;
            private set;
        }

        //how much the vitality of the character is increased/reduced from the default value
        [Serialize(0.0f, IsPropertySaveable.No)]
        public float VitalityModifier
        {
            get;
            private set;
        }

        //whether the job should be available to NPCs
        [Serialize(false, IsPropertySaveable.No)]
        public bool HiddenJob
        {
            get;
            private set;
        }

        public Sprite Icon;
        public Sprite IconSmall;

        public SkillPrefab PrimarySkill => Skills?.FirstOrDefault(s => s.IsPrimarySkill);

        public ContentXElement Element { get; private set; }
        public ContentXElement ClothingElement { get; private set; }
        public int Variants { get; private set; }

        public JobPrefab(ContentXElement element, JobsFile file) : base(file, element.GetAttributeIdentifier("identifier", ""))
        {
            SerializableProperty.DeserializeProperties(this, element);

            Name = TextManager.Get("JobName." + Identifier);
            Description = TextManager.Get("JobDescription." + Identifier);
            Element = element;

            var previewItems = new Dictionary<int, List<PreviewItem>>();

            int variant = 0;
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "itemset":
                        ItemSets.Add(variant, subElement);
                        previewItems[variant] = new List<PreviewItem>();
                        loadItemIdentifiers(subElement, variant);
                        variant++;
                        break;
                    case "skills":
                        foreach (var skillElement in subElement.Elements())
                        {
                            Skills.Add(new SkillPrefab(skillElement));
                        }
                        break;
                    case "autonomousobjectives":
                        subElement.Elements().ForEach(order => AutonomousObjectives.Add(new AutonomousObjective(order)));
                        break;
                    case "appropriateobjectives":
                    case "appropriateorders":
                        subElement.Elements().ForEach(order => AppropriateOrders.Add(order.GetAttributeIdentifier("identifier", "")));
                        break;
                    case "jobicon":
                        Icon = new Sprite(subElement.FirstElement());
                        break;
                    case "jobiconsmall":
                        IconSmall = new Sprite(subElement.FirstElement());
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

                    Identifier itemIdentifier = itemElement.GetAttributeIdentifier("identifier", Identifier.Empty);
                    if (itemIdentifier.IsEmpty)
                    {
                        DebugConsole.ThrowError("Error in job config \"" + Name + "\" - item with no identifier.");
                    }
                    else
                    {
                        previewItems[variant].Add(new PreviewItem(itemIdentifier, itemElement.GetAttributeBool("showpreview", true)));
                    }
                    loadItemIdentifiers(itemElement, variant);
                }
            }

            PreviewItems = previewItems.Select(kvp => (kvp.Key, kvp.Value.ToImmutableArray()))
                .ToImmutableDictionary();
            
            Variants = variant;

            Skills.Sort((x,y) => y.LevelRange.Start.CompareTo(x.LevelRange.Start));

            // Disabled on purpose, TODO: remove all references?
            //ClothingElement = element.GetChildElement("PortraitClothing");
        }

        public static JobPrefab Random(Rand.RandSync sync, Func<JobPrefab, bool> predicate = null) => Prefabs.GetRandom(p => !p.HiddenJob && (predicate == null || predicate(p)), sync);
    }
}
