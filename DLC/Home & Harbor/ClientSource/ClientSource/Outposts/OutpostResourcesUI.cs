using Barotrauma;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using HomeAndHarbor.Resources;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using static HomeAndHarbor.ResourceGenerator;
using ConversionRecipeHash = uint;

namespace HomeAndHarbor;

internal class OutpostResourcesUI
{
    private static readonly Identifier WealthResourceIdentifier = "wealth".ToIdentifier();

    private readonly PersistentOutpostData outpostData;
    private readonly Submarine outpost;

    private float uiUpdateTimer = 0f;
    private const float UIUpdateInterval = 10.0f;
    private GUIListBox itemList;

    private readonly ImmutableArray<Resource> trackedItemResources;

    private readonly Dictionary<Identifier, bool> visibleCategories = [];

    private readonly Dictionary<ItemPrefab, Resource?> cachedItemCategories = [];

    private readonly Dictionary<Resource, GUITextBlock> resourceAmountTexts = [];

    private GUIFrame? workstationFrame;

    /// <summary>
    /// UI element related to item conversion
    /// </summary>
    /// <param name="TickBox"></param>
    /// <param name="InputText"></param>
    /// <param name="OutputText"></param>
    /// <param name="OwnedText"></param>
    private readonly record struct WorkstationConversionUIElements(
        ConvertedItem Conversion,
        GUITickBox TickBox,
        GUITextBlock InputText,
        GUITextBlock OutputText,
        GUITextBlock OwnedText)
    {
        public void Toggle(bool state)
        {
            TickBox.SetSelected(state, callOnSelected: false);
            InputText.Enabled = state;
            OutputText.Enabled = state;
            OwnedText.Enabled = state;
        }
    }

    /// <summary>
    /// Used to keep track of the workstations currently opened on the interface with their respective
    /// conversion UI elements. Used in multiplayer to sync the recipes in the UI since this is the
    /// only interactive part of the entire UI.
    /// </summary>
    private readonly Dictionary<ResourceGeneratorBaseComponent, List<WorkstationConversionUIElements>> currentlyOpenedWorkstationConversions = [];

    private readonly Dictionary<ResourceGeneratorBaseComponent, GUIFrame> currentlyOpenedWorkstationFrame = [];

    public OutpostResourcesUI(PersistentOutpostData outpostData, Submarine outpost, GUIComponent parent)
    {
        this.outpostData = outpostData;
        this.outpost = outpost;

        var rightResourceBuilder = ImmutableArray.CreateBuilder<Resource>();

        if (outpostData.ResourceManager is ResourceManager resourceManager)
        {
            foreach (Resource resource in resourceManager.Resources.OrderBy(r => r.Prefab.MenuOrder))
            {
                if (!resource.Prefab.ShowItemsInResourcesUI) { continue; }
                rightResourceBuilder.Add(resource);
            }
        }

        trackedItemResources = rightResourceBuilder.ToImmutable();
        CreateUI(parent);

        outpostData.DwellerManager.OnDwellerWorkstationChanged.RegisterOverwriteExisting(nameof(OutpostResourcesUI).ToIdentifier(), e =>
        {
            if (workstationFrame is not { Visible: true }) { return; }

            if (IsWorkstationOpen(e.UnassignedWorkstation, out GUIFrame? unassignedFrame))
            {
                CreateWorkstationFrame(e.UnassignedWorkstation, unassignedFrame);
            }

            if (IsWorkstationOpen(e.AssignedWorkstation, out GUIFrame? assignedFrame))
            {
                CreateWorkstationFrame(e.AssignedWorkstation, assignedFrame);
            }
            return;

            bool IsWorkstationOpen(WorkstationComponent? workstation, [NotNullWhen(true)] out GUIFrame? frame)
            {
                frame = null;
                if (workstation is null) { return false; }
                return currentlyOpenedWorkstationFrame.TryGetValue(workstation, out frame);
            }
        });
    }

    [MemberNotNull(nameof(itemList))]
    private void CreateUI(GUIComponent parent)
    {
        GUILayoutGroup left = new(new RectTransform(new Vector2(0.5f, 1f), parent.RectTransform, Anchor.TopLeft))
        {
            Stretch = true,
            RelativeSpacing = 0.01f
        };
        new GUIFrame(new RectTransform(Vector2.Zero, left.RectTransform), style: null);
        new GUITextBlock(new RectTransform(Vector2.UnitX, left.RectTransform), TextManager.Get("Summary"), font: GUIStyle.SubHeadingFont, textAlignment: Alignment.CenterLeft);

        GUILayoutGroup leftHorizontalLayout = new GUILayoutGroup(new RectTransform(Vector2.One, left.RectTransform), isHorizontal: true)
        {
            AbsoluteSpacing = GUI.IntScale(8)
        };
        GUIListBox summaryList = new GUIListBox(new RectTransform(new Vector2(0.4f, 1f), leftHorizontalLayout.RectTransform), style: null)
        {
            Spacing = GUI.IntScale(8)
        };

        resourceAmountTexts.Clear();
        foreach (var (trackedResource, displayedResource) in outpostData.ResourcesToDisplayInUIs)
        {
            Resource resourceToLogAs = trackedResource.GetResourceToLogAs() ?? trackedResource;
            GUIButton resourceFrame = new GUIButton(new RectTransform(new Vector2(1f, 0.1f), summaryList.Content.RectTransform), style: "UpgradeUIFrame")
            {
                UserData = trackedResource,
                ToolTip = displayedResource.Prefab.Description
            };

            // Show uncollected tax revenue in the Wealth tooltip
            if (displayedResource.Identifier == WealthResourceIdentifier)
            {
                resourceFrame.OnAddedToGUIUpdateList += _ =>
                {
                    resourceFrame.ToolTip = RichString.Rich($"{displayedResource.Prefab.Description}\n\n{OutpostManagementUI.GetUncollectedTaxesTooltip(outpostData)}");
                };
            }
            new GUIFrame(new RectTransform(Vector2.One, resourceFrame.RectTransform), style: "InnerGlow")
            {
                CanBeFocused = false,
                Color = Color.Transparent,
                SelectedColor = GUIStyle.Green * 0.8f,
                PressedColor = GUIStyle.Green * 0.8f,
                HoverColor = GUIStyle.Green * 0.66f
            };
            GUILayoutGroup layout = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), resourceFrame.RectTransform, anchor: Anchor.Center), childAnchor: Anchor.CenterLeft, isHorizontal: true);
            new GUIImage(new RectTransform(new Vector2(0.8f), layout.RectTransform, scaleBasis: ScaleBasis.BothHeight), sprite: displayedResource.Prefab.Sprite, scaleToFit: true)
            {
                CanBeFocused = false,
                Color = GUIStyle.TextColorNormal,
                SelectedColor = GUIStyle.TextColorNormal,
                HoverColor = GUIStyle.TextColorNormal,
                PressedColor = GUIStyle.TextColorNormal
            };

            GUILayoutGroup textLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.8f), layout.RectTransform));
            new GUITextBlock(new RectTransform(new Vector2(1f, 0.5f), textLayout.RectTransform), displayedResource.DisplayName, font: GUIStyle.SubHeadingFont, textAlignment: Alignment.CenterLeft) { CanBeFocused = false };

            resourceAmountTexts.Add(displayedResource,
                new GUITextBlock(new RectTransform(new Vector2(1f, 0.5f), textLayout.RectTransform), displayedResource.GetAmountText(withNumber: true), font: GUIStyle.Font, textAlignment: Alignment.CenterLeft) { CanBeFocused = false });

            var changeIndicatorContainer = new GUIFrame(new RectTransform(new Vector2(0.25f, 0.25f), resourceFrame.RectTransform, anchor: Anchor.CenterRight), style: null);
            CreateResourceIncreaseDecreaseIndicators(outpostData, resourceToLogAs, changeIndicatorContainer, textAlignment: Alignment.Right);
        }

        workstationFrame = new GUIFrame(new RectTransform(new Vector2(0.8f, 0.6f), leftHorizontalLayout.RectTransform))
        {
            Visible = false
        };
        GUIListBox workstationList = new GUIListBox(new RectTransform(ToolBox.PaddingSizeParentRelative(workstationFrame.RectTransform, 0.9f), workstationFrame.RectTransform, Anchor.Center), style: "GUIListBoxNoBorder")
        {
            // The list only displays info, the elements in it don't need to be selectable (but they're still focusable for tooltips)
            CurrentSelectMode = GUIListBox.SelectMode.None,
            Spacing = GUI.IntScale(5)
        };

        summaryList.OnSelected = (component, o) =>
        {
            Resource selectedResource = (Resource)o;
            if (workstationFrame.Visible && workstationFrame.UserData == selectedResource)
            {
                workstationFrame.Visible = false;
            }
            else
            {
                workstationFrame.Visible = true;
                workstationFrame.UserData = (Resource)o;
            }
            foreach (GUIComponent child in summaryList.Content.Children)
            {
                if (child != component)
                {
                    child.Selected = false;
                }
            }
            CreateWorkstationList((Resource)o, workstationList);
            return true;
        };
        GUILayoutGroup right = new(new RectTransform(new Vector2(0.3f, 1f), parent.RectTransform, Anchor.TopRight))
        {
            Stretch = true,
            RelativeSpacing = 0.01f
        };
        new GUIFrame(new RectTransform(Vector2.Zero, left.RectTransform), style: null);

        new GUITextBlock(new RectTransform(Vector2.UnitX, right.RectTransform), TextManager.Get("Label.Storage"), font: GUIStyle.SubHeadingFont, textAlignment: Alignment.Center);
        itemList = new GUIListBox(new RectTransform(Vector2.One, right.RectTransform))
        {
            OnSelected = static (_, _) => false,
            Spacing = GUI.IntScale(2.5f)
        };

        foreach (Resource resource in trackedItemResources)
        {
            GUIButton btn = CreateCategory(itemList.Content, resource.DisplayName, resource.Prefab.Sprite);
            btn.UserData = resource;
            btn.OnClicked = (button, _) =>
            {
                ToggleVisibility(resource, button);
                return true;
            };
        }

        CreateCategory(itemList.Content, TextManager.Get("resourcecategory.other"), icon: null)
            .OnClicked = (button, _) =>
            {
                ToggleVisibility(null, button);
                return true;
            };


        return;

        void ToggleVisibility(Resource? resource, GUIButton button)
        {
            Identifier identifier = resource?.Identifier ?? Identifier.Empty;
            bool visible = !visibleCategories.GetValueOrDefault(identifier);
            visibleCategories[identifier] = visible;
            foreach (GUIComponent child in itemList.Content.Children)
            {
                if (child is GUIListBox && child.UserData == resource)
                {
                    child.Visible = visible;
                }
            }

            if (button.FindChild("arrow", recursive: true) is { } arrow)
            {
                arrow.SpriteEffects ^= SpriteEffects.FlipVertically;
            }

            itemList.RecalculateChildren();
        }

        static GUIButton CreateCategory(GUIComponent parent, LocalizedString label, Sprite? icon)
        {
            GUIButton frame = new GUIButton(new RectTransform(new Vector2(1f, 0.05f), parent.RectTransform), style: "GUIFrameCategory");
            GUILayoutGroup layout = new GUILayoutGroup(new RectTransform(new Vector2(0.975f, 1f), frame.RectTransform, anchor: Anchor.Center), isHorizontal: true, childAnchor: Anchor.CenterLeft);
            GUILayoutGroup leftLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.75f, 1f), layout.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft) { Stretch = true };
            GUILayoutGroup rightLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.25f, 1f), layout.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterRight) { Stretch = true };

            if (icon is null)
            {
                new GUITextBlock(new RectTransform(Vector2.One, leftLayout.RectTransform, scaleBasis: ScaleBasis.BothHeight), text: "?", textAlignment: Alignment.Center, font: GUIStyle.LargeFont);
            }
            else
            {
                new GUIImage(new RectTransform(Vector2.One, leftLayout.RectTransform, scaleBasis: ScaleBasis.BothHeight), icon, scaleToFit: true)
                {
                    Color = GUIStyle.TextColorNormal
                };
            }
            new GUITextBlock(new RectTransform(Vector2.One, leftLayout.RectTransform), label, font: GUIStyle.UnscaledSmallFont)
            {
                AutoScaleHorizontal = true
            };
            new GUIImage(new RectTransform(new Vector2(0.5f, 0.5f), rightLayout.RectTransform, scaleBasis: ScaleBasis.BothHeight), "GUIButtonVerticalArrowFreeScale", scaleToFit: true)
            {
                UserData = "arrow"
            };
            new GUITextBlock(new RectTransform(Vector2.One, rightLayout.RectTransform), "", font: GUIStyle.Font, textAlignment: Alignment.CenterRight)
            {
                UserData = "count"
            };
            return frame;
        }
    }

    public static void CreateResourceIncreaseDecreaseIndicators(PersistentOutpostData outpostData, Resource resourceToLogAs, GUIComponent parent, Alignment textAlignment)
    {
        float increasing = 0f,
              decreasing = 0f;
        if (outpostData.Logs.Count > 0)
        {
            foreach (EconomyLogEntry log in outpostData.Logs.Last().Value)
            {
                if (FindAllResources(resourceToLogAs).None(log.MatchesResource)) { continue; }
                float change = log.Change;
                if (change > 0)
                {
                    increasing += MathF.Abs(change);
                }
                else
                {
                    decreasing += MathF.Abs(change);
                }
            }
        }

        string decreasingIncreasingLabel = string.Empty;
        if (MathUtils.NearlyEqual(0f, increasing) && MathUtils.NearlyEqual(0f, decreasing))
        {
            decreasingIncreasingLabel = $"‖color:#808080‖‖icon:up‖‖end‖ ‖color:#808080‖‖icon:down‖‖end‖";
        }
        else
        {
            if (increasing > 0f)
            {
                decreasingIncreasingLabel += $"‖color:gui.green‖‖icon:up‖‖end‖ {MathF.Round(increasing)}";
            }
            if (decreasing > 0f)
            {
                decreasingIncreasingLabel += $" ‖color:gui.red‖‖icon:down‖‖end‖ {MathF.Round(decreasing)}";
            }
        }

        GUIFrame upDownLayout = new GUIFrame(new RectTransform(Vector2.One, parent.RectTransform), style: null)
        {
            CanBeFocused = false
        };
        new GUITextBlock(new RectTransform(Vector2.One, upDownLayout.RectTransform), RichString.Rich(decreasingIncreasingLabel), font: GUIStyle.IconFont, textAlignment: textAlignment)
        {
            ToolTip = TextManager.Get("outpostbuilderui.updowntooltip")
        };
    }

    private void CreateWorkstationList(Resource resource, GUIListBox listBox)
    {
        currentlyOpenedWorkstationConversions.Clear();
        currentlyOpenedWorkstationFrame.Clear();

        RectTransform listContent = listBox.Content.RectTransform;
        listContent.ClearChildren();

        #region Workstations

        GUIButton workstationButton = new GUIButton(new RectTransform(new Vector2(1f, 0.1f), listContent), style: "GUIFrameCategory");

        new GUITextBlock(new RectTransform(Vector2.One, workstationButton.RectTransform), text: TextManager.Get("outpostbuilderui.workstations"), textAlignment: Alignment.Center, font: GUIStyle.SubHeadingFont);
        GUIImage workstationArrow = new GUIImage(new RectTransform(new Vector2(0.66f), workstationButton.RectTransform, scaleBasis: ScaleBasis.BothHeight, anchor: Anchor.CenterRight)
        {
            RelativeOffset = new Vector2(0.01f, 0f)
        }, "GUIButtonVerticalArrowFreeScale", scaleToFit: true);

        workstationButton.OnClicked = (button, o) =>
        {
            foreach (RectTransform child in listContent.Children)
            {
                if (child.GUIComponent.UserData is WorkstationComponent)
                {
                    child.GUIComponent.Visible = !child.GUIComponent.Visible;
                }
            }

            workstationArrow.SpriteEffects ^= SpriteEffects.FlipVertically;
            listBox.RecalculateChildren();
            return true;
        };

        ImmutableArray<Resource> resources = [..FindAllResources(resource)];
        var availableWorkstations = WorkstationComponent.WorkstationComponents
                                                        .Where(w => w.Item?.Submarine == outpost)
                                                        .Where(ProducesResource)
                                                        .ToList();

        //no point in showing the category if no workstation affects this resource
        workstationButton.Visible = availableWorkstations.Count > 0;
        foreach (WorkstationComponent component in availableWorkstations)
        {
            var topSpacing = new GUIFrame(new RectTransform(new Vector2(1f, 0.025f), listContent), style: null)
            {
                Visible = false
            };
            GUIFrame frame = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.2f), listContent), style: null)
            {
                Visible = false,
                UserData = component
            };

            currentlyOpenedWorkstationFrame[component] = frame;
            CreateWorkstationFrame(component, frame);
            if (component != availableWorkstations.Last())
            {
                //divider + spacing between components
                new GUIFrame(new RectTransform(new Vector2(1f, 0.025f), listContent), style: null)
                {
                    Visible = false,
                    UserData = component
                };
                new GUIFrame(new RectTransform(new Vector2(1f, 0.0f), listContent), style: "HorizontalLine")
                {
                    Visible = false,
                    UserData = component
                };
            }
            new GUIFrame(new RectTransform(new Vector2(1f, 0.025f), listContent), style: null)
            {
                Visible = false
            };
            topSpacing.UserData = component;
        }

        #endregion

        #region Other Resources

        GUIButton otherButton = new GUIButton(new RectTransform(new Vector2(1f, 0.1f), listContent), style: "GUIFrameCategory");

        new GUITextBlock(new RectTransform(Vector2.One, otherButton.RectTransform), text: TextManager.Get("outpostbuilderui.othergenerators"), textAlignment: Alignment.Center, font: GUIStyle.SubHeadingFont);
        GUIImage otherArrow = new GUIImage(new RectTransform(new Vector2(0.66f), otherButton.RectTransform, scaleBasis: ScaleBasis.BothHeight, anchor: Anchor.CenterRight)
        {
            RelativeOffset = new Vector2(0.01f, 0f)
        }, "GUIButtonVerticalArrowFreeScale", scaleToFit: true);

        otherButton.OnClicked = (button, o) =>
        {
            foreach (RectTransform child in listContent.Children)
            {
                if (child.GUIComponent.UserData is ResourceGeneratorBaseComponent and not WorkstationComponent)
                {
                    child.GUIComponent.Visible = !child.GUIComponent.Visible;
                }
            }

            otherArrow.SpriteEffects ^= SpriteEffects.FlipVertically;
            listBox.RecalculateChildren();
            return true;
        };

        var otherGenerators = outpost
            .GetItems(alsoFromConnectedSubs: true)
            .Select(x => x.GetComponent<ResourceGeneratorBaseComponent>())
            .NotNull()
            .Where(x =>
                x is not WorkstationComponent &&
                x.ResourceGenerator.IsActive &&
                x.ResourceGenerator.ProducedResourcesList.Any(pr => CanDisplayProducedResource(outpostData.ResourceManager, pr)))
            .Where(ProducesResource)
            .ToList();

        otherButton.Visible = otherGenerators.Count > 0;

        foreach (ResourceGeneratorBaseComponent generator in otherGenerators)
        {
            GUIFrame frame = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.15f), listContent), style: null)
            {
                Visible = false,
                UserData = generator
            };

            CreateOtherResourceFrame(generator, frame);
        }

        #endregion

        #region History

        GUIButton historyButton = new GUIButton(new RectTransform(new Vector2(1f, 0.1f), listContent), style: "GUIFrameCategory");

        //happiness is mostly shaped by temporary modifiers rather than a plain log of changes
        string historyTextTag = resource.Identifier == "happiness" ? "outpostbuilderui.history.happiness" : "outpostbuilderui.history";
        new GUITextBlock(new RectTransform(Vector2.One, historyButton.RectTransform), text: TextManager.Get(historyTextTag), textAlignment: Alignment.Center, font: GUIStyle.SubHeadingFont);
        GUIImage historyArrow = new GUIImage(new RectTransform(new Vector2(0.66f), historyButton.RectTransform, scaleBasis: ScaleBasis.BothHeight, anchor: Anchor.CenterRight)
        {
            RelativeOffset = new Vector2(0.01f, 0f)
        }, "GUIButtonVerticalArrowFreeScale", scaleToFit: true);

        // All the history entries go in one container, so the background image can be drawn behind them
        int historySpacing = GUI.IntScale(16);
        GUILayoutGroup historyContainer = new GUILayoutGroup(new RectTransform(new Point(listContent.Rect.Width, Math.Max(listBox.Rect.Height, 1)), listContent))
        {
            AbsoluteSpacing = historySpacing,
            Visible = false
        };
        // Top padding
        new GUIFrame(new RectTransform(Vector2.Zero, historyContainer.RectTransform), style: null);

        historyButton.OnClicked = (button, o) =>
        {
            historyContainer.Visible = !historyContainer.Visible;
            historyArrow.SpriteEffects ^= SpriteEffects.FlipVertically;
            listBox.RecalculateChildren();
            return true;
        };

        bool hadHistory = false;
        Resource resourceToLogAs = resource.GetResourceToLogAs() ?? resource;

        if (resourceToLogAs is VariableResource variableResource)
        {
            foreach (var log in variableResource.LogEntriesFromActiveSetters)
            {
                hadHistory = true;
                // Active setters are ongoing rather than from a specific world step, so no "x steps ago" text
                OutpostResourceLogUI.CreateLogEntry(log, stepsAgo: null, historyContainer.RectTransform);
            }
        }

        foreach (var (steps, logs) in outpostData.Logs)
        {
            int stepsAgo = (GameMain.GameSession?.Map?.WorldStepCount ?? 0) - steps;
            for (int i = logs.Count - 1; i >= 0; i--)
            {
                EconomyLogEntry log = logs[i];
                if (!log.MatchesResource(resourceToLogAs)) 
                { 
                    if (FindAllResources(resource).None(log.MatchesResource))
                    {
                        continue;
                    }
                }

                hadHistory = true;
                OutpostResourceLogUI.CreateLogEntry(log, stepsAgo, historyContainer.RectTransform);
            }
        }

        // Bottom padding
        new GUIFrame(new RectTransform(Vector2.Zero, historyContainer.RectTransform), style: null);

        // Shrink the container to fit the entries, then add the background behind them
        int historyHeight = historyContainer.Children.Sum(static c => c.Rect.Height) + historySpacing * Math.Max(historyContainer.CountChildren - 1, 0);
        historyContainer.RectTransform.Resize(new Point(historyContainer.Rect.Width, historyHeight), resizeChildren: false);
        // Extend the background to the bottom of the list box if there's not enough entries to fill it
        GUICustomComponent historyBackground = OutpostResourceLogUI.CreateBackgroundImage(new RectTransform(Vector2.One, historyContainer.RectTransform), OutpostResourceLogUI.BackgroundStyle,
            getDrawArea: () =>
            {
                Rectangle area = historyContainer.Rect;
                area.Height = Math.Max(area.Height, listBox.Content.Rect.Bottom - area.Y);
                return area;
            });
        historyBackground.IgnoreLayoutGroups = true;
        historyBackground.RectTransform.SetAsFirstChild();

        if (!hadHistory)
        {
            historyButton.Enabled = false;
        }

        #endregion

        listBox.UpdateScrollBarSize();

        return;
        bool ProducesResource(ResourceGeneratorBaseComponent component)
        {
            foreach (Resource? r in resources)
            {
                if (component.ResourceUIIdentifier == r.Identifier) 
                {
                    return true;
                }

                if (component.ResourceGenerator.ProducedResourcesList.Any(pr => pr.Identifier == r.Identifier))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private static bool CanDisplayProducedResource(ResourceManager? manager, ProducedResource produced)
    {
        if (produced.ItemResourceAddedItems.Count > 0) { return true; }

        return manager != null &&
               manager.TryGetResource(produced.Identifier, out Resource? resource) &&
               ((resource is ItemResource && !resource.Hidden) || resource.Prefab.ShowInResourcesUI);
    }

    /// <summary>
    /// Ratio for converting the produced resource amount to the number of items shown in the UI.
    /// Only applies when the produced resource adds specific items, otherwise the amount is shown in resource units.
    /// </summary>
    private static float GetDisplayedItemRatio(ResourceManager? manager, ProducedResource produced)
    {
        if (produced.ItemResourceAddedItems.Count == 0) { return 1.0f; }
        return manager != null && manager.TryGetResource(produced.Identifier, out Resource? resource) && resource is ItemResource itemResource ? itemResource.ItemRatio : 1.0f;
    }

    private void CreateOtherResourceFrame(ResourceGeneratorBaseComponent component, GUIFrame frame)
    {
        frame.ClearChildren();
        RectTransform listContent = frame.Parent.RectTransform;
        int index = listContent.GetChildIndex(frame.RectTransform);

        GUILayoutGroup leftLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.75f, 1f), frame.RectTransform, anchor: Anchor.CenterLeft) { AbsoluteOffset = new Point(GUI.IntScale(10), 0) }, isHorizontal: true, childAnchor: Anchor.CenterLeft) { Stretch = true };

        Item generatedBy = component.Item;
        if (generatedBy.GetComponent<Growable>() is Growable growable)
        {
            generatedBy = growable.GetPlanter()?.Item ?? generatedBy;
        }
        
        Sprite? sprite = generatedBy.Prefab.InventoryIcon ?? generatedBy.Prefab.Sprite;
        GUIFrame iconContainer = new GUIFrame(new RectTransform(Vector2.One, leftLayout.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: null);
        new GUIImage(new RectTransform(new Vector2(0.9f), iconContainer.RectTransform, scaleBasis: ScaleBasis.Smallest, anchor: Anchor.Center), sprite);
        GUITextBlock nameBlock = new GUITextBlock(new RectTransform(Vector2.One, leftLayout.RectTransform), generatedBy.Name, font: GUIStyle.SubHeadingFont);
        leftLayout.Recalculate();
        nameBlock.ToolTip = nameBlock.Text;
        nameBlock.Text = ToolBox.LimitString(nameBlock.Text, nameBlock.Font, nameBlock.Rect.Width);

        var producedResources = component.ResourceGenerator.ProducedResourcesList;

        foreach (var produced in producedResources)
        {
            if (!produced.MatchesLocation(outpostData.Location) || !CanDisplayProducedResource(outpostData.ResourceManager, produced)) { continue; }

            bool hasMultiple = produced.ItemResourceAddedItems.Count > 1;

            float itemRatio = GetDisplayedItemRatio(outpostData.ResourceManager, produced);
            float producedAmount = produced.GetScaledAmount(component.ResourceGenerator.AssignedSkillLevel, outpostData.ResourceManager)
                                   * component.ResourceGenerator.GetDiminishingReturnMultiplier(outpostData)
                                   * itemRatio;
            LocalizedString amountLabel = $"{MathF.Round(producedAmount, 2)}";

            if (hasMultiple)
            {
                RichString label = RichString.Rich(TextManager.GetWithVariables("outpostbuilderui.generationlayout",
                                                                                ("[amount]", amountLabel),
                                                                                ("[item]", produced.DisplayNameTag is { } tag ? TextManager.Get(tag) : TextManager.Get($"resource.{produced.Identifier}").Fallback(produced.Identifier.Value))));

                GUITextBlock generationText = new GUITextBlock(new RectTransform(new Vector2(1f, 0f), listContent), font: GUIStyle.IconFont, text: label)
                {
                    Visible = frame.Visible,
                    UserData = component,
                    ToolTip = RichString.Rich(GetTooltip())
                };
                SetElementPosition(generationText);
            }

            if (produced.ItemResourceAddedItems.Count > 0)
            {
                foreach (var (identifier, amount) in produced.ItemResourceAddedItems)
                {
                    GUILayoutGroup textLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.1f), listContent), childAnchor: Anchor.CenterLeft, isHorizontal: true)
                    {
                        Stretch = true,
                        Visible = frame.Visible,
                        UserData = component
                    };
                    SetElementPosition(textLayout);

                    new GUIFrame(new RectTransform(new Vector2(hasMultiple ? 0.05f : 0.025f, 1f), textLayout.RectTransform), style: null)
                    {
                        CanBeFocused = false
                    };

                    CreateDisplayForPrefabs(identifier, textLayout.RectTransform);

                    LocalizedString itemName = GetItemNameForProducedResource(produced, identifier);

                    LocalizedString label =
                        hasMultiple
                            ? itemName
                            : TextManager.GetWithVariables("outpostbuilderui.generationlayout",
                                                           ("[amount]", amountLabel),
                                                           ("[item]", itemName));

                    GUITextBlock labelBlock = new GUITextBlock(new RectTransform(Vector2.One, textLayout.RectTransform), RichString.Rich(label), font: GUIStyle.IconFont);
                    if (!hasMultiple)
                    {
                        labelBlock.ToolTip = RichString.Rich(GetTooltip(itemName.Value));
                    }
                }
            }
            else if (outpostData.ResourceManager != null &&
                    outpostData.ResourceManager.TryGetResource(produced.Identifier, out Resource? resource))
            {
                GUILayoutGroup textLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.1f), listContent), childAnchor: Anchor.CenterLeft, isHorizontal: true)
                {
                    Stretch = true,
                    Visible = frame.Visible,
                    UserData = component
                };
                SetElementPosition(textLayout);

                new GUIFrame(new RectTransform(new Vector2(hasMultiple ? 0.05f : 0.025f, 1f), textLayout.RectTransform), style: null)
                {
                    CanBeFocused = false
                };

                LocalizedString itemName = LocalizedString.JoinIfNotNullOrEmpty(" ", TextManager.Get($"resourceicon.{resource.Identifier}"), resource.DisplayName);
                if (resource is ItemResource itemResource)
                {
                    // If this generator doesn't produce items, but still produces this resource, let's instead use all the item prefabs that belong to this resource
                    CreateDisplayForPrefabs(itemResource.GetAllowedPrefabs(ItemPrefab.Prefabs).ToArray(), textLayout.RectTransform);
                }

                LocalizedString label =
                    hasMultiple
                        ? itemName
                        : TextManager.GetWithVariables("outpostbuilderui.generationlayout",
                                                       ("[amount]", amountLabel),
                                                       ("[item]", itemName));

                GUITextBlock labelBlock = new GUITextBlock(new RectTransform(Vector2.One, textLayout.RectTransform), RichString.Rich(label), font: GUIStyle.IconFont);
                if (!hasMultiple)
                {
                    labelBlock.ToolTip = RichString.Rich(GetTooltip(itemName.Value));
                }
            }

            continue;

            LocalizedString GetTooltip(string label = "")
            {
                LocalizedString tooltip =
                    hasMultiple
                        ? TextManager.GetWithVariables("outpostbuilderui.generateditemtooltipmultiple", ("[amount]", amountLabel))
                        : TextManager.GetWithVariables("outpostbuilderui.generateditemtooltip", ("[amount]", amountLabel), ("[item]", label));

                return tooltip;
            }
        }

        void SetElementPosition(GUIComponent newComponent)
        {
            newComponent.RectTransform.RepositionChildInHierarchy(index + 1);
            index = listContent.GetChildIndex(newComponent.RectTransform);
        }
    }

    private void CreateWorkstationFrame(WorkstationComponent? component, GUIFrame frame)
    {
        if (component is null) { return; }
        frame.ClearChildren();
        RectTransform listContent = frame.Parent.RectTransform;

        foreach (RectTransform rectT in listContent.Children.ToArray())
        {
            if (rectT.GUIComponent == frame) { continue; }
            if (rectT.GUIComponent?.UserData == component)
            {
                rectT.Parent = null;
            }
        }

        int index = listContent.GetChildIndex(frame.RectTransform);

        bool visible = frame.Visible;

        GUILayoutGroup leftLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.75f, 1f), frame.RectTransform, anchor: Anchor.CenterLeft) { AbsoluteOffset = new Point(GUI.IntScale(10),0) }, 
            isHorizontal: true, childAnchor: Anchor.CenterLeft) { Stretch = true };
        Sprite? sprite = component.Item.Prefab.InventoryIcon ?? component.Item.Prefab.Sprite;
        GUIFrame iconContainer = new GUIFrame(new RectTransform(Vector2.One, leftLayout.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: null);
        new GUIImage(new RectTransform(new Vector2(0.9f), iconContainer.RectTransform, scaleBasis: ScaleBasis.Smallest, anchor: Anchor.Center), sprite);
        GUITextBlock nameBlock = new GUITextBlock(new RectTransform(Vector2.One, leftLayout.RectTransform), component.Item.Name, font: GUIStyle.SubHeadingFont);
        leftLayout.Recalculate();
        nameBlock.ToolTip = nameBlock.Text;
        nameBlock.Text = ToolBox.LimitString(nameBlock.Text, nameBlock.Font, nameBlock.Rect.Width);

        Character[] characters = component.GetAssignedCharacters.ToArray();
        if (characters.Length > 0)
        {
            GUIFrame assigneeFrame = new GUIFrame(new RectTransform(new Vector2(0.25f, 0.9f), frame.RectTransform, Anchor.CenterRight), style: null);

            GUIFrame charactersFrame = new GUIFrame(new RectTransform(Vector2.One, assigneeFrame.RectTransform), style: null);

            int offset = 0;
            foreach (Character character in characters)
            {
                var rectT = new RectTransform(new Vector2(1f, 0.8f), charactersFrame.RectTransform)
                {
                    AbsoluteOffset = new Point(offset, 0)
                };
                character.Info.CreateIcon(rectT);
                offset -= GUI.IntScale(8);
            }

            if (characters.Length > 1)
            {
                charactersFrame.RectTransform.AbsoluteOffset = new Point(-(offset / 2), 0);
            }

            if (characters.Length is 1)
            {
                new GUITextBlock(new RectTransform(new Vector2(1f, 0), assigneeFrame.RectTransform, Anchor.BottomCenter), TextManager.Get("outpostbuilderui.dwellerslayoutsingular"), textAlignment: Alignment.BottomCenter)
                {
                    TextColor = characters.First().Info?.Job?.Prefab?.UIColor ?? GUIStyle.Blue,
                    ToolTip = characters.First().Name
                };
            }
            else
            {
                StringBuilder tooltip = new();
                foreach (Character character in characters)
                {
                    tooltip.AppendLine(character.Name);
                }

                new GUITextBlock(new RectTransform(new Vector2(1f, 0), assigneeFrame.RectTransform, Anchor.BottomCenter), 
                    TextManager.GetWithVariable("outpostbuilderui.dwellerslayout", "[amount]", $"{characters.Length}"), 
                    textAlignment: Alignment.BottomCenter)
                {
                    ToolTip = tooltip.ToString().TrimEnd('\n')
                };
            }

            float globalMax = component.ResourceGenerator.MaximumRecipesPerTick;
            if (globalMax >= 0)
            {
                GUITextBlock maxTextBlock = new GUITextBlock(new RectTransform(new Vector2(1f, 0f), listContent), 
                    text: TextManager.GetWithVariable($"outpostbuilderui.workstationcapped.{(MathUtils.NearlyEqual(globalMax, 1) ? "singular" : "plural")}", "[amount]", $"{globalMax}"), 
                    textColor: GUIStyle.Red)
                {
                    AutoScaleHorizontal = true,
                    Visible = visible,
                    UserData = component
                };
                maxTextBlock.RectTransform.MinSize = new Point(0, (int)(maxTextBlock.TextSize.Y * 2));
                SetElementPosition(maxTextBlock);
            }

            var producedResources = component.ResourceGenerator.ProducedResourcesList;

            foreach (var produced in producedResources)
            {
                if (!produced.MatchesLocation(outpostData.Location)) { continue; }

                if (produced.ItemResourceAddedItems.Count is 0)
                {
                    if (outpostData.ResourceManager?.GetResource(produced.Identifier) is { Hidden: false } resource)
                    {
                        LocalizedString itemName = LocalizedString.JoinIfNotNullOrEmpty(" ", TextManager.Get($"resourceicon.{resource.Identifier}"), resource.DisplayName);

                        GUILayoutGroup textLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.1f), listContent), childAnchor: Anchor.CenterLeft, isHorizontal: true)
                        {
                            Stretch = true,
                            Visible = visible,
                            UserData = component
                        };
                        SetElementPosition(textLayout);

                        new GUIFrame(new RectTransform(new Vector2(0.025f, 1f), textLayout.RectTransform), style: null)
                        {
                            CanBeFocused = false
                        };

                        float amount = produced.GetScaledAmount(component.ResourceGenerator.AssignedSkillLevel, outpostData.ResourceManager)
                                       * component.ResourceGenerator.GetDiminishingReturnMultiplier(outpostData);

                        LocalizedString label = TextManager.GetWithVariables("outpostbuilderui.generationlayout",
                                                                             ("[amount]", $"{MathF.Round(amount, 2)}"),
                                                                             ("[item]", itemName));

                        new GUITextBlock(new RectTransform(Vector2.One, textLayout.RectTransform), RichString.Rich(label), font: GUIStyle.IconFont)
                        {
                           ToolTip = TextManager.GetWithVariables("outpostbuilderui.generateditemtooltip", ("[amount]", $"{MathF.Round(amount, 2)}"), ("[item]", resource.DisplayName))
                        };
                    }
                    continue;
                }

                bool hasMultiple = produced.ItemResourceAddedItems.Count > 1;

                float itemRatio = GetDisplayedItemRatio(outpostData.ResourceManager, produced);
                LocalizedString amountLabel;
                bool isBasedOnSkill = false;
                float diminishingMultiplier = component.ResourceGenerator.GetDiminishingReturnMultiplier(outpostData);
                if (produced is { AmountAtMaxSkill: { } maxSkill, AmountAtMinSkill: { } minSkill})
                {
                    amountLabel = TextManager.GetWithVariables("rangeformat",
                                                               ("[min]", $"{MathF.Round(minSkill * diminishingMultiplier * itemRatio, 2)}"),
                                                               ("[max]", $"{MathF.Round(maxSkill * diminishingMultiplier * itemRatio, 2)}"));
                    isBasedOnSkill = true;
                }
                else
                {
                    amountLabel = $"{MathF.Round(produced.Amount * diminishingMultiplier * itemRatio, 2)}";
                }

                if (hasMultiple)
                {
                    RichString label = RichString.Rich(TextManager.GetWithVariables("outpostbuilderui.generationlayout",
                                                                                    ("[amount]", amountLabel),
                                                                                    ("[item]", produced.DisplayNameTag is { } tag ? TextManager.Get(tag) : TextManager.Get($"resource.{produced.Identifier}").Fallback(produced.Identifier.Value))));

                    GUITextBlock generationText = new GUITextBlock(new RectTransform(new Vector2(1f, 0f), listContent), font: GUIStyle.IconFont, text: label)
                    {
                        Visible = visible,
                        UserData = component,
                        ToolTip = RichString.Rich(GetTooltip())
                    };
                    SetElementPosition(generationText);
                }

                foreach (var (identifier, amount) in produced.ItemResourceAddedItems)
                {
                    GUILayoutGroup textLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.1f), listContent), childAnchor: Anchor.CenterLeft, isHorizontal: true)
                    {
                        Stretch = true,
                        Visible = visible,
                        UserData = component
                    };
                    SetElementPosition(textLayout);

                    new GUIFrame(new RectTransform(new Vector2(hasMultiple ? 0.05f : 0.025f, 1f), textLayout.RectTransform), style: null)
                    {
                        CanBeFocused = false
                    };

                    CreateDisplayForPrefabs(identifier, textLayout.RectTransform);

                    LocalizedString itemName = GetItemNameForProducedResource(produced, identifier);

                    LocalizedString label =
                        hasMultiple
                            ? itemName
                            : TextManager.GetWithVariables("outpostbuilderui.generationlayout",
                                                           ("[amount]", amountLabel),
                                                           ("[item]", itemName));

                    GUITextBlock labelBlock = new GUITextBlock(new RectTransform(Vector2.One, textLayout.RectTransform), RichString.Rich(label), font: GUIStyle.IconFont);
                    if (!hasMultiple)
                    {
                        labelBlock.ToolTip = RichString.Rich(GetTooltip(itemName.Value));
                    }
                }
                continue;

                LocalizedString GetTooltip(string label = "")
                {
                    LocalizedString tooltip =
                        hasMultiple
                            ? TextManager.GetWithVariables("outpostbuilderui.generateditemtooltipmultiple", ("[amount]", amountLabel))
                            : TextManager.GetWithVariables("outpostbuilderui.generateditemtooltip", ("[amount]", amountLabel), ("[item]", label));

                    if (isBasedOnSkill)
                    {
                        tooltip += $"\n‖color:gui.orange‖{TextManager.Get("outpostbuilderui.generationdisclaimer")}‖end‖";
                    }

                    if (diminishingMultiplier < 1f)
                    {
                        int reductionPercentage = (int)MathF.Round((1f - diminishingMultiplier) * 100f);
                        tooltip += $"\n‖color:gui.orange‖{TextManager.GetWithVariable("outpostbuilderui.diminishingreturndisclaimer", "[percentage]", reductionPercentage.ToString())}‖end‖";
                    }

                    return tooltip;
                }
            }

            foreach (var converted in component.ResourceGenerator.ConvertedItemsList)
            {
                Resource? inputResource = outpostData.ResourceManager?.GetResource(converted.ResourceIdentifier);
                float inputItemRatio = inputResource is ItemResource inputItemResource ? inputItemResource.ItemRatio : 1.0f;

                Resource? outputResource = outpostData.ResourceManager?.GetResource(converted.OutputResource);
                float outputItemRatio = outputResource is ItemResource outputItemResource ? outputItemResource.ItemRatio : 1.0f;

                ConversionRecipeHash hash = converted.GetHash();
                GUILayoutGroup textLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.1f), listContent), childAnchor: Anchor.CenterLeft, isHorizontal: true)
                {
                    Stretch = true,
                    Visible = visible,
                    UserData = component
                };
                SetElementPosition(textLayout);

                LocalizedString inputName = string.Join(", ", 
                    converted.ConsumedItems.Select(consumed =>
                        GetItemNameWithAmount(consumed, inputItemRatio).Value));
                LocalizedString outputName = string.Join(", ",
                    converted.ProducedItems.Select(produced =>
                        GetItemNameWithAmount(produced, outputItemRatio).Value));

                // Item names highlighted in the tooltip
                LocalizedString inputNameHighlighted = string.Join(", ",
                    converted.ConsumedItems.Select(consumed =>
                        GetItemNameWithAmount(consumed, inputItemRatio, highlightName: true).Value));
                LocalizedString outputNameHighlighted = string.Join(", ",
                    converted.ProducedItems.Select(produced =>
                        GetItemNameWithAmount(produced, outputItemRatio, highlightName: true).Value));

                LocalizedString tooltipText = TextManager.GetWithVariables("outpostbuilderui.converteditemtooltip",
                                                                           ("[input]", inputNameHighlighted),
                                                                           ("[output]", outputNameHighlighted));

                RichString tooltip = RichString.Rich(tooltipText);
                RichString disabledTooltip = RichString.Rich($"{tooltipText}\n\n‖color:gui.red‖{TextManager.Get("outpostbuilderui.converteditemdisable")}‖end‖");

                bool conversionEnabled = component.ResourceGenerator.IsConversionEnabled(converted);

                GUIFrame tooltipContainer = new GUIFrame(new RectTransform(Vector2.One, textLayout.RectTransform), style: null)
                {
                    ToolTip = conversionEnabled ? tooltip : disabledTooltip,
                    IgnoreLayoutGroups = true
                };

                new GUIFrame(new RectTransform(new Vector2(0.025f, 1f), textLayout.RectTransform), style: null) { CanBeFocused = false };

                GUIFrame tickBoxContainer = new GUIFrame(new RectTransform(new Vector2(0.2f, 1f), textLayout.RectTransform), style: null);

                GUITickBox tickBox = new GUITickBox(new RectTransform(Vector2.One, tickBoxContainer.RectTransform, Anchor.Center), label: string.Empty)
                {
                    Selected = conversionEnabled,
                    ToolTip = TextManager.Get("outpostbuilderui.disablecraftingcheckbox"),
                };

                ItemPrefab[] consumedPrefabs = converted.ConsumedItems
                    .SelectMany(consumed => ItemResource.ResolveItemPrefab(consumed.Item, excludedIdentifierOrTag: Identifier.Empty))
                    .Distinct()
                    .ToArray();
                GUIComponent inputIcon = CreateDisplayForPrefabs(consumedPrefabs, textLayout.RectTransform);
                inputIcon.CanBeFocused = false;

                GUILayoutGroup inputTextLayout = new GUILayoutGroup(new RectTransform(Vector2.One, textLayout.RectTransform));

                GUITextBlock inputBlock = new GUITextBlock(new RectTransform(new Vector2(1f, 0.5f), inputTextLayout.RectTransform), new string('W', 32), font: GUIStyle.IconFont, textAlignment: Alignment.BottomLeft)
                {
                    Enabled = conversionEnabled,
                    DisabledTextColor = GUIStyle.TextColorDim,
                    AutoScaleHorizontal = false,
                    Wrap = true,
                    CanBeFocused = false
                };

                GUITextBlock ownedBlock = new GUITextBlock(new RectTransform(new Vector2(1f, 0.5f), inputTextLayout.RectTransform), GetOwnedText(converted, inputResource, component.ResourceGenerator.AssignedSkillLevel), font: GUIStyle.SmallFont, textAlignment: Alignment.TopLeft)
                {
                    Enabled = conversionEnabled,
                    DisabledTextColor = GUIStyle.TextColorDim,
                    AutoScaleHorizontal = true,
                    CanBeFocused = false
                };

                new GUIImage(new RectTransform(new Vector2(1f, 0.66f), textLayout.RectTransform, scaleBasis: ScaleBasis.BothHeight, anchor: Anchor.CenterRight), "GUIButtonHorizontalArrowFreeScale", scaleToFit: true)
                {
                    CanBeFocused = false
                };

                ItemPrefab[] producedPrefabs = converted.ProducedItems
                    .SelectMany(produced => ItemResource.ResolveItemPrefab(produced.Item, excludedIdentifierOrTag: Identifier.Empty))
                    .Distinct()
                    .ToArray();
                GUIComponent outputIcon = CreateDisplayForPrefabs(producedPrefabs, textLayout.RectTransform);
                outputIcon.CanBeFocused = false;

                GUITextBlock outputBlock = new GUITextBlock(new RectTransform(Vector2.One, textLayout.RectTransform), new string('W', 32), font: GUIStyle.IconFont)
                {
                    Enabled = conversionEnabled,
                    DisabledTextColor = GUIStyle.TextColorDim,
                    AutoScaleHorizontal = false,
                    Wrap = true,
                    CanBeFocused = false
                };

                tickBox.OnSelected = box =>
                {
                    bool state = box.Selected;
                    tooltipContainer.ToolTip = state ? tooltip : disabledTooltip;
                    inputBlock.Enabled = state;
                    outputBlock.Enabled = state;
                    ownedBlock.Enabled = state;
                    if (component.ResourceGenerator.SetConversionState(hash, state ? ConversionState.Enabled : ConversionState.Disabled))
                    {
                        component.Item.CreateClientEvent(component);
                    }
                    return true;
                };

                textLayout.ForceLayoutRecalculation();

                inputBlock.Text = RichString.Rich(inputName);
                outputBlock.Text = RichString.Rich(outputName);

                //don't let recipes with lots of items grow the row vertically: cut off the overflowing lines (full text is in the row's tooltip)
                ToolBox.LimitStringHeight(inputBlock, inputBlock.Rect.Height, setTooltip: false);
                ToolBox.LimitStringHeight(outputBlock, outputBlock.Rect.Height, setTooltip: false);

                currentlyOpenedWorkstationConversions.TryAdd(component, []);
                currentlyOpenedWorkstationConversions[component].Add(new WorkstationConversionUIElements(converted, tickBox, inputBlock, outputBlock, ownedBlock));

            }

            frame.ForceLayoutRecalculation();
        }
        else
        {
            GUITextBlock noDwellerAssignedBlock = new GUITextBlock(new RectTransform(new Vector2(1f, 0f), listContent), TextManager.Get("outpostbuilderui.nodwellerassigned"), 
                GUIStyle.Red, font: GUIStyle.SubHeadingFont, textAlignment: Alignment.Center)
            {
                CanBeFocused = false,
                Visible = visible,
                UserData = component
            };
            SetElementPosition(noDwellerAssignedBlock);

            new GUIButton(new RectTransform(new Vector2(0.2f, 0.5f), frame.RectTransform, Anchor.CenterRight), style: "DwellerUIWorkstationAssignButton")
            {
                OnClicked = (button, o) =>
                {
                    OutpostManagementUI.Instance?.SelectTab(OutpostManagementUI.Tab.Dwellers);
                    return true;
                },
                ToolTip = TextManager.Get("outpostbuilderui.assigndweller")
            };
        }
        return;

        void SetElementPosition(GUIComponent newComponent)
        {
            newComponent.RectTransform.RepositionChildInHierarchy(index + 1);
            index = listContent.GetChildIndex(newComponent.RectTransform);
        }
    }

    private static LocalizedString GetItemNameForProducedResource(ProducedResource produced, Identifier resourceItemIdentifier)
    {
        var itemPrefabs = ItemResource.ResolveItemPrefab(resourceItemIdentifier, excludedIdentifierOrTag: Identifier.Empty);

        LocalizedString itemName =
            produced.DisplayNameTag is { } tag ?
                TextManager.Get(tag) :
                TextManager.Get($"entityname.{resourceItemIdentifier}")
                    .Fallback(TextManager.Get($"tagname.{resourceItemIdentifier}"))
                    .Fallback(itemPrefabs.FirstOrDefault()?.Name ?? resourceItemIdentifier.ToString());

        return itemName;
    }

    private static LocalizedString GetItemNameWithAmount(ConvertedItem.ItemProduction itemProduction, float itemRatio, bool highlightName = false)
    {
        Identifier itemIdentifier = itemProduction.Item;
        LocalizedString itemName =
            itemProduction.DisplayNameTag is { } tag ?
                TextManager.Get(tag).Fallback(itemIdentifier.Value) :
                TextManager.Get($"entityname.{itemIdentifier}")
                           .Fallback(TextManager.Get($"tagname.{itemIdentifier}"))
                           .Fallback(itemIdentifier.Value);

        LocalizedString amountText;

        if (itemProduction.AmountAtMinSkill.HasValue && itemProduction.AmountAtMaxSkill.HasValue)
        {
            amountText = TextManager.GetWithVariables("rangeformat",
                ("[min]", GetAmountText(itemProduction.AmountAtMinSkill.Value, itemRatio)),
                ("[max]", GetAmountText(itemProduction.AmountAtMaxSkill.Value, itemRatio)));
        }
        else
        {
            amountText = GetAmountText(itemProduction.Amount, itemRatio);
        }
        string itemNameText = highlightName ? $"‖color:gui.orange‖{itemName.Value}‖end‖" : itemName.Value;
        return TextManager.GetWithVariables("outpostbuilderui.inputcraftinglayout", ("[amount]", amountText), ("[item]", itemNameText));


        static string GetAmountText(float producedAmount, float itemRatio)
        {
            return FormatAmount(producedAmount * itemRatio);
        }
    }

    /// <summary>
    /// Formats (nearly) whole numbers as integers, otherwise shows one decimal.
    /// </summary>
    public static string FormatAmount(float amount)
    {
        float rounded = MathF.Round(amount, 1);
        if (MathUtils.NearlyEqual(rounded, MathF.Round(rounded)))
        {
            return $"{(int)MathF.Round(rounded)}";
        }
        return $"{rounded}";
    }

    /// <summary>
    /// Text describing how much the given number of items counts towards the outpost's item resources (e.g. 10 thorium fuel rods count as 20 reactor fuel).
    /// </summary>
    private string GetResourceContributionText(ItemPrefab prefab, int itemCount, bool isDepleted)
    {
        if (outpostData.ResourceManager is not ResourceManager resourceManager) { return string.Empty; }
        
        List<string> lines = [];
        foreach (Resource resource in resourceManager.Resources)
        {
            if (resource is not ItemResource { ItemRatio: > 0.0f } itemResource) { continue; }
            if (!itemResource.BelongsIn(prefab) && itemResource.CountItems(prefab.Identifier) <= 0.0f) { continue; }

            // CombinedResources are a plain sum of their sub-resources, so the amount per item is the same in the parent resource
            Resource displayedResource = GetResourceToDisplayContributionAs(itemResource);
            float amount = itemCount / itemResource.ItemRatio;
            string line =
                isDepleted ?
                $"‖color:gui.red‖0‖end‖ ‖color:gui.orange‖{displayedResource.DisplayName}‖end‖" : 
                $"‖color:gui.green‖{FormatAmount(amount)}‖end‖ ‖color:gui.orange‖{displayedResource.DisplayName}‖end‖";
            if (!lines.Contains(line))
            {
                lines.Add(line);
            }
        }
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Hidden sub-resources (e.g. basicfuel) are displayed as the CombinedResource they're part of (e.g. reactorfuel).
    /// Finds the first non-hidden resource, or if there's none, the nearest one with a display name.
    /// </summary>
    private static Resource GetResourceToDisplayContributionAs(Resource resource)
    {
        if (!resource.Hidden) { return resource; }

        Resource current = resource;
        Resource? nearestNamed = TextManager.ContainsTag($"resource.{resource.Identifier}") ? resource : null;
        const int MaxDepth = 10;
        for (int i = 0; i < MaxDepth; i++)
        {
            Resource child = current;
            Resource? parent = child.Manager.Resources.FirstOrDefault(r => r is CombinedResource cr && cr.ContainsResource(child));
            if (parent is null) { break; }
            if (!parent.Hidden) { return parent; }
            if (nearestNamed is null && TextManager.ContainsTag($"resource.{parent.Identifier}"))
            {
                nearestNamed = parent;
            }
            current = parent;
        }
        return nearestNamed ?? resource;
    }

    /// <summary>
    /// How many times the conversion can be done with the items in the input resource, i.e. limited by the consumed item we have the least of.
    /// </summary>
    private static LocalizedString GetOwnedText(ConvertedItem conversion, Resource? inputResource, float skillLevel)
    {
        int conversionCount = 0;
        if (inputResource is ItemResource itemResource && conversion.ConsumedItems.Any())
        {
            conversionCount = int.MaxValue;
            bool hasAnyConsumedItems = false;
            foreach (var consumed in conversion.ConsumedItems)
            {
                // Both amounts are in resource units, so ItemRatio cancels out
                float amountPerConversion = consumed.GetScaledAmount(skillLevel);
                if (amountPerConversion <= 0f) { continue; }
                // Count the actual items if the outpost is loaded (the resource's stored amounts only match the items after leaving the outpost)
                conversionCount = Math.Min(conversionCount, (int)MathF.Floor(itemResource.CountItemsInLoadedOutpost(consumed.Item) / amountPerConversion));
                hasAnyConsumedItems = true;
            }
            if (!hasAnyConsumedItems) { conversionCount = 0; }
        }

        return TextManager.GetWithVariable("campaignstore.owned", "[amount]", conversionCount.ToString());
    }

    private static GUIComponent CreateDisplayForPrefabs(Identifier identifier, RectTransform parent)
    {
        ItemPrefab[] prefabs = ItemResource.ResolveItemPrefab(identifier, excludedIdentifierOrTag: Identifier.Empty).ToArray();
        return CreateDisplayForPrefabs(prefabs, parent);
    }

    private static GUIComponent CreateDisplayForPrefabs(ItemPrefab[] prefabs, RectTransform parent)
    {
        switch (prefabs.Length)
        {
            case 1:
                ItemPrefab onlyPrefab = prefabs.First();
                Sprite itemSprite = onlyPrefab.InventoryIcon ?? onlyPrefab.Sprite;
                return new GUIImage(new RectTransform(Vector2.One, parent, scaleBasis: ScaleBasis.Smallest), itemSprite)
                {
                    Color = onlyPrefab.InventoryIcon != null ? onlyPrefab.InventoryIconColor : onlyPrefab.SpriteColor
                };
            case > 1:
                return new GUICustomComponent(new RectTransform(Vector2.One, parent, scaleBasis: ScaleBasis.Smallest), onDraw: (batch, customComponent) =>
                {
                    float timer = (float)customComponent.UserData;
                    int index = (int)(MathF.Floor(timer));
                    ItemPrefab prefabToDraw = prefabs[index];
                    Sprite drawSprite = prefabToDraw.InventoryIcon ?? prefabToDraw.Sprite;
                    if (drawSprite is null) { return; }

                    Rectangle rect = customComponent.Rect;
                    float scale = Math.Min(rect.Width / drawSprite.size.X, rect.Height / drawSprite.size.Y);
                    Color color = prefabToDraw.InventoryIcon != null ? prefabToDraw.InventoryIconColor : prefabToDraw.SpriteColor;

                    //batch.Draw(drawSprite.Texture, new Vector2(rect.X + rect.Width / 2.0f, rect.Y + rect.Height / 2.0f), sourceRectangle: sprite.SourceRect, color: color, rotation: 0f, origin: sprite.Origin, scale: scale, SpriteEffects.None, 0f);
                    drawSprite.Draw(batch, rect.Center.ToVector2(), color, scale: scale);
                },
                                       onUpdate: (f, customComponent) =>
                                       {
                                           float timer = (float)customComponent.UserData;
                                           timer += f;
                                           if (timer > prefabs.Length)
                                           {
                                               timer = 0f;
                                           }

                                           customComponent.UserData = timer;
                                       })
                {
                    UserData = 0f
                };
        }
        return new GUIFrame(new RectTransform(Vector2.One, parent, scaleBasis: ScaleBasis.BothHeight));
    }

    private static void SetItemsText(GUIComponent button, int count)
    {
        if (button.FindChild("count", recursive: true) is not GUITextBlock block) { return; }

        block.Text = TextManager.GetWithVariable($"outpostbuilderui.itemsformat.{(count is 1 ? "singular" : "plural")}",
                                                 "[amount]",
                                                 $"{count}");
    }

    /// <summary>
    /// Is the item depleted (e.g. an empty fuel rod), meaning it no longer counts towards any resource? See <see cref="ItemResource.DepleteItemsInsteadOfRemoving"/>.
    /// </summary>
    private bool IsDepletedResourceItem(Item item)
    {
        if (item.Condition > 0.0f) { return false; }
        if (outpostData.ResourceManager is not ResourceManager resourceManager) { return false; }
        return resourceManager.Resources.Any(r => r is ItemResource itemResource && itemResource.IsDepleted(item));
    }

    private Resource? GetApplicableResourceForItem(ItemPrefab prefab)
    {
        if (cachedItemCategories.TryGetValue(prefab, out Resource? cached))
        {
            return cached;
        }

        foreach (Resource trackedResource in trackedItemResources)
        {
            foreach (Resource resource in FindAllResources(trackedResource))
            {
                switch (resource)
                {
                    case ItemResource ir when ir.BelongsIn(prefab):
                        cachedItemCategories[prefab] = trackedResource;
                        return trackedResource;
                    case FacilityResource facility when facility.FacilityTag == prefab.Identifier || prefab.Tags.Contains(facility.FacilityTag):
                        return trackedResource;
                }
            }
        }

        cachedItemCategories[prefab] = null;
        return null;
    }

    /// <summary>
    /// Find all the resources this resource consists of (subresources of a CombinedResource, or the resources that determine the value of a VariableResource).
    /// Also returns the resource itself.
    /// </summary>
    public static IEnumerable<Resource> FindAllResources(Resource resource)
    {
        yield return resource;

        if (resource.Prefab.ConsistsOfSubResources) 
        {
            switch (resource)
            {
                case CombinedResource cr:
                    {
                        foreach (Resource r in cr.UnderlyingResources.SelectMany(FindAllResources)) { yield return r; }
                        break;
                    }
                case VariableResource vr:
                    {
                        foreach (Resource r in vr.GetResourcesAffectingThisResource().SelectMany(FindAllResources)) { yield return r; }
                        break;
                    }
            }
        }
    }

    public void OnOpened()
    {
        uiUpdateTimer = 0.0f;
    }

    public void UpdateConversions(ResourceGeneratorBaseComponent component)
    {
        if (!currentlyOpenedWorkstationConversions.TryGetValue(component, out var conversions)) { return; }

        foreach (var conversionUIElements in conversions)
        {
            bool state = component.ResourceGenerator.IsConversionEnabled(conversionUIElements.Conversion);
            conversionUIElements.Toggle(state);
        }
    }

    public void UpdateUI(in float deltaTime)
    {
        const int itemsPerLine = 10;

        uiUpdateTimer -= deltaTime;
        if (uiUpdateTimer > 0) { return; }
        uiUpdateTimer = UIUpdateInterval;

        foreach ((Resource resource, GUITextBlock textBlock) in resourceAmountTexts)
        {
            textBlock.Text = resource.GetAmountText(withNumber: true);
        }

        foreach ((ResourceGeneratorBaseComponent workstation, List<WorkstationConversionUIElements> conversions) in currentlyOpenedWorkstationConversions)
        {
            foreach (var (conversion, _, _, _, ownedText) in conversions)
            {
                Resource? inputResource = outpostData.ResourceManager?.GetResource(conversion.ResourceIdentifier);
                ownedText.Text = GetOwnedText(conversion, inputResource, workstation.ResourceGenerator.AssignedSkillLevel);
            }
        }

        //depleted items (e.g. empty fuel rods) are listed separately, because they don't count towards the resources
        Dictionary<(ItemPrefab Prefab, bool Depleted), int> items = [];
        foreach (Item item in outpost.GetItems(alsoFromConnectedSubs: false))
        {
            if (item.IsHidden || !item.IsPlayerTeamInteractable || item.Prefab.HideInMenus) { continue; }
            if (item.GetRootInventoryOwner() is not Item rootItem || rootItem == item) { continue; }
            var key = (item.Prefab, IsDepletedResourceItem(item));
            if (items.TryGetValue(key, out int value))
            {
                items[key] = value + 1;
            }
            else
            {
                items.Add(key, 1);
            }
        }

        foreach (GUIComponent component in itemList.Content.Children.ToArray())
        {
            switch (component)
            {
                case GUIListBox:
                    component.RectTransform.Parent = null;
                    break;
                case GUIButton:
                    component.Enabled = false;
                    SetItemsText(component, 0);
                    break;
            }
        }

        Dictionary<Identifier, int> counts = [];

        foreach (var ((itemPrefab, isDepleted), amount) in items.OrderBy(kvp => kvp.Key.Prefab.Name).ThenBy(kvp => kvp.Key.Depleted))
        {
            Resource? res = GetApplicableResourceForItem(itemPrefab);
            GUIListBox parent = GetListBox(res);

            LocalizedString tooltip = itemPrefab.GetTooltip(Character.Controlled);

            if (isDepleted)
            {
                tooltip = tooltip.Replace(itemPrefab.Name,
                    $"‖color:gui.red‖{TextManager.GetWithVariable("displayname.emptyitem", "[itemname]", itemPrefab.Name)}‖end‖");
            }
            string contributionText = GetResourceContributionText(itemPrefab, amount, isDepleted);
            if (!contributionText.IsNullOrEmpty())
            {
                tooltip += $"\n\n{contributionText}";
            }
            GUIFrame frame = new GUIFrame(new RectTransform(new Vector2(1f / itemsPerLine, 1f), parent.Content.RectTransform), style: null)
            {
                ToolTip = RichString.Rich(tooltip)
            };

            GUIImage icon = new GUIImage(new RectTransform(new Vector2(0.9f), frame.RectTransform, anchor: Anchor.Center, scaleBasis: ScaleBasis.BothWidth), itemPrefab.InventoryIcon ?? itemPrefab.Sprite, scaleToFit: true)
            {
                CanBeFocused = false,
                Color = itemPrefab.InventoryIcon != null ? itemPrefab.InventoryIconColor : itemPrefab.SpriteColor
            };
            if (isDepleted)
            {
                icon.Color *= 0.5f;
            }
            icon.HoverColor = icon.SelectedColor = icon.Color;
            new GUITextBlock(new RectTransform(Vector2.One, frame.RectTransform, Anchor.BottomRight), text: TextManager.GetWithVariable("campaignstore.quantity", "[amount]", $"{amount}"), font: GUIStyle.Font, textAlignment: Alignment.BottomRight)
            {
                Shadow = true,
                CanBeFocused = false,
                Padding = Vector4.Zero
            };
            parent.ForceLayoutRecalculation();

            GUIButton? category = GetCategory(res);
            if (category != null)
            {
                Identifier identifier = res?.Identifier ?? Identifier.Empty;
                int newCount =
                    counts[identifier] =
                        counts.GetValueOrDefault(identifier) + 1;
                category.Enabled = true;
                SetItemsText(category, newCount);
            }
        }
        return;

        GUIButton? GetCategory(Resource? resource)
        {
            foreach (GUIComponent child in itemList.Content.Children)
            {
                if (child is GUIButton btn && child.UserData == resource)
                {
                    return btn;
                }
            }

            return null;
        }

        GUIListBox GetListBox(Resource? resource)
        {
            foreach (GUIComponent child in itemList.Content.Children.Reverse())
            {
                if (child is GUIListBox childList && child.UserData == resource)
                {
                    if (childList.Content.CountChildren < itemsPerLine)
                    {
                        return childList;
                    }
                }
            }

            GUIListBox lb = new GUIListBox(new RectTransform(new Vector2(1f, 0.075f), itemList.Content.RectTransform), isHorizontal: true, style: null)
            {
                Visible = visibleCategories.GetValueOrDefault(resource?.Identifier ?? Identifier.Empty),
                UserData = resource,
                AutoHideScrollBar = false,
                ScrollBarVisible = false
            };

            if (resource is null) { return lb; }

            int index = 0;
            foreach (GUIComponent? child in itemList.Content.Children.Reverse())
            {
                if (child.UserData == resource && child != lb)
                {
                    index = itemList.Content.GetChildIndex(child) + 1;
                    break;
                }
            }

            lb.RectTransform.RepositionChildInHierarchy(index);
            return lb;
        }
    }

    private static void CreateResourceStatusIndicatorIcon(ResourceStatusIndicator modifier, GUIComponent parent)
    {
        var frame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.1f), parent.RectTransform), style: null);
        var horizontalLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), frame.RectTransform, Anchor.Center), isHorizontal: true)
        { 
            RelativeSpacing = 0.02f,
            Stretch = true 
        };
        GUIImage icon = new(new RectTransform(new Point(horizontalLayout.Rect.Height), horizontalLayout.RectTransform), modifier.Icon, scaleToFit: true)
        {
            Color = modifier.IconColor,
            CanBeFocused = false
        };
        var verticalLayout = new GUILayoutGroup(new RectTransform(Vector2.One, horizontalLayout.RectTransform), isHorizontal: false)
        {
            Stretch = true
        };
        new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), verticalLayout.RectTransform), modifier.Name, font: GUIStyle.SubHeadingFont);
        new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), verticalLayout.RectTransform), modifier.Description, wrap: true, textColor: GUIStyle.TextColorBright);
        verticalLayout.Recalculate();
        horizontalLayout.Recalculate();
        verticalLayout.RectTransform.MinSize = new Point(0, (int)verticalLayout.Children.Sum(c => ((c as GUITextBlock)?.TextSize.Y ?? 0) * 1.2f));
        frame.RectTransform.MinSize = new Point(0, (int)(verticalLayout.RectTransform.MinSize.Y / horizontalLayout.RectTransform.RelativeSize.Y));
        verticalLayout.Recalculate();
        horizontalLayout.Recalculate();
    }
}
