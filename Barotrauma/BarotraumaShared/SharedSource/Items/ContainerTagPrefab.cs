#nullable enable

using Barotrauma.Extensions;
using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    internal class ContainerTagPrefab : Prefab
    {
        public static readonly PrefabCollection<ContainerTagPrefab> Prefabs = new();

        /// <summary>
        /// The categories that can be used as the values for <see cref="SelectableInGameFor"/>
        /// </summary>
        private static readonly ImmutableHashSet<Identifier> PlayerFacingCategories = ImmutableHashSet.Create(new Identifier("Submarine"), new Identifier("Outpost"));

        public readonly LocalizedString Name;
        public readonly LocalizedString Description;
        public readonly Identifier Category;
        public readonly int RecommendedAmount;
        public readonly bool WarnIfLess;
        /// <summary>
        /// Should the tag's name be shown to players in the container's UI panel? Disable for tags that are editor concepts only (eg. loot).
        /// </summary>
        public readonly bool ShowInContainerUI;
        /// <summary>
        /// Can players select this tag in-game for containers on their own subs, outposts, or both? See <see cref="GetSelectableTags"/>.
        /// Consider eg. if the tag describes a kind of storage, or something like a device, game mechanics etc.
        /// Defined in XML as a comma-separated list of categories (eg. "Outpost,Submarine"), empty or missing means the tag cannot be selected.
        /// </summary>
        public readonly ImmutableHashSet<Identifier> SelectableInGameFor;

        private static readonly Dictionary<Identifier, Identifier> CategoryToSubmarineType = new()
        {
            { new Identifier("Submarine"), SubmarineType.Player.ToIdentifier() },
            { new Identifier("AbandonedOutpost"), SubmarineType.OutpostModule.ToIdentifier() },
            { new Identifier("Ruin"), SubmarineType.OutpostModule.ToIdentifier() },
            { new Identifier("Enemy"), SubmarineType.AIControlled.ToIdentifier() } // TODO: shouldn't this be "EnemySubmarine"?
            // TODO: properly handle neutral subs
        };

        public bool IsRecommendedForSub(Submarine sub) => CategoryMatchesSub(Category, sub);

        /// <summary>
        /// Can players select this tag in-game for containers on the sub, based on <see cref="SelectableInGameFor"/>?
        /// </summary>
        public bool IsSelectableInGame(Submarine sub) => SelectableInGameFor.Any(category => CategoryMatchesSub(category, sub));

        private static bool CategoryMatchesSub(Identifier category, Submarine sub)
        {
            var type = sub.Info?.Type ?? SubmarineType.Player;
            Identifier subTypeCategory = CategoryToSubmarineType.GetValueOrDefault(category, category);
            return type.ToIdentifier() == subTypeCategory;
        }

        //text tag used for the name, takes the nameidentifier override into account
        private readonly Identifier nameTextTag;

        /// <summary>
        /// Base name shown to players, without the suffix (eg. "(outpost)") which should only be shown in the editor
        /// </summary>
        public LocalizedString BaseName => TextManager.Get(nameTextTag);

        /// <summary>
        /// Can the character change the container tags of the item in-game? Only containers that show their container type can be changed,
        /// and only on the submarines and outposts of the character's own team.
        /// Note: Doesn't check permissions, use the client and server overloads for that.
        /// </summary>
        public static bool CanChangeTags(Item? item, Character? character)
        {
            if (item?.Submarine == null || character == null) { return false; }
            if (item.Submarine.TeamID != character.TeamID) { return false; }
            if (!character.CanInteractWith(item)) { return false; }
            if (item.GetComponents<Items.Components.ItemContainer>().None(ic => ic.ShowContainerType)) { return false; }
            return GetSelectableTags(item).Any();
        }

#if CLIENT
        /// <summary>
        /// Can the controlled character change the container tags of the item (and are they permitted to)
        /// </summary>
        public static bool CanChangeTags(Item? item)
        {
            return CanChangeTags(item, Character.Controlled) &&
                   CampaignMode.AllowedToManageCampaign(ClientPermissions.ManageCampaign);
        }
#elif SERVER
        /// <summary>
        /// Can the client's character change the container tags of the item (and are they permitted to)
        /// </summary>
        public static bool CanChangeTags(Item? item, Client client)
        {
            return CanChangeTags(item, client.Character) &&
                   CampaignMode.AllowedToManageCampaign(client, ClientPermissions.ManageCampaign);
        }
#endif

        /// <summary>
        /// The tags that can be selected for the item in-game: the tags whose <see cref="SelectableInGameFor"/> includes
        /// the type of the sub the item is in (player submarines or outposts).
        /// Only includes tags that are shown in the container UI and have a name text.
        /// </summary>
        public static IEnumerable<ContainerTagPrefab> GetSelectableTags(Item? item)
        {
            if (item?.Submarine is not Submarine sub) { yield break; }
            foreach (ContainerTagPrefab prefab in Prefabs)
            {
                if (prefab.IsSelectableInGame(sub) &&
                    prefab.ShowInContainerUI &&
                    // Check the default language, untranslated names fall back to English like other texts
                    TextManager.ContainsTag(prefab.nameTextTag, TextManager.DefaultLanguage))
                {
                    yield return prefab;
                }
            }
        }

        /// <summary>
        /// Sets which of the selectable container tags the item has, other tags are left untouched.
        /// Can be called from anywhere: a client sends the request to the server and applies it locally,
        /// the server applies it and syncs the new tags to all clients.
        /// Note: Doesn't check if the tags are allowed to be changed, that should be handled by the caller, see <see cref="CanChangeTags"/>.
        /// </summary>
        /// <returns>True if any of the tags changed</returns>
        public static bool SetTags(Item item, IReadOnlyCollection<Identifier> selectedTags)
        {
            bool changed = false;
            foreach (ContainerTagPrefab prefab in GetSelectableTags(item))
            {
                bool shouldHaveTag = selectedTags.Contains(prefab.Identifier);
                //tags defined in the item prefab can't be removed
                if (item.GetTags().Contains(prefab.Identifier) == shouldHaveTag || item.Prefab.Tags.Contains(prefab.Identifier)) { continue; }

                changed = true;
                if (shouldHaveTag)
                {
                    item.AddTag(prefab.Identifier);
                }
                else
                {
                    item.RemoveTag(prefab.Identifier);
                }
            }

            if (!changed) { return false; }

#if CLIENT
            if (GameMain.Client != null)
            {
                GameMain.Client.CreateEntityEvent(item, new Item.SetTagsEventData(selectedTags.ToImmutableArray()));
            }
#elif SERVER
            SyncTags(item);
#endif

            return true;
        }

#if SERVER
        /// <summary>
        /// Sends the current tags of the item to the clients
        /// </summary>
        public static void SyncTags(Item item)
        {
            GameMain.Server?.CreateEntityEvent(item,
                new Item.ChangePropertyEventData(item.SerializableProperties[nameof(Item.Tags).ToIdentifier()], item));
        }
#endif

        public ContainerTagPrefab(ContentXElement element, ContainerTagFile file) : base(file, element.GetAttributeIdentifier("identifier", ""))
        {
            Category = element.GetAttributeIdentifier("category", "");

            var nameOverride = element.GetAttributeString("nameidentifier", string.Empty);
            nameTextTag = $"tagname.{(string.IsNullOrEmpty(nameOverride) ? Identifier.Value : nameOverride)}".ToIdentifier();

            Name = string.IsNullOrEmpty(nameOverride)
                ? TextManager.Get($"tagname.{Identifier}").Fallback(Identifier.Value)
                : TextManager.Get($"tagname.{nameOverride}").Fallback(Identifier.Value);

            Description = string.IsNullOrEmpty(nameOverride)
                ? TextManager.Get($"tagdescription.{Identifier}")
                : TextManager.Get($"tagdescription.{nameOverride}");

            var suffix = element.GetAttributeString("suffix", string.Empty);
            if (!string.IsNullOrEmpty(suffix))
            {
                Name = TextManager.GetWithVariable($"{suffix}.tagnamesuffix", "[tagname]", Name);
            }

            RecommendedAmount = element.GetAttributeInt("recommendedamount", 0);
            WarnIfLess = element.GetAttributeBool("warnifless", true);
            ShowInContainerUI = element.GetAttributeBool("showincontainerui", true);
            var selectableInGameFor = element.GetAttributeIdentifierArray("selectableingamefor", Array.Empty<Identifier>());
            foreach (Identifier category in selectableInGameFor.Where(c => !PlayerFacingCategories.Contains(c)))
            {
                DebugConsole.ThrowError(
                    $"Error in container tag '{Identifier}': '{category}' is not a valid SelectableInGameFor value, only the categories {string.Join(", ", PlayerFacingCategories)} can be selected in-game.",
                    contentPackage: element.ContentPackage);
            }
            SelectableInGameFor = selectableInGameFor.Where(PlayerFacingCategories.Contains).ToImmutableHashSet();
        }

        /// <summary>
        /// The container tags of the item that are shown to players in the container's UI panel, see <see cref="GetContainerTypeText"/>.
        /// Considers the tags set on the item instance as well as the prefab, and only container tags with a tagname text.
        /// </summary>
        public static IEnumerable<ContainerTagPrefab> GetDisplayedTags(Item item)
        {
            foreach (Identifier tag in item.GetTags().Union(item.Prefab.Tags))
            {
                if (Prefabs.TryGet(tag, out ContainerTagPrefab? tagPrefab) &&
                    tagPrefab.ShowInContainerUI &&
                    // Check the default language, untranslated names fall back to English like other texts
                    TextManager.ContainsTag(tagPrefab.nameTextTag, TextManager.DefaultLanguage))
                {
                    yield return tagPrefab;
                }
            }
        }

        /// <summary>
        /// Returns a text describing the container type of the given container, or null if it has no matching tags.
        /// Considers the tags set on the item instance as well as the prefab, and only container tags with a tagname text.
        /// Uses the base name without the editor suffix (e.g. "(outpost)"), and tags with the same base name are counted once.
        /// If one tag matches, returns its name. If two match, returns both names combined.
        /// If more match, returns a generic "diverse item storage" text.
        /// </summary>
        public static LocalizedString? GetContainerTypeText(Items.Components.ItemContainer container)
        {
            var matchingNames = GetDistinctDisplayedTags(container.Item).Select(t => t.BaseName).ToList();

            if (matchingNames.Count == 0) { return null; }
            if (matchingNames.Count == 2)
            {
                return TextManager.GetWithVariables("containertype.two",
                    ("[tag1]", matchingNames[0]),
                    ("[tag2]", matchingNames[1]));
            }
            if (matchingNames.Count > 2) { return TextManager.Get("containertype.multiple"); }
            return matchingNames[0];
        }

        /// <summary>
        /// <see cref="GetDisplayedTags"/> sorted by base name, with tags sharing the same base name included just once.
        /// </summary>
        public static List<ContainerTagPrefab> GetDistinctDisplayedTags(Item item)
        {
            var matchingTags = new List<ContainerTagPrefab>();
            foreach (ContainerTagPrefab tagPrefab in GetDisplayedTags(item)
                         // Note: Sorted so the order of the names doesn't depend on the order the tags happen to be stored in
                         .OrderBy(t => t.BaseName.Value))
            {
                // Use the base name without the editor suffix (e.g. 'secarmcab' and 'outpostsecarmcab' are both just "Secured arms cabinet")
                LocalizedString baseName = tagPrefab.BaseName;
                // Different tags can share the same base name, count them just once
                if (matchingTags.None(t => t.BaseName.Value == baseName.Value))
                {
                    matchingTags.Add(tagPrefab);
                }
            }
            return matchingTags;
        }

        public readonly record struct ItemAndProbability(ItemPrefab Prefab, float Probability, float CampaignProbability);

        public ImmutableArray<ItemAndProbability> GetItemsAndSpawnProbabilities()
        {
            var items = ImmutableArray.CreateBuilder<ItemAndProbability>();
            foreach (ItemPrefab ip in ItemPrefab.Prefabs)
            {
                bool found = false;
                float spawnProbability = 0f;
                float campaignSpawnProbability = 0f;

                foreach (PreferredContainer pc in ip.PreferredContainers)
                {
                    if (!pc.Primary.Contains(Identifier) && !pc.Secondary.Contains(Identifier)) { continue; }

                    found = true;
                    spawnProbability = Math.Max(pc.SpawnProbability, spawnProbability);
                    if (!pc.NotCampaign)
                    {
                        campaignSpawnProbability = Math.Max(spawnProbability, campaignSpawnProbability);
                    }

                    if (!pc.NotCampaign || pc.CampaignOnly)
                    {
                        campaignSpawnProbability = Math.Max(pc.SpawnProbability, campaignSpawnProbability);
                    }
                }

                if (found)
                {
                    items.Add(new ItemAndProbability(ip, spawnProbability, campaignSpawnProbability));
                }
            }
            return items.ToImmutable();
        }

        public static void CheckForContainerTagErrors()
        {
            var allContainerTagsInTheGame = new HashSet<Identifier>();
            var vanillaContainerTags = new HashSet<Identifier>();

            foreach (var prefab in ItemPrefab.Prefabs)
            {
                foreach (Identifier tag in prefab.PreferredContainers.SelectMany(pc => Enumerable.Union(pc.Primary, pc.Secondary)))
                {
                    allContainerTagsInTheGame.Add(tag);
                    if (prefab.ContentPackage == GameMain.VanillaContent && !TagExistsInItemOrCharacterPrefab(tag))
                    {
                        vanillaContainerTags.Add(tag);
                    }
                }
            }

            static bool TagExistsInItemOrCharacterPrefab(Identifier tag)
            {
                if (CharacterPrefab.Prefabs.TryGet(tag, out _))
                {
                    return true;
                }
                
                foreach (var prefab in ItemPrefab.Prefabs)
                {
                    if (prefab.Tags.Contains(tag) || prefab.Identifier == tag) { return true; }
                }

                return false;
            }

            // Find container tags that are defined in a ContainerTagPrefab but not used in any item prefabs.
            foreach (var prefab in Prefabs)
            {
                if (!allContainerTagsInTheGame.Contains(prefab.Identifier))
                {
                    DebugConsole.AddWarning($"Container tag \"{prefab.Identifier}\" defined in ContainerTagPrefab is not used in any item prefabs, did you misspell it? It's also possible mods override container tags in a way that causes some of the pre-defined tags to become unused.", 
                        contentPackage: prefab.ContentPackage);
                }
            }

            // Find container tags that are used in vanilla item prefabs but not defined in a ContainerTagPrefab.
            // We only check vanilla item prefabs because we don't want to force modders to define all vanilla container tags.
            foreach (var vanillaTag in vanillaContainerTags)
            {
                //mission types can be appended to the respawn container tags for mission-specific respawn items,
                //check those too so we don't unnecessarily log a warning
                if (vanillaTag.StartsWith(Tags.RespawnContainer))
                {
                    bool isMissionSpecificRespawnContainer = false;
                    foreach (var missionType in MissionPrefab.Prefabs.Select(p => p.Type).Distinct())
                    {
                        if (vanillaTag == Tags.RespawnContainer + "_" + missionType)
                        {
                            isMissionSpecificRespawnContainer = true;
                            break;
                        }
                    }
                    if (isMissionSpecificRespawnContainer) { continue; }

                }
                if (Prefabs.None(p => p.Identifier == vanillaTag))
                {
                    DebugConsole.AddWarning($"Container tag \"{vanillaTag}\" is used in vanilla item prefabs but not defined in a ContainerTagPrefab.");
                }
            }
        }

        public override void Dispose() { }
    }
}