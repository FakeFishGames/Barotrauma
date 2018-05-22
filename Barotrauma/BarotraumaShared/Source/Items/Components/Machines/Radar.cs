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

        private bool aiPingCheckPending;

        [Serialize(10000.0f, false)]
        public float Range
        {
            get { return range; }
            set { range = MathHelper.Clamp(value, 0.0f, 100000.0f); }
        }

        [Serialize(false, false)]
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
            
            IsActive = false;
            InitProjSpecific(element);
        }

        partial void InitProjSpecific(XElement element);

        public override void Update(float deltaTime, Camera cam)
        {
            currPowerConsumption = powerConsumption;

            UpdateOnActiveEffects(deltaTime);
            
            if (voltage >= minVoltage || powerConsumption <= 0.0f)
            {
                pingState = pingState + deltaTime * 0.5f;
                if (pingState > 1.0f)
                {
                    if (item.CurrentHull != null) item.CurrentHull.AiTarget.SoundRange = Math.Max(Range * pingState, item.CurrentHull.AiTarget.SoundRange);
                    if (item.AiTarget != null) item.AiTarget.SoundRange = Math.Max(Range * pingState, item.AiTarget.SoundRange);
                    aiPingCheckPending = true;
                    item.Use(deltaTime);
                    pingState = 0.0f;
                }
            }
            else
            {
                aiPingCheckPending = false;
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

        public override bool AIOperate(float deltaTime, Character character, AIObjectiveOperateItem objective)
        {
            if (!IsActive || !aiPingCheckPending) return false;

            Dictionary<string, List<Character>> targetGroups = new Dictionary<string, List<Character>>();

            foreach (Character c in Character.CharacterList)
            {
                if (c.AnimController.CurrentHull != null || !c.Enabled) continue;
                if (DetectSubmarineWalls && c.AnimController.CurrentHull == null && item.CurrentHull != null) continue;
                if (Vector2.DistanceSquared(c.WorldPosition, item.WorldPosition) > range * range) continue;

                string directionName = GetDirectionName(c.WorldPosition - item.WorldPosition);

                if (!targetGroups.ContainsKey(directionName))
                {
                    targetGroups.Add(directionName, new List<Character>());
                }
                targetGroups[directionName].Add(c);
            }

            foreach (KeyValuePair<string, List<Character>> targetGroup in targetGroups)
            {
                string dialogTag = "DialogSonarTarget";
                if (targetGroup.Value.Count > 1)
                {
                    dialogTag = "DialogSonarTargetMultiple";
                }
                else if (targetGroup.Value[0].Mass > 100.0f)
                {
                    dialogTag = "DialogSonarTargetLarge";
                }
                character.Speak(TextManager.Get(dialogTag).Replace("[direction]", targetGroup.Key).Replace("[count]", targetGroup.Value.Count.ToString()),
                    null, 0, "sonartarget" + targetGroup.Value[0].ID, 30);

                //prevent the character from reporting other targets in the group
                for (int i = 1; i < targetGroup.Value.Count; i++)
                {
                    character.DisableLine("sonartarget" + targetGroup.Value[i].ID);
                }
            }

            return true;
        }

        private string GetDirectionName(Vector2 dir)
        {
            float angle = MathUtils.WrapAngleTwoPi((float)-Math.Atan2(dir.Y, dir.X) + MathHelper.PiOver2);

            int clockDir = (int)Math.Round((angle / MathHelper.TwoPi) * 12);
            if (clockDir == 0) clockDir = 12;

            return TextManager.Get("SubDirOClock").Replace("[dir]", clockDir.ToString());
        }

        public void ServerRead(ClientNetObject type, Lidgren.Network.NetBuffer msg, Client c)
        {
            bool isActive = msg.ReadBoolean();

            if (!item.CanClientAccess(c)) return; 

            IsActive = isActive;
#if CLIENT
            isActiveTickBox.Selected = IsActive;
#endif

            item.CreateServerEvent(this);
        }

        public void ServerWrite(Lidgren.Network.NetBuffer msg, Client c, object[] extraData = null)
        {
            msg.Write(IsActive);
        }
    }
}
