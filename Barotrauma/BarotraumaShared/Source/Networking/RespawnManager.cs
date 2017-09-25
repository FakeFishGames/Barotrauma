using Barotrauma.Items.Components;
using FarseerPhysics;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    class RespawnManager : Entity, IServerSerializable
    {
        private readonly float respawnInterval;
        private float maxTransportTime;
        
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

        private float updateReturnTimer;

        public Submarine RespawnShuttle
        {
            get { return respawnShuttle; }
        }

        public RespawnManager(NetworkMember networkMember, Submarine shuttle)
            : base(shuttle)
        {
            this.networkMember = networkMember;

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
                        Array.ForEach(connection.Wires, w => { if (w != null) w.Locked = true; });
                    }
                }
            }
                        
            var server = networkMember as GameServer;
            if (server != null)
            {
                respawnInterval = server.RespawnInterval;
                maxTransportTime = server.MaxTransportTime;
            }            

            respawnTimer = respawnInterval;            
        }
        
        private List<Client> GetClientsToRespawn()
        {
            return networkMember.ConnectedClients.FindAll(c => 
                c.inGame && 
                (!c.SpectateOnly || !((GameServer)networkMember).AllowSpectating) && 
                (c.Character == null || c.Character.IsDead));
        }

        public void Update(float deltaTime)
        {
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

        private void UpdateWaiting(float deltaTime)
        {
            var server = networkMember as GameServer;
            if (server == null)
            {
                if (CountdownStarted)
                {
                    respawnTimer = Math.Max(0.0f, respawnTimer - deltaTime);
                }
                return;
            }

            respawnShuttle.Velocity = Vector2.Zero;

            shuttleSteering.AutoPilot = false;
            shuttleSteering.MaintainPos = false;

            int characterToRespawnCount = GetClientsToRespawn().Count;
            int totalCharacterCount = server.ConnectedClients.Count;
            if (server.Character != null)
            {
                totalCharacterCount++;
                if (server.Character.IsDead) characterToRespawnCount++;
            }
            bool startCountdown = (float)characterToRespawnCount >= Math.Max((float)totalCharacterCount * server.MinRespawnRatio, 1.0f);

            if (startCountdown)
            {
                if (!CountdownStarted)
                {
                    CountdownStarted = true;
                    server.CreateEntityEvent(this);
                }
            }
            else
            {
                CountdownStarted = false;
            }

            if (!CountdownStarted) return;

            respawnTimer -= deltaTime;
            if (respawnTimer <= 0.0f)
            {
                respawnTimer = respawnInterval;

                DispatchShuttle();
            }
        }

        private void UpdateTransporting(float deltaTime)
        {
            //infinite transport time -> shuttle wont return
            if (maxTransportTime <= 0.0f) return;

            shuttleTransportTimer -= deltaTime;

            if (shuttleTransportTimer + deltaTime > 15.0f && shuttleTransportTimer <= 15.0f &&
                networkMember.Character != null &&
                networkMember.Character.Submarine == respawnShuttle)
            {
                networkMember.AddChatMessage("The shuttle will automatically return back to the outpost. Please leave the shuttle immediately.", ChatMessageType.Server);
            }


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
                shuttleReturnTimer = maxTransportTime;
                shuttleTransportTimer = maxTransportTime;
            }
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

                shuttleSteering.SetDestinationLevelStart();

                foreach (Door door in shuttleDoors)
                {
                    if (door.IsOpen) door.SetState(false,false,true);
                }

                var shuttleGaps = Gap.GapList.FindAll(g => g.Submarine == respawnShuttle && g.ConnectedWall != null);
                shuttleGaps.ForEach(g => g.Remove());

                var dockingPorts = Item.ItemList.FindAll(i => i.Submarine == respawnShuttle && i.GetComponent<DockingPort>() != null);
                dockingPorts.ForEach(d => d.GetComponent<DockingPort>().Undock());

                var server = networkMember as GameServer;
                if (server == null) return;
                
                //shuttle has returned if the path has been traversed or the shuttle is close enough to the exit

                if (!CoroutineManager.IsCoroutineRunning("forcepos"))
                {
                    if (shuttleSteering.SteeringPath != null && shuttleSteering.SteeringPath.Finished
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

                    respawnTimer = respawnInterval;
                    CountdownStarted = false;
                }
            }
        }

        private void DispatchShuttle()
        {
            var server = networkMember as GameServer;
            if (server == null) return;

            state = State.Transporting;
            server.CreateEntityEvent(this);

            ResetShuttle();

            shuttleSteering.TargetVelocity = Vector2.Zero;

            GameServer.Log("Dispatching the respawn shuttle.", ServerLog.MessageType.Spawning);

            RespawnCharacters();

            CoroutineManager.StopCoroutines("forcepos");
            CoroutineManager.StartCoroutine(ForceShuttleToPos(Level.Loaded.StartPosition - Vector2.UnitY * Level.ShaftHeight, 100.0f), "forcepos");
        }

        private IEnumerable<object> ForceShuttleToPos(Vector2 position, float speed)
        {
            respawnShuttle.PhysicsBody.FarseerBody.IgnoreCollisionWith(Level.Loaded.TopBarrier);

            while (Math.Abs(position.Y - respawnShuttle.WorldPosition.Y) > 100.0f)
            {
                Vector2 displayVel = Vector2.Normalize(position - respawnShuttle.WorldPosition) * speed;
                respawnShuttle.SubBody.Body.LinearVelocity = ConvertUnits.ToSimUnits(displayVel);
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

            foreach (Item item in Item.ItemList)
            {
                if (item.Submarine != respawnShuttle) continue;

                if (item.body != null && item.body.Enabled && item.ParentInventory == null)
                {
                    Entity.Spawner.AddToRemoveQueue(item);
                    continue;
                }

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
            shuttleGaps.ForEach(g => g.Remove());

            foreach (Hull hull in Hull.hullList)
            {
                if (hull.Submarine != respawnShuttle) continue;

                hull.OxygenPercentage = 100.0f;
                hull.Volume = 0.0f;
            }

            foreach (Character c in Character.CharacterList)
            {
                if (c.Submarine == respawnShuttle)
                {
                    if (Character.Controlled == c) Character.Controlled = null;
                    c.Enabled = false;

                    if (c.Inventory != null)
                    {
                        foreach (Item item in c.Inventory.Items)
                        {
                            if (item == null) continue;
                            Entity.Spawner.AddToRemoveQueue(item);
                        }
                    }
                    
                    c.Kill(CauseOfDeath.Damage, true);
                }
            }

            respawnShuttle.SetPosition(new Vector2(Level.Loaded.StartPosition.X, Level.Loaded.Size.Y + respawnShuttle.Borders.Height));

            respawnShuttle.Velocity = Vector2.Zero;

            respawnShuttle.PhysicsBody.FarseerBody.RestoreCollisionWith(Level.Loaded.TopBarrier);

        }

        public void RespawnCharacters()
        {
            var server = networkMember as GameServer;
            if (server == null) return;

            
            var clients = GetClientsToRespawn();
            foreach (Client c in clients)
            {
                //all characters are in Team 1 in game modes/missions with only one team.
                //if at some point we add a game mode with multiple teams where respawning is possible, this needs to be reworked
                c.TeamID = 1;
                if (c.characterInfo == null) c.characterInfo = new CharacterInfo(Character.HumanConfigFile, c.name);
            }

            List<CharacterInfo> characterInfos = clients.Select(c => c.characterInfo).ToList();
            if (server.Character != null && server.Character.IsDead)
            {
                characterInfos.Add(server.CharacterInfo);
            }

            server.AssignJobs(clients, server.Character != null && server.Character.IsDead);
            foreach (Client c in clients)
            {
                c.characterInfo.Job = new Job(c.assignedJob);
            }

            //the spawnpoints where the characters will spawn
            var shuttleSpawnPoints = WayPoint.SelectCrewSpawnPoints(characterInfos, respawnShuttle);
            //the spawnpoints where they would spawn if they were spawned inside the main sub
            //(in order to give them appropriate ID card tags)
            var mainSubSpawnPoints = WayPoint.SelectCrewSpawnPoints(characterInfos, Submarine.MainSub);

            ItemPrefab divingSuitPrefab = ItemPrefab.list.Find(ip => ip.Name == "Diving Suit") as ItemPrefab;
            ItemPrefab oxyPrefab        = ItemPrefab.list.Find(ip => ip.Name == "Oxygen Tank") as ItemPrefab;
            ItemPrefab scooterPrefab    = ItemPrefab.list.Find(ip => ip.Name == "Underwater Scooter") as ItemPrefab;
            ItemPrefab batteryPrefab    = ItemPrefab.list.Find(ip => ip.Name == "Battery Cell") as ItemPrefab;

            var cargoSp = WayPoint.WayPointList.Find(wp => wp.Submarine == respawnShuttle && wp.SpawnType == SpawnType.Cargo);

            for (int i = 0; i < characterInfos.Count; i++)
            {
                bool myCharacter = false;
#if CLIENT
                myCharacter = i >= clients.Count;
#endif

                var character = Character.Create(characterInfos[i], shuttleSpawnPoints[i].WorldPosition, !myCharacter, false);
                
                character.TeamID = 1;

#if CLIENT
                if (myCharacter)
                {
                    server.Character = character;
                    Character.Controlled = character;

                    GameMain.LightManager.LosEnabled = true;
                    GameServer.Log(string.Format("Respawning {0} (host) as {1}", character.Name, characterInfos[i].Job.Name), ServerLog.MessageType.Spawning);
                }
                else
                {
#endif
                    clients[i].Character = character;
                    GameServer.Log(string.Format("Respawning {0} ({1}) as {2}", clients[i].name, clients[i].Connection?.RemoteEndPoint?.Address, characterInfos[i].Job.Name), ServerLog.MessageType.Spawning);

#if CLIENT
                }
#endif

                Vector2 pos = cargoSp == null ? character.Position : cargoSp.Position;

                if (divingSuitPrefab != null && oxyPrefab != null)
                {
                    var divingSuit  = new Item(divingSuitPrefab, pos, respawnShuttle);
                    Entity.Spawner.CreateNetworkEvent(divingSuit, false);

                    var oxyTank     = new Item(oxyPrefab, pos, respawnShuttle);
                    Entity.Spawner.CreateNetworkEvent(oxyTank, false);
                    divingSuit.Combine(oxyTank);                    
                }

                if (scooterPrefab != null && batteryPrefab != null)
                {
                    var scooter     = new Item(scooterPrefab, pos, respawnShuttle);
                    Entity.Spawner.CreateNetworkEvent(scooter, false);

                    var battery     = new Item(batteryPrefab, pos, respawnShuttle);
                    Entity.Spawner.CreateNetworkEvent(battery, false);

                    scooter.Combine(battery);
                }
                                
                //give the character the items they would've gotten if they had spawned in the main sub
                character.GiveJobItems(mainSubSpawnPoints[i]);

                //add the ID card tags they should've gotten when spawning in the shuttle
                foreach (Item item in character.Inventory.Items)
                {
                    if (item == null || item.Prefab.Name != "ID Card") continue;                    
                    foreach (string s in shuttleSpawnPoints[i].IdCardTags)
                    {
                        item.AddTag(s);
                    }                    
                }
#if CLIENT
                GameMain.GameSession.CrewManager.characters.Add(character);
#endif
            }
            
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
