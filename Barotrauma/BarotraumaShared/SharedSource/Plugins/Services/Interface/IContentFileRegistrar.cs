namespace Barotrauma.Plugins
{
    /// <summary>
    /// Interface that allows registering custom XML content in your mod.
    /// </summary>
    /// <remarks>
    /// The methods are instance methods. The examples assume an instance has been retrieved and stored, e.g.:
    /// <code>
    /// public static readonly IContentFileRegistrar ContentFileRegistrar = PluginServiceProvider.GetService&lt;IContentFileRegistrar&gt;();
    /// </code>
    /// </remarks>
    public interface IContentFileRegistrar : IPluginService
    {
        /// <summary>
        /// Registers a content file type to be recognized by the game.
        /// The class should have the name "%NAME%File" where %NAME% is the name of the content type,
        /// and it should preferably inherit <see cref="GenericPrefabFile{T}"/>.
        /// </summary>
        /// <example>
        /// Barotrauma will recognize this example content type with the name "MyContent" due to the class name and
        /// alternatively as "FooBar" due to the attribute.<br/>
        /// <br/>
        /// The content file implementation handles parsing the XML documents defined in filelist.xml and passing the found elements to the prefab class.
        /// <code>
        /// [Barotrauma.AlternativeContentTypeNames("FooBar")]
        /// public class MyContentFile : Barotrauma.GenericPrefabFile&lt;MyPrefab&gt;
        /// {
        ///     public MyContentFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }
        ///     protected override bool MatchesSingular(Identifier identifier) => identifier == "MyPrefab";
        ///     protected override bool MatchesPlural(Identifier identifier) => identifier == "MyPrefabs";
        ///     protected override MyPrefab CreatePrefab(ContentXElement element) => new(this, element);
        ///     protected override PrefabCollection&lt;MyPrefab&gt; Prefabs => MyPrefab.Prefabs;
        /// }
        /// </code>
        /// <br/>
        /// An instance of the prefab class is created for each matching XML element found in the content file.
        /// <code>
        /// public class MyPrefab : Barotrauma.Prefab
        /// {
        ///     public static readonly PrefabCollection&lt;MyPrefab&gt; Prefabs = new();
        ///     public int MyProperty { get; set; }
        ///     public MyPrefab(ContentFile file, ContentXElement element) : base(file, element.GetAttributeIdentifier("identifier", Identifier.Empty))
        ///     {
        ///         MyProperty = element.GetAttributeInt("myproperty", 0);
        ///     }
        ///     public override void Dispose() { }
        /// }
        /// </code>
        /// <br/>
        /// Plugin init:
        /// <code>
        /// ContentFileRegistrar.RegisterContentFile&lt;MyContentFile&gt;();
        /// </code>
        /// </example>
        /// <typeparam name="T">Implementation of <see cref="ContentFile"/> to register.</typeparam>
        public void RegisterContentFile<T>() where T : ContentFile;
    }
}