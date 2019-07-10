using Barotrauma.Networking;
using Lidgren.Network;
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
            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.9f), GuiFrame.RectTransform, Anchor.Center), childAnchor: Anchor.TopCenter)
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            inputInventoryHolder = new GUIFrame(new RectTransform(new Vector2(0.2f, 0.7f), paddedFrame.RectTransform), style: null);
            inputInventoryOverlay = new GUICustomComponent(new RectTransform(Vector2.One, inputInventoryHolder.RectTransform), DrawOverLay, null)
            {
                CanBeFocused = false
            };

            activateButton = new GUIButton(new RectTransform(new Vector2(0.8f, 0.1f), paddedFrame.RectTransform),
                TextManager.Get("DeconstructorDeconstruct"))
            {
                OnClicked = ToggleActive
            };


            inSufficientPowerWarning = new GUITextBlock(new RectTransform(Vector2.One, activateButton.RectTransform), TextManager.Get("DeconstructorNoPower"),
                textColor: Color.Orange, textAlignment: Alignment.Center, color: Color.Black, style: "OuterGlow")
            {
                HoverColor = Color.Black,
                IgnoreLayoutGroups = true,
                Visible = false,
                CanBeFocused = false
            };
            outputInventoryHolder = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.3f), paddedFrame.RectTransform), style: null);
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
                Color.Green * 0.5f, isFilled: true);
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

        public void ClientWrite(NetBuffer msg, object[] extraData = null)
        {
            msg.Write(pendingState);
        }

        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            SetActive(msg.ReadBoolean());
            progressTimer = msg.ReadSingle();
        }
    }
}
