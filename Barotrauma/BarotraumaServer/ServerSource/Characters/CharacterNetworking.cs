using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Character
    {
        public string OwnerClientEndPoint;
        public string OwnerClientName;
        public bool ClientDisconnected;
        public float KillDisconnectedTimer;

        private bool networkUpdateSent;

        private double LastInputTime;

        public bool HealthUpdatePending;

        public float GetPositionUpdateInterval(Client recipient)
        {
            if (!Enabled) { return 1000.0f; }

            Vector2 comparePosition = recipient.SpectatePos == null ? recipient.Character.WorldPosition : recipient.SpectatePos.Value;

            float distance = Vector2.Distance(comparePosition, WorldPosition);
            if (recipient.Character?.ViewTarget != null)
            {
                distance = Math.Min(distance, Vector2.Distance(recipient.Character.ViewTarget.WorldPosition, WorldPosition));
            }

            float priority = 1.0f - MathUtils.InverseLerp(
                NetConfig.HighPrioCharacterPositionUpdateDistance, 
                NetConfig.LowPrioCharacterPositionUpdateDistance,
                distance);

            float interval = MathHelper.Lerp(
                NetConfig.LowPrioCharacterPositionUpdateInterval, 
                NetConfig.HighPrioCharacterPositionUpdateInterval,
                priority);

            if (IsDead)
            {
                interval = Math.Max(interval * 2, 0.1f);
            }

            return interval;
        }

        partial void UpdateNetInput()
        {
            if (!(this is AICharacter) || IsRemotePlayer)
            {
                if (!CanMove)
                {
                    AnimController.Frozen = false;
                    if (memInput.Count > 0)
                    {
                        prevDequeuedInput = dequeuedInput;
                        dequeuedInput = memInput[memInput.Count - 1].states & InputNetFlags.Ragdoll;
                        memInput.RemoveAt(memInput.Count - 1);
                    }
                }
                else if (memInput.Count == 0)
                {
                    AnimController.Frozen = true;
                    if (Timing.TotalTime > LastInputTime + 0.5)
                    {
                        //no inputs have been received in 0.5 seconds, reset input
                        //(if there's a temporary network hiccup that prevents us from receiving inputs, we assume the inputs haven't changed,
                        //but if it takes too long, for example due to a client crashing/disconnecting, we don't want to keep the character
                        //firing a welding tool or whatever else they were doing until the kill disconnect timer kicks in)
                        prevDequeuedInput = dequeuedInput =
                            dequeuedInput.HasFlag(InputNetFlags.FacingLeft) ? InputNetFlags.FacingLeft : InputNetFlags.None;
                    }
                }
                else
                {
                    AnimController.Frozen = false;
                    prevDequeuedInput = dequeuedInput;

                    LastProcessedID = memInput[memInput.Count - 1].networkUpdateID;
                    dequeuedInput = memInput[memInput.Count - 1].states;

                    double aimAngle = ((double)memInput[memInput.Count - 1].intAim / 65535.0) * 2.0 * Math.PI;
                    cursorPosition = AimRefPosition + new Vector2((float)Math.Cos(aimAngle), (float)Math.Sin(aimAngle)) * 500.0f;

                    //reset focus when attempting to use/select something
                    if (memInput[memInput.Count - 1].states.HasFlag(InputNetFlags.Use) ||
                        memInput[memInput.Count - 1].states.HasFlag(InputNetFlags.Select) ||
                        memInput[memInput.Count - 1].states.HasFlag(InputNetFlags.Deselect) ||
                        memInput[memInput.Count - 1].states.HasFlag(InputNetFlags.Health) ||
                        memInput[memInput.Count - 1].states.HasFlag(InputNetFlags.Grab))
                    {
                        focusedItem = null;
                        FocusedCharacter = null;
                    }
                    var closestEntity = FindEntityByID(memInput[memInput.Count - 1].interact);
                    if (closestEntity is Item item)
                    {
                        if (CanInteractWith(item))
                        {
                            focusedItem = item;
                            FocusedCharacter = null;
                        }
                        else
                        {
                            //failed to interact with the item 
                            // -> correct the position and the state of the Holdable component (in case the item was deattached client-side)
                            item.PositionUpdateInterval = 0.0f;
                            var holdable = item.GetComponent<Items.Components.Holdable>();
                            holdable?.Item?.CreateServerEvent(holdable);
                        }
                    }
                    else if (closestEntity is Character character)
                    {
                        if (CanInteractWith(character, maxDist: 250.0f))
                        {
                            FocusedCharacter = character;
                            focusedItem = null;
                        }
                    }

                    memInput.RemoveAt(memInput.Count - 1);

                    TransformCursorPos();

                    if ((dequeuedInput == InputNetFlags.None || dequeuedInput == InputNetFlags.FacingLeft) && Math.Abs(AnimController.Collider.LinearVelocity.X) < 0.005f && Math.Abs(AnimController.Collider.LinearVelocity.Y) < 0.2f)
                    {
                        while (memInput.Count > 5 && memInput[memInput.Count - 1].states == dequeuedInput)
                        {
                            //remove inputs where the player is not moving at all
                            //helps the server catch up, shouldn't affect final position
                            LastProcessedID = memInput[memInput.Count - 1].networkUpdateID;
                            memInput.RemoveAt(memInput.Count - 1);
                        }
                    }
                }
            }
            AnimController.Frozen = false;

            if (networkUpdateSent)
            {
                foreach (Key key in keys)
                {
                    key.DequeueHit();
                    key.DequeueHeld();
                }

                networkUpdateSent = false;
            }
        }

        public void ServerReadInput(IReadMessage msg, Client c)
        {
            if (c.Character != this)
            {
#if DEBUG
                DebugConsole.Log("Received a character update message from a client who's not controlling the character");
#endif
                return;
            }

            UInt16 networkUpdateID = msg.ReadUInt16();
            byte inputCount = msg.ReadByte();

            if (AllowInput) { Enabled = true; }

            for (int i = 0; i < inputCount; i++)
            {
                InputNetFlags newInput = (InputNetFlags)msg.ReadRangedInteger(0, (int)InputNetFlags.MaxVal);
                UInt16 newAim = 0;
                UInt16 newInteract = 0;

                if (newInput != InputNetFlags.None && newInput != InputNetFlags.FacingLeft)
                {
                    c.KickAFKTimer = 0.0f;
                }
                else if (AnimController.Dir < 0.0f != newInput.HasFlag(InputNetFlags.FacingLeft))
                {
                    //character changed the direction they're facing
                    c.KickAFKTimer = 0.0f;
                }

                newAim = msg.ReadUInt16();
                if (newInput.HasFlag(InputNetFlags.Select) ||
                    newInput.HasFlag(InputNetFlags.Deselect) ||
                    newInput.HasFlag(InputNetFlags.Use) ||
                    newInput.HasFlag(InputNetFlags.Health) ||
                    newInput.HasFlag(InputNetFlags.Grab))
                {
                    newInteract = msg.ReadUInt16();
                }

                if (NetIdUtils.IdMoreRecent((ushort)(networkUpdateID - i), LastNetworkUpdateID) && (i < 60))
                {
                    if ((i > 0 && memInput[i - 1].intAim != newAim))
                    {
                        c.KickAFKTimer = 0.0f;
                    }
                    NetInputMem newMem = new NetInputMem
                    {
                        states = newInput,
                        intAim = newAim,
                        interact = newInteract,
                        networkUpdateID = (ushort)(networkUpdateID - i)
                    };
                    memInput.Insert(i, newMem);
                    LastInputTime = Timing.TotalTime;
                }
            }

            if (NetIdUtils.IdMoreRecent(networkUpdateID, LastNetworkUpdateID))
            {
                LastNetworkUpdateID = networkUpdateID;
            }
            else if (NetIdUtils.Difference(networkUpdateID, LastNetworkUpdateID) > 500)
            {
#if DEBUG || UNSTABLE
                DebugConsole.AddWarning($"Large disrepancy between a client character's network update ID server-side and client-side (client: {networkUpdateID}, server: {LastNetworkUpdateID}). Resetting the ID.");
#endif
                LastNetworkUpdateID = networkUpdateID;
            }
            if (memInput.Count > 60)
            {
                //deleting inputs from the queue here means the server is way behind and data needs to be dropped
                //we'll make the server drop down to 30 inputs for good measure
                memInput.RemoveRange(30, memInput.Count - 30);
            }
        }

        public virtual void ServerEventRead(IReadMessage msg, Client c)
        {
            EventType eventType = (EventType)msg.ReadRangedInteger((int)EventType.MinValue, (int)EventType.MaxValue);
            switch (eventType)
            {
                case EventType.InventoryState:
                    Inventory.ServerEventRead(msg, c);
                    break;
                case EventType.Treatment:
                    bool doingCPR = msg.ReadBoolean();
                    if (c.Character != this)
                    {
#if DEBUG
                        DebugConsole.Log("Received a character update message from a client who's not controlling the character");
#endif
                        return;
                    }

                    AnimController.Anim = doingCPR ? AnimController.Animation.CPR : AnimController.Animation.None;
                    break;
                case EventType.Status:
                    if (c.Character != this)
                    {
#if DEBUG
                        DebugConsole.Log("Received a character update message from a client who's not controlling the character");
#endif
                        return;
                    }

                    if (IsIncapacitated)
                    {
                        var causeOfDeath = CharacterHealth.GetCauseOfDeath();
                        Kill(causeOfDeath.type, causeOfDeath.affliction);
                    }
                    break;
                case EventType.UpdateTalents:
                    if (c.Character != this)
                    {
#if DEBUG
                        DebugConsole.Log("Received a character update message from a client who's not controlling the character");
#endif
                        return;
                    }

                    // get the full list of talents from the player, only give the ones
                    // that are not already given (or otherwise not viable)
                    ushort talentCount = msg.ReadUInt16();
                    List<Identifier> talentSelection = new List<Identifier>();
                    for (int i = 0; i < talentCount; i++)
                    {
                        UInt32 talentIdentifier = msg.ReadUInt32();
                        var prefab = TalentPrefab.TalentPrefabs.Find(p => p.UintIdentifier == talentIdentifier);
                        if (prefab == null) { continue; }

                        if (TalentTree.IsViableTalentForCharacter(this, prefab.Identifier, talentSelection))
                        {
                            GiveTalent(prefab.Identifier);
                            talentSelection.Add(prefab.Identifier);
                        }
                    }
                    if (talentSelection.Count != talentCount)
                    {
                        DebugConsole.AddWarning($"Failed to unlock talents: the amount of unlocked talents doesn't match (client: {talentCount}, server: {talentSelection.Count})");
                    }
                    break;
            }
        }

        public void ServerWritePosition(IWriteMessage msg, Client c)
        {
            msg.Write(ID);

            IWriteMessage tempBuffer = new WriteOnlyMessage();

            if (this == c.Character)
            {
                tempBuffer.Write(true);
                if (LastNetworkUpdateID < memInput.Count + 1)
                {
                    tempBuffer.Write((UInt16)0);
                }
                else
                {
                    tempBuffer.Write((UInt16)(LastNetworkUpdateID - memInput.Count - 1));
                }
            }
            else
            {
                tempBuffer.Write(false);

                bool aiming = false;
                bool use = false;
                bool attack = false;
                bool shoot = false;

                if (IsRemotePlayer)
                {
                    aiming  = dequeuedInput.HasFlag(InputNetFlags.Aim);
                    use     = dequeuedInput.HasFlag(InputNetFlags.Use);
                    attack  = dequeuedInput.HasFlag(InputNetFlags.Attack);
                    shoot   = dequeuedInput.HasFlag(InputNetFlags.Shoot);
                }
                else if (keys != null)
                {
                    aiming  = keys[(int)InputType.Aim].GetHeldQueue;
                    use     = keys[(int)InputType.Use].GetHeldQueue;
                    attack  = keys[(int)InputType.Attack].GetHeldQueue;
                    shoot   = keys[(int)InputType.Shoot].GetHeldQueue;
                    networkUpdateSent = true;
                }

                tempBuffer.Write(aiming);
                tempBuffer.Write(shoot);
                tempBuffer.Write(use);
                if (AnimController is HumanoidAnimController)
                {
                    tempBuffer.Write(((HumanoidAnimController)AnimController).Crouching);
                }
                tempBuffer.Write(attack);

                Vector2 relativeCursorPos = cursorPosition - AimRefPosition;
                tempBuffer.Write((UInt16)(65535.0 * Math.Atan2(relativeCursorPos.Y, relativeCursorPos.X) / (2.0 * Math.PI)));

                tempBuffer.Write(IsRagdolled || Stun > 0.0f || IsDead || IsIncapacitated);

                tempBuffer.Write(AnimController.Dir > 0.0f);
            }

            if (SelectedCharacter != null || HasSelectedAnyItem)
            {
                tempBuffer.Write(true);
                tempBuffer.Write(SelectedCharacter != null ? SelectedCharacter.ID : NullEntityID);
                tempBuffer.Write(SelectedItem != null ? SelectedItem.ID : NullEntityID);
                tempBuffer.Write(SelectedSecondaryItem != null ? SelectedSecondaryItem.ID : NullEntityID);
                if (SelectedCharacter != null)
                {
                    tempBuffer.Write(AnimController.Anim == AnimController.Animation.CPR);
                }
            }
            else
            {
                tempBuffer.Write(false);
            }

            tempBuffer.Write(SimPosition.X);
            tempBuffer.Write(SimPosition.Y);
            float MaxVel = NetConfig.MaxPhysicsBodyVelocity;
            AnimController.Collider.LinearVelocity = new Vector2(
                MathHelper.Clamp(AnimController.Collider.LinearVelocity.X, -MaxVel, MaxVel),
                MathHelper.Clamp(AnimController.Collider.LinearVelocity.Y, -MaxVel, MaxVel));
            tempBuffer.WriteRangedSingle(AnimController.Collider.LinearVelocity.X, -MaxVel, MaxVel, 12);
            tempBuffer.WriteRangedSingle(AnimController.Collider.LinearVelocity.Y, -MaxVel, MaxVel, 12);

            bool fixedRotation = AnimController.Collider.FarseerBody.FixedRotation || !AnimController.Collider.PhysEnabled;
            tempBuffer.Write(fixedRotation);
            if (!fixedRotation)
            {
                tempBuffer.Write(AnimController.Collider.Rotation);
                float MaxAngularVel = NetConfig.MaxPhysicsBodyAngularVelocity;
                AnimController.Collider.AngularVelocity = NetConfig.Quantize(AnimController.Collider.AngularVelocity, -MaxAngularVel, MaxAngularVel, 8);
                tempBuffer.WriteRangedSingle(MathHelper.Clamp(AnimController.Collider.AngularVelocity, -MaxAngularVel, MaxAngularVel), -MaxAngularVel, MaxAngularVel, 8);
            }

            bool writeStatus = healthUpdateTimer <= 0.0f;
            tempBuffer.Write(writeStatus);
            if (writeStatus)
            {
                WriteStatus(tempBuffer);
                AIController?.ServerWrite(tempBuffer);
                HealthUpdatePending = false;
            }

            tempBuffer.WritePadBits();

            msg.WriteVariableUInt32((uint)tempBuffer.LengthBytes);
            msg.Write(tempBuffer.Buffer, 0, tempBuffer.LengthBytes);
        }

        public virtual void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            if (!(extraData is IEventData eventData)) { throw new Exception($"Malformed character event: expected {nameof(Character)}.{nameof(IEventData)}, got {extraData?.GetType().Name ?? "[NULL]"}"); }

            msg.WriteRangedInteger((int)eventData.EventType, (int)EventType.MinValue, (int)EventType.MaxValue);
            switch (eventData)
            {
                case InventoryStateEventData _:
                    msg.Write(GameMain.Server.EntityEventManager.Events.Last()?.ID ?? (ushort)0);
                    Inventory.ServerEventWrite(msg, c);
                    break;
                case ControlEventData controlEventData:
                    Client owner = controlEventData.Owner;
                    msg.Write(owner == c && owner.Character == this);
                    msg.Write(owner != null && owner.Character == this && GameMain.Server.ConnectedClients.Contains(owner) ? owner.ID : (byte)0);
                    break;
                case CharacterStatusEventData statusEventData:
                    WriteStatus(msg, statusEventData.ForceAfflictionData);
                    break;
                case UpdateSkillsEventData _:
                    if (Info?.Job == null)
                    {
                        msg.Write((byte)0);
                    }
                    else
                    {
                        var skills = Info.Job.GetSkills();
                        msg.Write((byte)skills.Count());
                        foreach (Skill skill in skills)
                        {
                            msg.Write(skill.Identifier);
                            msg.Write(skill.Level);
                        }
                    }
                    break;
                case IAttackEventData attackEventData:
                    {
                        int attackLimbIndex = Removed ? -1 : Array.IndexOf(AnimController.Limbs, attackEventData.AttackLimb);
                        ushort targetEntityId = 0;
                        int targetLimbIndex = -1;
                        if (attackEventData.TargetEntity is Entity { Removed: false } targetEntity)
                        {
                            targetEntityId = targetEntity.ID;
                            if (targetEntity is Character { AnimController: { Limbs: var targetLimbsArray } })
                            {
                                targetLimbIndex = targetLimbsArray.IndexOf(attackEventData.TargetLimb);
                            }
                        }
                        msg.Write((byte)(attackLimbIndex < 0 ? 255 : attackLimbIndex));
                        msg.Write((ushort)targetEntityId);
                        msg.Write((byte)(targetLimbIndex < 0 ? 255 : targetLimbIndex));
                        msg.Write(attackEventData.TargetSimPos.X);
                        msg.Write(attackEventData.TargetSimPos.Y);
                    }
                    break;
                case AssignCampaignInteractionEventData _:
                    msg.Write((byte)CampaignInteractionType);
                    msg.Write(RequireConsciousnessForCustomInteract);
                    break;
                case ObjectiveManagerStateEventData objectiveManagerStateEventData:
                    AIObjectiveManager.ObjectiveType type = objectiveManagerStateEventData.ObjectiveType;
                    msg.WriteRangedInteger((int)type, (int)AIObjectiveManager.ObjectiveType.MinValue, (int)AIObjectiveManager.ObjectiveType.MaxValue);
                    if (!(AIController is HumanAIController controller))
                    {
                        msg.Write(false);
                        break;
                    }
                    if (type == AIObjectiveManager.ObjectiveType.Order)
                    {
                        var currentOrderInfo = controller.ObjectiveManager.GetCurrentOrderInfo();
                        bool validOrder = currentOrderInfo != null;
                        msg.Write(validOrder);
                        if (!validOrder) { break; }
                        var orderPrefab = currentOrderInfo.Prefab;
                        msg.Write(orderPrefab.UintIdentifier);
                        if (!orderPrefab.HasOptions) { break; }
                        int optionIndex = orderPrefab.AllOptions.IndexOf(currentOrderInfo.Option);
                        if (optionIndex == -1)
                        {
                            DebugConsole.AddWarning($"Error while writing order data. Order option \"{currentOrderInfo.Option}\" not found in the order prefab \"{orderPrefab.Name}\".");
                        }
                        msg.WriteRangedInteger(optionIndex, -1, orderPrefab.AllOptions.Length);
                    }
                    else if (type == AIObjectiveManager.ObjectiveType.Objective)
                    {
                        var objective = controller.ObjectiveManager.CurrentObjective;
                        bool validObjective = objective?.Identifier is { IsEmpty: false };
                        msg.Write(validObjective);
                        if (!validObjective) { break; }
                        msg.Write(objective.Identifier);
                        msg.Write(objective.Option);
                        UInt16 targetEntityId = 0;
                        if (objective is AIObjectiveOperateItem operateObjective && operateObjective.OperateTarget != null)
                        {
                            targetEntityId = operateObjective.OperateTarget.ID;
                        }
                        msg.Write(targetEntityId);
                    }
                    break;
                case TeamChangeEventData _:
                    msg.Write((byte)TeamID);
                    break;
                case AddToCrewEventData addToCrewEventData:
                    msg.Write((byte)addToCrewEventData.TeamType); // team id
                    ushort[] inventoryItemIDs = addToCrewEventData.InventoryItems.Select(item => item.ID).ToArray();
                    msg.Write((ushort)inventoryItemIDs.Length);
                    for (int i = 0; i < inventoryItemIDs.Length; i++)
                    {
                        msg.Write(inventoryItemIDs[i]);
                    }
                    break;
                case UpdateExperienceEventData _:
                    msg.Write(Info.ExperiencePoints);
                    break;
                case UpdateTalentsEventData _:
                    msg.Write((ushort)characterTalents.Count);
                    foreach (var unlockedTalent in characterTalents)
                    {
                        msg.Write(unlockedTalent.AddedThisRound);
                        msg.Write(unlockedTalent.Prefab.UintIdentifier);
                    }
                    break;
                case UpdateMoneyEventData _:
                    msg.Write(GameMain.GameSession.Campaign.GetWallet(c).Balance);
                    break;
                case UpdatePermanentStatsEventData updatePermanentStatsEventData:
                    StatTypes statType = updatePermanentStatsEventData.StatType;
                    if (Info == null)
                    {
                        msg.Write((byte)0);
                        msg.Write((byte)0);
                    }
                    else if (!Info.SavedStatValues.ContainsKey(statType))
                    {
                        msg.Write((byte)0);
                        msg.Write((byte)statType);
                    }
                    else
                    {
                        msg.Write((byte)Info.SavedStatValues[statType].Count);
                        msg.Write((byte)statType);
                        foreach (var savedStatValue in Info.SavedStatValues[statType])
                        {
                            msg.Write(savedStatValue.StatIdentifier);
                            msg.Write(savedStatValue.StatValue);
                            msg.Write(savedStatValue.RemoveOnDeath);
                        }
                    }
                    break;
                default:
                    throw new Exception($"Malformed character event: did not expect {eventData.GetType().Name}");
            }
        }

        private readonly List<int> severedJointIndices = new List<int>();

        /// <param name="forceAfflictionData">Normally full affliction data is not written for dead characters, this can be used to force them to be written</param>
        private void WriteStatus(IWriteMessage msg, bool forceAfflictionData = false)
        {
            msg.Write(IsDead);
            if (IsDead)
            {
                msg.WriteRangedInteger((int)CauseOfDeath.Type, 0, Enum.GetValues(typeof(CauseOfDeathType)).Length - 1);
                if (CauseOfDeath.Type == CauseOfDeathType.Affliction)
                {
                    msg.Write(CauseOfDeath.Affliction.UintIdentifier);
                }
                msg.Write(forceAfflictionData);
                if (forceAfflictionData)
                {
                    CharacterHealth.ServerWrite(msg);
                }
            }
            else
            {
                CharacterHealth.ServerWrite(msg);
            }
            if (AnimController?.LimbJoints == null)
            {
                //0 limbs severed
                msg.Write((byte)0);
            }
            else
            {
                severedJointIndices.Clear();
                for (int i = 0; i < AnimController.LimbJoints.Length; i++)
                {
                    if (AnimController.LimbJoints[i] != null && AnimController.LimbJoints[i].IsSevered)
                    {
                        severedJointIndices.Add(i);
                    }
                }
                msg.Write((byte)severedJointIndices.Count);
                foreach (int jointIndex in severedJointIndices)
                {
                    msg.Write((byte)jointIndex);
                }
            }
        }

        public void WriteSpawnData(IWriteMessage msg, UInt16 entityId, bool restrictMessageSize)
        {
            if (GameMain.Server == null) { return; }
            
            int initialMsgLength = msg.LengthBytes;

            msg.Write(Info == null);
            msg.Write(entityId);
            msg.Write(SpeciesName);
            msg.Write(Seed);

            if (Removed)
            {
                msg.Write(0.0f);
                msg.Write(0.0f);
            }
            else
            {
                msg.Write(WorldPosition.X);
                msg.Write(WorldPosition.Y);
            }

            msg.Write(Enabled);

            //character with no characterinfo (e.g. some monster)
            if (Info == null)
            {
                TryWriteStatus(msg);
                return;
            }

            Client ownerClient = GameMain.Server.ConnectedClients.Find(c => c.Character == this);
            if (ownerClient != null)
            {
                msg.Write(true);
                msg.Write(ownerClient.ID);
            }
            else if (GameMain.Server.Character == this)
            {
                msg.Write(true);
                msg.Write((byte)0);
            }
            else
            {
                msg.Write(false);
            }
            msg.Write(HumanPrefabHealthMultiplier);
            msg.Write(Wallet.Balance);
            msg.WriteRangedInteger(Wallet.RewardDistribution, 0, 100);
            msg.Write((byte)TeamID);
            msg.Write(this is AICharacter);
            msg.Write(info.SpeciesName);
            int msgLengthBeforeInfo = msg.LengthBytes;
            info.ServerWrite(msg);
            int infoLength = msg.LengthBytes - msgLengthBeforeInfo;

            msg.Write((byte)CampaignInteractionType);
            if (CampaignInteractionType == CampaignMode.InteractionType.Store)
            {
                msg.Write(MerchantIdentifier);
            }

            int msgLengthBeforeOrders = msg.LengthBytes;
            // Current orders
            msg.Write((byte)info.CurrentOrders.Count(o => o != null));
            foreach (var orderInfo in info.CurrentOrders)
            {
                if (orderInfo == null) { continue; }
                msg.Write(orderInfo.Prefab.UintIdentifier);
                msg.Write(orderInfo.TargetEntity == null ? (UInt16)0 : orderInfo.TargetEntity.ID);
                var hasOrderGiver = orderInfo.OrderGiver != null;
                msg.Write(hasOrderGiver);
                if (hasOrderGiver) { msg.Write(orderInfo.OrderGiver.ID); }
                msg.Write((byte)(orderInfo.Option == Identifier.Empty ? 0 : orderInfo.Prefab.Options.IndexOf(orderInfo.Option)));
                msg.Write((byte)orderInfo.ManualPriority);
                var hasTargetPosition = orderInfo.TargetPosition != null;
                msg.Write(hasTargetPosition);
                if (hasTargetPosition)
                {
                    msg.Write(orderInfo.TargetPosition.Position.X);
                    msg.Write(orderInfo.TargetPosition.Position.Y);
                    msg.Write(orderInfo.TargetPosition.Hull == null ? (UInt16)0 : orderInfo.TargetPosition.Hull.ID);
                }
            }
            int ordersLength = msg.LengthBytes - msgLengthBeforeOrders;

            if (msg.LengthBytes - initialMsgLength >= 255 && restrictMessageSize)
            {
                string errorMsg = $"Error when writing character spawn data for  \"{Name}\": data exceeded 255 bytes (info: {infoLength}, orders: {ordersLength}, total: {msg.LengthBytes - initialMsgLength})";
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("Character.WriteSpawnData:TooMuchData", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
            }

            TryWriteStatus(msg);

            void TryWriteStatus(IWriteMessage msg)
            {
                int msgLengthBeforeStatus = msg.LengthBytes - initialMsgLength;

                var tempBuffer = new ReadWriteMessage();
                WriteStatus(tempBuffer, forceAfflictionData: true);
                if (msgLengthBeforeStatus + tempBuffer.LengthBytes >= 255 && restrictMessageSize)
                { 
                    msg.Write(false);
                    if (msgLengthBeforeStatus < 255)
                    {
                        string errorMsg = $"Error when writing character spawn data for \"{Name}\": status data caused the length of the message to exceed 255 bytes ({msgLengthBeforeStatus} + {tempBuffer.LengthBytes})";
                        DebugConsole.ThrowError(errorMsg);
                        GameAnalyticsManager.AddErrorEventOnce("Character.WriteSpawnData:TooMuchDataForStatus", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                    }
                }
                else
                {
                    msg.Write(true);
                    WriteStatus(msg, forceAfflictionData: true);
                }
            }

            DebugConsole.Log("Character spawn message length: " + (msg.LengthBytes - initialMsgLength));
        }
    }
}
