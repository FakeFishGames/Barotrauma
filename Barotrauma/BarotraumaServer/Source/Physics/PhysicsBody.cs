using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    partial class PhysicsBody
    {
        public void ServerWrite(NetBuffer msg, Client c, object[] extraData = null)
        {
            float MaxVel = NetConfig.MaxPhysicsBodyVelocity;
            float MaxAngularVel = NetConfig.MaxPhysicsBodyAngularVelocity;

            msg.Write(SimPosition.X);
            msg.Write(SimPosition.Y);

#if DEBUG
            if (Math.Abs(body.LinearVelocity.X) > MaxVel || 
                Math.Abs(body.LinearVelocity.Y) > MaxVel)
            {
                DebugConsole.ThrowError("Item velocity out of range (" + body.LinearVelocity + ")");
            }
#endif

            msg.Write(FarseerBody.Awake);
            msg.Write(FarseerBody.FixedRotation);

            if (!FarseerBody.FixedRotation)
            {
                msg.WriteRangedSingle(MathUtils.WrapAngleTwoPi(body.Rotation), 0.0f, MathHelper.TwoPi, 8);
            }
            if (FarseerBody.Awake)
            {
                body.Enabled = true;
                msg.WriteRangedSingle(MathHelper.Clamp(body.LinearVelocity.X, -MaxVel, MaxVel), -MaxVel, MaxVel, 12);
                msg.WriteRangedSingle(MathHelper.Clamp(body.LinearVelocity.Y, -MaxVel, MaxVel), -MaxVel, MaxVel, 12);
                if (!FarseerBody.FixedRotation)
                {
                    msg.WriteRangedSingle(MathHelper.Clamp(body.AngularVelocity, -MaxAngularVel, MaxAngularVel), -MaxAngularVel, MaxAngularVel, 8);
                }
            }

            msg.WritePadBits();
        }
    }
}
