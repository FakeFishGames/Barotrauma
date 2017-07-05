using Barotrauma.Lights;
using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace Barotrauma
{
    partial class FireSource
    {
        static Sound fireSoundBasic, fireSoundLarge;

        private LightSource lightSource;

        partial void UpdateProjSpecific(float growModifier)
        {
            if (hull.FireSources.Any(fs => fs != this && fs.size.X > size.X))
            {
                if (basicSoundIndex > 0)
                {
                    Sounds.SoundManager.Stop(basicSoundIndex);
                    basicSoundIndex = -1;
                }
                if (largeSoundIndex > 0)
                {
                    Sounds.SoundManager.Stop(largeSoundIndex);
                    largeSoundIndex = -1;
                }
            }
            else
            {
                if (fireSoundBasic != null)
                {
                    basicSoundIndex = fireSoundBasic.Loop(basicSoundIndex,
                        Math.Min(size.X / 100.0f, 1.0f), WorldPosition + size / 2.0f, 1000.0f);

                }
                if (fireSoundLarge != null)
                {
                    largeSoundIndex = fireSoundLarge.Loop(largeSoundIndex,
                        MathHelper.Clamp((size.X - 200.0f) / 100.0f, 0.0f, 1.0f), WorldPosition + size / 2.0f, 1000.0f);
                }
            }

            float count = Rand.Range(0.0f, size.X / 50.0f);

            for (int i = 0; i < count; i++)
            {
                Vector2 particlePos = new Vector2(
                    WorldPosition.X + Rand.Range(0.0f, size.X),
                    Rand.Range(WorldPosition.Y - size.Y, WorldPosition.Y + 20.0f));

                Vector2 particleVel = new Vector2(
                    (particlePos.X - (WorldPosition.X + size.X / 2.0f)),
                    (float)Math.Sqrt(size.X) * Rand.Range(0.0f, 15.0f) * growModifier);

                var particle = GameMain.ParticleManager.CreateParticle("flame",
                    particlePos, particleVel, 0.0f, hull);

                if (particle == null) continue;

                //make some of the particles create another firesource when they enter another hull
                if (Rand.Int(20) == 1) particle.OnChangeHull = OnChangeHull;

                particle.Size *= MathHelper.Clamp(size.X / 60.0f * Math.Max(hull.Oxygen / hull.FullVolume, 0.4f), 0.5f, 1.0f);

                if (Rand.Int(5) == 1)
                {
                    var smokeParticle = GameMain.ParticleManager.CreateParticle("smoke",
                    particlePos, new Vector2(particleVel.X, particleVel.Y * 0.1f), 0.0f, hull);

                    if (smokeParticle != null)
                    {
                        smokeParticle.Size *= MathHelper.Clamp(size.X / 100.0f * Math.Max(hull.Oxygen / hull.FullVolume, 0.4f), 0.5f, 1.0f);
                    }
                }
            }

            lightSource.Range = Math.Max(size.X, size.Y) * 10.0f / 2.0f;
            lightSource.Color = new Color(1.0f, 0.45f, 0.3f) * Rand.Range(0.8f, 1.0f);
            lightSource.Position = position + Vector2.UnitY * 30.0f;
        }
    }
}
