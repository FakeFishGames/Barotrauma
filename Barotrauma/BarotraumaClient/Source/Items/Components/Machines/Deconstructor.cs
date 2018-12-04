using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Deconstructor : Powered, IServerSerializable, IClientSerializable
    {
        private GUIProgressBar progressBar;
        private GUIButton activateButton;
        private GUIComponent inputInventoryHolder, outputInventoryHolder;
        
        partial void InitProjSpecific(XElement element)
        {
            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), GuiFrame.RectTransform, Anchor.Center), childAnchor: Anchor.TopCenter)
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            inputInventoryHolder = new GUIFrame(new RectTransform(new Vector2(0.25f, 0.5f), paddedFrame.RectTransform), style: null);

            progressBar = new GUIProgressBar(new RectTransform(new Vector2(1.0f, 0.05f), paddedFrame.RectTransform, Anchor.BottomCenter),
                barSize: 0.0f, color: Color.Green);

            activateButton = new GUIButton(new RectTransform(new Vector2(0.6f, 0.1f), paddedFrame.RectTransform, Anchor.Center),
                TextManager.Get("DeconstuctorDeconstruct"))
            {
                OnClicked = ToggleActive
            };

            outputInventoryHolder = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.25f), paddedFrame.RectTransform), style: null);
        }

        public override void OnItemLoaded()
        {
            var itemContainers = item.GetComponents<ItemContainer>().ToList();
            for (int i = 0; i < 2 && i < itemContainers.Count; i++)
            {
                itemContainers[i].AllowUIOverlap = true;
                itemContainers[i].Inventory.RectTransform = i == 0 ? inputInventoryHolder.RectTransform : outputInventoryHolder.RectTransform;
            }
        }

        private bool ToggleActive(GUIButton button, object obj)
        {
            SetActive(!IsActive, Character.Controlled);

            currPowerConsumption = IsActive ? powerConsumption : 0.0f;

            if (GameMain.Server != null)
            {
                item.CreateServerEvent(this);
            }
            else if (GameMain.Client != null)
            {
                item.CreateClientEvent(this);
            }

            return true;
        }

        public void ClientWrite(NetBuffer msg, object[] extraData = null)
        {
            msg.Write(IsActive);
        }

        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            SetActive(msg.ReadBoolean());
        }
    }
}
