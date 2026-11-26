#nullable enable

using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Plugins
{
    public readonly record struct StatusEffectParams(float DeltaTime, Entity Entity, IReadOnlyList<ISerializableEntity> Targets, Vector2? WorldPosition);

    /// <summary>
    /// Allows plugins to register custom status effect actions.
    /// </summary>
    /// <example>
    /// You can create new status effect actions by implementing the interface <see cref="IStatusEffectAction"/>:
    /// <code>
    /// public class FooAction : IStatusEffectAction
    /// {
    ///     private readonly string _message;
    /// 
    ///     public FooAction(StatusEffect statusEffect, XElement element)
    ///     {
    ///         _message = element.GetAttributeString("message", "");
    ///     }
    /// 
    ///     public void Apply(StatusEffectParams effectParams)
    ///     {
    ///         if (!_message.IsNullOrEmpty())
    ///         {
    ///             DebugConsole.NewMessage(_message);
    ///         }
    ///     }
    /// }
    /// </code>
    /// And to register the type, you can use the built-in factory implementation <see cref="StatusEffectActionFactory"/>:
    /// <code>
    /// StatusEffectService.RegisterAction(new StatusEffectActionFactory("Foo", (StatusEffect statusEffect, XElement element) => new FooAction(statusEffect, element)));
    /// </code>
    /// Now you can use your new custom action in status effects:
    /// <code>
    /// &lt;StatusEffect ...&gt;
    ///   &lt;Foo message="Test" /&gt;
    /// &lt;/StatusEffect&gt;
    /// </code>
    /// </example>
    /// <remarks>
    /// Naming conflicts are not deterministic. Avoid using common names for your status effect actions.
    /// When possible, use unique prefixes to reduce the risk of colliding with action names from other plugins.
    /// </remarks>
    public interface IStatusEffectService : IPluginService
    {
        /// <summary>
        /// Registers a new status effect action factory that is used to create a status effect action.
        /// </summary>
        void RegisterAction(IStatusEffectActionFactory actionFactory);
    }

    public interface IStatusEffectAction
    {
        /// <summary>
        /// Called when a status effect with this action activates.
        /// </summary>
        void Apply(StatusEffectParams effectParams);
    }

    public interface IStatusEffectActionFactory
    {
        bool SupportsElement(XElement element);
        IStatusEffectAction Create(StatusEffect statusEffect, XElement element);
    }

    public class StatusEffectActionFactory : IStatusEffectActionFactory
    {
        public string ElementName { get; private set; }

        private readonly Func<StatusEffect, XElement, IStatusEffectAction> _createFunc;

        public StatusEffectActionFactory(string elementName, Func<StatusEffect, XElement, IStatusEffectAction> createFunc)
        {
            ElementName = elementName;
            _createFunc = createFunc;
        }

        public bool SupportsElement(XElement element) 
            => element.Name.ToString().ToLowerInvariant() == ElementName.ToLowerInvariant();

        public IStatusEffectAction Create(StatusEffect statusEffect, XElement element)
        {
            return _createFunc(statusEffect, element);
        }
    }
}