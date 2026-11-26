using Barotrauma;
using Barotrauma.Plugins;
using System.Xml.Linq;

namespace HomeAndHarbor
{
    [RunConstructorOnInit]
    public static class CampaignSaveLoadHooks
    {
        private const string ShortcutsUnlockedString = "ShortcutsUnlocked";
        public const string PlayerHasOwnedOutpost = "PlayerHasOwnedOutpost";

        static CampaignSaveLoadHooks()
        {
            HookService.RegisterHook<PluginCampaignSaveDelegate>(OnCampaignSave);
            HookService.RegisterHook<PluginCampaignLoadDelegate>(OnCampaignLoad);
        }

        private static void OnCampaignLoad(CampaignMode mode, Option<XElement> loadElement, CampaignSettings settings)
        {
            if (!loadElement.TryUnwrap(out XElement? element)) { return; }

            ResourceGeneratorBaseComponent.LastAssignedUniqueId = element.GetAttributeInt(nameof(ResourceGeneratorBaseComponent.LastAssignedUniqueId), 0);

            mode.SetExtraField(
                key: nameof(ResourceGeneratorBaseComponent.LastAssignedUniqueId), 
                value: element.GetAttributeInt(name: nameof(ResourceGeneratorBaseComponent.LastAssignedUniqueId), defaultValue: 0));

            mode.SetExtraField(PlayerHasOwnedOutpost, element.GetAttributeBool(PlayerHasOwnedOutpost, defaultValue: false));

            bool shortcutsUnlockedInSave = element.GetAttributeBool(ShortcutsUnlockedString, defaultValue: false);
            LocationShortcutManager.SetShortcutsUnlocked(mode, shortcutsUnlockedInSave, createNetworkEvent: false);

            bool spreadingEnabled = element.GetAttributeBool(EnableDescendantsSpreadingAction.FieldSpreadingEnabled, defaultValue: shortcutsUnlockedInSave);
            mode.SetExtraField(EnableDescendantsSpreadingAction.FieldSpreadingEnabled, spreadingEnabled);

            mode.SetExtraField(EnableDescendantsSpreadingAction.FieldWorldStepsPerSpreadAttempt,
                Math.Max(1, element.GetAttributeInt(EnableDescendantsSpreadingAction.FieldWorldStepsPerSpreadAttempt,
                    EnableDescendantsSpreadingAction.DefaultWorldStepsPerSpreadAttempt)));
            mode.SetExtraField(EnableDescendantsSpreadingAction.FieldMaxDistanceFromShortcut,
                Math.Max(0, element.GetAttributeInt(EnableDescendantsSpreadingAction.FieldMaxDistanceFromShortcut,
                    EnableDescendantsSpreadingAction.DefaultMaxDistanceFromShortcut)));

            int[] toAdd = element.GetAttributeIntArray(EnableDescendantsSpreadingAction.FieldDescendantsToAdd, Array.Empty<int>());
            if (toAdd.Length > 0)
            {
                mode.SetExtraField(EnableDescendantsSpreadingAction.FieldDescendantsToAdd, toAdd);
            }

            foreach (XElement subElement in element.Elements())
            {
                if (subElement.NameAsIdentifier() == ItemSwapStorage.ItemSwapStorageSaveElement)
                {
                    mode.SetExtraField(nameof(ItemSwapStorage), new ItemSwapStorage(subElement));
                }
                else if (subElement.NameAsIdentifier() == ResearchManager.ResearchManagerSaveElement)
                {
                    mode.SetExtraField(nameof(ResearchManager), new ResearchManager(subElement));
                }
            }

            //backwards compatibility: set PlayerHasOwnedOutpost if it isn't present in the XML, even though the player owns some outposts
            if (mode.Map is Map map && 
                map.Locations.Any(location => PersistentOutpostData.GetPersistentOutpostData(location) is { IsPlayerOwned: true }))
            {
                mode.SetExtraField(PlayerHasOwnedOutpost, true);
            }
        }

        private static void OnCampaignSave(CampaignMode mode, XElement save)
        {
            if (mode.TryGetExtraField(nameof(ResourceGeneratorBaseComponent.LastAssignedUniqueId), out int lastAssignedUniqueId))
            {
                save.SetAttributeValue(nameof(ResourceGeneratorBaseComponent.LastAssignedUniqueId), lastAssignedUniqueId);
            }

            save.SetAttributeValue(PlayerHasOwnedOutpost, mode.GetExtraField<bool>(PlayerHasOwnedOutpost));
            save.SetAttributeValue(ShortcutsUnlockedString, LocationShortcutManager.AreShortcutsUnlocked(mode));
            save.SetAttributeValue(EnableDescendantsSpreadingAction.FieldSpreadingEnabled, mode.GetExtraField<bool>(EnableDescendantsSpreadingAction.FieldSpreadingEnabled));

            if (mode.TryGetExtraField(EnableDescendantsSpreadingAction.FieldWorldStepsPerSpreadAttempt, out int stepsPerAttempt) && stepsPerAttempt > 0)
            {
                save.SetAttributeValue(EnableDescendantsSpreadingAction.FieldWorldStepsPerSpreadAttempt, stepsPerAttempt);
            }
            if (mode.TryGetExtraField(EnableDescendantsSpreadingAction.FieldMaxDistanceFromShortcut, out int maxDist) && maxDist >= 0)
            {
                save.SetAttributeValue(EnableDescendantsSpreadingAction.FieldMaxDistanceFromShortcut, maxDist);
            }

            if (mode.TryGetExtraField(EnableDescendantsSpreadingAction.FieldDescendantsToAdd, out int[]? descendantsToAdd))
            {
                save.SetAttributeValue(EnableDescendantsSpreadingAction.FieldDescendantsToAdd, string.Join(",", descendantsToAdd));
            }

            if (mode.TryGetExtraField(nameof(ItemSwapStorage), out ItemSwapStorage? storage))
            {
                XElement saveElement = storage.Save();
                save.Add(saveElement);
            }
            if (mode.TryGetExtraField(nameof(ResearchManager), out ResearchManager? researchManager))
            {
                XElement saveElement = researchManager.Save();
                save.Add(saveElement);
            }
        }
    }
}