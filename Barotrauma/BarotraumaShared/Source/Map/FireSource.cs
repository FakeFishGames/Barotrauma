using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Barotrauma.Networking;
#if CLIENT
using Barotrauma.Lights;
using Barotrauma.Particles;
#endif

namespace Barotrauma
{
    partial class FireSource
    {
        //public static float OxygenConsumption = 50.0f;
        public static float GrowSpeed = 5.0f;
        private Vector2 LastSentSize;
        private float SyncTimer;

        private int basicSoundIndex, largeSoundIndex;

        private Hull hull;

        private Vector2 position;
        private Vector2 size;

        private Entity Submarine;

#if CLIENT
        private List<Decal> burnDecals = new List<Decal>();
#endif

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

        public float DamageRange
        {
            get {  return (float)(Math.Sqrt(size.X) * 20.0f); }
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
            
            hull.AddFireSource(this);

            Submarine = hull.Submarine;

            this.position = worldPosition - new Vector2(-5.0f, 5.0f) - Submarine.Position;

#if CLIENT
            if (fireSoundBasic == null)
            {
                fireSoundBasic = Sound.Load("Content/Sounds/fire.ogg", false);
                fireSoundLarge = Sound.Load("Content/Sounds/firelarge.ogg", false);
            }

            lightSource = new LightSource(this.position, 50.0f, new Color(1.0f, 0.9f, 0.7f), hull == null ? null : hull.Submarine);
#endif

            size = new Vector2(10.0f, 10.0f);
            LastSentSize = size;
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
                for (int j = i - 1; j >= 0; j--)
                {
                    i = Math.Min(i, fireSources.Count - 1);
                    j = Math.Min(j, i - 1);
                    
                    if (!fireSources[i].CheckOverLap(fireSources[j])) continue;

                    float leftEdge = Math.Min(fireSources[i].position.X, fireSources[j].position.X);

                    fireSources[j].size.X =
                        Math.Max(fireSources[i].position.X + fireSources[i].size.X, fireSources[j].position.X + fireSources[j].size.X)
                        - leftEdge;

                    fireSources[j].position.X = leftEdge;

#if CLIENT
                    fireSources[j].burnDecals.AddRange(fireSources[i].burnDecals);
                    fireSources[j].burnDecals.Sort((d1, d2) => { return Math.Sign(d1.WorldPosition.X - d2.WorldPosition.X); });
#endif

                    fireSources[i].Remove();
                }
            }
        }
        
        private bool CheckOverLap(FireSource fireSource)
        {
            return !(position.X > fireSource.position.X + fireSource.size.X ||
                position.X + size.X < fireSource.position.X);
        }

        public void Update(float deltaTime)
        {
            //the firesource will start to shrink if oxygen percentage is below 10
            float growModifier = Math.Min((hull.OxygenPercentage / 10.0f) - 1.0f, 1.0f);

            DamageCharacters(deltaTime);
            DamageItems(deltaTime);

            if (hull.WaterVolume > 0.0f) HullWaterExtinquish(deltaTime);

            //hull.Oxygen -= size.X * deltaTime * OxygenConsumption;
            hull.Oxygen -= size.X * deltaTime * GameMain.NilMod.FireOxygenConsumption;


            if (growModifier > 0f)
            {
                position.X -= GameMain.NilMod.FireGrowthSpeed * growModifier * 0.5f * deltaTime;

                size.X += GameMain.NilMod.FireGrowthSpeed * growModifier * deltaTime;
            }
            else
            {
                position.X -= GameMain.NilMod.FireShrinkSpeed * growModifier * 0.5f * deltaTime;

                size.X += GameMain.NilMod.FireShrinkSpeed * growModifier * deltaTime;
            }
            size.Y = MathHelper.Clamp(size.Y + GrowSpeed * growModifier * deltaTime, 10.0f, 50.0f);

            if (size.X > 50.0f)
            {
                this.position.Y = MathHelper.Lerp(this.position.Y, hull.Rect.Y - hull.Rect.Height + size.Y, deltaTime);
            }

            LimitSize();

            UpdateProjSpecific(growModifier);

            if (GameMain.Client != null) return;



            if (size.X < 1.0f)
            {
                Remove();
                return;
            }

            SyncTimer += deltaTime;

            //add in new fire syncing c:
            if (GameMain.NilMod.SyncFireSizeChange && (GameMain.Server != null && (((size.X - LastSentSize.X) > 8f || (size.X - LastSentSize.X) < 8f)) || SyncTimer > GameMain.NilMod.FireSyncFrequency))
            {
                GameMain.Server.CreateEntityEvent(hull);
                LastSentSize = size;
                SyncTimer = 0f;
            }
        }

        partial void UpdateProjSpecific(float growModifier);

        private void OnChangeHull(Vector2 pos, Hull particleHull)
        {
            if (particleHull == hull || particleHull==null) return;

            //hull already has a firesource roughly at the particles position -> don't create a new one
            if (particleHull.FireSources.Find(fs => pos.X > fs.position.X - 100.0f && pos.X < fs.position.X + fs.size.X + 100.0f) != null) return;

            new FireSource(new Vector2(pos.X, particleHull.WorldRect.Y - particleHull.Rect.Height + 5.0f));
        }

        private void DamageCharacters(float deltaTime)
        {
            if (size.X <= 0.0f) return;

            for (int i = 0; i < Character.CharacterList.Count; i++)
            {
                Character c = Character.CharacterList[i];
                if (c.AnimController.CurrentHull == null || c.IsDead) continue;

                float range = DamageRange * GameMain.NilMod.FireCharRangeMultiplier;
                if (c.Position.X < position.X - range || c.Position.X > position.X + size.X + range) continue;
                if (c.Position.Y < position.Y - size.Y || c.Position.Y > hull.Rect.Y) continue;

                float dmg = (((float)Math.Sqrt(size.X) * deltaTime * GameMain.NilMod.FireCharDamageMultiplier) / c.AnimController.Limbs.Length);
                foreach (Limb limb in c.AnimController.Limbs)
                {
                    if (GameMain.NilMod.FireUseRangedDamage)
                    {
                        float closestdistance = Math.Abs(limb.Position.X - position.X) / range;
                        if (closestdistance > Math.Abs(limb.Position.X - (position.X + size.X)) / range) closestdistance = Math.Abs((limb.Position.X - (position.X + size.X)) / range);
                        if (limb.Position.X > position.X && limb.Position.X < (position.X + size.X)) closestdistance = 0f;

                        c.AddDamage(limb.SimPosition, DamageType.Burn, (dmg * Math.Min(Math.Max(((GameMain.NilMod.FireRangedDamageStrength) - (closestdistance * (GameMain.NilMod.FireRangedDamageStrength))), GameMain.NilMod.FireRangedDamageMinMultiplier), GameMain.NilMod.FireRangedDamageMaxMultiplier)), 0, 0, false);
                    }
                    else
                    {
                        c.AddDamage(limb.SimPosition, DamageType.Burn, dmg, 0, 0, false);
                    }
                }
            }
        }

        private void DamageItems(float deltaTime)
        {
            if (size.X <= 0.0f || GameMain.Client != null) return;

            foreach (Item item in Item.ItemList)
            {
                if (item.CurrentHull != hull || item.FireProof || item.Condition <= 0.0f) continue;
                if (item.ParentInventory != null && item.ParentInventory.Owner is Character) return;

                float range = ((float)Math.Sqrt(size.X) * 10.0f) * GameMain.NilMod.FireItemRangeMultiplier;
                if (item.Position.X < position.X - range || item.Position.X > position.X + size.X + range) continue;
                if (item.Position.Y < position.Y - size.Y || item.Position.Y > hull.Rect.Y) continue;

                item.ApplyStatusEffects(ActionType.OnFire, deltaTime);
                if (item.Condition <= 0.0f && GameMain.Server != null)
                {
                    GameMain.Server.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.ApplyStatusEffect, ActionType.OnFire });
                }
            }
        }

        private void HullWaterExtinquish(float deltaTime)
        {
            //the higher the surface of the water is relative to the firesource, the faster it puts out the fire 
            float extinquishAmount = ((hull.Surface - (position.Y - size.Y)) * deltaTime) * GameMain.NilMod.FireWaterExtinguishMultiplier;

            if (extinquishAmount < 0.0f) return;

#if CLIENT
            float steamCount = Rand.Range(-5.0f, Math.Min(extinquishAmount * 100.0f, 10));
            for (int i = 0; i < steamCount; i++)
            {
                Vector2 spawnPos = new Vector2(
                    WorldPosition.X + Rand.Range(0.0f, size.X), 
                    WorldPosition.Y + 10.0f);

                Vector2 speed = new Vector2((spawnPos.X - (WorldPosition.X + size.X / 2.0f)), (float)Math.Sqrt(size.X) * Rand.Range(20.0f, 25.0f));

                var particle = GameMain.ParticleManager.CreateParticle("steam",
                    spawnPos, speed, 0.0f, hull);

                if (particle == null) continue;

                particle.Size *= MathHelper.Clamp(size.X / 10.0f, 0.5f, 3.0f);
            }
#endif

            position.X += extinquishAmount / 2.0f;
            size.X -= extinquishAmount;

            //evaporate some of the water
            hull.WaterVolume -= extinquishAmount;
            
            if (GameMain.Client != null) return;

            if (size.X < 1.0f) Remove();
        }

        public void Extinguish(float deltaTime, float amount, Vector2 pos)
        {
            float range = 100.0f;

            if (pos.X < WorldPosition.X - range || pos.X > WorldPosition.X + size.X + range) return;
            if (pos.Y < WorldPosition.Y - range || pos.Y > WorldPosition.Y + 500.0f) return;

            float extinquishAmount = (amount * deltaTime) * GameMain.NilMod.FireToolExtinguishMultiplier;

#if CLIENT
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
#endif

            position.X += extinquishAmount / 2.0f;
            size.X -= extinquishAmount;

            hull.WaterVolume -= extinquishAmount;

            if (GameMain.Client != null) return;

            if (size.X < 1.0f) Remove();
        }

        public void Remove()
        {
#if CLIENT
            lightSource.Remove();

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

            foreach (Decal d in burnDecals)
            {
                d.StopFadeIn();
            }            
#endif

            hull.RemoveFire(this);
        }
    }
}
