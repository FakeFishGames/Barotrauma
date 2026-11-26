using Barotrauma;

namespace HomeAndHarbor
{
    /// <summary>
    /// A component that configures the underlying resource generator based on XML conditions.
    /// </summary>
    public class ConditionalResourceGeneratorComponent : ResourceGeneratorBaseComponent
    {
        private readonly List<PropertyConditional> propertyConditionals = [];

        [Serialize(1f, IsPropertySaveable.Yes, description: "How often in seconds the component checks for conditions.")]
        public float CheckInterval { get; set; }

        private float checkTimer;

        public ConditionalResourceGeneratorComponent(Item item, ContentXElement element) : base(item, element)
        {
            foreach (ContentXElement subElement in element.Elements())
            {
                if (subElement.Name.ToIdentifier() != "Conditional") { continue; }
                propertyConditionals.AddRange(PropertyConditional.FromXElement(subElement));
            }
        }

        public override void OnItemLoaded()
        {
            base.OnItemLoaded();
            IsActive = true;
            UpdatePropertyConditionals();
        }

        public override void Update(float deltaTime, Camera cam)
        {
            base.Update(deltaTime, cam);

            checkTimer += deltaTime;

            if (checkTimer > CheckInterval)
            {
                UpdatePropertyConditionals();
                checkTimer = 0f;
            }
        }

        private void UpdatePropertyConditionals()
        {
            bool conditionMatches = true;

            foreach (PropertyConditional conditional in propertyConditionals)
            {
                if (!Item.ConditionalMatches(conditional))
                {
                    conditionMatches = false;
                    break;
                }
            }

            ResourceGenerator.IsActive = conditionMatches;
        }
    }
}