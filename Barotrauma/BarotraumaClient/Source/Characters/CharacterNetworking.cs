using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
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
                        inventory.ClientWrite(msg, extraData);
                        break;
                    case NetEntityEvent.Type.Repair:
                        msg.WriteRangedInteger(0, 3, 1);
                        msg.Write(AnimController.Anim == AnimController.Animation.CPR);
                        break;
                    case NetEntityEvent.Type.Status:
                        msg.WriteRangedInteger(0, 3, 2);
                        break;
                    case NetEntityEvent.Type.Control:
                        msg.WriteRangedInteger(0, 3, 3);
                        msg.Write((byte)AnimController.GrabLimb);
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
                    if (memInput[i].states.HasFlag(InputNetFlags.Select) || memInput[i].states.HasFlag(InputNetFlags.Use))
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
                            AnimController.GrabLimb = (LimbType)msg.ReadByte();
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
                            cursorPosition = (ViewTarget == null ? AnimController.AimSourcePos : ViewTarget.Position)
                                + new Vector2((float)Math.Cos(aimAngle), (float)Math.Sin(aimAngle)) * 60.0f;

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

                    int eventType = msg.ReadRangedInteger(0, 2);
                    switch (eventType)
                    {
                        case 0:
                            if (inventory == null)
                            {
                                string errorMsg = "Received an inventory update message for an entity with no inventory (" + Name + ")";
                                DebugConsole.ThrowError(errorMsg);
                                GameAnalyticsManager.AddErrorEventOnce("CharacterNetworking.ClientRead:NoInventory" + ID, GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                            }
                            else
                            {
                                inventory?.ClientRead(type, msg, sendingTime);
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

                                controlled = this;
                                IsRemotePlayer = false;
                                GameMain.Client.HasSpawned = true;
                                GameMain.Client.Character = this;
                                GameMain.LightManager.LosEnabled = true;
                            }
                            else if (controlled == this)
                            {
                                controlled = null;
                                IsRemotePlayer = ownerID > 0;
                            }
                            break;
                        case 2:
                            ReadStatus(msg);
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

            bool noInfo = inc.ReadBoolean();
            ushort id = inc.ReadUInt16();
            string configPath = inc.ReadString();

            Vector2 position = new Vector2(inc.ReadFloat(), inc.ReadFloat());

            bool enabled = inc.ReadBoolean();

            DebugConsole.Log("Received spawn data for " + configPath);

            Character character = null;
            if (noInfo)
            {
                if (!spawn) return null;

                character = Character.Create(configPath, position, null, true);
                character.ID = id;
            }
            else
            {
                bool hasOwner = inc.ReadBoolean();
                int ownerId = hasOwner ? inc.ReadByte() : -1;


                string newName = inc.ReadString();
                byte teamID = inc.ReadByte();

                bool hasAi = inc.ReadBoolean();
                bool isFemale = inc.ReadBoolean();
                int headSpriteID = inc.ReadByte();
                string jobName = inc.ReadString();

                JobPrefab jobPrefab = null;
                Dictionary<string, int> skillLevels = new Dictionary<string, int>();
                if (!string.IsNullOrEmpty(jobName))
                {
                    jobPrefab = JobPrefab.List.Find(jp => jp.Name == jobName);
                    int skillCount = inc.ReadByte();
                    for (int i = 0; i < skillCount; i++)
                    {
                        string skillName = inc.ReadString();
                        int skillLevel = inc.ReadRangedInteger(0, 100);

                        skillLevels.Add(skillName, skillLevel);
                    }
                }

                if (!spawn) return null;


                CharacterInfo ch = new CharacterInfo(configPath, newName, isFemale ? Gender.Female : Gender.Male, jobPrefab);
                ch.HeadSpriteId = headSpriteID;

                System.Diagnostics.Debug.Assert(skillLevels.Count == ch.Job.Skills.Count);
                if (ch.Job != null)
                {
                    foreach (KeyValuePair<string, int> skill in skillLevels)
                    {
                        Skill matchingSkill = ch.Job.Skills.Find(s => s.Name == skill.Key);
                        if (matchingSkill == null)
                        {
                            DebugConsole.ThrowError("Skill \"" + skill.Key + "\" not found in character \"" + newName + "\"");
                            continue;
                        }
                        matchingSkill.Level = skill.Value;
                    }
                }

                character = Create(configPath, position, ch, GameMain.Client.ID != ownerId, hasAi);
                character.ID = id;
                character.TeamID = teamID;

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
                else
                {
                    var ownerClient = GameMain.Client.ConnectedClients.Find(c => c.ID == ownerId);
                    if (ownerClient != null)
                    {
                        ownerClient.Character = character;
                    }
                }

                if (configPath == Character.HumanConfigFile)
                {
                    GameMain.GameSession.CrewManager.AddCharacter(character);
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
                causeOfDeath = (CauseOfDeath)msg.ReadByte();
                byte severedLimbCount = msg.ReadByte();
                if (!IsDead)
                {
                    if (causeOfDeath == CauseOfDeath.Pressure)
                    {
                        Implode(true);
                    }
                    else
                    {
                        Kill(causeOfDeath, true);
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
                if (this.isDead) Revive();

                health = msg.ReadRangedSingle(minHealth, maxHealth, 8);

                bool lowOxygen = msg.ReadBoolean();
                if (lowOxygen)
                {
                    Oxygen = msg.ReadRangedSingle(-100.0f, 100.0f, 8);
                }
                else
                {
                    Oxygen = 100.0f;
                }

                bool isBleeding = msg.ReadBoolean();
                if (isBleeding)
                {
                    bleeding = msg.ReadRangedSingle(0.0f, 5.0f, 8);
                }
                else
                {
                    bleeding = 0.0f;
                }

                bool stunned = msg.ReadBoolean();
                if (stunned)
                {
                    float newStunTimer = msg.ReadRangedSingle(0.0f, 60.0f, 8);
                    SetStun(newStunTimer, true, true);
                }
                else
                {
                    SetStun(0.0f, true, true);
                }

                bool ragdolled = msg.ReadBoolean();
                IsRagdolled = ragdolled;

                bool huskInfected = msg.ReadBoolean();
                if (huskInfected)
                {
                    HuskInfectionState = Math.Max(HuskInfectionState, 0.01f);
                }
                else
                {
                    HuskInfectionState = 0.0f;
                }

            }
        }

    }
}
