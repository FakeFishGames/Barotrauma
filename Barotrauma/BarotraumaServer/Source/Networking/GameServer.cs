using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using RestSharp;
using Barotrauma.Items.Components;

namespace Barotrauma.Networking
{
    partial class GameServer : NetworkMember
    {
        void InitProjSpecific()
        {
            //do nothing
        }

        void InitUPnP()
        {
            server.UPnP.ForwardPort(config.Port, "barotrauma");
        }

        bool DiscoveringUPnP()
        {
            return server.UPnP.Status == UPnPStatus.Discovering;
        }

        void FinishUPnP()
        {
            //do nothing
        }
    }
}
