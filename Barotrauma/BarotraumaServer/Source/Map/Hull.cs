using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Hull : MapEntity, ISerializableEntity, IServerSerializable, IClientSerializable
    {
        private float lastSentVolume, lastSentOxygen, lastSentFireCount;
        private float sendUpdateTimer;

        public override bool IsMouseOn(Vector2 position)
        {
            return false;
        }

        partial void UpdateProjSpecific(float deltaTime, Camera cam)
        {
            if (IdFreed) { return; }

            //don't create updates if all clients are very far from the hull
            float hullUpdateDistanceSqr = NetConfig.HullUpdateDistance * NetConfig.HullUpdateDistance;
            if (!GameMain.Server.ConnectedClients.Any(c => 
                c.Character != null && 
                Vector2.DistanceSquared(c.Character.WorldPosition, WorldPosition) < hullUpdateDistanceSqr))
            {
                return;
            }

            //update client hulls if the amount of water has changed by >10%
            //or if oxygen percentage has changed by 5%
            if (Math.Abs(lastSentVolume - waterVolume) > Volume * 0.1f ||
                Math.Abs(lastSentOxygen - OxygenPercentage) > 5f ||
                lastSentFireCount != FireSources.Count ||
                FireSources.Count > 0)
            {
                sendUpdateTimer -= deltaTime;
                if (sendUpdateTimer < 0.0f)
                {
                    GameMain.NetworkMember.CreateEntityEvent(this);
                    lastSentVolume = waterVolume;
                    lastSentOxygen = OxygenPercentage;
                    lastSentFireCount = FireSources.Count;
                    sendUpdateTimer = NetConfig.HullUpdateInterval;
                }
            }
        }

        public void ServerWrite(IWriteMessage message, Client c, object[] extraData = null)
        {
            message.WriteRangedSingle(MathHelper.Clamp(waterVolume / Volume, 0.0f, 1.5f), 0.0f, 1.5f, 8);
            message.WriteRangedSingle(MathHelper.Clamp(OxygenPercentage, 0.0f, 100.0f), 0.0f, 100.0f, 8);

            message.Write(FireSources.Count > 0);
            if (FireSources.Count > 0)
            {
                message.WriteRangedInteger(0, 16, Math.Min(FireSources.Count, 16));
                for (int i = 0; i < Math.Min(FireSources.Count, 16); i++)
                {
                    var fireSource = FireSources[i];
                    Vector2 normalizedPos = new Vector2(
                        (fireSource.Position.X - rect.X) / rect.Width,
                        (fireSource.Position.Y - (rect.Y - rect.Height)) / rect.Height);

                    message.WriteRangedSingle(MathHelper.Clamp(normalizedPos.X, 0.0f, 1.0f), 0.0f, 1.0f, 8);
                    message.WriteRangedSingle(MathHelper.Clamp(normalizedPos.Y, 0.0f, 1.0f), 0.0f, 1.0f, 8);
                    message.WriteRangedSingle(MathHelper.Clamp(fireSource.Size.X / rect.Width, 0.0f, 1.0f), 0, 1.0f, 8);
                }
            }
        }

        //used when clients use the water/fire console commands
        public void ServerRead(ClientNetObject type, IReadMessage msg, Client c)
        {
            float newWaterVolume  = msg.ReadRangedSingle(0.0f, 1.5f, 8) * Volume;

            bool hasFireSources = msg.ReadBoolean();
            int fireSourceCount = 0;
            List<Vector3> newFireSources = new List<Vector3>();
            if (hasFireSources)
            {
                fireSourceCount = msg.ReadRangedInteger(0, 16);
                for (int i = 0; i < fireSourceCount; i++)
                {
                    newFireSources.Add(new Vector3(
                        MathHelper.Clamp(msg.ReadRangedSingle(0.0f, 1.0f, 8), 0.05f, 0.95f),
                        MathHelper.Clamp(msg.ReadRangedSingle(0.0f, 1.0f, 8), 0.05f, 0.95f),
                        msg.ReadRangedSingle(0.0f, 1.0f, 8)));                    
                }
            }

            if (!c.HasPermission(ClientPermissions.ConsoleCommands) || 
                !c.PermittedConsoleCommands.Any(command => command.names.Contains("fire") || command.names.Contains("editfire")))
            {
                return;
            }

            WaterVolume = newWaterVolume;

            for (int i = 0; i < fireSourceCount; i++)
            {
                Vector2 pos = new Vector2(
                    rect.X + rect.Width * newFireSources[i].X,
                    rect.Y - rect.Height + (rect.Height * newFireSources[i].Y));
                float size = newFireSources[i].Z * rect.Width;

                var newFire = i < FireSources.Count ?
                    FireSources[i] :
                    new FireSource(Submarine == null ? pos : pos + Submarine.Position, null, true);
                newFire.Position = pos;
                newFire.Size = new Vector2(size, newFire.Size.Y);

                //ignore if the fire wasn't added to this room (invalid position)?
                if (!FireSources.Contains(newFire))
                {
                    newFire.Remove();
                    continue;
                }
            }

            for (int i = FireSources.Count - 1; i >= fireSourceCount; i--)
            {
                FireSources[i].Remove();
                if (i < FireSources.Count)
                {
                    FireSources.RemoveAt(i);
                }
            }
        }
    }
}
