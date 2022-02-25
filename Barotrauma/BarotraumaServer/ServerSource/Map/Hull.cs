using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.MapCreatures.Behavior;

namespace Barotrauma
{
    partial class Hull : MapEntity, ISerializableEntity, IServerSerializable, IClientSerializable
    {
        private float lastSentVolume, lastSentOxygen, lastSentFireCount;
        private float sendUpdateTimer;

        private bool decalUpdatePending;

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

            sendUpdateTimer -= deltaTime;
            //update client hulls if the amount of water has changed by >10%
            //or if oxygen percentage has changed by 5%
            if (Math.Abs(lastSentVolume - waterVolume) > Volume * 0.1f || Math.Abs(lastSentOxygen - OxygenPercentage) > 5f ||
                lastSentFireCount != FireSources.Count || FireSources.Count > 0 || 
                pendingSectionUpdates.Count > 0 ||
                sendUpdateTimer < -NetConfig.SparseHullUpdateInterval || 
                decalUpdatePending)
            {
                if (sendUpdateTimer < 0.0f)
                {
                    if (decalUpdatePending)
                    {
                        GameMain.NetworkMember.CreateEntityEvent(this, new object[] { false });
                    }
                    if (pendingSectionUpdates.Count > 0)
                    {
                        foreach (int pendingSectionUpdate in pendingSectionUpdates)
                        {
                            GameMain.NetworkMember.CreateEntityEvent(this, new object[] { true, pendingSectionUpdate } );
                        }
                        pendingSectionUpdates.Clear();
                    }
                    else
                    {
                        GameMain.NetworkMember.CreateEntityEvent(this);
                    }

                    lastSentVolume = waterVolume;
                    lastSentOxygen = OxygenPercentage;
                    lastSentFireCount = FireSources.Count;
                    sendUpdateTimer = NetConfig.HullUpdateInterval;
                }
            }
        }

        public void ServerWrite(IWriteMessage message, Client c, object[] extraData = null)
        {
            if (extraData != null && extraData.Length >= 2 && extraData[0] is BallastFloraBehavior behavior && extraData[1] is BallastFloraBehavior.NetworkHeader header)
            {
                message.Write(true);
                message.Write((byte)header);

                switch (header)
                {
                    case BallastFloraBehavior.NetworkHeader.Spawn:
                        behavior.ServerWriteSpawn(message);
                        break;
                    case BallastFloraBehavior.NetworkHeader.Kill:
                    case BallastFloraBehavior.NetworkHeader.Remove:
                        break;
                    case BallastFloraBehavior.NetworkHeader.BranchCreate when extraData.Length >= 4 && extraData[2] is BallastFloraBranch branch && extraData[3] is int parentId:
                        behavior.ServerWriteBranchGrowth(message, branch, parentId);
                        break;
                    case BallastFloraBehavior.NetworkHeader.BranchDamage when extraData.Length >= 4 && extraData[2] is BallastFloraBranch branch:
                        behavior.ServerWriteBranchDamage(message, branch);
                        break;
                    case BallastFloraBehavior.NetworkHeader.BranchRemove when extraData.Length >= 3 && extraData[2] is BallastFloraBranch branch:
                        behavior.ServerWriteBranchRemove(message, branch);
                        break;
                    case BallastFloraBehavior.NetworkHeader.Infect when extraData.Length >= 4 && extraData[2] is UInt16 itemID && extraData[3] is bool infect:
                        BallastFloraBranch infector = null;
                        if (extraData.Length >= 5 && extraData[4] is BallastFloraBranch b) { infector = b; }  
                        behavior.ServerWriteInfect(message, itemID, infect, infector);
                        break;
                }

                message.Write(behavior.PowerConsumptionTimer);
                return;
            }

            message.Write(false); //not a ballast flora update
            message.WriteRangedSingle(MathHelper.Clamp(waterVolume / Volume, 0.0f, 1.5f), 0.0f, 1.5f, 8);
            message.WriteRangedSingle(MathHelper.Clamp(OxygenPercentage, 0.0f, 100.0f), 0.0f, 100.0f, 8);

            message.Write(FireSources.Count > 0);
            if (FireSources.Count > 0)
            {
                message.WriteRangedInteger(Math.Min(FireSources.Count, 16), 0, 16);
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

            message.Write(extraData != null);
            if (extraData != null)
            {
                message.Write((bool)extraData[0]);

                // Section update
                if ((bool)extraData[0])
                {
                    int sectorToUpdate = (int)extraData[1];
                    int start = sectorToUpdate * BackgroundSectionsPerNetworkEvent;
                    int end = Math.Min((sectorToUpdate + 1) * BackgroundSectionsPerNetworkEvent, BackgroundSections.Count - 1);
                    message.WriteRangedInteger(sectorToUpdate, 0, BackgroundSections.Count - 1);
                    for (int i = start; i < end; i++)
                    {
                        message.WriteRangedSingle(BackgroundSections[i].ColorStrength, 0.0f, 1.0f, 8);
                        message.Write(BackgroundSections[i].Color.PackedValue);
                    }
                }
                else // Decal update
                {
                    message.WriteRangedInteger(decals.Count, 0, MaxDecalsPerHull);
                    foreach (Decal decal in decals)
                    {
                        message.Write(decal.Prefab.UintIdentifier);
                        message.Write((byte)decal.SpriteIndex);
                        float normalizedXPos = MathHelper.Clamp(MathUtils.InverseLerp(0.0f, rect.Width, decal.CenterPosition.X), 0.0f, 1.0f);
                        float normalizedYPos = MathHelper.Clamp(MathUtils.InverseLerp(-rect.Height, 0.0f, decal.CenterPosition.Y), 0.0f, 1.0f);
                        message.WriteRangedSingle(normalizedXPos, 0.0f, 1.0f, 8);
                        message.WriteRangedSingle(normalizedYPos, 0.0f, 1.0f, 8);
                        message.WriteRangedSingle(decal.Scale, 0f, 2f, 12);
                    }
                }
            }
        }

        //used when clients use the water/fire console commands or section / decal updates are received
        public void ServerRead(ClientNetObject type, IReadMessage msg, Client c)
        {
            int messageType = msg.ReadRangedInteger(0, 2);
            if (messageType == 0)
            {
                float newWaterVolume = msg.ReadRangedSingle(0.0f, 1.5f, 8) * Volume;

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
            else if (messageType == 1)
            {
                byte decalIndex = msg.ReadByte();
                float decalAlpha = msg.ReadRangedSingle(0.0f, 1.0f, 255);
                if (decalIndex < 0 || decalIndex >= decals.Count) { return; }
                if (c.Character != null && c.Character.AllowInput && c.Character.HeldItems.Any(it => it.GetComponent<Sprayer>() != null))
                {
                    decals[decalIndex].BaseAlpha = decalAlpha;
                }
                decalUpdatePending = true;
            }
            else
            {
                int sectorToUpdate = msg.ReadRangedInteger(0, BackgroundSections.Count - 1);
                int start = sectorToUpdate * BackgroundSectionsPerNetworkEvent;
                int end = Math.Min((sectorToUpdate + 1) * BackgroundSectionsPerNetworkEvent, BackgroundSections.Count - 1);
                for (int i = start; i < end; i++)
                {
                    float colorStrength = msg.ReadRangedSingle(0.0f, 1.0f, 8);
                    Color color = new Color(msg.ReadUInt32());

                    //TODO: verify the client is close enough to this hull to paint it, that the sprayer is functional and that the color matches
                    if (c.Character != null && c.Character.AllowInput && c.Character.HeldItems.Any(it => it.GetComponent<Sprayer>() != null))
                    {
                        BackgroundSections[i].SetColorStrength(colorStrength);
                        BackgroundSections[i].SetColor(color);
                    }
                }
                //add to pending updates to notify other clients as well
                pendingSectionUpdates.Add(sectorToUpdate);
            }            
        }
    }
}
