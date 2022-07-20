using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;

namespace Barotrauma
{
    partial class PhysicsBody
    {
        public void ServerWrite(IWriteMessage msg)
        {
            float MaxVel = NetConfig.MaxPhysicsBodyVelocity;
            float MaxAngularVel = NetConfig.MaxPhysicsBodyAngularVelocity;

            msg.Write(SimPosition.X);
            msg.Write(SimPosition.Y);

#if DEBUG
            if (Math.Abs(FarseerBody.LinearVelocity.X) > MaxVel || 
                Math.Abs(FarseerBody.LinearVelocity.Y) > MaxVel)
            {
                DebugConsole.ThrowError($"Entity velocity out of range ({(UserData?.ToString() ?? "null")}, {FarseerBody.LinearVelocity})");
            }
#endif

            msg.Write(FarseerBody.Awake);
            msg.Write(FarseerBody.FixedRotation);

            if (!FarseerBody.FixedRotation)
            {
                msg.WriteRangedSingle(MathUtils.WrapAngleTwoPi(FarseerBody.Rotation), 0.0f, MathHelper.TwoPi, 8);
            }
            if (FarseerBody.Awake)
            {
                FarseerBody.Enabled = true;
                FarseerBody.LinearVelocity = new Vector2(
                    MathHelper.Clamp(FarseerBody.LinearVelocity.X, -MaxVel, MaxVel),
                    MathHelper.Clamp(FarseerBody.LinearVelocity.Y, -MaxVel, MaxVel));
                msg.WriteRangedSingle(FarseerBody.LinearVelocity.X, -MaxVel, MaxVel, 12);
                msg.WriteRangedSingle(FarseerBody.LinearVelocity.Y, -MaxVel, MaxVel, 12);
                if (!FarseerBody.FixedRotation)
                {
                    FarseerBody.AngularVelocity = MathHelper.Clamp(FarseerBody.AngularVelocity, -MaxAngularVel, MaxAngularVel);
                    msg.WriteRangedSingle(FarseerBody.AngularVelocity, -MaxAngularVel, MaxAngularVel, 8);
                }
            }

            msg.WritePadBits();
        }
    }
}
