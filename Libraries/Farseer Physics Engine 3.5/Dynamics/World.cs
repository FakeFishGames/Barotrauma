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
//#define USE_ACTIVE_CONTACT_SET
//#define USE_AWAKE_BODY_SET
//#define USE_ISLAND_SET
//#define OPTIMIZE_TOI
//#define USE_IGNORE_CCD_CATEGORIES

using System;
using System.Collections.Generic;
using System.Diagnostics;
using FarseerPhysics.Collision;
using FarseerPhysics.Common;
using FarseerPhysics.Controllers;
using FarseerPhysics.Dynamics.Contacts;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;

namespace FarseerPhysics.Dynamics
{
    /// <summary>
    /// The world class manages all physics entities, dynamic simulation,
    /// and asynchronous queries.
    /// </summary>
    public class World
    {
        private float _invDt0;
        private Body[] _stack = new Body[64];
        private bool _stepComplete;
        private HashSet<Body> _bodyAddList = new HashSet<Body>();
        private HashSet<Body> _bodyRemoveList = new HashSet<Body>();
        private HashSet<Joint> _jointAddList = new HashSet<Joint>();
        private HashSet<Joint> _jointRemoveList = new HashSet<Joint>();
        private Func<Fixture, bool> _queryAABBCallback;
        private Func<int, bool> _queryAABBCallbackWrapper;
        private TOIInput _input = new TOIInput();
        private Fixture _myFixture;
        private Vector2 _point1;
        private Vector2 _point2;
        private List<Fixture> _testPointAllFixtures;
        private Stopwatch _watch = new Stopwatch();
        private Func<Fixture, Vector2, Vector2, float, float> _rayCastCallback;
        private Func<RayCastInput, int, Category, float> _rayCastCallbackWrapper;

        internal Queue<Contact> _contactPool = new Queue<Contact>(256);
        internal bool _worldHasNewFixture;

        /// <summary>
        /// Fires whenever a body has been added
        /// </summary>
        public BodyDelegate BodyAdded;

        /// <summary>
        /// Fires whenever a body has been removed
        /// </summary>
        public BodyDelegate BodyRemoved;

        /// <summary>
        /// Fires whenever a fixture has been added
        /// </summary>
        public FixtureDelegate FixtureAdded;

        /// <summary>
        /// Fires whenever a fixture has been removed
        /// </summary>
        public FixtureDelegate FixtureRemoved;

        /// <summary>
        /// Fires whenever a joint has been added
        /// </summary>
        public JointDelegate JointAdded;

        /// <summary>
        /// Fires whenever a joint has been removed
        /// </summary>
        public JointDelegate JointRemoved;

        /// <summary>
        /// Fires every time a controller is added to the World.
        /// </summary>
        public ControllerDelegate ControllerAdded;

        /// <summary>
        /// Fires every time a controlelr is removed form the World.
        /// </summary>
        public ControllerDelegate ControllerRemoved;

        /// <summary>
        /// Initializes a new instance of the <see cref="World"/> class.
        /// </summary>
        public World(Vector2 gravity)
        {
            Island = new Island();
            Enabled = true;
            ControllerList = new List<Controller>();
            BreakableBodyList = new List<BreakableBody>();
            BodyList = new List<Body>(1000);
            JointList = new List<Joint>(1000);

#if USE_AWAKE_BODY_SET
            AwakeBodySet = new HashSet<Body>();
            AwakeBodyList = new List<Body>(32);
#endif
#if USE_ISLAND_SET
            IslandSet = new HashSet<Body>();
#endif
#if OPTIMIZE_TOI
            TOISet = new HashSet<Body>();
#endif

            _queryAABBCallbackWrapper = QueryAABBCallbackWrapper;
            _rayCastCallbackWrapper = RayCastCallbackWrapper;

            ContactManager = new ContactManager(new DynamicTreeBroadPhase());
            Gravity = gravity;
        }

        private void ProcessRemovedJoints()
        {
            if (_jointRemoveList.Count > 0)
            {
                foreach (Joint joint in _jointRemoveList)
                {
                    bool collideConnected = joint.CollideConnected;

                    // Remove from the world list.
                    JointList.Remove(joint);

                    // Disconnect from island graph.
                    Body bodyA = joint.BodyA;
                    Body bodyB = joint.BodyB;

                    // Wake up connected bodies.
                    bodyA.Awake = true;

                    // WIP David
                    if (!joint.IsFixedType())
                    {
                        bodyB.Awake = true;
                    }

                    // Remove from body 1.
                    if (joint.EdgeA.Prev != null)
                    {
                        joint.EdgeA.Prev.Next = joint.EdgeA.Next;
                    }

                    if (joint.EdgeA.Next != null)
                    {
                        joint.EdgeA.Next.Prev = joint.EdgeA.Prev;
                    }

                    if (joint.EdgeA == bodyA.JointList)
                    {
                        bodyA.JointList = joint.EdgeA.Next;
                    }

                    joint.EdgeA.Prev = null;
                    joint.EdgeA.Next = null;

                    // WIP David
                    if (!joint.IsFixedType())
                    {
                        // Remove from body 2
                        if (joint.EdgeB.Prev != null)
                        {
                            joint.EdgeB.Prev.Next = joint.EdgeB.Next;
                        }

                        if (joint.EdgeB.Next != null)
                        {
                            joint.EdgeB.Next.Prev = joint.EdgeB.Prev;
                        }

                        if (joint.EdgeB == bodyB.JointList)
                        {
                            bodyB.JointList = joint.EdgeB.Next;
                        }

                        joint.EdgeB.Prev = null;
                        joint.EdgeB.Next = null;
                    }

                    // WIP David
                    if (!joint.IsFixedType())
                    {
                        // If the joint prevents collisions, then flag any contacts for filtering.
                        if (collideConnected == false)
                        {
                            ContactEdge edge = bodyB.ContactList;
                            while (edge != null)
                            {
                                if (edge.Other == bodyA)
                                {
                                    // Flag the contact for filtering at the next time step (where either
                                    // body is awake).
                                    edge.Contact.FilterFlag = true;
                                }

                                edge = edge.Next;
                            }
                        }
                    }

                    if (JointRemoved != null)
                    {
                        JointRemoved(joint);
                    }
                }

                _jointRemoveList.Clear();
            }
        }

        private void ProcessAddedJoints()
        {
            if (_jointAddList.Count > 0)
            {
                foreach (Joint joint in _jointAddList)
                {
                    // Connect to the world list.
                    JointList.Add(joint);

                    // Connect to the bodies' doubly linked lists.
                    joint.EdgeA.Joint = joint;
                    joint.EdgeA.Other = joint.BodyB;
                    joint.EdgeA.Prev = null;
                    joint.EdgeA.Next = joint.BodyA.JointList;

                    if (joint.BodyA.JointList != null)
                        joint.BodyA.JointList.Prev = joint.EdgeA;

                    joint.BodyA.JointList = joint.EdgeA;

                    // WIP David
                    if (!joint.IsFixedType())
                    {
                        joint.EdgeB.Joint = joint;
                        joint.EdgeB.Other = joint.BodyA;
                        joint.EdgeB.Prev = null;
                        joint.EdgeB.Next = joint.BodyB.JointList;

                        if (joint.BodyB.JointList != null)
                            joint.BodyB.JointList.Prev = joint.EdgeB;

                        joint.BodyB.JointList = joint.EdgeB;

                        Body bodyA = joint.BodyA;
                        Body bodyB = joint.BodyB;

                        // If the joint prevents collisions, then flag any contacts for filtering.
                        if (joint.CollideConnected == false)
                        {
                            ContactEdge edge = bodyB.ContactList;
                            while (edge != null)
                            {
                                if (edge.Other == bodyA)
                                {
                                    // Flag the contact for filtering at the next time step (where either
                                    // body is awake).
                                    edge.Contact.FilterFlag = true;
                                }

                                edge = edge.Next;
                            }
                        }
                    }

                    if (JointAdded != null)
                        JointAdded(joint);

                    // Note: creating a joint doesn't wake the bodies.
                }

                _jointAddList.Clear();
            }
        }

        private void ProcessAddedBodies()
        {
            if (_bodyAddList.Count > 0)
            {
                foreach (Body body in _bodyAddList)
                {
#if USE_AWAKE_BODY_SET
                    Debug.Assert(!body.IsDisposed);
                    if (body.Awake)
                    {
                        if (!AwakeBodySet.Contains(body))
                        {
                            AwakeBodySet.Add(body);
                        }
                    }
                    else
                    {
                        if (AwakeBodySet.Contains(body))
                        {
                            AwakeBodySet.Remove(body);
                        }
                    }
#endif
                    // Add to world list.
                    BodyList.Add(body);

                    if (BodyAdded != null)
                        BodyAdded(body);
                }

                _bodyAddList.Clear();
            }
        }

        private void ProcessRemovedBodies()
        {
            if (_bodyRemoveList.Count > 0)
            {
                foreach (Body body in _bodyRemoveList)
                {
                    Debug.Assert(BodyList.Count > 0);

                    // You tried to remove a body that is not contained in the BodyList.
                    // Are you removing the body more than once?
                    Debug.Assert(BodyList.Contains(body));

#if USE_AWAKE_BODY_SET
                    Debug.Assert(!AwakeBodySet.Contains(body));
#endif
                    // Delete the attached joints.
                    JointEdge je = body.JointList;
                    while (je != null)
                    {
                        JointEdge je0 = je;
                        je = je.Next;

                        RemoveJoint(je0.Joint, false);
                    }
                    body.JointList = null;

                    // Delete the attached contacts.
                    ContactEdge ce = body.ContactList;
                    while (ce != null)
                    {
                        ContactEdge ce0 = ce;
                        ce = ce.Next;
                        ContactManager.Destroy(ce0.Contact);
                    }
                    body.ContactList = null;

                    // Delete the attached fixtures. This destroys broad-phase proxies.
                    for (int i = 0; i < body.FixtureList.Count; i++)
                    {
                        body.FixtureList[i].DestroyProxies(ContactManager.BroadPhase);
                        body.FixtureList[i].Destroy();
                    }

                    body.FixtureList = null;

                    // Remove world body list.
                    BodyList.Remove(body);

                    if (BodyRemoved != null)
                        BodyRemoved(body);

#if USE_AWAKE_BODY_SET
                    Debug.Assert(!AwakeBodySet.Contains(body));
#endif
                }

                _bodyRemoveList.Clear();
            }
        }

        private bool QueryAABBCallbackWrapper(int proxyId)
        {
            FixtureProxy proxy = ContactManager.BroadPhase.GetProxy(proxyId);
            return _queryAABBCallback(proxy.Fixture);
        }

        private float RayCastCallbackWrapper(RayCastInput rayCastInput, int proxyId, Category collisionCategory = Category.All)
        {
            FixtureProxy proxy = ContactManager.BroadPhase.GetProxy(proxyId);
            Fixture fixture = proxy.Fixture;
            if (collisionCategory != Category.All && !collisionCategory.HasFlag(fixture.CollisionCategories)) 
            { 
                return rayCastInput.MaxFraction; 
            }
            int index = proxy.ChildIndex;
            bool hit = fixture.RayCast(out RayCastOutput output, ref rayCastInput, index);

            if (hit)
            {
                float fraction = output.Fraction;
                Vector2 point = (1.0f - fraction) * rayCastInput.Point1 + fraction * rayCastInput.Point2;
                return _rayCastCallback(fixture, point, output.Normal, fraction);
            }

            return rayCastInput.MaxFraction;
        }

        private void Solve(ref TimeStep step)
        {
            // Size the island for the worst case.
            Island.Reset(BodyList.Count,
                         ContactManager.ContactList.Count,
                         JointList.Count,
                         ContactManager);

            // Clear all the island flags.
#if USE_ISLAND_SET
            Debug.Assert(IslandSet.Count == 0);
#else
            foreach (Body b in BodyList)
            {
                b._island = false;
            }
#endif

#if USE_ACTIVE_CONTACT_SET
            foreach (var c in ContactManager.ActiveContacts)
            {
                c.Flags &= ~ContactFlags.Island;
            }
#else
            foreach (Contact c in ContactManager.ContactList)
            {
                c.IslandFlag = false;
            }
#endif
            foreach (Joint j in JointList)
            {
                j.IslandFlag = false;
            }

            // Build and simulate all awake islands.
            int stackSize = BodyList.Count;
            if (stackSize > _stack.Length)
                _stack = new Body[Math.Max(_stack.Length * 2, stackSize)];

#if USE_AWAKE_BODY_SET

            // If AwakeBodyList is empty, the Island code will not have a chance
            // to update the diagnostics timer so reset the timer here. 
            Island.JointUpdateTime = 0;
      
            Debug.Assert(AwakeBodyList.Count == 0);
            AwakeBodyList.AddRange(AwakeBodySet);

            foreach (var seed in AwakeBodyList)
            {
#else
            for (int index = BodyList.Count - 1; index >= 0; index--)
            {
                Body seed = BodyList[index];
#endif
                if (seed._island)
                {
                    continue;
                }

                if (seed.Awake == false || seed.Enabled == false)
                {
                    continue;
                }

                // The seed can be dynamic or kinematic.
                if (seed.BodyType == BodyType.Static)
                {
                    continue;
                }

                // Reset island and stack.
                Island.Clear();
                int stackCount = 0;
                _stack[stackCount++] = seed;

#if USE_ISLAND_SET
            if (!IslandSet.Contains(body))
                IslandSet.Add(body);
#endif
                seed._island = true;

                // Perform a depth first search (DFS) on the constraint graph.
                while (stackCount > 0)
                {
                    // Grab the next body off the stack and add it to the island.
                    Body b = _stack[--stackCount];
                    Debug.Assert(b.Enabled);
                    Island.Add(b);

                    // Make sure the body is awake.
                    b.Awake = true;

                    // To keep islands as small as possible, we don't
                    // propagate islands across static bodies.
                    if (b.BodyType == BodyType.Static)
                    {
                        continue;
                    }

                    // Search all contacts connected to this body.
                    for (ContactEdge ce = b.ContactList; ce != null; ce = ce.Next)
                    {
                        Contact contact = ce.Contact;

                        // Has this contact already been added to an island?
                        if (contact.IslandFlag)
                        {
                            continue;
                        }

                        // Is this contact solid and touching?
                        if (ce.Contact.Enabled == false || ce.Contact.IsTouching == false)
                        {
                            continue;
                        }

                        // Skip sensors.
                        bool sensorA = contact.FixtureA.IsSensor;
                        bool sensorB = contact.FixtureB.IsSensor;
                        if (sensorA || sensorB)
                        {
                            continue;
                        }

                        Island.Add(contact);
                        contact.IslandFlag = true;

                        Body other = ce.Other;

                        // Was the other body already added to this island?
                        if (other._island)
                        {
                            continue;
                        }

                        Debug.Assert(stackCount < stackSize);
                        _stack[stackCount++] = other;

#if USE_ISLAND_SET
                        if (!IslandSet.Contains(body))
                            IslandSet.Add(body);
#endif
                        other._island = true;
                    }

                    // Search all joints connect to this body.
                    for (JointEdge je = b.JointList; je != null; je = je.Next)
                    {
                        if (je.Joint.IslandFlag)
                        {
                            continue;
                        }

                        Body other = je.Other;

                        // WIP David
                        //Enter here when it's a non-fixed joint. Non-fixed joints have a other body.
                        if (other != null)
                        {
                            // Don't simulate joints connected to inactive bodies.
                            if (other.Enabled == false)
                            {
                                continue;
                            }

                            Island.Add(je.Joint);
                            je.Joint.IslandFlag = true;

                            if (other._island)
                            {
                                continue;
                            }

                            Debug.Assert(stackCount < stackSize);
                            _stack[stackCount++] = other;
#if USE_ISLAND_SET
                            if (!IslandSet.Contains(body))
                                IslandSet.Add(body);
#endif
                            other._island = true;
                        }
                        else
                        {
                            Island.Add(je.Joint);
                            je.Joint.IslandFlag = true;
                        }
                    }
                }

                Island.Solve(ref step, ref Gravity);

                // Post solve cleanup.
                for (int i = 0; i < Island.BodyCount; ++i)
                {
                    // Allow static bodies to participate in other islands.
                    Body b = Island.Bodies[i];
                    if (b.BodyType == BodyType.Static)
                    {
                        b._island = false;
                    }
                }
            }

            // Synchronize fixtures, check for out of range bodies.
#if USE_ISLAND_SET
            foreach (var b in IslandSet)
#else
            foreach (Body b in BodyList)
#endif
            {
                // If a body was not in an island then it did not move.
                if (!b._island)
                {
                    continue;
                }
#if USE_ISLAND_SET
                Debug.Assert(b.BodyType != BodyType.Static);
#else
                if (b.BodyType == BodyType.Static)
                {
                    continue;
                }
#endif

                // Update fixtures (for broad-phase).
                b.SynchronizeFixtures();
            }
#if OPTIMIZE_TOI
            foreach (var b in IslandSet)
            {
                if (!TOISet.Contains(b))
                {
                    TOISet.Add(b);
                }
            }
#endif
#if USE_ISLAND_SET
            IslandSet.Clear();
#endif

            // Look for new contacts.
            ContactManager.FindNewContacts();

#if USE_AWAKE_BODY_SET
            AwakeBodyList.Clear();
#endif
        }

        private void SolveTOI(ref TimeStep step)
        {
            Island.Reset(2 * Settings.MaxTOIContacts, Settings.MaxTOIContacts, 0, ContactManager);

#if OPTIMIZE_TOI
            bool wasStepComplete = _stepComplete;
#endif
            if (_stepComplete)
            {
#if OPTIMIZE_TOI
                foreach (var b in TOISet)
                {
                    b.Flags &= ~BodyFlags.Island;
                    b.Sweep.Alpha0 = 0.0f;
                }
#else
                for (int i = 0; i < BodyList.Count; i++)
                {
                    BodyList[i]._island = false;
                    BodyList[i]._sweep.Alpha0 = 0.0f;
                }
#endif
#if USE_ACTIVE_CONTACT_SET
                foreach (var c in ContactManager.ActiveContacts)
                {
#else
                for (int i = 0; i < ContactManager.ContactList.Count; i++)
                {
                    Contact c = ContactManager.ContactList[i];
#endif
                    // Invalidate TOI
                    c.IslandFlag = false;
                    c.TOIFlag = false;
                    c._toiCount = 0;
                    c._toi = 1.0f;
                }
            }

            // Find TOI events and solve them.
            for (; ; )
            {
                // Find the first TOI.
                Contact minContact = null;
                float minAlpha = 1.0f;

#if USE_ACTIVE_CONTACT_SET
                foreach (var c in ContactManager.ActiveContacts)
                {
#else
                for (int i = 0; i < ContactManager.ContactList.Count; i++)
                {
                    Contact c = ContactManager.ContactList[i];
#endif

                    // Is this contact disabled?
                    if (c.Enabled == false)
                    {
                        continue;
                    }

                    // Prevent excessive sub-stepping.
                    if (c._toiCount > Settings.MaxSubSteps)
                    {
                        continue;
                    }

                    float alpha;
                    if (c.TOIFlag)
                    {
                        // This contact has a valid cached TOI.
                        alpha = c._toi;
                    }
                    else
                    {
                        Fixture fA = c.FixtureA;
                        Fixture fB = c.FixtureB;

                        // Is there a sensor?
                        if (fA.IsSensor || fB.IsSensor)
                        {
                            continue;
                        }

                        Body bA = fA.Body;
                        Body bB = fB.Body;

                        BodyType typeA = bA.BodyType;
                        BodyType typeB = bB.BodyType;
                        Debug.Assert(typeA == BodyType.Dynamic || typeB == BodyType.Dynamic);

                        bool activeA = bA.Awake && typeA != BodyType.Static;
                        bool activeB = bB.Awake && typeB != BodyType.Static;

                        // Is at least one body active (awake and dynamic or kinematic)?
                        if (activeA == false && activeB == false)
                        {
                            continue;
                        }

                        bool collideA = (bA.IsBullet || typeA != BodyType.Dynamic) && ((fA.IgnoreCCDWith & fB.CollisionCategories) == 0) && !bA.IgnoreCCD;
                        bool collideB = (bB.IsBullet || typeB != BodyType.Dynamic) && ((fB.IgnoreCCDWith & fA.CollisionCategories) == 0) && !bB.IgnoreCCD;

                        // Are these two non-bullet dynamic bodies?
                        if (collideA == false && collideB == false)
                        {
                            continue;
                        }

#if OPTIMIZE_TOI
                        if (_stepComplete)
                        {
                            if (!TOISet.Contains(bA))
                            {
                                TOISet.Add(bA);
                                bA.Flags &= ~BodyFlags.Island;
                                bA.Sweep.Alpha0 = 0.0f;
                            }
                            if (!TOISet.Contains(bB))
                            {
                                TOISet.Add(bB);
                                bB.Flags &= ~BodyFlags.Island;
                                bB.Sweep.Alpha0 = 0.0f;
                            }
                        }
#endif
                        // Compute the TOI for this contact.
                        // Put the sweeps onto the same time interval.
                        float alpha0 = bA._sweep.Alpha0;

                        if (bA._sweep.Alpha0 < bB._sweep.Alpha0)
                        {
                            alpha0 = bB._sweep.Alpha0;
                            bA._sweep.Advance(alpha0);
                        }
                        else if (bB._sweep.Alpha0 < bA._sweep.Alpha0)
                        {
                            alpha0 = bA._sweep.Alpha0;
                            bB._sweep.Advance(alpha0);
                        }

                        Debug.Assert(alpha0 < 1.0f);

                        // Compute the time of impact in interval [0, minTOI]
                        _input.ProxyA.Set(fA.Shape, c.ChildIndexA);
                        _input.ProxyB.Set(fB.Shape, c.ChildIndexB);
                        _input.SweepA = bA._sweep;
                        _input.SweepB = bB._sweep;
                        _input.TMax = 1.0f;

                        TOIOutput output;
                        TimeOfImpact.CalculateTimeOfImpact(out output, _input);

                        // Beta is the fraction of the remaining portion of the .
                        float beta = output.T;
                        if (output.State == TOIOutputState.Touching)
                        {
                            alpha = Math.Min(alpha0 + (1.0f - alpha0) * beta, 1.0f);
                        }
                        else
                        {
                            alpha = 1.0f;
                        }

                        c._toi = alpha;
                        c.TOIFlag = true;
                    }

                    if (alpha < minAlpha)
                    {
                        // This is the minimum TOI found so far.
                        minContact = c;
                        minAlpha = alpha;
                    }
                }

                if (minContact == null || 1.0f - 10.0f * Settings.Epsilon < minAlpha)
                {
                    // No more TOI events. Done!
                    _stepComplete = true;
                    break;
                }

                // Advance the bodies to the TOI.
                Fixture fA1 = minContact.FixtureA;
                Fixture fB1 = minContact.FixtureB;
                Body bA0 = fA1.Body;
                Body bB0 = fB1.Body;

                Sweep backup1 = bA0._sweep;
                Sweep backup2 = bB0._sweep;

                bA0.Advance(minAlpha);
                bB0.Advance(minAlpha);

                // The TOI contact likely has some new contact points.
                minContact.Update(ContactManager);
                minContact.TOIFlag = false;
                ++minContact._toiCount;

                // Is the contact solid?
                if (minContact.Enabled == false || minContact.IsTouching == false)
                {
                    // Restore the sweeps.
                    minContact.Enabled = false;
                    bA0._sweep = backup1;
                    bB0._sweep = backup2;
                    bA0.SynchronizeTransform();
                    bB0.SynchronizeTransform();
                    continue;
                }

                bA0.Awake = true;
                bB0.Awake = true;

                // Build the island
                Island.Clear();
                Island.Add(bA0);
                Island.Add(bB0);
                Island.Add(minContact);

                bA0._island = true;
                bB0._island = true;
                minContact.IslandFlag = true;

                // Get contacts on bodyA and bodyB.
                Body[] bodies = { bA0, bB0 };
                for (int i = 0; i < 2; ++i)
                {
                    Body body = bodies[i];
                    if (body.BodyType == BodyType.Dynamic)
                    {
                        for (ContactEdge ce = body.ContactList; ce != null; ce = ce.Next)
                        {
                            Contact contact = ce.Contact;

                            if (Island.BodyCount == Island.BodyCapacity)
                            {
                                break;
                            }

                            if (Island.ContactCount == Island.ContactCapacity)
                            {
                                break;
                            }

                            // Has this contact already been added to the island?
                            if (contact.IslandFlag)
                            {
                                continue;
                            }

                            // Only add static, kinematic, or bullet bodies.
                            Body other = ce.Other;
                            if (other.BodyType == BodyType.Dynamic &&
                                body.IsBullet == false && other.IsBullet == false)
                            {
                                continue;
                            }

                            // Skip sensors.
                            if (contact.FixtureA.IsSensor || contact.FixtureB.IsSensor)
                            {
                                continue;
                            }

                            // Tentatively advance the body to the TOI.
                            Sweep backup = other._sweep;
                            if (!other._island)
                            {
                                other.Advance(minAlpha);
                            }

                            // Update the contact points
                            contact.Update(ContactManager);

                            // Was the contact disabled by the user?
                            if (contact.Enabled == false)
                            {
                                other._sweep = backup;
                                other.SynchronizeTransform();
                                continue;
                            }

                            // Are there contact points?
                            if (contact.IsTouching == false)
                            {
                                other._sweep = backup;
                                other.SynchronizeTransform();
                                continue;
                            }

                            // Add the contact to the island
                            contact.IslandFlag = true;
                            Island.Add(contact);

                            // Has the other body already been added to the island?
                            if (other._island)
                            {
                                continue;
                            }

                            // Add the other body to the island.
                            other._island = true;

                            if (other.BodyType != BodyType.Static)
                            {
                                other.Awake = true;
                            }
#if OPTIMIZE_TOI
                            if (_stepComplete)
                            {
                                if (!TOISet.Contains(other))
                                {
                                    TOISet.Add(other);
                                    other.Sweep.Alpha0 = 0.0f;
                                }
                            }
#endif
                            Island.Add(other);
                        }
                    }
                }

                TimeStep subStep;
                subStep.dt = (1.0f - minAlpha) * step.dt;
                subStep.inv_dt = 1.0f / subStep.dt;
                subStep.dtRatio = 1.0f;
                Island.SolveTOI(ref subStep, bA0.IslandIndex, bB0.IslandIndex, false);

                // Reset island flags and synchronize broad-phase proxies.
                for (int i = 0; i < Island.BodyCount; ++i)
                {
                    Body body = Island.Bodies[i];
                    body._island = false;

                    if (body.BodyType != BodyType.Dynamic)
                    {
                        continue;
                    }

                    body.SynchronizeFixtures();

                    // Invalidate all contact TOIs on this displaced body.
                    for (ContactEdge ce = body.ContactList; ce != null; ce = ce.Next)
                    {
                        ce.Contact.TOIFlag = false;
                        ce.Contact.IslandFlag = false;
                    }
                }

                // Commit fixture proxy movements to the broad-phase so that new contacts are created.
                // Also, some contacts can be destroyed.
                ContactManager.FindNewContacts();

                if (Settings.EnableSubStepping)
                {
                    _stepComplete = false;
                    break;
                }
            }
#if OPTIMIZE_TOI
            if (wasStepComplete)
            {
                TOISet.Clear();
            }
#endif
        }

        public List<Controller> ControllerList { get; private set; }

        public List<BreakableBody> BreakableBodyList { get; private set; }

        public float UpdateTime { get; private set; }

        public float ContinuousPhysicsTime { get; private set; }

        public float ControllersUpdateTime { get; private set; }

        public float AddRemoveTime { get; private set; }

        public float NewContactsTime { get; private set; }

        public float ContactsUpdateTime { get; private set; }

        public float SolveUpdateTime { get; private set; }

        /// <summary>
        /// Get the number of broad-phase proxies.
        /// </summary>
        /// <value>The proxy count.</value>
        public int ProxyCount
        {
            get { return ContactManager.BroadPhase.ProxyCount; }
        }

        /// <summary>
        /// Change the global gravity vector.
        /// </summary>
        /// <value>The gravity.</value>
        public Vector2 Gravity;

        /// <summary>
        /// Get the contact manager for testing.
        /// </summary>
        /// <value>The contact manager.</value>
        public ContactManager ContactManager { get; private set; }

        /// <summary>
        /// Get the world body list.
        /// </summary>
        /// <value>Thehead of the world body list.</value>
        public List<Body> BodyList { get; private set; }

#if USE_AWAKE_BODY_SET
        public HashSet<Body> AwakeBodySet { get; private set; }
        List<Body> AwakeBodyList;
#endif
#if USE_ISLAND_SET
        HashSet<Body> IslandSet;
#endif
#if OPTIMIZE_TOI
        HashSet<Body> TOISet;
#endif

        /// <summary>
        /// Get the world joint list. 
        /// </summary>
        /// <value>The joint list.</value>
        public List<Joint> JointList { get; private set; }

        /// <summary>
        /// Get the world contact list. With the returned contact, use Contact.GetNext to get
        /// the next contact in the world list. A null contact indicates the end of the list.
        /// </summary>
        /// <value>The head of the world contact list.</value>
        public List<Contact> ContactList
        {
            get { return ContactManager.ContactList; }
        }

        /// <summary>
        /// If false, the whole simulation stops. It still processes added and removed geometries.
        /// </summary>
        public bool Enabled { get; set; }

        public Island Island { get; private set; }

        /// <summary>
        /// Add a rigid body.
        /// </summary>
        /// <returns></returns>
        internal void AddBody(Body body)
        {
            Debug.Assert(!_bodyAddList.Contains(body), "You are adding the same body more than once.");

            if (!_bodyAddList.Contains(body))
                _bodyAddList.Add(body);
        }

        /// <summary>
        /// Destroy a rigid body.
        /// Warning: This automatically deletes all associated shapes and joints.
        /// </summary>
        /// <param name="body">The body.</param>
        public void RemoveBody(Body body)
        {
            Debug.Assert(!_bodyRemoveList.Contains(body),
                         "The body is already marked for removal. You are removing the body more than once.");

            Debug.Assert(body != null);

            if (!_bodyRemoveList.Contains(body))
                _bodyRemoveList.Add(body);

#if USE_AWAKE_BODY_SET
            if (AwakeBodySet.Contains(body))
            {
                AwakeBodySet.Remove(body);
            }
#endif
        }

        /// <summary>
        /// Create a joint to constrain bodies together. This may cause the connected bodies to cease colliding.
        /// </summary>
        /// <param name="joint">The joint.</param>
        public void AddJoint(Joint joint)
        {
            Debug.Assert(!_jointAddList.Contains(joint), "You are adding the same joint more than once.");

            if (!_jointAddList.Contains(joint))
                _jointAddList.Add(joint);
        }

        private void RemoveJoint(Joint joint, bool doCheck)
        {
            if (doCheck)
            {
                Debug.Assert(!_jointRemoveList.Contains(joint),
                             "The joint is already marked for removal. You are removing the joint more than once.");
            }

            if (!_jointRemoveList.Contains(joint))
                _jointRemoveList.Add(joint);
        }

        /// <summary>
        /// Destroy a joint. This may cause the connected bodies to begin colliding.
        /// </summary>
        /// <param name="joint">The joint.</param>
        public void RemoveJoint(Joint joint)
        {
            RemoveJoint(joint, true);
        }

        /// <summary>
        /// All adds and removes are cached by the World duing a World step.
        /// To process the changes before the world updates again, call this method.
        /// </summary>
        public void ProcessChanges()
        {
            ProcessAddedBodies();
            ProcessAddedJoints();

            ProcessRemovedBodies();
            ProcessRemovedJoints();
#if DEBUG && USE_AWAKE_BODY_SET
            foreach (var b in AwakeBodySet)
            {
                Debug.Assert(BodyList.Contains(b));
            }
#endif
        }

        /// <summary>
        /// Take a time step. This performs collision detection, integration,
        /// and consraint solution.
        /// </summary>
        /// <param name="dt">The amount of time to simulate, this should not vary.</param>
        public void Step(float dt)
        {
            if (!Enabled)
                return;

            if (Settings.EnableDiagnostics)
                _watch.Start();

            ProcessChanges();

            if (Settings.EnableDiagnostics)
                AddRemoveTime = _watch.ElapsedTicks;

            // If new fixtures were added, we need to find the new contacts.
            if (_worldHasNewFixture)
            {
                ContactManager.FindNewContacts();
                _worldHasNewFixture = false;
            }

            if (Settings.EnableDiagnostics)
                NewContactsTime = _watch.ElapsedTicks - AddRemoveTime;

            //FPE only: moved position and velocity iterations into Settings.cs
            TimeStep step;
            step.inv_dt = dt > 0.0f ? 1.0f / dt : 0.0f;
            step.dt = dt;
            step.dtRatio = _invDt0 * dt;

            //Update controllers
            for (int i = 0; i < ControllerList.Count; i++)
            {
                ControllerList[i].Update(dt);
            }

            if (Settings.EnableDiagnostics)
                ControllersUpdateTime = _watch.ElapsedTicks - (AddRemoveTime + NewContactsTime);

            // Update contacts. This is where some contacts are destroyed.
            ContactManager.Collide();

            if (Settings.EnableDiagnostics)
                ContactsUpdateTime = _watch.ElapsedTicks - (AddRemoveTime + NewContactsTime + ControllersUpdateTime);

            // Integrate velocities, solve velocity raints, and integrate positions.
            Solve(ref step);

            if (Settings.EnableDiagnostics)
                SolveUpdateTime = _watch.ElapsedTicks - (AddRemoveTime + NewContactsTime + ControllersUpdateTime + ContactsUpdateTime);

            // Handle TOI events.
            if (Settings.ContinuousPhysics)
            {
                SolveTOI(ref step);
            }

            if (Settings.EnableDiagnostics)
                ContinuousPhysicsTime = _watch.ElapsedTicks - (AddRemoveTime + NewContactsTime + ControllersUpdateTime + ContactsUpdateTime + SolveUpdateTime);

            if (Settings.AutoClearForces)
                ClearForces();

            for (int i = 0; i < BreakableBodyList.Count; i++)
            {
                BreakableBodyList[i].Update();
            }

            _invDt0 = step.inv_dt;

            if (Settings.EnableDiagnostics)
            {
                _watch.Stop();
                UpdateTime = _watch.ElapsedTicks;
                _watch.Reset();
            }
        }

        /// <summary>
        /// Call this after you are done with time steps to clear the forces. You normally
        /// call this after each call to Step, unless you are performing sub-steps. By default,
        /// forces will be automatically cleared, so you don't need to call this function.
        /// </summary>
        public void ClearForces()
        {
            for (int i = 0; i < BodyList.Count; i++)
            {
                Body body = BodyList[i];
                body._force = Vector2.Zero;
                body._torque = 0.0f;
            }
        }

        /// <summary>
        /// Query the world for all fixtures that potentially overlap the provided AABB.
        /// 
        /// Inside the callback:
        /// Return true: Continues the query
        /// Return false: Terminate the query
        /// </summary>
        /// <param name="callback">A user implemented callback class.</param>
        /// <param name="aabb">The aabb query box.</param>
        public void QueryAABB(Func<Fixture, bool> callback, ref AABB aabb)
        {
            _queryAABBCallback = callback;
            ContactManager.BroadPhase.Query(_queryAABBCallbackWrapper, ref aabb);
            _queryAABBCallback = null;
        }

        /// <summary>
        /// Query the world for all fixtures that potentially overlap the provided AABB.
        /// Use the overload with a callback for filtering and better performance.
        /// </summary>
        /// <param name="aabb">The aabb query box.</param>
        /// <returns>A list of fixtures that were in the affected area.</returns>
        public List<Fixture> QueryAABB(ref AABB aabb)
        {
            List<Fixture> affected = new List<Fixture>();

            QueryAABB(fixture =>
                {
                    affected.Add(fixture);
                    return true;
                }, ref aabb);

            return affected;
        }

        /// <summary>
        /// Ray-cast the world for all fixtures in the path of the ray. Your callback
        /// controls whether you get the closest point, any point, or n-points.
        /// The ray-cast ignores shapes that contain the starting point.
        /// 
        /// Inside the callback:
        /// return -1: ignore this fixture and continue
        /// return 0: terminate the ray cast
        /// return fraction: clip the ray to this point
        /// return 1: don't clip the ray and continue
        /// </summary>
        /// <param name="callback">A user implemented callback class.</param>
        /// <param name="point1">The ray starting point.</param>
        /// <param name="point2">The ray ending point.</param>
        public void RayCast(Func<Fixture, Vector2, Vector2, float, float> callback, Vector2 point1, Vector2 point2, Category collisionCategory = Category.All)
        {
            RayCastInput input = new RayCastInput();
            input.MaxFraction = 1.0f;
            input.Point1 = point1;
            input.Point2 = point2;

            _rayCastCallback = callback;
            ContactManager.BroadPhase.RayCast(_rayCastCallbackWrapper, ref input);
            _rayCastCallback = null;
        }

        public List<Fixture> RayCast(Vector2 point1, Vector2 point2)
        {
            List<Fixture> affected = new List<Fixture>();

            RayCast((f, p, n, fr) =>
            {
                affected.Add(f);
                return 1;
            }, point1, point2);

            return affected;
        }

        public void AddController(Controller controller)
        {
            Debug.Assert(!ControllerList.Contains(controller), "You are adding the same controller more than once.");

            controller.World = this;
            ControllerList.Add(controller);

            if (ControllerAdded != null)
                ControllerAdded(controller);
        }

        public void RemoveController(Controller controller)
        {
            Debug.Assert(ControllerList.Contains(controller),
                         "You are removing a controller that is not in the simulation.");

            if (ControllerList.Contains(controller))
            {
                ControllerList.Remove(controller);

                if (ControllerRemoved != null)
                    ControllerRemoved(controller);
            }
        }

        public void AddBreakableBody(BreakableBody breakableBody)
        {
            BreakableBodyList.Add(breakableBody);
        }

        public void RemoveBreakableBody(BreakableBody breakableBody)
        {
            //The breakable body list does not contain the body you tried to remove.
            Debug.Assert(BreakableBodyList.Contains(breakableBody));

            BreakableBodyList.Remove(breakableBody);
        }

        public Fixture TestPoint(Vector2 point)
        {
            AABB aabb;
            Vector2 d = new Vector2(Settings.Epsilon, Settings.Epsilon);
            aabb.LowerBound = point - d;
            aabb.UpperBound = point + d;

            _myFixture = null;
            _point1 = point;

            // Query the world for overlapping shapes.
            QueryAABB(TestPointCallback, ref aabb);

            return _myFixture;
        }

        private bool TestPointCallback(Fixture fixture)
        {
            bool inside = fixture.TestPoint(ref _point1);
            if (inside)
            {
                _myFixture = fixture;
                return false;
            }

            // Continue the query.
            return true;
        }

        /// <summary>
        /// Returns a list of fixtures that are at the specified point.
        /// </summary>
        /// <param name="point">The point.</param>
        /// <returns></returns>
        public List<Fixture> TestPointAll(Vector2 point)
        {
            AABB aabb;
            Vector2 d = new Vector2(Settings.Epsilon, Settings.Epsilon);
            aabb.LowerBound = point - d;
            aabb.UpperBound = point + d;

            _point2 = point;
            _testPointAllFixtures = new List<Fixture>();

            // Query the world for overlapping shapes.
            QueryAABB(TestPointAllCallback, ref aabb);

            return _testPointAllFixtures;
        }

        private bool TestPointAllCallback(Fixture fixture)
        {
            bool inside = fixture.TestPoint(ref _point2);
            if (inside)
                _testPointAllFixtures.Add(fixture);

            // Continue the query.
            return true;
        }

        /// Shift the world origin. Useful for large worlds.
        /// The body shift formula is: position -= newOrigin
        /// @param newOrigin the new origin with respect to the old origin
        /// Warning: Calling this method mid-update might cause a crash.
        public void ShiftOrigin(Vector2 newOrigin)
        {
            foreach (Body b in BodyList)
            {
                b._xf.p -= newOrigin;
                b._sweep.C0 -= newOrigin;
                b._sweep.C -= newOrigin;
            }

            foreach (Joint joint in JointList)
            {
                //joint.ShiftOrigin(newOrigin); //TODO: uncomment
            }

            ContactManager.BroadPhase.ShiftOrigin(newOrigin);
        }

        public void Clear()
        {
            ProcessChanges();

            for (int i = BodyList.Count - 1; i >= 0; i--)
            {
                RemoveBody(BodyList[i]);
            }

            for (int i = ControllerList.Count - 1; i >= 0; i--)
            {
                RemoveController(ControllerList[i]);
            }

            for (int i = BreakableBodyList.Count - 1; i >= 0; i--)
            {
                RemoveBreakableBody(BreakableBodyList[i]);
            }

            ProcessChanges();
        }
    }
}