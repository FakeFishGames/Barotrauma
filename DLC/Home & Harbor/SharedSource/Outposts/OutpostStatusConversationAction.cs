using Barotrauma;
using System.Text;

namespace HomeAndHarbor;

/// <summary>
/// Action to show the current outpost's status report as part of the conversation dialogue.
/// </summary>
internal class OutpostStatusConversationAction(ScriptedEvent parentEvent, ContentXElement element) : OutpostConversationActionBase(parentEvent, element)
{
    protected override LocalizedString BuildMessageAndApply(PersistentOutpostData outpostData, Location location)
    {
        if (outpostData.ResourceManager == null)
        {
            DebugConsole.ThrowError($"Error in {nameof(BuildMessageAndApply)}: no {nameof(ResourceManager)} available in the location {location.DisplayName}.");
            return LocalizedString.EmptyString;
        }

        var sb = new StringBuilder();

        // Population count
        int currentPopulation = outpostData.DwellerManager.CurrentPopulation;
        sb.AppendLine(TextManager.GetWithVariable("outpoststatus.population",
            "[count]", currentPopulation.ToString()).Value);

        // Resources: get the same ResourceManager info shown when hovering over the location in campaign map
        var resources = outpostData.ResourceManager.Resources
            .Where(r => !r.Hidden) // Only show non-hidden resources
            .OrderBy(r => r.Prefab.Name);

        if (resources.Any())
        {
            sb.AppendLine();
            sb.AppendLine(TextManager.Get("outpoststatus.resources").Value);

            foreach (var resource in resources)
            {
                sb.AppendLine(TextManager.GetWithVariables("outpoststatus.resourceline",
                    ("[name]", resource.Prefab.Name.Value),
                    ("[amount]", resource.GetAmountText()))
                    .Value);
            }
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine(TextManager.Get("outpoststatus.noresources").Value);
        }

        return sb.ToString();
    }
}
