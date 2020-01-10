/* Original source Farseer Physics Engine:
 * Copyright (c) 2014 Ian Qvist, http://farseerphysics.codeplex.com
 * Microsoft Permissive License (Ms-PL) v1.1
 */

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using FarseerPhysics.Dynamics;

namespace FarseerPhysics.Collision
{
    public class QuadTreeBroadPhase : IBroadPhase
    {
        private const int TreeUpdateThresh = 10000;
        private int _currId;
        private Dictionary<int, Element<FixtureProxy>> _idRegister;
        private List<Element<FixtureProxy>> _moveBuffer;
        private List<Pair> _pairBuffer;
        private QuadTree<FixtureProxy> _quadTree;
        private int _treeMoveNum;

        /// <summary>
        /// Creates a new quad tree broadphase with the specified span.
        /// </summary>
        /// <param name="span">the maximum span of the tree (world size)</param>
        public QuadTreeBroadPhase(AABB span)
        {
            _quadTree = new QuadTree<FixtureProxy>(span, 5, 10);
            _idRegister = new Dictionary<int, Element<FixtureProxy>>();
            _moveBuffer = new List<Element<FixtureProxy>>();
            _pairBuffer = new List<Pair>();
        }

        #region IBroadPhase Members

        ///<summary>
        /// The number of proxies
        ///</summary>
        public int ProxyCount
        {
            get { return _idRegister.Count; }
        }

        public void GetFatAABB(int proxyID, out AABB aabb)
        {
            if (_idRegister.ContainsKey(proxyID))
                aabb = _idRegister[proxyID].Span;
            else
                throw new KeyNotFoundException("proxyID not found in register");
        }

        public void UpdatePairs(BroadphaseDelegate callback)
        {
            _pairBuffer.Clear();
            foreach (Element<FixtureProxy> qtnode in _moveBuffer)
            {
                // Query tree, create pairs and add them pair buffer.
                Query(proxyID => PairBufferQueryCallback(proxyID, qtnode.Value.ProxyId), ref qtnode.Span);
            }
            _moveBuffer.Clear();

            // Sort the pair buffer to expose duplicates.
            _pairBuffer.Sort();

            // Send the pairs back to the client.
            int i = 0;
            while (i < _pairBuffer.Count)
            {
                Pair primaryPair = _pairBuffer[i];

                callback(primaryPair.ProxyIdA, primaryPair.ProxyIdB);
                ++i;

                // Skip any duplicate pairs.
                while (i < _pairBuffer.Count && _pairBuffer[i].ProxyIdA == primaryPair.ProxyIdA &&
                       _pairBuffer[i].ProxyIdB == primaryPair.ProxyIdB)
                    ++i;
            }
        }

        /// <summary>
        /// Test overlap of fat AABBs.
        /// </summary>
        /// <param name="proxyIdA">The proxy id A.</param>
        /// <param name="proxyIdB">The proxy id B.</param>
        /// <returns></returns>
        public bool TestOverlap(int proxyIdA, int proxyIdB)
        {
            AABB aabb1;
            AABB aabb2;
            GetFatAABB(proxyIdA, out aabb1);
            GetFatAABB(proxyIdB, out aabb2);
            return AABB.TestOverlap(ref aabb1, ref aabb2);
        }

        public int AddProxy(ref AABB uaabb)
        {
            int proxyId = _currId++;
            AABB aabb = Fatten(ref uaabb);
            Element<FixtureProxy> qtnode = new Element<FixtureProxy>(aabb);

            _idRegister.Add(proxyId, qtnode);
            _quadTree.AddNode(qtnode);

            return proxyId;
        }

        public void RemoveProxy(int proxyId)
        {
            if (_idRegister.ContainsKey(proxyId))
            {
                Element<FixtureProxy> qtnode = _idRegister[proxyId];
                UnbufferMove(qtnode);
                _idRegister.Remove(proxyId);
                _quadTree.RemoveNode(qtnode);
            }
            else
                throw new KeyNotFoundException("proxyID not found in register");
        }

        public void MoveProxy(int proxyId, ref AABB aabb, Vector2 displacement)
        {
            AABB fatAABB;
            GetFatAABB(proxyId, out fatAABB);

            //exit if movement is within fat aabb
            if (fatAABB.Contains(ref aabb))
                return;

            // Extend AABB.
            AABB b = aabb;
            Vector2 r = new Vector2(Settings.AABBExtension, Settings.AABBExtension);
            b.LowerBound = b.LowerBound - r;
            b.UpperBound = b.UpperBound + r;

            // Predict AABB displacement.
            Vector2 d = Settings.AABBMultiplier * displacement;

            if (d.X < 0.0f)
                b.LowerBound.X += d.X;
            else
                b.UpperBound.X += d.X;

            if (d.Y < 0.0f)
                b.LowerBound.Y += d.Y;
            else
                b.UpperBound.Y += d.Y;


            Element<FixtureProxy> qtnode = _idRegister[proxyId];
            qtnode.Value.AABB = b; //not neccesary for QTree, but might be accessed externally
            qtnode.Span = b;

            ReinsertNode(qtnode);

            BufferMove(qtnode);
        }

        public void SetProxy(int proxyId, ref FixtureProxy proxy)
        {
            _idRegister[proxyId].Value = proxy;
        }

        public FixtureProxy GetProxy(int proxyId)
        {
            if (_idRegister.ContainsKey(proxyId))
                return _idRegister[proxyId].Value;

            throw new KeyNotFoundException("proxyID not found in register");
        }

        public void TouchProxy(int proxyId)
        {
            if (_idRegister.ContainsKey(proxyId))
                BufferMove(_idRegister[proxyId]);
            else
                throw new KeyNotFoundException("proxyID not found in register");
        }

        public void Query(Func<int, bool> callback, ref AABB query)
        {
            _quadTree.QueryAABB(TransformPredicate(callback), ref query);
        }

        public void RayCast(Func<RayCastInput, FixtureProxy, float> callback, ref RayCastInput input, Category collisionCategory = Category.All)
        {
            _quadTree.RayCast(TransformRayCallback(callback), ref input, collisionCategory);
        }

        public void ShiftOrigin(Vector2 newOrigin)
        {
            //TODO
        }

        #endregion

        private AABB Fatten(ref AABB aabb)
        {
            Vector2 r = new Vector2(Settings.AABBExtension, Settings.AABBExtension);
            return new AABB(aabb.LowerBound - r, aabb.UpperBound + r);
        }

        private Func<Element<FixtureProxy>, bool> TransformPredicate(Func<int, bool> idPredicate)
        {
            Func<Element<FixtureProxy>, bool> qtPred = qtnode => idPredicate(qtnode.Value.ProxyId);
            return qtPred;
        }

        private Func<RayCastInput, Element<FixtureProxy>, float> TransformRayCallback(Func<RayCastInput, FixtureProxy, float> callback)
        {
            Func<RayCastInput, Element<FixtureProxy>, float> newCallback =
                (input, qtnode) => callback(input, qtnode.Value);
            return newCallback;
        }

        private bool PairBufferQueryCallback(int proxyId, int baseId)
        {
            // A proxy cannot form a pair with itself.
            if (proxyId == baseId)
                return true;

            Pair p = new Pair();
            p.ProxyIdA = Math.Min(proxyId, baseId);
            p.ProxyIdB = Math.Max(proxyId, baseId);
            _pairBuffer.Add(p);

            return true;
        }

        private void ReconstructTree()
        {
            //this is faster than _quadTree.Reconstruct(), since the quadtree method runs a recusive query to find all nodes.
            _quadTree.Clear();
            foreach (Element<FixtureProxy> elem in _idRegister.Values)
                _quadTree.AddNode(elem);
        }

        private void ReinsertNode(Element<FixtureProxy> qtnode)
        {
            _quadTree.RemoveNode(qtnode);
            _quadTree.AddNode(qtnode);

            if (++_treeMoveNum > TreeUpdateThresh)
            {
                ReconstructTree();
                _treeMoveNum = 0;
            }
        }

        private void BufferMove(Element<FixtureProxy> proxy)
        {
            _moveBuffer.Add(proxy);
        }

        private void UnbufferMove(Element<FixtureProxy> proxy)
        {
            _moveBuffer.Remove(proxy);
        }
    }
}