using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Barotrauma
{
    partial class Character
    {
        [Flags]
        private enum InputNetFlags : ushort
        {
            None = 0x0,
            Left = 0x1,
            Right = 0x2,
            Up = 0x4,
            Down = 0x8,
            FacingLeft = 0x10,
            Run = 0x20,
            Crouch = 0x40,
            Select = 0x80,
            Use = 0x100,
            Aim = 0x200,
            Attack = 0x400,

            MaxVal = 0x7FF
        }
        private InputNetFlags dequeuedInput = 0;
        private InputNetFlags prevDequeuedInput = 0;

        public UInt16 LastNetworkUpdateID = 0;

        /// <summary>
        /// ID of the last inputs the server has processed
        /// </summary>
        public UInt16 LastProcessedID;

        private struct NetInputMem
        {
            public InputNetFlags states; //keys pressed/other boolean states at this step
            public UInt16 intAim; //aim angle, represented as an unsigned short where 0=0º, 65535=just a bit under 360º
            public UInt16 interact; //id of the item being interacted with

            public UInt16 networkUpdateID;
        }

        private List<NetInputMem> memInput  = new List<NetInputMem>();

        private List<PosInfo> memPos        = new List<PosInfo>();
        private List<PosInfo> memLocalPos   = new List<PosInfo>();

        private bool networkUpdateSent;

        public bool isSynced = false;

        private void UpdateNetInput()
        {
            if (this != Character.Controlled)
            {
                if (GameMain.Client != null)
                {
                    //freeze AI characters if more than 1 seconds have passed since last update from the server
                    if (lastRecvPositionUpdateTime < NetTime.Now - 1.0f)
                    {
                        AnimController.Frozen = true;
                        memPos.Clear();
                        //hide after 2 seconds
                        if (lastRecvPositionUpdateTime < NetTime.Now - 2.0f)
                        {
                            Enabled = false;
                            return;
                        }
                    }
                }
                else if (GameMain.Server != null && !(this is AICharacter))
                {
                    if (!AllowInput)
                    {
                        AnimController.Frozen = false;
                    }
                    else if (memInput.Count == 0)
                    {
                        AnimController.Frozen = true;
                    }
                    else
                    {
                        AnimController.Frozen = false;
                        prevDequeuedInput = dequeuedInput;

                        LastProcessedID = memInput[memInput.Count - 1].networkUpdateID;
                        dequeuedInput = memInput[memInput.Count - 1].states;

                        double aimAngle = ((double)memInput[memInput.Count - 1].intAim / 65535.0) * 2.0 * Math.PI;
                        cursorPosition = (ViewTarget == null ? AnimController.Collider.Position : ViewTarget.Position)
                            + new Vector2((float)Math.Cos(aimAngle), (float)Math.Sin(aimAngle)) * 60.0f;

                        var closestEntity = Entity.FindEntityByID(memInput[memInput.Count - 1].interact);
                        if (closestEntity is Item)
                        {
                            closestItem = closestEntity as Item;
                        }
                        else if (closestEntity is Character)
                        {
                            closestCharacter = closestEntity as Character;
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
            }
            else if (GameMain.Client != null)
            {
                memLocalPos.Add(new PosInfo(SimPosition, AnimController.TargetDir, LastNetworkUpdateID));

                InputNetFlags newInput = InputNetFlags.None;
                if (IsKeyDown(InputType.Left))      newInput |= InputNetFlags.Left;
                if (IsKeyDown(InputType.Right))     newInput |= InputNetFlags.Right;
                if (IsKeyDown(InputType.Up))        newInput |= InputNetFlags.Up;
                if (IsKeyDown(InputType.Down))      newInput |= InputNetFlags.Down;
                if (IsKeyDown(InputType.Run))       newInput |= InputNetFlags.Run;
                if (IsKeyDown(InputType.Crouch))    newInput |= InputNetFlags.Crouch;
                if (IsKeyHit(InputType.Select))     newInput |= InputNetFlags.Select; //TODO: clean up the way this input is registered
                if (IsKeyDown(InputType.Use))       newInput |= InputNetFlags.Use;
                if (IsKeyDown(InputType.Aim))       newInput |= InputNetFlags.Aim;
                if (IsKeyDown(InputType.Attack))    newInput |= InputNetFlags.Attack;

                if (AnimController.TargetDir == Direction.Left) newInput |= InputNetFlags.FacingLeft;

                Vector2 relativeCursorPos = cursorPosition - (ViewTarget == null ? AnimController.Collider.Position : ViewTarget.Position);
                relativeCursorPos.Normalize();
                UInt16 intAngle = (UInt16)(65535.0 * Math.Atan2(relativeCursorPos.Y, relativeCursorPos.X) / (2.0 * Math.PI));

                NetInputMem newMem = new NetInputMem();
                newMem.states = newInput;
                newMem.intAim = intAngle;
                if (closestItem != null)
                {
                    newMem.interact = closestItem.ID;
                }
                else if (closestCharacter != null)
                {
                    newMem.interact = closestCharacter.ID;
                }

                memInput.Insert(0, newMem);
                LastNetworkUpdateID++;
                if (memInput.Count > 60)
                {
                    memInput.RemoveRange(60, memInput.Count - 60);
                }
            }

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

        public virtual void ClientWrite(NetBuffer msg, object[] extraData = null)
        {
            if (GameMain.Server != null) return;

            if (extraData != null && (NetEntityEvent.Type)extraData[0] == NetEntityEvent.Type.InventoryState)
            {
                inventory.ClientWrite(msg, extraData);
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
                    if (memInput[i].states.HasFlag(InputNetFlags.Select))
                    {
                        msg.Write(memInput[i].interact);
                    }
                    /*if (memInput[i].HasFlag(InputNetFlags.Select) || memInput[i].HasFlag(InputNetFlags.Aim))
                    {
                        msg.Write(memMousePos[i].X);
                        msg.Write(memMousePos[i].Y);
                    }*/
                }
            }
        }
        public virtual void ServerRead(ClientNetObject type, NetBuffer msg, Client c)
        {
            if (GameMain.Server == null) return;

            if (c.Character != this)
            {
#if DEBUG
                DebugConsole.Log("Received a character update message from a client who's not controlling the character");
#endif
                return;
            }

            switch (type)
            {
                case ClientNetObject.CHARACTER_INPUT:

                    UInt16 networkUpdateID = msg.ReadUInt16();
                    byte inputCount = msg.ReadByte();

                    for (int i = 0; i < inputCount; i++)
                    {
                        InputNetFlags newInput = (InputNetFlags)msg.ReadRangedInteger(0, (int)InputNetFlags.MaxVal);
                        //Vector2 newMousePos = Position;
                        UInt16 newAim = 0;
                        UInt16 newInteract = 0;

                        if (newInput.HasFlag(InputNetFlags.Aim))
                        {
                            newAim = msg.ReadUInt16();
                        }
                        if (newInput.HasFlag(InputNetFlags.Select))
                        {
                            newInteract = msg.ReadUInt16();
                        }

                        if (AllowInput)
                        {
                            /*if (newInput.HasFlag(InputNetFlags.Select) || newInput.HasFlag(InputNetFlags.Aim))
                            {
                                newMousePos.X = msg.ReadSingle();
                                newMousePos.Y = msg.ReadSingle();
                            }*/



                            if (NetIdUtils.IdMoreRecent((ushort)(networkUpdateID - i), LastNetworkUpdateID) && (i < 60))
                            {
                                NetInputMem newMem = new NetInputMem();
                                newMem.states = newInput;
                                newMem.intAim = newAim;
                                newMem.interact = newInteract;

                                newMem.networkUpdateID = (ushort)(networkUpdateID - i);

                                memInput.Insert(i, newMem);
                            }
                        }
                    }

                    if (NetIdUtils.IdMoreRecent(networkUpdateID, LastNetworkUpdateID))
                    {
                        LastNetworkUpdateID = networkUpdateID;
                    }
                    if (memInput.Count > 60)
                    {
                        //deleting inputs from the queue here means the server is way behind and data needs to be dropped
                        //we'll make the server drop down to 30 inputs for good measure
                        memInput.RemoveRange(30, memInput.Count - 30);
                    }
                    break;

                case ClientNetObject.ENTITY_STATE:
                    inventory.ServerRead(type, msg, c);
                    break;
            }
        }

        public virtual void ServerWrite(NetBuffer msg, Client c, object[] extraData = null)
        {
            if (GameMain.Server == null) return;

            if (extraData != null)
            {
                switch ((NetEntityEvent.Type)extraData[0])
                {
                    case NetEntityEvent.Type.InventoryState:
                        msg.Write(true);
                        inventory.ClientWrite(msg, extraData);
                        break;
                    case NetEntityEvent.Type.Status:
                        msg.Write(false);
                        WriteStatus(msg);
                        break;
                }
            }
            else
            {
                msg.Write(ID);

                NetBuffer tempBuffer = new NetBuffer();

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

                    if (IsRemotePlayer)
                    {
                        aiming = dequeuedInput.HasFlag(InputNetFlags.Aim);
                        use = dequeuedInput.HasFlag(InputNetFlags.Use);

                        attack = dequeuedInput.HasFlag(InputNetFlags.Attack);
                    }
                    else
                    {
                        aiming = keys[(int)InputType.Aim].GetHeldQueue;
                        use = keys[(int)InputType.Use].GetHeldQueue;

                        attack = keys[(int)InputType.Attack].GetHeldQueue;

                        networkUpdateSent = true;
                    }

                    tempBuffer.Write(aiming);
                    tempBuffer.Write(use);

                    if (AnimController.Limbs.Any(l => l != null && l.attack != null))
                    {
                        tempBuffer.Write(attack);
                    }

                    if (selectedCharacter != null || selectedConstruction != null)
                    {
                        tempBuffer.Write(true);
                        tempBuffer.Write(selectedCharacter != null ? selectedCharacter.ID : selectedConstruction.ID);
                    }
                    else
                    {
                        tempBuffer.Write(false);
                    }

                    if (aiming)
                    {
                        Vector2 relativeCursorPos = cursorPosition - (ViewTarget == null ? AnimController.Collider.Position : ViewTarget.Position);
                        tempBuffer.Write((UInt16)(65535.0 * Math.Atan2(relativeCursorPos.Y, relativeCursorPos.X) / (2.0 * Math.PI)));
                    }

                    tempBuffer.Write(AnimController.TargetDir == Direction.Right);
                }

                tempBuffer.Write(SimPosition.X);
                tempBuffer.Write(SimPosition.Y);

                msg.Write((byte)tempBuffer.LengthBytes);
                msg.Write(tempBuffer);
            }
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

                        if (AnimController.Limbs.Any(l => l != null && l.attack != null))
                        {
                            bool attackInput = msg.ReadBoolean();
                            keys[(int)InputType.Attack].Held = attackInput;
                            keys[(int)InputType.Attack].SetState(false, attackInput);
                        }

                        bool entitySelected = msg.ReadBoolean();
                        if (entitySelected)
                        {
                            ushort entityID = msg.ReadUInt16();
                            Entity selectedEntity = Entity.FindEntityByID(entityID);
                            if (selectedEntity is Character)
                            {
                                SelectCharacter((Character)selectedEntity);
                            }
                            else if (selectedEntity is Item)
                            {
                                var newSelectedConstruction = (Item)selectedEntity;
                                if (newSelectedConstruction != null && selectedConstruction != newSelectedConstruction)
                                {
                                    newSelectedConstruction.Pick(this, true, true);
                                }
                            }
                        }
                        else
                        {
                            if (selectedCharacter != null) DeselectCharacter();
                            selectedConstruction = null;
                        }

                        if (aimInput)
                        {
                            double aimAngle = ((double)msg.ReadUInt16() / 65535.0) * 2.0 * Math.PI;
                            cursorPosition = (ViewTarget == null ? AnimController.Collider.Position : ViewTarget.Position)
                                + new Vector2((float)Math.Cos(aimAngle), (float)Math.Sin(aimAngle)) * 60.0f;

                            TransformCursorPos();
                        }
                        facingRight = msg.ReadBoolean();
                    }

                    Vector2 pos = new Vector2(
                        msg.ReadFloat(),
                        msg.ReadFloat());

                    var posInfo = new PosInfo(pos, facingRight ? Direction.Right : Direction.Left, networkUpdateID, sendingTime);

                    int index = 0;
                    if (GameMain.NetworkMember.Character == this && AllowInput)
                    {
                        while (index < memPos.Count && NetIdUtils.IdMoreRecent(posInfo.ID, memPos[index].ID))
                            index++;
                    }
                    else
                    {
                        while (index < memPos.Count && posInfo.Timestamp > memPos[index].Timestamp)
                            index++;
                    }

                    memPos.Insert(index, posInfo);
                    break;
                case ServerNetObject.ENTITY_EVENT:
                    bool isInventoryUpdate = msg.ReadBoolean();

                    if (isInventoryUpdate)
                    {
                        inventory.ClientRead(type, msg, sendingTime);
                    }
                    else
                    {
                        ReadStatus(msg);
                    }

                    break;
            }
        }

        private void WriteStatus(NetBuffer msg)
        {
            if (GameMain.Client != null)
            {
                DebugConsole.ThrowError("Client attempted to write character status to a networked message");
                return;
            }

            msg.Write(isDead);
            if (isDead)
            {
                msg.Write((byte)causeOfDeath);
            }
            else
            {
                msg.WriteRangedSingle(health, minHealth, maxHealth, 8);

                msg.Write(oxygen < 100.0f);
                if (oxygen < 100.0f)
                {
                    msg.WriteRangedSingle(oxygen, -100.0f, 100.0f, 8);
                }

                msg.Write(bleeding > 0.0f);
                if (bleeding > 0.0f)
                {
                    msg.WriteRangedSingle(bleeding, 0.0f, 5.0f, 8);
                }

                msg.Write(Stun > 0.0f);
                if (Stun > 0.0f)
                {
                    Stun = MathHelper.Clamp(Stun, 0.0f, 60.0f);
                    msg.WriteRangedSingle(Stun, 0.0f, 60.0f, 8);
                }
            }
        }

        private void ReadStatus(NetBuffer msg)
        {
            if (GameMain.Server != null)
            {
                DebugConsole.ThrowError("Server attempted to read character status from a networked message");
                return;
            }

            bool isDead = msg.ReadBoolean();
            if (isDead)
            {
                causeOfDeath = (CauseOfDeath)msg.ReadByte();
                if (causeOfDeath == CauseOfDeath.Pressure)
                {
                    Implode(true);
                }
                else
                {
                    Kill(causeOfDeath, true);
                }
            }
            else
            {
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
                    StartStun(newStunTimer, true, true);
                }
                else
                {
                    StartStun(0.0f, true, true);
                }
            }
        }

        public void WriteSpawnData(NetBuffer msg)
        {
            if (GameMain.Server == null) return;

            msg.Write(Info == null);
            msg.Write(ID);
            msg.Write(ConfigPath);

            msg.Write(WorldPosition.X);
            msg.Write(WorldPosition.Y);

            msg.Write(Enabled);

            //character with no characterinfo (e.g. some monster)
            if (Info == null) return;

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

            msg.Write(Info.Name);
            msg.Write(TeamID);

            msg.Write(this is AICharacter);
            msg.Write(Info.Gender == Gender.Female);
            msg.Write((byte)Info.HeadSpriteId);
            msg.Write(Info.Job == null ? "" : Info.Job.Name);
        }

        public static Character ReadSpawnData(NetBuffer inc, bool spawn = true)
        {
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

                if (!spawn) return null;

                JobPrefab jobPrefab = JobPrefab.List.Find(jp => jp.Name == jobName);

                CharacterInfo ch = new CharacterInfo(configPath, newName, isFemale ? Gender.Female : Gender.Male, jobPrefab);
                ch.HeadSpriteId = headSpriteID;

                character = Character.Create(configPath, position, ch, GameMain.Client.ID != ownerId, hasAi);
                character.ID = id;
                character.TeamID = teamID;

                if (GameMain.Client.ID == ownerId)
                {
                    GameMain.Client.Character = character;
                    Controlled = character;

                    GameMain.LightManager.LosEnabled = true;

                    character.memInput.Clear();
                    character.memPos.Clear();
                    character.memLocalPos.Clear();
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
                    GameMain.GameSession.CrewManager.characters.Add(character);
                }
            }

            character.Enabled = enabled;

            return character;
        }
    }
}
