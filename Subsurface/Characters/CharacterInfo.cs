using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Subsurface
{
    public enum Gender { None, Male, Female };        

    class CharacterInfo
    {
        public string Name;

        public readonly string File;

        public int HeadSpriteId;

        public Job Job;

        public Gender Gender;

        public int Salary;

        //public string GenderString()
        //{
        //    return gender.ToString();
        //}

        public CharacterInfo(string file, string name = "", Gender gender = Gender.None, Job job = null)
        {
            this.File = file;

            //ID = -1;

            XDocument doc = ToolBox.TryLoadXml(file);
            if (doc == null) return;

            Salary = 500;

            if (ToolBox.GetAttributeBool(doc.Root, "genders", false))
            {
                if (gender == Gender.None)
                {
                    float femaleRatio = ToolBox.GetAttributeFloat(doc.Root, "femaleratio", 0.5f);
                    this.Gender = (Rand.Range(0.0f, 1.0f, false) < femaleRatio) ? Gender.Female : Gender.Male;
                }
                else
                {
                    this.Gender = gender;
                }
            }

            Vector2 headSpriteRange = ToolBox.GetAttributeVector2(doc.Root, "headid", Vector2.Zero);
            if (headSpriteRange == Vector2.Zero)
            {
                headSpriteRange = ToolBox.GetAttributeVector2(
                    doc.Root,
                    this.Gender == Gender.Female ? "femaleheadid" : "maleheadid",
                    Vector2.Zero);
            }

            if (headSpriteRange != Vector2.Zero)
            {
                HeadSpriteId = Rand.Range((int)headSpriteRange.X, (int)headSpriteRange.Y + 1);
            }

            this.Job = (job == null) ? Job.Random() : job;            

            if (!string.IsNullOrEmpty(name))
            {
                this.Name = name;
                return;
            }

            if (doc.Root.Element("name") != null)
            {
                string firstNamePath = (ToolBox.GetAttributeString(doc.Root.Element("name"), "firstname", ""));
                if (firstNamePath != "")
                {
                    firstNamePath = firstNamePath.Replace("[GENDER]", (this.Gender == Gender.Female) ? "f" : "");
                    this.Name = ToolBox.GetRandomLine(firstNamePath);
                }

                string lastNamePath = (ToolBox.GetAttributeString(doc.Root.Element("name"), "lastname", ""));
                if (lastNamePath != "")
                {
                    lastNamePath = lastNamePath.Replace("[GENDER]", (this.Gender == Gender.Female) ? "f" : "");
                    if (this.Name != "") this.Name += " ";
                    this.Name += ToolBox.GetRandomLine(lastNamePath);
                }
            }
        }

        public CharacterInfo(XElement element)
        {
            Name = ToolBox.GetAttributeString(element, "name", "unnamed");

            string genderStr = ToolBox.GetAttributeString(element, "gender", "male").ToLower();
            Gender = (genderStr == "male") ? Gender.Male : Gender.Female;

            File = ToolBox.GetAttributeString(element, "file", "");

            Salary = ToolBox.GetAttributeInt(element, "salary", 1000);

            HeadSpriteId = ToolBox.GetAttributeInt(element, "headspriteid", 1);
        }

        public virtual XElement Save(XElement parentElement)
        {
            XElement componentElement = new XElement("character");

            componentElement.Add(
                new XAttribute("name", Name),
                new XAttribute("file", File),
                new XAttribute("gender", Gender == Gender.Male ? "male" : "female"),
                new XAttribute("salary", Salary),
                new XAttribute("headspriteid", HeadSpriteId));

            parentElement.Add(componentElement);
            return componentElement;
        }
    }
}
