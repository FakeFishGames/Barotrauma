using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    partial class Ragdoll
    {
        public virtual void Draw(SpriteBatch spriteBatch)
        {
            if (simplePhysicsEnabled) return;

            Collider.UpdateDrawPosition();

            foreach (Limb limb in Limbs)
            {
                limb.Draw(spriteBatch);
            }
        }

        public void DebugDraw(SpriteBatch spriteBatch)
        {
            if (!GameMain.DebugDraw || !character.Enabled) return;
            if (simplePhysicsEnabled) return;

            foreach (Limb limb in Limbs)
            {

                if (limb.pullJoint != null)
                {
                    Vector2 pos = ConvertUnits.ToDisplayUnits(limb.pullJoint.WorldAnchorA);
                    if (currentHull != null) pos += currentHull.Submarine.DrawPosition;
                    pos.Y = -pos.Y;
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X, (int)pos.Y, 5, 5), Color.Red, true, 0.01f);
                }

                limb.body.DebugDraw(spriteBatch, inWater ? Color.Cyan : Color.White);
            }

            Collider.DebugDraw(spriteBatch, frozen ? Color.Red : (inWater ? Color.SkyBlue : Color.Gray));
            GUI.Font.DrawString(spriteBatch, Collider.LinearVelocity.X.ToString(), new Vector2(Collider.DrawPosition.X, -Collider.DrawPosition.Y), Color.Orange);

            foreach (RevoluteJoint joint in limbJoints)
            {
                Vector2 pos = ConvertUnits.ToDisplayUnits(joint.WorldAnchorA);
                GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X, (int)-pos.Y, 5, 5), Color.White, true);

                pos = ConvertUnits.ToDisplayUnits(joint.WorldAnchorB);
                GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X, (int)-pos.Y, 5, 5), Color.White, true);
            }

            foreach (Limb limb in Limbs)
            {
                if (limb.body.TargetPosition != null)
                {
                    Vector2 pos = ConvertUnits.ToDisplayUnits((Vector2)limb.body.TargetPosition);
                    if (currentHull != null) pos += currentHull.Submarine.DrawPosition;
                    pos.Y = -pos.Y;

                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X - 10, (int)pos.Y - 10, 20, 20), Color.Cyan, false, 0.01f);
                    GUI.DrawLine(spriteBatch, pos, new Vector2(limb.WorldPosition.X, -limb.WorldPosition.Y), Color.Cyan);
                }
            }

            if (character.MemState.Count > 1)
            {
                Vector2 prevPos = ConvertUnits.ToDisplayUnits(character.MemState[0].Position);
                if (currentHull != null) prevPos += currentHull.Submarine.DrawPosition;
                prevPos.Y = -prevPos.Y;

                for (int i = 1; i < character.MemState.Count; i++)
                {
                    Vector2 currPos = ConvertUnits.ToDisplayUnits(character.MemState[i].Position);
                    if (currentHull != null) currPos += currentHull.Submarine.DrawPosition;
                    currPos.Y = -currPos.Y;

                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)currPos.X - 3, (int)currPos.Y - 3, 6, 6), Color.Cyan * 0.6f, true, 0.01f);
                    GUI.DrawLine(spriteBatch, prevPos, currPos, Color.Cyan * 0.6f, 0, 3);

                    prevPos = currPos;
                }
            }

            if (ignorePlatforms)
            {
                GUI.DrawLine(spriteBatch,
                    new Vector2(Collider.DrawPosition.X, -Collider.DrawPosition.Y),
                    new Vector2(Collider.DrawPosition.X, -Collider.DrawPosition.Y + 50),
                    Color.Orange, 0, 5);
            }
        }
    }
}
