using System.Xml.Linq;

namespace Barotrauma
{
    #warning TODO: this is almost a GenericPrefabFile. Must refactor further.
    [RequiredByCorePackage]
    sealed class OrdersFile : ContentFile
    {
        public OrdersFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        public void LoadFromXElement(ContentXElement parentElement, bool overriding)
        {
            Identifier elemName = new Identifier(parentElement.Name.ToString());
            if (parentElement.IsOverride())
            {
                foreach (var element in parentElement.Elements())
                {
                    LoadFromXElement(element, true);
                }
            }
            else if (elemName == "order")
            {
                OrderPrefab prefab = new OrderPrefab(parentElement, this);
                OrderPrefab.Prefabs.Add(prefab, overriding);
            }
            else if (elemName == "ordercategory")
            {
                OrderCategoryIcon prefab = new OrderCategoryIcon(parentElement, this);
                OrderCategoryIcon.OrderCategoryIcons.Add(prefab, overriding);
            }
            else if (elemName == "orders")
            {
                foreach (var element in parentElement.Elements())
                {
                    LoadFromXElement(element, overriding);
                }
            }
            else if (elemName == "clear")
            {
                OrderCategoryIcon.OrderCategoryIcons.AddOverrideFile(this);
                OrderPrefab.Prefabs.AddOverrideFile(this);
            }
            else
            {
                DebugConsole.ThrowError($"Invalid {GetType().Name} element: {parentElement.Name} in {Path}");
            }
        }

        public override sealed void LoadFile()
        {
            XDocument doc = XMLExtensions.TryLoadXml(Path);
            if (doc == null) { return; }

            var rootElement = doc.Root.FromPackage(ContentPackage);
            LoadFromXElement(rootElement, false);
        }

        public override sealed void UnloadFile()
        {
            OrderCategoryIcon.OrderCategoryIcons.RemoveByFile(this);
            OrderPrefab.Prefabs.RemoveByFile(this);
        }

        public override sealed void Sort()
        {
            OrderCategoryIcon.OrderCategoryIcons.SortAll();
            OrderPrefab.Prefabs.SortAll();
        }
    }
}
