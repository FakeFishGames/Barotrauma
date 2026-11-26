global using static HomeAndHarbor.Services;

using Barotrauma.Plugins;

namespace HomeAndHarbor
{
    public static class Services
    {
        public static readonly IContentFileRegistrar ContentFileRegistrar = PluginServiceProvider.GetService<IContentFileRegistrar>();
        public static readonly IEventActionRegistrar EventActionRegistrar = PluginServiceProvider.GetService<IEventActionRegistrar>();
        public static readonly ISimpleHookService HookService = PluginServiceProvider.GetService<ISimpleHookService>();
        public static readonly IDebugConsole DebugConsole = PluginServiceProvider.GetService<IDebugConsole>();
        public static readonly IItemComponentRegistrar ItemComponentRegistrar = PluginServiceProvider.GetService<IItemComponentRegistrar>();
        public static readonly IGameNetwork GameNetwork = PluginServiceProvider.GetService<IGameNetwork>();
        public static readonly IGameScreen PGameScreen = PluginServiceProvider.GetService<IGameScreen>();
        public static readonly IAIObjectiveRegistrar AIObjectiveRegistrar = PluginServiceProvider.GetService<IAIObjectiveRegistrar>();
        public static readonly IContentPackageRetrievalService ContentPackageRetrieval = PluginServiceProvider.GetService<IContentPackageRetrievalService>();
    }
}