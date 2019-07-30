using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;

namespace Barotrauma
{
    partial class PhysicsBody
    {
        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
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
                body.LinearVelocity = new Vector2(
                    MathHelper.Clamp(body.LinearVelocity.X, -MaxVel, MaxVel),
                    MathHelper.Clamp(body.LinearVelocity.Y, -MaxVel, MaxVel));
                msg.WriteRangedSingle(body.LinearVelocity.X, -MaxVel, MaxVel, 12);
                msg.WriteRangedSingle(body.LinearVelocity.Y, -MaxVel, MaxVel, 12);
                if (!FarseerBody.FixedRotation)
                {
                    body.AngularVelocity = MathHelper.Clamp(body.AngularVelocity, -MaxAngularVel, MaxAngularVel);
                    msg.WriteRangedSingle(body.AngularVelocity, -MaxAngularVel, MaxAngularVel, 8);
                }
            }

            msg.WritePadBits();
        }
    }
}
