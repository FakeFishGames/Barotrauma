using System.Collections.Immutable;
using System.Diagnostics;
using System.Xml.Linq;
using Barotrauma;
using Microsoft.Xna.Framework;

namespace HomeAndHarbor.Resources
{
    internal sealed class CombinedResource : Resource
    {
        private readonly record struct ResourceAndRatio(Identifier Identifier, float Ratio, bool AllowIncrement, ResourceManager Manager)
        {
            public Resource? Resource
                => Manager.GetResource(Identifier);

            public float Amount
                => Resource?.Amount ?? 0f;
        }

        private readonly ImmutableArray<ResourceAndRatio> resourceIdentifiers;

        [Serialize(false, IsPropertySaveable.No, description: "If enabled, decreasing the resource drains the child resources one at a time in the order they're defined (e.g. use up basic fuel before advanced fuel), " +
            "instead of distributing the decrease between them based on their ratios. Increases are still distributed based on the ratios.")]
        public bool DecreaseSequentially { get; set; }

        /// <summary>
        /// Cannot sync the value of a combined resource, the child resources should be synced instead
        /// </summary>
        public override bool IsSyncable => false;

        public IEnumerable<Resource> UnderlyingResources
            => resourceIdentifiers.Select(static ri => ri.Resource).Where(static r => r is not null)!;

        public CombinedResource(ResourcePrefab prefab, ResourceManager manager, XElement? element = null) : base(prefab, manager, element)
        {
            var builder = ImmutableArray.CreateBuilder<ResourceAndRatio>();
            foreach (XElement subElement in prefab.OriginalElement.Elements())
            {
                if (subElement.NameAsIdentifier() == "Resource")
                {
                    Identifier locationType = subElement.GetAttributeIdentifier("locationtype", Identifier.Empty);
                    if (!locationType.IsEmpty && manager.Location.Type.Identifier != locationType) { continue; }

                    Identifier resourceIdentifier = subElement.GetAttributeIdentifier("identifier", Identifier.Empty);
                    if (resourceIdentifier.IsEmpty) { continue; }

                    float ratio = subElement.GetAttributeFloat("ratio", 1f);

                    if (subElement.GetAttribute("weight") != null)
                    {
                        DebugConsole.ThrowError(
                            $"Error in {nameof(CombinedResource)} \"{Identifier}\": child resource defined using weight instead of ratio."
                            + $" The ratio determines how much each of the child resources contribute to the {nameof(CombinedResource)}.");
                    }

                    bool allowIncrement = subElement.GetAttributeBool("allowincrement", true);
                    builder.Add(new ResourceAndRatio(resourceIdentifier, ratio, allowIncrement, manager));
                }
            }
            resourceIdentifiers = builder.ToImmutable();
        }

        public override void SetDefault(Random rand)
        {
            if (DefaultAmount != Vector2.Zero)
            {
                foreach (ResourceAndRatio resource in resourceIdentifiers)
                {
                    Resource? childResource = Manager.GetResource(resource.Identifier);
                    if (childResource == null) { continue; }
                    if (childResource.DefaultAmount != Vector2.Zero)
                    {
                        DebugConsole.ThrowError(
                            $"Combined resource \"{Identifier}\" has a child resource \"{resource.Identifier}\" which also has a default amount defined. " +
                            " The default amount must be defined in either the combiner resource or the resources it consists of, not both.");
                    }
                }
            }

            base.SetDefault(rand);
        }

        public bool ContainsResource(Resource resource)
        {
            return resourceIdentifiers.Any(r => r.Identifier == resource.Identifier);
        }
        
        protected override float GetAmountResourceSpecific()
        {
            float totalAmount = 0f;
            foreach (ResourceAndRatio resource in resourceIdentifiers)
            {
                totalAmount += resource.Amount;
            }
            return totalAmount;
        }

        protected override void SetAmount(float amount)
        {
            float oldAmount = GetAmount();
            //no clamping here, the Amount setter already clamps in the direction of the change
            if (MathUtils.NearlyEqual(amount, oldAmount)) { return; }

            float difference = amount - oldAmount;
            float totalRatio = GetTotalRatio(difference);

            if (DecreaseSequentially && difference < 0)
            {
                //if some of the decrease couldn't be applied this way, the iterations below try to distribute the rest
                DecreaseInOrder(-difference);
            }

            // In case some reasources could not be decreased or increased further,
            // we need to do multiple iterations.
            // For example, in the case of a combined resource with two resources A and B,
            // where A = 10, and B = 5, and we are removing 15.
            // We would first divide them equally, which means removing 7.5 from each,
            // but because B only has 5, we can only remove 5 from it and the overspill would be 2.5.
            // We then perform a second iteration where we would remove the overspill 2.5 from A
            float remainingAmountToAdd = amount - GetAmount();
            const int maxIterations = 100;
            for (int i = 0; i < maxIterations && !MathUtils.NearlyEqual(remainingAmountToAdd, 0f); i++)
            {
                totalRatio = GetTotalRatio(remainingAmountToAdd);
                Modify(remainingAmountToAdd);
                remainingAmountToAdd = amount - GetAmount();
            }
            

            if (Math.Abs(remainingAmountToAdd) > 0.01f)
            {
                DebugConsole.ThrowError(
                    $"Failed to set combined resource amount ({Identifier}) in location \"{Manager.Location.DisplayName}\". Expected: {amount}, Actual: {GetAmount()}." +
                    $"This may mean that some of the sub-resources have min/max amounts that prevent the {Identifier} from decreasing/increasing further. Subresources:\n - " +
                    string.Join("\n - ", resourceIdentifiers.Select(r => $"{r.Identifier} ({Manager.GetResource(r.Identifier)?.Amount ?? 0})")));
            }
            return;

            void DecreaseInOrder(float amountToRemove)
            {
                foreach (ResourceAndRatio resourceAndRatio in resourceIdentifiers)
                {
                    if (amountToRemove <= 0.0f || MathUtils.NearlyEqual(amountToRemove, 0.0f)) { break; }
                    if (!Manager.TryGetResource(resourceAndRatio.Identifier, out Resource? resource) || resource == null) { continue; }
                    if (resource.IsReadonly) { continue; }
                    if (resource.Amount <= resource.MinAmount) { continue; }

                    Manager.AddToResource(resource.Identifier, -amountToRemove, out float remainingAmountToAdd);
                    //negative remainder = the amount that couldn't be removed from this resource
                    amountToRemove = -remainingAmountToAdd;
                    if (MathUtils.NearlyEqual(resource.Amount, 0.0f))
                    {
                        Manager.SetResource(resource.Identifier, 0.0f);
                    }
                }
            }

            void Modify(float amountToAdd)
            {
                foreach (ResourceAndRatio resourceAndRatio in resourceIdentifiers)
                {
                    if (!Manager.TryGetResource(resourceAndRatio.Identifier, out Resource? resource) || resource == null) { continue; }
                    if (resource.IsReadonly) { continue; }
                    switch (amountToAdd)
                    {
                        case > 0 when !resourceAndRatio.AllowIncrement:
                        case < 0 when resourceAndRatio.Amount <= 0:
                            continue;
                    }
                    //can't increase resource amount below min or above max
                    if (amountToAdd < 0 && resource.Amount <= resource.MinAmount) { continue; }
                    if (amountToAdd > 0 && resource.Amount >= resource.MaxAmount) { continue; }

                    float resourceAmount = amountToAdd * (resourceAndRatio.Ratio / totalRatio);
                    Manager.AddToResource(resource.Identifier, resourceAmount, out float remainingAmountToAdd);
                    if (MathUtils.NearlyEqual(resource.Amount, 0.0f))
                    {
                        Manager.SetResource(resource.Identifier, 0.0f);
                    }
                }
            }

            float GetTotalRatio(float amountToAdd)
            {
                float result = 0f;
                foreach (ResourceAndRatio resourceAndRatio in resourceIdentifiers)
                {
                    if (!Manager.TryGetResource(resourceAndRatio.Identifier, out Resource? resource) || resource == null) { continue; }
                    if (resource.IsReadonly) { continue; }
                    switch (amountToAdd)
                    {
                        case > 0 when !resourceAndRatio.AllowIncrement:
                        case < 0 when resourceAndRatio.Amount <= 0:
                            continue;
                    }
                    //can't increase resource amount below min or above max
                    if (amountToAdd < 0 && resource.Amount <= resource.MinAmount) { continue; }
                    if (amountToAdd > 0 && resource.Amount >= resource.MaxAmount) { continue; }

                    result += resourceAndRatio.Ratio;
                }
                return result;
            }
        }
    }
}
