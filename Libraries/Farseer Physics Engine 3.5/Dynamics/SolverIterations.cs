// Copyright (c) 2018 Kastellanos Nikolaos

namespace FarseerPhysics.Dynamics
{
    public struct SolverIterations
    {
        /// <summary>The number of velocity iterations used in the solver.</summary>
        public int VelocityIterations;

        /// <summary>The number of position iterations used in the solver.</summary>
        public int PositionIterations;

        /// <summary>The number of velocity iterations in the TOI solver</summary>
        public int TOIVelocityIterations;

        /// <summary>The number of position iterations in the TOI solver</summary>
        public int TOIPositionIterations;
    }
}
