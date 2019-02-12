using Barotrauma.Items.Components;
using FarseerPhysics;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    partial class RespawnManager : Entity, IServerSerializable
    {
        public enum State
        {
            Waiting,
            Transporting,
            Returning
        }

        private NetworkMember networkMember;

        private State state;
        
        private Submarine respawnShuttle;
        private Steering shuttleSteering;
        private List<Door> shuttleDoors;

        //items created during respawn
        //any respawn items left in the shuttle are removed when the shuttle despawns
        private List<Item> respawnItems = new List<Item>();

        public bool UsingShuttle
        {
            get { return respawnShuttle != null; }
        }

        /// <summary>
        /// How long until the shuttle is dispatched with respawned characters
        /// </summary>
        public float RespawnTimer
        {
            get { return respawnTimer; }
        }

        /// <summary>
        /// how long until the shuttle starts heading back out of the level
        /// </summary>
        public float TransportTimer
        {
            get { return shuttleTransportTimer; }
        }

        public bool CountdownStarted
        {
            get;
            private set;
        }

        public State CurrentState
        {
            get { return state; }
        }

        private float respawnTimer, shuttleReturnTimer, shuttleTransportTimer;

        private float maxTransportTime;

        private float updateReturnTimer;

        public Submarine RespawnShuttle
        {
            get { return respawnShuttle; }
        }

        public RespawnManager(NetworkMember networkMember, Submarine shuttle)
            : base(shuttle)
        {
            this.networkMember = networkMember;

            if (shuttle != null)
            {
                respawnShuttle = new Submarine(shuttle.FilePath, shuttle.MD5Hash.Hash, true);
                respawnShuttle.Load(false);

                ResetShuttle();
                
                //respawnShuttle.GodMode = true;

                shuttleDoors = new List<Door>();
                foreach (Item item in Item.ItemList)
                {
                    if (item.Submarine != respawnShuttle) continue;

                    var steering = item.GetComponent<Steering>();
                    if (steering != null) shuttleSteering = steering;

                    var door = item.GetComponent<Door>();
                    if (door != null) shuttleDoors.Add(door);

                    //lock all wires to prevent the players from messing up the electronics
                    var connectionPanel = item.GetComponent<ConnectionPanel>();
                    if (connectionPanel != null)
                    {
                        foreach (Connection connection in connectionPanel.Connections)
                        {
                            foreach (Wire wire in connection.Wires)
                            {
                                if (wire != null) wire.Locked = true;
                            }
                        }
                    }
                }
            }
            else
            {
                respawnShuttle = null;
            }

#if SERVER
            if (networkMember is GameServer server)
            {
                respawnTimer = server.ServerSettings.RespawnInterval;
                maxTransportTime = server.ServerSettings.MaxTransportTime;
            }
#endif
        }
        
        public void Update(float deltaTime)
        {
            if (respawnShuttle == null)
            {
                if (state != State.Waiting)
                {
                    state = State.Waiting;
                }
            }

            switch (state)
            {
                case State.Waiting:
                    UpdateWaiting(deltaTime);
                    break;
                case State.Transporting:
                    UpdateTransporting(deltaTime);
                    break;
                case State.Returning:
                    UpdateReturning(deltaTime);
                    break;
            }
        }

        partial void UpdateWaiting(float deltaTime);
        
        private void UpdateTransporting(float deltaTime)
        {
            //infinite transport time -> shuttle wont return
            if (maxTransportTime <= 0.0f) return;

            shuttleTransportTimer -= deltaTime;

#if CLIENT
            GameClient nClient = networkMember as GameClient;
            if (shuttleTransportTimer + deltaTime > 15.0f && shuttleTransportTimer <= 15.0f &&
                nClient.Character != null &&
                nClient.Character.Submarine == respawnShuttle)
            {
                nClient.AddChatMessage("The shuttle will automatically return back to the outpost. Please leave the shuttle immediately.", ChatMessageType.Server);
            }
#endif

#if SERVER
            var server = networkMember as GameServer;
            if (server == null) return;

            //if there are no living chracters inside, transporting can be stopped immediately
            if (!Character.CharacterList.Any(c => c.Submarine == respawnShuttle && !c.IsDead))
            {
                shuttleTransportTimer = 0.0f;
            }

            if (shuttleTransportTimer <= 0.0f)
            {
                GameServer.Log("The respawn shuttle is leaving.", ServerLog.MessageType.ServerMessage);
                state = State.Returning;

                server.CreateEntityEvent(this);

                CountdownStarted = false;
                maxTransportTime = server.ServerSettings.MaxTransportTime;
                shuttleReturnTimer = maxTransportTime;
                shuttleTransportTimer = maxTransportTime;
            }
#endif
        }

        private void UpdateReturning(float deltaTime)
        {
            //if (shuttleReturnTimer == maxTransportTime && 
            //    networkMember.Character != null && 
            //    networkMember.Character.Submarine == respawnShuttle)
            //{
            //    networkMember.AddChatMessage("The shuttle will automatically return back to the outpost. Please leave the shuttle immediately.", ChatMessageType.Server);
            //}

            shuttleReturnTimer -= deltaTime;

            updateReturnTimer += deltaTime;

            if (updateReturnTimer > 1.0f)
            {
                updateReturnTimer = 0.0f;

                respawnShuttle.PhysicsBody.FarseerBody.IgnoreCollisionWith(Level.Loaded.TopBarrier);

                if (shuttleSteering != null)
                {
                    shuttleSteering.SetDestinationLevelStart();
                }


#if SERVER
                var server = networkMember as GameServer;
                if (server == null) return;

                foreach (Door door in shuttleDoors)
                {
                    if (door.IsOpen) door.TrySetState(false, false, true);
                }
                
                var shuttleGaps = Gap.GapList.FindAll(g => g.Submarine == respawnShuttle && g.ConnectedWall != null);
                shuttleGaps.ForEach(g => Spawner.AddToRemoveQueue(g));

                var dockingPorts = Item.ItemList.FindAll(i => i.Submarine == respawnShuttle && i.GetComponent<DockingPort>() != null);
                dockingPorts.ForEach(d => d.GetComponent<DockingPort>().Undock());

                //shuttle has returned if the path has been traversed or the shuttle is close enough to the exit

                if (!CoroutineManager.IsCoroutineRunning("forcepos"))
                {
                    if ((shuttleSteering?.SteeringPath != null && shuttleSteering.SteeringPath.Finished)
                        || (respawnShuttle.WorldPosition.Y + respawnShuttle.Borders.Y > Level.Loaded.StartPosition.Y - Level.ShaftHeight &&
                            Math.Abs(Level.Loaded.StartPosition.X - respawnShuttle.WorldPosition.X) < 1000.0f))
                    {
                        CoroutineManager.StopCoroutines("forcepos");
                        CoroutineManager.StartCoroutine(
                            ForceShuttleToPos(new Vector2(Level.Loaded.StartPosition.X, Level.Loaded.Size.Y + 1000.0f), 100.0f), "forcepos");

                    }
                }

                if (respawnShuttle.WorldPosition.Y > Level.Loaded.Size.Y || shuttleReturnTimer <= 0.0f)
                {
                    CoroutineManager.StopCoroutines("forcepos");

                    ResetShuttle();

                    state = State.Waiting;
                    GameServer.Log("The respawn shuttle has left.", ServerLog.MessageType.Spawning);
                    server.CreateEntityEvent(this);

                    respawnTimer = server.ServerSettings.RespawnInterval;
                    CountdownStarted = false;
                }
#endif
            }
        }

        partial void DispatchShuttle();
        
        private IEnumerable<object> ForceShuttleToPos(Vector2 position, float speed)
        {
            if (respawnShuttle == null)
            {
                yield return CoroutineStatus.Success;
            }

            respawnShuttle.PhysicsBody.FarseerBody.IgnoreCollisionWith(Level.Loaded.TopBarrier);

            while (Math.Abs(position.Y - respawnShuttle.WorldPosition.Y) > 100.0f)
            {
                Vector2 diff = position - respawnShuttle.WorldPosition;
                if (diff.LengthSquared() > 0.01f)
                {
                    Vector2 displayVel = Vector2.Normalize(diff) * speed;
                    respawnShuttle.SubBody.Body.LinearVelocity = ConvertUnits.ToSimUnits(displayVel);
                }
                yield return CoroutineStatus.Running;

                if (respawnShuttle.SubBody == null) yield return CoroutineStatus.Success;
            }

            respawnShuttle.PhysicsBody.FarseerBody.RestoreCollisionWith(Level.Loaded.TopBarrier);

            yield return CoroutineStatus.Success;
        }

        private void ResetShuttle()
        {
            shuttleTransportTimer = maxTransportTime;
            shuttleReturnTimer = maxTransportTime;

            if (respawnShuttle == null) return;

            foreach (Item item in Item.ItemList)
            {
                if (item.Submarine != respawnShuttle) continue;
                
                //remove respawn items that have been left in the shuttle
                if (respawnItems.Contains(item))
                {
                    Spawner.AddToRemoveQueue(item);
                    continue;
                }

                //restore other items to full condition and recharge batteries
                item.Condition = item.Prefab.Health;
                var powerContainer = item.GetComponent<PowerContainer>();
                if (powerContainer != null)
                {
                    powerContainer.Charge = powerContainer.Capacity;
                }
            }

            foreach (Structure wall in Structure.WallList)
            {
                if (wall.Submarine != respawnShuttle) continue;

                for (int i = 0; i < wall.SectionCount; i++)
                {
                    wall.AddDamage(i, -100000.0f);
                }            
            }

            var shuttleGaps = Gap.GapList.FindAll(g => g.Submarine == respawnShuttle && g.ConnectedWall != null);
            shuttleGaps.ForEach(g => Spawner.AddToRemoveQueue(g));

            foreach (Hull hull in Hull.hullList)
            {
                if (hull.Submarine != respawnShuttle) continue;

                hull.OxygenPercentage = 100.0f;
                hull.WaterVolume = 0.0f;
            }

            foreach (Character c in Character.CharacterList)
            {
                if (c.Submarine != respawnShuttle) continue;

#if CLIENT
                if (Character.Controlled == c) Character.Controlled = null;
#endif
                c.Kill(CauseOfDeathType.Unknown, null, true);
                c.Enabled = false;
                    
                Spawner.AddToRemoveQueue(c);
                if (c.Inventory != null)
                {
                    foreach (Item item in c.Inventory.Items)
                    {
                        if (item == null) continue;
                        Spawner.AddToRemoveQueue(item);
                    }
                }                
            }

            respawnShuttle.SetPosition(new Vector2(Level.Loaded.StartPosition.X, Level.Loaded.Size.Y + respawnShuttle.Borders.Height));

            respawnShuttle.Velocity = Vector2.Zero;

            respawnShuttle.PhysicsBody.FarseerBody.RestoreCollisionWith(Level.Loaded.TopBarrier);

        }

        partial void RespawnCharactersProjSpecific();
        public void RespawnCharacters()
        {
            RespawnCharactersProjSpecific();
        }

        public void ServerWrite(NetBuffer msg, Client c, object[] extraData = null)
        {
            msg.WriteRangedInteger(0, Enum.GetNames(typeof(State)).Length, (int)state);

            switch (state)
            {
                case State.Transporting:
                    msg.Write(TransportTimer);
                    break;
                case State.Waiting:
                    msg.Write(CountdownStarted);
                    msg.Write(respawnTimer);
                    break;
                case State.Returning:
                    break;
            }

            msg.WritePadBits();
        }

        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            var newState = (State)msg.ReadRangedInteger(0, Enum.GetNames(typeof(State)).Length);

            switch (newState)
            {
                case State.Transporting:
                    maxTransportTime = msg.ReadSingle();
                    shuttleTransportTimer = maxTransportTime;
                    CountdownStarted = false;

                    if (state != newState)
                    {
                        CoroutineManager.StopCoroutines("forcepos");
                        CoroutineManager.StartCoroutine(ForceShuttleToPos(Level.Loaded.StartPosition - Vector2.UnitY * Level.ShaftHeight, 100.0f), "forcepos");
                    }
                    break;
                case State.Waiting:
                    CountdownStarted = msg.ReadBoolean();
                    ResetShuttle();
                    respawnTimer = msg.ReadSingle();
                    break;
                case State.Returning:
                    CountdownStarted = false;
                    break;
            }
            state = newState;

            msg.ReadPadBits();
        }
    }
}
