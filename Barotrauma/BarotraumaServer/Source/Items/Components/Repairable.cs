using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Barotrauma.Items.Components
{
    partial class Repairable : ItemComponent, IServerSerializable, IClientSerializable
    {
        void InitProjSpecific()
        {
            //let the clients know the initial deterioration delay
            item.CreateServerEvent(this);
        }
    }
}
