using System;
using System.Linq;
using System.Reflection;

namespace Barotrauma.Plugins
{
    // ReSharper disable once ClassNeverInstantiated.Global
    internal sealed class ImplEventActionRegistrar(PluginData data, Assembly assembly) : SimpleRegisteringService<Type>(data, assembly), IEventActionRegistrar
    {
        public void RegisterEventAction<T>() where T : EventAction
        {
            Register(typeof(T));
            PluginData.EventActions.Add(typeof(T));
        }

        public void RegisterEventActions(params Type[] types)
        {
            foreach (Type type in types)
            {
                Register(type);
                PluginData.EventActions.Add(type);
            }
        }

        public override void OnDeregister(Type item)
            => PluginData.EventActions.RemoveWhere(type => type == item);

        public void RegisterAllEventActions()
        {
            Type[] eventActions = PluginAssembly
                                  .GetTypes()
                                  .Where(static type => !type.IsAbstract && typeof(EventAction).IsAssignableFrom(type))
                                  .ToArray();

            RegisterEventActions(eventActions);
        }
    }
}