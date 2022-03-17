#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    [SuppressMessage("ReSharper", "UnusedVariable")]
    internal class MedicalClinicUI
    {
        private enum ElementState
        {
            Enabled,
            Disabled
        }

        // Represents a pending affliction in the right side pending heal list
        private struct PendingAfflictionElement
        {
            public readonly GUIComponent UIElement;
            public readonly MedicalClinic.NetAffliction Target;
            public readonly GUITextBlock Price;

            public PendingAfflictionElement(MedicalClinic.NetAffliction target, GUIComponent element, GUITextBlock price)
            {
                UIElement = element;
                Target = target;
                Price = price;
            }
        }

        // Represents a pending heal on the right side list
        private struct PendingHealElement
        {
            public readonly GUIComponent UIElement;
            public MedicalClinic.NetCrewMember Target;
            public readonly GUIListBox AfflictionList;
            public readonly List<PendingAfflictionElement> Afflictions;

            public PendingHealElement(MedicalClinic.NetCrewMember target, GUIComponent element, GUIListBox afflictionList)
            {
                UIElement = element;
                Target = target;
                AfflictionList = afflictionList;
                Afflictions = new List<PendingAfflictionElement>();
            }

            public PendingAfflictionElement? FindAfflictionElement(MedicalClinic.NetAffliction target) => Afflictions.FirstOrNull(element => element.Target.Identifier == target.Identifier);
        }

        // Represents an affliction on the left side crew entry
        private readonly struct AfflictionElement
        {
            public readonly GUIImage? UIImage;
            public readonly GUIComponent UIElement;
            public readonly MedicalClinic.NetAffliction Target;

            public AfflictionElement(MedicalClinic.NetAffliction target, GUIComponent element, GUIImage? icon)
            {
                UIElement = element;
                UIImage = icon;
                Target = target;
            }
        }

        // Represent an entry on the left side crew list
        private readonly struct CrewElement
        {
            public readonly GUIComponent UIElement;
            public readonly CharacterInfo Target;
            public readonly GUIListBox AfflictionList;
            public readonly List<AfflictionElement> Afflictions;
            public readonly GUIComponent OverflowIndicator;

            public CrewElement(CharacterInfo target, GUIComponent overflowIndicator, GUIComponent element, GUIListBox afflictionList)
            {
                OverflowIndicator = overflowIndicator;
                UIElement = element;
                Target = target;
                AfflictionList = afflictionList;
                Afflictions = new List<AfflictionElement>();
            }
        }

        // Represents the right side pending list
        private readonly struct PendingHealList
        {
            public readonly GUIListBox HealList;
            public readonly GUITextBlock? ErrorBlock;
            public readonly GUITextBlock PriceBlock;
            public readonly List<PendingHealElement> HealElements;
            public readonly GUIButton HealButton;

            public PendingHealList(GUIListBox healList, GUITextBlock priceBlock, GUIButton healButton, GUITextBlock? errorBlock)
            {
                HealList = healList;
                ErrorBlock = errorBlock;
                PriceBlock = priceBlock;
                HealButton = healButton;
                HealElements = new List<PendingHealElement>();
            }

            public void UpdateElement(PendingHealElement newElement)
            {
                foreach (PendingHealElement element in HealElements.ToList())
                {
                    if (element.Target.CharacterEquals(newElement.Target))
                    {
                        HealElements.Remove(element);
                        HealElements.Add(newElement);
                        return;
                    }
                }
            }

            public PendingHealElement? FindCrewElement(MedicalClinic.NetCrewMember crewMember) => HealElements.FirstOrNull(element => element.Target.CharacterInfoID == crewMember.CharacterInfoID);
        }

        // Represents the left side crew list
        private readonly struct CrewHealList
        {
            public readonly GUIComponent Panel;
            public readonly GUIListBox HealList;
            public readonly List<CrewElement> HealElements;

            public CrewHealList(GUIListBox healList, GUIComponent panel)
            {
                Panel = panel;
                HealList = healList;
                HealElements = new List<CrewElement>();
            }
        }

        private readonly struct PopupAffliction
        {
            public readonly MedicalClinic.NetAffliction Target;
            public readonly ImmutableArray<GUIComponent> ElementsToDisable;

            public PopupAffliction(ImmutableArray<GUIComponent> elementsToDisable, MedicalClinic.NetAffliction target)
            {
                Target = target;
                ElementsToDisable = elementsToDisable;
            }
        }

        private readonly struct PopupAfflictionList
        {
            public readonly MedicalClinic.NetCrewMember Target;
            public readonly GUIButton TreatAllButton;
            public readonly List<PopupAffliction> Afflictions;

            public PopupAfflictionList(MedicalClinic.NetCrewMember crewMember, GUIButton treatAllButton)
            {
                Target = crewMember;
                TreatAllButton = treatAllButton;
                Afflictions = new List<PopupAffliction>();
            }
        }

        // private enum SortMode
        // {
        //     Severity
        // }

        private readonly MedicalClinic medicalClinic;
        private readonly GUIComponent container;
        private Point prevResolution;

        private PendingHealList? pendingHealList;
        private CrewHealList? crewHealList;

        private GUIFrame? selectedCrewElement;
        private PopupAfflictionList? selectedCrewAfflictionList;
        private bool isWaitingForServer;
        private const float refreshTimerMax = 3f;
        private float refreshTimer = 0;

        public MedicalClinicUI(MedicalClinic clinic, GUIComponent parent)
        {
            medicalClinic = clinic;
            container = parent;
            clinic.OnUpdate = OnMedicalClinicUpdated;

#if DEBUG
            // creates a button that re-creates the UI
            CreateRefreshButton();
            void CreateRefreshButton()
            {
                new GUIButton(new RectTransform(new Vector2(0.2f, 0.1f), parent.RectTransform, Anchor.TopCenter), "Recreate UI - NOT PRESENT IN RELEASE!")
                {
                    OnClicked = (_, __) =>
                    {
                        parent.ClearChildren();
                        CreateUI();
                        CreateRefreshButton();
                        RequestLatestPending();
                        return true;
                    }
                };
            }
#endif
            CreateUI();
        }

        private void OnMedicalClinicUpdated()
        {
            UpdateCrewPanel();
            UpdatePending();
            UpdatePopupAfflictions();
        }

        private void UpdatePopupAfflictions()
        {
            if (selectedCrewAfflictionList is { } afflictionList)
            {
                foreach (PopupAffliction popupAffliction in afflictionList.Afflictions)
                {
                    ToggleElements(ElementState.Enabled, popupAffliction.ElementsToDisable);
                    if (medicalClinic.IsAfflictionPending(afflictionList.Target, popupAffliction.Target))
                    {
                        ToggleElements(ElementState.Disabled, popupAffliction.ElementsToDisable);
                    }
                }

                afflictionList.TreatAllButton.Enabled = true;
                if (afflictionList.Afflictions.All(a => medicalClinic.IsAfflictionPending(afflictionList.Target, a.Target)))
                {
                    afflictionList.TreatAllButton.Enabled = false;
                }
            }
        }

        private void UpdatePending()
        {
            if (!(pendingHealList is { } healList)) { return; }

            ImmutableArray<MedicalClinic.NetCrewMember> pendingList = medicalClinic.PendingHeals.ToImmutableArray();

            // check if there are crew members that are not in the UI
            foreach (MedicalClinic.NetCrewMember crewMember in pendingList)
            {
                if (healList.FindCrewElement(crewMember) is { } element)
                {
                    element.Target = crewMember;
                    healList.UpdateElement(element);
                    continue;
                }

                CreatePendingHealElement(healList.HealList.Content, crewMember, healList, Array.Empty<MedicalClinic.NetAffliction>());
            }

            // check if there are elements that the crew doesn't have
            foreach (PendingHealElement element in healList.HealElements.ToList())
            {
                if (pendingList.Any(member => member.CharacterEquals(element.Target)))
                {
                    UpdatePendingAfflictions(element);
                    continue;
                }

                healList.HealElements.Remove(element);
                healList.HealList.Content.RemoveChild(element.UIElement);
            }

            int totalCost = medicalClinic.GetTotalCost();
            healList.PriceBlock.Text = UpgradeStore.FormatCurrency(totalCost);
            healList.PriceBlock.TextColor = GUIStyle.Red;
            healList.HealButton.Enabled = false;
            if (medicalClinic.GetWallet().CanAfford(totalCost))
            {
                healList.PriceBlock.TextColor = GUIStyle.TextColorNormal;
                if (medicalClinic.PendingHeals.Any())
                {
                    healList.HealButton.Enabled = true;
                }
            }
        }

        private void UpdatePendingAfflictions(PendingHealElement element)
        {
            MedicalClinic.NetCrewMember crewMember = element.Target;
            foreach (MedicalClinic.NetAffliction affliction in crewMember.Afflictions.ToList())
            {
                if (element.FindAfflictionElement(affliction) is { } existingAffliction)
                {
                    existingAffliction.Price.Text = UpgradeStore.FormatCurrency(affliction.Strength);
                    continue;
                }

                CreatePendingAffliction(element.AfflictionList, crewMember, affliction, element);
            }

            foreach (PendingAfflictionElement afflictionElement in element.Afflictions.ToList())
            {
                if (crewMember.Afflictions.Any(affliction => affliction.AfflictionEquals(afflictionElement.Target))) { continue; }

                element.Afflictions.Remove(afflictionElement);
                element.AfflictionList.Content.RemoveChild(afflictionElement.UIElement);
            }
        }

        private void UpdateCrewPanel()
        {
            if (!(crewHealList is { } healList)) { return; }

            ImmutableArray<CharacterInfo> crew = MedicalClinic.GetCrewCharacters();

            // check if there are crew members that are not in the UI
            foreach (CharacterInfo info in crew)
            {
                if (healList.HealElements.Any(element => element.Target == info)) { continue; }

                CreateCrewEntry(healList.HealList.Content, healList, info, healList.Panel);
            }

            // check if there are elements that the crew doesn't have
            foreach (CrewElement element in healList.HealElements.ToList())
            {
                if (crew.Any(info => element.Target == info))
                {
                    UpdateAfflictionList(element);
                    continue;
                }

                healList.HealElements.Remove(element);
                healList.HealList.Content.RemoveChild(element.UIElement);
            }

            IEnumerable<CrewElement> orderedList = healList.HealElements.OrderBy(element => element.Target.Character?.HealthPercentage ?? 100);

            foreach (CrewElement element in orderedList)
            {
                element.UIElement.SetAsLastChild();
            }
        }

        private static void UpdateAfflictionList(CrewElement healElement)
        {
            CharacterHealth? health = healElement.Target.Character?.CharacterHealth;
            if (health is null) { return; }

            // sum up all the afflictions and their strengths
            Dictionary<AfflictionPrefab, float> afflictionAndStrength = new Dictionary<AfflictionPrefab, float>();

            foreach (Affliction affliction in health.GetAllAfflictions().Where(a => MedicalClinic.IsHealable(a)))
            {
                if (afflictionAndStrength.TryGetValue(affliction.Prefab, out float strength))
                {
                    strength += affliction.Strength;
                    afflictionAndStrength[affliction.Prefab] = strength;
                    continue;
                }

                afflictionAndStrength.Add(affliction.Prefab, affliction.Strength);
            }

            // hide all the elements because we only want to show 3 later on
            foreach (AfflictionElement element in healElement.Afflictions)
            {
                element.UIElement.Visible = false;
            }

            healElement.OverflowIndicator.Visible = false;

            foreach (var (prefab, strength) in afflictionAndStrength)
            {
                bool found = false;
                foreach (AfflictionElement existingElement in healElement.Afflictions)
                {
                    if (!existingElement.Target.AfflictionEquals(prefab)) { continue; }

                    if (existingElement.UIImage is { } icon)
                    {
                        icon.Color = CharacterHealth.GetAfflictionIconColor(prefab, strength);
                    }

                    found = true;
                }

                if (found) { continue; }

                CreateCrewAfflictionIcon(healElement, healElement.AfflictionList.Content, prefab, strength);
            }

            foreach (AfflictionElement element in healElement.Afflictions.ToList())
            {
                if (afflictionAndStrength.Any(pair => element.Target.AfflictionEquals(pair.Key))) { continue; }

                healElement.AfflictionList.Content.RemoveChild(element.UIElement);
                healElement.Afflictions.Remove(element);
            }

            for (int i = 0; i < 3 && i < healElement.Afflictions.Count; i++)
            {
                healElement.Afflictions[i].UIElement.Visible = true;
            }

            healElement.OverflowIndicator.Visible = healElement.Afflictions.Count > 3;
            healElement.OverflowIndicator.SetAsLastChild();
        }

        private static void CreateCrewAfflictionIcon(CrewElement healElement, GUIComponent parent, AfflictionPrefab prefab, float strength)
        {
            GUIFrame backgroundFrame = new GUIFrame(new RectTransform(new Vector2(0.25f, 1f), parent.RectTransform), style: null)
            {
                CanBeFocused = false,
                Visible = false
            };

            GUIImage? uiIcon = null;
            if (prefab.Icon is { } icon)
            {
                uiIcon = new GUIImage(new RectTransform(Vector2.One, backgroundFrame.RectTransform), icon, scaleToFit: true)
                {
                    Color = CharacterHealth.GetAfflictionIconColor(prefab, strength)
                };
            }

            healElement.Afflictions.Add(new AfflictionElement(new MedicalClinic.NetAffliction { Prefab = prefab }, backgroundFrame, uiIcon));
        }

        private void CreateUI()
        {
            container.ClearChildren();
            pendingHealList = null;
            int panelMaxWidth = (int)(GUI.xScale * (GUI.HorizontalAspectRatio < 1.4f ? 650 : 560));

            GUIFrame paddedParent = new GUIFrame(new RectTransform(new Vector2(0.95f), container.RectTransform, Anchor.Center), style: null);

            GUILayoutGroup clinicContent = new GUILayoutGroup(new RectTransform(new Vector2(0.45f, 1.0f), paddedParent.RectTransform)
            {
                MaxSize = new Point(panelMaxWidth, container.Rect.Height)
            })
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };

            GUILayoutGroup clinicLabelLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.1f), clinicContent.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft);
            GUIImage clinicIcon = new GUIImage(new RectTransform(Vector2.One, clinicLabelLayout.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: "CrewManagementHeaderIcon", scaleToFit: true);
            GUITextBlock clinicLabel = new GUITextBlock(new RectTransform(Vector2.One, clinicLabelLayout.RectTransform), TextManager.Get("medicalclinic.medicalclinic"), font: GUIStyle.LargeFont);

            GUIFrame clinicBackground = new GUIFrame(new RectTransform(Vector2.One, clinicContent.RectTransform));

            CreateLeftSidePanel(clinicBackground);

            GUILayoutGroup crewContent = new GUILayoutGroup(new RectTransform(new Vector2(0.45f, 1.0f), paddedParent.RectTransform, anchor: Anchor.TopRight)
            {
                MaxSize = new Point(panelMaxWidth, container.Rect.Height)
            })
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };

            GUILayoutGroup balanceLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.1f), crewContent.RectTransform));
            GUITextBlock balanceLabel = new GUITextBlock(new RectTransform(new Vector2(1f, 0.5f), balanceLayout.RectTransform), TextManager.Get("campaignstore.balance"), textAlignment: Alignment.BottomRight, font: GUIStyle.Font)
            {
                AutoScaleVertical = true,
                ForceUpperCase = ForceUpperCase.Yes
            };

            GUITextBlock moneyLabel = new GUITextBlock(new RectTransform(new Vector2(1f, 0.5f), balanceLayout.RectTransform), string.Empty, textAlignment: Alignment.TopRight, font: GUIStyle.SubHeadingFont)
            {
                TextGetter = () => UpgradeStore.FormatCurrency(medicalClinic.GetWallet().Balance),
                AutoScaleVertical = true,
                TextScale = 1.1f
            };

            GUIFrame crewBackground = new GUIFrame(new RectTransform(Vector2.One, crewContent.RectTransform));

            CreateRightSidePanel(crewBackground);

            prevResolution = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);
        }

        private void CreateLeftSidePanel(GUIComponent parent)
        {
            crewHealList = null;
            GUILayoutGroup clinicContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.95f), parent.RectTransform, Anchor.Center))
            {
                RelativeSpacing = 0.015f,
                Stretch = true
            };

            // GUILayoutGroup sortLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.05f), clinicContainer.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft);

            // new GUITextBlock(new RectTransform(new Vector2(0.2f, 1f), sortLayout.RectTransform), TextManager.Get("campaignstore.sortby"), font: GUI.SubHeadingFont);

            // GUIDropDown sortDropdown = new GUIDropDown(new RectTransform(new Vector2(0.3f, 1f), sortLayout.RectTransform));
            //
            // foreach (SortMode mode in Enum.GetValues(typeof(SortMode)).Cast<SortMode>())
            // {
            //     sortDropdown.AddItem(TextManager.Get($"medicalclinic.sortmode.{mode}"), mode);
            // }
            //
            // sortDropdown.SelectItem(SortMode.Severity);

            GUIListBox crewList = new GUIListBox(new RectTransform(Vector2.One, clinicContainer.RectTransform));

            crewHealList = new CrewHealList(crewList, parent);
        }

        private void CreateCrewEntry(GUIComponent parent, CrewHealList healList, CharacterInfo info, GUIComponent panel)
        {
            GUIButton crewBackground = new GUIButton(new RectTransform(new Vector2(1f, 0.1f), parent.RectTransform), style: "ListBoxElement");

            GUILayoutGroup crewLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.95f), crewBackground.RectTransform, Anchor.Center), isHorizontal: true, childAnchor: Anchor.CenterLeft);

            GUILayoutGroup characterBlockLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.45f, 0.9f), crewLayout.RectTransform), isHorizontal: true, Anchor.CenterLeft);
            CreateCharacterBlock(characterBlockLayout, info);

            GUIListBox afflictionList = new GUIListBox(new RectTransform(new Vector2(0.45f, 1f), crewLayout.RectTransform), style: null, isHorizontal: true);

            GUILayoutGroup healthLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.1f, 1f), crewLayout.RectTransform), isHorizontal: true, Anchor.Center);

            new GUITextBlock(new RectTransform(Vector2.One, healthLayout.RectTransform), string.Empty, textAlignment: Alignment.Center, font: GUIStyle.SubHeadingFont)
            {
                TextGetter = () => TextManager.GetWithVariable("percentageformat", "[value]", $"{(int)(info.Character?.HealthPercentage ?? 100f)}"),
                TextColor = GUIStyle.Green
            };

            GUITextBlock overflowIndicator =
                new GUITextBlock(new RectTransform(new Vector2(0.25f, 1f), afflictionList.Content.RectTransform, scaleBasis: ScaleBasis.BothHeight), text: "+", textAlignment: Alignment.Center, font: GUIStyle.LargeFont)
                {
                    Visible = false,
                    CanBeFocused = false,
                    TextColor = GUIStyle.Red
                };

            MedicalClinic.NetCrewMember member = new MedicalClinic.NetCrewMember { CharacterInfo = info, Afflictions = Array.Empty<MedicalClinic.NetAffliction>() };

            crewBackground.OnClicked = (_, __) =>
            {
                SelectCharacter(member, new Vector2(panel.Rect.Right, crewBackground.Rect.Top));
                return true;
            };

            healList.HealElements.Add(new CrewElement(info, overflowIndicator, crewBackground, afflictionList));
        }

        private void CreateRightSidePanel(GUIComponent parent)
        {
            GUILayoutGroup pendingHealContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.95f), parent.RectTransform, anchor: Anchor.Center))
            {
                RelativeSpacing = 0.015f,
                Stretch = true
            };

            new GUITextBlock(new RectTransform(new Vector2(1f, 0.05f), pendingHealContainer.RectTransform), TextManager.Get("medicalclinic.pendingheals"), font: GUIStyle.SubHeadingFont);

            GUIFrame healListContainer = new GUIFrame(new RectTransform(new Vector2(1f, 0.9f), pendingHealContainer.RectTransform), style: null);
            GUITextBlock? errorBlock = null;
            if (!GameMain.IsSingleplayer)
            {
                errorBlock = new GUITextBlock(new RectTransform(Vector2.One, healListContainer.RectTransform), text: TextManager.Get("pleasewaitupnp"), font: GUIStyle.LargeFont, textAlignment: Alignment.Center);
            }

            GUIListBox healList = new GUIListBox(new RectTransform(Vector2.One, healListContainer.RectTransform))
            {
                Spacing = GUI.IntScale(8),
                Visible = GameMain.IsSingleplayer
            };

            GUILayoutGroup footerLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.1f), pendingHealContainer.RectTransform));

            GUILayoutGroup priceLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.5f), footerLayout.RectTransform), isHorizontal: true);
            GUITextBlock priceLabelBlock = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1f), priceLayout.RectTransform), TextManager.Get("campaignstore.total"));
            GUITextBlock priceBlock = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1f), priceLayout.RectTransform), UpgradeStore.FormatCurrency(medicalClinic.GetTotalCost()), font: GUIStyle.SubHeadingFont,
                textAlignment: Alignment.Right);

            GUILayoutGroup buttonLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.5f), footerLayout.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterRight);
            GUIButton healButton = new GUIButton(new RectTransform(new Vector2(0.33f, 1f), buttonLayout.RectTransform), TextManager.Get("medicalclinic.heal"))
            {
                Enabled = medicalClinic.PendingHeals.Any() && medicalClinic.GetWallet().CanAfford(medicalClinic.GetTotalCost()),
                OnClicked = (button, _) =>
                {
                    button.Enabled = false;
                    medicalClinic.HealAllButtonAction(request =>
                    {
                        switch (request.HealResult)
                        {
                            case MedicalClinic.HealRequestResult.InsufficientFunds:
                                GUI.NotifyPrompt(TextManager.Get("medicalclinic.unabletoheal"), TextManager.Get("medicalclinic.insufficientfunds"));
                                break;
                            case MedicalClinic.HealRequestResult.Refused:
                                GUI.NotifyPrompt(TextManager.Get("medicalclinic.unabletoheal"), TextManager.Get("medicalclinic.healrefused"));
                                break;
                        }

                        button.Enabled = true;
                        ClosePopup();
                    });
                    ClosePopup();
                    return true;
                }
            };

            GUIButton clearButton = new GUIButton(new RectTransform(new Vector2(0.33f, 1f), buttonLayout.RectTransform), TextManager.Get("campaignstore.clearall"))
            {
                OnClicked = (button, _) =>
                {
                    button.Enabled = false;
                    medicalClinic.ClearAllButtonAction(_ =>
                    {
                        button.Enabled = true;
                    });
                    return true;
                }
            };

            PendingHealList list = new PendingHealList(healList, priceBlock, healButton, errorBlock);

            foreach (MedicalClinic.NetCrewMember heal in GetPendingCharacters())
            {
                CreatePendingHealElement(healList.Content, heal, list, heal.Afflictions);
            }

            pendingHealList = list;
        }

        private void CreatePendingHealElement(GUIComponent parent, MedicalClinic.NetCrewMember crewMember, PendingHealList healList, MedicalClinic.NetAffliction[] afflictions)
        {
            CharacterInfo? healInfo = crewMember.FindCharacterInfo(MedicalClinic.GetCrewCharacters());
            if (healInfo is null) { return; }

            GUIFrame pendingHealBackground = new GUIFrame(new RectTransform(new Vector2(1f, 0.25f), parent.RectTransform), style: "ListBoxElement")
            {
                CanBeFocused = false
            };
            GUILayoutGroup pendingHealLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.95f), pendingHealBackground.RectTransform, Anchor.Center));

            GUILayoutGroup topHeaderLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.3f), pendingHealLayout.RectTransform), isHorizontal: true, Anchor.CenterLeft) { Stretch = true };

            CreateCharacterBlock(topHeaderLayout, healInfo);

            GUILayoutGroup bottomLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.7f), pendingHealLayout.RectTransform), childAnchor: Anchor.Center);

            GUIListBox pendingAfflictionList = new GUIListBox(new RectTransform(Vector2.One, bottomLayout.RectTransform))
            {
                AutoHideScrollBar = false,
                ScrollBarVisible = true
            };

            PendingHealElement healElement = new PendingHealElement(crewMember, pendingHealBackground, pendingAfflictionList);

            foreach (MedicalClinic.NetAffliction affliction in afflictions)
            {
                CreatePendingAffliction(pendingAfflictionList, crewMember, affliction, healElement);
            }

            healList.HealElements.Add(healElement);
            RecalculateLayouts(pendingHealLayout, topHeaderLayout, bottomLayout);
            pendingAfflictionList.ForceUpdate();
        }

        private void CreatePendingAffliction(GUIListBox parent, MedicalClinic.NetCrewMember crewMember, MedicalClinic.NetAffliction affliction, PendingHealElement healElement)
        {
            GUIFrame backgroundFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.33f), parent.Content.RectTransform), style: "ListBoxElement")
            {
                CanBeFocused = false
            };
            GUILayoutGroup parentLayout = new GUILayoutGroup(new RectTransform(Vector2.One, backgroundFrame.RectTransform), isHorizontal: true) { Stretch = true };

            if (!(affliction.Prefab is { } prefab)) { return; }

            if (prefab.Icon is { } icon)
            {
                new GUIImage(new RectTransform(Vector2.One, parentLayout.RectTransform, scaleBasis: ScaleBasis.BothHeight), icon, scaleToFit: true)
                {
                    Color = CharacterHealth.GetAfflictionIconColor(prefab, affliction.Strength)
                };
            }

            GUILayoutGroup textLayout = new GUILayoutGroup(new RectTransform(Vector2.One, parentLayout.RectTransform), isHorizontal: true);

            LocalizedString name = prefab.Name;

            GUIFrame textContainer = new GUIFrame(new RectTransform(new Vector2(0.6f, 1f), textLayout.RectTransform), style: null);
            GUITextBlock afflictionName = new GUITextBlock(new RectTransform(Vector2.One, textContainer.RectTransform), name, font: GUIStyle.SubHeadingFont);

            GUITextBlock healCost = new GUITextBlock(new RectTransform(new Vector2(0.2f, 1f), textLayout.RectTransform), UpgradeStore.FormatCurrency(affliction.Price), textAlignment: Alignment.Center, font: GUIStyle.LargeFont)
            {
                Padding = Vector4.Zero
            };

            GUIButton healButton = new GUIButton(new RectTransform(new Vector2(0.2f, 1f), textLayout.RectTransform), style: "CrewManagementRemoveButton")
            {
                OnClicked = (button, _) =>
                {
                    button.Enabled = false;
                    medicalClinic.RemovePendingButtonAction(crewMember, affliction, _ =>
                    {
                        button.Enabled = true;
                    });
                    return true;
                }
            };

            EnsureTextDoesntOverflow(name.Value, afflictionName, textContainer.Rect, ImmutableArray.Create(textLayout, parentLayout));

            healElement.Afflictions.Add(new PendingAfflictionElement(affliction, backgroundFrame, healCost));

            RecalculateLayouts(parentLayout, textLayout);

            parent.ForceUpdate();
        }

        private static void CreateCharacterBlock(GUIComponent parent, CharacterInfo info)
        {
            new GUICustomComponent(new RectTransform(Vector2.One, parent.RectTransform, scaleBasis: ScaleBasis.BothHeight), (spriteBatch, component) =>
            {
                info.DrawPortrait(spriteBatch, component.Rect.Location.ToVector2(), Vector2.Zero, component.Rect.Width);
            });

            GUILayoutGroup textGroup = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.8f), parent.RectTransform));

            string? characterName = info.Name;
            LocalizedString? jobName = null;

            GUITextBlock? nameBlock = new GUITextBlock(new RectTransform(new Vector2(1f, 0.5f), textGroup.RectTransform), characterName),
                          jobBlock = null;

            if (info.Job is { Name: { } name, Prefab: { UIColor: var color} } job)
            {
                jobName = name;
                jobBlock = new GUITextBlock(new RectTransform(new Vector2(1f, 0.5f), textGroup.RectTransform), jobName);
                nameBlock.TextColor = color;
            }

            if (parent is GUILayoutGroup layoutGroup)
            {
                ImmutableArray<GUILayoutGroup> layoutGroups = ImmutableArray.Create(layoutGroup, textGroup);

                EnsureTextDoesntOverflow(characterName, nameBlock, parent.Rect, layoutGroups);

                if (jobBlock is null) { return; }

                EnsureTextDoesntOverflow(jobName?.Value, jobBlock, parent.Rect, layoutGroups);
            }
        }

        private void SelectCharacter(MedicalClinic.NetCrewMember crewMember, Vector2 location)
        {
            CharacterInfo? info = crewMember.FindCharacterInfo(MedicalClinic.GetCrewCharacters());
            if (info is null) { return; }

            if (isWaitingForServer) { return; }

            ClosePopup();

            GUIFrame mainFrame = new GUIFrame(new RectTransform(new Vector2(0.28f, 0.45f), container.RectTransform)
            {
                ScreenSpaceOffset = location.ToPoint()
            });

            GUILayoutGroup mainLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.95f), mainFrame.RectTransform, Anchor.Center)) { RelativeSpacing = 0.01f, Stretch = true };

            if (mainFrame.Rect.Bottom > GameMain.GraphicsHeight)
            {
                mainFrame.RectTransform.ScreenSpaceOffset = new Point((int)location.X, GameMain.GraphicsHeight - mainFrame.Rect.Height);
            }

            GUITextBlock feedbackBlock = new GUITextBlock(new RectTransform(Vector2.One, mainFrame.RectTransform), TextManager.Get("pleasewaitupnp"), textAlignment: Alignment.Center, font: GUIStyle.LargeFont, wrap: true)
            {
                Visible = true
            };

            GUIButton treatAllButton = new GUIButton(new RectTransform(new Vector2(1f, 0.2f), mainLayout.RectTransform), TextManager.Get("medicalclinic.treatall"))
            {
                Font = GUIStyle.SubHeadingFont,
                Visible = false
            };

            GUIListBox afflictionList = new GUIListBox(new RectTransform(new Vector2(1f, 0.8f), mainLayout.RectTransform)) { Visible = false };

            PopupAfflictionList popupAfflictionList = new PopupAfflictionList(crewMember, treatAllButton);
            selectedCrewElement = mainFrame;
            selectedCrewAfflictionList = popupAfflictionList;

            isWaitingForServer = true;
            medicalClinic.RequestAfflictions(info, OnReceived);

            void OnReceived(MedicalClinic.AfflictionRequest request)
            {
                isWaitingForServer = false;

                if (request.Result != MedicalClinic.RequestResult.Success)
                {
                    feedbackBlock.Text = GetErrorText(request.Result);
                    feedbackBlock.TextColor = GUIStyle.Red;
                    return;
                }

                List<GUIComponent> allComponents = new List<GUIComponent>();
                foreach (MedicalClinic.NetAffliction affliction in request.Afflictions)
                {
                    ImmutableArray<GUIComponent> createdComponents = CreatePopupAffliction(afflictionList.Content, crewMember, affliction);
                    allComponents.AddRange(createdComponents);
                    popupAfflictionList.Afflictions.Add(new PopupAffliction(createdComponents, affliction));
                }

                allComponents.Add(treatAllButton);
                treatAllButton.OnClicked = (_, __) =>
                {
                    ImmutableArray<MedicalClinic.NetAffliction> afflictions = request.Afflictions.Where(a => !medicalClinic.IsAfflictionPending(crewMember, a)).ToImmutableArray();
                    if (!afflictions.Any()) { return true; }

                    AddPending(allComponents.ToImmutableArray(), crewMember, afflictions);
                    return true;
                };

                afflictionList.Visible = true;
                feedbackBlock.Visible = false;
                treatAllButton.Visible = true;
                UpdatePopupAfflictions();
            }
        }

        private ImmutableArray<GUIComponent> CreatePopupAffliction(GUIComponent parent, MedicalClinic.NetCrewMember crewMember, MedicalClinic.NetAffliction affliction)
        {
            if (!(affliction.Prefab is { } prefab)) { return ImmutableArray<GUIComponent>.Empty; }

            GUIFrame backgroundFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.33f), parent.RectTransform), style: "ListBoxElement");
            GUILayoutGroup mainLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.95f), backgroundFrame.RectTransform, Anchor.Center))
            {
                RelativeSpacing = 0.05f
            };

            GUILayoutGroup topLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.33f), mainLayout.RectTransform), isHorizontal: true) { Stretch = true };

            Color iconColor = CharacterHealth.GetAfflictionIconColor(prefab, affliction.Strength);

            GUIImage icon = new GUIImage(new RectTransform(Vector2.One, topLayout.RectTransform, scaleBasis: ScaleBasis.BothHeight), prefab.Icon, scaleToFit: true)
            {
                Color = iconColor,
                DisabledColor = iconColor * 0.5f
            };

            GUILayoutGroup topTextLayout = new GUILayoutGroup(new RectTransform(Vector2.One, topLayout.RectTransform), isHorizontal: true);

            GUITextBlock prefabBlock = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1f), topTextLayout.RectTransform), prefab.Name, font: GUIStyle.SubHeadingFont);

            Color textColor = Color.Lerp(GUIStyle.Orange, GUIStyle.Red, (int)affliction.AfflictionSeverity / 2f);

            LocalizedString vitalityText = TextManager.GetWithVariable("medicalclinic.vitalitydifference", "[amount]", (-affliction.Strength).ToString());
            GUITextBlock vitalityBlock = new GUITextBlock(new RectTransform(new Vector2(0.25f, 1f), topTextLayout.RectTransform), vitalityText, textAlignment: Alignment.Center)
            {
                TextColor = textColor,
                DisabledTextColor = textColor * 0.5f,
                Padding = Vector4.Zero,
                AutoScaleHorizontal = true
            };

            LocalizedString severityText = TextManager.Get($"AfflictionStrength{affliction.AfflictionSeverity}");
            GUITextBlock severityBlock = new GUITextBlock(new RectTransform(new Vector2(0.25f, 1f), topTextLayout.RectTransform), severityText, textAlignment: Alignment.Center, font: GUIStyle.SubHeadingFont)
            {
                TextColor = textColor,
                DisabledTextColor = textColor * 0.5f,
                Padding = Vector4.Zero,
                AutoScaleHorizontal = true
            };

            EnsureTextDoesntOverflow(prefab.Name.Value, prefabBlock, prefabBlock.Rect, ImmutableArray.Create(mainLayout, topLayout, topTextLayout));

            GUILayoutGroup bottomLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.66f), mainLayout.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft);

            GUILayoutGroup bottomTextLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 1f), bottomLayout.RectTransform));
            GUITextBlock descriptionBlock = new GUITextBlock(new RectTransform(new Vector2(1f, 0.5f), bottomTextLayout.RectTransform), ToolBox.LimitString(prefab.Description, GUIStyle.Font, GUI.IntScale(64)), wrap: true)
            {
                ToolTip = prefab.Description
            };

            GUITextBlock priceBlock = new GUITextBlock(new RectTransform(new Vector2(1f, 0.5f), bottomTextLayout.RectTransform), UpgradeStore.FormatCurrency(affliction.Price), font: GUIStyle.LargeFont);

            GUIButton buyButton = new GUIButton(new RectTransform(new Vector2(0.2f, 0.75f), bottomLayout.RectTransform), style: "CrewManagementAddButton");

            ImmutableArray<GUIComponent> elementsToDisable = ImmutableArray.Create<GUIComponent>(prefabBlock, backgroundFrame, icon, vitalityBlock, severityBlock, buyButton, descriptionBlock, priceBlock);

            buyButton.OnClicked = (_, __) =>
            {
                if (!buyButton.Enabled) { return false; }

                AddPending(elementsToDisable, crewMember, ImmutableArray.Create(affliction));
                return true;
            };

            return elementsToDisable;
        }

        private void AddPending(ImmutableArray<GUIComponent> elementsToDisable, MedicalClinic.NetCrewMember crewMember, ImmutableArray<MedicalClinic.NetAffliction> afflictions)
        {
            MedicalClinic.NetCrewMember existingMember;

            if (medicalClinic.PendingHeals.FirstOrNull(m => m.CharacterEquals(crewMember)) is { } foundHeal)
            {
                existingMember = foundHeal;
            }
            else
            {
                MedicalClinic.NetCrewMember newMember = new MedicalClinic.NetCrewMember
                {
                    CharacterInfoID = crewMember.CharacterInfoID,
                    Afflictions = Array.Empty<MedicalClinic.NetAffliction>()
                };

                existingMember = newMember;
            }

            foreach (MedicalClinic.NetAffliction affliction in afflictions)
            {
                if (existingMember.Afflictions.FirstOrNull(a => a.AfflictionEquals(affliction)) != null)
                {
                    return;
                }
            }

            existingMember.Afflictions = existingMember.Afflictions.Concat(afflictions).ToArray();
            ToggleElements(ElementState.Disabled, elementsToDisable);
            medicalClinic.AddPendingButtonAction(existingMember, request =>
            {
                if (request.Result == MedicalClinic.RequestResult.Timeout)
                {
                    ToggleElements(ElementState.Enabled, elementsToDisable);
                }
            });
        }

        private static void EnsureTextDoesntOverflow(string? text, GUITextBlock textBlock, Rectangle bounds, ImmutableArray<GUILayoutGroup>? layoutGroups = null)
        {
            if (string.IsNullOrWhiteSpace(text)) { return; }

            string originalText = text;

            UpdateLayoutGroups();

            while (textBlock.Rect.X + textBlock.TextSize.X + textBlock.Padding.X + textBlock.Padding.W > bounds.Right)
            {
                if (string.IsNullOrWhiteSpace(text)) { break; }

                text = text[..^1];
                textBlock.Text = text + "...";
                textBlock.ToolTip = originalText;

                UpdateLayoutGroups();
            }

            void UpdateLayoutGroups()
            {
                if (layoutGroups is null) { return; }

                foreach (GUILayoutGroup layoutGroup in layoutGroups)
                {
                    layoutGroup.Recalculate();
                }
            }
        }

        public void RequestLatestPending()
        {
            UpdateCrewPanel();

            if (GameMain.IsSingleplayer || !(pendingHealList is { ErrorBlock: { } errorBlock, HealList: { } healList })) { return; }

            errorBlock.Visible = true;
            errorBlock.TextColor = GUIStyle.TextColorNormal;
            errorBlock.Text = TextManager.Get("pleasewaitupnp");
            healList.Visible = false;

            isWaitingForServer = true;

            medicalClinic.RequestLatestPending(OnReceived);

            void OnReceived(MedicalClinic.PendingRequest request)
            {
                isWaitingForServer = false;

                if (request.Result != MedicalClinic.RequestResult.Success)
                {
                    errorBlock.Text = GetErrorText(request.Result);
                    errorBlock.TextColor = GUIStyle.Red;
                    return;
                }

                medicalClinic.PendingHeals.Clear();
                foreach (MedicalClinic.NetCrewMember member in request.CrewMembers)
                {
                    medicalClinic.PendingHeals.Add(member);
                }

                OnMedicalClinicUpdated();

                errorBlock.Visible = false;
                healList.Visible = true;
            }
        }

        public void ClosePopup()
        {
            if (selectedCrewElement is { } popup)
            {
                popup.Parent?.RemoveChild(selectedCrewElement);
            }

            selectedCrewElement = null;
            selectedCrewAfflictionList = null;
        }

        private static LocalizedString GetErrorText(MedicalClinic.RequestResult result)
        {
            return result switch
            {
                MedicalClinic.RequestResult.Error => TextManager.Get("error"),
                MedicalClinic.RequestResult.Timeout => TextManager.Get("medicalclinic.requesttimeout"),
                _ => "What the hell did you just do" // this should never happen
            };
        }

        private ImmutableArray<MedicalClinic.NetCrewMember> GetPendingCharacters() => medicalClinic.PendingHeals.ToImmutableArray();

        private static void ToggleElements(ElementState state, ImmutableArray<GUIComponent> elements)
        {
            foreach (GUIComponent component in elements)
            {
                component.Enabled = state switch
                {
                    ElementState.Enabled => true,
                    ElementState.Disabled => false,
                    _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
                };
            }
        }

        private static void RecalculateLayouts(params GUILayoutGroup[] layouts)
        {
            foreach (GUILayoutGroup layout in layouts)
            {
                layout.Recalculate();
            }
        }

        public void Update(float deltaTime)
        {
            if (prevResolution.X != GameMain.GraphicsWidth || prevResolution.Y != GameMain.GraphicsHeight)
            {
                CreateUI();
            }

            refreshTimer += deltaTime;

            if (refreshTimer > refreshTimerMax)
            {
                UpdateCrewPanel();
                refreshTimer = 0;
            }
        }
    }
}