using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Joints;
using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    partial class Character : Entity, IDamageable, IPropertyObject, IClientSerializable, IServerSerializable
    {
        //the Character that the player is currently controlling
        private const Character controlled = null;

        public static Character Controlled
        {
            get { return controlled; }
            set
            {
                //do nothing
            }
        }

        private void InitProjSpecific(XDocument doc)
        {
            keys = null;
        }

        private void UpdateControlled(float deltaTime)
        {
            //do nothing
        }

        private void ImplodeFX()
        {
            //do nothing
        }
    }
}
