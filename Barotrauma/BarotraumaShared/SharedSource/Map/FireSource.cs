using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Barotrauma.Extensions;
#if CLIENT
using Barotrauma.Sounds;
using Barotrauma.Lights;
using Barotrauma.Particles;
#endif
using FarseerPhysics;
using System.Linq;

namespace Barotrauma
{
    partial class FireSource : ISpatialEntity
    {
        const float OxygenConsumption = 50.0f;
        const float GrowSpeed = 20.0f;
        const float MaxDamageRange = 250.0f;

        protected Hull hull;

        protected Vector2 position;
        protected Vector2 size;

        private readonly Submarine submarine;
        public Submarine Submarine => submarine;

        protected bool removed;

        private readonly List<Decal> burnDecals = new List<Decal>();

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
            get { return Submarine == null ? position : Submarine.Position + position; }
        }

        public Vector2 SimPosition => ConvertUnits.ToSimUnits(Position);

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

        public virtual float DamageRange
        {
            get { return Math.Min((float)Math.Sqrt(size.X) * 10.0f, MaxDamageRange); }
        }

        public bool DamagesItems
        {
            get;
            set;
        } = true;

        public bool DamagesCharacters
        {
            get;
            set;
        } = true;

        public bool Removed
        {
            get { return removed; }
        }

        public Hull Hull
        {
            get { return hull; }
        }

        public FireSource(Vector2 worldPosition, Hull spawningHull = null, bool isNetworkMessage = false)
        {
            hull = Hull.FindHull(worldPosition, spawningHull);
            if (hull == null || worldPosition.Y < hull.WorldSurface) return;

#if CLIENT
            if (!isNetworkMessage && GameMain.Client != null) { return; }
#endif
            
            hull.AddFireSource(this);
            
            position = worldPosition - new Vector2(-5.0f, 5.0f);
            if (hull.Submarine != null)
            {
                submarine = hull.Submarine;
                position -= Submarine.Position;
            }

#if CLIENT
            lightSource = new LightSource(this.position, 50.0f, new Color(1.0f, 0.9f, 0.7f), hull?.Submarine);
#endif

            size = new Vector2(10.0f, 10.0f);
        }

        protected virtual void LimitSize()
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

                    if (!fireSources[i].CheckOverLap(fireSources[j])) { continue; }

                    float leftEdge = Math.Min(fireSources[i].position.X, fireSources[j].position.X);

                    fireSources[j].size.X =
                        Math.Max(fireSources[i].position.X + fireSources[i].size.X, fireSources[j].position.X + fireSources[j].size.X)
                        - leftEdge;

                    fireSources[j].position.X = leftEdge;
                    fireSources[j].burnDecals.AddRange(fireSources[i].burnDecals);
                    fireSources[j].burnDecals.Sort((d1, d2) => { return Math.Sign(d1.WorldPosition.X - d2.WorldPosition.X); });
                    fireSources[i].Remove();
                }
            }
        }

        public static void UpdateAll(List<DummyFireSource> fireSources, float deltaTime)
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

                    if (!fireSources[i].CheckOverLap(fireSources[j])) { continue; }

                    float leftEdge = Math.Min(fireSources[i].position.X, fireSources[j].position.X);

                    fireSources[j].size.X =
                        Math.Max(fireSources[i].position.X + fireSources[i].size.X, fireSources[j].position.X + fireSources[j].size.X)
                        - leftEdge;

                    fireSources[j].position.X = leftEdge;
                    fireSources[i].Remove();
                }
            }
        }

        private bool CheckOverLap(FireSource fireSource)
        {
            if (this is DummyFireSource != fireSource is DummyFireSource)
            {
                return false;
            }

            return !(position.X > fireSource.position.X + fireSource.size.X ||
                position.X + size.X < fireSource.position.X);
        }

        public void Update(float deltaTime)
        {
            //the firesource will start to shrink if oxygen percentage is below 10
            float growModifier = Math.Min((hull.OxygenPercentage / 10.0f) - 1.0f, 1.0f);

            if (DamagesCharacters) { DamageCharacters(deltaTime); }
            if (DamagesItems) { DamageItems(deltaTime); }

            if (hull.WaterVolume > 0.0f)
            {
                HullWaterExtinguish(deltaTime);
                if (removed) { return; }
            }

            if (!(this is DummyFireSource))
            {
                ReduceOxygen(deltaTime);
            }

            AdjustXPos(growModifier, deltaTime);

            size.X += GrowSpeed * growModifier * deltaTime;
            size.Y = MathHelper.Clamp(size.Y + GrowSpeed * growModifier * deltaTime, 10.0f, 50.0f);

            if (size.X > 50.0f)
            {
                this.position.Y = MathHelper.Lerp(this.position.Y, hull.Rect.Y - hull.Rect.Height + size.Y, deltaTime);
            }

            LimitSize();

            if (size.X > 256.0f && !(this is DummyFireSource))
            {
                if (burnDecals.Count == 0)
                {
                    var newDecal = hull.AddDecal("burnt", WorldPosition + size / 2, 1f, isNetworkEvent: false);
                    if (newDecal != null) { burnDecals.Add(newDecal); }
                }
                else if (WorldPosition.X < burnDecals[0].WorldPosition.X - 256.0f)
                {
                    var newDecal = hull.AddDecal("burnt", WorldPosition, 1f, isNetworkEvent: false);
                    if (newDecal != null) { burnDecals.Insert(0, newDecal); }
                }
                else if (WorldPosition.X + size.X > burnDecals[burnDecals.Count - 1].WorldPosition.X + 256.0f)
                {
                    var newDecal = hull.AddDecal("burnt", WorldPosition + Vector2.UnitX * size.X, 1f, isNetworkEvent: false);
                    if (newDecal != null) { burnDecals.Add(newDecal); }
                }
            }

            foreach (Decal d in burnDecals)
            {
                //prevent the decals from fading out as long as the firesource is alive
                d.ForceRefreshFadeTimer(Math.Min(d.FadeTimer, d.FadeInTime));
            }

            UpdateProjSpecific(growModifier);

            
            if (size.X < 1.0f && (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer))
            {
                Remove();
            }
        }

        protected virtual void ReduceOxygen(float deltaTime)
        {
            hull.Oxygen -= size.X * deltaTime * OxygenConsumption;
        }

        protected virtual void AdjustXPos(float growModifier, float deltaTime)
        {
            position.X -= GrowSpeed * growModifier * 0.5f * deltaTime;
        }

        partial void UpdateProjSpecific(float growModifier);

        private void OnChangeHull(Vector2 pos, Hull particleHull)
        {
            if (particleHull == hull || particleHull == null) return;

            //hull already has a firesource roughly at the particles position -> don't create a new one
            if (particleHull.FireSources.Find(fs => pos.X > fs.position.X - 100.0f && pos.X < fs.position.X + fs.size.X + 100.0f) != null) return;

            new FireSource(new Vector2(pos.X, particleHull.WorldRect.Y - particleHull.Rect.Height + 5.0f));
        }

        private void DamageCharacters(float deltaTime)
        {
            if (size.X <= 0.0f) { return; }

            for (int i = 0; i < Character.CharacterList.Count; i++)
            {
                Character c = Character.CharacterList[i];
                if (c.CurrentHull == null || c.IsDead) { continue; }

                if (!IsInDamageRange(c, DamageRange)) { continue; }

                //GetApproximateDistance returns float.MaxValue if there's no path through open gaps between the hulls (e.g. if there's a door/wall in between)
                if (hull.GetApproximateDistance(Position, c.Position, c.CurrentHull, 10000.0f) > size.X + DamageRange)
                {
                    continue;
                }

                float dmg = (float)Math.Sqrt(Math.Min(500, size.X)) * deltaTime / c.AnimController.Limbs.Count(l => !l.IsSevered && !l.Hidden);
                foreach (Limb limb in c.AnimController.Limbs)
                {
                    if (limb.IsSevered) { continue; }
                    c.LastDamageSource = null;
                    c.DamageLimb(WorldPosition, limb, AfflictionPrefab.Burn.Instantiate(dmg).ToEnumerable(), 0.0f, false, 0.0f);
                }
#if CLIENT
                //let clients display the client-side damage immediately, otherwise they may not be able to react to the damage fast enough
                c.CharacterHealth.DisplayedVitality = c.Vitality;
#endif
                c.ApplyStatusEffects(ActionType.OnFire, deltaTime);
            }
        }

        public bool IsInDamageRange(Character c, float damageRange)
        {
            if (c.Position.X < position.X - damageRange || c.Position.X > position.X + size.X + damageRange) return false;
            if (c.Position.Y < position.Y - size.Y || c.Position.Y > hull.Rect.Y) return false;

            return true;
        }

        public bool IsInDamageRange(Vector2 worldPosition, float damageRange)
        {
            if (worldPosition.X < WorldPosition.X - damageRange || worldPosition.X > WorldPosition.X + size.X + damageRange) return false;
            if (worldPosition.Y < WorldPosition.Y - size.Y || worldPosition.Y > hull.WorldRect.Y) return false;

            return true;
        }

        private void DamageItems(float deltaTime)
        {
            if (size.X <= 0.0f) { return; }
#if CLIENT
            if (GameMain.Client != null) { return; }
#endif
            foreach (Item item in Item.ItemList)
            {
                if (item.CurrentHull != hull || item.FireProof || item.Condition <= 0.0f) { continue; }

                //don't apply OnFire effects if the item is inside a fireproof container
                //(or if it's inside a container that's inside a fireproof container, etc)
                Item container = item.Container;
                bool fireProof = false;
                while (container != null)
                {
                    if (container.FireProof) 
                    { 
                        fireProof = true; 
                        break; 
                    }
                    container = container.Container;
                }
                if (fireProof) { continue; }

                float range = (float)Math.Sqrt(size.X) * 10.0f;
                if (item.Position.X < position.X - range || item.Position.X > position.X + size.X + range) { continue; }
                if (item.Position.Y < position.Y - size.Y || item.Position.Y > hull.Rect.Y) { continue; }

                item.ApplyStatusEffects(ActionType.OnFire, deltaTime);
                if (item.Condition <= 0.0f && GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
                {
                    GameMain.NetworkMember.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.ApplyStatusEffect, ActionType.OnFire });
                }
            }
        }

        private void HullWaterExtinguish(float deltaTime)
        {
            //the higher the surface of the water is relative to the firesource, the faster it puts out the fire 
            float extinguishAmount = (hull.Surface - (position.Y - size.Y)) * deltaTime;

            if (extinguishAmount < 0.0f) return;

#if CLIENT
            float steamCount = Rand.Range(-5.0f, Math.Min(extinguishAmount * 100.0f, 10));
            for (int i = 0; i < steamCount; i++)
            {
                Vector2 spawnPos = new Vector2(
                    WorldPosition.X + Rand.Range(0.0f, size.X),
                    WorldPosition.Y + 10.0f);

                Vector2 speed = new Vector2((spawnPos.X - (WorldPosition.X + size.X / 2.0f)), (float)Math.Sqrt(size.X) * Rand.Range(20.0f, 25.0f));

                var particle = GameMain.ParticleManager.CreateParticle("steam",
                    spawnPos, speed, 0.0f, hull);
            }
#endif
            extinguishAmount = Math.Min(size.X, extinguishAmount);

            position.X += extinguishAmount / 2.0f;
            size.X -= extinguishAmount;

            //evaporate some of the water
            hull.WaterVolume -= extinguishAmount;

            if (size.X < 1.0f && (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer))
            {
                Remove();
            }
        }

        public void Extinguish(float deltaTime, float amount)
        {
            float extinguishAmount = amount * deltaTime;

#if CLIENT
            float steamCount = Rand.Range(-5.0f, (float)Math.Sqrt(amount));
            for (int i = 0; i < steamCount; i++)
            {
                Vector2 spawnPos = new Vector2(Rand.Range(position.X, position.X + size.X), Rand.Range(position.Y - size.Y, position.Y) + 10.0f);

                Vector2 speed = new Vector2((spawnPos.X - (position.X + size.X / 2.0f)), (float)Math.Sqrt(size.X) * Rand.Range(20.0f, 25.0f));

                var particle = GameMain.ParticleManager.CreateParticle("steam",
                    spawnPos, speed, 0.0f, hull);
            }
#endif
            extinguishAmount = Math.Min(size.X, extinguishAmount);

            position.X += extinguishAmount / 2.0f;
            size.X -= extinguishAmount;

            hull.WaterVolume -= extinguishAmount;
            
            if (size.X < 1.0f && (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer))
            {
                Remove();
            }
        }

        public void Extinguish(float deltaTime, float amount, Vector2 worldPosition)
        {
            if (IsInDamageRange(worldPosition, 100.0f))
            {
                Extinguish(deltaTime, amount);
            }
        }

        public void Remove()
        {
#if CLIENT
            lightSource?.Remove();
            lightSource = null;
#endif            
            foreach (Decal d in burnDecals)
            {
                d.StopFadeIn();
            }
            hull?.RemoveFire(this);
            removed = true;
        }
    }
}
