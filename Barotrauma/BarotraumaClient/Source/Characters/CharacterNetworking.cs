using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class Character
    {
        public virtual void ClientWrite(NetBuffer msg, object[] extraData = null)
        {
            if (GameMain.Server != null) return;

            if (extraData != null)
            {
                switch ((NetEntityEvent.Type)extraData[0])
                {
                    case NetEntityEvent.Type.InventoryState:
                        msg.WriteRangedInteger(0, 3, 0);
                        Inventory.ClientWrite(msg, extraData);
                        break;
                    case NetEntityEvent.Type.Treatment:
                        msg.WriteRangedInteger(0, 3, 1);
                        msg.Write(AnimController.Anim == AnimController.Animation.CPR);
                        break;
                    case NetEntityEvent.Type.Status:
                        msg.WriteRangedInteger(0, 3, 2);
                        break;
                }
            }
            else
            {
                msg.Write((byte)ClientNetObject.CHARACTER_INPUT);

                if (memInput.Count > 60)
                {
                    memInput.RemoveRange(60, memInput.Count - 60);
                }

                msg.Write(LastNetworkUpdateID);
                byte inputCount = Math.Min((byte)memInput.Count, (byte)60);
                msg.Write(inputCount);
                for (int i = 0; i < inputCount; i++)
                {
                    msg.WriteRangedInteger(0, (int)InputNetFlags.MaxVal, (int)memInput[i].states);
                    if (memInput[i].states.HasFlag(InputNetFlags.Aim))
                    {
                        msg.Write(memInput[i].intAim);
                    }
                    if (memInput[i].states.HasFlag(InputNetFlags.Select) || 
                        memInput[i].states.HasFlag(InputNetFlags.Use) || 
                        memInput[i].states.HasFlag(InputNetFlags.Health) ||
                        memInput[i].states.HasFlag(InputNetFlags.Grab))
                    {
                        msg.Write(memInput[i].interact);
                    }
                }
            }
            msg.WritePadBits();
        }

        public virtual void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            if (GameMain.Server != null) return;

            switch (type)
            {
                case ServerNetObject.ENTITY_POSITION:
                    bool facingRight = AnimController.Dir > 0.0f;

                    lastRecvPositionUpdateTime = (float)NetTime.Now;

                    AnimController.Frozen = false;
                    Enabled = true;

                    UInt16 networkUpdateID = 0;
                    if (msg.ReadBoolean())
                    {
                        networkUpdateID = msg.ReadUInt16();
                    }
                    else
                    {
                        bool aimInput = msg.ReadBoolean();
                        keys[(int)InputType.Aim].Held = aimInput;
                        keys[(int)InputType.Aim].SetState(false, aimInput);

                        bool useInput = msg.ReadBoolean();
                        keys[(int)InputType.Use].Held = useInput;
                        keys[(int)InputType.Use].SetState(false, useInput);

                        if (AnimController is HumanoidAnimController)
                        {
                            bool crouching = msg.ReadBoolean();
                            keys[(int)InputType.Crouch].Held = crouching;
                            keys[(int)InputType.Crouch].SetState(false, crouching);
                        }

                        bool hasAttackLimb = msg.ReadBoolean();
                        if (hasAttackLimb)
                        {
                            bool attackInput = msg.ReadBoolean();
                            keys[(int)InputType.Attack].Held = attackInput;
                            keys[(int)InputType.Attack].SetState(false, attackInput);
                        }

                        if (aimInput)
                        {
                            double aimAngle = ((double)msg.ReadUInt16() / 65535.0) * 2.0 * Math.PI;
                            cursorPosition = AimRefPosition + new Vector2((float)Math.Cos(aimAngle), (float)Math.Sin(aimAngle)) * 60.0f;
                            TransformCursorPos();
                        }
                        bool ragdollInput = msg.ReadBoolean();
                        keys[(int)InputType.Ragdoll].Held = ragdollInput;
                        keys[(int)InputType.Ragdoll].SetState(false, ragdollInput);

                        facingRight = msg.ReadBoolean();
                    }

                    bool entitySelected = msg.ReadBoolean();
                    Entity selectedEntity = null;

                    AnimController.Animation animation = AnimController.Animation.None;
                    if (entitySelected)
                    {
                        ushort entityID = msg.ReadUInt16();
                        selectedEntity = FindEntityByID(entityID);
                        if (selectedEntity is Character)
                        {
                            bool doingCpr = msg.ReadBoolean();
                            if (doingCpr && SelectedCharacter != null)
                            {
                                animation = AnimController.Animation.CPR;
                            }
                        }
                    }

                    Vector2 pos = new Vector2(
                        msg.ReadFloat(),
                        msg.ReadFloat());

                    float rotation = msg.ReadFloat();

                    ReadStatus(msg);

                    msg.ReadPadBits();

                    int index = 0;
                    if (GameMain.NetworkMember.Character == this && AllowInput)
                    {
                        var posInfo = new CharacterStateInfo(pos, rotation, networkUpdateID, facingRight ? Direction.Right : Direction.Left, selectedEntity, animation);
                        while (index < memState.Count && NetIdUtils.IdMoreRecent(posInfo.ID, memState[index].ID))
                            index++;

                        memState.Insert(index, posInfo);
                    }
                    else
                    {
                        var posInfo = new CharacterStateInfo(pos, rotation, sendingTime, facingRight ? Direction.Right : Direction.Left, selectedEntity, animation);
                        while (index < memState.Count && posInfo.Timestamp > memState[index].Timestamp)
                            index++;

                        memState.Insert(index, posInfo);
                    }

                    break;
                case ServerNetObject.ENTITY_EVENT:

                    int eventType = msg.ReadRangedInteger(0, 3);
                    switch (eventType)
                    {
                        case 0:
                            if (Inventory == null)
                            {
                                string errorMsg = "Received an inventory update message for an entity with no inventory (" + Name + ")";
                                DebugConsole.ThrowError(errorMsg);
                                GameAnalyticsManager.AddErrorEventOnce("CharacterNetworking.ClientRead:NoInventory" + ID, GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                            }
                            else
                            {
                                Inventory.ClientRead(type, msg, sendingTime);
                            }
                            break;
                        case 1:
                            byte ownerID = msg.ReadByte();
                            ResetNetState();
                            if (ownerID == GameMain.Client.ID)
                            {
                                if (controlled != null)
                                {
                                    LastNetworkUpdateID = controlled.LastNetworkUpdateID;
                                }

                                Controlled = this;
                                IsRemotePlayer = false;
                                GameMain.Client.HasSpawned = true;
                                GameMain.Client.Character = this;
                                GameMain.LightManager.LosEnabled = true;
                            }
                            else if (controlled == this)
                            {
                                Controlled = null;
                                IsRemotePlayer = ownerID > 0;
                            }
                            break;
                        case 2:
                            ReadStatus(msg);
                            break;
                        case 3:
                            int skillCount = msg.ReadByte();
                            for (int i = 0; i < skillCount; i++)
                            {
                                string skillIdentifier = msg.ReadString();
                                float skillLevel = msg.ReadSingle();
                                info?.SetSkillLevel(skillIdentifier, skillLevel, WorldPosition + Vector2.UnitY * 150.0f);
                            }
                            break;
                    }
                    msg.ReadPadBits();
                    break;
            }
        }
        public static Character ReadSpawnData(NetBuffer inc, bool spawn = true)
        {
            DebugConsole.NewMessage("READING CHARACTER SPAWN DATA", Color.Cyan);

            if (GameMain.Server != null) return null;

            bool noInfo         = inc.ReadBoolean();
            ushort id           = inc.ReadUInt16();
            string configPath   = inc.ReadString();
            string seed         = inc.ReadString();

            Vector2 position = new Vector2(inc.ReadFloat(), inc.ReadFloat());

            bool enabled = inc.ReadBoolean();

            DebugConsole.Log("Received spawn data for " + configPath);

            Character character = null;
            if (noInfo)
            {
                if (!spawn) return null;

                character = Character.Create(configPath, position, seed, null, true);
                character.ID = id;
            }
            else
            {
                bool hasOwner       = inc.ReadBoolean();
                int ownerId         = hasOwner ? inc.ReadByte() : -1;                
                byte teamID         = inc.ReadByte();
                bool hasAi          = inc.ReadBoolean();
                
                if (!spawn) return null;

                CharacterInfo info = CharacterInfo.ClientRead(configPath, inc);

                character = Create(configPath, position, seed, info, GameMain.Client.ID != ownerId, hasAi);
                character.ID = id;
                character.TeamID = teamID;

                if (configPath == HumanConfigFile)
                {
                    CharacterInfo duplicateCharacterInfo = GameMain.GameSession.CrewManager.GetCharacterInfos().FirstOrDefault(c => c.ID == info.ID);
                    GameMain.GameSession.CrewManager.RemoveCharacterInfo(duplicateCharacterInfo);
                    GameMain.GameSession.CrewManager.AddCharacter(character);
                }
                
                if (GameMain.Client.ID == ownerId)
                {
                    GameMain.Client.HasSpawned = true;
                    GameMain.Client.Character = character;
                    Controlled = character;

                    GameMain.LightManager.LosEnabled = true;

                    character.memInput.Clear();
                    character.memState.Clear();
                    character.memLocalState.Clear();
                }
            }

            character.Enabled = Controlled == character || enabled;

            return character;
        }

        private void ReadStatus(NetBuffer msg)
        {
            bool isDead = msg.ReadBoolean();
            if (isDead)
            {
                CauseOfDeathType causeOfDeathType = (CauseOfDeathType)msg.ReadRangedInteger(0, Enum.GetValues(typeof(CauseOfDeathType)).Length - 1);
                AfflictionPrefab causeOfDeathAffliction = null;
                if (causeOfDeathType == CauseOfDeathType.Affliction)
                {
                    int afflictionIndex = msg.ReadRangedInteger(0, AfflictionPrefab.List.Count - 1);
                    causeOfDeathAffliction = AfflictionPrefab.List[afflictionIndex];
                }

                byte severedLimbCount = msg.ReadByte();
                if (!IsDead)
                {
                    if (causeOfDeathType == CauseOfDeathType.Pressure)
                    {
                        Implode(true);
                    }
                    else
                    {
                        Kill(causeOfDeathType, causeOfDeathAffliction, true);
                    }
                }

                for (int i = 0; i < severedLimbCount; i++)
                {
                    int severedJointIndex = msg.ReadByte();
                    AnimController.SeverLimbJoint(AnimController.LimbJoints[severedJointIndex]);
                }
            }
            else
            {
                if (IsDead) Revive();
                
                CharacterHealth.ClientRead(msg);
                
                bool ragdolled = msg.ReadBoolean();
                IsRagdolled = ragdolled;
            }
        }
    }
}
