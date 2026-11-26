using System;
using Barotrauma.Items.Components;

namespace Barotrauma.Plugins
{
    /// <summary>
    /// Provides methods for registering custom implementations of <see cref="ItemComponent"/> that can be added to items.
    /// </summary>
    /// <remarks>
    /// The methods are instance methods. The examples assume an instance has been retrieved and stored, e.g.:
    /// <code>
    /// public static readonly IItemComponentRegistrar ItemComponentRegistrar = PluginServiceProvider.GetService&lt;IItemComponentRegistrar&gt;();
    /// </code>
    /// </remarks>
    public interface IItemComponentRegistrar : IPluginService
    {
        /// <summary>
        /// Registers a custom item component to be recognized by the game.
        /// </summary>
        /// <typeparam name="T">Implementation of <see cref="ItemComponent"/> to register.</typeparam>
        /// <example>
        /// MyItemComponent.cs:
        /// <code>
        /// public class MyItemComponent(Item item, ContentXElement element) : ItemComponent(item, element)
        /// {
        ///     [Serialize(0, IsPropertySaveable.Yes, description: "My custom property")]
        ///     public int MyProperty { get; set; }
        /// }
        /// </code>
        /// Plugin init:
        /// <code>
        /// ItemComponentRegistrar.RegisterItemComponent&lt;MyItemComponent&gt;();
        /// </code>
        /// </example>
        public void RegisterItemComponent<T>() where T : ItemComponent;

        /// <summary>
        /// Registers multiple custom item components to be recognized by the game.
        /// </summary>
        /// <param name="types">list of types inheriting <see cref="ItemComponent"/> to register.</param>
        /// <example>
        /// MyItemComponent.cs:
        /// <code>
        /// public class MyItemComponent(Item item, ContentXElement element) : ItemComponent(item, element)
        /// {
        ///     [Serialize("", IsPropertySaveable.Yes, description: "My custom property")]
        ///     public int MyProperty { get; set; }
        /// }
        ///
        /// public class AnotherItemComponent(Item item, ContentXElement element) : ItemComponent(item, element)
        /// {
        ///     [Serialize("", IsPropertySaveable.Yes, description: "Another custom property")]
        ///     public string AnotherProperty { get; set; }
        /// }
        /// </code>
        /// Plugin init:
        /// <code>
        /// ItemComponentRegistrar.RegisterItemComponents(typeof(MyItemComponent), typeof(AnotherItemComponent));
        /// </code>
        /// </example>
        /// <exception cref="ArgumentException">if a type does not inherit <see cref="ItemComponent"/>.</exception>
        public void RegisterItemComponents(params Type[] types);

        /// <summary>
        /// Registers all types in the calling plugin that inherit <see cref="ItemComponent"/>.
        /// </summary>
        /// <remarks>
        /// The method will find all types in the calling assembly that inherit <see cref="ItemComponent"/>
        /// via reflection and register them using <see cref="RegisterItemComponents"/>.
        /// </remarks>
        /// <example>
        /// Plugin init:
        /// <code>
        /// ItemComponentRegistrar.RegisterAllItemComponents();
        /// </code>
        /// </example>
        public void RegisterAllItemComponents();

        /// <summary>
        /// Adds a custom item component to an already existing item.
        /// </summary>
        /// <param name="identifierOrTag">Identifier of the item to add the component to.</param>
        /// <param name="element">The initial prefab element of the item component.</param>
        /// <example>
        /// Plugin init:
        /// <code>
        /// ItemComponentRegistrar.AddItemComponentToItem("planter".ToIdentifier(), prefabElement);
        /// </code>
        /// </example>
        public void AddItemComponentToItem(Identifier identifierOrTag, ContentXElement element);
    }
}