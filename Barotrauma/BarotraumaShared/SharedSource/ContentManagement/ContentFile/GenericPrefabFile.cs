using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    /// <summary>
    /// A generic content file that maps XML elements to prefab instances.
    /// </summary>
    /// <typeparam name="T">The prefab type that this file contains.</typeparam>
    public abstract class GenericPrefabFile<T>(ContentPackage contentPackage, ContentPath path)
        : ContentFile(contentPackage, path) where T : Prefab
    {
        /// <summary>
        /// Checks if the XML element name matches the singular form of the prefab type.
        /// </summary>
        /// <param name="identifier">XML element name as an identifier.</param>
        /// <returns>
        /// True if the XML element name matches the singular form of the prefab type,
        /// in which case it will be passed to <see cref="CreatePrefab"/>.
        /// </returns>
        /// <example>
        /// <code>
        /// protected override bool MatchesSingular(Identifier identifier) => identifier == "MyPrefab";
        /// </code>
        /// or alternatively:
        /// <code>
        /// protected override bool MatchesSingular(Identifier identifier) => !MatchesPlural(identifier);
        /// </code>
        /// </example>
        protected abstract bool MatchesSingular(Identifier identifier);

        /// <summary>
        /// Checks if the XML element name matches the plural form of the prefab type.
        /// </summary>
        /// <param name="identifier">XML element name as an identifier.</param>
        /// <returns>
        /// True if the XML element name matches the plural form of the prefab type, in which case its XML
        /// element children will be iterated over and checked for singular or plural matches recursively.
        /// </returns>
        /// <example>
        /// <code>
        /// protected override bool MatchesPlural(Identifier identifier) => identifier == "MyPrefabs";
        /// </code>
        /// </example>
        protected abstract bool MatchesPlural(Identifier identifier);

        /// <summary>
        /// A list of all prefabs that were loaded from this file.
        /// Ideally your Prefab type should have a static <see cref="PrefabCollection{T}"/> field and this
        /// property should return that field.
        /// </summary>
        /// <example>
        /// MyPrefab:
        /// <code>
        /// public static readonly PrefabCollection&lt;MyPrefab&gt; Prefabs = [];
        /// </code>
        /// MyContentFile:
        /// <code>
        /// protected override PrefabCollection&lt;MyPrefab&gt; Prefabs => MyPrefab.Prefabs;
        /// </code>
        /// </example>
        protected abstract PrefabCollection<T> Prefabs { get; }

        /// <summary>
        /// Constructs a new prefab instance from an XML element.
        /// </summary>
        /// <param name="element">The XML element that matches the singular form of the prefab type.</param>
        /// <returns>A new prefab instance.</returns>
        /// <example>
        /// <code>
        /// protected override MyPrefab CreatePrefab(ContentXElement element)
        ///     => new MyPrefab(element, this);
        /// </code>
        /// </example>
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
                Prefabs.AddOverrideFile(this);
            }
            else if (MatchesSingular(elemName))
            {
                T prefab = CreatePrefab(parentElement);
                try
                {
                    Prefabs.Add(prefab, overriding);
                }
                catch
                {
                    //clean up before rethrowing, since some prefab types might lock resources
                    prefab.Dispose();
                    Prefabs.Remove(prefab);
                    throw;
                }
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
                DebugConsole.ThrowError($"GenericPrefabFile: Invalid {GetType().Name} element: {parentElement.Name} in {Path}", contentPackage: ContentPackage);
            }
        }

        public sealed override void LoadFile()
        {
            XDocument doc = XMLExtensions.TryLoadXml(Path);
            if (doc == null) { return; }

            var rootElement = doc.Root.FromPackage(ContentPackage);
            LoadFromXElement(rootElement, false);
        }

        public sealed override void UnloadFile()
        {
            Prefabs.RemoveByFile(this);
        }

        public sealed override void Sort()
        {
            Prefabs.SortAll();
        }
    }
}