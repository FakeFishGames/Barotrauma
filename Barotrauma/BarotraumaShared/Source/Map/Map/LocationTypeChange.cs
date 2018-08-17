using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class LocationTypeChange
    {
        public readonly string ChangeTo;
        public readonly float Probability;
        public readonly int RequiredDuration;

        public List<string> Messages = new List<string>();

        //the change can't happen if there's a location of the given type next to this one
        public readonly List<string> DisallowedAdjacentLocations;

        //the change can only happen if there's at least one of the given types of locations next to this one
        public readonly List<string> RequiredAdjacentLocations;

        public LocationTypeChange(XElement element)
        {
            ChangeTo = element.GetAttributeString("type", "");
            Probability = element.GetAttributeFloat("probability", 1.0f);
            RequiredDuration = element.GetAttributeInt("requiredduration", 0);

            DisallowedAdjacentLocations = element.GetAttributeStringArray("disallowedadjacentlocations", new string[0]).ToList();
            RequiredAdjacentLocations = element.GetAttributeStringArray("requiredadjacentlocations", new string[0]).ToList();

            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() == "message")
                {
                    Messages.Add(subElement.GetAttributeString("text", ""));
                }
            }
        }
    }
}
