using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Barotrauma.Items.Components;

namespace Barotrauma.Plugins
{
    // ReSharper disable once ClassNeverInstantiated.Global
    internal sealed class ImplItemComponentRegistrar(PluginData data, Assembly assembly) : IItemComponentRegistrar, ITransientService
    {
        public Assembly PluginAssembly { get; } = assembly;
        public PluginData PluginData { get; } = data;

        private readonly HashSet<Type> registeredItemComponents = [];

        public void RegisterItemComponent<T>() where T : ItemComponent
        {
            registeredItemComponents.Add(typeof(T));
            PluginData.ItemComponents.Add(typeof(T));
        }

        public void RegisterItemComponents(params Type[] types)
        {
            foreach (Type type in types)
            {
                if (!typeof(ItemComponent).IsAssignableFrom(type))
                {
                    throw new ArgumentException($"Type {type} is not an ItemComponent.");
                }

                registeredItemComponents.Add(type);
                PluginData.ItemComponents.Add(type);
            }
        }

        public void RegisterAllItemComponents()
        {
            Type[] eventActions = PluginAssembly
                                  .GetTypes()
                                  .Where(static type => !type.IsAbstract && typeof(ItemComponent).IsAssignableFrom(type))
                                  .ToArray();

            RegisterItemComponents(eventActions);
        }

        public void AddItemComponentToItem(Identifier identifierOrTag, ContentXElement element)
        {
            if (!PluginData.AdditionalItemComponents.TryGetValue(identifierOrTag, out HashSet<ContentXElement> existingElements)) { existingElements = []; }
            existingElements.Add(element);
            PluginData.AdditionalItemComponents[identifierOrTag] = existingElements;
        }

        public void Dispose()
        {
            PluginData.ItemComponents.RemoveWhere(type => registeredItemComponents.Contains(type));
            registeredItemComponents.Clear();
            PluginData.AdditionalItemComponents.Clear();
        }
    }
}