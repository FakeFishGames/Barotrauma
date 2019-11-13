// Copyright (c) 2017 Kastellanos Nikolaos

/* Original source Farseer Physics Engine:
 * Copyright (c) 2014 Ian Qvist, http://farseerphysics.codeplex.com
 * Microsoft Permissive License (Ms-PL) v1.1
 */

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

using System.Collections.Generic;
using FarseerPhysics.Collision;
using FarseerPhysics.Dynamics.Contacts;

namespace FarseerPhysics.Dynamics
{
    public class ContactManager
    {
        #region Settings
        /// <summary>
        /// A threshold for activating multiple cores to solve VelocityConstraints.
        /// An Island with a contact count above this threshold will use multiple threads to solve VelocityConstraints.
        /// A value of 0 will always use multithreading. A value of (int.MaxValue) will never use multithreading.
        /// Typical values are {128 or 256}.
        /// </summary>
        public int VelocityConstraintsMultithreadThreshold = 128;

        /// <summary>
        /// A threshold for activating multiple cores to solve PositionConstraints.
        /// An Island with a contact count above this threshold will use multiple threads to solve PositionConstraints.
        /// A value of 0 will always use multithreading. A value of (int.MaxValue) will never use multithreading.
        /// Typical values are {128 or 256}.
        /// </summary>
        public int PositionConstraintsMultithreadThreshold = 128;
        
        /// <summary>
        /// A threshold for activating multiple cores to solve Collide.
        /// An World with a contact count above this threshold will use multiple threads to solve Collide.
        /// A value of 0 will always use multithreading. A value of (int.MaxValue) will never use multithreading.
        /// Typical values are {128 or 256}.
        /// </summary>
        public int CollideMultithreadThreshold = 64;
        #endregion


        /// <summary>
        /// Fires when a contact is created
        /// </summary>
        public BeginContactDelegate BeginContact;

        public IBroadPhase BroadPhase;

        public readonly ContactListHead ContactList;
        public int ContactCount { get; private set; }
        internal readonly ContactListHead _contactPoolList;

        /// <summary>
        /// The filter used by the contact manager.
        /// </summary>
        public CollisionFilterDelegate ContactFilter;


#if USE_ACTIVE_CONTACT_SET
        /// <summary>
        /// The set of active contacts.
        /// </summary>
		public HashSet<Contact> ActiveContacts = new HashSet<Contact>();

        /// <summary>
        /// A temporary copy of active contacts that is used during updates so
		/// the hash set can have members added/removed during the update.
		/// This list is cleared after every update.
        /// </summary>
		List<Contact> ActiveList = new List<Contact>();
#endif

        /// <summary>
        /// Fires when a contact is deleted
        /// </summary>
        public EndContactDelegate EndContact;

        /// <summary>
        /// Fires when the broadphase detects that two Fixtures are close to each other.
        /// </summary>
        public BroadphaseDelegate OnBroadphaseCollision;

        /// <summary>
        /// Fires after the solver has run
        /// </summary>
        public PostSolveDelegate PostSolve;

        /// <summary>
        /// Fires before the solver runs
        /// </summary>
        public PreSolveDelegate PreSolve;

        internal ContactManager(IBroadPhase broadPhase)
        {
            ContactList = new ContactListHead();
            ContactCount = 0;
            _contactPoolList = new ContactListHead();

            BroadPhase = broadPhase;
            OnBroadphaseCollision = AddPair;
        }

        // Broad-phase callback.
        private void AddPair(int proxyIdA, int proxyIdB)
        {
            FixtureProxy proxyA = BroadPhase.GetProxy(proxyIdA);
            FixtureProxy proxyB = BroadPhase.GetProxy(proxyIdB);

            Body bodyA = proxyA.Body;
            Body bodyB = proxyB.Body;

            // Are the fixtures on the same body?
            if (bodyA == bodyB)
            {
                return;
            }

            Fixture fixtureA = proxyA.Fixture;
            Fixture fixtureB = proxyB.Fixture;

            int indexA = proxyA.ChildIndex;
            int indexB = proxyB.ChildIndex;

            // Does a contact already exist?
            for (ContactEdge ceB = bodyB.ContactList; ceB != null; ceB = ceB.Next)
            {
                if (ceB.Other == bodyA)
                {
                    Fixture fA = ceB.Contact.FixtureA;
                    Fixture fB = ceB.Contact.FixtureB;
                    int iA = ceB.Contact.ChildIndexA;
                    int iB = ceB.Contact.ChildIndexB;

                    if (fA == fixtureA && fB == fixtureB && iA == indexA && iB == indexB)
                    {
                        // A contact already exists.
                        return;
                    }

                    if (fA == fixtureB && fB == fixtureA && iA == indexB && iB == indexA)
                    {
                        // A contact already exists.
                        return;
                    }
                }
            }

            // Does a joint override collision? Is at least one body dynamic?
            if (bodyB.ShouldCollide(bodyA) == false)
                return;

            //Check default filter
            if (ShouldCollide(fixtureA, fixtureB) == false)
                return;

            // Check user filtering.
            if (ContactFilter != null && ContactFilter(fixtureA, fixtureB) == false)
                return;

            //FPE feature: BeforeCollision delegate
            if (fixtureA.BeforeCollision != null && fixtureA.BeforeCollision(fixtureA, fixtureB) == false)
                return;

            if (fixtureB.BeforeCollision != null && fixtureB.BeforeCollision(fixtureB, fixtureA) == false)
                return;

            // Call the factory.
            Contact c = Contact.Create(this, fixtureA, indexA, fixtureB, indexB);

            if (c == null)
                return;

            // Contact creation may swap fixtures.
            fixtureA = c.FixtureA;
            fixtureB = c.FixtureB;
            bodyA = fixtureA.Body;
            bodyB = fixtureB.Body;

            // Insert into the world.
            c.Prev = ContactList;
            c.Next = c.Prev.Next;
            c.Prev.Next = c;
            c.Next.Prev = c;
            ContactCount++;

#if USE_ACTIVE_CONTACT_SET
			ActiveContacts.Add(c);
#endif
            // Connect to island graph.

            // Connect to body A
            c._nodeA.Contact = c;
            c._nodeA.Other = bodyB;

            c._nodeA.Prev = null;
            c._nodeA.Next = bodyA.ContactList;
            if (bodyA.ContactList != null)
            {
                bodyA.ContactList.Prev = c._nodeA;
            }
            bodyA.ContactList = c._nodeA;

            // Connect to body B
            c._nodeB.Contact = c;
            c._nodeB.Other = bodyA;

            c._nodeB.Prev = null;
            c._nodeB.Next = bodyB.ContactList;
            if (bodyB.ContactList != null)
            {
                bodyB.ContactList.Prev = c._nodeB;
            }
            bodyB.ContactList = c._nodeB;

            // Wake up the bodies
            if (fixtureA.IsSensor == false && fixtureB.IsSensor == false)
            {
                bodyA.Awake = true;
                bodyB.Awake = true;
            }
        }

        internal void FindNewContacts()
        {
            BroadPhase.UpdatePairs(OnBroadphaseCollision);
        }

        internal void Destroy(Contact contact)
        {
            Fixture fixtureA = contact.FixtureA;
            Fixture fixtureB = contact.FixtureB;
            Body bodyA = fixtureA.Body;
            Body bodyB = fixtureB.Body;

            if (contact.IsTouching)
            {
                //Report the separation to both participants:
                if (fixtureA != null && fixtureA.OnSeparation != null)
                    fixtureA.OnSeparation(fixtureA, fixtureB, contact);

                //Reverse the order of the reported fixtures. The first fixture is always the one that the
                //user subscribed to.
                if (fixtureB != null && fixtureB.OnSeparation != null)
                    fixtureB.OnSeparation(fixtureB, fixtureA, contact);

                //Report the separation to both bodies:
                if (fixtureA != null && fixtureA.Body != null && fixtureA.Body.onSeparationEventHandler != null)
                    fixtureA.Body.onSeparationEventHandler(fixtureA, fixtureB, contact);

                //Reverse the order of the reported fixtures. The first fixture is always the one that the
                //user subscribed to.
                if (fixtureB != null && fixtureB.Body != null && fixtureB.Body.onSeparationEventHandler != null)
                    fixtureB.Body.onSeparationEventHandler(fixtureB, fixtureA, contact);

                if (EndContact != null)
                    EndContact(contact);
            }

            // Remove from the world.
            contact.Prev.Next = contact.Next;
            contact.Next.Prev = contact.Prev;
            contact.Next = null;
            contact.Prev = null;
            ContactCount--;

            // Remove from body 1
            if (contact._nodeA == bodyA.ContactList)
                bodyA.ContactList = contact._nodeA.Next;
            if (contact._nodeA.Prev != null)
                contact._nodeA.Prev.Next = contact._nodeA.Next;
            if (contact._nodeA.Next != null)
                contact._nodeA.Next.Prev = contact._nodeA.Prev;

            // Remove from body 2
            if (contact._nodeB == bodyB.ContactList)
                bodyB.ContactList = contact._nodeB.Next;
            if (contact._nodeB.Prev != null)
                contact._nodeB.Prev.Next = contact._nodeB.Next;
            if (contact._nodeB.Next != null)
                contact._nodeB.Next.Prev = contact._nodeB.Prev;

#if USE_ACTIVE_CONTACT_SET
			if (ActiveContacts.Contains(contact))
				ActiveContacts.Remove(contact);
#endif
            contact.Destroy();
            
            // Insert into the pool.
            contact.Next = _contactPoolList.Next;
            _contactPoolList.Next = contact;
        }

        internal void Collide()
        {
#if NET40 || NET45 || PORTABLE40 || PORTABLE45 || W10 || W8_1 || WP8_1
            if (this.ContactCount > CollideMultithreadThreshold && System.Environment.ProcessorCount > 1)
            {
                CollideMultiCore();
                return;
            }
#endif

            // Update awake contacts.
#if USE_ACTIVE_CONTACT_SET
            ActiveList.AddRange(ActiveContacts);
            foreach (var tmpc in ActiveList)
            {
                Contact c = tmpc;
#else
            for (Contact c = ContactList.Next; c != ContactList;)
            {
#endif
                Fixture fixtureA = c.FixtureA;
                Fixture fixtureB = c.FixtureB;
                int indexA = c.ChildIndexA;
                int indexB = c.ChildIndexB;
                Body bodyA = fixtureA.Body;
                Body bodyB = fixtureB.Body;

                //Do no try to collide disabled bodies
                if (!bodyA.Enabled || !bodyB.Enabled)
                {
                    c = c.Next;
                    continue;
                }

                // Is this contact flagged for filtering?
                if (c.FilterFlag)
                {
                    // Should these bodies collide?
                    if (bodyB.ShouldCollide(bodyA) == false)
                    {
                        Contact cNuke = c;
                        c = c.Next;
                        Destroy(cNuke);
                        continue;
                    }

                    // Check default filtering
                    if (ShouldCollide(fixtureA, fixtureB) == false)
                    {
                        Contact cNuke = c;
                        c = c.Next;
                        Destroy(cNuke);
                        continue;
                    }

                    // Check user filtering.
                    if (ContactFilter != null && ContactFilter(fixtureA, fixtureB) == false)
                    {
                        Contact cNuke = c;
                        c = c.Next;
                        Destroy(cNuke);
                        continue;
                    }

                    // Clear the filtering flag.
                    c.FilterFlag = false;
                }

                bool activeA = bodyA.Awake && bodyA.BodyType != BodyType.Static;
                bool activeB = bodyB.Awake && bodyB.BodyType != BodyType.Static;

                // At least one body must be awake and it must be dynamic or kinematic.
                if (activeA == false && activeB == false)
                {
#if USE_ACTIVE_CONTACT_SET
					ActiveContacts.Remove(c);
#endif
                    c = c.Next;
                    continue;
                }

                int proxyIdA = fixtureA.Proxies[indexA].ProxyId;
                int proxyIdB = fixtureB.Proxies[indexB].ProxyId;

                bool overlap = BroadPhase.TestOverlap(proxyIdA, proxyIdB);

                // Here we destroy contacts that cease to overlap in the broad-phase.
                if (overlap == false)
                {
                    Contact cNuke = c;
                    c = c.Next;
                    Destroy(cNuke);
                    continue;
                }

                // The contact persists.
                c.Update(this);

                c = c.Next;
            }

#if USE_ACTIVE_CONTACT_SET
			ActiveList.Clear();
#endif
        }

        /// <summary>
        /// A temporary list of contacts to be updated during Collide().
        /// </summary>
        List<Contact> updateList = new List<Contact>();

#if NET40 || NET45 || PORTABLE40 || PORTABLE45 || W10 || W8_1 || WP8_1
        internal void CollideMultiCore()
        {
            int lockOrder = 0;
 
            // Update awake contacts.
#if USE_ACTIVE_CONTACT_SET
            ActiveList.AddRange(ActiveContacts);
            foreach (var tmpc in ActiveList)
            {
                Contact c = tmpc;
#else
            for (Contact c = ContactList.Next; c != ContactList; )
            {
#endif
                Fixture fixtureA = c.FixtureA;
                Fixture fixtureB = c.FixtureB;
                int indexA = c.ChildIndexA;
                int indexB = c.ChildIndexB;
                Body bodyA = fixtureA.Body;
                Body bodyB = fixtureB.Body;

                //Do no try to collide disabled bodies
                if (!bodyA.Enabled || !bodyB.Enabled)
                {
                    c = c.Next;
                    continue;
                }

                // Is this contact flagged for filtering?
                if (c.FilterFlag)
                {
                    // Should these bodies collide?
                    if (bodyB.ShouldCollide(bodyA) == false)
                    {
                        Contact cNuke = c;
                        c = c.Next;
                        Destroy(cNuke);
                        continue;
                    }

                    // Check default filtering
                    if (ShouldCollide(fixtureA, fixtureB) == false)
                    {
                        Contact cNuke = c;
                        c = c.Next;
                        Destroy(cNuke);
                        continue;
                    }

                    // Check user filtering.
                    if (ContactFilter != null && ContactFilter(fixtureA, fixtureB) == false)
                    {
                        Contact cNuke = c;
                        c = c.Next;
                        Destroy(cNuke);
                        continue;
                    }

                    // Clear the filtering flag.
                    c.FilterFlag = false;
                }

                bool activeA = bodyA.Awake && bodyA.BodyType != BodyType.Static;
                bool activeB = bodyB.Awake && bodyB.BodyType != BodyType.Static;

                // At least one body must be awake and it must be dynamic or kinematic.
                if (activeA == false && activeB == false)
                {
#if USE_ACTIVE_CONTACT_SET
					ActiveContacts.Remove(c);
#endif
                    c = c.Next;
                    continue;
                }

                int proxyIdA = fixtureA.Proxies[indexA].ProxyId;
                int proxyIdB = fixtureB.Proxies[indexB].ProxyId;

                bool overlap = BroadPhase.TestOverlap(proxyIdA, proxyIdB);

                // Here we destroy contacts that cease to overlap in the broad-phase.
                if (overlap == false)
                {
                    Contact cNuke = c;
                    c = c.Next;
                    Destroy(cNuke);
                    continue;
                }

                // The contact persists.
                updateList.Add(c);
                // Assign a unique id for lock order
                bodyA._lockOrder = lockOrder++;
                bodyB._lockOrder = lockOrder++;


                c = c.Next;
            }

#if USE_ACTIVE_CONTACT_SET
			ActiveList.Clear();
#endif

            // update contacts
            System.Threading.Tasks.Parallel.ForEach<Contact>(updateList, (c) =>
            {
                // find lower order item
                Fixture fixtureA = c.FixtureA;
                Fixture fixtureB = c.FixtureB;

                // find lower order item
                Body orderedBodyA = fixtureA.Body;
                Body orderedBodyB = fixtureB.Body;
                int idA = orderedBodyA._lockOrder;
                int idB = orderedBodyB._lockOrder;
                if (idA == idB)
                    throw new System.Exception();

                if (idA > idB)
                {
                    orderedBodyA = fixtureB.Body;
                    orderedBodyB = fixtureA.Body;
                }

                // obtain lock
                for (; ; )
                {
                    if (System.Threading.Interlocked.CompareExchange(ref orderedBodyA._lock, 1, 0) == 0)
                    {
                        if (System.Threading.Interlocked.CompareExchange(ref orderedBodyB._lock, 1, 0) == 0)
                            break;
                        System.Threading.Interlocked.Exchange(ref orderedBodyA._lock, 0);
                    }
#if NET40 || NET45
                    System.Threading.Thread.Sleep(0);
#endif
                }

                c.Update(this);

                System.Threading.Interlocked.Exchange(ref orderedBodyB._lock, 0);
                System.Threading.Interlocked.Exchange(ref orderedBodyA._lock, 0);
            });

            updateList.Clear();
        }
#endif

        private static bool ShouldCollide(Fixture fixtureA, Fixture fixtureB)
        {
            if (fixtureA.CollisionGroup != 0 && fixtureA.CollisionGroup == fixtureB.CollisionGroup)
            {
                return (fixtureA.CollisionGroup > 0);
            }

            bool collide = ((fixtureA.CollidesWith & fixtureB.CollisionCategories) != 0) &&
                            ((fixtureB.CollidesWith & fixtureA.CollisionCategories) != 0);

            return collide;
        }

#if USE_ACTIVE_CONTACT_SET
        internal void UpdateActiveContacts(ContactEdge ContactList, bool value)
        {
            if(value)
            {
                for (var contactEdge = ContactList; contactEdge != null; contactEdge = contactEdge.Next)
                {
                    if (!ActiveContacts.Contains(contactEdge.Contact))
                        ActiveContacts.Add(contactEdge.Contact);
                }
            }
            else
            {
                for (var contactEdge = ContactList; contactEdge != null; contactEdge = contactEdge.Next)
                {
                    if (!contactEdge.Other.Awake)
                    {
                        if (ActiveContacts.Contains(contactEdge.Contact))
                            ActiveContacts.Remove(contactEdge.Contact);
                    }
                }
            }
        }
#endif
    }
}