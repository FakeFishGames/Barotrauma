using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;
using Barotrauma.MapCreatures.Behavior;

namespace Barotrauma
{
    partial class Hull : MapEntity, ISerializableEntity, IServerSerializable, IClientSerializable
    {
        private float lastSentVolume;
        private float lastSentOxygen;
        private int lastSentFireCount;

        private float statusUpdateTimer;
        private float decalUpdateTimer;
        private float backgroundSectionUpdateTimer;

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

            statusUpdateTimer -= deltaTime;
            decalUpdateTimer -= deltaTime;
            backgroundSectionUpdateTimer -= deltaTime;

            //update client hulls if the amount of water has changed by >10%
            //or if oxygen percentage has changed by 5%
            bool shouldSendStatusUpdate =
                (Math.Abs(lastSentVolume - waterVolume) > Volume * 0.1f
                    || Math.Abs(lastSentOxygen - OxygenPercentage) > 5f
                    || lastSentFireCount != FireSources.Count)
                && statusUpdateTimer <= 0.0f;

            if (shouldSendStatusUpdate)
            {
                GameMain.NetworkMember.CreateEntityEvent(this, new StatusEventData());
                
                lastSentVolume = waterVolume;
                lastSentOxygen = OxygenPercentage;
                lastSentFireCount = FireSources.Count;
                
                statusUpdateTimer = NetConfig.SparseHullUpdateInterval;
            }
            if (decalUpdatePending && decalUpdateTimer <= 0.0f)
            {
                GameMain.NetworkMember.CreateEntityEvent(this, new DecalEventData());

                decalUpdateTimer = NetConfig.HullUpdateInterval;
                decalUpdatePending = false;
            }
            if (pendingSectionUpdates.Count > 0 && backgroundSectionUpdateTimer <= 0.0f)
            {
                foreach (int pendingSectionUpdate in pendingSectionUpdates)
                {
                    GameMain.NetworkMember.CreateEntityEvent(this, new BackgroundSectionsEventData(pendingSectionUpdate));
                }
                
                backgroundSectionUpdateTimer = NetConfig.HullUpdateInterval;
                pendingSectionUpdates.Clear();
            }
        }

        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            if (!(extraData is IEventData eventData)) { throw new Exception($"Malformed hull event: expected {nameof(Hull)}.{nameof(IEventData)}"); }
            msg.WriteRangedInteger((int)eventData.EventType, (int)EventType.MinValue, (int)EventType.MaxValue);

            switch (eventData)
            {
                case StatusEventData statusEventData:
                    msg.WriteRangedSingle(MathHelper.Clamp(OxygenPercentage, 0.0f, 100.0f), 0.0f, 100.0f, 8);
                    SharedStatusWrite(msg);
                    break;
                case BackgroundSectionsEventData backgroundSectionsEventData:
                    SharedBackgroundSectionsWrite(msg, backgroundSectionsEventData);
                    break;
                case DecalEventData decalEventData:
                    msg.WriteRangedInteger(decals.Count, 0, MaxDecalsPerHull);
                    foreach (Decal decal in decals)
                    {
                        msg.Write(decal.Prefab.UintIdentifier);
                        msg.Write((byte)decal.SpriteIndex);
                        float normalizedXPos = MathHelper.Clamp(MathUtils.InverseLerp(0.0f, rect.Width, decal.CenterPosition.X), 0.0f, 1.0f);
                        float normalizedYPos = MathHelper.Clamp(MathUtils.InverseLerp(-rect.Height, 0.0f, decal.CenterPosition.Y), 0.0f, 1.0f);
                        msg.WriteRangedSingle(normalizedXPos, 0.0f, 1.0f, 8);
                        msg.WriteRangedSingle(normalizedYPos, 0.0f, 1.0f, 8);
                        msg.WriteRangedSingle(decal.Scale, 0f, 2f, 12);
                    }
                    break;
                case BallastFloraEventData ballastFloraEventData:
                    ballastFloraEventData.Behavior.ServerWrite(msg, ballastFloraEventData.SubEventData);
                    break;
                default:
                    throw new Exception($"Malformed hull event: did not expect {eventData.GetType().Name}");
            }
        }

        //used when clients use the water/fire console commands or section / decal updates are received
        public void ServerEventRead(IReadMessage msg, Client c)
        {
            EventType eventType = (EventType)msg.ReadRangedInteger((int)EventType.MinValue, (int)EventType.MaxValue);
            switch (eventType)
            {
                case EventType.Status:
                    SharedStatusRead(
                        msg,
                        out float newWaterVolume,
                        out NetworkFireSource[] newFireSources);

                    if (!c.HasPermission(ClientPermissions.ConsoleCommands) ||
                        !c.PermittedConsoleCommands.Any(command => command.names.Contains("fire") || command.names.Contains("editfire")))
                    {
                        return;
                    }

                    WaterVolume = newWaterVolume;

                    for (int i = 0; i < newFireSources.Length; i++)
                    {
                        Vector2 pos = newFireSources[i].Position;
                        float size = newFireSources[i].Size;

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

                    for (int i = FireSources.Count - 1; i >= newFireSources.Length; i--)
                    {
                        FireSources[i].Remove();
                        if (i < FireSources.Count)
                        {
                            FireSources.RemoveAt(i);
                        }
                    }
                    break;
                case EventType.BackgroundSections:
                    SharedBackgroundSectionRead(
                        msg,
                        bsnu =>
                        {
                            int i = bsnu.SectionIndex;
                            Color color = bsnu.Color;
                            float colorStrength = bsnu.ColorStrength;

                            #warning TODO: verify the client is close enough to this hull to paint it, that the sprayer is functional and that the color matches
                            if (!(c.Character is { AllowInput: true })) { return; }
                            if (c.Character.HeldItems.All(it => it.GetComponent<Sprayer>() == null)) { return; }

                            BackgroundSections[i].SetColorStrength(colorStrength);
                            BackgroundSections[i].SetColor(color);
                        },
                        out int sectorToUpdate);
                    //add to pending updates to notify other clients as well
                    pendingSectionUpdates.Add(sectorToUpdate);
                    break;
                case EventType.Decal:
                    byte decalIndex = msg.ReadByte();
                    float decalAlpha = msg.ReadRangedSingle(0.0f, 1.0f, 255);
                    if (decalIndex < 0 || decalIndex >= decals.Count) { return; }
                    if (c.Character != null && c.Character.AllowInput && c.Character.HeldItems.Any(it => it.GetComponent<Sprayer>() != null))
                    {
                        decals[decalIndex].BaseAlpha = decalAlpha;
                    }
                    decalUpdatePending = true;
                    break;
                default:
                    throw new Exception($"Malformed incoming hull event: {eventType} is not a supported event type");
            }         
        }
    }
}
