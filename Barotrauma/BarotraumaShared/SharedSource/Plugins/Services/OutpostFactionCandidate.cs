#nullable enable

using System.Collections.Generic;

namespace Barotrauma
{
    /// <summary>
    /// A candidate for outpost faction assignment.
    /// </summary>
    /// <param name="PrimaryFaction">The primary faction. Use null to explicitly assign no faction.</param>
    /// <param name="SecondaryFaction">The secondary faction. Use null to explicitly assign no faction.</param>
    /// <param name="Weight">Relative weight for selection. The base game's default random selection has weight 1. A single plugin contribution with a weight of 1 will give each a 1/2 chance (1 / (1 + 1)).</param>
    /// <param name="OverrideCompletely">If true, bypasses weighted selection and uses this result directly. Intended only for total conversion mods and such, doesn't mix well with other overrides (if more than one plugin has this set the choice will be arbitrary).</param>
    public readonly record struct OutpostFactionCandidate(
        Faction? PrimaryFaction,
        Faction? SecondaryFaction,
        float Weight = 1f,
        bool OverrideCompletely = false);

    /// <summary>
    /// Called when a location becomes an outpost and faction assignment is needed.
    /// Plugins can add candidates to the mix to participate in weighted selection.
    /// Note that if a plugin candidate wins, it will always set both primary and secondary factions, even if empty.
    /// </summary>
    /// <param name="location">The location becoming an outpost.</param>
    /// <param name="campaign">The campaign mode instance.</param>
    /// <param name="candidates">Collection to add candidates to. Use null factions to explicitly assign none.</param>
    public delegate void PluginNewOutpostFactionCandidateDelegate(
        Location location,
        CampaignMode campaign,
        ICollection<OutpostFactionCandidate> candidates);
}
