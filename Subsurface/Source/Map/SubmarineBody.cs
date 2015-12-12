using FarseerPhysics;
using FarseerPhysics.Collision;
using FarseerPhysics.Common;
using FarseerPhysics.Common.Decomposition;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using FarseerPhysics.Factories;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Voronoi2;

namespace Barotrauma
{
    class SubmarineBody
    {
        public const float DamageDepth = -10000.0f;
        const float PressureDamageMultiplier = 0.001f;

        //structure damage = impact * damageMultiplier
        const float DamageMultiplier = 50.0f;

        const float Friction = 0.2f, Restitution = 0.0f;
        
        public List<Vector2> HullVertices
        {
            get;
            private set;
        }

        private float depthDamageTimer;

        private Submarine submarine;
        
        private Body body;

        private Vector2 targetPosition;
                
        float mass = 10000.0f;

        //private Vector2? lastContactPoint;
        //private VoronoiCell lastContactCell;

        public Rectangle Borders
        {
            get;
            private set;
        }
                
        public Vector2 Velocity
        {
            get { return body.LinearVelocity; }
            set
            {
                if (!MathUtils.IsValid(value)) return;
                body.LinearVelocity = value;
            }
        }

        public Vector2 TargetPosition
        {
            get { return targetPosition; }
            set
            {
                if (!MathUtils.IsValid(value)) return;
                targetPosition = value;
            }
        }

        public Vector2 Position
        {
            get { return ConvertUnits.ToDisplayUnits(body.Position); }
        }

        public Vector2 Center
        {
            get { return new Vector2(Borders.X + Borders.Width / 2, Borders.Y - Borders.Height / 2); }
        }

        public bool AtDamageDepth
        {
            get { return Position.Y < DamageDepth; }
        }

        public SubmarineBody(Submarine sub)
        {
            this.submarine = sub;


            List<Vector2> convexHull = GenerateConvexHull();
            HullVertices = convexHull;

            for (int i = 0; i < convexHull.Count; i++)
            {
                convexHull[i] = ConvertUnits.ToSimUnits(convexHull[i]);
            }

            convexHull.Reverse();

            //get farseer 'vertices' from vectors
            Vertices shapevertices = new Vertices(convexHull);

            AABB hullAABB = shapevertices.GetAABB();

            Borders = new Rectangle(
                (int)ConvertUnits.ToDisplayUnits(hullAABB.LowerBound.X),
                (int)ConvertUnits.ToDisplayUnits(hullAABB.UpperBound.Y),
                (int)ConvertUnits.ToDisplayUnits(hullAABB.Extents.X * 2.0f),
                (int)ConvertUnits.ToDisplayUnits(hullAABB.Extents.Y * 2.0f));


            var triangulatedVertices = Triangulate.ConvexPartition(shapevertices, TriangulationAlgorithm.Bayazit);

            body = BodyFactory.CreateCompoundPolygon(GameMain.World, triangulatedVertices, 5.0f);
            body.BodyType = BodyType.Dynamic;

            body.CollisionCategories = Physics.CollisionMisc | Physics.CollisionWall;
            body.CollidesWith = Physics.CollisionLevel | Physics.CollisionCharacter;
            body.Restitution = Restitution;
            body.Friction = Friction;
            body.FixedRotation = true;
            body.Mass = mass;
            body.Awake = true;
            body.SleepingAllowed = false;
            body.IgnoreGravity = true;
            body.OnCollision += OnCollision;
            body.UserData = this;
        }


        private List<Vector2> GenerateConvexHull()
        {
            if (!Structure.wallList.Any())
            {
                return new List<Vector2>() { new Vector2(-1.0f, 1.0f), new Vector2(1.0f, 1.0f), new Vector2(0.0f, -1.0f) };
            }

            List<Vector2> points = new List<Vector2>();

            Vector2 leftMost = Vector2.Zero;

            foreach (Structure wall in Structure.wallList)
            {
                for (int x = -1; x <= 1; x += 2)
                {
                    for (int y = -1; y <= 1; y += 2)
                    {
                        Vector2 corner = new Vector2(wall.Rect.X + wall.Rect.Width / 2.0f, wall.Rect.Y - wall.Rect.Height / 2.0f);
                        corner.X += x * wall.Rect.Width / 2.0f;
                        corner.Y += y * wall.Rect.Height / 2.0f;

                        if (points.Contains(corner)) continue;

                        points.Add(corner);
                        if (leftMost == Vector2.Zero || corner.X < leftMost.X) leftMost = corner;
                    }
                }
            }

            List<Vector2> hullPoints = new List<Vector2>();

            Vector2 currPoint = leftMost;
            Vector2 endPoint;
            do
            {
                hullPoints.Add(currPoint);
                endPoint = points[0];

                for (int i = 1; i < points.Count; i++)
                {
                    if ((currPoint == endPoint)
                        || (MathUtils.VectorOrientation(currPoint, endPoint, points[i]) == -1))
                    {
                        endPoint = points[i];
                    }
                }

                currPoint = endPoint;

            }
            while (endPoint != hullPoints[0]);

            return hullPoints;
        }


        float collisionRigidness = 1.0f;

        public void Update(float deltaTime)
        {
            if (Position!=Vector2.Zero)
            {
                //!!!!!!!!!!!!!!!!
                //UpdateColliding();
            }

            //Vector2 translateAmount = speed * deltaTime;            
            //translateAmount += ConvertUnits.ToDisplayUnits(Position) * collisionRigidness;

            //if (targetPosition != Vector2.Zero && targetPosition != Position)
            //{
            //    float dist = Vector2.Distance(targetPosition, Position);
                
            //    if (dist>1000.0f)
            //    {
            //        sub.SetPosition(targetPosition);
            //        targetPosition = Vector2.Zero;
            //    }
            //    else if (dist>50.0f)
            //    {
            //        translateAmount += (targetPosition - Position) * 0.01f;
            //    }
            //}
            //else
            //{
            //    targetPosition = Vector2.Zero;
            //}

            //sub.Translate(translateAmount);

            //-------------------------

            Vector2 totalForce = CalculateBuoyancy();

            if (body.LinearVelocity.LengthSquared() > 0.000001f)
            {
                float dragCoefficient = 0.01f;

                float speedLength = (body.LinearVelocity == Vector2.Zero) ? 0.0f : body.LinearVelocity.Length();
                float drag = speedLength * speedLength * dragCoefficient * mass;

                totalForce += -Vector2.Normalize(body.LinearVelocity) * drag;                
            }

            ApplyForce(totalForce);

            //UpdateDepthDamage(deltaTime);

            //hullBodies[0].body.LinearVelocity = -hullBodies[0].body.Position;

            //hullBody.SetTransform(Vector2.Zero , 0.0f);
            //body.SetTransform(Vector2.Zero, 0.0f);// .LinearVelocity = -body.Position / (float)Physics.step;
            //body.LinearVelocity = Vector2.Zero;
        }

        private Vector2 CalculateBuoyancy()
        {
            float waterVolume = 0.0f;
            float volume = 0.0f;
            foreach (Hull hull in Hull.hullList)
            {
                waterVolume += hull.Volume;
                volume += hull.FullVolume;
            }

            float waterPercentage = waterVolume / volume;

            float neutralPercentage = 0.07f;

            float buoyancy = Math.Max(neutralPercentage - waterPercentage, -neutralPercentage*2.0f);
            buoyancy *= mass;

            return new Vector2(0.0f, buoyancy*10.0f);
        }

        public void ApplyForce(Vector2 force)
        {
            body.ApplyForce(force);
        }

        public void SetPosition(Vector2 position)
        {
            body.SetTransform(ConvertUnits.ToSimUnits(position), 0.0f);
        }

        private void UpdateDepthDamage(float deltaTime)
        {
            if (Position.Y > DamageDepth) return;

            float depth = DamageDepth - Position.Y;
            depth = Math.Min(depth, 40000.0f);

           // float prevTimer = depthDamageTimer;

            depthDamageTimer -= deltaTime*Math.Min(depth,20000)*PressureDamageMultiplier;

            //if (prevTimer>5.0f && depthDamageTimer<=5.0f)
            //{
            //    SoundPlayer.PlayDamageSound(DamageSoundType.Pressure, 50.0f,);
            //}

            if (depthDamageTimer > 0.0f) return;

            Vector2 damagePos = Vector2.Zero;
            if (Rand.Int(2)==0)
            {
                damagePos = new Vector2(
                    (Rand.Int(2) == 0) ? Borders.X : Borders.X+Borders.Width, 
                    Rand.Range(Borders.Y - Borders.Height, Borders.Y));
            }
            else
            {
                damagePos = new Vector2(
                    Rand.Range(Borders.X, Borders.X + Borders.Width),
                    (Rand.Int(2) == 0) ? Borders.Y : Borders.Y - Borders.Height);
            }

            SoundPlayer.PlayDamageSound(DamageSoundType.Pressure, 50.0f, damagePos, 10000.0f);

            GameMain.GameScreen.Cam.Shake = depth * PressureDamageMultiplier * 0.1f;

            Explosion.RangedStructureDamage(damagePos, depth * PressureDamageMultiplier * 50.0f, depth * PressureDamageMultiplier);
            //SoundPlayer.PlayDamageSound(DamageSoundType.StructureBlunt, Rand.Range(0.0f, 100.0f), damagePos, 5000.0f);
            
            depthDamageTimer = 10.0f;
        }

        //private void UpdateColliding()
        //{

        //    return;

        //    if (body.Position.LengthSquared()<0.00001f) return;

        //    Vector2 normal = Vector2.Normalize(body.Position);
        //    Vector2 simSpeed = ConvertUnits.ToSimUnits(body.LinearVelocity);

        //    float impact = Vector2.Dot(simSpeed, -normal);

        //    if (impact < 0.0f) return;

        //    Vector2 u = Vector2.Dot(simSpeed, -normal) * normal;
        //    Vector2 w = (simSpeed + u);

        //    //speed = ConvertUnits.ToDisplayUnits(w * (1.0f - Friction) + u * Restitution);
            
        //    if (lastContactPoint == null || lastContactCell==null || impact < 3.0f) return;
            
        //    SoundPlayer.PlayDamageSound(DamageSoundType.StructureBlunt, impact * 10.0f, ConvertUnits.ToDisplayUnits((Vector2)lastContactPoint));
        //    GameMain.GameScreen.Cam.Shake = impact * 2.0f;

        //    Vector2 limbForce = -normal * impact*0.5f;

        //    float length = limbForce.Length();
        //    if (length > 10.0f) limbForce = (limbForce / length) * 10.0f;

        //    foreach (Character c in Character.CharacterList)
        //    {
        //        if (c.AnimController.CurrentHull == null) continue;

        //        if (impact > 2.0f) c.AnimController.StunTimer = (impact - 2.0f) * 0.1f;

        //        foreach (Limb limb in c.AnimController.Limbs)
        //        {
        //            if (c.AnimController.LowestLimb == limb) continue;
        //            limb.body.ApplyLinearImpulse(limb.Mass * limbForce);
        //        }
        //    }

        //    Explosion.RangedStructureDamage(ConvertUnits.ToDisplayUnits((Vector2)lastContactPoint), impact*50.0f, impact*DamageMultiplier);

        //    //Body wallBody = Submarine.PickBody(
        //    //    (Vector2)lastContactPoint - body.Position, 
        //    //    (Vector2)lastContactPoint + body.Position * 10.0f, 
        //    //    new List<Body>() { lastContactCell.body });

        //    //if (wallBody == null || wallBody.UserData == null) return;
            
        //    //var damageable = wallBody.UserData as IDamageable;
        //    //Structure structure = wallBody.UserData as Structure;

        //    //if (structure == null) return;
            
        //    //int sectionIndex = structure.FindSectionIndex(ConvertUnits.ToDisplayUnits(Submarine.LastPickedPosition));

        //    //for (int i = sectionIndex - (int)(impact / 5.0f); i < sectionIndex + (int)(impact / 5.0f); i++)
        //    //{
        //    //    structure.AddDamage(i, impact * DamageMultiplier);      
        //    //}            
        //}

        public bool OnCollision(Fixture f1, Fixture f2, Contact contact)
        {


            VoronoiCell cell = f2.Body.UserData as VoronoiCell;
            
            if (cell == null)
            {
                Limb limb = f2.Body.UserData as Limb;
                if (limb!=null && limb.character.Submarine==null)
                {
                    Vector2 normal2;
                    FixedArray2<Vector2> points;
                    contact.GetWorldManifold(out normal2, out points);

                    if (Submarine.PickBody(points[0] - ConvertUnits.ToSimUnits(submarine.Position) + normal2, points[0] - ConvertUnits.ToSimUnits(submarine.Position) - normal2, null, Physics.CollisionWall) != null)
                    {
                        
                        return true;
                    }

                    var ragdoll = limb.character.AnimController;
                    ragdoll.FindHull();                    
              
                    return false;

                }

                return true;
            }

            Vector2 normal;
            FarseerPhysics.Common.FixedArray2<Vector2> worldPoints;
            contact.GetWorldManifold(out normal, out worldPoints);

            Vector2 lastContactPoint = worldPoints[0];

            normal = Vector2.Normalize(ConvertUnits.ToDisplayUnits(body.Position) - cell.Center);

            float impact = Vector2.Dot(Velocity, -normal);

            //Vector2 u = Vector2.Dot(Velocity, -normal) * normal;
            //Vector2 w = (Velocity + u);

            //speed = ConvertUnits.ToDisplayUnits(w * (1.0f - Friction) + u * Restitution);

            if (impact < 3.0f) return true;

            SoundPlayer.PlayDamageSound(DamageSoundType.StructureBlunt, impact * 10.0f, ConvertUnits.ToDisplayUnits(lastContactPoint));
            GameMain.GameScreen.Cam.Shake = impact * 2.0f;

            Vector2 limbForce = -normal * impact * 0.5f;

            float length = limbForce.Length();
            if (length > 10.0f) limbForce = (limbForce / length) * 10.0f;

            foreach (Character c in Character.CharacterList)
            {
                if (c.AnimController.CurrentHull == null) continue;

                if (impact > 2.0f) c.AnimController.StunTimer = (impact - 2.0f) * 0.1f;

                foreach (Limb limb in c.AnimController.Limbs)
                {
                    if (c.AnimController.LowestLimb == limb) continue;
                    limb.body.ApplyLinearImpulse(limb.Mass * limbForce);
                }
            }

            Explosion.RangedStructureDamage(ConvertUnits.ToDisplayUnits(lastContactPoint), impact * 50.0f, impact * DamageMultiplier);

            return true;
        }

    }
}
