using System;
using System.Reflection;
using System.Xml.Linq;

namespace Barotrauma.Plugins;

internal sealed class ImplStatusEffectService(PluginData data, Assembly assembly) : SimpleRegisteringService<IStatusEffectActionFactory>(data, assembly), IStatusEffectService
{
    public override void OnDeregister(IStatusEffectActionFactory item)
    {
        PluginData.StatusEffectActions.Remove(item);
    }

    public void RegisterAction(IStatusEffectActionFactory actionFactory)
    {
        PluginData.StatusEffectActions.Add(actionFactory);
        Register(actionFactory);
    }
}