using System.Collections.Generic;
using System.Collections.Immutable;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;

namespace Barotrauma.Items.Components
{
    partial class Deconstructor : Powered, IServerSerializable, IClientSerializable
    {
        public GUIButton ActivateButton
        {
            get { return activateButton; }
        }
        private GUIButton activateButton;
        private GUIComponent inputInventoryHolder, outputInventoryHolder;
        private GUIListBox outputDisplayListBox;

        private GUIComponent inSufficientPowerWarning;

        private bool pendingState;

        private GUITextBlock infoArea;

        [Serialize("DeconstructorDeconstruct", IsPropertySaveable.Yes)]
        public string ActivateButtonText { get; set; }
        
        [Serialize("", IsPropertySaveable.Yes)]
        public string InfoText { get; set; }

        [Serialize(0.0f, IsPropertySaveable.Yes)]
        public float InfoAreaWidth { get; set; }

        [Serialize(true, IsPropertySaveable.Yes)]
        public bool ShowOutput { get; set; }

        partial void InitProjSpecific(XElement _)
        {
            CreateGUI();
        }

        public override bool RecreateGUIOnResolutionChange => true;

        protected override void OnResolutionChanged()
        {
            OnItemLoadedProjSpecific();
        }

        protected override void CreateGUI()
        {
            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.88f), GuiFrame.RectTransform, Anchor.Center), childAnchor: Anchor.TopCenter)
            {
                Stretch = true,
                RelativeSpacing = 0.08f
            };

            new GUITextBlock(new RectTransform(new Vector2(1f, 0.07f), paddedFrame.RectTransform) { MinSize = new Point(0, GUI.IntScale(25)) }, item.Name, font: GUIStyle.SubHeadingFont)
            {
                TextAlignment = Alignment.Center,
                AutoScaleHorizontal = true
            };

            var topFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.375f), paddedFrame.RectTransform), style: null);

                // === INPUT LABEL === //
                var inputLabelArea = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.15f), topFrame.RectTransform, Anchor.TopCenter), childAnchor: Anchor.CenterLeft, isHorizontal: true);

                    var queueLabelLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.43f, 1f), inputLabelArea.RectTransform), childAnchor: Anchor.CenterLeft, isHorizontal: true)
                    {
                        Stretch = true,
                        RelativeSpacing = 0.05f
                    };
                        var queueLabel = new GUITextBlock(new RectTransform(Vector2.One, queueLabelLayout.RectTransform), TextManager.Get("deconstructor.inputqueue"), font: GUIStyle.SubHeadingFont) { Padding = Vector4.Zero };
                        queueLabel.RectTransform.Resize(new Point((int) queueLabel.Font.MeasureString(queueLabel.Text).X, queueLabel.RectTransform.Rect.Height));
                        new GUIFrame(new RectTransform(Vector2.One, queueLabelLayout.RectTransform), style: "HorizontalLine");

                    var inputLabelLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.57f, 1f), inputLabelArea.RectTransform), childAnchor: Anchor.CenterLeft, isHorizontal: true)
                    {
                        Stretch = true,
                        RelativeSpacing = 0.05f
                    };
                        var inputLabel = new GUITextBlock(new RectTransform(Vector2.One, inputLabelLayout.RectTransform), TextManager.Get("deconstructor.input", "uilabel.input"), font: GUIStyle.SubHeadingFont) { Padding = Vector4.Zero };
                        inputLabel.RectTransform.Resize(new Point((int) inputLabel.Font.MeasureString(inputLabel.Text).X, inputLabel.RectTransform.Rect.Height));
                        new GUIFrame(new RectTransform(Vector2.One, inputLabelLayout.RectTransform), style: "HorizontalLine");

                var inputArea = new GUILayoutGroup(new RectTransform(new Vector2(1f, 1f), topFrame.RectTransform, Anchor.CenterLeft), childAnchor: Anchor.BottomLeft, isHorizontal: true) { Stretch = true, RelativeSpacing = 0.05f };

                    // === INPUT SLOTS === //
                    inputInventoryHolder = new GUIFrame(new RectTransform(new Vector2(0.7f, 1f), inputArea.RectTransform), style: null);
                        new GUICustomComponent(new RectTransform(Vector2.One, inputInventoryHolder.RectTransform), DrawOverLay, null) { CanBeFocused = false };

                    // === ACTIVATE BUTTON === //
                    var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.4f, 0.8f), inputArea.RectTransform), childAnchor: Anchor.CenterLeft);
                        activateButton = new GUIButton(new RectTransform(new Vector2(0.95f, 0.8f), buttonContainer.RectTransform), TextManager.Get("DeconstructorDeconstruct"), style: "DeviceButton")
                        {
                            UserData = UIHighlightAction.ElementId.DeconstructButton,
                            TextBlock = { AutoScaleHorizontal = true },
                            OnClicked = OnActivateButtonClicked
                        };
                            inSufficientPowerWarning = new GUITextBlock(new RectTransform(Vector2.One, activateButton.RectTransform),
                                TextManager.Get("DeconstructorNoPower"), textColor: GUIStyle.Orange, textAlignment: Alignment.Center, color: Color.Black, style: "OuterGlow", wrap: true)
                                {
                                HoverColor = Color.Black, 
                                IgnoreLayoutGroups = true, 
                                Visible = false, 
                                CanBeFocused = false,
                                AutoScaleHorizontal = true
                            };

            // === OUTPUT AREA === //
            var bottomFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.375f), paddedFrame.RectTransform), style: null);

                // === OUTPUT LABEL === //
                var outputLabelArea = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.15f), bottomFrame.RectTransform, Anchor.TopCenter), childAnchor: Anchor.CenterLeft, isHorizontal: true)
                {
                    Stretch = true, 
                    RelativeSpacing = 0.05f
                };
                    var outputLabel = new GUITextBlock(new RectTransform(new Vector2(0f, 1.0f), outputLabelArea.RectTransform), TextManager.Get("uilabel.output"), font: GUIStyle.SubHeadingFont) { Padding = Vector4.Zero };
                    outputLabel.RectTransform.Resize(new Point((int) outputLabel.Font.MeasureString(outputLabel.Text).X, outputLabel.RectTransform.Rect.Height));
                    new GUIFrame(new RectTransform(Vector2.One, outputLabelArea.RectTransform), style: "HorizontalLine");

                var outputArea = new GUILayoutGroup(new RectTransform(new Vector2(1f, 1f), bottomFrame.RectTransform, Anchor.CenterLeft), childAnchor: Anchor.BottomLeft, isHorizontal: true) { Stretch = true, RelativeSpacing = 0.05f };

                // === OUTPUT SLOTS === //
                outputInventoryHolder = new GUIFrame(new RectTransform(new Vector2(1f - InfoAreaWidth, 1f), outputArea.RectTransform, Anchor.CenterLeft), style: null);

            if (ShowOutput)
            {
                GUILayoutGroup outputDisplayLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.25f), paddedFrame.RectTransform), childAnchor: Anchor.TopCenter);
                        GUILayoutGroup outDisplayTopGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.2f), outputDisplayLayout.RectTransform), isHorizontal: true);
                            GUITextBlock outDisplayBlock = new GUITextBlock(new RectTransform(Vector2.One, outDisplayTopGroup.RectTransform), TextManager.Get("deconstructor.output"), font: GUIStyle.SubHeadingFont) { Padding = Vector4.Zero };
                        GUILayoutGroup outDisplayBottomGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.975f, 0.8f), outputDisplayLayout.RectTransform), isHorizontal: true);
                            outputDisplayListBox = new GUIListBox(new RectTransform(new Vector2(1f, 1f), outDisplayBottomGroup.RectTransform), isHorizontal: true, style: null);
            }

            if (InfoAreaWidth >= 0.0f)
            {
                var infoAreaContainer = new GUILayoutGroup(new RectTransform(new Vector2(InfoAreaWidth, 0.8f), outputArea.RectTransform), childAnchor: Anchor.CenterLeft);
                infoArea = new GUITextBlock(new RectTransform(new Vector2(0.95f, 0.95f), infoAreaContainer.RectTransform), string.Empty, wrap: true);
            }

            ActivateButton.OnAddedToGUIUpdateList += (GUIComponent component) =>
            {
                activateButton.Enabled = true;
                if (string.IsNullOrEmpty(InfoText))
                {
                    infoArea.Text = string.Empty;
                }
                else
                {
                    infoArea.Text = TextManager.Get(InfoText).Fallback(InfoText);
                }
                if (IsActive)
                {
                    activateButton.Text = TextManager.Get("DeconstructorCancel");
                    infoArea.Text = string.Empty;
                    return;
                }
                bool outputsFound = false;
                foreach (var (inputItem, deconstructItem) in GetAvailableOutputs(checkRequiredOtherItems: true))
                {
                    outputsFound = true;
                    if (!string.IsNullOrEmpty(deconstructItem.ActivateButtonText))
                    {
                        LocalizedString buttonText = TextManager.Get(deconstructItem.ActivateButtonText).Fallback(deconstructItem.ActivateButtonText);
                        LocalizedString infoText =  string.Empty;
                        if (!string.IsNullOrEmpty(deconstructItem.InfoText))
                        {
                            infoText = TextManager.Get(deconstructItem.InfoText).Fallback(deconstructItem.InfoText);
                        }
                        inputItem.GetComponent<GeneticMaterial>()?.ModifyDeconstructInfo(this, ref buttonText, ref infoText);
                        activateButton.Text = buttonText;
                        if (infoArea != null)
                        {
                            infoArea.Text = infoText;
                        }
                        return;
                    }
                }
                //no valid outputs found: check if we're missing some required items from the input slots and display a message about it if possible
                if (!outputsFound && infoArea != null)
                {
                    foreach (var (inputItem, deconstructItem) in GetAvailableOutputs(checkRequiredOtherItems: false))
                    {
                        if (deconstructItem.RequiredOtherItem.Any() && !string.IsNullOrEmpty(deconstructItem.InfoTextOnOtherItemMissing))
                        {
                            LocalizedString missingItemName = TextManager.Get("entityname." + deconstructItem.RequiredOtherItem.First());
                            infoArea.Text = TextManager.GetWithVariable(deconstructItem.InfoTextOnOtherItemMissing, "[itemname]", missingItemName);
                        }
                    }
                }
                activateButton.Enabled = outputsFound || !InputContainer.Inventory.IsEmpty();
                activateButton.Text = TextManager.Get(ActivateButtonText);
            };
        }

        public override bool Select(Character character)
        {
            // TODO, This works fine as of now but if GUI.PreventElementOverlap ever gets fixed this block of code may become obsolete or detrimental.
            // Only do this if there's only one linked component. If you link more containers then may
            // GUI.PreventElementOverlap have mercy on your HUD layout
            if (GuiFrame != null && item.linkedTo.Count(entity => entity is Item { DisplaySideBySideWhenLinked: true }) == 1)
            {
                foreach (MapEntity linkedTo in item.linkedTo)
                {
                    if (!(linkedTo is Item { DisplaySideBySideWhenLinked: true } linkedItem)) { continue; }
                    if (!linkedItem.Components.Any()) { continue; }

                    var itemContainer = linkedItem.GetComponent<ItemContainer>();
                    if (itemContainer?.GuiFrame == null || itemContainer.AllowUIOverlap) { continue; }

                    // how much spacing do we want between the components
                    var padding = (int) (8 * GUI.Scale);
                    // Move the linked container to the right and move the deconstructor to the left
                    itemContainer.GuiFrame.RectTransform.AbsoluteOffset = new Point(GuiFrame.Rect.Width / -2 - padding, 0);
                    GuiFrame.RectTransform.AbsoluteOffset = new Point(itemContainer.GuiFrame.Rect.Width / 2 + padding, 0);
                }
            }
            return base.Select(character);
        }

        partial void OnItemSlotsChanged(ItemContainer container)
        {
            if (container.Inventory is null) { return; }
            RefreshOutputDisplay(container.Inventory.AllItems.ToImmutableArray());
        }

        private void RefreshOutputDisplay(ImmutableArray<Item> items)
        {
            const string outputItemCountUserData = "OutputItemCount";
            const string questionMarkUserData = "UnknownItemOutput";

            if (outputDisplayListBox is null || inputContainer.Inventory is null) { return; }

            Dictionary<Identifier, int> itemCounts = new Dictionary<Identifier, int>();
            Dictionary<Identifier, GUIComponent> children = new Dictionary<Identifier, GUIComponent>();

            bool addQuestionMark = false;

            foreach (GUIComponent child in outputDisplayListBox.Content.Children)
            {
                if (child.UserData is Identifier it)
                {
                    children.Add(it, child);
                }
            }

            if (outputDisplayListBox.Content.FindChild(questionMarkUserData) is { } foundChild)
            {
                outputDisplayListBox.RemoveChild(foundChild);
            }

            foreach (Item it in items)
            {
                if (it.Prefab.RandomDeconstructionOutput)
                {
                    addQuestionMark = true;
                    continue;
                }

                foreach (DeconstructItem deconstructItem in it.Prefab.DeconstructItems)
                {
                    if (!deconstructItem.IsValidDeconstructor(item)) { continue; }
                    RegisterItem(deconstructItem.ItemIdentifier, deconstructItem.Amount);
                }

                /*if (it.OwnInventory is { } inventory)
                {
                    foreach (Item inventoryItems in inventory.AllItems)
                    {
                        RegisterItem(inventoryItems.Prefab.Identifier);
                    }
                }*/

                void RegisterItem(Identifier identifier, int amount = 1)
                {
                    if (itemCounts.ContainsKey(identifier))
                    {
                        itemCounts[identifier] += amount;
                        return;
                    }
                    itemCounts.Add(identifier, amount);
                }
            }

            foreach (var (it, child) in children)
            {
                if (!itemCounts.ContainsKey(it))
                {
                    outputDisplayListBox.RemoveChild(child);
                }
            }

            foreach (var (it, amount) in itemCounts)
            {
                if (!children.TryGetValue(it, out GUIComponent child))
                {
                    child = CreateOutputDisplayItem(it, outputDisplayListBox.Content);
                }

                if (child is null) { continue; }
                UpdateOutputDisplayItemCount(child, amount);
            }

            if (addQuestionMark)
            {
                CreateQuestionMark(outputDisplayListBox.Content);
            }

            static void CreateQuestionMark(GUIComponent parent)
            {
                GUIFrame itemFrame = new GUIFrame(new RectTransform(new Vector2(0.1f, 1f), parent.RectTransform), style: null)
                {
                    UserData = questionMarkUserData,
                    ToolTip = TextManager.Get("deconstructor.unknownitemsoutput")
                };

                GUIFrame questionMarkFrame = new GUIFrame(new RectTransform(Vector2.One, itemFrame.RectTransform, scaleBasis: ScaleBasis.Smallest, anchor: Anchor.Center), style: "GUIFrameListBox")
                {
                    CanBeFocused = false,
                };

                // question mark text
                new GUITextBlock(new RectTransform(Vector2.One, questionMarkFrame.RectTransform, anchor: Anchor.Center), text: "?", textAlignment: Alignment.Center, font: GUIStyle.LargeFont)
                {
                    CanBeFocused = false
                };
            }

            static GUIComponent CreateOutputDisplayItem(Identifier identifier, GUIComponent parent)
            {
                ItemPrefab prefab = ItemPrefab.Find(null, identifier);
                if (prefab is null) { return null; }

                GUIFrame itemFrame = new GUIFrame(new RectTransform(new Vector2(0.1f, 1f), parent.RectTransform), style: null)
                {
                    UserData = identifier,
                    ToolTip = GetTooltip(prefab)
                };

                Sprite icon = prefab.InventoryIcon ?? prefab.Sprite;
                Color iconColor = prefab.InventoryIcon is null ? prefab.SpriteColor : prefab.InventoryIconColor;

                GUIImage itemIcon = new GUIImage(new RectTransform(Vector2.One, itemFrame.RectTransform, scaleBasis: ScaleBasis.Smallest, anchor: Anchor.Center), sprite: icon, scaleToFit: true)
                {
                    Color = iconColor,
                    CanBeFocused = false
                };

                // item count text
                new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.5f), itemIcon.RectTransform, anchor: Anchor.BottomRight), "", font: GUIStyle.Font, textAlignment: Alignment.BottomRight)
                {
                    UserData = outputItemCountUserData,
                    Shadow = true,
                    CanBeFocused = false,
                    Padding = Vector4.Zero,
                    TextColor = Color.White,
                };

                return itemFrame;
            }

            static void UpdateOutputDisplayItemCount(GUIComponent component, int count)
            {
                if (!(component.FindChild(outputItemCountUserData, recursive: true) is GUITextBlock textBlock)) { return; }

                textBlock.Text = TextManager.GetWithVariable("campaignstore.quantity", "[amount]", count.ToString());
            }

            static RichString GetTooltip(ItemPrefab prefab)
            {
                LocalizedString toolTip = $"‖color:{Color.White.ToStringHex()}‖{prefab.Name}‖color:end‖";

                LocalizedString description = prefab.Description;
                if (!description.IsNullOrEmpty()) { toolTip += '\n' + description; }

                if (prefab.ContentPackage != GameMain.VanillaContent && prefab.ContentPackage != null)
                {
                    toolTip += $"\n‖color:{Color.MediumPurple.ToStringHex()}‖{prefab.ContentPackage.Name}‖color:end‖";
                }

                return RichString.Rich(toolTip);
            }
        }

        partial void OnItemLoadedProjSpecific()
        {
            inputContainer.AllowUIOverlap = true;
            inputContainer.Inventory.RectTransform = inputInventoryHolder.RectTransform;
            outputContainer.AllowUIOverlap = true;
            outputContainer.Inventory.RectTransform = outputInventoryHolder.RectTransform;

            inputContainer.Inventory.Locked = IsActive;
        }

        private void DrawOverLay(SpriteBatch spriteBatch, GUICustomComponent overlayComponent)
        {
            overlayComponent.RectTransform.SetAsLastChild();

            if (!(inputContainer?.Inventory?.visualSlots is { } visualSlots)) { return; }

            if (DeconstructItemsSimultaneously)
            {
                for (int i = 0; i < InputContainer.Inventory.Capacity; i++)
                {
                    if (InputContainer.Inventory.GetItemAt(i) == null) { continue; }
                    DrawProgressBar(InputContainer.Inventory.visualSlots[i]);
                }
            }
            else
            {
                DrawProgressBar(inputContainer.Inventory.visualSlots.Last());
            }

            void DrawProgressBar(VisualSlot slot)
            {
                GUI.DrawRectangle(spriteBatch,
                    new Rectangle(
                        slot.Rect.X, slot.Rect.Y + (int)(slot.Rect.Height * (1.0f - progressState)),
                        slot.Rect.Width, (int)(slot.Rect.Height * progressState)),
                    GUIStyle.Green * 0.5f, isFilled: true);
            }
        }

        public override void UpdateHUD(Character character, float deltaTime, Camera cam)
        {
            inSufficientPowerWarning.Visible = IsActive && !hasPower;
        }

        private bool OnActivateButtonClicked(GUIButton button, object obj)
        {
            if (!IsActive)
            {
                //don't allow turning on if there's non-deconstructable items in the queue
                var disallowedItem = inputContainer.Inventory.FindItem(i => !i.AllowDeconstruct, recursive: false);
                if (disallowedItem != null && !DeconstructItemsSimultaneously)
                {
                    int index = inputContainer.Inventory.FindIndex(disallowedItem);
                    if (index >= 0 && index < inputContainer.Inventory.visualSlots.Length)
                    {
                        var slot = inputContainer.Inventory.visualSlots[index];
                        slot?.ShowBorderHighlight(GUIStyle.Red, 0.1f, 0.9f);
                    }
                    return true;
                }
            }

            if (GameMain.Client != null)
            {
                pendingState = !IsActive;
                item.CreateClientEvent(this);
            }
            else
            {
                SetActive(!IsActive, Character.Controlled);
            }
            return true;
        }

        public void ClientEventWrite(IWriteMessage msg, NetEntityEvent.IData extraData = null)
        {
            msg.WriteBoolean(pendingState);
        }

        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            ushort userID = msg.ReadUInt16();
            Character user = userID == Entity.NullEntityID ? null : Entity.FindEntityByID(userID) as Character;
            SetActive(msg.ReadBoolean(), user);
            progressTimer = msg.ReadSingle();
        }
    }
}
