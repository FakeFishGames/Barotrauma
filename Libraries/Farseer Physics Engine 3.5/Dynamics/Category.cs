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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using FarseerPhysics.Collision;
using FarseerPhysics.Collision.Shapes;
using FarseerPhysics.Common;
using FarseerPhysics.Dynamics.Contacts;
using Microsoft.Xna.Framework;

namespace FarseerPhysics.Dynamics
{
    [Flags]
    public enum Category
    {
        None  = 0x00000000,        
        Cat1  = 0x00000001,
        Cat2  = 0x00000002,
        Cat3  = 0x00000004,
        Cat4  = 0x00000008,
        Cat5  = 0x00000010,
        Cat6  = 0x00000020,
        Cat7  = 0x00000040,
        Cat8  = 0x00000080,
        Cat9  = 0x00000100,
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
}
