/* Original source Farseer Physics Engine:
 * Copyright (c) 2014 Ian Qvist, http://farseerphysics.codeplex.com
 * Microsoft Permissive License (Ms-PL) v1.1
 */

namespace FarseerPhysics.Common.Decomposition.Seidel
{
    internal class Sink : Node
    {
        public Trapezoid Trapezoid;

        private Sink(Trapezoid trapezoid)
            : base(null, null)
        {
            Trapezoid = trapezoid;
            trapezoid.Sink = this;
        }

        public static Sink Isink(Trapezoid trapezoid)
        {
            if (trapezoid.Sink == null)
                return new Sink(trapezoid);

            return trapezoid.Sink;
        }

        public override Sink Locate(Edge edge)
        {
            return this;
        }
    }
}