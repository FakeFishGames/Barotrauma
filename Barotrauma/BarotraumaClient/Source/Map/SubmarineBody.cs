using FarseerPhysics;
using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class SubmarineBody
    {
        partial void ClientUpdatePosition(float deltaTime)
        {
            if (GameMain.Client == null) { return; }

            Vector2 newVelocity = Body.LinearVelocity;
            Vector2 newPosition = Body.SimPosition;

            Body.CorrectPosition(positionBuffer, out newPosition, out newVelocity, out _, out _);
            Vector2 moveAmount = ConvertUnits.ToDisplayUnits(newPosition - Body.SimPosition);
            newVelocity = newVelocity.ClampLength(100.0f);
            if (!MathUtils.IsValid(newVelocity) || moveAmount.LengthSquared() < 0.0001f)
            {
                return;
            }

            List<Submarine> subsToMove = submarine.GetConnectedSubs();
            foreach (Submarine dockedSub in subsToMove)
            {
                if (dockedSub == submarine) continue;
                //clear the position buffer of the docked subs to prevent unnecessary position corrections
                dockedSub.SubBody.positionBuffer.Clear();
            }

            Submarine closestSub = null;
            if (Character.Controlled == null)
            {
                closestSub = Submarine.FindClosest(GameMain.GameScreen.Cam.Position);
            }
            else
            {
                closestSub = Character.Controlled.Submarine;
            }

            bool displace = moveAmount.LengthSquared() > 100.0f * 100.0f;
            foreach (Submarine sub in subsToMove)
            {
                sub.PhysicsBody.LinearVelocity = newVelocity;

                if (displace)
                {
                    sub.PhysicsBody.SetTransform(sub.PhysicsBody.SimPosition + ConvertUnits.ToSimUnits(moveAmount), 0.0f);
                    sub.SubBody.DisplaceCharacters(moveAmount);
                }
                else
                {
                    sub.PhysicsBody.SetTransformIgnoreContacts(sub.PhysicsBody.SimPosition + ConvertUnits.ToSimUnits(moveAmount), 0.0f);
                }
            }

            if (closestSub != null && subsToMove.Contains(closestSub))
            {
                GameMain.GameScreen.Cam.Position += moveAmount;
                if (GameMain.GameScreen.Cam.TargetPos != Vector2.Zero) GameMain.GameScreen.Cam.TargetPos += moveAmount;

                if (Character.Controlled != null) Character.Controlled.CursorPosition += moveAmount;
            }
        }
    }
}
