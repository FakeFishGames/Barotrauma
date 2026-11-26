using Barotrauma;
using Barotrauma.Extensions;
using Barotrauma.Plugins;
using Microsoft.Xna.Framework;

namespace HomeAndHarbor
{
    [RunConstructorOnInit]
    public static class OutpostMapTooltip
    {
        static OutpostMapTooltip()
            => HookService.RegisterHook<PluginCreateLocationInfoOverlayDelegate>(CreateLocationInfoOverlay);

        private static void CreateLocationInfoOverlay(Location location, GUIComponent parent)
        {
            if (location.GetExtraField<PersistentOutpostData>(nameof(PersistentOutpostData)) is not { } outpostData) { return; }
            if (outpostData.ResourceManager == null) { return; }
            if (GameMain.GameSession.Map is not { } map) { return; }

            if (outpostData.ResourceManager.Resources.All(r => r.Hidden)) { return; }

            GUILayoutGroup content = parent.GetChild<GUILayoutGroup>();

            if (content is null) { return; }

            if (!location.HasOutpost()) { return; }

            GUITextBlock nameText = content.GetChild<GUITextBlock>();
            GUIButton logButton = new(new RectTransform(new Vector2(0.15f, 0.1f), content.RectTransform, Anchor.TopRight), style: "ResourceLogButton")
            {
                ToolTip = TextManager.Get("economylog.title"),
                UserData = outpostData,
                IgnoreLayoutGroups = true,
                OnClicked = (button, o) =>
                {
                    OutpostResourceLogUI.CreateOrCloseUI((PersistentOutpostData)o, parent);
                    return true;
                },
                OnAddedToGUIUpdateList = component => component.RectTransform.AbsoluteOffset = component.Rect.Width > content.Rect.Width - nameText.TextSize.X ? new Point(0, nameText.Rect.Height) : Point.Zero
            };

            if (outpostData.IsPlayerOwned)
            {
                GUIButton btn = new GUIButton(new RectTransform(new Vector2(0.5f, 0.1f), content.RectTransform)
                {
                    MinSize = new Point(15, 15)
                }, TextManager.Get("PlayerOwnedOutpost"), style: "GUIButtonPlayerOwned")
                {
                    CanBeSelected = false,
                    CanBeFocused = false,
                    Enabled = outpostData.IsPlayerOwned,
                };
                btn.SetAsFirstChild();
            }

            GUILayoutGroup lineLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.1f), content.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                Stretch = true
            };
            GUITextBlock outpostStatusBlock = new GUITextBlock(new RectTransform(Vector2.Zero, lineLayout.RectTransform), TextManager.Get("OutpostStatus"), font: GUIStyle.SubHeadingFont)
            {
                TextColor = GUIStyle.Green
            };
            float outpostStatusBlockTextWidth = outpostStatusBlock.Font.MeasureString(outpostStatusBlock.Text).X + outpostStatusBlock.Padding.X + outpostStatusBlock.Padding.Z;
            outpostStatusBlock.RectTransform.MinSize = new Point((int)MathF.Ceiling(outpostStatusBlockTextWidth), 0);
            new GUIImage(new RectTransform(Vector2.One, lineLayout.RectTransform), style: "HorizontalLine");

            GUIFrame resourceFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.4f), content.RectTransform), style: null);
            GUIListBox resourceList = new GUIListBox(new RectTransform(Vector2.One, resourceFrame.RectTransform), style: null)
            {
                UseGridLayout = true,
                CanBeFocused = false
            };

            foreach (var (trackedResource, displayedResource) in outpostData.ResourcesToDisplayInUIs)
            {
                GUIFrame frame = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.33f), resourceList.Content.RectTransform), style: null);
                GUILayoutGroup layout = new GUILayoutGroup(new RectTransform(ToolBox.PaddingSizeParentRelative(frame.RectTransform, 0.9f), frame.RectTransform, Anchor.Center), isHorizontal: true, childAnchor: Anchor.CenterLeft)
                {
                    Stretch = true
                };

                RichString toolTip =
                    RichString.Rich($"‖color:gui.textcolorbright‖{displayedResource.DisplayName}‖color:end‖\n{displayedResource.GetAmountText(withNumber: true)}");

                new GUIImage(new RectTransform(Vector2.One, layout.RectTransform, scaleBasis: ScaleBasis.BothHeight), displayedResource.Prefab.Sprite, scaleToFit: true)
                {
                    ToolTip = toolTip
                };

                bool hasMax = displayedResource.MaxAmount < float.MaxValue;
                float max = hasMax ? displayedResource.MaxAmount : GetMaxAMountFromText(displayedResource);

                bool isUnknown = displayedResource.DisplayedAmount < 0f;

                float normalized = isUnknown ? 0 : MathUtils.InverseLerp(0f, max, displayedResource.Amount);

                GUIProgressBar progress = new GUIProgressBar(new RectTransform(new Vector2(1f, 0.8f), layout.RectTransform), barSize: normalized)
                {
                    Color = ToolBox.GradientLerp(normalized, GUIStyle.Red, GUIStyle.Orange, GUIStyle.Green)
                };

                LocalizedString label = "?";
                if (!isUnknown)
                {
                    label = hasMax
                                ? TextManager.GetWithVariables("upgrades.progressformat", ("[level]", ((int)MathF.Round(displayedResource.Amount)).ToString()), ("[maxlevel]", ((int)MathF.Round(displayedResource.MaxAmount)).ToString()))
                                : $"{MathF.Round(displayedResource.Amount)}";
                }

                new GUITextBlock(new RectTransform(Vector2.One, progress.RectTransform, Anchor.Center), text: label, textAlignment: Alignment.Center)
                {
                    TextOutlineColor = Color.Black,
                    ToolTip = toolTip
                };
                continue;

                static float GetMaxAMountFromText(Resource resource)
                {
                    if (resource.Prefab.HasSoftMaxValueInTooltip)
                    {
                        if (resource.Prefab.SoftMaxValueInTooltip.TryGet(out float f))
                        {
                            return f;
                        }
                        else if (resource.Prefab.SoftMaxValueInTooltip.TryGet(out Identifier i) && !i.IsEmpty)
                        {
                            if (resource.Manager.GetResource(i) is { } res)
                            {
                                return res.Amount;
                            }
                            DebugConsole.ThrowError($"Error in SoftMaxValueInTooltip for \"{resource.Identifier}\": \"{i}\" resource not found.");
                        }
                    }

                    int count = resource.Prefab.AmountTexts.Length;
                    if (count is 0 or 1)
                    {
                        return 100;
                    }

                    float[] texts = resource.Prefab.AmountTexts
                                            .Select(static at => at.minAmount)
                                            .OrderBy(static f => f)
                                            .ToArray();
                    float total = 0;
                    for (int i = 1; i < texts.Length; i++)
                    {
                        total += (texts[i - 1] - texts[i]);
                    }

                    return texts.Last() + (total / count);
                }
            }

            GUIListBox statusIndicatorList = new GUIListBox(new RectTransform(new Vector2(1f, 0.25f), content.RectTransform), style: null, isHorizontal: true)
            {
                ScrollBarVisible = false,
                AutoHideScrollBar = true,
                KeepSpaceForScrollBar = true
            };

            bool hadStatus = false;
            foreach (var statusIndicator in outpostData.ResourceManager.GetActiveResourceStatusIndicators())
            {
                if (statusIndicator.Hidden) { continue; }
                hadStatus = true;
                GUIFrame frame = new GUIFrame(new RectTransform(Vector2.One, statusIndicatorList.Content.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: null)
                {
                    ToolTip = RichString.Rich($"‖color:gui.textcolorbright‖{statusIndicator.Name}‖color:end‖\n{statusIndicator.Description}")
                };
                new GUIImage(new RectTransform(new Vector2(0.9f), frame.RectTransform, scaleBasis: ScaleBasis.BothHeight, anchor: Anchor.Center), statusIndicator.Icon, scaleToFit: true)
                {
                    Color = statusIndicator.IconColor,
                    HoverColor = Color.Lerp(statusIndicator.IconColor, Color.White, 0.5f),
                    SelectedColor = statusIndicator.IconColor,
                    CanBeFocused = false
                };
            }

            if (!hadStatus)
            {
                statusIndicatorList.RectTransform.Parent = null;
            }

            // Debug only
#if DEBUG
            GUICustomComponent statBoxScroller = new GUICustomComponent(new RectTransform(new Vector2(1f, 0.4f), content.RectTransform))
            {
                CanBeFocused = false
            };

            GUIListBox trackedStatsBox = new GUIListBox(new RectTransform(Vector2.One, statBoxScroller.RectTransform))
            {
                UseGridLayout = false,
                AutoHideScrollBar = false,
            };

            statBoxScroller.OnUpdate = (deltaTime, component) =>
            {
                trackedStatsBox.BarScroll -= (PlayerInput.ScrollWheelSpeed / 500.0f) * trackedStatsBox.ScrollBar.UnclampedBarSize;
            };

            foreach (Resource resource in outpostData.ResourceManager.Resources
                                                     .OrderBy(static r => r.Hidden)
                                                     .ThenBy(static r => r.Prefab.Name))
            {
                CreateTracker(resource);
            }

            return;

            void CreateTracker(Resource resource)
            {
                new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.2f), trackedStatsBox.Content.RectTransform)
                                 {
                                     MinSize = new Point(15, 15)
                                 }, RichString.Rich($"{resource.Prefab.Name}: {resource.GetAmountText(withNumber: true)}{GetGain(resource, outpostData)}"))
                {
                    TextColor = resource.Hidden ? Color.Magenta : GUIStyle.TextColorNormal
                };
                return;

                // This needs to eventually go so not bothering with duplicate code for now
                static string GetGain(Resource resource, PersistentOutpostData outpostData)
                {
                    float total = 0f;
                    foreach (ResourceGenerator generator in outpostData.ResourceGenerators)
                    {
                        if (!generator.IsActive) { continue; }

                        if (generator.ProducedResourcesList
                                     .FirstOrNone(r => r.Identifier == resource.Identifier)
                                     .TryUnwrap(out var produced))
                        {
                            total += produced.Amount * generator.GetDiminishingReturnMultiplier(outpostData);
                        }
                    }

                    if (MathUtils.NearlyEqual(total, 0f))
                    {
                        return string.Empty;
                    }

                    string tag = total switch
                    {
                        > 0 => "+",
                        < 0 => "-",
                        _   => string.Empty
                    };
                    return $" {tag}(‖color:{(total > 0 ? "gui.green" : "gui.red")}‖{Math.Abs(total)}‖color:end‖)";
                }
            }
#endif
        }
    }
}