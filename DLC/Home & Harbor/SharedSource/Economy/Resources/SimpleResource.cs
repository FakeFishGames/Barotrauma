using Barotrauma;
using System.Xml.Linq;

namespace HomeAndHarbor.Resources
{
    internal class SimpleResource(ResourcePrefab prefab, ResourceManager manager, XElement? element = null)
        : Resource(prefab, manager, element)
    {
        private float amount;

        //only overriding Amount to make it serializable, no changes in behavior
        [Serialize(0f, IsPropertySaveable.Yes, description: "The amount of the resource.")]
        public override float Amount
        {
            get => base.Amount;
            set => base.Amount = value;
        }

        protected override void SetAmount(float value)
        {
            amount = value;
        }

        protected override float GetAmountResourceSpecific()
        {
            return amount;
        }
    }
}