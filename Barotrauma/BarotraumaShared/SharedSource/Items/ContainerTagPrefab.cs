#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    internal class ContainerTagPrefab : Prefab
    {
        public static readonly PrefabCollection<ContainerTagPrefab> Prefabs = new();

        public readonly LocalizedString Name;
        public readonly LocalizedString Description;
        public readonly Identifier Category;
        public readonly int RecommendedAmount;
        public readonly bool WarnIfLess;

        private static readonly Dictionary<Identifier, Identifier> categoryToSubmarineType = new()
        {
            { new Identifier("Submarine"), SubmarineType.Player.ToIdentifier() },
            { new Identifier("AbandonedOutpost"), SubmarineType.OutpostModule.ToIdentifier() },
            { new Identifier("Ruin"), SubmarineType.OutpostModule.ToIdentifier() },
            { new Identifier("Enemy"), SubmarineType.EnemySubmarine.ToIdentifier() }
        };

        public bool IsRecommendedForSub(Submarine sub)
        {
            var type = sub.Info?.Type ?? SubmarineType.Player;
            Identifier category = categoryToSubmarineType.GetValueOrDefault(Category, Category);
            return type.ToIdentifier() == category;
        }

        public ContainerTagPrefab(ContentXElement element, ContainerTagFile file) : base(file, element.GetAttributeIdentifier("identifier", ""))
        {
            Category = element.GetAttributeIdentifier("category", "");

            var nameOverride = element.GetAttributeString("nameidentifier", string.Empty);

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
                if (Prefabs.All(p => p.Identifier != vanillaTag))
                {
                    DebugConsole.AddWarning($"Container tag \"{vanillaTag}\" is used in vanilla item prefabs but not defined in a ContainerTagPrefab.");
                }
            }
        }

        public override void Dispose() { }
    }
}