using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Collision;
using FarseerPhysics.Common;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using FarseerPhysics.Dynamics.Joints;
using FarseerPhysics.Factories;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Voronoi2;

namespace Barotrauma
{
    partial class SubmarineBody
    {
        public const float NeutralBallastPercentage = 0.07f;

        const float HorizontalDrag = 0.01f;
        const float VerticalDrag = 0.05f;
        const float MaxDrag = 0.1f;

        public const float DamageDepth = -30000.0f;
        private const float ImpactDamageMultiplier = 10.0f;

        //limbs with a mass smaller than this won't cause an impact when they hit the sub
        private const float MinImpactLimbMass = 10.0f;
        //impacts smaller than this are ignored
        private const float MinCollisionImpact = 3.0f;
        //impacts are clamped below this value
        private const float MaxCollisionImpact = 5.0f;
        private const float Friction = 0.2f, Restitution = 0.0f;

        public List<Vector2> HullVertices
        {
            get;
            private set;
        }

        private float depthDamageTimer;

        private readonly Submarine submarine;

        public readonly PhysicsBody Body;

        private List<PosInfo> positionBuffer = new List<PosInfo>();

        public Rectangle Borders
        {
            get;
            private set;
        }
                
        public Vector2 Velocity
        {
            get { return Body.LinearVelocity; }
            set
            {
                if (!MathUtils.IsValid(value)) return;
                Body.LinearVelocity = value;
            }
        }

        public Vector2 Position
        {
            get { return ConvertUnits.ToDisplayUnits(Body.SimPosition); }
        }

        public List<PosInfo> PositionBuffer
        {
            get { return positionBuffer; }
        }
        
        public bool AtDamageDepth
        {
            get { return Position.Y < DamageDepth; }
        }

        public Submarine Submarine
        {
            get { return submarine; }
        }

        public SubmarineBody(Submarine sub, bool showWarningMessages = true)
        {
            this.submarine = sub;

            Body farseerBody = null;
            if (!Hull.hullList.Any())
            {
                farseerBody = BodyFactory.CreateRectangle(GameMain.World, 1.0f, 1.0f, 1.0f);
                if (showWarningMessages)
                {
                    DebugConsole.ThrowError("WARNING: no hulls found, generating a physics body for the submarine failed.");
                }
            }
            else
            {
                List<Vector2> convexHull = GenerateConvexHull();
                for (int i = 0; i < convexHull.Count; i++)
                {
                    convexHull[i] = ConvertUnits.ToSimUnits(convexHull[i]);
                }
                HullVertices = convexHull;

                Vector2 minExtents = Vector2.Zero, maxExtents = Vector2.Zero;

                farseerBody = BodyFactory.CreateBody(GameMain.World, this);
                foreach (Structure wall in Structure.WallList)
                {
                    if (wall.Submarine != submarine) continue;

                    Rectangle rect = wall.Rect;

                    FixtureFactory.AttachRectangle(
                          ConvertUnits.ToSimUnits(wall.BodyWidth),
                          ConvertUnits.ToSimUnits(wall.BodyHeight),
                          50.0f,
                          -wall.BodyRotation,
                          ConvertUnits.ToSimUnits(new Vector2(rect.X + rect.Width / 2, rect.Y - rect.Height / 2) + wall.BodyOffset),
                          farseerBody, this);

                    minExtents.X = Math.Min(rect.X, minExtents.X);
                    minExtents.Y = Math.Min(rect.Y - rect.Height, minExtents.Y);
                    maxExtents.X = Math.Max(rect.Right, maxExtents.X);
                    maxExtents.Y = Math.Max(rect.Y, maxExtents.Y);
                }

                foreach (Hull hull in Hull.hullList)
                {
                    if (hull.Submarine != submarine) continue;

                    Rectangle rect = hull.Rect;
                    FixtureFactory.AttachRectangle(
                        ConvertUnits.ToSimUnits(rect.Width),
                        ConvertUnits.ToSimUnits(rect.Height),
                        100.0f,
                        ConvertUnits.ToSimUnits(new Vector2(rect.X + rect.Width / 2, rect.Y - rect.Height / 2)),
                        farseerBody, this);

                    minExtents.X = Math.Min(rect.X, minExtents.X);
                    minExtents.Y = Math.Min(rect.Y - rect.Height, minExtents.Y);
                    maxExtents.X = Math.Max(rect.Right, maxExtents.X);
                    maxExtents.Y = Math.Max(rect.Y, maxExtents.Y);
                }

                foreach (Item item in Item.ItemList)
                {
                    if (item.StaticBodyConfig == null || item.Submarine != submarine) continue;

                    float radius    = item.StaticBodyConfig.GetAttributeFloat("radius", 0.0f) * item.Scale;
                    float width     = item.StaticBodyConfig.GetAttributeFloat("width", 0.0f) * item.Scale;
                    float height    = item.StaticBodyConfig.GetAttributeFloat("height", 0.0f) * item.Scale;

                    Vector2 simPos  = ConvertUnits.ToSimUnits(item.Position);
                    float simRadius = ConvertUnits.ToSimUnits(radius);
                    float simWidth  = ConvertUnits.ToSimUnits(width);
                    float simHeight = ConvertUnits.ToSimUnits(height);

                    if (width > 0.0f && height > 0.0f)
                    {
                        FixtureFactory.AttachRectangle(simWidth, simHeight, 5.0f, simPos, farseerBody, this).UserData = item;

                        minExtents.X = Math.Min(item.Position.X - width / 2, minExtents.X);
                        minExtents.Y = Math.Min(item.Position.Y - height / 2, minExtents.Y);
                        maxExtents.X = Math.Max(item.Position.X + width / 2, maxExtents.X);
                        maxExtents.Y = Math.Max(item.Position.Y + height / 2, maxExtents.Y);
                    }
                    else if (radius > 0.0f && width > 0.0f)
                    {
                        FixtureFactory.AttachRectangle(simWidth, simRadius * 2, 5.0f, simPos, farseerBody, this).UserData = item;
                        FixtureFactory.AttachCircle(simRadius, 5.0f, farseerBody, simPos - Vector2.UnitX * simWidth / 2, this).UserData = item;
                        FixtureFactory.AttachCircle(simRadius, 5.0f, farseerBody, simPos + Vector2.UnitX * simWidth / 2, this).UserData = item;
                        minExtents.X = Math.Min(item.Position.X - width / 2 - radius, minExtents.X);
                        minExtents.Y = Math.Min(item.Position.Y - radius, minExtents.Y);
                        maxExtents.X = Math.Max(item.Position.X + width / 2 + radius, maxExtents.X);
                        maxExtents.Y = Math.Max(item.Position.Y + radius, maxExtents.Y);
                    }
                    else if (radius > 0.0f && height > 0.0f)
                    {
                        FixtureFactory.AttachRectangle(simRadius * 2, height, 5.0f, simPos, farseerBody, this).UserData = item;
                        FixtureFactory.AttachCircle(simRadius, 5.0f, farseerBody, simPos - Vector2.UnitY * simHeight / 2, this).UserData = item;
                        FixtureFactory.AttachCircle(simRadius, 5.0f, farseerBody, simPos + Vector2.UnitX * simHeight / 2, this).UserData = item;
                        minExtents.X = Math.Min(item.Position.X - radius, minExtents.X);
                        minExtents.Y = Math.Min(item.Position.Y - height / 2 - radius, minExtents.Y);
                        maxExtents.X = Math.Max(item.Position.X + radius, maxExtents.X);
                        maxExtents.Y = Math.Max(item.Position.Y + height / 2 + radius, maxExtents.Y);
                    }
                    else if (radius > 0.0f)
                    {
                        FixtureFactory.AttachCircle(simRadius, 5.0f, farseerBody, simPos, this).UserData = item;
                        minExtents.X = Math.Min(item.Position.X - radius, minExtents.X);
                        minExtents.Y = Math.Min(item.Position.Y - radius, minExtents.Y);
                        maxExtents.X = Math.Max(item.Position.X + radius, maxExtents.X);
                        maxExtents.Y = Math.Max(item.Position.Y + radius, maxExtents.Y);
                    }
                }

                Borders = new Rectangle((int)minExtents.X, (int)maxExtents.Y, (int)(maxExtents.X - minExtents.X), (int)(maxExtents.Y - minExtents.Y));
            }

            farseerBody.BodyType = BodyType.Dynamic;
            farseerBody.CollisionCategories = Physics.CollisionWall;
            farseerBody.CollidesWith = 
                Physics.CollisionItem | 
                Physics.CollisionLevel | 
                Physics.CollisionCharacter | 
                Physics.CollisionProjectile | 
                Physics.CollisionWall;

            farseerBody.Restitution = Restitution;
            farseerBody.Friction = Friction;
            farseerBody.FixedRotation = true;
            farseerBody.Awake = true;
            farseerBody.SleepingAllowed = false;
            farseerBody.IgnoreGravity = true;
            farseerBody.OnCollision += OnCollision;
            farseerBody.UserData = submarine;

            Body = new PhysicsBody(farseerBody);
        }

        private List<Vector2> GenerateConvexHull()
        {
            List<Structure> subWalls = Structure.WallList.FindAll(wall => wall.Submarine == submarine);

            if (subWalls.Count == 0)
            {
                return new List<Vector2> { new Vector2(-1.0f, 1.0f), new Vector2(1.0f, 1.0f), new Vector2(0.0f, -1.0f) };
            }

            List<Vector2> points = new List<Vector2>();

            foreach (Structure wall in subWalls)
            {
                points.Add(new Vector2(wall.Rect.X, wall.Rect.Y));
                points.Add(new Vector2(wall.Rect.X + wall.Rect.Width, wall.Rect.Y));
                points.Add(new Vector2(wall.Rect.X, wall.Rect.Y - wall.Rect.Height));
                points.Add(new Vector2(wall.Rect.X + wall.Rect.Width, wall.Rect.Y - wall.Rect.Height));
            }

            List<Vector2> hullPoints = MathUtils.GiftWrap(points);

            return hullPoints;
        }

        public void Update(float deltaTime)
        {
            if (Body.FarseerBody.IsStatic) { return; }

            ClientUpdatePosition(deltaTime);
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            
            //if outside left or right edge of the level
            if (Position.X < 0 || Position.X > Level.Loaded.Size.X)
            {
                Rectangle worldBorders = Borders;
                worldBorders.Location += MathUtils.ToPoint(Position);

                //push the sub back below the upper "barrier" of the level
                if (worldBorders.Y > Level.Loaded.Size.Y)
                {
                    Body.LinearVelocity = new Vector2(
                        Body.LinearVelocity.X,
                        Math.Min(Body.LinearVelocity.Y, ConvertUnits.ToSimUnits(Level.Loaded.Size.Y - worldBorders.Y)));
                }
                else if (worldBorders.Y - worldBorders.Height < Level.Loaded.BottomPos)
                {
                    Body.LinearVelocity = new Vector2(
                        Body.LinearVelocity.X,
                        Math.Max(Body.LinearVelocity.Y, ConvertUnits.ToSimUnits(Level.Loaded.BottomPos - (worldBorders.Y - worldBorders.Height))));
                }
            }

            //-------------------------

            Vector2 totalForce = CalculateBuoyancy();
            if (Body.LinearVelocity.LengthSquared() > 0.0001f)
            {
                //TODO: sync current drag with clients?
                float attachedMass = 0.0f;
                JointEdge jointEdge = Body.FarseerBody.JointList;
                while (jointEdge != null)
                {
                    Body otherBody = jointEdge.Joint.BodyA == Body.FarseerBody ? jointEdge.Joint.BodyB : jointEdge.Joint.BodyA;
                    Character character = (otherBody.UserData as Limb)?.character;
                    if (character != null) attachedMass += character.Mass;

                    jointEdge = jointEdge.Next;
                }
                
                float horizontalDragCoefficient = MathHelper.Clamp(HorizontalDrag + attachedMass / 5000.0f, 0.0f, MaxDrag);
                totalForce.X -= Math.Sign(Body.LinearVelocity.X) * Body.LinearVelocity.X * Body.LinearVelocity.X * horizontalDragCoefficient * Body.Mass;
                
                float verticalDragCoefficient = MathHelper.Clamp(VerticalDrag + attachedMass / 5000.0f, 0.0f, MaxDrag);
                totalForce.Y -= Math.Sign(Body.LinearVelocity.Y) * Body.LinearVelocity.Y * Body.LinearVelocity.Y * verticalDragCoefficient * Body.Mass;
            }

            ApplyForce(totalForce);

            UpdateDepthDamage(deltaTime);
        }

        partial void ClientUpdatePosition(float deltaTime);

        /// <summary>
        /// Moves away any character that is inside the bounding box of the sub (but not inside the sub)
        /// </summary>
        /// <param name="subTranslation">The translation that was applied to the sub before doing the displacement 
        /// (used for determining where to push the characters)</param>
        private void DisplaceCharacters(Vector2 subTranslation)
        {
            Rectangle worldBorders = Borders;
            worldBorders.Location += MathUtils.ToPoint(ConvertUnits.ToDisplayUnits(Body.SimPosition));

            Vector2 translateDir = Vector2.Normalize(subTranslation);
            if (!MathUtils.IsValid(translateDir)) translateDir = Vector2.UnitY;

            foreach (Character c in Character.CharacterList)
            {
                if (c.AnimController.CurrentHull != null && c.AnimController.CanEnterSubmarine) continue;

                foreach (Limb limb in c.AnimController.Limbs)
                {
                    //if the character isn't inside the bounding box, continue
                    if (!Submarine.RectContains(worldBorders, limb.WorldPosition)) continue;

                    //cast a line from the position of the character to the same direction as the translation of the sub
                    //and see where it intersects with the bounding box
                    if (!MathUtils.GetLineRectangleIntersection(limb.WorldPosition,
                        limb.WorldPosition + translateDir * 100000.0f, worldBorders, out Vector2 intersection))
                    {
                        //should never happen when casting a line out from inside the bounding box
                        Debug.Assert(false);
                        continue;
                    }


                    //"+ translatedir" in order to move the character slightly away from the wall
                    c.AnimController.SetPosition(ConvertUnits.ToSimUnits(c.WorldPosition + (intersection - limb.WorldPosition)) + translateDir);

                    return;
                }

            }
        }

        private Vector2 CalculateBuoyancy()
        {
            float waterVolume = 0.0f;
            float volume = 0.0f;
            foreach (Hull hull in Hull.hullList)
            {
                if (hull.Submarine != submarine) continue;

                waterVolume += hull.WaterVolume;
                volume += hull.Volume;
            }

            float waterPercentage = volume <= 0.0f ? 0.0f : waterVolume / volume;
            
            float buoyancy = NeutralBallastPercentage - waterPercentage;

            if (buoyancy > 0.0f)
                buoyancy *= 2.0f;
            else
                buoyancy = Math.Max(buoyancy, -0.5f);

            return new Vector2(0.0f, buoyancy * Body.Mass * 10.0f);
        }

        public void ApplyForce(Vector2 force)
        {
            Body.ApplyForce(force, maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
        }

        public void SetPosition(Vector2 position)
        {
            Body.SetTransform(ConvertUnits.ToSimUnits(position), 0.0f);
        }

        private void UpdateDepthDamage(float deltaTime)
        {
            if (Position.Y > DamageDepth) return;

            float depth = DamageDepth - Position.Y;

            depthDamageTimer -= deltaTime;

            if (depthDamageTimer > 0.0f) return;

            foreach (Structure wall in Structure.WallList)
            {
                if (wall.Submarine != submarine) continue;

                if (wall.Health < depth * 0.01f)
                {
                    Explosion.RangedStructureDamage(wall.WorldPosition, 100.0f, depth * 0.01f);

                    if (Character.Controlled != null && Character.Controlled.Submarine == submarine)
                    {
                        GameMain.GameScreen.Cam.Shake = Math.Max(GameMain.GameScreen.Cam.Shake, Math.Min(depth * 0.001f, 50.0f));
                    }
                }
            }

            depthDamageTimer = 10.0f;
        }

        public void FlipX()
        {
            List<Vector2> convexHull = GenerateConvexHull();
            for (int i = 0; i < convexHull.Count; i++)
            {
                convexHull[i] = ConvertUnits.ToSimUnits(convexHull[i]);
            }
            HullVertices = convexHull;
        }

        public bool OnCollision(Fixture f1, Fixture f2, Contact contact)
        {
            if (f2.Body.UserData is Limb limb)
            {
                bool collision = CheckCharacterCollision(contact, limb.character);
                if (collision) HandleLimbCollision(contact, limb);
                return collision;
            }
            if (f2.Body.UserData is Character character)
            {
                return CheckCharacterCollision(contact, character);
            }

            contact.GetWorldManifold(out Vector2 normal, out FixedArray2<Vector2> _);
            if (contact.FixtureA.Body == f1.Body)
            {
                normal = -normal;
            }

            if (f2.UserData is VoronoiCell)
            {
                HandleLevelCollision(contact, normal);
                return true;
            }

            if (f2.Body.UserData is Structure)
            {

                HandleLevelCollision(contact, normal);
                return true;
            }

            if (f2.Body.UserData is Submarine otherSub)
            {
                HandleSubCollision(contact, otherSub);
                return true;
            }

            return true;
        }

        private bool CheckCharacterCollision(Contact contact, Character character)
        {
            //characters that can't enter the sub always collide regardless of gaps
            if (!character.AnimController.CanEnterSubmarine) return true;
            if (character.Submarine != null) return false;

            contact.GetWorldManifold(out Vector2 contactNormal, out FixedArray2<Vector2> points);

            Vector2 normalizedVel = character.AnimController.Collider.LinearVelocity == Vector2.Zero ?
                Vector2.Zero : Vector2.Normalize(character.AnimController.Collider.LinearVelocity);

            Vector2 targetPos = ConvertUnits.ToDisplayUnits(points[0] - contactNormal);
            Hull newHull = Hull.FindHull(targetPos, null);

            if (newHull == null)
            {
                targetPos = ConvertUnits.ToDisplayUnits(points[0] + normalizedVel);
                newHull = Hull.FindHull(targetPos, null);
            }

            var gaps = newHull?.ConnectedGaps ?? Gap.GapList.Where(g => g.Submarine == submarine);
            targetPos = character.WorldPosition;
            Gap adjacentGap = Gap.FindAdjacent(gaps, targetPos, 500.0f);
            if (adjacentGap == null) return true;

            if (newHull != null)
            {
                character.AnimController.FindHull(newHull.WorldPosition, true);
            }

            return false;
        }

        private void HandleLimbCollision(Contact contact, Limb limb)
        {
            if (limb.Mass > MinImpactLimbMass)
            {
                Vector2 normal = 
                    Vector2.DistanceSquared(Body.SimPosition, limb.SimPosition) < 0.0001f ?
                    Vector2.UnitY :
                    Vector2.Normalize(Body.SimPosition - limb.SimPosition);

                float impact = Math.Min(Vector2.Dot(Velocity - limb.LinearVelocity, -normal), 50.0f) * Math.Min(limb.Mass / 100.0f, 1);

                ApplyImpact(impact, normal, contact, applyDamage: false);
                foreach (Submarine dockedSub in submarine.DockedTo)
                {
                    dockedSub.SubBody.ApplyImpact(impact, normal, contact, applyDamage: false);
                }
            }

            //find all contacts between the limb and level walls
            List<Contact> levelContacts = new List<Contact>();
            ContactEdge contactEdge = limb.body.FarseerBody.ContactList;
            while (contactEdge.Next != null)
            {
                if (contactEdge.Contact.Enabled &&
                    contactEdge.Other.UserData is VoronoiCell &&
                    contactEdge.Contact.IsTouching)
                {
                    levelContacts.Add(contactEdge.Contact);
                }
                contactEdge = contactEdge.Next;
            }

            if (levelContacts.Count == 0) return;

            //if the limb is in contact with the level, apply an artifical impact to prevent the sub from bouncing on top of it
            //not a very realistic way to handle the collisions (makes it seem as if the characters were made of reinforced concrete),
            //but more realistic than bouncing and prevents using characters as "bumpers" that prevent all collision damage
            Vector2 avgContactNormal = Vector2.Zero;
            foreach (Contact levelContact in levelContacts)
            {
                levelContact.GetWorldManifold(out Vector2 contactNormal, out FixedArray2<Vector2> temp);

                //if the contact normal is pointing from the limb towards the level cell it's touching, flip the normal
                VoronoiCell cell = levelContact.FixtureB.UserData is VoronoiCell ?
                    ((VoronoiCell)levelContact.FixtureB.UserData) : ((VoronoiCell)levelContact.FixtureA.UserData);

                var cellDiff = ConvertUnits.ToDisplayUnits(limb.body.SimPosition) - cell.Center;
                if (Vector2.Dot(contactNormal, cellDiff) < 0)
                {
                    contactNormal = -contactNormal;
                }

                avgContactNormal += contactNormal;

                //apply impacts at the positions where this sub is touching the limb
                ApplyImpact((Vector2.Dot(-Velocity, contactNormal) / 2.0f) / levelContacts.Count, contactNormal, levelContact, applyDamage: false);
            }
            avgContactNormal /= levelContacts.Count;
            
            float contactDot = Vector2.Dot(Body.LinearVelocity, -avgContactNormal);
            if (contactDot > 0.001f)
            {
                Vector2 velChange = Vector2.Normalize(Body.LinearVelocity) * contactDot;
                if (!MathUtils.IsValid(velChange))
                {
                    GameAnalyticsManager.AddErrorEventOnce(
                        "SubmarineBody.HandleLimbCollision:" + submarine.ID,
                        GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                        "Invalid velocity change in SubmarineBody.HandleLimbCollision (submarine velocity: " + Body.LinearVelocity
                        + ", avgContactNormal: " + avgContactNormal
                        + ", contactDot: " + contactDot
                        + ", velChange: " + velChange + ")");
                    return;
                }


                Body.LinearVelocity -= velChange;

                float damageAmount = contactDot * Body.Mass / limb.character.Mass;

                contact.GetWorldManifold(out _, out FixedArray2<Vector2> contactPos);
                limb.character.LastDamageSource = submarine;
                limb.character.DamageLimb(ConvertUnits.ToDisplayUnits(contactPos[0]), limb, 
                    new List<Affliction>() { AfflictionPrefab.InternalDamage.Instantiate(damageAmount) }, 0.0f, true, 0.0f);

                if (limb.character.IsDead)
                {
                    foreach (LimbJoint limbJoint in limb.character.AnimController.LimbJoints)
                    {
                        if (limbJoint.IsSevered || (limbJoint.LimbA != limb && limbJoint.LimbB != limb)) continue;
                        limb.character.AnimController.SeverLimbJoint(limbJoint);
                    }
                }
            }
        }

        private void HandleLevelCollision(Contact contact, Vector2 collisionNormal)
        {
            float wallImpact = Vector2.Dot(Velocity, -collisionNormal);

            ApplyImpact(wallImpact, -collisionNormal, contact);
            foreach (Submarine dockedSub in submarine.DockedTo)
            {
                dockedSub.SubBody.ApplyImpact(wallImpact, -collisionNormal, contact);
            }

#if CLIENT
            contact.GetWorldManifold(out _, out FixedArray2<Vector2> particlePos);
            
            int particleAmount = (int)Math.Min(wallImpact * 10.0f, 50);
            for (int i = 0; i < particleAmount; i++)
            {
                GameMain.ParticleManager.CreateParticle("iceshards",
                    ConvertUnits.ToDisplayUnits(particlePos[0]) + Rand.Vector(Rand.Range(1.0f, 50.0f)),
                    Rand.Vector(Rand.Range(50.0f, 500.0f)) + Velocity);
            }
#endif
        }

        private void HandleSubCollision(Contact contact, Submarine otherSub)
        {
            Debug.Assert(otherSub != submarine);

            contact.GetWorldManifold(out Vector2 normal, out FixedArray2<Vector2> points);
            if (contact.FixtureA.Body == otherSub.SubBody.Body.FarseerBody)
            {
                normal = -normal;
            }

            float thisMass = Body.Mass + submarine.DockedTo.Sum(s => s.PhysicsBody.Mass);
            float otherMass = otherSub.PhysicsBody.Mass + otherSub.DockedTo.Sum(s => s.PhysicsBody.Mass);
            float massRatio = otherMass / (thisMass + otherMass);

            float impact = (Vector2.Dot(Velocity - otherSub.Velocity, normal) / 2.0f) * massRatio;

            //apply impact to this sub (the other sub takes care of this in its own collision callback)
            ApplyImpact(impact, normal, contact);
            foreach (Submarine dockedSub in submarine.DockedTo)
            {
                dockedSub.SubBody.ApplyImpact(impact, normal, contact);
            }

            //find all contacts between this sub and level walls
            List<Contact> levelContacts = new List<Contact>();
            ContactEdge contactEdge = Body.FarseerBody.ContactList;
            while (contactEdge.Next != null)
            {
                if (contactEdge.Contact.Enabled &&
                    contactEdge.Other.UserData is VoronoiCell &&
                    contactEdge.Contact.IsTouching)
                {
                    levelContacts.Add(contactEdge.Contact);
                }

                contactEdge = contactEdge.Next;
            }

            if (levelContacts.Count == 0) return;
            
            //if this sub is in contact with the level, apply artifical impacts
            //to both subs to prevent the other sub from bouncing on top of this one 
            //and to fake the other sub "crushing" this one against a wall
            Vector2 avgContactNormal = Vector2.Zero;
            foreach (Contact levelContact in levelContacts)
            {
                levelContact.GetWorldManifold(out Vector2 contactNormal, out FixedArray2<Vector2> temp);

                //if the contact normal is pointing from the sub towards the level cell we collided with, flip the normal
                VoronoiCell cell = levelContact.FixtureB.UserData is VoronoiCell ? 
                    ((VoronoiCell)levelContact.FixtureB.UserData) : ((VoronoiCell)levelContact.FixtureA.UserData);

                var cellDiff = ConvertUnits.ToDisplayUnits(Body.SimPosition) - cell.Center;
                if (Vector2.Dot(contactNormal, cellDiff) < 0)
                {
                    contactNormal = -contactNormal;
                }

                avgContactNormal += contactNormal;

                //apply impacts at the positions where this sub is touching the level
                ApplyImpact((Vector2.Dot(Velocity - otherSub.Velocity, contactNormal) / 2.0f) * massRatio / levelContacts.Count, contactNormal, levelContact);
            }
            avgContactNormal /= levelContacts.Count;

            //apply an impact to the other sub
            float contactDot = Vector2.Dot(otherSub.PhysicsBody.LinearVelocity, -avgContactNormal);
            if (contactDot > 0.0f)
            {
                if (otherSub.PhysicsBody.LinearVelocity.LengthSquared() > 0.0001f)
                {
                    otherSub.PhysicsBody.LinearVelocity -= Vector2.Normalize(otherSub.PhysicsBody.LinearVelocity) * contactDot;
                }

                impact = Vector2.Dot(otherSub.Velocity, normal);
                otherSub.SubBody.ApplyImpact(impact, normal, contact);
                foreach (Submarine dockedSub in otherSub.DockedTo)
                {
                    dockedSub.SubBody.ApplyImpact(impact, normal, contact);
                }
            }            
        }

        private void ApplyImpact(float impact, Vector2 direction, Contact contact, bool applyDamage = true)
        {
            if (impact < MinCollisionImpact) { return; }

            contact.GetWorldManifold(out Vector2 tempNormal, out FixedArray2<Vector2> worldPoints);
            Vector2 lastContactPoint = worldPoints[0];
            
            Vector2 impulse = direction * impact * 0.5f;            
            impulse = impulse.ClampLength(MaxCollisionImpact);

            if (!MathUtils.IsValid(impulse))
            {
                string errorMsg =
                    "Invalid impulse in SubmarineBody.ApplyImpact: " + impulse +
                    ". Direction: " + direction + ", body position: " + Body.SimPosition + ", impact: " + impact + ".";
                if (GameMain.NetworkMember != null)
                {
                    errorMsg += GameMain.NetworkMember.IsClient ? " Playing as a client." : " Hosting a server.";
                }
                if (GameSettings.VerboseLogging) DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce(
                    "SubmarineBody.ApplyImpact:InvalidImpulse",
                    GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                    errorMsg);
                return;
            }

#if CLIENT
            if (Character.Controlled != null && Character.Controlled.Submarine == submarine)
            {
                GameMain.GameScreen.Cam.Shake = impact * 2.0f;
                float angularVelocity = 
                    (lastContactPoint.X - Body.SimPosition.X) / ConvertUnits.ToSimUnits(submarine.Borders.Width / 2) * impulse.Y 
                    - (lastContactPoint.Y - Body.SimPosition.Y) / ConvertUnits.ToSimUnits(submarine.Borders.Height / 2) * impulse.X;
                GameMain.GameScreen.Cam.AngularVelocity = MathHelper.Clamp(angularVelocity * 0.1f, -1.0f, 1.0f);
            }
#endif

            foreach (Character c in Character.CharacterList)
            {
                if (c.Submarine != submarine) { continue; }
                
                foreach (Limb limb in c.AnimController.Limbs)
                {
                    limb.body.ApplyLinearImpulse(limb.Mass * impulse, 10.0f);
                }
                c.AnimController.Collider.ApplyLinearImpulse(c.AnimController.Collider.Mass * impulse, 10.0f);
                //stun for up to 1 second if the impact is 50% or more of the maximum impact
                if (impact > (MinCollisionImpact + MaxCollisionImpact) / 2.0f)
                {
                    c.SetStun(impulse.Length() * 0.2f);
                }
            }

            foreach (Item item in Item.ItemList)
            {
                if (item.Submarine != submarine || item.CurrentHull == null || 
                    item.body == null || !item.body.Enabled) continue;

                item.body.ApplyLinearImpulse(item.body.Mass * impulse, 10.0f);
            }
            
            var damagedStructures = Explosion.RangedStructureDamage(
                ConvertUnits.ToDisplayUnits(lastContactPoint), 
                impact * 50.0f, 
                applyDamage ? impact * ImpactDamageMultiplier : 0.0f);

#if CLIENT
            //play a damage sound for the structure that took the most damage
            float maxDamage = 0.0f;
            Structure maxDamageStructure = null;
            foreach (KeyValuePair<Structure, float> structureDamage in damagedStructures)
            {
                if (maxDamageStructure == null || structureDamage.Value > maxDamage)
                {
                    maxDamage = structureDamage.Value;
                    maxDamageStructure = structureDamage.Key;
                }
            }

            if (maxDamageStructure != null)
            {
                SoundPlayer.PlayDamageSound(
                    "StructureBlunt",
                    impact * 10.0f,
                    ConvertUnits.ToDisplayUnits(lastContactPoint),
                    MathHelper.Lerp(2000.0f, 10000.0f, (impact - MinCollisionImpact) / 2.0f),
                    maxDamageStructure.Tags);            
            }
#endif
        }

    }
}
