using Barotrauma.Items.Components;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Barotrauma
{
    partial class Ragdoll
    {
        partial void ImpactProjSpecific(float impact, Body body)
        {
            float volume = Math.Min(impact - 3.0f, 1.0f);

            if (body.UserData is Limb && character.Stun <= 0f)
            {
                Limb limb = (Limb)body.UserData;

                if (impact > 3.0f && limb.SoundTimer <= 0.0f)
                {
                    limb.SoundTimer = Limb.SoundInterval;
                    if (!string.IsNullOrWhiteSpace(limb.HitSound))
                    {
                        SoundPlayer.PlaySound(limb.HitSound, volume, impact * 100.0f, limb.WorldPosition);
                    }
                    foreach (WearableSprite wearable in limb.WearingItems)
                    {
                        if (limb.type == wearable.Limb && !string.IsNullOrWhiteSpace(wearable.Sound))
                        {
                            SoundPlayer.PlaySound(wearable.Sound, volume, impact * 100.0f, limb.WorldPosition);
                        }
                    }
                }
            }
            else if (body.UserData is Limb || body == Collider.FarseerBody)
            {
                if (!character.IsRemotePlayer || GameMain.Server != null)
                {
                    if (impact > ImpactTolerance)
                    {
                        SoundPlayer.PlayDamageSound("LimbBlunt", strongestImpact, Collider);
                    }
                }

                if (Character.Controlled == character) GameMain.GameScreen.Cam.Shake = Math.Min(strongestImpact, 3.0f);
            }
        }

        partial void Splash(Limb limb, Hull limbHull)
        {
            //create a splash particle
            GameMain.ParticleManager.CreateParticle("watersplash",
                new Vector2(limb.Position.X, limbHull.Surface) + limbHull.Submarine.Position,
                new Vector2(0.0f, Math.Abs(-limb.LinearVelocity.Y * 20.0f)),
                0.0f, limbHull);

            GameMain.ParticleManager.CreateParticle("bubbles",
                new Vector2(limb.Position.X, limbHull.Surface) + limbHull.Submarine.Position,
                limb.LinearVelocity * 0.001f,
                0.0f, limbHull);

            //if the Character dropped into water, create a wave
            if (limb.LinearVelocity.Y < 0.0f)
            {
                if (splashSoundTimer <= 0.0f)
                {
                    SoundPlayer.PlaySplashSound(limb.WorldPosition, Math.Abs(limb.LinearVelocity.Y) + Rand.Range(-5.0f, 0.0f));
                    splashSoundTimer = 0.5f;
                }
            }
        }

        public virtual void Draw(SpriteBatch spriteBatch)
        {
            if (simplePhysicsEnabled) return;

            Collider.UpdateDrawPosition();

            if (Limbs == null)
            {
                DebugConsole.ThrowError("Failed to draw a ragdoll, limbs have been removed. Character: \"" + character.Name + "\", removed: " + character.Removed + "\n" + Environment.StackTrace);
                return;
            }

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

            foreach (RevoluteJoint joint in LimbJoints)
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
