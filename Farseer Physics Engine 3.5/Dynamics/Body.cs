/*
* Farseer Physics Engine:
* Copyright (c) 2012 Ian Qvist
* 
* Original source Box2D:
* Copyright (c) 2006-2011 Erin Catto http://www.box2d.org 
* 
* This software is provided 'as-is', without any express or implied 
* warranty.  In no event will the authors be held liable for any damages 
* arising from the use of this software. 
* Permission is granted to anyone to use this software for any purpose, 
* including commercial applications, and to alter it and redistribute it 
* freely, subject to the following restrictions: 
* 1. The origin of this software must not be misrepresented; you must not 
* claim that you wrote the original software. If you use this software 
* in a product, an acknowledgment in the product documentation would be 
* appreciated but is not required. 
* 2. Altered source versions must be plainly marked as such, and must not be 
* misrepresented as being the original software. 
* 3. This notice may not be removed or altered from any source distribution. 
*/
//#define USE_AWAKE_BODY_SET

using System;
using System.Collections.Generic;
using System.Diagnostics;
using FarseerPhysics.Collision;
using FarseerPhysics.Collision.Shapes;
using FarseerPhysics.Common;
using FarseerPhysics.Common.PhysicsLogic;
using FarseerPhysics.Controllers;
using FarseerPhysics.Dynamics.Contacts;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;

namespace FarseerPhysics.Dynamics
{
    /// <summary>
    /// The body type.
    /// </summary>
    public enum BodyType
    {
        /// <summary>
        /// Zero velocity, may be manually moved. Note: even static bodies have mass.
        /// </summary>
        Static,
        /// <summary>
        /// Zero mass, non-zero velocity set by user, moved by solver
        /// </summary>
        Kinematic,
        /// <summary>
        /// Positive mass, non-zero velocity determined by forces, moved by solver
        /// </summary>
        Dynamic,
    }

    public class Body : IDisposable
    {
        [ThreadStatic]
        private static int _bodyIdCounter;

        private float _angularDamping;
        private BodyType _bodyType;
        private float _inertia;
        private float _linearDamping;
        private float _mass;
        private bool _sleepingAllowed = true;
        private bool _awake = true;
        private bool _fixedRotation;

        internal bool _enabled = true;
        internal float _angularVelocity;
        internal Vector2 _linearVelocity;
        internal Vector2 _force;
        internal float _invI;
        internal float _invMass;
        internal float _sleepTime;
        internal Sweep _sweep; // the swept motion for CCD
        internal float _torque;
        internal World _world;
        internal Transform _xf; // the body origin transform
        internal bool _island;

        public PhysicsLogicFilter PhysicsLogicFilter;
        public ControllerFilter ControllerFilter;

        public Body(World world, Vector2? position = null, float rotation = 0, object userdata = null)
        {
            FixtureList = new List<Fixture>();
            BodyId = _bodyIdCounter++;
            _world = world;

            UserData = userdata;
            GravityScale = 1.0f;
            BodyType = BodyType.Static;
            Enabled = true; //FPE note: Also creates proxies in the broadphase

            _xf.q.Set(rotation);

            if (position.HasValue)
            {
                _xf.p = position.Value;
                _sweep.C0 = _xf.p;
                _sweep.C = _xf.p;
                _sweep.A0 = rotation;
                _sweep.A = rotation;
            }

            world.AddBody(this); //FPE note: bodies can't live without a World
        }

        /// <summary>
        /// A unique id for this body.
        /// </summary>
        public int BodyId { get; private set; }

        public int IslandIndex { get; set; }

        /// <summary>
        /// Scale the gravity applied to this body.
        /// Defaults to 1. A value of 2 means double the gravity is applied to this body.
        /// </summary>
        public float GravityScale { get; set; }

        /// <summary>
        /// Set the user data. Use this to store your application specific data.
        /// </summary>
        /// <value>The user data.</value>
        public object UserData { get; set; }

        /// <summary>
        /// Gets the total number revolutions the body has made.
        /// </summary>
        /// <value>The revolutions.</value>
        public float Revolutions
        {
            get { return Rotation / (float)Math.PI; }
        }

        /// <summary>
        /// Gets or sets the body type.
        /// Warning: Calling this mid-update might cause a crash.
        /// </summary>
        /// <value>The type of body.</value>
        public BodyType BodyType
        {
            get { return _bodyType; }
            set
            {
                if (_bodyType == value)
                    return;

                _bodyType = value;

                ResetMassData();

                if (_bodyType == BodyType.Static)
                {
                    _linearVelocity = Vector2.Zero;
                    _angularVelocity = 0.0f;
                    _sweep.A0 = _sweep.A;
                    _sweep.C0 = _sweep.C;
                    SynchronizeFixtures();
                }

                Awake = true;

                _force = Vector2.Zero;
                _torque = 0.0f;

                // Delete the attached contacts.
                ContactEdge ce = ContactList;
                while (ce != null)
                {
                    ContactEdge ce0 = ce;
                    ce = ce.Next;
                    _world.ContactManager.Destroy(ce0.Contact);
                }

                ContactList = null;

                // Touch the proxies so that new contacts will be created (when appropriate)
                IBroadPhase broadPhase = _world.ContactManager.BroadPhase;
                foreach (Fixture fixture in FixtureList)
                {
                    int proxyCount = fixture.ProxyCount;
                    for (int j = 0; j < proxyCount; j++)
                    {
                        broadPhase.TouchProxy(fixture.Proxies[j].ProxyId);
                    }
                }
            }
        }

        /// <summary>
        /// Get or sets the linear velocity of the center of mass.
        /// </summary>
        /// <value>The linear velocity.</value>
        public Vector2 LinearVelocity
        {
            set
            {
                Debug.Assert(!float.IsNaN(value.X) && !float.IsNaN(value.Y));

                if (_bodyType == BodyType.Static)
                    return;

                if (Vector2.Dot(value, value) > 0.0f)
                    Awake = true;

                _linearVelocity = value;
            }
            get { return _linearVelocity; }
        }

        /// <summary>
        /// Gets or sets the angular velocity. Radians/second.
        /// </summary>
        /// <value>The angular velocity.</value>
        public float AngularVelocity
        {
            set
            {
                Debug.Assert(!float.IsNaN(value));

                if (_bodyType == BodyType.Static)
                    return;

                if (value * value > 0.0f)
                    Awake = true;

                _angularVelocity = value;
            }
            get { return _angularVelocity; }
        }

        /// <summary>
        /// Gets or sets the linear damping.
        /// </summary>
        /// <value>The linear damping.</value>
        public float LinearDamping
        {
            get { return _linearDamping; }
            set
            {
                Debug.Assert(!float.IsNaN(value));

                _linearDamping = value;
            }
        }

        /// <summary>
        /// Gets or sets the angular damping.
        /// </summary>
        /// <value>The angular damping.</value>
        public float AngularDamping
        {
            get { return _angularDamping; }
            set
            {
                Debug.Assert(!float.IsNaN(value));

                _angularDamping = value;
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this body should be included in the CCD solver.
        /// </summary>
        /// <value><c>true</c> if this instance is included in CCD; otherwise, <c>false</c>.</value>
        public bool IsBullet { get; set; }

        /// <summary>
        /// You can disable sleeping on this body. If you disable sleeping, the
        /// body will be woken.
        /// </summary>
        /// <value><c>true</c> if sleeping is allowed; otherwise, <c>false</c>.</value>
        public bool SleepingAllowed
        {
            set
            {
                if (!value)
                    Awake = true;

                _sleepingAllowed = value;
            }
            get { return _sleepingAllowed; }
        }

        /// <summary>
        /// Set the sleep state of the body. A sleeping body has very
        /// low CPU cost.
        /// </summary>
        /// <value><c>true</c> if awake; otherwise, <c>false</c>.</value>
        public bool Awake
        {
            set
            {
                if (value)
                {
                    if (!_awake)
                    {
                        _sleepTime = 0.0f;
                        _world.ContactManager.UpdateContacts(ContactList, true);
#if USE_AWAKE_BODY_SET
						if (InWorld && !World.AwakeBodySet.Contains(this))
						{
							World.AwakeBodySet.Add(this);
						}
#endif
                    }
                }
                else
                {
#if USE_AWAKE_BODY_SET
					// Check even for BodyType.Static because if this body had just been changed to Static it will have
					// set Awake = false in the process.
					if (InWorld && World.AwakeBodySet.Contains(this))
					{
						World.AwakeBodySet.Remove(this);
					}
#endif
                    ResetDynamics();
                    _sleepTime = 0.0f;
                    _world.ContactManager.UpdateContacts(ContactList, false);
                }

                _awake = value;
            }
            get { return _awake; }
        }

        /// <summary>
        /// Set the active state of the body. An inactive body is not
        /// simulated and cannot be collided with or woken up.
        /// If you pass a flag of true, all fixtures will be added to the
        /// broad-phase.
        /// If you pass a flag of false, all fixtures will be removed from
        /// the broad-phase and all contacts will be destroyed.
        /// Fixtures and joints are otherwise unaffected. You may continue
        /// to create/destroy fixtures and joints on inactive bodies.
        /// Fixtures on an inactive body are implicitly inactive and will
        /// not participate in collisions, ray-casts, or queries.
        /// Joints connected to an inactive body are implicitly inactive.
        /// An inactive body is still owned by a b2World object and remains
        /// in the body list.
        /// </summary>
        /// <value><c>true</c> if active; otherwise, <c>false</c>.</value>
        public bool Enabled
        {
            set
            {
                if (value == _enabled)
                    return;

                if (value)
                {
                    // Create all proxies.
                    IBroadPhase broadPhase = _world.ContactManager.BroadPhase;
                    for (int i = 0; i < FixtureList.Count; i++)
                    {
                        FixtureList[i].CreateProxies(broadPhase, ref _xf);
                    }

                    // Contacts are created the next time step.
                }
                else
                {
                    // Destroy all proxies.
                    IBroadPhase broadPhase = _world.ContactManager.BroadPhase;

                    for (int i = 0; i < FixtureList.Count; i++)
                    {
                        FixtureList[i].DestroyProxies(broadPhase);
                    }

                    // Destroy the attached contacts.
                    ContactEdge ce = ContactList;
                    while (ce != null)
                    {
                        ContactEdge ce0 = ce;
                        ce = ce.Next;
                        _world.ContactManager.Destroy(ce0.Contact);
                    }
                    ContactList = null;
                }

                _enabled = value;
            }
            get { return _enabled; }
        }

        /// <summary>
        /// Set this body to have fixed rotation. This causes the mass
        /// to be reset.
        /// </summary>
        /// <value><c>true</c> if it has fixed rotation; otherwise, <c>false</c>.</value>
        public bool FixedRotation
        {
            set
            {
                if (_fixedRotation == value)
                    return;

                _fixedRotation = value;

                _angularVelocity = 0f;
                ResetMassData();
            }
            get { return _fixedRotation; }
        }

        /// <summary>
        /// Gets all the fixtures attached to this body.
        /// </summary>
        /// <value>The fixture list.</value>
        public List<Fixture> FixtureList { get; internal set; }

        /// <summary>
        /// Get the list of all joints attached to this body.
        /// </summary>
        /// <value>The joint list.</value>
        public JointEdge JointList { get; internal set; }

        /// <summary>
        /// Get the list of all contacts attached to this body.
        /// Warning: this list changes during the time step and you may
        /// miss some collisions if you don't use ContactListener.
        /// </summary>
        /// <value>The contact list.</value>
        public ContactEdge ContactList { get; internal set; }

        /// <summary>
        /// Get the world body origin position.
        /// </summary>
        /// <returns>Return the world position of the body's origin.</returns>
        public Vector2 Position
        {
            get { return _xf.p; }
            set
            {
                Debug.Assert(!float.IsNaN(value.X) && !float.IsNaN(value.Y));

                SetTransform(ref value, Rotation);
            }
        }

        /// <summary>
        /// Get the angle in radians.
        /// </summary>
        /// <returns>Return the current world rotation angle in radians.</returns>
        public float Rotation
        {
            get { return _sweep.A; }
            set
            {
                Debug.Assert(!float.IsNaN(value));

                SetTransform(ref _xf.p, value);
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this body is static.
        /// </summary>
        /// <value><c>true</c> if this instance is static; otherwise, <c>false</c>.</value>
        public bool IsStatic
        {
            get { return _bodyType == BodyType.Static; }
            set { BodyType = value ? BodyType.Static : BodyType.Dynamic; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this body is kinematic.
        /// </summary>
        /// <value><c>true</c> if this instance is kinematic; otherwise, <c>false</c>.</value>
        public bool IsKinematic
        {
            get { return _bodyType == BodyType.Kinematic; }
            set { BodyType = value ? BodyType.Kinematic : BodyType.Dynamic; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this body ignores gravity.
        /// </summary>
        /// <value><c>true</c> if  it ignores gravity; otherwise, <c>false</c>.</value>
        public bool IgnoreGravity { get; set; }

        /// <summary>
        /// Get the world position of the center of mass.
        /// </summary>
        /// <value>The world position.</value>
        public Vector2 WorldCenter
        {
            get { return _sweep.C; }
        }

        /// <summary>
        /// Get the local position of the center of mass.
        /// </summary>
        /// <value>The local position.</value>
        public Vector2 LocalCenter
        {
            get { return _sweep.LocalCenter; }
            set
            {
                if (_bodyType != BodyType.Dynamic)
                    return;

                // Move center of mass.
                Vector2 oldCenter = _sweep.C;
                _sweep.LocalCenter = value;
                _sweep.C0 = _sweep.C = MathUtils.Mul(ref _xf, ref _sweep.LocalCenter);

                // Update center of mass velocity.
                Vector2 a = _sweep.C - oldCenter;
                _linearVelocity += new Vector2(-_angularVelocity * a.Y, _angularVelocity * a.X);
            }
        }

        /// <summary>
        /// Gets or sets the mass. Usually in kilograms (kg).
        /// </summary>
        /// <value>The mass.</value>
        public float Mass
        {
            get { return _mass; }
            set
            {
                Debug.Assert(!float.IsNaN(value));

                if (_bodyType != BodyType.Dynamic) //Make an assert
                    return;

                _mass = value;

                if (_mass <= 0.0f)
                    _mass = 1.0f;

                _invMass = 1.0f / _mass;
            }
        }

        /// <summary>
        /// Get or set the rotational inertia of the body about the local origin. usually in kg-m^2.
        /// </summary>
        /// <value>The inertia.</value>
        public float Inertia
        {
            get { return _inertia + Mass * Vector2.Dot(_sweep.LocalCenter, _sweep.LocalCenter); }
            set
            {
                Debug.Assert(!float.IsNaN(value));

                if (_bodyType != BodyType.Dynamic) //Make an assert
                    return;

                if (value > 0.0f && !_fixedRotation) //Make an assert
                {
                    _inertia = value - Mass * Vector2.Dot(LocalCenter, LocalCenter);
                    Debug.Assert(_inertia > 0.0f);
                    _invI = 1.0f / _inertia;
                }
            }
        }

        public float Restitution
        {
            get
            {
                float res = 0;

                for (int i = 0; i < FixtureList.Count; i++)
                {
                    Fixture f = FixtureList[i];
                    res += f.Restitution;
                }

                return FixtureList.Count > 0 ? res / FixtureList.Count : 0;
            }
            set
            {
                for (int i = 0; i < FixtureList.Count; i++)
                {
                    Fixture f = FixtureList[i];
                    f.Restitution = value;
                }
            }
        }

        public float Friction
        {
            get
            {
                float res = 0;

                for (int i = 0; i < FixtureList.Count; i++)
                {
                    Fixture f = FixtureList[i];
                    res += f.Friction;
                }

                return FixtureList.Count > 0 ? res / FixtureList.Count : 0;
            }
            set
            {
                for (int i = 0; i < FixtureList.Count; i++)
                {
                    Fixture f = FixtureList[i];
                    f.Friction = value;
                }
            }
        }

        public Category CollisionCategories
        {
            set
            {
                for (int i = 0; i < FixtureList.Count; i++)
                {
                    Fixture f = FixtureList[i];
                    f.CollisionCategories = value;
                }
            }
        }

        public Category CollidesWith
        {
            set
            {
                for (int i = 0; i < FixtureList.Count; i++)
                {
                    Fixture f = FixtureList[i];
                    f.CollidesWith = value;
                }
            }
        }

        /// <summary>
        /// Body objects can define which categories of bodies they wish to ignore CCD with. 
        /// This allows certain bodies to be configured to ignore CCD with objects that
        /// aren't a penetration problem due to the way content has been prepared.
        /// This is compared against the other Body's fixture CollisionCategories within World.SolveTOI().
        /// </summary>
        public Category IgnoreCCDWith
        {
            set
            {
                for (int i = 0; i < FixtureList.Count; i++)
                {
                    Fixture f = FixtureList[i];
                    f.IgnoreCCDWith = value;
                }
            }
        }

        public short CollisionGroup
        {
            set
            {
                for (int i = 0; i < FixtureList.Count; i++)
                {
                    Fixture f = FixtureList[i];
                    f.CollisionGroup = value;
                }
            }
        }

        public bool IsSensor
        {
            set
            {
                for (int i = 0; i < FixtureList.Count; i++)
                {
                    Fixture f = FixtureList[i];
                    f.IsSensor = value;
                }
            }
        }

        public bool IgnoreCCD { get; set; }

        /// <summary>
        /// Resets the dynamics of this body.
        /// Sets torque, force and linear/angular velocity to 0
        /// </summary>
        public void ResetDynamics()
        {
            _torque = 0;
            _angularVelocity = 0;
            _force = Vector2.Zero;
            _linearVelocity = Vector2.Zero;
        }

        /// <summary>
        /// Creates a fixture and attach it to this body.
        /// If the density is non-zero, this function automatically updates the mass of the body.
        /// Contacts are not created until the next time step.
        /// Warning: This function is locked during callbacks.
        /// </summary>
        /// <param name="shape">The shape.</param>
        /// <param name="userData">Application specific data</param>
        /// <returns></returns>
        public Fixture CreateFixture(Shape shape, object userData = null)
        {
            return new Fixture(this, shape, userData);
        }

        /// <summary>
        /// Destroy a fixture. This removes the fixture from the broad-phase and
        /// destroys all contacts associated with this fixture. This will
        /// automatically adjust the mass of the body if the body is dynamic and the
        /// fixture has positive density.
        /// All fixtures attached to a body are implicitly destroyed when the body is destroyed.
        /// Warning: This function is locked during callbacks.
        /// </summary>
        /// <param name="fixture">The fixture to be removed.</param>
        public void DestroyFixture(Fixture fixture)
        {
            Debug.Assert(fixture.Body == this);

            // Remove the fixture from this body's singly linked list.
            Debug.Assert(FixtureList.Count > 0);

            // You tried to remove a fixture that not present in the fixturelist.
            Debug.Assert(FixtureList.Contains(fixture));

            // Destroy any contacts associated with the fixture.
            ContactEdge edge = ContactList;
            while (edge != null)
            {
                Contact c = edge.Contact;
                edge = edge.Next;

                Fixture fixtureA = c.FixtureA;
                Fixture fixtureB = c.FixtureB;

                if (fixture == fixtureA || fixture == fixtureB)
                {
                    // This destroys the contact and removes it from
                    // this body's contact list.
                    _world.ContactManager.Destroy(c);
                }
            }

            if (_enabled)
            {
                IBroadPhase broadPhase = _world.ContactManager.BroadPhase;
                fixture.DestroyProxies(broadPhase);
            }

            FixtureList.Remove(fixture);
            fixture.Destroy();
            fixture.Body = null;

            ResetMassData();
        }

        /// <summary>
        /// Set the position of the body's origin and rotation.
        /// This breaks any contacts and wakes the other bodies.
        /// Manipulating a body's transform may cause non-physical behavior.
        /// </summary>
        /// <param name="position">The world position of the body's local origin.</param>
        /// <param name="rotation">The world rotation in radians.</param>
        public void SetTransform(ref Vector2 position, float rotation)
        {
            SetTransformIgnoreContacts(ref position, rotation);

            _world.ContactManager.FindNewContacts();
        }

        /// <summary>
        /// Set the position of the body's origin and rotation.
        /// This breaks any contacts and wakes the other bodies.
        /// Manipulating a body's transform may cause non-physical behavior.
        /// </summary>
        /// <param name="position">The world position of the body's local origin.</param>
        /// <param name="rotation">The world rotation in radians.</param>
        public void SetTransform(Vector2 position, float rotation)
        {
            SetTransform(ref position, rotation);
        }

        /// <summary>
        /// For teleporting a body without considering new contacts immediately.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <param name="angle">The angle.</param>
        public void SetTransformIgnoreContacts(ref Vector2 position, float angle)
        {
            _xf.q.Set(angle);
            _xf.p = position;

            _sweep.C = MathUtils.Mul(ref _xf, _sweep.LocalCenter);
            _sweep.A = angle;

            _sweep.C0 = _sweep.C;
            _sweep.A0 = angle;

            IBroadPhase broadPhase = _world.ContactManager.BroadPhase;
            for (int i = 0; i < FixtureList.Count; i++)
            {
                FixtureList[i].Synchronize(broadPhase, ref _xf, ref _xf);
            }
        }

        /// <summary>
        /// Get the body transform for the body's origin.
        /// </summary>
        /// <param name="transform">The transform of the body's origin.</param>
        public void GetTransform(out Transform transform)
        {
            transform = _xf;
        }

        /// <summary>
        /// Apply a force at a world point. If the force is not
        /// applied at the center of mass, it will generate a torque and
        /// affect the angular velocity. This wakes up the body.
        /// </summary>
        /// <param name="force">The world force vector, usually in Newtons (N).</param>
        /// <param name="point">The world position of the point of application.</param>
        public void ApplyForce(Vector2 force, Vector2 point)
        {
            ApplyForce(ref force, ref point);
        }

        /// <summary>
        /// Applies a force at the center of mass.
        /// </summary>
        /// <param name="force">The force.</param>
        public void ApplyForce(ref Vector2 force)
        {
            ApplyForce(ref force, ref _xf.p);
        }

        /// <summary>
        /// Applies a force at the center of mass.
        /// </summary>
        /// <param name="force">The force.</param>
        public void ApplyForce(Vector2 force)
        {
            ApplyForce(ref force, ref _xf.p);
        }

        /// <summary>
        /// Apply a force at a world point. If the force is not
        /// applied at the center of mass, it will generate a torque and
        /// affect the angular velocity. This wakes up the body.
        /// </summary>
        /// <param name="force">The world force vector, usually in Newtons (N).</param>
        /// <param name="point">The world position of the point of application.</param>
        public void ApplyForce(ref Vector2 force, ref Vector2 point)
        {
            Debug.Assert(!float.IsNaN(force.X));
            Debug.Assert(!float.IsNaN(force.Y));
            Debug.Assert(!float.IsNaN(point.X));
            Debug.Assert(!float.IsNaN(point.Y));

            if (_bodyType == BodyType.Dynamic)
            {
                if (Awake == false)
                {
                    Awake = true;
                }

                _force += force;
                _torque += (point.X - _sweep.C.X) * force.Y - (point.Y - _sweep.C.Y) * force.X;
            }
        }

        /// <summary>
        /// Apply a torque. This affects the angular velocity
        /// without affecting the linear velocity of the center of mass.
        /// This wakes up the body.
        /// </summary>
        /// <param name="torque">The torque about the z-axis (out of the screen), usually in N-m.</param>
        public void ApplyTorque(float torque)
        {
            Debug.Assert(!float.IsNaN(torque));

            if (_bodyType == BodyType.Dynamic)
            {
                if (Awake == false)
                {
                    Awake = true;
                }

                _torque += torque;
            }
        }

        /// <summary>
        /// Apply an impulse at a point. This immediately modifies the velocity.
        /// This wakes up the body.
        /// </summary>
        /// <param name="impulse">The world impulse vector, usually in N-seconds or kg-m/s.</param>
        public void ApplyLinearImpulse(Vector2 impulse)
        {
            ApplyLinearImpulse(ref impulse);
        }

        /// <summary>
        /// Apply an impulse at a point. This immediately modifies the velocity.
        /// It also modifies the angular velocity if the point of application
        /// is not at the center of mass.
        /// This wakes up the body.
        /// </summary>
        /// <param name="impulse">The world impulse vector, usually in N-seconds or kg-m/s.</param>
        /// <param name="point">The world position of the point of application.</param>
        public void ApplyLinearImpulse(Vector2 impulse, Vector2 point)
        {
            ApplyLinearImpulse(ref impulse, ref point);
        }

        /// <summary>
        /// Apply an impulse at a point. This immediately modifies the velocity.
        /// This wakes up the body.
        /// </summary>
        /// <param name="impulse">The world impulse vector, usually in N-seconds or kg-m/s.</param>
        public void ApplyLinearImpulse(ref Vector2 impulse)
        {
            if (_bodyType != BodyType.Dynamic)
            {
                return;
            }
            if (Awake == false)
            {
                Awake = true;
            }
            _linearVelocity += _invMass * impulse;
        }

        /// <summary>
        /// Apply an impulse at a point. This immediately modifies the velocity.
        /// It also modifies the angular velocity if the point of application
        /// is not at the center of mass.
        /// This wakes up the body.
        /// </summary>
        /// <param name="impulse">The world impulse vector, usually in N-seconds or kg-m/s.</param>
        /// <param name="point">The world position of the point of application.</param>
        public void ApplyLinearImpulse(ref Vector2 impulse, ref Vector2 point)
        {
            if (_bodyType != BodyType.Dynamic)
                return;

            if (Awake == false)
                Awake = true;

            _linearVelocity += _invMass * impulse;
            _angularVelocity += _invI * ((point.X - _sweep.C.X) * impulse.Y - (point.Y - _sweep.C.Y) * impulse.X);
        }

        /// <summary>
        /// Apply an angular impulse.
        /// </summary>
        /// <param name="impulse">The angular impulse in units of kg*m*m/s.</param>
        public void ApplyAngularImpulse(float impulse)
        {
            if (_bodyType != BodyType.Dynamic)
            {
                return;
            }

            if (Awake == false)
            {
                Awake = true;
            }

            _angularVelocity += _invI * impulse;
        }

        /// <summary>
        /// This resets the mass properties to the sum of the mass properties of the fixtures.
        /// This normally does not need to be called unless you called SetMassData to override
        /// the mass and you later want to reset the mass.
        /// </summary>
        public void ResetMassData()
        {
            // Compute mass data from shapes. Each shape has its own density.
            _mass = 0.0f;
            _invMass = 0.0f;
            _inertia = 0.0f;
            _invI = 0.0f;
            _sweep.LocalCenter = Vector2.Zero;

            // Kinematic bodies have zero mass.
            if (BodyType == BodyType.Kinematic)
            {
                _sweep.C0 = _xf.p;
                _sweep.C = _xf.p;
                _sweep.A0 = _sweep.A;
                return;
            }

            Debug.Assert(BodyType == BodyType.Dynamic || BodyType == BodyType.Static);

            // Accumulate mass over all fixtures.
            Vector2 localCenter = Vector2.Zero;
            foreach (Fixture f in FixtureList)
            {
                if (f.Shape._density == 0)
                {
                    continue;
                }

                MassData massData = f.Shape.MassData;
                _mass += massData.Mass;
                localCenter += massData.Mass * massData.Centroid;
                _inertia += massData.Inertia;
            }

            //Static bodies only have mass, they don't have other properties. A little hacky tho...
            if (BodyType == BodyType.Static)
            {
                _sweep.C0 = _sweep.C = _xf.p;
                return;
            }

            // Compute center of mass.
            if (_mass > 0.0f)
            {
                _invMass = 1.0f / _mass;
                localCenter *= _invMass;
            }
            else
            {
                // Force all dynamic bodies to have a positive mass.
                _mass = 1.0f;
                _invMass = 1.0f;
            }

            if (_inertia > 0.0f && !_fixedRotation)
            {
                // Center the inertia about the center of mass.
                _inertia -= _mass * Vector2.Dot(localCenter, localCenter);

                Debug.Assert(_inertia > 0.0f);
                _invI = 1.0f / _inertia;
            }
            else
            {
                _inertia = 0.0f;
                _invI = 0.0f;
            }

            // Move center of mass.
            Vector2 oldCenter = _sweep.C;
            _sweep.LocalCenter = localCenter;
            _sweep.C0 = _sweep.C = MathUtils.Mul(ref _xf, ref _sweep.LocalCenter);

            // Update center of mass velocity.
            Vector2 a = _sweep.C - oldCenter;
            _linearVelocity += new Vector2(-_angularVelocity * a.Y, _angularVelocity * a.X);
        }

        /// <summary>
        /// Get the world coordinates of a point given the local coordinates.
        /// </summary>
        /// <param name="localPoint">A point on the body measured relative the the body's origin.</param>
        /// <returns>The same point expressed in world coordinates.</returns>
        public Vector2 GetWorldPoint(ref Vector2 localPoint)
        {
            return MathUtils.Mul(ref _xf, ref localPoint);
        }

        /// <summary>
        /// Get the world coordinates of a point given the local coordinates.
        /// </summary>
        /// <param name="localPoint">A point on the body measured relative the the body's origin.</param>
        /// <returns>The same point expressed in world coordinates.</returns>
        public Vector2 GetWorldPoint(Vector2 localPoint)
        {
            return GetWorldPoint(ref localPoint);
        }

        /// <summary>
        /// Get the world coordinates of a vector given the local coordinates.
        /// Note that the vector only takes the rotation into account, not the position.
        /// </summary>
        /// <param name="localVector">A vector fixed in the body.</param>
        /// <returns>The same vector expressed in world coordinates.</returns>
        public Vector2 GetWorldVector(ref Vector2 localVector)
        {
            return MathUtils.Mul(_xf.q, localVector);
        }

        /// <summary>
        /// Get the world coordinates of a vector given the local coordinates.
        /// </summary>
        /// <param name="localVector">A vector fixed in the body.</param>
        /// <returns>The same vector expressed in world coordinates.</returns>
        public Vector2 GetWorldVector(Vector2 localVector)
        {
            return GetWorldVector(ref localVector);
        }

        /// <summary>
        /// Gets a local point relative to the body's origin given a world point.
        /// Note that the vector only takes the rotation into account, not the position.
        /// </summary>
        /// <param name="worldPoint">A point in world coordinates.</param>
        /// <returns>The corresponding local point relative to the body's origin.</returns>
        public Vector2 GetLocalPoint(ref Vector2 worldPoint)
        {
            return MathUtils.MulT(ref _xf, worldPoint);
        }

        /// <summary>
        /// Gets a local point relative to the body's origin given a world point.
        /// </summary>
        /// <param name="worldPoint">A point in world coordinates.</param>
        /// <returns>The corresponding local point relative to the body's origin.</returns>
        public Vector2 GetLocalPoint(Vector2 worldPoint)
        {
            return GetLocalPoint(ref worldPoint);
        }

        /// <summary>
        /// Gets a local vector given a world vector.
        /// Note that the vector only takes the rotation into account, not the position.
        /// </summary>
        /// <param name="worldVector">A vector in world coordinates.</param>
        /// <returns>The corresponding local vector.</returns>
        public Vector2 GetLocalVector(ref Vector2 worldVector)
        {
            return MathUtils.MulT(_xf.q, worldVector);
        }

        /// <summary>
        /// Gets a local vector given a world vector.
        /// Note that the vector only takes the rotation into account, not the position.
        /// </summary>
        /// <param name="worldVector">A vector in world coordinates.</param>
        /// <returns>The corresponding local vector.</returns>
        public Vector2 GetLocalVector(Vector2 worldVector)
        {
            return GetLocalVector(ref worldVector);
        }

        /// <summary>
        /// Get the world linear velocity of a world point attached to this body.
        /// </summary>
        /// <param name="worldPoint">A point in world coordinates.</param>
        /// <returns>The world velocity of a point.</returns>
        public Vector2 GetLinearVelocityFromWorldPoint(Vector2 worldPoint)
        {
            return GetLinearVelocityFromWorldPoint(ref worldPoint);
        }

        /// <summary>
        /// Get the world linear velocity of a world point attached to this body.
        /// </summary>
        /// <param name="worldPoint">A point in world coordinates.</param>
        /// <returns>The world velocity of a point.</returns>
        public Vector2 GetLinearVelocityFromWorldPoint(ref Vector2 worldPoint)
        {
            return _linearVelocity +
                   new Vector2(-_angularVelocity * (worldPoint.Y - _sweep.C.Y),
                               _angularVelocity * (worldPoint.X - _sweep.C.X));
        }

        /// <summary>
        /// Get the world velocity of a local point.
        /// </summary>
        /// <param name="localPoint">A point in local coordinates.</param>
        /// <returns>The world velocity of a point.</returns>
        public Vector2 GetLinearVelocityFromLocalPoint(Vector2 localPoint)
        {
            return GetLinearVelocityFromLocalPoint(ref localPoint);
        }

        /// <summary>
        /// Get the world velocity of a local point.
        /// </summary>
        /// <param name="localPoint">A point in local coordinates.</param>
        /// <returns>The world velocity of a point.</returns>
        public Vector2 GetLinearVelocityFromLocalPoint(ref Vector2 localPoint)
        {
            return GetLinearVelocityFromWorldPoint(GetWorldPoint(ref localPoint));
        }

        internal void SynchronizeFixtures()
        {
            Transform xf1 = new Transform();
            xf1.q.Set(_sweep.A0);
            xf1.p = _sweep.C0 - MathUtils.Mul(xf1.q, _sweep.LocalCenter);

            IBroadPhase broadPhase = _world.ContactManager.BroadPhase;
            for (int i = 0; i < FixtureList.Count; i++)
            {
                FixtureList[i].Synchronize(broadPhase, ref xf1, ref _xf);
            }
        }

        internal void SynchronizeTransform()
        {
            _xf.q.Set(_sweep.A);
            _xf.p = _sweep.C - MathUtils.Mul(_xf.q, _sweep.LocalCenter);
        }

        /// <summary>
        /// This is used to prevent connected bodies from colliding.
        /// It may lie, depending on the collideConnected flag.
        /// </summary>
        /// <param name="other">The other body.</param>
        /// <returns></returns>
        internal bool ShouldCollide(Body other)
        {
            // At least one body should be dynamic.
            if (_bodyType != BodyType.Dynamic && other._bodyType != BodyType.Dynamic)
            {
                return false;
            }

            // Does a joint prevent collision?
            for (JointEdge jn = JointList; jn != null; jn = jn.Next)
            {
                if (jn.Other == other)
                {
                    if (jn.Joint.CollideConnected == false)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        internal void Advance(float alpha)
        {
            // Advance to the new safe time. This doesn't sync the broad-phase.
            _sweep.Advance(alpha);
            _sweep.C = _sweep.C0;
            _sweep.A = _sweep.A0;
            _xf.q.Set(_sweep.A);
            _xf.p = _sweep.C - MathUtils.Mul(_xf.q, _sweep.LocalCenter);
        }

        public event OnCollisionEventHandler OnCollision
        {
            add
            {
                for (int i = 0; i < FixtureList.Count; i++)
                {
                    FixtureList[i].OnCollision += value;
                }
            }
            remove
            {
                for (int i = 0; i < FixtureList.Count; i++)
                {
                    FixtureList[i].OnCollision -= value;
                }
            }
        }

        public event OnSeparationEventHandler OnSeparation
        {
            add
            {
                for (int i = 0; i < FixtureList.Count; i++)
                {
                    FixtureList[i].OnSeparation += value;
                }
            }
            remove
            {
                for (int i = 0; i < FixtureList.Count; i++)
                {
                    FixtureList[i].OnSeparation -= value;
                }
            }
        }

        public void IgnoreCollisionWith(Body other)
        {
            for (int i = 0; i < FixtureList.Count; i++)
            {
                for (int j = 0; j < other.FixtureList.Count; j++)
                {
                    FixtureList[i].IgnoreCollisionWith(other.FixtureList[j]);
                }
            }
        }

        public void RestoreCollisionWith(Body other)
        {
            for (int i = 0; i < FixtureList.Count; i++)
            {
                for (int j = 0; j < other.FixtureList.Count; j++)
                {
                    FixtureList[i].RestoreCollisionWith(other.FixtureList[j]);
                }
            }
        }

        #region IDisposable Members

        public bool IsDisposed { get; set; }

        public void Dispose()
        {
            if (!IsDisposed)
            {
                _world.RemoveBody(this);
                IsDisposed = true;
                GC.SuppressFinalize(this);
            }
        }

        #endregion

        /// <summary>
        /// Makes a clone of the body. Fixtures and therefore shapes are not included.
        /// Use DeepClone() to clone the body, as well as fixtures and shapes.
        /// </summary>
        /// <param name="world"></param>
        /// <returns></returns>
        public Body Clone(World world = null)
        {
            Body body = new Body(world ?? _world, Position, Rotation, UserData);
            body._bodyType = _bodyType;
            body._linearVelocity = _linearVelocity;
            body._angularVelocity = _angularVelocity;
            body.GravityScale = GravityScale;
            body.UserData = UserData;
            body._enabled = _enabled;
            body._fixedRotation = _fixedRotation;
            body._sleepingAllowed = _sleepingAllowed;
            body._linearDamping = _linearDamping;
            body._angularDamping = _angularDamping;
            body._awake = _awake;
            body.IsBullet = IsBullet;
            body.IgnoreCCD = IgnoreCCD;
            body.IgnoreGravity = IgnoreGravity;
            body._torque = _torque;

            return body;
        }

        /// <summary>
        /// Clones the body and all attached fixtures and shapes. Simply said, it makes a complete copy of the body.
        /// </summary>
        /// <param name="world"></param>
        /// <returns></returns>
        public Body DeepClone(World world = null)
        {
            Body body = Clone(world ?? _world);

            int count = FixtureList.Count; //Make a copy of the count. Otherwise it causes an infinite loop.
            for (int i = 0; i < count; i++)
            {
                FixtureList[i].CloneOnto(body);
            }

            return body;
        }
    }
}