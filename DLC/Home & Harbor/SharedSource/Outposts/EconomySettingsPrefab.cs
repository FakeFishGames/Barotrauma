using Barotrauma;
using Microsoft.Xna.Framework;

namespace HomeAndHarbor
{
    public static class EconomyValues
    {
        public static readonly Identifier
            FoodDesired = "FoodDesired".ToIdentifier(),
            InitialFoodStored = "InitialFoodStored".ToIdentifier();
    }

    public sealed class EconomySettingsPrefab : Prefab
    {
        public static readonly PrefabCollection<EconomySettingsPrefab> Prefabs = [];

        public readonly Range<int> PopulationRange;

        public EconomySettingsPrefab(ContentFile file, ContentXElement element) : base(file, element.GetAttributeIdentifier("identifier", Identifier.Empty))
        {
            if (element.GetAttributePoint("population", Point.Zero) is { X: > 0, Y: > 0 } population)
            {
                PopulationRange = new Range<int>(population.X, population.Y);
            }
            else
            {
                int populationMin = element.GetAttributeInt("populationmin", 0);
                int populationMax = element.GetAttributeInt("populationmax", 0);

                PopulationRange = new Range<int>(populationMin, populationMax);
            }
        }

        public override void Dispose() { }
    }
}