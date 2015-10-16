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

        string name;
        string description;

        //how many crew members can have the job (only one captain etc)        
        private int maxNumber;

        //how many crew members are REQUIRED to have a job 
        //(i.e. if one captain is required, one captain is chosen even if all the players have set captain to lowest preference)
        private int minNumber;

        private float commonness;

        //if set to true, a client that has chosen this as their preferred job will get it no matter what
        public bool AllowAlways
        {
            get;
            private set;
        }
                       
        //names of the items the character spawns with
        public List<string> ItemNames;
        public List<bool> EquipItem;

        public List<SkillPrefab> Skills;

        public string Name
        {
            get { return name; }
        }

        public string Description
        {
            get { return description; }
        }

        public int MaxNumber
        {
            get { return maxNumber; }
        }

        public int MinNumber
        {
            get { return minNumber; }
        }

        public float Commonness
        {
            get { return commonness; }
        }

        public JobPrefab(XElement element)
        {
            name = ToolBox.GetAttributeString(element, "name", "name not found");

            description = ToolBox.GetAttributeString(element, "description", "");

            minNumber = ToolBox.GetAttributeInt(element, "minnumber", 0);
            maxNumber = ToolBox.GetAttributeInt(element, "maxnumber", 10);

            commonness = ToolBox.GetAttributeInt(element, "commonness", 10);

            AllowAlways = ToolBox.GetAttributeBool(element, "allowalways", false);

            ItemNames = new List<string>();
            EquipItem = new List<bool>();

            Skills = new List<SkillPrefab>();

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLower())
                {
                    case "item":
                        string itemName = ToolBox.GetAttributeString(subElement, "name", "");
                        bool equipItem = ToolBox.GetAttributeBool(subElement, "equip", false);
                        if (!string.IsNullOrEmpty(itemName))
                        {
                            ItemNames.Add(itemName);
                            EquipItem.Add(equipItem);
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

            GUIFrame frame = new GUIFrame(new Rectangle(GameMain.GraphicsWidth / 2 - width / 2, GameMain.GraphicsHeight / 2 - height / 2, width, height), GUI.Style, backFrame);
            frame.Padding = new Vector4(30.0f, 30.0f, 30.0f, 30.0f);
            
            new GUITextBlock(new Rectangle(0,0,100,20), name, GUI.Style, Alignment.TopLeft, Alignment.TopLeft, frame, false, GUI.LargeFont);

            var descriptionBlock = new GUITextBlock(new Rectangle(0, 40, 0, 0), description, GUI.Style, Alignment.TopLeft, Alignment.TopLeft, frame, true, GUI.SmallFont);

            new GUITextBlock(new Rectangle(0, 40 + descriptionBlock.Rect.Height + 20, 100, 20), "Skills: ", GUI.Style, Alignment.TopLeft, Alignment.TopLeft, frame, false, GUI.LargeFont);

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
                    "   - " + skill.Name + ": " + skillDescription, GUI.Style, Alignment.TopLeft, Alignment.TopLeft, frame, false, GUI.SmallFont);

                y += 20;
            }

            new GUITextBlock(new Rectangle(250, 40 + descriptionBlock.Rect.Height + 20, 0, 20), "Items: ", GUI.Style, Alignment.TopLeft, Alignment.TopLeft, frame, false, GUI.LargeFont);

            y = 40 + descriptionBlock.Rect.Height + 50;
            foreach (string itemName in ItemNames)
            {
                new GUITextBlock(new Rectangle(250, y, 100, 20),
                "   - " + itemName, GUI.Style, Alignment.TopLeft, Alignment.TopLeft, frame, false, GUI.SmallFont);

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
                if (doc == null) return;

                foreach (XElement element in doc.Root.Elements())
                {
                    JobPrefab job = new JobPrefab(element);
                    List.Add(job);
                }
            }
        }
    }
}
