// Copyright (c) 2017 Kastellanos Nikolaos

/* Original source Farseer Physics Engine:
 * Copyright (c) 2014 Ian Qvist, http://farseerphysics.codeplex.com
 * Microsoft Permissive License (Ms-PL) v1.1
 */

using System;

namespace FarseerPhysics.Common.PhysicsLogic
{
    [Flags]
    public enum ControllerCategory
    {
        None  = 0x00000000,
        Cat01 = 0x00000001,
        Cat02 = 0x00000002,
        Cat03 = 0x00000004,
        Cat04 = 0x00000008,
        Cat05 = 0x00000010,
        Cat06 = 0x00000020,
        Cat07 = 0x00000040,
        Cat08 = 0x00000080,
        Cat09 = 0x00000100,
        Cat10 = 0x00000200,
        Cat11 = 0x00000400,
        Cat12 = 0x00000800,
        Cat13 = 0x00001000,
        Cat14 = 0x00002000,
        Cat15 = 0x00004000,
        Cat16 = 0x00008000,
        Cat17 = 0x00010000,
        Cat18 = 0x00020000,
        Cat19 = 0x00040000,
        Cat20 = 0x00080000,
        Cat21 = 0x00100000,
        Cat22 = 0x00200000,
        Cat23 = 0x00400000,
        Cat24 = 0x00800000,
        Cat25 = 0x01000000,
        Cat26 = 0x02000000,
        Cat27 = 0x04000000,
        Cat28 = 0x08000000,
        Cat29 = 0x10000000,
        Cat30 = 0x20000000,
        Cat31 = 0x40000000,
        All = int.MaxValue,
    }

    public struct ControllerFilter
    {
        public ControllerCategory ControllerCategories;
        
        public ControllerFilter(ControllerCategory controllerCategory)
        {
            this.ControllerCategories = controllerCategory;
        }

        /// <summary>
        /// Ignores the controller. The controller has no effect on this body.
        /// </summary>
        /// <param name="type">The logic type.</param>
        public void IgnoreController(ControllerCategory category)
        {
            ControllerCategories &= ~category;
        }

        /// <summary>
        /// Restore the controller. The controller affects this body.
        /// </summary>
        /// <param name="category">The logic type.</param>
        public void RestoreController(ControllerCategory category)
        {   
            ControllerCategories |= category;
        }

        /// <summary>
        /// Determines whether this body ignores the the specified controller.
        /// </summary>
        /// <param name="category">The logic type.</param>
        /// <returns>
        /// 	<c>true</c> if the body has the specified flag; otherwise, <c>false</c>.
        /// </returns>
        public bool IsControllerIgnored(ControllerCategory category)
        {
            return (ControllerCategories & category) != category;
        }

    }
}
