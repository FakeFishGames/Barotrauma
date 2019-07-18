using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class RoundEndCinematic
    {
        public bool Running
        {
            get;
            private set;
        }

        public Camera AssignedCamera;

        private float duration;

        private CoroutineHandle updateCoroutine;
        
        public RoundEndCinematic(Submarine submarine, Camera cam, float duration = 10.0f)
            : this(new List<Submarine>() { submarine }, cam, duration)
        {

        }

        public RoundEndCinematic(List<Submarine> submarines, Camera cam, float duration)
        {
            if (!submarines.Any(s => s != null)) return;

            this.duration = duration;
            AssignedCamera = cam;

            Running = true;
            updateCoroutine = CoroutineManager.StartCoroutine(Update(submarines, cam));
        }

        public void Stop()
        {
            CoroutineManager.StopCoroutines(updateCoroutine);
            Running = false;
#if CLIENT
            GUI.ScreenOverlayColor = Color.TransparentBlack;
#endif
        }

        private IEnumerable<object> Update(List<Submarine> subs, Camera cam)
        {
            if (!subs.Any()) yield return CoroutineStatus.Success;

            Character.Controlled = null;
            cam.TargetPos = Vector2.Zero;
#if CLIENT
            GameMain.LightManager.LosEnabled = false;
#endif

            Level.Loaded.TopBarrier.Enabled = false;

            cam.TargetPos = Vector2.Zero;
            float timer = 0.0f;
            float initialZoom = cam.Zoom;
            Vector2 initialCameraPos = cam.Position;

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

                Vector2 minPos = new Vector2(
                    subs.Min(s => s.WorldPosition.X - s.Borders.Width / 2),
                    subs.Min(s => s.WorldPosition.Y - s.Borders.Height / 2));
                Vector2 maxPos = new Vector2(
                    subs.Min(s => s.WorldPosition.X + s.Borders.Width / 2),
                    subs.Min(s => s.WorldPosition.Y + s.Borders.Height / 2));
                Vector2 cameraPos = new Vector2(
                    MathHelper.SmoothStep(minPos.X, maxPos.X, timer / duration),
                    (minPos.Y + maxPos.Y) / 2.0f);
                cam.Translate(cameraPos - cam.Position);

#if CLIENT
                cam.Zoom = MathHelper.SmoothStep(initialZoom, 0.5f, timer / duration);
                if (timer / duration > 0.9f)
                {
                    GUI.ScreenOverlayColor = Color.Lerp(Color.TransparentBlack, Color.Black, ((timer / duration) - 0.9f) * 10.0f);
                }
#endif
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
