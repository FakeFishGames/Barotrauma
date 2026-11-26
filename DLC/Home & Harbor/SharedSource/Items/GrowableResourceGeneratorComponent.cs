using Barotrauma;
using Barotrauma.Items.Components;
using System.Xml.Linq;

namespace HomeAndHarbor
{
    /// <summary>
    /// Hardcoded component for growable items that configures the underlying resource generator
    /// to produce items based on the growable component of the seed.
    /// </summary>
    public class GrowableResourceGeneratorComponent(Item item, ContentXElement element) : ResourceGeneratorBaseComponent(item, element)
    {
        private Growable? growable;

        [Serialize("", IsPropertySaveable.No, description: "Workstation (item identifier or tag) that must be active (=staffed) in the same outpost for the plant to grow and produce resources, probably the farmer workstation.")]
        public Identifier RequiredWorkstation { get; set; }

        [Serialize(0.8f, IsPropertySaveable.No, description: "Growth rate while the outpost isn't loaded, relative to 1 vine per growth interval.")]
        public float AwayGrowthRateMultiplier { get; set; }

        private const float IsActiveRefreshIntervalSeconds = 1.0f;
        private float isActiveRefreshTimer;

        /// <summary>
        /// World step when the item was saved, used to catch up on the growth while the outpost wasn't loaded. Null if there's nothing to catch up on.
        /// Note: only saved if the generator was active.
        /// </summary>
        private int? lastSavedWorldStep;

        private bool catchUpChecked;

        /// <summary>
        /// Vine count the produced Amounts were last calculated from, to recalculate them whenever it changes.
        /// </summary>
        private int vineCountAmountsCalculatedFrom = -1;

        /// <summary>
        /// Amounts of the generator's ProducedResources in the item XML, when fully grown.
        /// </summary>
        private float[] fullyGrownAmounts = [];

        public override void OnItemLoaded()
        {
            base.OnItemLoaded();
            //must be kept active to update the state of the resource generator
            IsActive = true;
            growable = Item.GetComponent<Growable>();
            if (growable is null) { return; }

            ResourceGenerator.ProducedResourcesSetByComponent = true;
            // The Amounts follow the plant's growth, so they need to be kept while the outpost isn't loaded
            fullyGrownAmounts = ResourceGenerator.ProducedResourcesList.Select(static p => p.Amount).ToArray();

            // overwrite the added items of the resource generator with the growable's produced items
            foreach (var produced in ResourceGenerator.ProducedResourcesList)
            {
                produced.ItemResourceAddedItems.Clear();
            }

            foreach (ProducedItem producedItem in growable.ProducedItems)
            {
                if (producedItem.Prefab is null) { continue; }

                foreach (var produced in ResourceGenerator.ProducedResourcesList)
                {
                    produced.ItemResourceAddedItems.Add(producedItem.Prefab.Identifier, producedItem.Probability);
                }
            }
        }

        /// <summary>
        /// Approximation: about 1 new vine per growth interval in optimal conditions (assumed to be maintained by the farmer while away),
        /// scaled by <see cref="AwayGrowthRateMultiplier"/>
        /// </summary>
        private float GetVinesPerWorldStep(Growable growable)
            => growable.GrowthInterval > 0f ? CampaignMode.WorldStepLengthMinutes * 60f / growable.GrowthInterval * AwayGrowthRateMultiplier : 0f;

        /// <summary>
        /// Projects output multiplier for each upcoming world step, from the current growth until the plant is fully grown.
        /// </summary>
        private float[] ProjectOutputMultipliers(Growable growable)
        {
            if (growable.MaximumVines <= 0) { return []; }

            float growthPerStep = GetVinesPerWorldStep(growable) / growable.MaximumVines;
            float growthProgress = growable.GrowthProgress;
            List<float> multipliers = [growable.GetProductSpawnChanceMultiplier()];
            while (!growable.FullyGrown && growthProgress < 1f && growthPerStep > 0f)
            {
                growthProgress = MathF.Min(growthProgress + growthPerStep, 1f);
                multipliers.Add(growable.GetProductSpawnChanceMultiplier(growthProgress));
            }
            return multipliers.ToArray();
        }

        /// <summary>
        /// Sets the generator's produced Amounts based on the plant's growth (current and projected)
        /// </summary>
        private void UpdateProducedAmounts(Growable updatedGrowable)
        {
            float[] multipliers = ProjectOutputMultipliers(updatedGrowable);
            if (multipliers.Length == 0) { return; }

            var producedResources = ResourceGenerator.ProducedResourcesList;
            for (int i = 0; i < producedResources.Count && i < fullyGrownAmounts.Length; i++)
            {
                float fullyGrownAmount = fullyGrownAmounts[i];
                producedResources[i] = producedResources[i] with
                {
                    Amount = fullyGrownAmount * multipliers[0],
                    UpcomingAmountsSchedule = multipliers[1..].Select(m => fullyGrownAmount * m).ToArray()
                };
            }
        }

        public override void Load(ContentXElement componentElement, bool usePrefabValues, IdRemap idRemap, bool isItemSwap)
        {
            base.Load(componentElement, usePrefabValues, idRemap, isItemSwap);
            if (componentElement.GetAttribute(nameof(lastSavedWorldStep)) != null)
            {
                lastSavedWorldStep = componentElement.GetAttributeInt(nameof(lastSavedWorldStep), 0);
            }
        }

        public override XElement Save(XElement parentElement)
        {
            XElement element = base.Save(parentElement);
            if (!catchUpChecked)
            {
                // Not caught up yet (eg. the save at round start), so the vines are still from the previous save's step
                if (lastSavedWorldStep is int savedStep)
                {
                    element.Add(new XAttribute(nameof(lastSavedWorldStep), savedStep));
                }
            }
            else if (GameMain.GameSession?.Campaign?.Map is Map map && ResourceGenerator.IsActive)
            {
                element.Add(new XAttribute(nameof(lastSavedWorldStep), map.WorldStepCount));
            }

            // Restart the output projection from the current growth, so it ignores elapsed world steps progressed at the
            // end of the round and starts from the same world step as the catch-up growth
            if (growable is not null)
            {
                UpdateProducedAmounts(growable);
            }

            return element;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            base.Update(deltaTime, cam);

            if (growable is null)
            {
                ResourceGenerator.IsActive = false;
                return;
            }

            // Catch up growth once per load (while loaded the plant grows the normal way)
            if (!catchUpChecked && GameMain.NetworkMember is not { IsClient: true })
            {
                catchUpChecked = true;
                if (GameMain.GameSession?.Campaign?.Map is Map map && lastSavedWorldStep is int savedStep && map.WorldStepCount > savedStep)
                {
                    CatchUpGrowth(map.WorldStepCount - savedStep);
                }
            }

            isActiveRefreshTimer -= deltaTime;
            if (isActiveRefreshTimer <= 0f)
            {
                isActiveRefreshTimer = IsActiveRefreshIntervalSeconds;
                RefreshIsActive();
            }
            
            // Recalculate the output projection after loading and whenever the plant has grown
            if (growable.Vines.Count != vineCountAmountsCalculatedFrom)
            {
                vineCountAmountsCalculatedFrom = growable.Vines.Count;
                UpdateProducedAmounts(growable);
            }
        }

        /// <summary>
        /// Any planted plant tended to by an active <see cref="RequiredWorkstation"/> is an active generator.
        /// </summary>
        protected override bool ShouldResourceGeneratorBeActive()
        {
            if (growable is null || growable.Decayed || Item.Removed || growable.GetPlanter() is null) { return false; }
            if (RequiredWorkstation.IsEmpty) { return true; }
            if (Item.Submarine is not Submarine sub) { return false; }

            // Uses the workstation's own active state (staffed, has required items etc.), so they're both frozen the same way while away
            return WorkstationComponent.WorkstationComponents.Any(ws =>
                ws.Item.Submarine is Submarine wsSub && wsSub.Info.Type == sub.Info.Type && sub.IsConnectedTo(wsSub) &&
                ws.ResourceGenerator.IsActive &&
                (ws.Item.Prefab.Identifier == RequiredWorkstation || ws.Item.HasTag(RequiredWorkstation)));
        }

        /// <summary>
        /// Grow the plant by the amount <see cref="ResourceGenerator.Tick"/> approximated for the world steps spent away.
        /// The vines are grown immediately, regardless of the current conditions (since the growth has already "happened"),
        /// and synced to clients like any other growth.
        /// </summary>
        private void CatchUpGrowth(int worldSteps)
        {
            if (growable is null || growable.Decayed || growable.GetPlanter() is null) { return; }
            if (Item.Submarine is not { Info.IsOutpost: true } || GameMain.GameSession?.Map?.CurrentLocation is not Location location) { return; }

            if (!PersistentOutpostData.GetPersistentOutpostData(location).IsPlayerOwned) { return; }

            growable.GrowVinesImmediately((int)MathF.Round(worldSteps * GetVinesPerWorldStep(growable)));
        }
    }
}