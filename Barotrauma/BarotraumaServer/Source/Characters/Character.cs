using Barotrauma.Networking;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class Character : Entity, IDamageable, ISerializableEntity, IClientSerializable, IServerSerializable
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

        partial void InitProjSpecific(XDocument doc)
        {
            keys = null;
        }
    }
}
