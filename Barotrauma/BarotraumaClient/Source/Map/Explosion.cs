using Barotrauma.Lights;
using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class Explosion
    {
        partial void ExplodeProjSpecific(Vector2 worldPosition, Hull hull)
        {
            if (shockwave)
            {
                GameMain.ParticleManager.CreateParticle("shockwave", worldPosition,
                    Vector2.Zero, 0.0f, hull);
            }

            for (int i = 0; i < attack.Range * 0.1f; i++)
            {
                Vector2 bubblePos = Rand.Vector(attack.Range * 0.5f);
                GameMain.ParticleManager.CreateParticle("bubbles", worldPosition + bubblePos,
                    bubblePos, 0.0f, hull);

                if (sparks)
                {
                    GameMain.ParticleManager.CreateParticle("spark", worldPosition,
                        Rand.Vector(Rand.Range(500.0f, 800.0f)), 0.0f, hull);
                }
                if (flames)
                {
                    GameMain.ParticleManager.CreateParticle("explosionfire", ClampParticlePos(worldPosition + Rand.Vector(50f), hull),
                        Rand.Vector(Rand.Range(50.0f, 100.0f)), 0.0f, hull);
                }
                if (smoke)
                {
                    GameMain.ParticleManager.CreateParticle("smoke", ClampParticlePos(worldPosition + Rand.Vector(50f), hull),
                        Rand.Vector(Rand.Range(1.0f, 10.0f)), 0.0f, hull);
                }
            }

            if (hull != null && !string.IsNullOrWhiteSpace(decal) && decalSize > 0.0f)
            {
                hull.AddDecal(decal, worldPosition, decalSize);
            }

            if (flash)
            {
                float displayRange = attack.Range;
                if (displayRange < 0.1f) return;

                var light = new LightSource(worldPosition, displayRange, Color.LightYellow, null);
                CoroutineManager.StartCoroutine(DimLight(light));
            }
        }

        private IEnumerable<object> DimLight(LightSource light)
        {
            float currBrightness = 1.0f;
            float startRange = light.Range;

            while (light.Color.A > 0.0f)
            {
                light.Color = new Color(light.Color.R, light.Color.G, light.Color.B, currBrightness);
                light.Range = startRange * currBrightness;

                currBrightness -= CoroutineManager.DeltaTime * 20.0f;

                yield return CoroutineStatus.Running;
            }

            light.Remove();

            yield return CoroutineStatus.Success;
        }
    }
}
