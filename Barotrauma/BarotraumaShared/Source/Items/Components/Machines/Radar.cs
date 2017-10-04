using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Radar : Powered, IServerSerializable, IClientSerializable
    {
        private float range;

        private float pingState;

        private readonly Sprite pingCircle, screenOverlay;

        private readonly Sprite radarBlip;

        private float prevPingRadius;

        float prevPassivePingRadius;

        private Vector2 center;
        private float displayRadius;
        private float displayScale;
        
        private float displayBorderSize;
                
        [HasDefaultValue(10000.0f, false)]
        public float Range
        {
            get { return range; }
            set { range = MathHelper.Clamp(value, 0.0f, 100000.0f); }
        }
        
        [HasDefaultValue(false, false)]
        public bool DetectSubmarineWalls
        {
            get;
            set;
        }

        public override bool IsActive
        {
            get
            {
                return base.IsActive;
            }

            set
            {
                base.IsActive = value;
#if CLIENT
                if (isActiveTickBox != null) isActiveTickBox.Selected = value;
#endif
            }
        }

        public Radar(Item item, XElement element)
            : base(item, element)
        {
#if CLIENT
            radarBlips = new List<RadarBlip>();
#endif

            displayBorderSize = element.GetAttributeFloat("displaybordersize", 0.0f);

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "pingcircle":
                        pingCircle = new Sprite(subElement);
                        break;
                    case "screenoverlay":
                        screenOverlay = new Sprite(subElement);
                        break;
                    case "blip":
                        radarBlip = new Sprite(subElement);
                        break;
                }
            }

#if CLIENT
            isActiveTickBox = new GUITickBox(new Rectangle(0, 0, 20, 20), "Active Sonar", Alignment.TopLeft, GuiFrame);
            isActiveTickBox.OnSelected = (GUITickBox box) =>
            {
                if (GameMain.Server != null)
                {
                    item.CreateServerEvent(this);
                }
                else if (GameMain.Client != null)
                {
                    item.CreateClientEvent(this);
                    correctionTimer = CorrectionDelay;
                }
                IsActive = box.Selected;

                return true;
            };
            
            GuiFrame.CanBeFocused = false;
#endif

            IsActive = false;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            currPowerConsumption = powerConsumption;

            base.Update(deltaTime, cam);
            
            if (voltage >= minVoltage || powerConsumption <= 0.0f)
            {
                pingState = pingState + deltaTime * 0.5f;
                if (pingState > 1.0f)
                {
                    if (item.CurrentHull != null) item.CurrentHull.AiTarget.SoundRange = Math.Max(Range * pingState, item.CurrentHull.AiTarget.SoundRange);
                    item.Use(deltaTime);
                    pingState = 0.0f;
                }
            }
            else
            {
                pingState = 0.0f;
            }

            Voltage -= deltaTime;
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            return pingState > 1.0f;
        }
        
        protected override void RemoveComponentSpecific()
        {
            if (pingCircle!=null) pingCircle.Remove();
            if (screenOverlay != null) screenOverlay.Remove();
        }

        public void ServerRead(ClientNetObject type, Lidgren.Network.NetBuffer msg, Barotrauma.Networking.Client c)
        {
            bool isActive = msg.ReadBoolean();

            if (!item.CanClientAccess(c)) return; 

            IsActive = isActive;
#if CLIENT
            isActiveTickBox.Selected = IsActive;
#endif

            item.CreateServerEvent(this);
        }

        public void ServerWrite(Lidgren.Network.NetBuffer msg, Barotrauma.Networking.Client c, object[] extraData = null)
        {
            msg.Write(IsActive);
        }
    }
}
