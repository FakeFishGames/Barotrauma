#nullable enable

using HarmonyLib;
using System.Reflection;

namespace Barotrauma.Plugins
{
    internal sealed class ImplHarmonyProvider : IHarmonyProvider, ITransientService
    {
        public Assembly PluginAssembly { get; private set; }
        public PluginData PluginData { get; private set; }

        private Harmony? harmony;

        public ImplHarmonyProvider(PluginData data, Assembly assembly)
        {
            PluginAssembly = assembly;
            PluginData = data;
            PluginLoader.AddTransientService(assembly, service: this);
        }

        public Harmony GetHarmony()
        {
            if (harmony == null)
            {
                harmony = new Harmony(PluginAssembly.FullName);
            }

            return harmony;
        }

        public void PatchAll()
        {
            GetHarmony().PatchAll(PluginAssembly);
        }

        public void Dispose()
        {
            harmony?.UnpatchSelf();
            harmony = null;
        }
    }
}