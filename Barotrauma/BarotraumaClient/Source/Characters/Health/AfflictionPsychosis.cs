using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Barotrauma
{
    partial class AfflictionPsychosis : Affliction
    {
        class FakeFireSource
        {
            public Vector2 Size;
            public Vector2 Position;
            public Hull Hull;

            public float LifeTime;
        }

        const int MaxFakeFireSources = 10;
        private float minFakeFireSourceInterval = 10.0f, maxFakeFireSourceInterval = 200.0f;
        private float createFireSourceTimer;
        private List<FakeFireSource> fakeFireSources = new List<FakeFireSource>();

        private float minSoundInterval = 10.0f, maxSoundInterval = 60.0f;
        private float soundTimer;

        partial void UpdateProjSpecific(CharacterHealth characterHealth, Limb targetLimb, float deltaTime)
        {
            if (Character.Controlled != characterHealth.Character) return;
            UpdateSounds(characterHealth.Character, deltaTime);
            UpdateFires(characterHealth.Character, deltaTime);
        }

        private void UpdateSounds(Character character, float deltaTime)
        {
            if (soundTimer < MathHelper.Lerp(maxSoundInterval, minSoundInterval, Strength / 100.0f))
            {
                soundTimer += deltaTime;
                return;
            }

            float impactStrength = MathHelper.Lerp(0.1f, 1.0f, Strength / 100.0f);
            SoundPlayer.PlayDamageSound("StructureBlunt", Rand.Range(10.0f, 1000.0f), character.WorldPosition + Rand.Vector(500.0f));
            GameMain.GameScreen.Cam.Shake = impactStrength * 10.0f;
            GameMain.GameScreen.Cam.AngularVelocity = Rand.Range(-impactStrength, impactStrength);
            soundTimer = 0.0f;
        }

        private void UpdateFires(Character character, float deltaTime)
        {
            createFireSourceTimer += deltaTime;
            if (fakeFireSources.Count < MaxFakeFireSources &&
                character.Submarine != null &&
                createFireSourceTimer > MathHelper.Lerp(maxFakeFireSourceInterval, minFakeFireSourceInterval, Strength / 100.0f))
            {
                Hull fireHull = Hull.hullList.GetRandom(h => h.Submarine == character.Submarine);

                fakeFireSources.Add(new FakeFireSource()
                {
                    Size = Vector2.One * 20.0f,
                    Hull = fireHull,
                    Position = new Vector2(Rand.Range(0.0f, fireHull.Rect.Width), fireHull.Rect.Height + 30.0f),
                    LifeTime = MathHelper.Lerp(10.0f, 100.0f, Strength / 100.0f)
                });
                createFireSourceTimer = 0.0f;
            }

            foreach (FakeFireSource fakeFireSource in fakeFireSources)
            {
                fakeFireSource.LifeTime -= deltaTime;
                float growAmount = deltaTime * 5.0f;
                fakeFireSource.Size.X += growAmount;
                fakeFireSource.Position.X = MathHelper.Clamp(fakeFireSource.Position.X - growAmount / 2.0f, 0.0f, fakeFireSource.Hull.Rect.Width);
                fakeFireSource.Position.Y = MathHelper.Clamp(fakeFireSource.Position.Y, 0.0f, fakeFireSource.Hull.Rect.Height);
                fakeFireSource.Size.X = Math.Min(fakeFireSource.Hull.Rect.Width - fakeFireSource.Position.X, fakeFireSource.Size.X);
                fakeFireSource.Size.Y = Math.Min(fakeFireSource.Hull.Rect.Height - fakeFireSource.Position.Y, fakeFireSource.Size.Y);

                FireSource.EmitParticles(
                    fakeFireSource.Size,
                    fakeFireSource.Hull.WorldRect.Location.ToVector2() + fakeFireSource.Position,
                    fakeFireSource.Hull,
                    0.5f);
            }

            fakeFireSources.RemoveAll(fs => fs.LifeTime <= 0.0f);
        }
    }
}
