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
        private GUICustomComponent inputInventoryOverlay;

        private GUIComponent inSufficientPowerWarning;

        private bool pendingState;

        partial void InitProjSpecific(XElement element)
        {
            CreateGUI();
            GameMain.Instance.OnResolutionChanged += () =>
            {
                GuiFrame.ClearChildren();
                CreateGUI();
                OnItemLoadedProjSpecific();
            };
        }

        private void CreateGUI()
        {
            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.90f, 0.80f), GuiFrame.RectTransform, Anchor.Center), childAnchor: Anchor.TopCenter)
            {
                Stretch = true, 
                RelativeSpacing = 0.08f
            };

            var topFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.5f), paddedFrame.RectTransform), style: null);
                
                // === INPUT LABEL === //
                var inputLabelArea = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.15f), topFrame.RectTransform, Anchor.TopCenter), childAnchor: Anchor.CenterLeft, isHorizontal: true)
                {
                    Stretch = true, 
                    RelativeSpacing = 0.05f
                };
                    var inputLabel = new GUITextBlock(new RectTransform(Vector2.One, inputLabelArea.RectTransform), TextManager.Get("uilabel.input"), font: GUI.SubHeadingFont) { Padding = Vector4.Zero };
                    inputLabel.RectTransform.Resize(new Point((int) inputLabel.Font.MeasureString(inputLabel.Text).X, inputLabel.RectTransform.Rect.Height));
                    new GUIFrame(new RectTransform(Vector2.One, inputLabelArea.RectTransform), style: "HorizontalLine");

                var inputArea = new GUILayoutGroup(new RectTransform(new Vector2(1f, 1f), topFrame.RectTransform, Anchor.CenterLeft), childAnchor: Anchor.BottomLeft, isHorizontal: true) { Stretch = true, RelativeSpacing = 0.05f };
                    
                    // === INPUT SLOTS === //
                    inputInventoryHolder = new GUIFrame(new RectTransform(new Vector2(0.7f, 1f), inputArea.RectTransform), style: null);
                        inputInventoryOverlay = new GUICustomComponent(new RectTransform(Vector2.One, inputInventoryHolder.RectTransform), DrawOverLay, null) { CanBeFocused = false };

                    // === ACTIVATE BUTTON === //
                    var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.4f, 0.7f), inputArea.RectTransform), childAnchor: Anchor.CenterLeft);
                        activateButton = new GUIButton(new RectTransform(new Vector2(0.95f, 0.8f), buttonContainer.RectTransform), TextManager.Get("DeconstructorDeconstruct"), style: "DeviceButton")
                        {
                            TextBlock = { AutoScaleHorizontal = true },
                            OnClicked = ToggleActive
                        };
                            inSufficientPowerWarning = new GUITextBlock(new RectTransform(Vector2.One, activateButton.RectTransform), 
                                TextManager.Get("DeconstructorNoPower"), textColor: GUI.Style.Orange, textAlignment: Alignment.Center, color: Color.Black, style: "OuterGlow")
                            {
                                HoverColor = Color.Black, 
                                IgnoreLayoutGroups = true, 
                                Visible = false, 
                                CanBeFocused = false
                            };

            // === OUTPUT AREA === //
            var bottomFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.5f), paddedFrame.RectTransform), style: null);
                
                // === OUTPUT LABEL === //
                var outputLabelArea = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.15f), bottomFrame.RectTransform, Anchor.TopCenter), childAnchor: Anchor.CenterLeft, isHorizontal: true)
                {
                    Stretch = true, 
                    RelativeSpacing = 0.05f
                };
                    var outputLabel = new GUITextBlock(new RectTransform(new Vector2(0f, 1.0f), outputLabelArea.RectTransform), TextManager.Get("uilabel.output"), font: GUI.SubHeadingFont) { Padding = Vector4.Zero };
                    outputLabel.RectTransform.Resize(new Point((int) outputLabel.Font.MeasureString(outputLabel.Text).X, outputLabel.RectTransform.Rect.Height));
                    new GUIFrame(new RectTransform(Vector2.One, outputLabelArea.RectTransform), style: "HorizontalLine");

                // === OUTPUT SLOTS === //
                outputInventoryHolder = new GUIFrame(new RectTransform(new Vector2(1f, 1f), bottomFrame.RectTransform, Anchor.CenterLeft), style: null);
        }

        public override bool Select(Character character)
        {
            // TODO, This works fine as of now but if GUI.PreventElementOverlap ever gets fixed this block of code may become obsolete or detrimental.
            // Only do this if there's only one linked component. If you link more containers then may
            // GUI.PreventElementOverlap have mercy on your HUD layout
            if (item.linkedTo.Count(entity => entity is Item item && item.DisplaySideBySideWhenLinked) == 1)
            {
                foreach (MapEntity linkedTo in item.linkedTo)
                {
                    if (!(linkedTo is Item linkedItem)) continue;
                    if (!linkedItem.Components.Any()) continue;
                
                    var itemContainer = linkedItem.Components.First();
                    if (itemContainer == null) { continue; }

                    if (!itemContainer.Item.DisplaySideBySideWhenLinked) continue;

                    // how much spacing do we want between the components
                    var padding = (int) (8 * GUI.Scale);
                    // Move the linked container to the right and move the fabricator to the left
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
            var lastSlot = inputContainer.Inventory.slots.Last();

            GUI.DrawRectangle(spriteBatch, 
                new Rectangle(
                    lastSlot.Rect.X, lastSlot.Rect.Y + (int)(lastSlot.Rect.Height * (1.0f - progressState)), 
                    lastSlot.Rect.Width, (int)(lastSlot.Rect.Height * progressState)), 
                GUI.Style.Green * 0.5f, isFilled: true);
        }

        public override void UpdateHUD(Character character, float deltaTime, Camera cam)
        {
            inSufficientPowerWarning.Visible = CurrPowerConsumption > 0 && !hasPower;
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
                currPowerConsumption = IsActive ? powerConsumption : 0.0f;
            }

            return true;
        }

        public void ClientWrite(IWriteMessage msg, object[] extraData = null)
        {
            msg.Write(pendingState);
        }

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            SetActive(msg.ReadBoolean());
            progressTimer = msg.ReadSingle();
        }
    }
}
