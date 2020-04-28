using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class PowerContainer : Powered, IDrawableComponent, IServerSerializable, IClientSerializable
    {
        public void ServerRead(ClientNetObject type, IReadMessage msg, Client c)
        {
            float newRechargeSpeed = msg.ReadRangedInteger(0, 10) / 10.0f * maxRechargeSpeed;

            if (item.CanClientAccess(c))
            {
                RechargeSpeed = newRechargeSpeed;
                GameServer.Log(GameServer.CharacterLogName(c.Character) + " set the recharge speed of " + item.Name + " to " + (int)((rechargeSpeed / maxRechargeSpeed) * 100.0f) + " %", ServerLog.MessageType.ItemInteraction);
            }

            item.CreateServerEvent(this);
        }

        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            msg.WriteRangedInteger((int)(rechargeSpeed / MaxRechargeSpeed * 10), 0, 10);

            float chargeRatio = MathHelper.Clamp(charge / capacity, 0.0f, 1.0f);
            msg.WriteRangedSingle(chargeRatio, 0.0f, 1.0f, 8);
        }
    }
}
