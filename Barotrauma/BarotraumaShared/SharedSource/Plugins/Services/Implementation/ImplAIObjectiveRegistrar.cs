#nullable enable

using System.Reflection;

namespace Barotrauma.Plugins;

internal sealed class ImplAIObjectiveRegistrar(PluginData data, Assembly assembly) : SimpleRegisteringService<IOrderObjectiveFactory>(data, assembly), IAIObjectiveRegistrar
{
    public override void OnDeregister(IOrderObjectiveFactory objectiveFactory)
    {
        data.OrderObjectiveFactories.Remove(objectiveFactory);
    }

    public void RegisterObjectiveFactory(IOrderObjectiveFactory objectiveFactory)
    {
        data.OrderObjectiveFactories.Add(objectiveFactory);
        Register(objectiveFactory);
    }
}