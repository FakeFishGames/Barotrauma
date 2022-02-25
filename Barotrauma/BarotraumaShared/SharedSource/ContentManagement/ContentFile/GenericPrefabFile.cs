using System;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    abstract class GenericPrefabFile<T> : ContentFile where T : Prefab
    {
        protected GenericPrefabFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        protected abstract bool MatchesSingular(Identifier identifier);
        protected abstract bool MatchesPlural(Identifier identifier);
        protected abstract PrefabCollection<T> prefabs { get; }
        protected abstract T CreatePrefab(ContentXElement element);
        
        private void LoadFromXElement(ContentXElement parentElement, bool overriding)
        {
            Identifier elemName = parentElement.NameAsIdentifier();
            var childElements = parentElement.Elements()
#if DEBUG
                .OrderBy(e => Rand.Int(int.MaxValue, Rand.RandSync.Unsynced)).ToArray()
#endif
                ;
            if (parentElement.IsOverride())
            {
                foreach (var element in childElements)
                {
                    LoadFromXElement(element, true);
                }
            }
            else if (elemName == "clear")
            {
                prefabs.AddOverrideFile(this);
            }
            else if (MatchesSingular(elemName))
            {
                T prefab = CreatePrefab(parentElement);
                prefabs.Add(prefab, overriding);
            }
            else if (MatchesPlural(elemName))
            {
                foreach (var element in childElements)
                {
                    LoadFromXElement(element, overriding);
                }
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
            prefabs.RemoveByFile(this);
        }

        public sealed override void Sort()
        {
            prefabs.SortAll();
        }
    }
}