using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

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
            : this(new List<Submarine>() { submarine }, cam, duration)
        {

        }

        public TransitionCinematic(List<Submarine> submarines, Camera cam, float duration)
        {
            if (!submarines.Any(s => s != null)) return;

            Vector2 targetPos = new Vector2(
                submarines.Sum(s => s.Position.X),
                submarines.Sum(s => s.Position.Y)) / submarines.Count;

            if (submarines.First().AtEndPosition)
            {
                targetPos = Level.Loaded.EndPosition + Vector2.UnitY * 500.0f;
            }
            else if (submarines.First().AtStartPosition)
            {
                targetPos = Level.Loaded.StartPosition + Vector2.UnitY * 500.0f;
            }

            this.duration = duration;

            Running = true;
            CoroutineManager.StartCoroutine(UpdateTransitionCinematic(submarines, cam, targetPos));
        }

        private IEnumerable<object> UpdateTransitionCinematic(List<Submarine> subs, Camera cam, Vector2 targetPos)
        {
            if (!subs.Any()) yield return CoroutineStatus.Success;

            Character.Controlled = null;
            cam.TargetPos = Vector2.Zero;
#if CLIENT
            GameMain.LightManager.LosEnabled = false;
#endif

            //Vector2 diff = targetPos - sub.Position;
            float targetSpeed = 10.0f;

            Level.Loaded.TopBarrier.Enabled = false;
            
            cam.TargetPos = Vector2.Zero;
            float timer = 0.0f;

            while (timer < duration)
            {
                if (Screen.Selected != GameMain.GameScreen)
                {
                    yield return new WaitForSeconds(0.1f);

#if CLIENT
                    GUI.ScreenOverlayColor = Color.TransparentBlack;
#endif

                    Running = false;
                    yield return CoroutineStatus.Success;
                }

                cam.Zoom = Math.Max(0.2f, cam.Zoom - CoroutineManager.UnscaledDeltaTime * 0.1f);
                
                Vector2 cameraPos = subs.First().Position + Submarine.MainSub.HiddenSubPosition;
                cameraPos.Y = Math.Min(cameraPos.Y, ConvertUnits.ToDisplayUnits(Level.Loaded.TopBarrier.Position.Y) - cam.WorldView.Height / 2.0f);
                cam.Translate((cameraPos - cam.Position) * CoroutineManager.UnscaledDeltaTime * 10.0f);
#if CLIENT
                GUI.ScreenOverlayColor = Color.Lerp(Color.TransparentBlack, Color.Black, timer/duration);
#endif
                
                foreach (Submarine sub in subs)
                {
                    if (sub.Position == targetPos) continue;
                    Vector2 dir = Vector2.Normalize(targetPos - sub.Position);
                    if (!MathUtils.IsValid(dir)) continue;
                    sub.ApplyForce((dir * targetSpeed - sub.Velocity) * 500.0f);
                }
                
                timer += CoroutineManager.UnscaledDeltaTime;

                yield return CoroutineStatus.Running;
            }

            Running = false;

            yield return new WaitForSeconds(0.1f);

#if CLIENT
            GUI.ScreenOverlayColor = Color.TransparentBlack;
#endif

            yield return CoroutineStatus.Success;
        }
    }
}
