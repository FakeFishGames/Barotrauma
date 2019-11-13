/* Original source Farseer Physics Engine:
 * Copyright (c) 2014 Ian Qvist, http://farseerphysics.codeplex.com
 * Microsoft Permissive License (Ms-PL) v1.1
 */

using FarseerPhysics.Dynamics;

namespace FarseerPhysics.Common.PhysicsLogic
{
    public abstract class PhysicsLogic : FilterData
    {
        public ControllerCategory ControllerCategory = ControllerCategory.Cat01;

        public World World { get; internal set; }

        public PhysicsLogic(World world)
        {
            World = world;
        }
        public override bool IsActiveOn(Body body)
        {
            if (body.ControllerFilter.IsControllerIgnored(ControllerCategory))
                return false;

            return base.IsActiveOn(body);
        }
        
    }
}