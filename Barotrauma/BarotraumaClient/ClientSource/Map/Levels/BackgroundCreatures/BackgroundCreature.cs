using Barotrauma.SpriteDeformations;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{

    class BackgroundCreature : ISteerable
    {
        const float MaxDepth = 10000.0f;

        const float CheckWallsInterval = 5.0f;

        public bool Visible;

        public readonly BackgroundCreaturePrefab Prefab;

        private readonly List<SpriteDeformation> uniqueSpriteDeformations = new List<SpriteDeformation>();
        private readonly List<SpriteDeformation> spriteDeformations = new List<SpriteDeformation>();
        private readonly List<SpriteDeformation> lightSpriteDeformations = new List<SpriteDeformation>();
        
        private Vector2 position;

        private Vector3 velocity;

        private float depth;

        private float alpha = 1.0f;
        
        private readonly SteeringManager steeringManager;

        private float checkWallsTimer, flashTimer;

        private float wanderZPhase;
        private Vector2 obstacleDiff;
        private float obstacleDist;

        public Swarm Swarm;

        Vector2 drawPosition;

        public Vector2[,] CurrentSpriteDeformation
        {
            get;
            private set;
        }
        public Vector2[,] CurrentLightSpriteDeformation
        {
            get;
            private set;
        }

        public Vector2 SimPosition
        {
            get { return FarseerPhysics.ConvertUnits.ToSimUnits(position); }
        }

        public Vector2 WorldPosition
        {
            get { return position; }
        }

        public Vector2 Velocity
        {
            get { return new Vector2(velocity.X, velocity.Y); }
        }

        public Vector2 Steering
        {
            get;
            set;
        }

        public BackgroundCreature(BackgroundCreaturePrefab prefab, Vector2 position)
        {
            this.Prefab = prefab;
            this.position = position;

            drawPosition = position;

            steeringManager = new SteeringManager(this);

            velocity = new Vector3(
                Rand.Range(-prefab.Speed, prefab.Speed, Rand.RandSync.ClientOnly),
                Rand.Range(-prefab.Speed, prefab.Speed, Rand.RandSync.ClientOnly),
                Rand.Range(0.0f, prefab.WanderZAmount, Rand.RandSync.ClientOnly));

            checkWallsTimer = Rand.Range(0.0f, CheckWallsInterval, Rand.RandSync.ClientOnly);

            foreach (XElement subElement in prefab.Config.Elements())
            {
                List<SpriteDeformation> deformationList = null;
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "deformablesprite":
                        deformationList = spriteDeformations;
                        break;
                    case "deformablelightsprite":
                        deformationList = lightSpriteDeformations;
                        break;
                    default:
                        continue;
                }
                foreach (XElement animationElement in subElement.Elements())
                {
                    SpriteDeformation deformation = null;
                    int sync = animationElement.GetAttributeInt("sync", -1);
                    if (sync > -1)
                    {
                        string typeName = animationElement.GetAttributeString("type", "").ToLowerInvariant();
                        deformation = uniqueSpriteDeformations.Find(d => d.TypeName == typeName && d.Sync == sync);
                    }
                    if (deformation == null)
                    {
                        deformation = SpriteDeformation.Load(animationElement, prefab.Name);
                        if (deformation != null)
                        {
                            uniqueSpriteDeformations.Add(deformation);
                        }
                    }
                    if (deformation != null)
                    {
                        deformationList.Add(deformation);
                    }
                }
            }
        }
        
        public void Update(float deltaTime)
        {
            position += new Vector2(velocity.X, velocity.Y) * deltaTime;
            depth = MathHelper.Clamp(depth + velocity.Z * deltaTime, Prefab.MinDepth, Prefab.MaxDepth * 10);

            if (Prefab.FlashInterval > 0.0f)
            {
                flashTimer -= deltaTime;
                if (flashTimer > 0.0f)
                {
                    alpha = 0.0f;
                }
                else
                {
                    //value goes from 0 to 1 and back to 0 during the flash
                    alpha = (float)Math.Sin(-flashTimer / Prefab.FlashDuration * MathHelper.Pi) * PerlinNoise.GetPerlin((float)Timing.TotalTime * 0.1f, (float)Timing.TotalTime * 0.2f);
                    if (flashTimer < -Prefab.FlashDuration)
                    {
                        flashTimer = Prefab.FlashInterval;
                    }
                }
            }

            checkWallsTimer -= deltaTime;
            if (checkWallsTimer <= 0.0f && Level.Loaded != null)
            {
                checkWallsTimer = CheckWallsInterval;

                obstacleDiff = Vector2.Zero;
                if (position.Y > Level.Loaded.Size.Y)
                {
                    obstacleDiff = Vector2.UnitY;
                }
                else if (position.Y < 0.0f)
                {
                    obstacleDiff = -Vector2.UnitY;
                }
                else if (position.X < 0.0f)
                {
                    obstacleDiff = Vector2.UnitX;
                }
                else if (position.X > Level.Loaded.Size.X)
                {
                    obstacleDiff = -Vector2.UnitX;
                }
                else
                {
                    var cells = Level.Loaded.GetCells(position, 1);
                    if (cells.Count > 0)
                    {
                        int cellCount = 0;
                        foreach (Voronoi2.VoronoiCell cell in cells)
                        {
                            Vector2 diff = cell.Center - position;
                            if (diff.LengthSquared() > 5000.0f * 5000.0f) continue;
                            obstacleDiff += diff;
                            cellCount++;
                        }
                        if (cellCount > 0)
                        {
                            obstacleDiff /= cellCount;
                            obstacleDist = obstacleDiff.Length();
                            obstacleDiff = Vector2.Normalize(obstacleDiff);
                        }
                    }
                }
            }

            if (Swarm != null)
            {
                Vector2 midPoint = Swarm.MidPoint();
                float midPointDist = Vector2.Distance(SimPosition, midPoint) * 100.0f;
                if (midPointDist > Swarm.MaxDistance)
                {
                    steeringManager.SteeringSeek(midPoint, ((midPointDist / Swarm.MaxDistance) - 1.0f) * Prefab.Speed);
                }
                steeringManager.SteeringManual(deltaTime, Swarm.AvgVelocity() * Swarm.Cohesion);
            }

            if (Prefab.WanderAmount > 0.0f)
            {
                steeringManager.SteeringWander(Prefab.Speed);
            }

            if (obstacleDiff != Vector2.Zero)
            {
                steeringManager.SteeringManual(deltaTime, -obstacleDiff * (1.0f - obstacleDist / 5000.0f) * Prefab.Speed);
            }

            steeringManager.Update(Prefab.Speed);

            if (Prefab.WanderZAmount > 0.0f)
            {
                wanderZPhase += Rand.Range(-Prefab.WanderZAmount, Prefab.WanderZAmount);
                velocity.Z = (float)Math.Sin(wanderZPhase) * Prefab.Speed;
            }

            velocity = Vector3.Lerp(velocity, new Vector3(Steering.X, Steering.Y, velocity.Z), deltaTime);

            UpdateDeformations(deltaTime);            
        }

        public void DrawLightSprite(SpriteBatch spriteBatch, Camera cam)
        {
            Draw(spriteBatch, cam, Prefab.LightSprite, Prefab.DeformableLightSprite, CurrentLightSpriteDeformation, Color.White * alpha);
        }

        public void Draw(SpriteBatch spriteBatch, Camera cam)
        {
            Draw(spriteBatch, 
                cam, 
                Prefab.Sprite, 
                Prefab.DeformableSprite, 
                CurrentSpriteDeformation,
                Color.Lerp(Color.White, Level.Loaded.BackgroundColor, depth / Math.Max(MaxDepth, Prefab.MaxDepth)) * alpha);
        }

        private void Draw(SpriteBatch spriteBatch, Camera cam, Sprite sprite, DeformableSprite deformableSprite, Vector2[,] currentSpriteDeformation, Color color)
        {
            if (sprite == null && deformableSprite == null) { return; }
            if (color.A == 0) { return; }

            float rotation = 0.0f;
            if (!Prefab.DisableRotation)
            {
                rotation = MathUtils.VectorToAngle(new Vector2(velocity.X, -velocity.Y));
                if (velocity.X < 0.0f) { rotation -= MathHelper.Pi; }
            }

            drawPosition = GetDrawPosition(cam);

            float scale = GetScale();
            sprite?.Draw(spriteBatch,
                new Vector2(drawPosition.X, -drawPosition.Y),
                color,
                rotation, 
                scale,
                Prefab.DisableFlipping || velocity.X > 0.0f ? SpriteEffects.None : SpriteEffects.FlipHorizontally,
                Math.Min(depth / MaxDepth, 1.0f));

            if (deformableSprite != null)
            {
                if (currentSpriteDeformation != null)
                {
                    deformableSprite.Deform(currentSpriteDeformation);
                }
                else
                {
                    deformableSprite.Reset();
                }
                deformableSprite?.Draw(cam,
                    new Vector3(drawPosition.X, drawPosition.Y, Math.Min(depth / 10000.0f, 1.0f)),
                    deformableSprite.Origin,
                    rotation,
                    Vector2.One * scale,
                    color,
                    mirror: Prefab.DisableFlipping || velocity.X <= 0.0f);
            }
        }

        public Vector2 GetDrawPosition(Camera cam)
        {
            Vector2 drawPosition = WorldPosition;
            if (depth >= 0)
            {
                Vector2 camOffset = drawPosition - cam.WorldViewCenter;
                drawPosition -= camOffset * depth / MaxDepth;
            }
            return drawPosition;
        }

        public float GetScale()
        {
            return Math.Max(1.0f - depth / MaxDepth, 0.05f) * Prefab.Scale;
        }

        public Rectangle GetExtents(Camera cam)
        {
            Vector2 min = GetDrawPosition(cam);
            Vector2 max = min;

            float scale = GetScale();
            GetSpriteExtents(Prefab.Sprite, ref min, ref max);
            GetSpriteExtents(Prefab.LightSprite, ref min, ref max);
            GetSpriteExtents(Prefab.DeformableSprite?.Sprite, ref min, ref max);
            GetSpriteExtents(Prefab.DeformableLightSprite?.Sprite, ref min, ref max);

            return new Rectangle(min.ToPoint(), (max - min).ToPoint());

            void GetSpriteExtents(Sprite sprite, ref Vector2 min, ref Vector2 max)
            {
                if (sprite == null) { return; }
                min.X = Math.Min(min.X, min.X - sprite.size.X * sprite.RelativeOrigin.X * scale);
                min.Y = Math.Min(min.Y, min.Y - sprite.size.Y * sprite.RelativeOrigin.Y * scale);
                max.X = Math.Max(max.X, max.X + sprite.size.X * (1.0f - sprite.RelativeOrigin.X) * scale);
                max.Y = Math.Max(max.Y, max.Y + sprite.size.Y * (1.0f - sprite.RelativeOrigin.Y) * scale);
            }
        }

        private void UpdateDeformations(float deltaTime)
        {
            foreach (SpriteDeformation deformation in uniqueSpriteDeformations)
            {
                deformation.Update(deltaTime);
            }
            if (spriteDeformations.Count > 0)
            {
                CurrentSpriteDeformation = SpriteDeformation.GetDeformation(spriteDeformations, Prefab.DeformableSprite.Size);
            }
            if (lightSpriteDeformations.Count > 0)
            {
                CurrentLightSpriteDeformation = SpriteDeformation.GetDeformation(lightSpriteDeformations, Prefab.DeformableLightSprite.Size);
            }
        }
    }

    class Swarm
    {
        public List<BackgroundCreature> Members;

        public readonly float MaxDistance;
        public readonly float Cohesion;

        public Vector2 MidPoint()
        {
            if (Members.Count == 0) return Vector2.Zero;

            Vector2 midPoint = Vector2.Zero;

            foreach (BackgroundCreature member in Members)
            {
                midPoint += member.SimPosition;
            }

            midPoint /= Members.Count;

            return midPoint;
        }

        public Vector2 AvgVelocity()
        {
            if (Members.Count == 0) return Vector2.Zero;

            Vector2 avgVel = Vector2.Zero;
            foreach (BackgroundCreature member in Members)
            {
                avgVel += member.Velocity;
            }
            avgVel /= Members.Count;
            return avgVel;
        }

        public Swarm(List<BackgroundCreature> members, float maxDistance, float cohesion)
        {
            Members = members;
            MaxDistance = maxDistance;
            Cohesion = cohesion;
            foreach (BackgroundCreature bgSprite in members)
            {
                bgSprite.Swarm = this;
            }
        }
    }
}
