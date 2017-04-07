using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using System.Linq;

namespace Barotrauma
{
    class JobPrefab
    {
        public static List<JobPrefab> List;
                
        public readonly XElement Items;
        public readonly List<string> ItemNames;

        public List<SkillPrefab> Skills;

        public string Name
        {
            get;
            private set;
        }

        public string Description
        {
            get;
            private set;
        }


        //if set to true, a client that has chosen this as their preferred job will get it no matter what
        public bool AllowAlways
        {
            get;
            private set;
        }

        //how many crew members can have the job (only one captain etc)    
        public int MaxNumber
        {
            get;
            private set;
        }

        //how many crew members are REQUIRED to have the job 
        //(i.e. if one captain is required, one captain is chosen even if all the players have set captain to lowest preference)
        public int MinNumber
        {
            get;
            private set;
        }

        public float Commonness
        {
            get;
            private set;
        }

        public JobPrefab(XElement element)
        {
            Name = ToolBox.GetAttributeString(element, "name", "name not found");

            Description = ToolBox.GetAttributeString(element, "description", "");

            MinNumber = ToolBox.GetAttributeInt(element, "minnumber", 0);
            MaxNumber = ToolBox.GetAttributeInt(element, "maxnumber", 10);

            Commonness = ToolBox.GetAttributeInt(element, "commonness", 10);

            AllowAlways = ToolBox.GetAttributeBool(element, "allowalways", false);

            ItemNames = new List<string>();

            Skills = new List<SkillPrefab>();

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "items":
                        Items = subElement;
                        foreach (XElement itemElement in subElement.Elements())
                        {
                            string itemName = ToolBox.GetAttributeString(itemElement, "name", "");
                            if (!string.IsNullOrWhiteSpace(itemName)) ItemNames.Add(itemName);
                        }
                        break;
                    case "skills":
                        foreach (XElement skillElement in subElement.Elements())
                        {
                            Skills.Add(new SkillPrefab(skillElement));
                        } 
                        break;
                }
            }

            Skills.Sort((x,y) => y.LevelRange.X.CompareTo(x.LevelRange.X));
        }

        public static JobPrefab Random()
        {
            return List[Rand.Int(List.Count)];
        }

        public GUIFrame CreateInfoFrame()
        {
            int width = 500, height = 400;

            GUIFrame backFrame = new GUIFrame(Rectangle.Empty, Color.Black*0.5f);

            GUIFrame frame = new GUIFrame(new Rectangle(GameMain.GraphicsWidth / 2 - width / 2, GameMain.GraphicsHeight / 2 - height / 2, width, height), "", backFrame);
            frame.Padding = new Vector4(30.0f, 30.0f, 30.0f, 30.0f);
            
            new GUITextBlock(new Rectangle(0,0,100,20), Name, "", Alignment.TopLeft, Alignment.TopLeft, frame, false, GUI.LargeFont);

            var descriptionBlock = new GUITextBlock(new Rectangle(0, 40, 0, 0), Description, "", Alignment.TopLeft, Alignment.TopLeft, frame, true, GUI.SmallFont);

            new GUITextBlock(new Rectangle(0, 40 + descriptionBlock.Rect.Height + 20, 100, 20), "Skills: ", "", Alignment.TopLeft, Alignment.TopLeft, frame, false, GUI.LargeFont);

            int y = 40 + descriptionBlock.Rect.Height + 50;
            foreach (SkillPrefab skill in Skills)
            {
                string skillDescription = Skill.GetLevelName((int)skill.LevelRange.X);
                string skillDescription2 =  Skill.GetLevelName((int)skill.LevelRange.Y);

                if (skillDescription2!= skillDescription)
                {
                    skillDescription += "/"+skillDescription2;
                }
                new GUITextBlock(new Rectangle(0, y, 100, 20),
                    "   - " + skill.Name + ": " + skillDescription, "", Alignment.TopLeft, Alignment.TopLeft, frame, false, GUI.SmallFont);

                y += 20;
            }

            new GUITextBlock(new Rectangle(250, 40 + descriptionBlock.Rect.Height + 20, 0, 20), "Items: ", "", Alignment.TopLeft, Alignment.TopLeft, frame, false, GUI.LargeFont);

            y = 40 + descriptionBlock.Rect.Height + 50;
            foreach (string itemName in ItemNames)
            {
                new GUITextBlock(new Rectangle(250, y, 100, 20),
                "   - " + itemName, "", Alignment.TopLeft, Alignment.TopLeft, frame, false, GUI.SmallFont);

                y += 20;
            }

            return backFrame;
        }

        public static void LoadAll(List<string> filePaths)
        {
            List = new List<JobPrefab>();

            foreach (string filePath in filePaths)
            {
                XDocument doc = ToolBox.TryLoadXml(filePath);
                if (doc == null || doc.Root == null) return;

                foreach (XElement element in doc.Root.Elements())
                {
                    JobPrefab job = new JobPrefab(element);
                    List.Add(job);
                }
            }
        }
    }
}
