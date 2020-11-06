using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class LocationTypeChange
    {
        public readonly string ChangeToType;

        public readonly float Probability;
        public readonly int RequiredDuration;

        public readonly float ProximityProbabilityIncrease;
        public readonly int RequiredProximityForProbabilityIncrease;

        public List<string> Messages = new List<string>();

        //the change can't happen if there's a location of the given type next to this one
        public readonly List<string> DisallowedAdjacentLocations;

        //the change can only happen if there's at least one of the given types of locations next to this one
        public readonly List<string> RequiredAdjacentLocations;

        public LocationTypeChange(string currentType, XElement element)
        {
            ChangeToType = element.GetAttributeString("type", "");
            Probability = element.GetAttributeFloat("probability", 1.0f);
            RequiredDuration = element.GetAttributeInt("requiredduration", 0);

            ProximityProbabilityIncrease = element.GetAttributeFloat("proximityprobabilityincrease", 0.0f);
            RequiredProximityForProbabilityIncrease = element.GetAttributeInt("requiredproximityforprobabilityincrease", 0);

            DisallowedAdjacentLocations = element.GetAttributeStringArray("disallowedadjacentlocations", new string[0]).ToList();
            RequiredAdjacentLocations = element.GetAttributeStringArray("requiredadjacentlocations", new string[0]).ToList();

            string messageTag = element.GetAttributeString("messagetag", "LocationChange." + currentType + ".ChangeTo." + ChangeToType);

            Messages = TextManager.GetAll(messageTag);
            if (Messages == null)
            {
                DebugConsole.ThrowError("No messages defined for the location type change " + currentType + " -> " + ChangeToType);
            }
        }
    }
}
