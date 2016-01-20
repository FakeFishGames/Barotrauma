using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class TransitionCinematic
    {
        public bool Running
        {
            get;
            private set;
        }

        private float duration;
        
        public TransitionCinematic(Submarine submarine, Camera cam, float duration)
        {
            Vector2 targetPos = submarine.Position;

            if (submarine.AtEndPosition)
            {
                targetPos = Level.Loaded.EndPosition + Vector2.UnitY * 500.0f;
            }
            else if (submarine.AtStartPosition)
            {
                targetPos = Level.Loaded.StartPosition + Vector2.UnitY * 500.0f;
            }

            this.duration = duration;

            Running = true;
            CoroutineManager.StartCoroutine(UpdateTransitionCinematic(submarine, cam, targetPos));
        }

        private IEnumerable<object> UpdateTransitionCinematic(Submarine sub, Camera cam, Vector2 targetPos)
        {
            Character.Controlled = null;
            cam.TargetPos = Vector2.Zero;
            GameMain.LightManager.LosEnabled = false;

            Vector2 diff = targetPos - sub.Position;
            float targetSpeed = 10.0f;

            Level.Loaded.ShaftBodies[0].Enabled = false;
            Level.Loaded.ShaftBodies[1].Enabled = false;
            
            cam.TargetPos = Vector2.Zero;
            float timer = 0.0f;

            while (timer < duration)
            {
                if (Screen.Selected != GameMain.GameScreen)
                {
                    yield return new WaitForSeconds(0.1f);

                    GUI.ScreenOverlayColor = Color.TransparentBlack;
                    yield return CoroutineStatus.Success;
                }

                cam.Zoom = Math.Max(0.2f, cam.Zoom - CoroutineManager.DeltaTime * 0.1f);

                Vector2 cameraPos = sub.Position + Submarine.HiddenSubPosition;
                cameraPos.Y = Math.Min(cameraPos.Y, ConvertUnits.ToDisplayUnits(Level.Loaded.ShaftBodies[0].Position.Y) - cam.WorldView.Height/2.0f);

                GUI.ScreenOverlayColor = Color.Lerp(Color.TransparentBlack, Color.Black, timer/duration);

                cam.Translate((cameraPos - cam.Position) * CoroutineManager.DeltaTime*10.0f);

                if (diff != Vector2.Zero)
                {
                    sub.ApplyForce((Vector2.Normalize(diff) * targetSpeed - sub.Velocity) * 500.0f);
                }

                timer += CoroutineManager.DeltaTime;

                yield return CoroutineStatus.Running;
            }

            Running = false;

            yield return new WaitForSeconds(0.1f);

            GUI.ScreenOverlayColor = Color.TransparentBlack;

            yield return CoroutineStatus.Success;
        }
    }
}
