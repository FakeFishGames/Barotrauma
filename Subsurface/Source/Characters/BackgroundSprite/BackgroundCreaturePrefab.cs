using System.Xml.Linq;

namespace Barotrauma
{
    class BackgroundCreaturePrefab
    {

        public readonly Sprite Sprite;

        public readonly float Speed;

        public readonly float WanderAmount;

        public readonly float WanderZAmount;

        public readonly int SwarmMin, SwarmMax;

        public readonly float SwarmRadius;

        public readonly bool DisableRotation;
        
        public BackgroundCreaturePrefab(XElement element)
        {
            Speed = ToolBox.GetAttributeFloat(element, "speed", 1.0f);

            WanderAmount = ToolBox.GetAttributeFloat(element, "wanderamount", 0.0f);

            WanderZAmount = ToolBox.GetAttributeFloat(element, "wanderzamount", 0.0f);
            
            SwarmMin = ToolBox.GetAttributeInt(element, "swarmmin", 1);
            SwarmMax = ToolBox.GetAttributeInt(element, "swarmmax", 1);

            SwarmRadius = ToolBox.GetAttributeFloat(element, "swarmradius", 200.0f);

            DisableRotation = ToolBox.GetAttributeBool(element, "disablerotation", false);

            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLower() != "sprite") continue;

                Sprite = new Sprite(subElement);
                break;
            }
        }
    }

}
