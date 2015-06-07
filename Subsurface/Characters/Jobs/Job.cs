using System.Collections.Generic;
using System.Xml.Linq;

namespace Subsurface
{
    class Job
    {
        public static List<Job> jobList;

        string name;
        string description;
        
        //names of the items the character spawns with
        public List<string> itemNames;

        public string Name
        {
            get { return name; }
        }

        public Job(XElement element)
        {
            name = element.Name.ToString();

            description = ToolBox.GetAttributeString(element, "description", "");

            itemNames = new List<string>();

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString())
                {
                    case "item":
                        string itemName = ToolBox.GetAttributeString(subElement, "name", "");
                        if (!string.IsNullOrEmpty(itemName)) itemNames.Add(itemName);
                        break;
                }
            }
        }


        public static void LoadAll(string filePath)
        {
            jobList = new List<Job>();

            XDocument doc = ToolBox.TryLoadXml(filePath);
            if (doc == null) return;

            foreach (XElement element in doc.Root.Elements())
            {
                Job job = new Job(element);
                jobList.Add(job);
            }
        }
    }
}
