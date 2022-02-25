using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Linq;
using System.Xml.Linq;

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

        private GUIComponent inSufficientPowerWarning;

        private bool pendingState;

        private GUITextBlock infoArea;

        [Serialize("DeconstructorDeconstruct", IsPropertySaveable.Yes)]
        public string ActivateButtonText { get; set; }
        
        [Serialize("", IsPropertySaveable.Yes)]
        public string InfoText { get; set; }

        [Serialize(0.0f, IsPropertySaveable.Yes)]
        public float InfoAreaWidth { get; set; }

        partial void InitProjSpecific(XElement element)
        {
            CreateGUI();
        }

        protected override void OnResolutionChanged()
        {
            base.OnResolutionChanged();
            OnItemLoadedProjSpecific();
        }

        protected override void CreateGUI()
        {
            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.90f, 0.80f), GuiFrame.RectTransform, Anchor.Center), childAnchor: Anchor.TopCenter)
            {
                Stretch = true, 
                RelativeSpacing = 0.08f
            };

            new GUITextBlock(new RectTransform(new Vector2(1f, 0.07f), paddedFrame.RectTransform), item.Name, font: GUIStyle.SubHeadingFont)
            {
                TextAlignment = Alignment.Center,
                AutoScaleHorizontal = true
            };

            var topFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.5f), paddedFrame.RectTransform), style: null);
                
                // === INPUT LABEL === //
                var inputLabelArea = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.15f), topFrame.RectTransform, Anchor.TopCenter), childAnchor: Anchor.CenterLeft, isHorizontal: true)
                {
                    Stretch = true, 
                    RelativeSpacing = 0.05f
                };
                    var inputLabel = new GUITextBlock(new RectTransform(Vector2.One, inputLabelArea.RectTransform), TextManager.Get("deconstructor.input", "uilabel.input"), font: GUIStyle.SubHeadingFont) { Padding = Vector4.Zero };
                    inputLabel.RectTransform.Resize(new Point((int) inputLabel.Font.MeasureString(inputLabel.Text).X, inputLabel.RectTransform.Rect.Height));
                    new GUIFrame(new RectTransform(Vector2.One, inputLabelArea.RectTransform), style: "HorizontalLine");

                var inputArea = new GUILayoutGroup(new RectTransform(new Vector2(1f, 1f), topFrame.RectTransform, Anchor.CenterLeft), childAnchor: Anchor.BottomLeft, isHorizontal: true) { Stretch = true, RelativeSpacing = 0.05f };
                    
                    // === INPUT SLOTS === //
                    inputInventoryHolder = new GUIFrame(new RectTransform(new Vector2(0.7f, 1f), inputArea.RectTransform), style: null);
                        new GUICustomComponent(new RectTransform(Vector2.One, inputInventoryHolder.RectTransform), DrawOverLay, null) { CanBeFocused = false };

                    // === ACTIVATE BUTTON === //
                    var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.4f, 0.8f), inputArea.RectTransform), childAnchor: Anchor.CenterLeft);
                        activateButton = new GUIButton(new RectTransform(new Vector2(0.95f, 0.8f), buttonContainer.RectTransform), TextManager.Get("DeconstructorDeconstruct"), style: "DeviceButton")
                        {
                            TextBlock = { AutoScaleHorizontal = true },
                            OnClicked = ToggleActive
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
            var bottomFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.5f), paddedFrame.RectTransform), style: null);
                
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
                activateButton.Enabled = outputsFound;
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

        partial void OnItemLoadedProjSpecific()
        {
            inputContainer.AllowUIOverlap = true;
            inputContainer.Inventory.RectTransform = inputInventoryHolder.RectTransform;
            outputContainer.AllowUIOverlap = true;
            outputContainer.Inventory.RectTransform = outputInventoryHolder.RectTransform;
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

        private bool ToggleActive(GUIButton button, object obj)
        {
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

        public void ClientWrite(IWriteMessage msg, object[] extraData = null)
        {
            msg.Write(pendingState);
        }

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            ushort userID = msg.ReadUInt16();
            Character user = userID == Entity.NullEntityID ? null : Entity.FindEntityByID(userID) as Character;
            SetActive(msg.ReadBoolean(), user);
            progressTimer = msg.ReadSingle();
        }
    }
}
