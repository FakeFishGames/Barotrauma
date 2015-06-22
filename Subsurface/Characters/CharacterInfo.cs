using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Subsurface
{
    public enum Gender { None, Male, Female };        

    class CharacterInfo
    {
        public string name;

        public readonly string file;

        public readonly int headSpriteId;

        //public int ID;

        public Gender gender;

        public int salary;

        //public string GenderString()
        //{
        //    return gender.ToString();
        //}

        public CharacterInfo(string file, string name = "", Gender gender = Gender.None)
        {
            this.file = file;

            //ID = -1;

            XDocument doc = ToolBox.TryLoadXml(file);
            if (doc == null) return;

            salary = 500;

            if (ToolBox.GetAttributeBool(doc.Root, "genders", false))
            {
                if (gender == Gender.None)
                {
                    float femaleRatio = ToolBox.GetAttributeFloat(doc.Root, "femaleratio", 0.5f);
                    this.gender = (Game1.random.NextDouble() < femaleRatio) ? Gender.Female : Gender.Male;
                }
                else
                {
                    this.gender = gender;
                }
            }

            Vector2 headSpriteRange = ToolBox.GetAttributeVector2(doc.Root, "headid", Vector2.Zero);
            if (headSpriteRange == Vector2.Zero)
            {
                headSpriteRange = ToolBox.GetAttributeVector2(
                    doc.Root,
                    this.gender == Gender.Female ? "femaleheadid" : "maleheadid",
                    Vector2.Zero);
            }

            if (headSpriteRange != Vector2.Zero)
            {
                headSpriteId = Game1.localRandom.Next((int)headSpriteRange.X, (int)headSpriteRange.Y + 1);
            }

            if (!string.IsNullOrEmpty(name))
            {
                this.name = name;
                return;
            }

            if (doc.Root.Element("name") != null)
            {
                string firstNamePath = (ToolBox.GetAttributeString(doc.Root.Element("name"), "firstname", ""));
                if (firstNamePath != "")
                {
                    firstNamePath = firstNamePath.Replace("[GENDER]", (this.gender == Gender.Female) ? "f" : "");
                    this.name = ToolBox.GetRandomLine(firstNamePath);
                }

                string lastNamePath = (ToolBox.GetAttributeString(doc.Root.Element("name"), "lastname", ""));
                if (lastNamePath != "")
                {
                    lastNamePath = lastNamePath.Replace("[GENDER]", (this.gender == Gender.Female) ? "f" : "");
                    if (this.name != "") this.name += " ";
                    this.name += ToolBox.GetRandomLine(lastNamePath);
                }
            }
        }

        public CharacterInfo(XElement element)
        {
            name = ToolBox.GetAttributeString(element, "name", "unnamed");

            string genderStr = ToolBox.GetAttributeString(element, "gender", "male").ToLower();
            gender = (genderStr == "male") ? Gender.Male : Gender.Female;

            file = ToolBox.GetAttributeString(element, "file", "");

            salary = ToolBox.GetAttributeInt(element, "salary", 1000);

            headSpriteId = ToolBox.GetAttributeInt(element, "headspriteid", 1);
        }

        public virtual XElement Save(XElement parentElement)
        {
            XElement componentElement = new XElement("character");

            componentElement.Add(
                new XAttribute("name", name),
                new XAttribute("file", file),
                new XAttribute("gender", gender == Gender.Male ? "male" : "female"),
                new XAttribute("salary", salary),
                new XAttribute("headspriteid", headSpriteId));

            parentElement.Add(componentElement);
            return componentElement;
        }
    }
}
