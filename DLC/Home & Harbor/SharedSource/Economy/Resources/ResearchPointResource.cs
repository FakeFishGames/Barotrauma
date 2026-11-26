using Barotrauma;
using System.Xml.Linq;

namespace HomeAndHarbor.Resources;

internal class ResearchPointResource(ResourcePrefab prefab, ResourceManager manager, XElement? element = null)
    : Resource(prefab, manager, element)
{
    private ResearchManager? researchManager
    {
        get
        {
            ResearchManager.TryGetResearchManagerForCampaign(GameMain.GameSession.Campaign, out ResearchManager? researchManager);
            return researchManager;
        }
    }

    /// <summary>
    /// There's no actual research point amount, since added research points only go towards the currently active research.
    /// </summary>
    public override float Amount
    {
        get => 0;
        set { }
    }

    protected override void SetAmount(float value)
    {
        Amount = value;
    }

    protected override float GetAmountResourceSpecific()
    {
        return Amount;
    }

    public void AddResearchPoints(int amount)
    {
        researchManager?.AddResearchPoints(amount);
    }
}