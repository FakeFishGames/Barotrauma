using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using Barotrauma.Networking;

namespace Barotrauma
{
    partial class WreckAI : IServerSerializable
    {
        private CoroutineHandle fadeOutRoutine;
        partial void FadeOutColors()
        {
            if (fadeOutRoutine != null)
            {
                CoroutineManager.StopCoroutines(fadeOutRoutine);
            }
            fadeOutRoutine = CoroutineManager.StartCoroutine(FadeOutColors(Config.DeadEntityColorFadeOutTime));
        }

        private IEnumerable<CoroutineStatus> FadeOutColors(float time)
        {
            float timer = 0;
            while (timer < time)
            {
                timer += CoroutineManager.DeltaTime;
                float m = MathHelper.Lerp(1, Config.DeadEntityColorMultiplier, MathUtils.InverseLerp(0, time, timer));
                foreach (var item in thalamusItems)
                {
                    if (item.Prefab.BrokenSprites.None())
                    {
                        Color c = item.Prefab.SpriteColor;
                        item.SpriteColor = new Color(c.R / 255f * m, c.G / 255f * m, c.B / 255f * m, c.A / 255f);
                    }
                }
                foreach (var structure in thalamusStructures)
                {
                    Color c = structure.Prefab.SpriteColor;
                    structure.SpriteColor = new Color(c.R / 255f * m, c.G / 255f * m, c.B / 255f * m, c.A / 255f);
                }
                yield return CoroutineStatus.Running;
            }
            yield return CoroutineStatus.Success;
        }

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            IsAlive = msg.ReadBoolean();
        }
    }
}