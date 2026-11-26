using Barotrauma;

namespace HomeAndHarbor
{
    public class ExamplePrefab : Prefab
    {
        public static readonly PrefabCollection<ExamplePrefab> Prefabs = new();

        public int MyProperty { get; set; }

        public ExamplePrefab(ContentFile file, ContentXElement element) : base(file, element.GetAttributeIdentifier("identifier", Identifier.Empty))
        {
            MyProperty = element.GetAttributeInt("myproperty", 0);
        }

        public override void Dispose()
        {
            // do nothing
        }
    }
}