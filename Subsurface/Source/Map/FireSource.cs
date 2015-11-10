using Barotrauma.Lights;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class FireSource
    {
        const float OxygenConsumption = 10.0f;
        const float GrowSpeed = 5.0f;

        Hull hull;

        LightSource lightSource;

        Vector2 position;
        Vector2 size;

        public Vector2 Size
        {
            get { return size; }
        }

        public FireSource(Vector2 position)
        {
            hull = Hull.FindHull(position);
            if (hull == null) return;

            lightSource = new LightSource(position, 50.0f, new Color(1.0f, 0.9f, 0.6f));

            hull.AddFireSource(this);

            this.position = position - new Vector2(-5.0f, 5.0f);

            this.position.Y = hull.Rect.Y - hull.Rect.Height;

            size = new Vector2(10.0f, 10.0f);
        }

        private void LimitSize()
        {
            position.X = Math.Max(hull.Rect.X, position.X);
            position.Y = Math.Min(hull.Rect.Y, position.Y);

            size.X = Math.Min(hull.Rect.Width - (position.X - hull.Rect.X), size.X);
            size.Y = Math.Min(hull.Rect.Height - (hull.Rect.Y - position.Y), size.Y);
        }

        public static void UpdateAll(List<FireSource> fireSources, float deltaTime)
        {
            for (int i = fireSources.Count - 1; i >= 0; i--)
            {
                fireSources[i].Update(deltaTime);
            }
            
            //combine overlapping fires
            for (int i = fireSources.Count - 1; i >= 0; i--)
            {
                for (int j = i-1; j>=0 ; j--)
                {
                    if (!fireSources[i].CheckOverLap(fireSources[j])) continue;

                    fireSources[j].position.X = Math.Min(fireSources[i].position.X, fireSources[j].position.X);

                    fireSources[j].size.X = 
                        Math.Max(fireSources[i].position.X + fireSources[i].size.X, fireSources[j].position.X + fireSources[j].size.X) 
                        - fireSources[j].position.X;

                    fireSources[i].Remove();
                }
            }
        }

        private bool CheckOverLap(FireSource fireSource)
        {
            return !(position.X > fireSource.position.X + fireSource.size.X &&
                position.X + size.X < fireSource.position.X);


        }

        public void Update(float deltaTime)
        {
            float count = Rand.Range(0.0f, (float)Math.Sqrt(size.X)/2.0f);
            
            for (int i = 0; i < count; i++ )
            {
                float normalizedPos = 0.5f-(i / count);

                Vector2 spawnPos = new Vector2(position.X + Rand.Range(0.0f, size.X), Rand.Range(position.Y - size.Y, position.Y)+10.0f);

                Vector2 speed = new Vector2((spawnPos.X - (position.X + size.X/2.0f)), (float)Math.Sqrt(size.X)*Rand.Range(10.0f,15.0f));
                
                var particle = GameMain.ParticleManager.CreateParticle("flame",
                    spawnPos, speed, 0.0f, hull);

                if (particle == null) continue;

                if (Rand.Int(20) == 1) particle.OnChangeHull = OnChangeHull;


                particle.Size *= MathHelper.Clamp(size.X/100.0f * (hull.Oxygen/hull.FullVolume), 0.5f, 4.0f);
            }

            DamageCharacters(deltaTime);

            if (hull.Volume > 0.0f) Extinquish(deltaTime);

            lightSource.Range = Math.Max(size.X, size.Y)*Rand.Range(8.0f, 10.0f)/2.0f;
            lightSource.Color = new Color(1.0f, 0.9f, 0.6f) * Rand.Range(0.8f, 1.0f); 

            hull.Oxygen -= size.X*deltaTime*OxygenConsumption;

            float growModifier = hull.OxygenPercentage < 20.0f ? hull.OxygenPercentage/20.0f : 1.0f;

            position.X -= GrowSpeed * growModifier * 0.5f * deltaTime;
            //position.Y += GrowSpeed*0.5f * deltaTime;

            size.X += GrowSpeed * growModifier * deltaTime;
            //size.Y += GrowSpeed * deltaTime;
            
            LimitSize();
        }

        private void OnChangeHull(Vector2 pos, Hull particleHull)
        {
            if (particleHull == hull || particleHull==null) return;

            if (particleHull.FireSources.Find(fs => pos.X > fs.position.X && pos.X < fs.position.X+fs.size.X)!=null) return;

            new FireSource(new Vector2(pos.X, particleHull.Rect.Y-particleHull.Rect.Height + 5.0f));
        }

        private void DamageCharacters(float deltaTime)
        {
            foreach (Character c in Character.CharacterList)
            {
                if (c.AnimController.CurrentHull == null) continue;

                float range = (float)Math.Sqrt(size.X) * 10.0f;
                if (c.Position.X < position.X - range || c.Position.X > position.X + size.X + range) continue;
                if (c.Position.Y < position.Y - size.Y || c.Position.Y > hull.Rect.Y) continue;

                c.Health -= (float)Math.Sqrt(size.X) * deltaTime;
            }
        }

        private void Extinquish(float deltaTime)
        {
            float extinquishAmount = Math.Min(hull.Volume / 100.0f, size.X);

            float steamCount = Rand.Range(-5.0f, (float)Math.Sqrt(extinquishAmount));

            for (int i = 0; i < steamCount; i++)
            {
                Vector2 spawnPos = new Vector2(position.X + size.X * (i / steamCount) + Rand.Range(-5.0f, 5.0f), Rand.Range(position.Y - size.Y, position.Y) + 10.0f);

                Vector2 speed = new Vector2((spawnPos.X - (position.X + size.X / 2.0f)), (float)Math.Sqrt(size.X) * Rand.Range(20.0f, 25.0f));

                var particle = GameMain.ParticleManager.CreateParticle("steam",
                    spawnPos, speed, 0.0f, hull);

                if (particle == null) continue;

                particle.Size *= MathHelper.Clamp(size.X / 10.0f, 0.5f, 3.0f);
            }

            position.X += extinquishAmount * 0.1f / 2.0f;
            size.X -= extinquishAmount * 0.1f;

            hull.Volume -= extinquishAmount;

            if (size.X < 1.0f) Remove();
        }

        public void Remove()
        {
            lightSource.Remove();

            hull.RemoveFire(this);
        }
    }
}
