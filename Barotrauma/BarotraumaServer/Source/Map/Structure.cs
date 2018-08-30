using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using FarseerPhysics.Factories;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class Structure : MapEntity, IDamageable, IServerSerializable, ISerializableEntity
    {
        partial void AdjustKarma(IDamageable attacker, float amount)
        {
            if (GameMain.Server != null)
            {
                if (Submarine == null) return;
                if (attacker == null) return;
                if (attacker is Character)
                {
                    Character attackerCharacter = attacker as Character;
                    Barotrauma.Networking.Client attackerClient = GameMain.Server.ConnectedClients.Find(c => c.Character == attackerCharacter);
                    if (attackerClient != null)
                    {
                        if (attackerCharacter.TeamID == Submarine.TeamID)
                        {
                            attackerClient.Karma -= amount * 0.001f;
                        }
                    }
                }
            }
        }
    }
}
