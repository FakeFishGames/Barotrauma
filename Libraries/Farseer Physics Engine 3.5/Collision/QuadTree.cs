/* Original source Farseer Physics Engine:
 * Copyright (c) 2014 Ian Qvist, http://farseerphysics.codeplex.com
 * Microsoft Permissive License (Ms-PL) v1.1
 */

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace FarseerPhysics.Collision
{
    public class Element<T>
    {
        public QuadTree<T> Parent;
        public AABB Span;
        public T Value;

        public Element(AABB span)
        {
            Span = span;
            Value = default(T);
            Parent = null;
        }
    }

    public class QuadTree<T>
    {
        public int MaxBucket;
        public int MaxDepth;
        public List<Element<T>> Nodes;
        public AABB Span;
        public QuadTree<T>[] SubTrees;

        public QuadTree(AABB span, int maxbucket, int maxdepth)
        {
            Span = span;
            Nodes = new List<Element<T>>();

            MaxBucket = maxbucket;
            MaxDepth = maxdepth;
        }

        public bool IsPartitioned
        {
            get { return SubTrees != null; }
        }

        /// <summary>
        /// returns the quadrant of span that entirely contains test. if none, return 0.
        /// </summary>
        /// <param name="span"></param>
        /// <param name="test"></param>
        /// <returns></returns>
        private int Partition(AABB span, AABB test)
        {
            if (span.Q1.Contains(ref test)) return 1;
            if (span.Q2.Contains(ref test)) return 2;
            if (span.Q3.Contains(ref test)) return 3;
            if (span.Q4.Contains(ref test)) return 4;

            return 0;
        }

        public void AddNode(Element<T> node)
        {
            if (!IsPartitioned)
            {
                if (Nodes.Count >= MaxBucket && MaxDepth > 0) //bin is full and can still subdivide
                {
                    //
                    //partition into quadrants and sort existing nodes amonst quads.
                    //
                    Nodes.Add(node); //treat new node just like other nodes for partitioning

                    SubTrees = new QuadTree<T>[4];
                    SubTrees[0] = new QuadTree<T>(Span.Q1, MaxBucket, MaxDepth - 1);
                    SubTrees[1] = new QuadTree<T>(Span.Q2, MaxBucket, MaxDepth - 1);
                    SubTrees[2] = new QuadTree<T>(Span.Q3, MaxBucket, MaxDepth - 1);
                    SubTrees[3] = new QuadTree<T>(Span.Q4, MaxBucket, MaxDepth - 1);

                    List<Element<T>> remNodes = new List<Element<T>>();
                    //nodes that are not fully contained by any quadrant

                    foreach (Element<T> n in Nodes)
                    {
                        switch (Partition(Span, n.Span))
                        {
                            case 1: //quadrant 1
                                SubTrees[0].AddNode(n);
                                break;
                            case 2:
                                SubTrees[1].AddNode(n);
                                break;
                            case 3:
                                SubTrees[2].AddNode(n);
                                break;
                            case 4:
                                SubTrees[3].AddNode(n);
                                break;
                            default:
                                n.Parent = this;
                                remNodes.Add(n);
                                break;
                        }
                    }

                    Nodes = remNodes;
                }
                else
                {
                    node.Parent = this;
                    Nodes.Add(node);
                    //if bin is not yet full or max depth has been reached, just add the node without subdividing
                }
            }
            else //we already have children nodes
            {
                //
                //add node to specific sub-tree
                //
                switch (Partition(Span, node.Span))
                {
                    case 1: //quadrant 1
                        SubTrees[0].AddNode(node);
                        break;
                    case 2:
                        SubTrees[1].AddNode(node);
                        break;
                    case 3:
                        SubTrees[2].AddNode(node);
                        break;
                    case 4:
                        SubTrees[3].AddNode(node);
                        break;
                    default:
                        node.Parent = this;
                        Nodes.Add(node);
                        break;
                }
            }
        }

        /// <summary>
        /// tests if ray intersects AABB
        /// </summary>
        /// <param name="aabb"></param>
        /// <returns></returns>
        public static bool RayCastAABB(AABB aabb, Vector2 p1, Vector2 p2)
        {
            AABB segmentAABB = new AABB();
            {
                Vector2.Min(ref p1, ref p2, out segmentAABB.LowerBound);
                Vector2.Max(ref p1, ref p2, out segmentAABB.UpperBound);
            }
            if (!AABB.TestOverlap(ref aabb, ref segmentAABB)) return false;

            Vector2 rayDir = p2 - p1;
            Vector2 rayPos = p1;

            Vector2 norm = new Vector2(-rayDir.Y, rayDir.X); //normal to ray
            if (norm.Length() == 0.0f)
                return true; //if ray is just a point, return true (iff point is within aabb, as tested earlier)
            norm.Normalize();

            float dPos = Vector2.Dot(rayPos, norm);

            var verts = aabb.Vertices;
            float d0 = Vector2.Dot(verts[0], norm) - dPos;
            for (int i = 1; i < 4; i++)
            {
                float d = Vector2.Dot(verts[i], norm) - dPos;
                if (Math.Sign(d) != Math.Sign(d0))
                    //return true if the ray splits the vertices (ie: sign of dot products with normal are not all same)
                    return true;
            }

            return false;
        }

        public void QueryAABB(Func<Element<T>, bool> callback, ref AABB searchR)
        {
            Stack<QuadTree<T>> stack = new Stack<QuadTree<T>>();
            stack.Push(this);

            while (stack.Count > 0)
            {
                QuadTree<T> qt = stack.Pop();
                if (!AABB.TestOverlap(ref searchR, ref qt.Span))
                    continue;

                foreach (Element<T> n in qt.Nodes)
                    if (AABB.TestOverlap(ref searchR, ref n.Span))
                    {
                        if (!callback(n)) return;
                    }

                if (qt.IsPartitioned)
                    foreach (QuadTree<T> st in qt.SubTrees)
                        stack.Push(st);
            }
        }

        public void RayCast(Func<RayCastInput, Element<T>, float> callback, ref RayCastInput input)
        {
            Stack<QuadTree<T>> stack = new Stack<QuadTree<T>>();
            stack.Push(this);

            float maxFraction = input.MaxFraction;
            Vector2 p1 = input.Point1;
            Vector2 p2 = p1 + (input.Point2 - input.Point1) * maxFraction;

            while (stack.Count > 0)
            {
                QuadTree<T> qt = stack.Pop();

                if (!RayCastAABB(qt.Span, p1, p2))
                    continue;

                foreach (Element<T> n in qt.Nodes)
                {
                    if (!RayCastAABB(n.Span, p1, p2))
                        continue;

                    RayCastInput subInput;
                    subInput.Point1 = input.Point1;
                    subInput.Point2 = input.Point2;
                    subInput.MaxFraction = maxFraction;

                    float value = callback(subInput, n);
                    if (value == 0.0f)
                        return; // the client has terminated the raycast.

                    if (value <= 0.0f)
                        continue;

                    maxFraction = value;
                    p2 = p1 + (input.Point2 - input.Point1) * maxFraction; //update segment endpoint
                }
                if (qt.IsPartitioned)
                    foreach (QuadTree<T> st in qt.SubTrees)
                        stack.Push(st);
            }
        }

        public void GetAllNodesR(ref List<Element<T>> nodes)
        {
            nodes.AddRange(Nodes);

            if (IsPartitioned)
                foreach (QuadTree<T> st in SubTrees) st.GetAllNodesR(ref nodes);
        }

        public void RemoveNode(Element<T> node)
        {
            node.Parent.Nodes.Remove(node);
        }

        public void Reconstruct()
        {
            List<Element<T>> allNodes = new List<Element<T>>();
            GetAllNodesR(ref allNodes);

            Clear();

            foreach (var node in allNodes)
                AddNode(node);
        }

        public void Clear()
        {
            Nodes.Clear();
            SubTrees = null;
        }
    }
}