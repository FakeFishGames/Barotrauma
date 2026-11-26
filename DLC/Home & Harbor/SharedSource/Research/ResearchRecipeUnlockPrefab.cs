using Barotrauma;

namespace HomeAndHarbor
{
    public sealed class ResearchRecipeUnlockPrefab(ContentFile file, ContentXElement element)
        : Prefab(file, element.GetAttributeIdentifier("overrideidentifier", Identifier.Empty))
    {
        public readonly Identifier TargetTechnologyPrefab = element.GetAttributeIdentifier("technologyprefab", Identifier.Empty);
        private readonly Identifier[] unlockedItems = element.GetAttributeIdentifierArray("unlockitemidentifiers", []);

        public override void Dispose() { }

        public static readonly PrefabCollection<ResearchRecipeUnlockPrefab> Prefabs = [];

        public static void UnlockRecipes(TechnologyPrefab prefab)
        {
            if (GameMain.GameSession is not { } session)
            {
                DebugConsole.ThrowError($"Attempted to unlock recipes for technology prefab '{prefab.Identifier}' while a game isn't running.");
                return;
            }

            foreach (ResearchRecipeUnlockPrefab recipePrefab in Prefabs)
            {
                if (recipePrefab.TargetTechnologyPrefab != prefab.Identifier) { continue; }
                foreach (Identifier identifier in recipePrefab.unlockedItems)
                {
                    // safe to assume Team1 I think
                    session.UnlockRecipe(CharacterTeamType.Team1, identifier, showNotifications: true);
                }
            }
        }

        /// <summary>
        /// Locks the recipes unlocked by the technology, unless some other finished technology unlocks them too.
        /// </summary>
        public static void LockRecipes(TechnologyPrefab prefab, ResearchManager researchManager)
        {
            if (GameMain.GameSession is not { } session) { return; }

            foreach (ResearchRecipeUnlockPrefab recipePrefab in Prefabs.Where(r => r.TargetTechnologyPrefab == prefab.Identifier))
            {
                foreach (Identifier identifier in recipePrefab.unlockedItems)
                {
                    bool unlockedByOtherTechnology = Prefabs.Any(otherPrefab =>
                        otherPrefab.TargetTechnologyPrefab != prefab.Identifier &&
                        otherPrefab.unlockedItems.Contains(identifier) &&
                        researchManager.IsFinished(otherPrefab.TargetTechnologyPrefab));
                    if (unlockedByOtherTechnology) { continue; }
                    session.LockRecipe(CharacterTeamType.Team1, identifier);
                }
            }
        }
    }
}