using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Linq;

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
                    (c.Character != null && Vector2.DistanceSquared(c.Character.WorldPosition, WorldPosition) < hullUpdateDistanceSqr) ||
                    (c.SpectatePos != null && Vector2.DistanceSquared(c.SpectatePos.Value, WorldPosition) < hullUpdateDistanceSqr)) )
            {
                return;
            }

            statusUpdateTimer += deltaTime;
            decalUpdateTimer += deltaTime;
            backgroundSectionUpdateTimer += deltaTime;

            //update client hulls if the amount of water has changed by >10%
            //or if oxygen percentage has changed by 5%
            bool shouldSendStatusUpdate =
                (Math.Abs(lastSentVolume - waterVolume) > Volume * 0.1f
                    || Math.Abs(lastSentOxygen - OxygenPercentage) > 5f
                    || lastSentFireCount != FireSources.Count)
                && (statusUpdateTimer > NetConfig.HullUpdateInterval);

            //force an update every 5 seconds even if nothing's changed (in case a client's gotten out of sync somehow)
            if (shouldSendStatusUpdate || statusUpdateTimer > NetConfig.SparseHullUpdateInterval)
            {
                GameMain.NetworkMember.CreateEntityEvent(this, new StatusEventData());                
                lastSentVolume = waterVolume;
                lastSentOxygen = OxygenPercentage;
                lastSentFireCount = FireSources.Count;
                statusUpdateTimer = 0;
            }
            if (decalUpdatePending && decalUpdateTimer > NetConfig.HullUpdateInterval)
            {
                GameMain.NetworkMember.CreateEntityEvent(this, new DecalEventData());

                decalUpdateTimer = 0;
                decalUpdatePending = false;
            }
            if (pendingSectorUpdates.Count > 0 && backgroundSectionUpdateTimer > NetConfig.HullUpdateInterval)
            {
                foreach (int pendingSectorUpdate in pendingSectorUpdates)
                {
                    GameMain.NetworkMember.CreateEntityEvent(this, new BackgroundSectionsEventData(pendingSectorUpdate));
                }
                
                backgroundSectionUpdateTimer = 0;
                pendingSectorUpdates.Clear();
            }
        }

        public void ForceStatusUpdate()
        {
            statusUpdateTimer = NetConfig.SparseHullUpdateInterval;
        }


        public void CreateStatusEvent()
        {
            GameMain.NetworkMember?.CreateEntityEvent(this, new StatusEventData());
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
                        msg.WriteUInt32(decal.Prefab.UintIdentifier);
                        msg.WriteByte((byte)decal.SpriteIndex);
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
                        !c.PermittedConsoleCommands.Any(command => command.Names.Contains("fire".ToIdentifier()) || command.Names.Contains("editfire".ToIdentifier())))
                    {
                        return;
                    }

                    WaterVolume = newWaterVolume;

                    if (newFireSources.Length != FireSources.Count)
                    {
                        //number of fire sources has changed, force a network update
                        ForceStatusUpdate();
                    }

                    for (int i = 0; i < newFireSources.Length; i++)
                    {
                        Vector2 pos = newFireSources[i].Position;
                        float size = newFireSources[i].Size;

                        var newFire = i < FireSources.Count ?
                            FireSources[i] :
                            new FireSource(Submarine == null ? pos : pos + Submarine.Position, sourceCharacter: null, isNetworkMessage: true);
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
                    bool addPendingSectorUpdate = false;

                    SharedBackgroundSectionRead(
                        msg,
                        bsnu =>
                        {
                            int i = bsnu.SectionIndex;
                            Color color = bsnu.Color;
                            float colorStrength = bsnu.ColorStrength;

                            if (c.Character is not { AllowInput: true }) { return; }

                            //ideally the server would just run the painting logic the same way as clients instead of relying on the clients setting colors on the hull,
                            //but that's non-trivial because the server doesn't know the client's exact cursor position, just the direction they're aiming at
                            //and we want the painting to be precise, lag shouldn't cause the paint to end up in the wrong place, etc.
                            //but now that clients set the colors themselves, we need to do some sanity checks:

                            var sprayer = c.Character.HeldItems
                                .Select(it => it.GetComponent<Sprayer>())
                                .FirstOrDefault(component => component != null);
                            if (sprayer == null) { return; }

                            Item liquidItem = sprayer.LiquidContainer?.Inventory?.FirstOrDefault();
                            if (liquidItem == null) { return; }

                            if (!sprayer.LiquidColors.TryGetValue(liquidItem.Prefab.Identifier, out Color paintColor)) { return; }

                            bool isCleaning = paintColor.A == 0;

                            var backgroundSectionPos = GetBackgroundSectionWorldPos(BackgroundSections[i]);
                            //rough distance check to disallow painting from very far away
                            //(slightly longer range than the normal range of the sprayer to give the client some leeway)
                            if (Vector2.Distance(backgroundSectionPos, sprayer.Item.WorldPosition) > sprayer.Range * 1.1f)
                            {
                                return;
                            }

                            //if we get to this point (client can paint this section), let's sync the changes
                            //the color change below may fail if the color is out of sync client-side, even if the client isn't doing anything malicious,
                            //in which case we want to get the client back in sync
                            addPendingSectorUpdate = true;

                            if (isCleaning)
                            {
                                //if we're cleaning, strength of the color must go down
                                if (colorStrength >= BackgroundSections[i].ColorStrength) { return; }
                            }
                            else
                            {
                                Vector3 colorChange = color.ToVector3() - BackgroundSections[i].Color.ToVector3();
                                Vector3 expectedColorChange = paintColor.ToVector3() - BackgroundSections[i].Color.ToVector3();

                                //color should be going towards the color of the paint, if it's not, don't allow changing it
                                if (Math.Sign(colorChange.X) != Math.Sign(expectedColorChange.X) ||
                                    Math.Sign(colorChange.Y) != Math.Sign(expectedColorChange.Y) ||
                                    Math.Sign(colorChange.Z) != Math.Sign(expectedColorChange.Z))
                                {
                                    return;
                                }
                                BackgroundSections[i].SetColor(color);
                            }
                            BackgroundSections[i].SetColorStrength(colorStrength);
                        },
                        out int sectorToUpdate);

                    if (addPendingSectorUpdate)
                    {
                        RefreshAveragePaintedColor();
                        //add to pending updates to notify other clients as well
                        pendingSectorUpdates.Add(sectorToUpdate);
                    }
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
