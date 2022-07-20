using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class AfflictionPsychosis : Affliction
    {
        const int MaxFakeFireSources = 10;
        const float MinFakeFireSourceInterval = 30.0f, MaxFakeFireSourceInterval = 240.0f;
        private float createFireSourceTimer;
        private readonly List<DummyFireSource> fakeFireSources = new List<DummyFireSource>();

        public enum FloodType
        {
            None,
            Minor,
            Major,
            HideFlooding
        }

        const float MinSoundInterval = 60.0f, MaxSoundInterval = 240.0f;
        private FloodType currentFloodType;
        private float soundTimer;

        const float MinFloodInterval = 60.0f, MaxFloodInterval = 240.0f;
        private float createFloodTimer;
        private float currentFloodState;
        private float currentFloodDuration;

        private float fakeBrokenInterval = 30.0f;
        private float fakeBrokenTimer = 0.0f;

        private float invisibleCharacterInterval = 30.0f;
        private float invisibleCharacterTimer = 0.0f;

        public FloodType CurrentFloodType
        {
            get { return currentFloodType; }
        }

        partial void UpdateProjSpecific(CharacterHealth characterHealth, Limb targetLimb, float deltaTime)
        {
            if (Character.Controlled != characterHealth.Character) return;
            UpdateFloods(deltaTime);
            UpdateSounds(characterHealth.Character, deltaTime);
            UpdateFires(characterHealth.Character, deltaTime);
            UpdateInvisibleCharacters(deltaTime);
            UpdateFakeBroken(deltaTime);
        }

        private void UpdateSounds(Character character, float deltaTime)
        {
            if (soundTimer < MathHelper.Lerp(MaxSoundInterval, MinSoundInterval, Strength / 100.0f))
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
                        //lerp the water surface in all hulls 15 units above the floor within 10 seconds
                        foreach (Hull hull in Hull.HullList)
                        {
                            for (int i = hull.FakeFireSources.Count - 1; i >= 0; i--)
                            {
                                hull.FakeFireSources[i].Extinguish(deltaTime, 50.0f);
                            }
                            hull.DrawSurface = hull.Rect.Y - hull.Rect.Height + MathHelper.Lerp(0.0f, 15.0f, currentFloodState / 10.0f);
                        }
                        break;
                    case FloodType.Major:
                        currentFloodState += deltaTime;
                        //create a full flood in 10 seconds
                        foreach (Hull hull in Hull.HullList)
                        {
                            for (int i = hull.FakeFireSources.Count - 1; i >= 0; i--)
                            {
                                hull.FakeFireSources[i].Extinguish(deltaTime, 200.0f);
                            }
                            hull.DrawSurface = hull.Rect.Y - MathHelper.Lerp(hull.Rect.Height, 0.0f, currentFloodState / 10.0f);
                        }
                        break;
                    case FloodType.HideFlooding:
                        //hide water inside hulls (the player can't see which hulls are flooded)
                        foreach (Hull hull in Hull.HullList)
                        {
                            hull.DrawSurface = hull.Rect.Y - hull.Rect.Height;
                        }
                        break;
                }
                return;
            }

            if (createFloodTimer < MathHelper.Lerp(MaxFloodInterval, MinFloodInterval, Strength / 100.0f))
            {
                currentFloodType = FloodType.None;
                createFloodTimer += deltaTime;
                return;
            }

            //probability of a fake flood goes from 0%-100%
            if (Rand.Range(0.0f, 100.0f) < Strength)
            {
                if (Rand.Range(0.0f, 1.0f) < 0.5f)
                {
                    currentFloodType = FloodType.HideFlooding;
                    currentFloodType = FloodType.Minor;
                }
                else
                {
                    //disabled Major flooding because it's too easy to tell it's fake
                    currentFloodType = FloodType.Minor;// Strength < 50.0f ? FloodType.Minor : FloodType.Major;
                }
                currentFloodDuration = Rand.Range(20.0f, 100.0f);
            }
            createFloodTimer = 0.0f;
        }

        private void UpdateFires(Character character, float deltaTime)
        {
            createFireSourceTimer += deltaTime;
            fakeFireSources.RemoveAll(fs => fs.Removed);
            if (fakeFireSources.Count < MaxFakeFireSources &&
                character.Submarine != null &&
                createFireSourceTimer > MathHelper.Lerp(MaxFakeFireSourceInterval, MinFakeFireSourceInterval, Strength / 100.0f))
            {
                Hull fireHull = Hull.HullList.GetRandomUnsynced(h => h.Submarine == character.Submarine);
                if (fireHull != null)
                {
                    var fakeFire = new DummyFireSource(Vector2.One * 500.0f, new Vector2(Rand.Range(fireHull.WorldRect.X, fireHull.WorldRect.Right), fireHull.WorldPosition.Y + 1), fireHull, isNetworkMessage: true)
                    {
                        CausedByPsychosis = true,
                        DamagesItems = false,
                        DamagesCharacters = false
                    };
                    fakeFireSources.Add(fakeFire);
                    createFireSourceTimer = 0.0f;
                }
            }
        }

        private void UpdateInvisibleCharacters(float deltaTime)
        {
            invisibleCharacterTimer -= deltaTime;
            if (invisibleCharacterTimer > 0.0f) { return; }

            foreach (Character c in Character.CharacterList)
            {
                if (c.IsDead || c == Character.Controlled) { continue; }
                if (c.WorldPosition.X < GameMain.GameScreen.Cam.WorldView.X || c.WorldPosition.X > GameMain.GameScreen.Cam.WorldView.Right) { continue; }
                if (c.WorldPosition.Y < GameMain.GameScreen.Cam.WorldView.Y - GameMain.GameScreen.Cam.WorldView.Height || c.WorldPosition.Y > GameMain.GameScreen.Cam.WorldView.Y) { continue; }
                if (Rand.Range(0.0f, 500.0f) < Strength)
                {
                    c.InvisibleTimer = 60.0f;
                }
            }

            invisibleCharacterTimer = invisibleCharacterInterval;
        }


        private void UpdateFakeBroken(float deltaTime)
        {
            fakeBrokenTimer -= deltaTime;
            if (fakeBrokenTimer > 0.0f) { return; }

            foreach (Item item in Item.ItemList)
            {
                var repairable = item.GetComponent<Repairable>();
                if (repairable == null) { continue; }
                if (ShouldFakeBrokenItem(item))
                {
                    repairable.FakeBrokenTimer = 60.0f;
                }
            }

            fakeBrokenTimer = fakeBrokenInterval;
        }

        private bool ShouldFakeBrokenItem(Item item)
        {
            return Rand.Range(0.0f, 1000.0f) < Strength;
        }
    }
}
