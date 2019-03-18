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

        enum FloodType
        {
            Minor,
            Major,
            HideFlooding
        }

        private float minSoundInterval = 10.0f, maxSoundInterval = 60.0f;
        private FloodType currentFloodType;
        private float soundTimer;

        private float minFloodInterval = 30.0f, maxFloodInterval = 180.0f;
        private float createFloodTimer;
        private float currentFloodState;
        private float currentFloodDuration;

        partial void UpdateProjSpecific(CharacterHealth characterHealth, Limb targetLimb, float deltaTime)
        {
            if (Character.Controlled != characterHealth.Character) return;
            UpdateFloods(deltaTime);
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

        private void UpdateFloods(float deltaTime)
        {
            if (currentFloodDuration > 0.0f)
            {
                currentFloodDuration -= deltaTime;
                switch (currentFloodType)
                {
                    case FloodType.Minor:
                        currentFloodState += deltaTime;
                        //lerp the water surface in all hulls 50 units above the floor within 10 seconds
                        foreach (Hull hull in Hull.hullList)
                        {
                            hull.DrawSurface = hull.Rect.Y - hull.Rect.Height + MathHelper.Lerp(0.0f, 50.0f, currentFloodState / 10.0f);
                        }
                        break;
                    case FloodType.Major:
                        currentFloodState += deltaTime;
                        //create a full flood in 10 seconds
                        foreach (Hull hull in Hull.hullList)
                        {
                            hull.DrawSurface = hull.Rect.Y - MathHelper.Lerp(hull.Rect.Height, 0.0f, currentFloodState / 10.0f);
                        }
                        break;
                    case FloodType.HideFlooding:
                        //hide water inside hulls (the player can't see which hulls are flooded)
                        foreach (Hull hull in Hull.hullList)
                        {
                            hull.DrawSurface = hull.Rect.Y - hull.Rect.Height;
                        }
                        break;
                }
                return;
            }

            if (createFloodTimer < MathHelper.Lerp(maxFloodInterval, minFloodInterval, Strength / 100.0f))
            {
                createFloodTimer += deltaTime;
                return;
            }

            //probability of a fake flood goes from 0%-100%
            if (Rand.Range(0.0f, 100.0f) < Strength)
            {
                if (Rand.Range(0.0f, 1.0f) < 0.5f)
                {
                    currentFloodType = FloodType.HideFlooding;
                }
                else
                {
                    currentFloodType = Strength < 50.0f ? FloodType.Minor : FloodType.Major;
                }
                currentFloodDuration = Rand.Range(20.0f, 100.0f);
            }
            createFloodTimer = 0.0f;
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
                    Position = new Vector2(Rand.Range(0.0f, fireHull.Rect.Width), 30.0f),
                    LifeTime = MathHelper.Lerp(10.0f, 100.0f, Strength / 100.0f)
                });
                createFireSourceTimer = 0.0f;
            }

            foreach (FakeFireSource fakeFireSource in fakeFireSources)
            {
                if (fakeFireSource.Hull.Surface > fakeFireSource.Hull.Rect.Y - fakeFireSource.Hull.Rect.Height + fakeFireSource.Position.Y)
                {
                    fakeFireSource.LifeTime -= deltaTime * 10.0f;
                }

                fakeFireSource.LifeTime -= deltaTime;
                float growAmount = deltaTime * 5.0f;
                fakeFireSource.Size.X += growAmount;
                fakeFireSource.Position.X = MathHelper.Clamp(fakeFireSource.Position.X - growAmount / 2.0f, 0.0f, fakeFireSource.Hull.Rect.Width);
                fakeFireSource.Position.Y = MathHelper.Clamp(fakeFireSource.Position.Y, 0.0f, fakeFireSource.Hull.Rect.Height);
                fakeFireSource.Size.X = Math.Min(fakeFireSource.Hull.Rect.Width - fakeFireSource.Position.X, fakeFireSource.Size.X);
                fakeFireSource.Size.Y = Math.Min(fakeFireSource.Hull.Rect.Height - fakeFireSource.Position.Y, fakeFireSource.Size.Y);

                FireSource.EmitParticles(
                    fakeFireSource.Size,
                    fakeFireSource.Hull.WorldRect.Location.ToVector2() + fakeFireSource.Position - Vector2.UnitY * fakeFireSource.Hull.Rect.Height,
                    fakeFireSource.Hull,
                    0.5f);
            }

            fakeFireSources.RemoveAll(fs => fs.LifeTime <= 0.0f);
        }
    }
}
