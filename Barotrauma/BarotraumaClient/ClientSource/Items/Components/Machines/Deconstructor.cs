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
            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.8f), GuiFrame.RectTransform, Anchor.Center), childAnchor: Anchor.TopCenter)
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            var topFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.5f), paddedFrame.RectTransform), style: null);
            var paddedLine = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.25f), topFrame.RectTransform, Anchor.TopCenter), childAnchor: Anchor.CenterLeft, isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };
            var inputText = new GUITextBlock(new RectTransform(new Vector2(0f, 1.0f), paddedLine.RectTransform), TextManager.Get("uilabel.input"), font: GUI.SubHeadingFont) { Padding = Vector4.Zero };
            new GUIFrame(new RectTransform(new Vector2(1f, 1.0f), paddedLine.RectTransform), style: "HorizontalLine");
            
            // Resize GUITextBlock width according to the text length
            inputText.RectTransform.Resize(new Point((int)inputText.Font.MeasureString(inputText.Text).X, inputText.RectTransform.Rect.Height));

            
            var inputArea = new GUILayoutGroup(new RectTransform(new Vector2(1f, 1.2f), topFrame.RectTransform, Anchor.CenterLeft), childAnchor: Anchor.BottomLeft, isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.045f
            };
            inputInventoryHolder = new GUIFrame(new RectTransform(new Vector2(0.7f, 1f), inputArea.RectTransform), style: null);
            inputInventoryOverlay = new GUICustomComponent(new RectTransform(Vector2.One, inputInventoryHolder.RectTransform), DrawOverLay, null)
            {
                CanBeFocused = false
            };

            var buttonContainer = new GUIFrame(new RectTransform(new Vector2(0.4f, 0.75f), inputArea.RectTransform), style: null);
            activateButton = new GUIButton(new RectTransform(new Vector2(0.95f, 0.65f), buttonContainer.RectTransform, Anchor.CenterLeft),
                TextManager.Get("DeconstructorDeconstruct"), style: "DeviceButton")
            {
                TextBlock = { AutoScale = true },
                OnClicked = ToggleActive
            };


            inSufficientPowerWarning = new GUITextBlock(new RectTransform(Vector2.One, activateButton.RectTransform), TextManager.Get("DeconstructorNoPower"),
                textColor: GUI.Style.Orange, textAlignment: Alignment.Center, color: Color.Black, style: "OuterGlow")
            {
                HoverColor = Color.Black,
                IgnoreLayoutGroups = true,
                Visible = false,
                CanBeFocused = false
            };
            
            var bottomFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.5f), paddedFrame.RectTransform), style: null);
            var paddedBottomLine = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.25f), bottomFrame.RectTransform, Anchor.TopCenter), childAnchor: Anchor.CenterLeft, isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };
            var outputText = new GUITextBlock(new RectTransform(new Vector2(0f, 1.0f), paddedBottomLine.RectTransform), TextManager.Get("uilabel.output"), font: GUI.SubHeadingFont) { Padding = Vector4.Zero };
            new GUIFrame(new RectTransform(new Vector2(1f, 1.0f), paddedBottomLine.RectTransform), style: "HorizontalLine");
            
            // Resize GUITextBlock width according to the text length
            outputText.RectTransform.Resize(new Point((int)outputText.Font.MeasureString(outputText.Text).X, outputText.RectTransform.Rect.Height));

            
            outputInventoryHolder = new GUIFrame(new RectTransform(new Vector2(1f, 1.2f), bottomFrame.RectTransform, Anchor.CenterLeft), style: null);
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
