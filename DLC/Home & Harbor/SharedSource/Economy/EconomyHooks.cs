using Barotrauma;
using HomeAndHarbor.Resources;

namespace HomeAndHarbor
{
    [RunConstructorOnInit]
    public static class EconomyHooks
    {
        /// <summary>
        /// How much tax money is generated per point of Wealth, per world step.
        /// </summary>
        private const float TaxMoneyPerWealth = 1.0f;

        /// <summary>
        /// The taxation calculation only considers Wealth up to this cap.
        /// </summary>
        private const float MaxWealthForTaxation = 200f;

        /// <summary>
        /// Population at which the tax per point of Wealth is unchanged. Tax scales linearly with population relative to this. Example: 5 population is 50% in taxes, while 20 population is 200% in taxes.
        /// </summary>
        private const float TaxReferencePopulation = 10f;

        /// <summary>
        /// The outpost the players left at the end of the round. The world steps processed after leaving (before the next round starts) are the steps spent in that outpost,
        /// so the generators whose effect is already simulated during the round (see <see cref="ResourceGenerator.DisableGenerationWhileInOutpost"/>) shouldn't run in it.
        /// </summary>
        private static PersistentOutpostData? outpostLeftAtRoundEnd;

        static EconomyHooks()
        {
            HookService.RegisterHook<PluginRoundStartedDelegate>(OnRoundStarted);
            HookService.RegisterHook<PluginCampaignOnLeaveOutpostDelegate>(OnLeaveOutpost);
            HookService.RegisterHook<PluginCampaignOnEnterOutpostDelegate>(OnEnterOutpost);
            HookService.RegisterHook<PluginOnProgressWorldDelegate>(OnProgressWorld);
            HookService.RegisterHook<PluginCampaignOnOutpostNPCsChangedDelegate>(OnOutpostNPCsChanged);
            HookService.RegisterHook<PluginOnLocationTypeChangedDelegate>(OnLocationTypeChanged);
            HookService.RegisterHook<PluginCampaignMoneySpentAtLocationDelegate>(OnMoneySpentAtLocation);
            HookService.RegisterHook<PluginCampaignItemBuyPriceMultiplier>(OnCampaignItemPriceMultiplier);
            HookService.RegisterHook<PluginCampaignItemSellPriceMultiplier>(OnCampaignItemPriceMultiplier);
            HookService.RegisterHook<PluginCampaignItemModifyAvailability>(OnCampaignItemModifyyAvailability);
#if SERVER
            HookService.RegisterHook<PluginMultiplayerRoundStartedDelegate>(OnMultiplayerRoundStarted);
#endif
        }

        private static int OnCampaignItemModifyyAvailability(Location location, ItemPrefab itemPrefab, PriceInfo priceInfo, int currentQuantity)
        {
            if (!location.HasOutpost()) { return currentQuantity; }
            PersistentOutpostData data = PersistentOutpostData.GetPersistentOutpostData(location);

            if (data.ResourceManager == null) { return currentQuantity; }

            foreach (var resourceIndicator in data.ResourceManager.GetActiveResourceStatusIndicators())
            {
                foreach (var priceModifier in resourceIndicator.PriceMultipliers)
                {
                    if (priceModifier.IncreaseAvailability && itemPrefab.Tags.Intersect(priceModifier.ItemTags).Any())
                    {
                        return priceInfo.MaxAvailableAmount;
                    }
                }
            }

            return currentQuantity;
        }

        private static float OnCampaignItemPriceMultiplier(Location.StoreInfo storeInfo, ItemPrefab itemPrefab, PriceInfo? priceInfo)
        {
            Location location = storeInfo.Location;

            float priceMultipier = 1f;

            if (!location.HasOutpost()) { return priceMultipier; }
            PersistentOutpostData data = PersistentOutpostData.GetPersistentOutpostData(location);

            if (data.ResourceManager == null) { return priceMultipier; }

            foreach (var resourceIndicator in data.ResourceManager.GetActiveResourceStatusIndicators())
            {
                foreach (var priceModifier in resourceIndicator.PriceMultipliers)
                {
                    if (itemPrefab.Tags.Intersect(priceModifier.ItemTags).Any())
                    {
                        priceMultipier *= priceModifier.Multiplier;
                    }
                }
            }

            return priceMultipier;
        }

        private static void OnProgressWorld(Map map, CampaignMode campaign)
        {
            if (map.Locations.Count == 0) { return; }

            foreach (Location location in map.Locations)
            {
                if (!location.HasOutpost()) { continue; }
                PersistentOutpostData data = PersistentOutpostData.GetPersistentOutpostData(location);

                Dictionary<Identifier, float> diminishingReturns = [];

                if (data.IsPlayerOwned)
                {
                    foreach (ResourceGenerator generator in data.ResourceGenerators)
                    {
                        // The effect of the generator has already been simulated during the round (e.g. reactor burning fuel rods)
                        if (generator.DisableGenerationWhileInOutpost && data == outpostLeftAtRoundEnd) { continue; }

                        // Refresh the skill level from persisted dweller data before generating resources, so production
                        // correctly reflects the characters' current skill even when the outpost is not loaded.
                        if (!generator.SkillIdentifier.IsEmpty)
                        {
                            RefreshGeneratorSkillLevel(generator, data);
                        }

                        generator.Tick(data, ref diminishingReturns);
                    }

                    // Grant skill gain to characters simply for "working" (staying) at their assigned workstations.
                    foreach (OutpostDwellerInfo dwellerInfo in data.DwellerManager.DwellerInfos)
                    {
                        // Dwellers who are unavailable for work (on strike, sick, etc.) don't gain skill even if some assignment data lingers
                        if (dwellerInfo.IsUnavailableForWork) { continue; }
                        if (!dwellerInfo.SkillToGainPerWorldStep.IsEmpty && dwellerInfo.SkillIncreasePerWorldStep > 0f)
                        {
                            dwellerInfo.CharacterInfo.ApplySkillGain(
                                dwellerInfo.SkillToGainPerWorldStep,
                                dwellerInfo.SkillIncreasePerWorldStep);
                        }
                    }

                    // Count down the dwellers unavailable for work (after generating the resources for this step, so the steps without production match the unavailability duration)
                    data.DwellerManager.ProgressWorkUnavailability();
                }

                if (data.ResourceManager != null)
                {
                    foreach (Resource resource in data.ResourceManager.Resources)
                    {
                        resource.Tick(map.WorldStepCount);
                    }
#if SERVER
                    data.ServerWriteResources(createUINotification: false);
#endif
                }

                TaxOutpost(data);
            }
        }

        private static void TaxOutpost(PersistentOutpostData data)
        {
            if (GameMain.NetworkMember is { IsClient: true }) { return; }
            if (!data.IsPlayerOwned || data.ResourceManager == null) { return; }
            if (!data.ResourceManager.TryGetResource("wealth".ToIdentifier(), out Resource? wealthResource)) { return; }
            if (!data.ResourceManager.TryGetResource("currentpopulation".ToIdentifier(), out Resource? populationResource)) { return; }

            float populationMultiplier = populationResource.Amount / TaxReferencePopulation;
            float wealthToTax = MathF.Min(wealthResource.Amount, MaxWealthForTaxation);
            int tax = (int)MathF.Floor(wealthToTax * TaxMoneyPerWealth * populationMultiplier);
            if (tax <= 0) { return; }

            data.OutpostWalletBalance += tax;

            // Log the taxation in Mk under the Wealth resource
            data.StoreResourceChangeEvent(new EconomyLogEntrySpecial(
                textTag: "economylog.taxation".ToIdentifier(),
                resourceIdentifier: wealthResource.Identifier,
                change: tax,
                relatedLocationRef: LogLocationReference.Empty,
                relatedResource: Identifier.Empty,
                relatedResourceAmount: 0f
            ));
#if SERVER
            data.ServerWriteState(); // Sync the new wallet balance to clients
#endif
        }

        private static void OnLocationTypeChanged(Location location, LocationType _)
        {
            PersistentOutpostData outpostData = PersistentOutpostData.GetPersistentOutpostData(location);
            outpostData.ResetResourceManager();
            outpostData.ResetCreatureManager();
            //location type changed while we were present at the location -> update resources to reflect what's actually in the outpost
            if (Level.Loaded is Level { Type: LevelData.LevelType.Outpost, StartOutpost: Submarine startOutpost } level &&
                level.StartLocation == location &&
                GameMain.GameSession?.Campaign != null &&
                outpostData.ResourceManager is ResourceManager resourceManager)
            {
                foreach (var resource in resourceManager.Resources)
                {
                    resource.OnOutpostExited(startOutpost, outpostData, shouldCreateLogs: false);
                }
            }
        }
        /// <summary>
        /// Updates ResourceGenerator.AssignedSkillLevel from persisted dweller data, so skill-scaled resource production is correct even when the outpost is not currently loaded.
        /// </summary>
        private static void RefreshGeneratorSkillLevel(ResourceGenerator generator, PersistentOutpostData outpostData)
        {
            float totalSkill = 0f;
            int count = 0;
            foreach (OutpostDwellerInfo dwellerInfo in outpostData.DwellerManager.DwellerInfos)
            {
                if (dwellerInfo.AssignedWorkstationId != generator.UniqueId)
                {
                    continue;
                }
                totalSkill += dwellerInfo.CharacterInfo.Job.GetSkillLevel(generator.SkillIdentifier);
                count++;
            }
            generator.SetAssignedSkillLevel(count > 0 ? totalSkill / count : 0f);
        }

        private static void OnMoneySpentAtLocation(int price, Location? location)
        {
            // Only run on server or in singleplayer, MP clients get these updates via network sync
            if (GameMain.NetworkMember is { IsClient: true }
                || price == 0
                || location == null)
            {
                return;
            }

            Identifier wealthIdentifier = "wealth".ToIdentifier();

            if (PersistentOutpostData.GetPersistentOutpostData(location) is not PersistentOutpostData outpostData
                || outpostData.ResourceManager == null
                || !outpostData.ResourceManager.LocationHasResource(wealthIdentifier))
            {
                return;
            }

            float wealthGain = price / 100f; // 100 Mk spent = 1 wealth
            outpostData.ResourceManager.AddToResource(wealthIdentifier, wealthGain, out _);
            outpostData.PendingWealthGainToLog += wealthGain;
#if SERVER
            outpostData.ServerWriteResources(createUINotification: true);
#endif
        }

        private static void OnRoundStarted(GameSession gameSession)
        {
            outpostLeftAtRoundEnd = null;
        }

        private static void OnLeaveOutpost(CampaignMode mode)
        {
            PersistentOutpostData outpostData = PersistentOutpostData.GetPersistentOutpostData(mode.CurrentLocation);
            outpostLeftAtRoundEnd = outpostData;
            if (outpostData.ResourceManager == null) { return; }

            foreach (var resource in outpostData.ResourceManager.Resources)
            {
                resource.OnOutpostExited(Level.Loaded.StartOutpost, outpostData);
            }
            RefreshResourceGenerators(Level.Loaded.StartOutpost, outpostData);

            if (outpostData.PendingWealthGainToLog != 0f && GameMain.NetworkMember is not { IsClient: true }
                && outpostData.ResourceManager != null && outpostData.ResourceManager.TryGetResource(new Identifier("wealth"), out Resource? wealthResource))
            {
                float oldAmount = wealthResource.Amount - outpostData.PendingWealthGainToLog;
                outpostData.StoreResourceChangeEvent(EconomyLogEntrySpecial.Create(new Identifier("economylog.tradingwealthgain"), wealthResource, oldAmount, wealthResource.Amount));
            }
            outpostData.PendingWealthGainToLog = 0f;
        }

        private static void OnEnterOutpost(CampaignMode mode, Location location, Submarine outpost)
        {
            PersistentOutpostData outpostData = PersistentOutpostData.GetPersistentOutpostData(location);

            if (outpostData.ResourceManager != null)
            {
                foreach (var resource in outpostData.ResourceManager.Resources.OrderBy(r => r.Prefab.SpawnOrder))
                {
                    resource.OnOutpostEntered(outpost, outpostData);
                }
                //handle refilling emptied containers (like reactors) after we've handled all the adding and removing of items
                foreach (var resource in outpostData.ResourceManager.Resources)
                {
                    if (resource is ItemResource itemResource)
                    {
                        itemResource.FixEmptiedContainers(outpost);
                    }
                }                
                //update resource status indicators even though resources also do it in OnOutpostEntered 
                //some of them may depend on other resource status indicators (e.g. happiness and its indicator "miserable" is affected by "insufficientlighting"),
                //and doing this 2nd pass ensures the order in which we process the indicators doesn't matter (unless there's some cyclical dependency, but that would be a design bug :barodev:)
                foreach (var resource in outpostData.ResourceManager.Resources)
                {
                    resource.UpdateResourceStatusIndicators();
                }
                //do this last, as the triggers may be affected by the resource status indicators too
                foreach (var resource in outpostData.ResourceManager.Resources)
                {
                    resource.TryTriggerEvents();
                }
            }

            outpostData.OutpostFacilities = Option.Some(OutpostFacilityManager.ConstructFromOutpost(outpost));            

            RefreshResourceGenerators(outpost, outpostData);
        }

        private static void OnOutpostNPCsChanged(CampaignMode mode, Location location, Submarine outpost)
        {
            ArgumentNullException.ThrowIfNull(location);
            ArgumentNullException.ThrowIfNull(outpost);

            PersistentOutpostData outpostData = PersistentOutpostData.GetPersistentOutpostData(location);
            if (outpostData.ResourceManager != null)
            {
                foreach (var resource in outpostData.ResourceManager.Resources.OrderBy(r => r.Prefab.SpawnOrder))
                {
                    if (resource is NPCResource npcResource)
                    {
                        npcResource.RefreshAmount(outpost, outpostData);
                    }
                }
            }

            outpostData.OutpostFacilities = Option.Some(OutpostFacilityManager.ConstructFromOutpost(outpost));            

            RefreshResourceGenerators(outpost, outpostData);
        }

        /// <summary>
        /// Make the <see cref="PersistentOutpostData.ResourceGenerators">resource generators in the outpost data</see> 
        /// match the actual outpost generator items currently present in the loaded outpost.
        /// Called on outpost entry and exit so the list is up-to-date for worldstep ticks while the player is away.
        /// </summary>
        private static void RefreshResourceGenerators(Submarine outpost, PersistentOutpostData outpostData)
        {
            outpostData.ResourceGenerators.Clear();
            var outpostComponents = ResourceGeneratorBaseComponent.ResourceGeneratorComponents
                .Where(c => outpost.GetConnectedSubs().Where(s => s.Info.Type is SubmarineType.Outpost or SubmarineType.OutpostModule).Contains(c.Item.Submarine))
                .ToList();

            // Refresh workstations first, because other generators may depend on their active state (eg. plants requiring a farmer)
            foreach (ResourceGeneratorBaseComponent wsComponent in outpostComponents.OrderBy(static c => c is WorkstationComponent ? 0 : 1))
            {
                wsComponent.RefreshIsActive(forceNetworkEvent: true);
            }

            foreach (ResourceGeneratorBaseComponent wsComponent in outpostComponents)
            {
                outpostData.ResourceGenerators.Add(wsComponent.ResourceGenerator);
            }
        }

#if SERVER
        private static void OnMultiplayerRoundStarted(GameSession gameSession)
        {
            if (Level.Loaded?.StartLocation is Location location &&
                gameSession.Campaign != null)
            {
                PersistentOutpostData outpostData = PersistentOutpostData.GetPersistentOutpostData(location);
                //sync resources so clients are notified of the population and other resources that aren't determined until the level has been fully loaded and initialized
                outpostData?.ServerWriteResources(createUINotification: false);
            }
        }
#endif
    }
}