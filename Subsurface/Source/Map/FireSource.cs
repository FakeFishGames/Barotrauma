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
        static Sound fireSoundBasic, fireSoundLarge;

        const float OxygenConsumption = 50.0f;
        const float GrowSpeed = 5.0f;

        private int basicSoundIndex, largeSoundIndex;

        private Hull hull;

        private LightSource lightSource;

        private Vector2 position;
        private Vector2 size;

        private Entity Submarine;

        public Vector2 Position
        {
            get { return position; }
            set
            {
                if (!MathUtils.IsValid(value)) return;

                position = value;
            }
        }

        public Vector2 WorldPosition
        {
            get { return Submarine.Position + position; }
        }

        public Vector2 Size
        {
            get { return size; }
            set
            {
                if (value == size) return;

                Vector2 sizeChange = value - size;

                size = value;
                position.X -= sizeChange.X * 0.5f;
                LimitSize();
            }
        }

        public Hull Hull
        {
            get { return hull; }
        }

        public FireSource(Vector2 worldPosition, Hull spawningHull = null, bool isNetworkMessage = false)
        {
            hull = Hull.FindHull(worldPosition, spawningHull);
            if (hull == null) return;

            if (!isNetworkMessage && GameMain.Client != null) return;

            if (fireSoundBasic==null)
            {
                fireSoundBasic = Sound.Load("Content/Sounds/fire.ogg");
                fireSoundLarge = Sound.Load("Content/Sounds/firelarge.ogg");
            }

            hull.AddFireSource(this);

            Submarine = hull.Submarine;

            this.position = worldPosition - new Vector2(-5.0f, 5.0f) - Submarine.Position;


            lightSource = new LightSource(this.position, 50.0f, new Color(1.0f, 0.9f, 0.7f), hull == null ? null : hull.Submarine);



            //this.position.Y = hull.Rect.Y - hull.Rect.Height;

            size = new Vector2(10.0f, 10.0f);
        }

        private void LimitSize()
        {
            if (hull == null) return;

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
                    i = Math.Min(i, fireSources.Count - 1);
                    j = Math.Min(j, i - 1);

                    if (!fireSources[i].CheckOverLap(fireSources[j])) continue;

                    float leftEdge = Math.Min(fireSources[i].position.X, fireSources[j].position.X);

                    fireSources[j].size.X =
                        Math.Max(fireSources[i].position.X + fireSources[i].size.X, fireSources[j].position.X + fireSources[j].size.X)
                        - leftEdge;

                    fireSources[j].position.X = leftEdge;
                    
                    fireSources[i].Remove();
                }
            }
        }

        public bool Contains(Vector2 pos)
        {
            return pos.X > position.X &&  pos.X<position.X + size.X;
        }

        private bool CheckOverLap(FireSource fireSource)
        {
            return !(position.X > fireSource.position.X + fireSource.size.X ||
                position.X + size.X < fireSource.position.X);
        }

        public void Update(float deltaTime)
        {
            float count = Rand.Range(0.0f, size.X/50.0f);
            
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

            //the firesource will start to shrink if oxygen percentage is below 10
            float growModifier = Math.Min((hull.OxygenPercentage / 10.0f) - 1.0f, 1.0f);

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
                    particlePos, particleVel * 0.1f, 0.0f, hull);

                    if (smokeParticle != null)
                    {
                        smokeParticle.Size *= MathHelper.Clamp(size.X / 100.0f * Math.Max(hull.Oxygen / hull.FullVolume, 0.4f), 0.5f, 1.0f);
                    }
                }
            }

            DamageCharacters(deltaTime);
            DamageItems(deltaTime);

            if (hull.Volume > 0.0f) HullWaterExtinquish(deltaTime);

            hull.Oxygen -= size.X * deltaTime * OxygenConsumption;

            position.X -= GrowSpeed * growModifier * 0.5f * deltaTime;

            size.X += GrowSpeed * growModifier * deltaTime;
            size.Y = MathHelper.Clamp(size.Y + GrowSpeed * growModifier * deltaTime, 10.0f, 50.0f);

            if (size.X > 50.0f)
            {
                this.position.Y = MathHelper.Lerp(this.position.Y, hull.Rect.Y - hull.Rect.Height + size.Y, deltaTime);
            }

            LimitSize();

            lightSource.Range = Math.Max(size.X, size.Y) * 10.0f / 2.0f;
            lightSource.Color = new Color(1.0f, 0.45f, 0.3f) * Rand.Range(0.8f, 1.0f);
            lightSource.Position = position + Vector2.UnitY * 30.0f;

            if (GameMain.Client != null) return;

            if (size.X < 1.0f) Remove();
        }

        private void OnChangeHull(Vector2 pos, Hull particleHull)
        {
            if (particleHull == hull || particleHull==null) return;

            if (particleHull.FireSources.Find(fs => pos.X > fs.position.X-100.0f && pos.X < fs.position.X+fs.size.X+100.0f)!=null) return;

            new FireSource(new Vector2(pos.X, particleHull.WorldRect.Y-particleHull.Rect.Height + 5.0f));
        }

        private void DamageCharacters(float deltaTime)
        {
            if (size.X <= 0.0f) return;

            foreach (Character c in Character.CharacterList)
            {
                if (c.AnimController.CurrentHull == null || c.IsDead) continue;

                float range = (float)Math.Sqrt(size.X) * 20.0f;
                if (c.Position.X < position.X - range || c.Position.X > position.X + size.X + range) continue;
                if (c.Position.Y < position.Y - size.Y || c.Position.Y > hull.Rect.Y) continue;
                
                float dmg = (float)Math.Sqrt(size.X) * deltaTime / c.AnimController.Limbs.Length;
                foreach (Limb limb in c.AnimController.Limbs)
                {
                    if (limb.WearingItems.Find(w => w!=null && w.WearableComponent.Item.FireProof)!=null) continue;
                    limb.Burnt += dmg * 10.0f;
                    c.AddDamage(limb.SimPosition, DamageType.None, dmg, 0,0,false);
                }
            }
        }

        private void DamageItems(float deltaTime)
        {
            if (size.X <= 0.0f) return;

            foreach (Item item in Item.ItemList)
            {
                if (item.CurrentHull != hull || item.FireProof || item.Condition <= 0.0f) continue;
                if (item.ParentInventory != null && item.ParentInventory.Owner is Character) return;

                float range = (float)Math.Sqrt(size.X) * 10.0f;
                if (item.Position.X < position.X - range || item.Position.X > position.X + size.X + range) continue;
                if (item.Position.Y < position.Y - size.Y || item.Position.Y > hull.Rect.Y) continue;

                //item.Condition -= (float)Math.Sqrt(size.X) * deltaTime;

                item.ApplyStatusEffects(ActionType.OnFire, deltaTime);
            }
        }

        private void HullWaterExtinquish(float deltaTime)
        {
            //the higher the surface of the water is relative to the firesource, the faster it puts out the fire 
            float extinquishAmount = (hull.Surface - (position.Y - size.Y)) * deltaTime;

            if (extinquishAmount < 0.0f) return;

            float steamCount = Rand.Range(-5.0f, Math.Min(extinquishAmount * 100.0f, 10));
            for (int i = 0; i < steamCount; i++)
            {
                Vector2 spawnPos = new Vector2(
                    WorldPosition.X + Rand.Range(0.0f, size.X), 
                    Rand.Range(position.Y - size.Y, WorldPosition.Y) + 10.0f);

                Vector2 speed = new Vector2((spawnPos.X - (WorldPosition.X + size.X / 2.0f)), (float)Math.Sqrt(size.X) * Rand.Range(20.0f, 25.0f));

                var particle = GameMain.ParticleManager.CreateParticle("steam",
                    spawnPos, speed, 0.0f, hull);

                if (particle == null) continue;

                particle.Size *= MathHelper.Clamp(size.X / 10.0f, 0.5f, 3.0f);
            }

            position.X += extinquishAmount / 2.0f;
            size.X -= extinquishAmount;

            //evaporate some of the water
            hull.Volume -= extinquishAmount;
            
            if (GameMain.Client != null) return;

            if (size.X < 1.0f) Remove();
        }

        public void Extinquish(float deltaTime, float amount, Vector2 pos)
        {
            float range = 100.0f;

            if (pos.X < WorldPosition.X - range || pos.X > WorldPosition.X + size.X + range) return;
            if (pos.Y < WorldPosition.Y - range || pos.Y > WorldPosition.Y + 500.0f) return;

            float extinquishAmount = amount * deltaTime;

            float steamCount = Rand.Range(-5.0f, (float)Math.Sqrt(amount));
            for (int i = 0; i < steamCount; i++)
            {
                Vector2 spawnPos = new Vector2(pos.X + Rand.Range(-5.0f, 5.0f), Rand.Range(position.Y - size.Y, position.Y) + 10.0f);

                Vector2 speed = new Vector2((spawnPos.X - (position.X + size.X / 2.0f)), (float)Math.Sqrt(size.X) * Rand.Range(20.0f, 25.0f));

                var particle = GameMain.ParticleManager.CreateParticle("steam",
                    spawnPos, speed, 0.0f, hull);

                if (particle == null) continue;

                particle.Size *= MathHelper.Clamp(size.X / 10.0f, 0.5f, 3.0f);
            }

            position.X += extinquishAmount / 2.0f;
            size.X -= extinquishAmount;

            hull.Volume -= extinquishAmount;

            if (GameMain.Client != null) return;

            if (size.X < 1.0f) Remove();
        }

        public void Remove()
        {
            lightSource.Remove();

            if (basicSoundIndex > -1)
            {
                Sounds.SoundManager.Stop(basicSoundIndex);
                basicSoundIndex = -1;
            }
            if (largeSoundIndex > -1)
            {
                Sounds.SoundManager.Stop(largeSoundIndex);
                largeSoundIndex = -1;
            }

            hull.RemoveFire(this);
        }
    }
}
