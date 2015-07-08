using System;
using FarseerPhysics.Dynamics;

namespace FarseerPhysics.Common.PhysicsLogic
{
    [Flags]
    public enum PhysicsLogicType
    {
        Explosion = (1 << 0)
    }

    public struct PhysicsLogicFilter
    {
        public PhysicsLogicType ControllerIgnores;

        /// <summary>
        /// Ignores the controller. The controller has no effect on this body.
        /// </summary>
        /// <param name="type">The logic type.</param>
        public void IgnorePhysicsLogic(PhysicsLogicType type)
        {
            ControllerIgnores |= type;
        }

        /// <summary>
        /// Restore the controller. The controller affects this body.
        /// </summary>
        /// <param name="type">The logic type.</param>
        public void RestorePhysicsLogic(PhysicsLogicType type)
        {
            ControllerIgnores &= ~type;
        }

        /// <summary>
        /// Determines whether this body ignores the the specified controller.
        /// </summary>
        /// <param name="type">The logic type.</param>
        /// <returns>
        /// 	<c>true</c> if the body has the specified flag; otherwise, <c>false</c>.
        /// </returns>
        public bool IsPhysicsLogicIgnored(PhysicsLogicType type)
        {
            return (ControllerIgnores & type) == type;
        }
    }

    public abstract class PhysicsLogic : FilterData
    {
        private PhysicsLogicType _type;
        public World World;

        public override bool IsActiveOn(Body body)
        {
            if (body.PhysicsLogicFilter.IsPhysicsLogicIgnored(_type))
                return false;

            return base.IsActiveOn(body);
        }

        public PhysicsLogic(World world, PhysicsLogicType type)
        {
            _type = type;
            World = world;
        }
    }
}