using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Linq;
using Barotrauma;
using Barotrauma.Extensions;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace HomeAndHarbor;

internal sealed class TechnologyUI : IDisposable
{
    private readonly record struct TierComponent(TechnologyTier Tier, GUIComponent Title, GUITextBlock RequirementText, GUIComponent Parent);
    private const float CategoryPadding = 5f;
    private const float TechnologySpacing = 25f;

    public GUIComponent Component
        => scrollArea;

    private GUIDualScrollArea scrollArea;
    public GUIComponent PrefabParents;
    public ImmutableDictionary<TechnologyPrefab, GUIButton> PrefabComponents = ImmutableDictionary<TechnologyPrefab, GUIButton>.Empty;
    private ImmutableArray<TierComponent> tierComponents = [];
    private GUIFrame selectedNodeTooltip;

    private static int TitleSize
        => GUI.IntScale(100);

    private TechUILineManager? lineManager;

    public static ResearchManager Manager
    {
        get
        {
            ResearchManager.TryGetResearchManagerForCampaign(GameMain.GameSession.Campaign, out ResearchManager? researchManager);
            return researchManager!;
        }
    }

    private static bool HasPermission
        => CampaignMode.AllowedToManageCampaign(ClientPermissions.ManageCampaign);

    public bool IsSelected(TechnologyPrefab prefab)
    {
        if (PrefabComponents.TryGetValue(prefab, out GUIButton? button))
        {
            return button.State is not GUIComponent.ComponentState.None;
        }

        return false;
    }

    public bool AnySelected()
    {
        foreach (GUIButton button in PrefabComponents.Values)
        {
            if (button.Selected) { return true; }
        }

        return false;
    }

    // Having more than 1 technology tree is very unsupported, but... there's UI for it at least
    public static Dictionary<TechnologyTreePrefab, TechnologyUI> CreateTechTreesForPrefabs(GUIComponent parent, PersistentOutpostData? outpostData)
    {
        GUILayoutGroup layout = new GUILayoutGroup(new RectTransform(Vector2.One, parent.RectTransform))
        {
            Stretch = true
        };
        Dictionary<TechnologyTreePrefab, TechnologyUI> techTrees = new();

        GUIListBox? buttonLayout = null;
        int count = TechnologyTreePrefab.Prefabs.Count();
        if (count > 1)
        {
            buttonLayout = new GUIListBox(new RectTransform(new Vector2(1f, 0.05f), layout.RectTransform), isHorizontal: true, style: null)
            {
                AutoHideScrollBar = false,
                ScrollBarVisible = false,
                ScrollBarEnabled = false,
                PlaySoundOnSelect = true,
                OnSelected = (component, o) =>
                {
                    if (o is not TechnologyTreePrefab userDataPrefab) { return false; }
                    foreach (var (prefab, ui) in techTrees)
                    {
                        ui.Component.Parent.Visible = (prefab == userDataPrefab);
                    }

                    foreach (GUIComponent child in component.Parent.Children)
                    {
                        child.Selected = (child == component);
                    }

                    return true;
                }
            };
        }
        GUIFrame treeFrameParent = new GUIFrame(new RectTransform(new Vector2(1f, count > 1 ? 0.95f : 1f), layout.RectTransform));

        foreach (TechnologyTreePrefab prefab in TechnologyTreePrefab.Prefabs)
        {
            if (buttonLayout != null)
            {
                GUIFrame headerButton = new GUIFrame(new RectTransform(new Vector2(1f / count, 1f), buttonLayout.Content.RectTransform), style: "GUITabButton")
                {
                    UserData = prefab
                };

                new GUITextBlock(new RectTransform(Vector2.One, headerButton.RectTransform), text: prefab.Identifier.Value, textAlignment: Alignment.Center)
                {
                    Font = GUIStyle.SubHeadingFont,
                    TextColor = GUIStyle.TextColorNormal,
                    CanBeFocused = false,
                };
            }

            GUIFrame frame = new GUIFrame(new RectTransform(Vector2.One, treeFrameParent.RectTransform), style: null)
            {
                Visible = false,
            };
            TechnologyUI ui = new TechnologyUI(frame, prefab, outpostData);
            techTrees.Add(prefab, ui);
        }

        if (TechnologyTreePrefab.Prefabs.FirstOrDefault() is { } first)
        {
            techTrees[first].Component.Parent.Visible = true;
        }

        return techTrees;
    }

    private Identifier EventIdentifier => new Identifier($"{nameof(TechnologyUI)}: {originalTechTree.Identifier}");
    private readonly TechnologyTreePrefab originalTechTree;
    private readonly GUIComponent originalParent;
    private readonly TechnologyUIPrefabLayout originalLayout;

    public TechnologyUI(GUIComponent parent, TechnologyTreePrefab tree, PersistentOutpostData? outpostData)
    {
        TechnologyUIPrefabLayout layout = new TechnologyUIPrefabLayout(tree);

        originalLayout = layout;
        originalParent = parent;
        originalTechTree = tree;

        CreateUI(tree, layout, parent.RectTransform);
        GameMain.Client?.OnPermissionChanged.RegisterOverwriteExisting(EventIdentifier, _ =>
        {
            RecreateFrames();
        });

        outpostData?.DwellerManager.OnDwellerWorkstationChanged.RegisterOverwriteExisting(EventIdentifier, OnDwellerWorkstationChanged);
    }

    private void OnDwellerWorkstationChanged(DwellerChangedEvent _)
    {
        RecreateFramesAndTierLabels();
    }

    /// <summary>
    /// Recreates the technology frames and refreshes the tier labels, so changes to the requirements (prerequisites, tiers, campaign metadata) are reflected in the UI.
    /// </summary>
    public void RecreateFramesAndTierLabels()
    {
        RecreateFrames();
        foreach (TierComponent component in tierComponents)
        {
            component.RequirementText.Text = string.Empty; // Clear the text first, else the text won't override since the text is the same (but colors aren't)
            component.RequirementText.Text = GetTierLabel(component.Tier, ResearchManager.IsTierUnlocked(component.Tier.Identifier));
        }
    }

    [MemberNotNull(nameof(scrollArea), nameof(selectedNodeTooltip), nameof(lineManager), nameof(PrefabParents))]
    private void CreateUI(TechnologyTreePrefab tree, TechnologyUIPrefabLayout layout, RectTransform rectTransform)
    {
        rectTransform.ClearChildren();
        scrollArea = new GUIDualScrollArea(new RectTransform(Vector2.One, rectTransform));
        selectedNodeTooltip = new GUIFrame(new RectTransform(new Vector2(0.25f, 0.45f), rectTransform, anchor: Anchor.BottomRight)
        {
            AbsoluteOffset = new Point(GUI.IntScale(32)),
            MinSize = new Point(400, 350)
        }, style: null)
        {
            Visible = false
        };

        Vector2 areaSize = new Vector2(1.0f, layout.MaxTierCounts.Values.Sum() * 0.375f);

        // don't allow the categories to be less wide than 300px
        Point minSize = new Point(tree.Categories.Length * GUI.IntScale(300), 0);

        // resize background to be a bit extra so we can scroll further
        GUILayoutGroup backgroundLayout = new GUILayoutGroup(new RectTransform(areaSize with { Y = areaSize.Y * 1.2f }, scrollArea.Content.RectTransform) { MinSize = minSize }, isHorizontal: true);

        new GUICustomComponent(new RectTransform(areaSize, scrollArea.Content.RectTransform) { MinSize = minSize }, onDraw: DrawBackground, onUpdate: Update);
        GUILayoutGroup prefabLayout = new GUILayoutGroup(new RectTransform(areaSize, scrollArea.Content.RectTransform) { MinSize = minSize }, isHorizontal: false);

        new GUICustomComponent(new RectTransform(areaSize, scrollArea.Content.RectTransform) { MinSize = minSize }, onDraw: DrawForeground)
        {
            CanBeFocused = false
        };

        PrefabParents = prefabLayout;

        foreach (TechnologyCategory category in tree.Categories)
        {
            LocalizedString name =
                TextManager.Get($"technologycategory.{category.Identifier}")
                           .Fallback(category.Identifier.Value);

            GUIFrame frame = CreateCategoryFrame(name, category.Color, new RectTransform(new Vector2(1f / tree.Categories.Length, 1f), backgroundLayout.RectTransform), category.Portrait);
            GUILayoutGroup categoryLayout = new(new RectTransform(Vector2.One, frame.RectTransform), childAnchor: Anchor.TopCenter)
            {
                AbsoluteSpacing = GUI.IntScale(TechnologySpacing),
            };

            foreach (TechnologyTier tier in tree.Tiers)
            {
                new GUILayoutGroup(new RectTransform(Vector2.One, categoryLayout.RectTransform))
                {
                    UserData = tier,
                    AbsoluteSpacing = GUI.IntScale(TechnologySpacing),
                };
            }

            prefabLayout.RectTransform.AbsoluteOffset = new Point(0, frame.Rect.Location.Y - backgroundLayout.Rect.Location.Y);
            prefabLayout.RectTransform.MaxSize = new Point(prefabLayout.RectTransform.NonScaledSize.X, frame.Rect.Height);
        }
        Dictionary<GUIComponent, float> relativeSizes = [];
        Dictionary<(Identifier Category, Identifier Tier), GUIComponent> technologyPositions = [];

        var tierBuilder = ImmutableArray.CreateBuilder<TierComponent>();
        foreach (TechnologyTier tier in tree.Tiers)
        {
            int count = layout.MaxTierCounts.GetValueOrDefault(tier, 0);

            GUILayoutGroup titleComponent = new GUILayoutGroup(new RectTransform(new Point(prefabLayout.Rect.Width, TitleSize), prefabLayout.RectTransform))
            {
                Visible = false
            };

            new GUITextBlock(new RectTransform(new Vector2(1f, 0.3f), titleComponent.RectTransform),
                             text: TextManager.Get($"TechnologyTier.{tier.Identifier}").Fallback(tier.Identifier.Value),
                             font: GUIStyle.SubHeadingFont, textAlignment: Alignment.CenterLeft)
            {
                TextColor = GUIStyle.TextColorBright,
                Shadow = true
            };

            GUITextBlock requirementLabel = new GUITextBlock(new RectTransform(new Vector2(1f, 0.7f), titleComponent.RectTransform),
                             text: GetTierLabel(tier, ResearchManager.IsTierUnlocked(tier.Identifier)), textAlignment: Alignment.TopLeft, font: GUIStyle.Font)
            {
                TextColor = GUIStyle.TextColorNormal,
                TextOutlineColor = Color.Black * 0.33f,
            };
            titleComponent.Recalculate();

            float titleRelativeSize = TitleSize / (float)prefabLayout.RectTransform.Rect.Height;

            float size = count / (float)layout.MaxTierCounts.Values.Sum() - titleRelativeSize;
            GUIFrame tierFrame = new GUIFrame(new RectTransform(new Vector2(1f, size), prefabLayout.RectTransform), style: null)
            {
                UserData = tier,
                CanBeFocused = false
            };

            GUILayoutGroup bodyComponent = new GUILayoutGroup(new RectTransform(Vector2.One, tierFrame.RectTransform, anchor: Anchor.BottomLeft), isHorizontal: true);

            tierBuilder.Add(new TierComponent(tier, titleComponent, requirementLabel, tierFrame));

            foreach (TechnologyCategory category in tree.Categories)
            {
                GUILayoutGroup categoryTierFrame = new GUILayoutGroup(new RectTransform(new Vector2(1f / tree.Categories.Length, 1f), bodyComponent.RectTransform), childAnchor: Anchor.TopCenter);

                GUILayoutGroup padding = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 1f), categoryTierFrame.RectTransform))
                {
                    UserData = category.Identifier,
                };
                relativeSizes[padding] = 1f / count;
                technologyPositions[(category.Identifier, tier.Identifier)] = padding;
            }
        }

        tierComponents = tierBuilder.ToImmutable();

        var prefabBuilder = ImmutableDictionary.CreateBuilder<TechnologyPrefab, GUIButton>();
        foreach (var ((tileTier, tileCategory), tiles) in layout.LayoutTiles)
        {
            if (!technologyPositions.TryGetValue((tileCategory.Identifier, tileTier.Identifier), out GUIComponent? category)) { continue; }

            foreach (Tile tile in tiles)
            {
                float height = relativeSizes.GetValueOrDefault(category, 1f);

                GUILayoutGroup prefabContainer = new GUILayoutGroup(new RectTransform(new Vector2(1f, height), category.RectTransform), childAnchor: Anchor.TopCenter);

                GUILayoutGroup padding = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.66f), prefabContainer.RectTransform));

                if (tile.TryGetPrefab(out TechnologyPrefab? prefab))
                {
                    GUIButton frame = CreatePrefabFrame(prefab, padding.RectTransform);
                    prefabBuilder.Add(prefab, frame);
                }
            }
        }

        backgroundLayout.Recalculate();
        prefabLayout.ForceLayoutRecalculation();
        scrollArea.Recalculate();
        PrefabComponents = prefabBuilder.ToImmutable();
        ApplyButtonActions();

        if (lineManager != null)
        {
            lineManager.Recalculate();
        }
        else
        {
            lineManager = new TechUILineManager(layout, tree, this);
        }

        return;

        void DrawBackground(SpriteBatch spriteBatch, GUICustomComponent parent)
        {
            lineManager?.Draw(spriteBatch);
        }

        void DrawForeground(SpriteBatch spriteBatch, GUICustomComponent parent)
        {
            for (int i = 0; i < tierComponents.Length; i++)
            {
                var tier = tierComponents[i];

                Rectangle rect = new Rectangle(tier.Parent.Rect.Location, tier.Title.Rect.Size)
                {
                    Y = scrollArea.ScrollAreaRect.Y,
                    X = scrollArea.ScrollAreaRect.X,
                };
                rect.Y = Math.Clamp(rect.Y, min: tier.Parent.Rect.Top - rect.Height, max: i + 1 < tierComponents.Length ? tier.Parent.Rect.Bottom - tier.Title.Rect.Height : int.MaxValue);
                Point offset = tier.Title.Parent.Rect.Location - rect.Location;
                offset.Y = -offset.Y;
                offset.X = -offset.X;

                rect.Width = scrollArea.ScrollAreaRect.Width;
                tier.Title.RectTransform.AbsoluteOffset = offset;
                tier.Title.RectTransform.Resize(new Point(rect.Width, rect.Height));
                var oldEffect = spriteBatch.GetCurrentEffect();
                spriteBatch.SwapEffect(GameMain.GameScreen.GradientEffect, new Dictionary<string, object>
                {
                    { "color1", Color.Transparent.ToVector4() },
                    { "color2", (tier.Tier.Color * 0.8f).ToVector4() },
                    { "midPoint", 0.5f },
                    { "fadeDist", 1f }
                });

                GUI.DrawRectangle(spriteBatch, tier.Title.Rect, tier.Tier.Color, isFilled: true);
                spriteBatch.SwapEffect(oldEffect);
                // have to do this jank because elements inside GUIScissorComponent are drawn manually already,
                // so we can't prevent the GUI elements from drawing twice
                tier.Title.Visible = true;
                tier.Title.DrawManually(spriteBatch, alsoChildren: true);
                tier.Title.Visible = false;
            }

            // uncomment if pathfinding ever breaks
            //lineManager?.DrawDebug(spriteBatch, parent.Rect.Location.ToVector2(), size: 16f);
        }
    }

    private static RichString GetTierLabel(TechnologyTier tier, bool isUnlocked)
        => RichString.Rich(TextManager.GetWithVariable("technologytier.requirement", "[requirement]", $"‖color:{(isUnlocked ? "gui.green" : "gui.red")}‖{tier.UnlockText}‖color:end‖"));

    private void ApplyButtonActions()
    {
        foreach (var (prefab, button) in PrefabComponents)
        {
            button.OnClicked = (prefabButton, _) =>
            {
                ClearTooltip();
                prefabButton.Selected = true;
                CreateTooltip(prefab);

                return true;
            };
        }
    }

    public void OnOpened()
    {
        // Requirements may have changed while the UI was closed (e.g. campaign metadata set by an event), and nothing else triggers a refresh for those
        RecreateFramesAndTierLabels();
        if (selectedNodeTooltip.UserData is TechnologyPrefab prefab)
        {
            // Recreate tooltip
            CreateTooltip(prefab);
        }
    }

    private void ClearTooltip()
    {
        selectedNodeTooltip.UserData = null;
        selectedNodeTooltip.Visible = false;
        foreach (GUIButton otherButton in PrefabComponents.Values)
        {
            otherButton.Selected = false;
        }
    }

    /// <summary>
    /// Highlights the lines from the prerequisites of the selected technology (red for the ones blocking the research, green for the finished ones),
    /// and optionally flashes the prerequisites in the same colors (they can be far from the selected one in the tree).
    /// </summary>
    private void RefreshPrerequisiteHighlights(bool flash)
    {
        if (selectedPrefab == null)
        {
            lineManager?.ClearHighlightedLines();
            return;
        }

        // Finished first, so blocking ones take precedence if they share a line (grouped prefabs)
        List<(TechnologyPrefab Prerequisite, Color Color)> highlightedPrefabs =
        [
            ..GetFinishedPrerequisites(selectedPrefab).Select(static p => (p, GUIStyle.Green)),
            ..GetBlockingPrerequisites(selectedPrefab).Select(static p => (p, GUIStyle.Red))
        ];
        lineManager?.HighlightLines(selectedPrefab, highlightedPrefabs.Select(static h => (h.Prerequisite, h.Color * 0.7f)));

        if (!flash) { return; }
        foreach (var (prerequisite, color) in highlightedPrefabs)
        {
            if (PrefabComponents.TryGetValue(prerequisite, out GUIButton? prerequisiteButton))
            {
                prerequisiteButton.GetAllChildren<GUIFrame>().ForEach(c => c.Flash(color));
            }
        }
    }

    /// <summary>
    /// Returns the finished technologies in the prerequisites (including finished options of "or" prerequisites).
    /// </summary>
    private static IEnumerable<TechnologyPrefab> GetFinishedPrerequisites(TechnologyPrefab prefab)
    {
        foreach (TechnologyPrefab.TechnologyPrerequisite prerequisite in prefab.Prerequisites)
        {
            foreach (Identifier identifier in prerequisite.Identifiers)
            {
                if (!Manager.IsFinished(identifier)) { continue; }
                if (TechnologyPrefab.Prefabs.TryGet(identifier, out TechnologyPrefab? prereqPrefab))
                {
                    yield return prereqPrefab;
                }
            }
        }
    }

    /// <summary>
    /// Returns the unfinished technologies in the prerequisites that aren't met yet.
    /// With "or" prerequisites, all of the unfinished options are returned.
    /// </summary>
    private static IEnumerable<TechnologyPrefab> GetBlockingPrerequisites(TechnologyPrefab prefab)
    {
        foreach (TechnologyPrefab.TechnologyPrerequisite prerequisite in prefab.Prerequisites)
        {
            if (prerequisite.Matches(Manager)) { continue; }
            foreach (Identifier identifier in prerequisite.Identifiers)
            {
                if (Manager.IsFinished(identifier)) { continue; }
                if (TechnologyPrefab.Prefabs.TryGet(identifier, out TechnologyPrefab? prereqPrefab))
                {
                    yield return prereqPrefab;
                }
            }
        }
    }

    private void CreateTooltip(TechnologyPrefab prefab)
    {
        selectedNodeTooltip.UserData = prefab;
        selectedNodeTooltip.Visible = true;
        selectedNodeTooltip.ClearChildren();

        new GUIFrame(
            new RectTransform(ToolBox.PaddingSizeParentRelative(selectedNodeTooltip.RectTransform, 1.25f), selectedNodeTooltip.RectTransform, Anchor.Center),
            style: "OuterGlow", color: Color.Black)
        {
            UserData = "outerglow",
            CanBeFocused = false
        };

        GUIFrame bgFrame = new GUIFrame(new RectTransform(Vector2.One, selectedNodeTooltip.RectTransform));

        GUILayoutGroup layout = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), bgFrame.RectTransform, anchor: Anchor.Center), childAnchor: Anchor.TopCenter)
        {
            Stretch = true,
            RelativeSpacing = 0.05f
        };

        bool isHidden = prefab.IsHidden();

        new GUITextBlock(new RectTransform(new Vector2(1f, 0f), layout.RectTransform), text: isHidden ? TechnologyPrefab.HiddenText : prefab.Name, font: GUIStyle.LargeFont);

        new GUITextBlock(new RectTransform(new Vector2(1f, 0.25f), layout.RectTransform),
                                     text: isHidden ? TechnologyPrefab.HiddenText : prefab.Description, textAlignment: Alignment.TopLeft)
        {
            AutoScaleVertical = true,
            Wrap = true
        };

        GUILayoutGroup prereqLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.35f), layout.RectTransform))
        {
            RelativeSpacing = 0.1f,
        };

        if (!prefab.UnlockedByDefault)
        {
            new GUITextBlock(new RectTransform(new Vector2(1f, 0f), prereqLayout.RectTransform), TextManager.Get("technologyprefab.prerequirement"));

            // If hidden, only show the metadata requirement that's hiding the technology, so the player knows how to reveal it.
            // The other requirements (incl. the metadata requirements of the technologies hidden because of a hidden prerequisite) could be spoilers.
            if (!prefab.MetadataRequirement.IsEmpty && (!isHidden || prefab.IsHiddenByOwnMetadataRequirement))
            {
                var metadataText = new GUITextBlock(new RectTransform(new Vector2(1f, 0f), prereqLayout.RectTransform), text: TextManager.GetWithVariable("listformat", "[item]", prefab.MetadataRequirementName))
                {
                    TextColor = prefab.CheckMetadataRequirement()
                                    ? GUIStyle.Green
                                    : GUIStyle.Red,
                    ToolTip = prefab.MetadataRequirementTooltip,
                    OverflowClip = true,
                    Wrap = true
                };

                metadataText.SetTextPos();

                metadataText.RectTransform.MinSize = new Point(0, (int)metadataText.TextSize.Y);
            }

            if (isHidden)
            {
                new GUITextBlock(new RectTransform(new Vector2(1f, 0f), prereqLayout.RectTransform), text: TextManager.GetWithVariable("listformat", "[item]", TechnologyPrefab.HiddenText))
                {
                    TextColor = GUIStyle.Red,
                    OverflowClip = true
                };
            }
            else if (originalTechTree.Tiers.FirstOrNull(t => t.Identifier == prefab.Tier) is { } tier)
            {
                new GUITextBlock(new RectTransform(new Vector2(1f, 0f), prereqLayout.RectTransform), text: TextManager.GetWithVariable("listformat", "[item]", tier.UnlockText))
                {
                    TextColor = ResearchManager.IsTierUnlocked(tier.Identifier)
                                    ? GUIStyle.Green
                                    : GUIStyle.Red,
                    OverflowClip = true
                };
            }

            foreach (var prerequisite in isHidden ? [] : prefab.Prerequisites)
            {
                new GUITextBlock(new RectTransform(new Vector2(1f, 0f), prereqLayout.RectTransform), text: RichString.Rich(TextManager.GetWithVariable("listformat", "[item]", CreatePrerequisiteLine(prerequisite))))
                {
                    TextColor = prerequisite.Matches(Manager)
                                    ? GUIStyle.Green
                                    : GUIStyle.Red,
                    OverflowClip = true
                };
            }
        }

        GUILayoutGroup progressBarLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.25f), layout.RectTransform));

        if (Manager.ActiveResearch is { } active && active.Prefab == prefab)
        {
            GUILayoutGroup progressTextLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.5f), progressBarLayout.RectTransform), isHorizontal: true);
            new GUITextBlock(new RectTransform(new Vector2(0.5f, 1f), progressTextLayout.RectTransform), text: TextManager.Get("researchprogress"), textAlignment: Alignment.CenterLeft)
            {
                Padding = Vector4.Zero
            };

            LocalizedString progressText = TextManager.GetWithVariables("upgrades.progressformat", ("[level]", active.Progress.ToString()), ("[maxlevel]", prefab.Cost.ToString()));
            new GUITextBlock(new RectTransform(new Vector2(0.5f, 1f), progressTextLayout.RectTransform), text: progressText, textAlignment: Alignment.CenterRight)
            {
                Padding = Vector4.Zero
            };

            new GUIProgressBar(new RectTransform(new Vector2(1f, 0.5f), progressBarLayout.RectTransform), barSize: active.Progress / (float)prefab.Cost, color: active.IsFinished ? GUIStyle.Green : GUIStyle.Orange);

            new GUIButton(new RectTransform(new Vector2(0.4f, 0f), layout.RectTransform), text: TextManager.Get(Manager.IsFinished(prefab) ? "finalizeresearch" : "cancel"), style: "GUIButtonSmall")
            {
                Enabled = HasPermission,
                OnClicked = (_, _) =>
                {
                    if (!HasPermission) { return false; }
                    if (Manager.ActiveResearch is { IsFinished: true })
                    {
                        Manager.ClearActiveResearch();
                        RecreateFrames();
                        Manager.ClientSync();
                        OutpostManagementUI.Instance?.OutpostOverviewUI?.Refresh();
                    }
                    else
                    {
                        GUI.AskForConfirmation(TextManager.Get("warning"), TextManager.Get("technology.cancelresearchwarning"), onConfirm: () =>
                        {
                            Manager.ClearActiveResearch();
                            RecreateFrames();
                            Manager.ClientSync();
                            OutpostManagementUI.Instance?.OutpostOverviewUI?.Refresh();
                        });
                    }

                    return true;
                }
            };
        }
        else
        {
            LocalizedString text = TextManager.Get(Manager.IsFinished(prefab) ? "researchfinished" : "researchbutton");
            GUIButton researchButton = new GUIButton(new RectTransform(new Vector2(0.4f, 0f), layout.RectTransform), text, style: "GUIButtonSmall")
            {
                Enabled = HasPermission && !Manager.IsFinished(prefab),
                OnClicked = (_, _) =>
                {
                    if (!HasPermission) { return false; }

                    if (Manager.ActiveResearch is null || Manager.ActiveResearch.IsFinished)
                    {
                        Manager.SelectActiveResearch(prefab);
                        RecreateSingle(prefab);
                        Manager.ClientSync();
                        OutpostManagementUI.Instance?.OutpostOverviewUI?.Refresh();
                    }
                    else
                    {
                        GUI.AskForConfirmation(TextManager.Get("warning"), TextManager.Get("technology.activeresearchwarning"), onConfirm: () =>
                        {
                            Manager.SelectActiveResearch(prefab);
                            RecreateFrames();
                            Manager.ClientSync();
                            OutpostManagementUI.Instance?.OutpostOverviewUI?.Refresh();
                        });
                    }

                    return true;
                }
            };

            if (!Manager.HasAllPrerequisites(prefab))
            {
                researchButton.Enabled = false;
            }
        }
        return;

        static RichString CreatePrerequisiteLine(TechnologyPrefab.TechnologyPrerequisite prereq)
        {
            string tag = $"technologyprefab.prerequirement.{prereq.Comparison}";

            if (prereq.Identifiers.Length == 1)
            {
                return TextManager.Get($"technologyname.{prereq.Identifiers.First()}");
            }

            RichString current = TextManager.Get($"technologyname.{prereq.Identifiers.First()}");
            foreach (Identifier identifier in prereq.Identifiers.Skip(1))
            {
                current = TextManager.GetWithVariables(tag, ("[value1]", current), ("[value2]", TextManager.Get($"technologyname.{identifier}")));
            }

            return current;
        }
    }

    public void RecreateFrames()
    {
        var prefabBuilder = ImmutableDictionary.CreateBuilder<TechnologyPrefab, GUIButton>();
        foreach (var (prefab, button) in PrefabComponents)
        {
            GUIButton newButton = CreatePrefabFrame(prefab, button.Parent.RectTransform);
            newButton.Selected = button.Selected;
            if (button.Selected)
            {
                CreateTooltip(prefab);
            }

            prefabBuilder.Add(prefab, newButton);
        }

        PrefabComponents = prefabBuilder.ToImmutable();
        ApplyButtonActions();
        //the research state may have changed
        RefreshPrerequisiteHighlights(flash: false);
    }

    private void RecreateSingle(TechnologyPrefab targetPrefab)
    {
        foreach (var (prefab, button) in PrefabComponents)
        {
            if (prefab != targetPrefab) { continue; }

            GUIButton newButton = CreatePrefabFrame(targetPrefab, button.Parent.RectTransform);
            newButton.Selected = button.Selected;

            PrefabComponents = PrefabComponents
                               .Remove(prefab)
                               .Add(targetPrefab, newButton);
            ApplyButtonActions();
            if (button.Selected)
            {
                CreateTooltip(targetPrefab);
            }
            RefreshPrerequisiteHighlights(flash: false);

            return;
        }
    }

    private static GUIButton CreatePrefabFrame(TechnologyPrefab prefab, RectTransform parent)
    {
        parent.ClearChildren();
        GUIButton mainFrame = new GUIButton(new RectTransform(Vector2.One, parent), style: null)
        {
            UserData = prefab
        };

        Color frameColor = GUIStyle.Orange;
        bool isFinished = Manager.IsFinished(prefab);
        if (isFinished)
        {
            frameColor = GUIStyle.Green;
        }
        else
        {
            if (!Manager.HasAllPrerequisites(prefab))
            {
                frameColor = GUIStyle.Red;
            }
        }

        // Same style as the workstation panels in the dweller UI
        Color panelOuterColor = Color.Lerp(frameColor, Color.Black, 0.5f);
        Color panelInnerColor = new Color(18, 29, 25);//Color.Lerp(GUIStyle.Blue, Color.Black, 0.75f);

        // Button propagates its state to direct children, so all the state colors need to be set
        GUIFrame outlineFrame = new GUIFrame(new RectTransform(Vector2.One, mainFrame.RectTransform),
            style: null)
        {
            CanBeFocused = false,
            Color = panelInnerColor,
            SelectedColor = panelInnerColor,
            PressedColor = panelInnerColor,
            HoverColor = panelInnerColor,
            DisabledColor = panelInnerColor,
            OutlineColor = panelOuterColor,
            OutlineThickness = 1
        };
        GUIFrame headerFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.15f), outlineFrame.RectTransform),
            style: null)
        {
            CanBeFocused = false,
            Color = panelOuterColor
        };
        GUIFrame bodyFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.85f), outlineFrame.RectTransform, Anchor.BottomLeft),
            style: null)
        {
            CanBeFocused = false
        };

        // glow frame
        new GUIFrame(new RectTransform(Vector2.One, mainFrame.RectTransform), style: "InnerGlow")
        {
            SelectedColor = GUIStyle.Green * 0.8f,
            HoverColor = GUIStyle.Green * 0.8f,
            PressedColor = GUIStyle.Green * 0.8f,
            Color = Color.Transparent,
            DisabledColor = Color.Transparent,
            CanBeFocused = false
        };

        GUILayoutGroup headerLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.98f, 1f), headerFrame.RectTransform), isHorizontal: true)
        {
            Stretch = true
        };

        bool isHidden = prefab.IsHidden();

        var technologyText = new GUITextBlock(new RectTransform(new Vector2(1f, 1f), headerLayout.RectTransform),
                         text: isHidden ? TechnologyPrefab.HiddenText : TextManager.Get($"TechnologyName.{prefab.Identifier}").Fallback(prefab.Identifier.Value),
                         font: GUIStyle.SubHeadingFont, textAlignment: Alignment.CenterLeft, textColor: isFinished ? GUIStyle.TextColorNormal : Color.LightGray)
        {
            AutoScaleHorizontal = true
        };

        GUITextBlock rpCostText = new GUITextBlock(new RectTransform(new Vector2(0f, 1f), headerLayout.RectTransform, Anchor.CenterRight),
                                                   text: TextManager.GetWithVariable("researchpoints", "[amount]", prefab.Cost.ToString()),
                                                   font: GUIStyle.SubHeadingFont, textAlignment: Alignment.Center, textColor: isFinished ? GUIStyle.TextColorNormal : Color.LightGray)
        {
            ToolTip = TextManager.Get("resource.researchpoints.description")
        };

        GUILayoutGroup bodyLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.85f), bodyFrame.RectTransform, anchor: Anchor.TopCenter), childAnchor: Anchor.BottomLeft);

        GUILayoutGroup innerBodyLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.95f), bodyLayout.RectTransform), childAnchor: Anchor.TopCenter)
        {
            Stretch = true,
        };

        GUILayoutGroup unlockLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.85f), innerBodyLayout.RectTransform))
        {
            Stretch = true,
        };

        GUIListBox unlockListBox = new GUIListBox(new RectTransform(Vector2.One, unlockLayout.RectTransform), style: null);

        if (!prefab.UnlockTexts.Any())
        {
            DebugConsole.ThrowError($"{nameof(TechnologyPrefab)} \"{prefab.Identifier}\" does not list any unlock texts.");
        }

        foreach (LocalizedString unlock in isHidden ? [TechnologyPrefab.HiddenText] : prefab.UnlockTexts)
        {
            var unlockText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0f), unlockListBox.Content.RectTransform),
                             // No list formatting for the hidden text, a bullet point on a single "???" looks odd
                             text: RichString.Rich(isHidden ? unlock : TextManager.GetWithVariable("listformat", "[item]", unlock)), textAlignment: Alignment.CenterLeft, font: GUIStyle.SmallFont,
                         textColor: isFinished ? GUIStyle.Blue : Color.LightGray, wrap: true)
            {
                Padding = Vector4.Zero,
                CanBeFocused = false
            };
            unlockText.RectTransform.SizeChanged += () =>
            {
                unlockText.RectTransform.MaxSize = new Point(int.MaxValue, (int)unlockText.TextSize.Y);
            };
        }

        if (!isHidden && prefab.UnlockTexts.Length > 0 && unlockListBox.Content.Children.Max(static c => c.Rect.Bottom) > unlockListBox.Rect.Bottom)
        {
            StringBuilder sb = new();
            foreach (LocalizedString unlock in prefab.UnlockTexts)
            {
                sb.AppendLine(TextManager.GetWithVariable("listformat", "[item]", unlock).Value);
            }

            unlockListBox.ToolTip = RichString.Rich($"‖color:gui.blue‖{sb.ToString().TrimEnd('\n')}‖color:end‖");
        }

        if (Manager.ActiveResearch is { } active)
        {
            if (active.Prefab == prefab)
            {
                rpCostText.Text = TextManager.Get("researchinprogress");
                rpCostText.ToolTip = LocalizedString.EmptyString;
                rpCostText.TextColor = GUIStyle.Green;
                GUIProgressBar progress = new GUIProgressBar(new RectTransform(new Vector2(0.9f, 0.15f), innerBodyLayout.RectTransform), barSize: active.Progress / (float)prefab.Cost, color: active.IsFinished ? GUIStyle.Green : GUIStyle.Orange);
                var progressText = new GUITextBlock(new RectTransform(Vector2.One, progress.RectTransform), text: TextManager.GetWithVariables("technologyui.technologyprogress", ("[value1]", active.Progress.ToString()), ("[value2]", prefab.Cost.ToString())), textAlignment: Alignment.Center)
                {
                    Shadow = true
                };
                //remove padding from the left side to create more space for the texts
                progressText.Padding = new Vector4(0, progressText.Padding.Y, progressText.Padding.Z, progressText.Padding.W);

                if (active.IsFinished)
                {
                    progress.Color = GUIStyle.Green;
                }
            }
        }

        if (Manager.IsFinished(prefab))
        {
            rpCostText.Text = TextManager.Get("researchfinished");
            rpCostText.ToolTip = LocalizedString.EmptyString;
            rpCostText.TextColor = GUIStyle.Green;
        }

        rpCostText.RectTransform.MinSize = new Point((int)rpCostText.TextSize.X + GUI.IntScale(10f), 0);

        mainFrame.ForceLayoutRecalculation();

        technologyText.SetTextPos();
        rpCostText.SetTextPos();

        return mainFrame;
    }

    private GUIFrame CreateCategoryFrame(LocalizedString name, Color color, RectTransform rectTransform, Sprite? sprite)
    {
        Point padding = PaddingFromFloat(CategoryPadding);

        Dictionary<string, object> spriteGradientParams = new()
        {
            { "color1", Color.Transparent.ToVector4() },
            { "color2", (Color.White with { A = 100 }).ToVector4() },
            { "midPoint", 0.75f },
            { "fadeDist", 1.0f }
        };

        Dictionary<string, object> textGradientParams = new()
        {
            { "color1", Color.Transparent.ToVector4() },
            { "color2", Color.Black.ToVector4() },
            { "midPoint", 0.5f },
            { "fadeDist", 1f }
        };

        Vector2 textSize = GUIStyle.LargeFont.MeasureString(name);

        GUIScissorComponent scissor = new GUIScissorComponent(rectTransform)
        {
            CanBeFocused = false,
        };
        GUICustomComponent categoryFrame = new GUICustomComponent(new RectTransform(Vector2.One, scissor.Content.RectTransform), onDraw: (spriteBatch, component) =>
        {
            var oldEffect = spriteBatch.GetCurrentEffect();
            GUI.DrawFilledRectangle(spriteBatch, component.Rect, color * 0.1f);
            if (sprite != null)
            {
                spriteBatch.SwapEffect(GameMain.GameScreen.GradientEffect, spriteGradientParams);
                Vector2 location = new Vector2(component.Rect.Center.X, scrollArea.ScrollAreaRect.Top);
                sprite.Draw(spriteBatch, location, Color.White, scale: Math.Min(component.Rect.Width / (sprite.SourceRect.Width / 2f), component.Rect.Height / (sprite.SourceRect.Height / 2f)));
            }

            spriteBatch.SwapEffect(GameMain.GameScreen.GradientEffect, textGradientParams);
            GUI.DrawFilledRectangle(spriteBatch, new Rectangle(component.Rect.X, component.Rect.Y, component.Rect.Width, (int)textSize.Y * 2), Color.White);

            spriteBatch.SwapEffect(oldEffect);
        })
        {
            CanBeFocused = false
        };
        GUITextBlock categoryTitle = new(new RectTransform(Vector2.UnitX, categoryFrame.RectTransform) { AbsoluteOffset = new Point(0, padding.Y / 2) },
                                         name, font: GUIStyle.LargeFont, textAlignment: Alignment.Center);
        return new GUIFrame(new RectTransform(categoryFrame.Rect.Size - new Point(0, categoryTitle.Rect.Height) - padding, categoryFrame.RectTransform,
                                              Anchor.BottomCenter) { AbsoluteOffset = new Point(0, padding.Y / 2) }, style: null)
        {
            CanBeFocused = false
        };
    }

    private bool scrollWithLeftClick;

    /// <summary>
    /// The selected technology, whose prerequisites are highlighted
    /// </summary>
    private TechnologyPrefab? selectedPrefab;

    public void Update(float f, GUICustomComponent guiCustomComponent)
    {
        scrollArea.AllowScroll = !PlayerInput.MidButtonHeld() && GUI.MouseOn is not GUIListBox { ScrollBarVisible: true };

        TechnologyPrefab? newSelectedPrefab = selectedNodeTooltip.UserData as TechnologyPrefab;
        if (newSelectedPrefab != selectedPrefab)
        {
            selectedPrefab = newSelectedPrefab;
            RefreshPrerequisiteHighlights(flash: true);
        }

        if (PlayerInput.PrimaryMouseButtonDown())
        {
            scrollWithLeftClick = scrollArea.ScrollAreaRect.Contains(PlayerInput.MousePosition) && !GUI.IsMouseOn(selectedNodeTooltip);
            foreach (GUIButton btn in PrefabComponents.Values)
            {
                if (GUI.IsMouseOn(btn))
                {
                    scrollWithLeftClick = false;
                    break;
                }
            }
        }

        if (PlayerInput.MidButtonHeld() || (scrollWithLeftClick && PlayerInput.PrimaryMouseButtonHeld()))
        {
            scrollArea.ScrollArea(PlayerInput.MouseSpeed);
        }

        if (PlayerInput.PrimaryMouseButtonClicked() && !GUI.IsMouseOn(selectedNodeTooltip))
        {
            ClearTooltip();
        }
    }

    public bool HasTechnology(Identifier technologyPrefabIdentifier)
    {
        return PrefabComponents.Keys.Any(techPrefab => techPrefab.Identifier == technologyPrefabIdentifier);
    }

    public void FocusOnTechnology(Identifier technologyPrefabIdentifier)
    {
        if (!TechnologyPrefab.Prefabs.TryGet(technologyPrefabIdentifier, out TechnologyPrefab? prefab))
        {
            DebugConsole.ThrowError($"Could not find technology prefab with identifier {technologyPrefabIdentifier} in {nameof(FocusOnTechnology)}");
            return;
        }
        FocusOnTechnology(prefab);
    }

    public void FocusOnTechnology(TechnologyPrefab prefab)
    {
        if (!PrefabComponents.TryGetValue(prefab, out GUIButton? element))
        {
            DebugConsole.ThrowError($"Could not find GUI element for technology prefab {prefab.Identifier} in {nameof(FocusOnTechnology)}");
            return;
        }
        scrollArea.ScrollToElement(element, duration: 0.5f);
        element.GetAllChildren<GUIFrame>().ForEach(c => c.Flash(GUIStyle.Green));
    }

    private static Point PaddingFromFloat(float padding)
        => new Point(GUI.IntScale(padding * 2f));

    public void Dispose()
    {
        GameMain.Client?.OnPermissionChanged.TryDeregister(EventIdentifier);
    }
}