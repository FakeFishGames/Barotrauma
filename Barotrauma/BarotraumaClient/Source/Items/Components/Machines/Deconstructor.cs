using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Deconstructor : Powered, IServerSerializable, IClientSerializable
    {
        private GUIProgressBar progressBar;
        private GUIButton activateButton;
        
        public override void AddToGUIUpdateList()
        {
            GuiFrame.AddToGUIUpdateList();
        }
        
        partial void InitProjSpecific(XElement element)
        {
            var paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.8f), GuiFrame.RectTransform, Anchor.Center), style: null);

            progressBar = new GUIProgressBar(new RectTransform(new Vector2(1.0f, 0.15f), paddedFrame.RectTransform, Anchor.BottomCenter),
                barSize: 0.0f, color: Color.Green);

            activateButton = new GUIButton(new RectTransform(new Point(200, 30), paddedFrame.RectTransform, Anchor.Center),
                TextManager.Get("DeconstuctorDeconstruct"))
            {
                OnClicked = ToggleActive
            };
        }

        private bool ToggleActive(GUIButton button, object obj)
        {
            SetActive(!IsActive, Character.Controlled);

            currPowerConsumption = IsActive ? powerConsumption : 0.0f;
            
            if (GameMain.Client != null)
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
