using System.Text;
using Barotrauma;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace HomeAndHarbor
{
    internal class OutpostOverviewUI
    {
        private GUIComponent? originalParent;
        private GUILayoutGroup? contentContainer;
        private PersistentOutpostData outpostData;
        private Location selectedLocation;
        private readonly CampaignMode? campaign;
        private readonly OutpostManagementUI outpostUi;
        private static readonly string[] ResourcesToShowInResourceTab = ["food", "wealth", "producedgoods", "security"],
                                         ResourcesToShowInDwellersTab = ["currentpopulation", "bunks", "happiness"];

        public Location SelectedLocation => selectedLocation;

        /// <summary>
        /// Gap between the overview panels
        /// </summary>
        private static int PanelSpacing => GUI.IntScale(16);

        /// <summary>
        /// Id used on the OnDwellerWorkstationChanged
        /// </summary>
        private static readonly Identifier DwellerEventIdentifier = nameof(OutpostOverviewUI).ToIdentifier();
        private static readonly Identifier OutpostManagerWorkstationTag = new Identifier("outpostmanagement");

        private enum AlertCategory
        {
            Resource,
            Dwellers,
            Research
        }

        public OutpostOverviewUI(OutpostManagementUI outpostUi, CampaignMode? campaign, Location currentLocation, PersistentOutpostData? currentOutpostData, GUIComponent parent)
        {
            this.outpostUi = outpostUi;
            this.campaign = campaign;

            selectedLocation = 
                currentOutpostData is { IsPlayerOwned: true } ? 
                    currentLocation : 
                    PersistentOutpostData.GetOwnedLocations().FirstOrDefault() ?? currentLocation;
            outpostData = PersistentOutpostData.GetPersistentOutpostData(selectedLocation);

            CreateUI(parent);
            SubscribeToDwellerEvents(outpostData.DwellerManager);
        }

        private void SubscribeToDwellerEvents(OutpostDwellerManager manager)
        {
            manager.OnDwellerWorkstationChanged.RegisterOverwriteExisting(DwellerEventIdentifier, _ =>
            {
                Refresh();
            });
        }

        public void Dispose()
        {
            outpostData.DwellerManager.OnDwellerWorkstationChanged.Deregister(DwellerEventIdentifier);
        }

        private static readonly Identifier FoodIdentifier = new Identifier("food"),
                                           ResearchIdentifier = new Identifier("researchpoints"),
                                           BunksIdentifier = new Identifier("bunks"),
                                           PopulationIdentifier = new Identifier("currentpopulation"),
                                           ReactorFuelIdentifier = new Identifier("reactorfuel"),
                                           EthanolIdentifier = new Identifier("ethanol"),
                                           OxygenIdentifier = new Identifier("oxygengenerator"),
                                           OxygenDispenserIdentifier = new Identifier("oxygendispenser"),
                                           OxygenPlantIdentifier = new Identifier("oxygenplant");

        private IEnumerable<(AlertCategory Category, LocalizedString Message)> GetAlerts()
        {
            ResourceManager? rs = outpostData.ResourceManager;
            if (rs is null) { yield break; }

            if (outpostData.OutpostFacilities.TryUnwrap(out var facilities) && facilities.GetCountByTag(OutpostManagerWorkstationTag) <= 0)
            {
                yield return (AlertCategory.Dwellers, TextManager.Get("outpostoverview.noadminterminal"));
            }

            if (rs.GetResource(ReactorFuelIdentifier) is { } reactorFuelResource && rs.GetResource(EthanolIdentifier) is { } ethanolResource)
            {
                var (nuclearConsumption, nuclearProduction) = outpostUi.GetResourceGenerationForResource(reactorFuelResource);
                var (ethanolConsumption, ethanolProduction) = outpostUi.GetResourceGenerationForResource(ethanolResource);

                bool runningOutOfReactorFuel = nuclearConsumption > nuclearProduction,
                     runningOutOfEthanol = ethanolConsumption > ethanolProduction;

                if (runningOutOfReactorFuel || runningOutOfEthanol)
                {
                    yield return (AlertCategory.Resource, TextManager.Get("outpostoverview.fuellow"));
                }

                if (reactorFuelResource.Amount <= 0 && ethanolResource.Amount <= 0)
                {
                    yield return (AlertCategory.Resource, TextManager.Get("outpostoverview.fuelzero"));
                }
            }

            if (outpostData.ResourceManager?.GetResource(FoodIdentifier) is { } foodResource)
            {
                if (foodResource.Amount <= 0)
                {
                    yield return (AlertCategory.Resource, TextManager.Get("outpostoverview.foodzero"));
                }

                var (foodConsumption, foodProduction) = outpostUi.GetResourceGenerationForResource(foodResource);
                if (foodConsumption > foodProduction)
                {
                    yield return (AlertCategory.Resource, TextManager.Get("outpostoverview.foodconsumptionexceedsproduction"));
                }
            }

            if (outpostData.ResourceManager?.GetResource(ResearchIdentifier) is { } rpResource)
            {
                var (_, researchProduction) = outpostUi.GetResourceGenerationForResource(rpResource);
                if (researchProduction is 0)
                {
                    yield return (AlertCategory.Research, TextManager.Get("outpostoverview.noresearchpointgeneration"));
                }
            }

            if (outpostData.ResourceManager?.GetResource(BunksIdentifier) is { } bunksResource && outpostData.ResourceManager?.GetResource(PopulationIdentifier) is { } populationResource)
            {
                if (MathUtils.NearlyEqual(populationResource.Amount, bunksResource.Amount))
                {
                    yield return (AlertCategory.Dwellers, TextManager.Get("outpostoverview.bunksfull"));
                }

                if (populationResource.Amount > bunksResource.Amount)
                {
                    yield return (AlertCategory.Dwellers, TextManager.Get("outpostoverview.exceededbunks"));
                }
            }

            float oxygenDispensers = outpostData.ResourceManager?.GetResource(OxygenDispenserIdentifier)?.Amount ?? 0,
                  oxygenGenerators = outpostData.ResourceManager?.GetResource(OxygenIdentifier)?.Amount ?? 0,
                  oxygenPlants = outpostData.ResourceManager?.GetResource(OxygenPlantIdentifier)?.Amount ?? 0;
            if (oxygenGenerators < 1 && oxygenPlants < 1)
            {
                if (oxygenDispensers < 1)
                {
                    yield return (AlertCategory.Dwellers, TextManager.Get("outpostoverview.nooxygensource"));
                }
                else
                {
                    yield return (AlertCategory.Dwellers, TextManager.Get("outpostoverview.onlyoxygendispensers"));
                }
            }

            foreach (ResourceGenerator generator in outpostData.ResourceGenerators.Where(static rg => rg.IsWorkstation && rg.UniqueId != 0))
            {
                int assignedCount = outpostData.DwellerManager.DwellerInfos.Count(d => d.AssignedWorkstationId == generator.UniqueId);
                if (assignedCount <= 0)
                {
                    LocalizedString itemName = ItemPrefab.Prefabs.TryGet(generator.ItemPrefabIdentifier, out ItemPrefab? prefab) ? prefab.Name : generator.ItemPrefabIdentifier.Value;
                    yield return (AlertCategory.Dwellers, TextManager.GetWithVariable("outpostoverview.unassignedworkstation", "[workstation]", $"‖color:gui.textcolorbright‖{itemName}‖end‖"));
                }
            }

            if (CreatureResourceGeneratorComponent.DoesRanchingFoodConsumptionExceedProduction())
            {
                yield return (AlertCategory.Dwellers, TextManager.Get("outpostoverview.ranchingfoodconsumptionexceedsproduction"));
            }

            if (ResearchManager.TryGetResearchManagerForCampaign(GameMain.GameSession.Campaign, out ResearchManager? rm))
            {
                //only consider technologies that are part of a technology tree (others, like ones unlocked via events, can't be selected as the active research)
                if (rm.ActiveResearch is null &&
                    TechnologyPrefab.Prefabs.Any(p => TechnologyTreePrefab.Prefabs.Any(tree => tree.Belongs(p)) && rm.CurrentResearches.All(cr => cr.Prefab != p)))
                {
                    yield return (AlertCategory.Research, TextManager.Get("outpostoverview.noresearchselected"));
                }
            }
        }

        public void Refresh()
        {
            RefreshContent();
        }

        private void CreateUI(GUIComponent parent)
        {
            parent.ClearChildren();
            originalParent = parent;

            Location[] ownedLocations = PersistentOutpostData.GetOwnedLocations().ToArray();

            GUILayoutGroup outerLayout = new GUILayoutGroup(new RectTransform(Vector2.One, parent.RectTransform))
            {
                Stretch = true
            };

            if (ownedLocations.Length > 1)
            {
                CreateOutpostSelector(outerLayout, ownedLocations);
            }

            contentContainer = new GUILayoutGroup(new RectTransform(new Vector2(1f, ownedLocations.Length > 1 ? 0.94f : 1f), outerLayout.RectTransform), isHorizontal: true)
            {
                AbsoluteSpacing = PanelSpacing,
                Stretch = true
            };

            RefreshContent();
        }

        /// <summary>
        /// Lets the player pick which of their owned outposts to view the overview of, since this tab (unlike Building/Resources/Dwellers)
        /// works even when the player isn't physically at the outpost.
        /// </summary>
        private void CreateOutpostSelector(GUIComponent parent, IReadOnlyList<Location> ownedLocations)
        {
            GUILayoutGroup selectorLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.06f), parent.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                Stretch = false,
                RelativeSpacing = 0.01f
            };

            new GUITextBlock(new RectTransform(new Vector2(0.2f, 1f), selectorLayout.RectTransform), TextManager.Get("outpostoverview.selectoutpost"), font: GUIStyle.SubHeadingFont);

            GUIDropDown outpostDropDown = new GUIDropDown(new RectTransform(new Vector2(0.3f, 1f), selectorLayout.RectTransform), text: selectedLocation.DisplayName, elementCount: Math.Min(ownedLocations.Count, 8))
            {
                //make sure the names displayed in the dropdown match the names of the locations (= that renaming is reflected in the dropdown)
                OnAddedToGUIUpdateList = component =>
                {
                    if (component is not GUIDropDown dropDown) { return; }
                    foreach (GUIComponent child in dropDown.ListBox.Content.Children)
                    {
                        if (child is GUITextBlock textBlock && child.UserData is Location location && textBlock.Text != location.DisplayName.Value)
                        {
                            textBlock.Text = location.DisplayName;
                        }
                    }
                    if (dropDown.Text != selectedLocation.DisplayName)
                    {
                        dropDown.Text = selectedLocation.DisplayName;
                    }
                }
            };
            foreach (Location ownedLocation in ownedLocations)
            {
                outpostDropDown.AddItem(ownedLocation.DisplayName, ownedLocation);
            }
            outpostDropDown.SelectItem(selectedLocation);

            outpostDropDown.AfterSelected = (_, userData) =>
            {
                if (userData is not Location newLocation || newLocation == selectedLocation) { return true; }

                outpostData.DwellerManager.OnDwellerWorkstationChanged.Deregister(DwellerEventIdentifier);
                selectedLocation = newLocation;
                outpostData = PersistentOutpostData.GetPersistentOutpostData(newLocation);
                SubscribeToDwellerEvents(outpostData.DwellerManager);
                RefreshContent();
                OutpostManagementUI.Instance?.UpdateTabLocks();
                return true;
            };
        }

        private void RefreshContent()
        {
            if (contentContainer is null) { return; }
            contentContainer.ClearChildren();

            var alerts = GetAlerts()
                         .OrderBy(static t => t.Message)
                         .ToArray();

            if (outpostData.ResourceManager is null) { return; }

            // Create the layout cells for all the panels first, and recalculate the layouts before creating the panels,
            // because CreateFrame uses the sizes of the cells (which are only final after stretching to fit the spacing)
            GUILayoutGroup leftLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.75f, 1f), contentContainer.RectTransform))
            {
                AbsoluteSpacing = PanelSpacing,
                Stretch = true
            };
            GUILayoutGroup alertsLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.33f), leftLayout.RectTransform));
            GUILayoutGroup middleLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.33f), leftLayout.RectTransform), isHorizontal: true)
            {
                AbsoluteSpacing = PanelSpacing,
                Stretch = true
            };
            GUILayoutGroup resourcesLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1f), middleLayout.RectTransform));
            GUILayoutGroup modulesLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1f), middleLayout.RectTransform));
            GUILayoutGroup bottomLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.34f), leftLayout.RectTransform), isHorizontal: true)
            {
                AbsoluteSpacing = PanelSpacing,
                Stretch = true
            };
            GUILayoutGroup dwellersLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1f), bottomLayout.RectTransform));
            GUILayoutGroup researchLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1f), bottomLayout.RectTransform));
            GUILayoutGroup rightLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.25f, 1f), contentContainer.RectTransform));
            contentContainer.ForceLayoutRecalculation();

            GUIFrame alertsFrame = CreateFrame(alertsLayout, TextManager.Get("outpostoverview.alerts"), "AlertsBackground");

            GUILayoutGroup alertsInnerLayout = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.97f), alertsFrame.RectTransform, Anchor.BottomCenter));
            GUIListBox alertsList = new GUIListBox(new RectTransform(Vector2.One, alertsInnerLayout.RectTransform), style: null)
            {
                Spacing = GUI.IntScale(8),
                AutoHideScrollBar = false,
                ScrollBarVisible = true
            };


            GUIFrame resourceFrame = CreateFrame(resourcesLayout, TextManager.Get("outpostoverview.resources"), "ResourcesBackground");

            Resource[] resources = ResourcesToShowInResourceTab.Select(static s => s.ToIdentifier())
                                   .Select(outpostData.ResourceManager.GetResource)
                                   .NotNull()
                                   .ToArray();

            GUILayoutGroup resourceIndicatorLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.99f, 0.8f), resourceFrame.RectTransform, Anchor.Center), isHorizontal: true)
            {
                RelativeSpacing = 0.05f,
                Stretch = true
            };

            foreach (Resource resource in resources)
            {
                var (consumption, production) = outpostUi.GetResourceGenerationForResource(resource);
                GUIFrame indicatorFrame = new GUIFrame(new RectTransform(new Vector2(0.25f, 1f), resourceIndicatorLayout.RectTransform))
                {
                    ToolTip = GetTooltip(resource)
                };

                GUILayoutGroup indicatorFrameLayout = new GUILayoutGroup(new RectTransform(Vector2.One, indicatorFrame.RectTransform));
                GUILayoutGroup iconLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.66f), indicatorFrameLayout.RectTransform), childAnchor: Anchor.Center);
                new GUIImage(new RectTransform(new Vector2(1f, 0.5f), iconLayout.RectTransform), resource.Prefab.Sprite, scaleToFit: true)
                {
                    Color = GUIStyle.TextColorNormal,
                    CanBeFocused = false
                };
                GUITextBlock amountBlock = new GUITextBlock(new RectTransform(new Vector2(1f, 0.33f), indicatorFrameLayout.RectTransform), $"{resource.DisplayedAmountInt}", textAlignment: Alignment.Center, font: GUIStyle.LargeFont)
                {
                    CanBeFocused = false,
                    TextGetter = () => $"{resource.DisplayedAmountInt}"
                };

                string str = $"‖color:{(production > 0 ? "gui.green" : "#808080")}‖‖icon:up‖‖end‖{EconomyLogEntry.RoundAmount(production)} ‖color:{(consumption > 0 ? "gui.red" : "#808080")}‖‖icon:down‖‖end‖ {EconomyLogEntry.RoundAmount(consumption)}";

                GUIFrame generatingBlock = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.25f), amountBlock.RectTransform, anchor: Anchor.TopCenter, Pivot.BottomCenter), style: null);
                OutpostResourcesUI.CreateResourceIncreaseDecreaseIndicators(outpostData, resource, generatingBlock, textAlignment: Alignment.Center);
            }

            GUIFrame modulesFrame = CreateFrame(modulesLayout, TextManager.Get("outpostoverview.modules"), "ModulesBackground");
            GUILayoutGroup modulesInnerLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.99f, 0.97f), modulesFrame.RectTransform, Anchor.BottomCenter))
            {
                RelativeSpacing = 0.05f,
                Stretch = true
            };

            new GUIButton(new RectTransform(new Vector2(1f, 0.3f), modulesInnerLayout.RectTransform), text: TextManager.Get("outpostoverview.managemodules"), style: "OutpostManagementBlankButton")
            {
                OnClicked = (button, o) =>
                {
                    outpostUi.SelectTab(OutpostManagementUI.Tab.Building);
                    return true;
                },
                OnAddedToGUIUpdateList = component =>
                {
                    component.Enabled = !outpostUi.IsTabLocked(OutpostManagementUI.Tab.Building);
                }
            };
            LocalizedString buildStr = TextManager.Get("outpostoverview.nomodulebuilding");
            StringBuilder moduleListBuilder = new();
            bool hasModulesBuilding = false;
            foreach (var pendingModule in outpostData.PendingOutpostModules)
            {
                moduleListBuilder.Append(TextManager.GetWithVariables("outpostoverview.modulebuildformat", ("[module]", pendingModule.ModulePrefab?.DisplayName ?? "ERROR!"), ("[steps]", $"{pendingModule.TimeStepsRemainingToBuild}")));
                moduleListBuilder.Append(", ");
                hasModulesBuilding = true;
            }

            if (hasModulesBuilding)
            {
                LocalizedString moduleListText = moduleListBuilder
                                                 .ToString()
                                                 .TrimEnd(' ')
                                                 .TrimEnd(',');
                buildStr = TextManager.GetWithVariable("outpostoverview.modulebuilding", "[modules]", moduleListText);
            }

            new GUITextBlock(new RectTransform(new Vector2(1f, 0.7f), modulesInnerLayout.RectTransform), text: RichString.Rich(buildStr), textAlignment: Alignment.TopLeft, font: GUIStyle.IconFont, wrap: true)
            {
                ForceUpperCase = ForceUpperCase.No
            };

            GUIFrame dwellerFrame = CreateFrame(dwellersLayout, TextManager.Get("outpostoverview.dwellers"), "DwellersBackground");

            GUILayoutGroup dwellersInnerLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.99f, 0.95f), dwellerFrame.RectTransform, Anchor.BottomCenter))
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            GUILayoutGroup upperDwellersLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.3f), dwellersInnerLayout.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };

            GUIListBox dwellerAlertsList = new GUIListBox(new RectTransform(new Vector2(1f, 0.7f), dwellersInnerLayout.RectTransform), style: null)
            {
                Spacing = GUI.IntScale(8),
                AutoHideScrollBar = false,
                ScrollBarVisible = true
            };

            foreach (Resource resource in ResourcesToShowInDwellersTab.Select(static s => s.ToIdentifier())
                                                                      .Select(outpostData.ResourceManager.GetResource)
                                                                      .NotNull())
            {
                GUIFrame indicatorFrame = new GUIFrame(new RectTransform(new Vector2(0.25f, 1f), upperDwellersLayout.RectTransform))
                {
                    ToolTip = GetTooltip(resource)
                };

                GUILayoutGroup indicatorFrameLayout = new GUILayoutGroup(new RectTransform(Vector2.One, indicatorFrame.RectTransform), isHorizontal: true) { Stretch = true };
                GUILayoutGroup iconLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1f), indicatorFrameLayout.RectTransform), childAnchor: Anchor.Center);
                new GUIImage(new RectTransform(new Vector2(1f, 0.66f), iconLayout.RectTransform, scaleBasis: ScaleBasis.Smallest), resource.Prefab.Sprite, scaleToFit: true)
                {
                    Color = GUIStyle.TextColorNormal,
                    CanBeFocused = false
                };
                new GUITextBlock(new RectTransform(new Vector2(0.5f, 1f), indicatorFrameLayout.RectTransform), $"{resource.DisplayedAmountInt}", textAlignment: Alignment.Center, font: GUIStyle.LargeFont)
                {
                    CanBeFocused = false,
                    TextGetter = () => $"{resource.DisplayedAmountInt}"
                };
            }

            new GUIButton(new RectTransform(new Vector2(0.5f, 1f), upperDwellersLayout.RectTransform), text: TextManager.Get("outpostoverview.managedwellers"), style: "OutpostManagementBlankButton")
            {
                OnClicked = (button, o) =>
                {
                    outpostUi.SelectTab(OutpostManagementUI.Tab.Dwellers);
                    return true;
                },
                OnAddedToGUIUpdateList = component =>
                {
                    component.Enabled = !outpostUi.IsTabLocked(OutpostManagementUI.Tab.Dwellers);
                }
            };

            GUIFrame researchFrame = CreateFrame(researchLayout, TextManager.Get("outpostoverview.research"), "ResearchBackground");

            GUILayoutGroup researchInnerLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.99f, 0.95f), researchFrame.RectTransform, Anchor.BottomCenter))
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };
            GUILayoutGroup upperResearchLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.3f), researchInnerLayout.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };

            new GUIButton(new RectTransform(new Vector2(0.5f, 1f), upperResearchLayout.RectTransform), text: TextManager.Get("outpostoverview.manageresearch"), style: "OutpostManagementBlankButton")
            {
                OnClicked = (button, o) =>
                {
                    outpostUi.SelectTab(OutpostManagementUI.Tab.Research);
                    return true;
                },
                OnAddedToGUIUpdateList = component =>
                {
                    component.Enabled = !outpostUi.IsTabLocked(OutpostManagementUI.Tab.Research);
                }
            };

            GUILayoutGroup lowerResearchLayout  = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.7f), researchInnerLayout.RectTransform))
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };

            ResearchManager.TryGetResearchManagerForCampaign(GameMain.GameSession?.Campaign, out ResearchManager? researchManager);
            new GUITextBlock(new RectTransform(new Vector2(1f, 0.2f), lowerResearchLayout.RectTransform), text: RichString.Rich(TextManager.GetWithVariable("outpostoverview.activeresearch", "[research]", researchManager?.ActiveResearch is { } active ? $"‖color:gui.textcolorbright‖{active.Prefab.Name}‖end‖" : $"‖color:gui.red‖{TextManager.Get("none")}‖end‖")), font: GUIStyle.SubHeadingFont)
            {
                Shadow = true,
                ForceUpperCase = ForceUpperCase.No
            };
            GUIListBox researchAlertsList = new GUIListBox(new RectTransform(new Vector2(1f, 0.8f), lowerResearchLayout.RectTransform), style: null)
            {
                Spacing = GUI.IntScale(8),
                AutoHideScrollBar = false,
                ScrollBarVisible = true
            };

            GUIFrame activeEffectFrame = CreateFrame(rightLayout, TextManager.Get("outpostoverview.happenings"), OutpostResourceLogUI.BackgroundStyle);

            GUILayoutGroup happeningsLayout = new GUILayoutGroup(new RectTransform(Vector2.One, activeEffectFrame.RectTransform), childAnchor: Anchor.TopCenter);
            GUIListBox resourceStatusIndicatorList = new GUIListBox(new RectTransform(Vector2.One, happeningsLayout.RectTransform), style: null)
            {
                Spacing = GUI.IntScale(16),
                HoverCursor = CursorState.Default,
                AutoHideScrollBar = false,
                ScrollBarVisible = true
            };

            new GUIFrame(new RectTransform(Vector2.Zero, resourceStatusIndicatorList.Content.RectTransform), style: null);
            bool hasEffect = false;
            foreach (ResourceStatusIndicator indicator in outpostData.ResourceManager.GetActiveResourceStatusIndicators())
            {
                if (indicator.Hidden) { continue; }
                hasEffect = true;
                GUIFrame paddingFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.15f), resourceStatusIndicatorList.Content.RectTransform), style: null);
                GUIFrame indicatorFrame = new GUIFrame(new RectTransform(new Vector2(0.95f, 1f), paddingFrame.RectTransform, Anchor.Center));
                GUILayoutGroup effectIndicatorLayout = new GUILayoutGroup(new RectTransform(Vector2.One, indicatorFrame.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft);
                GUIFrame imageContainer = new GUIFrame(new RectTransform(new Vector2(0.33f, 1f), effectIndicatorLayout.RectTransform), style: null);
                new GUIImage(new RectTransform(new Vector2(0.8f), imageContainer.RectTransform, Anchor.Center), indicator.Icon, scaleToFit: true)
                {
                    Color = indicator.IconColor,
                    CanBeFocused = false
                };
                GUILayoutGroup effectTextLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.66f, 0.8f), effectIndicatorLayout.RectTransform));
                new GUITextBlock(new RectTransform(new Vector2(1f, 0.2f), effectTextLayout.RectTransform), indicator.Name, font: GUIStyle.SubHeadingFont)
                {
                    AutoScaleHorizontal = true
                };
                GUITextBlock descBlock = new GUITextBlock(new RectTransform(new Vector2(1f, 0.8f), effectTextLayout.RectTransform), text: indicator.Description, wrap: true, textAlignment: Alignment.TopLeft, font: GUIStyle.SmallFont);
                ToolBox.LimitStringHeight(descBlock, descBlock.Rect.Height, setTooltip: true);
            }

            if (hasEffect)
            {
                new GUIFrame(new RectTransform(new Vector2(1f, 0f), resourceStatusIndicatorList.Content.RectTransform), style: "HorizontalLine");
            }

            originalParent?.ForceLayoutRecalculation();
            resourceStatusIndicatorList.RecalculateChildren();

            foreach (var (steps, logs) in outpostData.Logs)
            {
                int stepsAgo = (GameMain.GameSession?.Map?.WorldStepCount ?? 0) - steps;
                for (int i = logs.Count - 1; i >= 0; i--)
                {
                    OutpostResourceLogUI.CreateLogEntry(logs[i], stepsAgo, resourceStatusIndicatorList.Content.RectTransform);
                }
            }

            foreach (var (category, alert) in alerts)
            {
                switch (category)
                {
                    case AlertCategory.Research:
                        CreateAlertBlock(alert, researchAlertsList);
                        break;
                    case AlertCategory.Dwellers:
                        CreateAlertBlock(alert, dwellerAlertsList);
                        break;
                }

                CreateAlertBlock(alert, alertsList);
            }

            return;

            static void CreateAlertBlock(LocalizedString alert, GUIListBox parent)
            {
                GUITextBlock block = new GUITextBlock(new RectTransform(new Vector2(1f, 0f), parent.Content.RectTransform), RichString.Rich(alert), textAlignment: Alignment.CenterLeft, wrap: true, font: GUIStyle.IconFont)
                {
                    ForceUpperCase = ForceUpperCase.No,
                    CanBeFocused = false,
                    Shadow = true,
                };
                // Something in GUIListBox seems to be messing with the relative size of this text block, fix by forcing the minsize
                block.RectTransform.MinSize = block.Font.MeasureString(block.WrappedText).ToPoint();
            }

            static GUIFrame CreateFrame(GUILayoutGroup parent, LocalizedString headerText, string backgroundStyle)
            {
                float headerSize = GUI.IntScale(48);
                float mainSize = GUI.IntScale(16);

                Color frameColor = new Color(25, 35, 35);

                // Same style as the workstation panels in the dweller UI
                Color panelForeground = frameColor;
                Color panelBackground = frameColor * 0.33f;
                int outlineThickness = Math.Max(GUI.IntScale(1), 1);

                // Fill the whole layout cell, the gaps between the panels come from the layout groups' spacing
                GUIFrame frame = new GUIFrame(new RectTransform(Vector2.One, parent.RectTransform),
                    style: null)
                {
                    CanBeFocused = false,
                    Color = panelBackground,
                    OutlineColor = Color.Lerp(panelBackground, Color.White, 0.1f),
                    OutlineThickness = outlineThickness
                };
                float headerHeight = headerSize / frame.Rect.Height;
                GUIFrame headerFrame = new GUIFrame(new RectTransform(new Vector2(1f, headerHeight), frame.RectTransform),
                    style: null)
                {
                    CanBeFocused = false,
                    Color = panelForeground,
                    OutlineColor = Color.Lerp(panelForeground, Color.White, 0.1f),
                    OutlineThickness = outlineThickness
                };

                new GUITextBlock(new RectTransform(Vector2.One, headerFrame.RectTransform),
                    headerText,
                    textColor: Color.White,
                    textAlignment: Alignment.Center,
                    font: GUIStyle.SubHeadingFont)
                {
                    CanBeFocused = false
                };

                // Inset the background image so it doesn't cover the outline
                Point bodySize = new Point(frame.Rect.Width - outlineThickness * 2, frame.Rect.Height - headerFrame.Rect.Height - outlineThickness);
                GUICustomComponent custom = OutpostResourceLogUI.CreateBackgroundImage(new RectTransform(bodySize, frame.RectTransform, Anchor.BottomCenter) { AbsoluteOffset = new Point(0, outlineThickness) }, backgroundStyle);

                return new GUIFrame(new RectTransform(new Vector2(1f - mainSize * 2f / custom.Rect.Width, 1f - mainSize / custom.Rect.Height), custom.RectTransform, anchor: Anchor.TopCenter), style: null);
            }
        }

        private static RichString GetTooltip(Resource resource)
            => RichString.Rich($"‖color:gui.textcolorbright‖{resource.DisplayName}‖end‖\n{resource.Prefab.Description}");
    }
}
