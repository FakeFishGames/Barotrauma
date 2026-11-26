using System;
using System.Reflection;

namespace Barotrauma.Plugins
{
    // ReSharper disable once ClassNeverInstantiated.Global
    internal sealed class ImplContentFileRegistrar(PluginData data, Assembly assembly) : SimpleRegisteringService<Type>(data, assembly), IContentFileRegistrar
    {
        public void RegisterContentFile<T>() where T : ContentFile
        {
            Register(typeof(T));
            PluginData.DynamicTypes.Add(new ContentFile.TypeInfo(typeof(T)));
        }

        public override void OnDeregister(Type item)
            => PluginData.DynamicTypes.RemoveWhere(typeInfo => typeInfo.Type == item);
    }
}