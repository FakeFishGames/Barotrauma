using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using Voronoi2;

namespace Barotrauma
{
    partial class DestructibleLevelWall : LevelWall, IDamageable 
    {
        public bool NetworkUpdatePending;

        public float Damage
        {
            get;
            private set;
        }

        public float MaxHealth
        {
            get;
            private set;
        } = 1000.0f;

        public bool Destroyed
        {
            get;
            private set;
        }

        public float FadeOutDuration
        {
            get;
            private set;
        }

        public float FadeOutTimer
        {
            get;
            private set;
        }

        public Vector2 SimPosition
        {
            get { return Body.Position; }
        }

        public Vector2 WorldPosition 
        {
            get { return ConvertUnits.ToDisplayUnits(Body.Position); }
        }

        public float Health 
        { 
            get { return MaxHealth - Damage; } 
        }

        public DestructibleLevelWall(List<Vector2> vertices, Color color, Level level, float? health = null, bool giftWrap = false)
            : base (vertices, color, level, giftWrap)
        {
            MaxHealth = health ?? MathHelper.Clamp(Body.Mass, 100.0f, 1000.0f);
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            if (FadeOutDuration > 0.0f)
            {
                FadeOutTimer += deltaTime;
                if (FadeOutTimer > FadeOutDuration && (GameMain.NetworkMember == null || GameMain.NetworkMember.IsClient)) { Destroy(); }
            }
        }

        public void AddDamage(float damage, Vector2 worldPosition)
        {
            AddDamageProjSpecific(damage, worldPosition);
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            if (Destroyed) { return; }
            if (!MathUtils.NearlyEqual(damage, 0.0f)) { NetworkUpdatePending = true; }
            Damage += damage;
            if (Damage >= MaxHealth)
            {
                CreateFragments();
                Destroy();
            }
        }

        partial void AddDamageProjSpecific(float damage, Vector2 worldPosition);


        public AttackResult AddDamage(Character attacker, Vector2 worldPosition, Attack attack, float deltaTime, bool playSound = true)
        {
            AddDamage(attack.StructureDamage, worldPosition);
            return new AttackResult(attack.StructureDamage);
        }

        private void CreateFragments()
        {
#if CLIENT
            SoundPlayer.PlaySound("icebreak", WorldPosition);
#endif
            //generate initial triangles (one triangle from each edge to the center of the cell)
            List<List<Vector2>> triangles = new List<List<Vector2>>();
            foreach (var cell in Cells)
            {
                foreach (GraphEdge edge in cell.Edges)
                {
                    List<Vector2> triangleVerts = new List<Vector2>
                    {
                        edge.Point1 + cell.Translation,
                        edge.Point2 + cell.Translation,
                        cell.Center
                    };
                    triangles.Add(triangleVerts);
                }
            }

            //split triangles that have edges more than 1000 units long
            Pair<int, int> longestEdge = new Pair<int, int>(-1, -1);
            float longestEdgeLength = 0.0f;
            do
            {
                longestEdge.First = -1;
                longestEdge.Second = -1;
                longestEdgeLength = 0.0f;
                for (int i = 0; i < triangles.Count; i++)
                {
                    for (int edge = 0; edge < 3; edge++)
                    {
                        float edgeLength = Vector2.Distance(triangles[i][edge], triangles[i][(edge + 1) % 3]);
                        if (edgeLength > longestEdgeLength)
                        {
                            longestEdge.First = i;
                            longestEdge.Second = edge;
                            longestEdgeLength = edgeLength;
                        }
                    }
                }
                if (longestEdgeLength < 1000.0f)
                {
                    break;
                }
                Vector2 p0 = triangles[longestEdge.First][longestEdge.Second];
                Vector2 p1 = triangles[longestEdge.First][(longestEdge.Second + 1) % 3];
                Vector2 p2 = triangles[longestEdge.First][(longestEdge.Second + 2) % 3];
                triangles[longestEdge.First] = new List<Vector2> { p0, (p0 + p1) / 2, p2 };
                triangles.Add(new List<Vector2> { (p0 + p1) / 2, p1, p2 });


            } while (triangles.Count < 32);

            //generate fragments
            foreach (var triangle in triangles)
            {
                Vector2 triangleCenter = (triangle[0] + triangle[1]+ triangle[2]) / 3;
                triangle[0] -= triangleCenter;
                triangle[1] -= triangleCenter;
                triangle[2] -= triangleCenter;
                Vector2 simTriangleCenter = ConvertUnits.ToSimUnits(triangleCenter);

                DestructibleLevelWall fragment = new DestructibleLevelWall(triangle, Color.White, Level.Loaded, giftWrap: true);
                fragment.Damage = fragment.MaxHealth;
                fragment.Body.Position = simTriangleCenter;
                fragment.Body.BodyType = BodyType.Dynamic;
                fragment.Body.FixedRotation = false;
                fragment.Body.LinearDamping = Rand.Range(0.2f, 0.3f);
                fragment.Body.AngularDamping = Rand.Range(0.1f, 0.2f);
                fragment.Body.GravityScale = 0.1f;
                fragment.Body.Mass *= 10.0f;
                fragment.Body.CollisionCategories = Physics.CollisionNone;
                fragment.Body.CollidesWith = Physics.CollisionWall;
                fragment.FadeOutDuration = 20.0f;

                Vector2 bodyDiff = simTriangleCenter - Body.Position;
                fragment.Body.LinearVelocity = (bodyDiff + Rand.Vector(0.5f)).ClampLength(15.0f);
                fragment.Body.AngularVelocity = Rand.Range(-0.5f, 0.5f);// MathHelper.Clamp(-bodyDiff.X * 0.1f, -0.5f, 0.5f);

                Level.Loaded.UnsyncedExtraWalls.Add(fragment);

#if CLIENT
                for (int i = 0; i < 20; i++)
                {
                    int startEdgeIndex = Rand.Int(3);
                    Vector2 pos1 = triangle[startEdgeIndex];
                    Vector2 pos2 = triangle[(startEdgeIndex + 1) % 3];

                    var particle = GameMain.ParticleManager.CreateParticle("iceshards",
                        triangleCenter + Vector2.Lerp(pos1, pos2, Rand.Range(0.0f, 1.0f)),
                        Rand.Vector(Rand.Range(50.0f, 1000.0f)) + fragment.Body.LinearVelocity * 100.0f);
                    if (particle != null)
                    {
                        particle.Size *= Rand.Range(1.0f, 5.0f);
                    }
                }
#endif
            }
        }

        public void Destroy()
        {
            if (Destroyed) { return; }
            Destroyed = true;
            level?.UnsyncedExtraWalls?.Remove(this);
            GameMain.World.Remove(Body);
            Dispose();
        }

    }
}
