using Barotrauma;

namespace HomeAndHarbor;

[RunConstructorOnInit]
public static class DisableJovianRadiation
{
    static DisableJovianRadiation()
    {
        HookService.RegisterHook<PluginShouldDisableJovianRadiation>(ShouldDisableJovianRadiationEnabled);
    }

    private static bool ShouldDisableJovianRadiationEnabled(out RichString disabledTooltip)
    {
        disabledTooltip = RichString.Rich(TextManager.Get("campaignoption.enableradiation.dlcdisabledtooltip"));

        return true;
    }
}