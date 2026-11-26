using System;
using System.Reflection;

namespace Barotrauma.Plugins
{
    // ReSharper disable once ClassNeverInstantiated.Global
    internal sealed class ImplSimpleHookService(PluginData data, Assembly assembly) : SimpleRegisteringService<Delegate>(data, assembly), ISimpleHookService
    {
        public override void OnDeregister(Delegate item)
            => PluginData.RemoveHook(item);

        public void RegisterHook<T>(T callback) where T : Delegate
        {
            PluginData.AddHook(callback);
            Register(callback);
        }

        public void DeregisterHook<T>(T callback) where T : Delegate
        {
            Deregister(callback);
        }
    }
}