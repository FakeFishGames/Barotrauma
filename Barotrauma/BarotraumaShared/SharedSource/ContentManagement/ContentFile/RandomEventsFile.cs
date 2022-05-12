using System.Xml.Linq;

namespace Barotrauma
{
    [RequiredByCorePackage]
    sealed class RandomEventsFile : ContentFile
    {
        public RandomEventsFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

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
            else if (elemName == "randomevents")
            {
                foreach (var element in parentElement.Elements())
                {
                    LoadFromXElement(element, overriding);
                }
            }
            else if (elemName == "eventprefabs")
            {
                foreach (var subElement in parentElement.Elements())
                {
                    var prefab = new EventPrefab(subElement, this);
                    EventPrefab.Prefabs.Add(prefab, overriding);
                }
            }
            else if (elemName == "eventsprites")
            {
#if CLIENT
                foreach (var subElement in parentElement.Elements())
                {
                    var prefab = new EventSprite(subElement, this);
                    EventSprite.Prefabs.Add(prefab, overriding);
                }
#endif
            }
            else if (elemName == "eventset")
            {
                var prefab = new EventSet(parentElement, this);
                EventSet.Prefabs.Add(prefab, overriding);
            }
            else if (elemName == "clear")
            {
                EventPrefab.Prefabs.AddOverrideFile(this);
                EventSet.Prefabs.AddOverrideFile(this);
#if CLIENT
                EventSprite.Prefabs.AddOverrideFile(this);
#endif
            }
            else
            {
                DebugConsole.ThrowError($"RandomEventsFile: Invalid {GetType().Name} element: {parentElement.Name} in {Path}");
            }
        }

        public override void LoadFile()
        {
            XDocument doc = XMLExtensions.TryLoadXml(Path);
            if (doc == null) { return; }

            var rootElement = doc.Root.FromPackage(ContentPackage);
            LoadFromXElement(rootElement, false);
        }

        public override void UnloadFile()
        {
            EventPrefab.Prefabs.RemoveByFile(this);
            EventSet.Prefabs.RemoveByFile(this);
#if CLIENT
            EventSprite.Prefabs.RemoveByFile(this);
#endif
        }

        public override void Sort()
        {
            EventPrefab.Prefabs.SortAll();
            EventSet.Prefabs.SortAll();
#if CLIENT
            EventSprite.Prefabs.SortAll();
#endif
        }
    }
}