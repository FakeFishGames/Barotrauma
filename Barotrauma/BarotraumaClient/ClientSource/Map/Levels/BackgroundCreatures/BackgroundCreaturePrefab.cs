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
        public readonly float SwarmRadius, SwarmCohesion;

        public readonly bool DisableRotation;

        public readonly float Scale;
        
        public BackgroundCreaturePrefab(XElement element)
        {
            Speed = element.GetAttributeFloat("speed", 1.0f);

            WanderAmount = element.GetAttributeFloat("wanderamount", 0.0f);

            WanderZAmount = element.GetAttributeFloat("wanderzamount", 0.0f);
            
            SwarmMin = element.GetAttributeInt("swarmmin", 1);
            SwarmMax = element.GetAttributeInt("swarmmax", 1);

            SwarmRadius = element.GetAttributeFloat("swarmradius", 200.0f);
            SwarmCohesion = element.GetAttributeFloat("swarmcohesion", 0.2f);

            DisableRotation = element.GetAttributeBool("disablerotation", false);

            Scale = element.GetAttributeFloat("scale", 1.0f);

            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() != "sprite") continue;

                Sprite = new Sprite(subElement, lazyLoad: true);
                break;
            }
        }
    }

}
