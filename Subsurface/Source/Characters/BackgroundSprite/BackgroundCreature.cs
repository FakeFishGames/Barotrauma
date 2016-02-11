using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{

    class BackgroundCreature : ISteerable
    {
        const float MaxDepth = 100.0f;

        const float CheckWallsInterval = 5.0f;

        public bool Enabled;

        private BackgroundCreaturePrefab prefab;

        private Vector2 position;

        private Vector3 velocity;

        private float depth;
        
        private SteeringManager steeringManager;

        private float checkWallsTimer;

        public Swarm Swarm;

        Vector2 drawPosition;
        public Vector2 TransformedPosition
        {
            get { return drawPosition; }
        }

        public Vector2 SimPosition
        {
            get { return FarseerPhysics.ConvertUnits.ToSimUnits(position); }
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
            this.prefab = prefab;

            this.position = position;

            drawPosition = position;

            steeringManager = new SteeringManager(this);

            velocity = new Vector3(
                Rand.Range(-prefab.Speed, prefab.Speed),
                Rand.Range(-prefab.Speed, prefab.Speed),
                Rand.Range(0.0f, prefab.WanderZAmount));

            checkWallsTimer = Rand.Range(0.0f, CheckWallsInterval);
            
        }

        float ang;
        Vector2 obstacleDiff;

        public void Update(float deltaTime)
        {
            position += new Vector2(velocity.X, velocity.Y) * deltaTime;
            depth = MathHelper.Clamp(depth + velocity.Z * deltaTime, 0.0f, MaxDepth);

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

                        foreach (Voronoi2.VoronoiCell cell in cells)
                        {
                            obstacleDiff += cell.Center - position;
                        }
                        obstacleDiff /= cells.Count;

                        obstacleDiff = Vector2.Normalize(obstacleDiff) * prefab.Speed;
                    }
                }
            }




            if (Swarm!=null)
            {
                Vector2 midPoint = Swarm.MidPoint();
                float midPointDist = Vector2.Distance(SimPosition, midPoint);

                if (midPointDist > Swarm.MaxDistance)
                {
                    steeringManager.SteeringSeek(midPoint, (midPointDist / Swarm.MaxDistance) * prefab.Speed);
                }
            }

            if (prefab.WanderAmount > 0.0f)
            {
                steeringManager.SteeringWander(prefab.Speed);
            }

            //Level.Loaded.Size


            if (obstacleDiff != Vector2.Zero)
            {
                steeringManager.SteeringSeek(SimPosition-obstacleDiff, prefab.Speed*2.0f);
            }

            steeringManager.Update(prefab.Speed);

            if (prefab.WanderZAmount>0.0f)
            {
                ang += Rand.Range(-prefab.WanderZAmount, prefab.WanderZAmount);
                velocity.Z = (float)Math.Sin(ang)*prefab.Speed;
            }
            
            velocity = Vector3.Lerp(velocity, new Vector3(Steering.X, Steering.Y, velocity.Z), deltaTime);
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            float rotation = 0.0f;
            if (!prefab.DisableRotation)
            {
                rotation = MathUtils.VectorToAngle(new Vector2(velocity.X, -velocity.Y));
                if (velocity.X < 0.0f) rotation -= MathHelper.Pi;
            }

            drawPosition = position;// +Level.Loaded.Position;

            if (depth > 0.0f)
            {
                Vector2 camOffset = drawPosition - GameMain.GameScreen.Cam.WorldViewCenter;

                drawPosition -= camOffset * (depth / MaxDepth) * 0.05f;
            }
            
            prefab.Sprite.Draw(spriteBatch, new Vector2(drawPosition.X, -drawPosition.Y), Color.Lerp(Color.White, Color.DarkBlue, (depth/MaxDepth)*0.3f),
                rotation, 1.0f - (depth / MaxDepth) * 0.2f, velocity.X > 0.0f ? SpriteEffects.None : SpriteEffects.FlipHorizontally, (depth / MaxDepth));
        }
    }

    class Swarm
    {
        public List<BackgroundCreature> Members;

        public readonly float MaxDistance;

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

        public Swarm(List<BackgroundCreature> members, float maxDistance)
        {
            this.Members = members;

            this.MaxDistance = maxDistance;

            foreach (BackgroundCreature bgSprite in members)
            {
                bgSprite.Swarm = this;
            }
        }
    }
}
