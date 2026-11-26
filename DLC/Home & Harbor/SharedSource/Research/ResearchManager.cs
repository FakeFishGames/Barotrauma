using Barotrauma;
using Barotrauma.Extensions;
using Barotrauma.Networking;
using Barotrauma.Plugins;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

namespace HomeAndHarbor;

/// <summary>
/// Stores and keeps track of ResearchPoints and finished/in progress researches
/// </summary>
[RunConstructorOnInit]
public sealed partial class ResearchManager
{
    [NetworkSerialize]
    public readonly record struct UpdateResearchNetworkData : INetSerializableStruct
    {
        public readonly Identifier TechnologyIdentifier;
        public readonly int Progress;
        public readonly bool IsActiveResearch;
        public TechnologyPrefab? ResearchPrefab => TechnologyPrefab.Prefabs.TryGet(TechnologyIdentifier, out TechnologyPrefab? prefab) ? prefab : null;
        public UpdateResearchNetworkData(Research research, bool isActiveResearch)
        {
            TechnologyIdentifier = research.Prefab.Identifier;
            Progress = research.Progress;
            IsActiveResearch = isActiveResearch;
        }
    }

    [NetworkSerialize]
    public readonly record struct ClientSetActiveResearchNetworkData(Identifier Identifier) : INetSerializableStruct
    {
        public TechnologyPrefab? ResearchPrefab => TechnologyPrefab.Prefabs.TryGet(Identifier, out TechnologyPrefab? prefab) ? prefab : null;
    }

    [NetworkSerialize]
    public readonly record struct SyncResearchNetworkData : INetSerializableStruct
    {
        public readonly ImmutableArray<UpdateResearchNetworkData> ResearchData;

        public SyncResearchNetworkData(IReadOnlyCollection<Research> research, Research? activeResearch)
        {
            ResearchData = research.Select(x => new UpdateResearchNetworkData(x, activeResearch == x)).ToImmutableArray();
        }
    }

    public const string ResearchManagerSaveElement = "ResearchManager";

    private readonly List<Research> currentResearches = [];
    public IReadOnlyCollection<Research> CurrentResearches => currentResearches;

    /// <summary>
    /// The currently active research project
    /// </summary>
    public Research? ActiveResearch { get; private set; }

    static ResearchManager()
    {
        InitProjectSpecific();
    }

    static partial void InitProjectSpecific();

    public ResearchManager(XElement? element)
    {
        if (element == null)
        {
            TechnologyPrefab.Prefabs.Where(static prefab => prefab.UnlockedByDefault).ForEach(Unlock);
            return;
        }

        foreach (XElement subElement in element.Elements())
        {
            switch (subElement.Name.LocalName.ToLowerInvariant())
            {
                case "research":
                    try
                    {
                        currentResearches.Add(new Research(subElement));
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.ThrowError("Error while parsing research: ", ex);
                    }
                    break;
            }
        }

        TechnologyPrefab.Prefabs.Where(static prefab => prefab.UnlockedByDefault).ForEach(Unlock);

        Identifier activeResearchIdentifier = element.GetAttributeIdentifier("activeresearch", Identifier.Empty);
        if (activeResearchIdentifier != Identifier.Empty)
        {
            ActiveResearch = currentResearches.FirstOrDefault(x => x.Prefab.Identifier == activeResearchIdentifier);
        }
    }

    public void Unlock(TechnologyPrefab technologyPrefab)
    {
        if (IsFinished(technologyPrefab)) { return; }

        Research research = new Research(technologyPrefab, true);
        currentResearches.Add(research);
        research.OnFinished();

#if SERVER
        GameNetwork.Broadcast(Plugin.NetworkHeaders.UpdateResearch, new UpdateResearchNetworkData(research, research == ActiveResearch));
#endif
    }

    /// <summary>
    /// Resets the technologies that require campaign metadata (see <see cref="TechnologyPrefab.MetadataRequirement"/>) and the ones that depend on them.
    /// </summary>
    public void ResetMetadataDependentResearch()
    {
        HashSet<Identifier> resetTechnologies = [];
        bool changed;
        do
        {
            changed = false;
            foreach (Research research in currentResearches.ToList())
            {
                TechnologyPrefab prefab = research.Prefab;
                bool dependsOnResetTechnology = prefab.Prerequisites.Any(prerequisite =>
                    prerequisite.Identifiers.Any(resetTechnologies.Contains) && !prerequisite.Matches(this));
                if (prefab.MetadataRequirement.IsEmpty && !dependsOnResetTechnology) { continue; }

                currentResearches.Remove(research);
                if (ActiveResearch == research) { ActiveResearch = null; }
                if (research.IsFinished)
                {
                    ResearchRecipeUnlockPrefab.LockRecipes(prefab, this);
                }
                resetTechnologies.Add(prefab.Identifier);
                changed = true;
            }
        } while (changed);
    }

    public bool IsFinished(TechnologyPrefab technologyPrefab)
        => currentResearches.Any(x => x.Prefab == technologyPrefab && x.IsFinished);

    public bool IsFinished(Identifier identifier)
    {
        System.Diagnostics.Debug.Assert(!identifier.IsEmpty, "Attempted to check whether a research has finished without supplying an identifier.");
        if (identifier.IsEmpty) { return true; }
        return currentResearches.Any(x => x.Prefab.Identifier == identifier && x.IsFinished);
    }

    public static bool IsTierUnlocked(Identifier tierIdentifier)
    {
        CampaignMode? campaignMode = GameMain.GameSession.Campaign;

        if (campaignMode == null || campaignMode.Map == null) { return true; }

        // check for current level
        foreach (var workstation in WorkstationComponent.WorkstationComponents)
        {
            var resourceGenerator = workstation.ResourceGenerator;
            if (resourceGenerator.IsActive && resourceGenerator.UnlockResearchTiers.Contains(tierIdentifier))
            {
                return true;
            }
        }

        // check if any other player hosted outpost has this research station active
        foreach (Location location in campaignMode.Map.Locations)
        {
            if (!location.HasOutpost() || !location.Discovered) { continue; }
            PersistentOutpostData data = PersistentOutpostData.GetPersistentOutpostData(location);

            if (!data.IsPlayerOwned) { continue; }

            foreach (ResourceGenerator resourceGenerator in data.ResourceGenerators)
            {
                if (resourceGenerator.IsActive && resourceGenerator.UnlockResearchTiers.Contains(tierIdentifier))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Returns whenever the specified technology has all research prerequisites met
    /// </summary>
    public bool HasAllPrerequisites(TechnologyPrefab technologyPrefab)
    {
        if (!IsTierUnlocked(technologyPrefab.Tier)) { return false; }
        if (!technologyPrefab.CheckMetadataRequirement()) { return false; }

        foreach (var prerequisite in technologyPrefab.Prerequisites)
        {
            switch (prerequisite.Comparison)
            {
                case PropertyConditional.LogicalOperatorType.And:
                    if (prerequisite.Identifiers.Where(IsFinished).Count() != prerequisite.Identifiers.Length)
                    {
                        return false;
                    }
                    break;
                case PropertyConditional.LogicalOperatorType.Or:
                    if (!prerequisite.Identifiers.Where(IsFinished).Any())
                    {
                        return false;
                    }
                    break;
            }
        }

        return true;
    }

    /// <summary>
    /// Sets the current ActiveResearch
    /// </summary>
    public void SelectActiveResearch(TechnologyPrefab technologyPrefab)
    {
        Research research = GetOrAddResearch(technologyPrefab);

        ActiveResearch = research;

#if SERVER
        GameNetwork.Broadcast(Plugin.NetworkHeaders.UpdateResearch, new UpdateResearchNetworkData(ActiveResearch, true));
#endif
    }

    public void ClearActiveResearch()
    {
#if SERVER
        if (ActiveResearch != null)
        {
            GameNetwork.Broadcast(Plugin.NetworkHeaders.UpdateResearch, new UpdateResearchNetworkData(ActiveResearch, false));
        }
#endif
        ActiveResearch = null;
    }

    /// <summary>
    /// Adds research points to the currently active research.
    /// </summary>
    public void AddResearchPoints(int amount)
    {
        if (ActiveResearch == null)
        {
            return;
        }

        ActiveResearch.AddProgress(amount);

        if (ActiveResearch.IsFinished)
        {
            ClearActiveResearch();
        }
#if SERVER
        else
        {
            GameNetwork.Broadcast(Plugin.NetworkHeaders.UpdateResearch, new UpdateResearchNetworkData(ActiveResearch, true));
        }
#elif CLIENT
        if (OutpostManagementUI.Instance is { } instance)
        {
            foreach (TechnologyUI ui in instance.TechnologyUIs)
            {
                ui.RecreateFrames();
                instance.OutpostOverviewUI?.Refresh();
            }
        }
#endif
    }

    public Research GetOrAddResearch(TechnologyPrefab technologyPrefab)
    {
        Research? research = currentResearches.FirstOrDefault(x => x.Prefab == technologyPrefab);

        if (research == null)
        {
            research = new Research(technologyPrefab);
            currentResearches.Add(research);
        }

        return research;
    }

    public XElement Save()
    {
        XElement element = new XElement(ResearchManagerSaveElement);

        if (ActiveResearch != null)
        {
            element.SetAttributeValue("activeresearch", ActiveResearch.Prefab.Identifier);
        }

        foreach (Research research in currentResearches)
        {
            element.Add(research.Save());
        }

        return element;
    }

    public static bool TryGetResearchManagerForCampaign(CampaignMode? campaignMode, [NotNullWhen(true)] out ResearchManager? researchManager)
    {
        if (campaignMode is null)
        {
            researchManager = null;
            return false;
        }

        researchManager = campaignMode.GetOrAddExtraField(nameof(ResearchManager), new ResearchManager(null));
        return true;
    }
}
