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
        
        public Submarine respawnShuttle;
        private Steering shuttleSteering;
        private List<Door> shuttleDoors;
        
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
                            Array.ForEach(connection.Wires, w => { if (w != null) w.Locked = true; });
                        }
                    }
                    //Item Removal Prevention
                    if (item.body != null && item.body.Enabled && item.ParentInventory == null)
                    {
                        item.AddTag("RespawnItem");
                        continue;
                    }
                }
            }
            else
            {
                respawnShuttle = null;
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
                c.InGame && 
                (!c.SpectateOnly || !((GameServer)networkMember).AllowSpectating) && 
                (c.Character == null || c.Character.IsDead));
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
            
            if (respawnShuttle == null) return;

            respawnShuttle.Velocity = Vector2.Zero;

            shuttleSteering.AutoPilot = false;
            shuttleSteering.MaintainPos = false;
        }

        private void UpdateTransporting(float deltaTime)
        {
            //infinite transport time -> shuttle wont return
            if (maxTransportTime <= 0.0f) return;

            shuttleTransportTimer -= deltaTime;

            /*
            if (shuttleTransportTimer + deltaTime > 15.0f && shuttleTransportTimer <= 15.0f &&
                networkMember.Character != null &&
                networkMember.Character.Submarine == respawnShuttle)
            {
                networkMember.AddChatMessage("The shuttle will automatically return back to the outpost. Please leave the shuttle immediately.", ChatMessageType.Server);
            }
            */

            //15 second warning time
            if (shuttleTransportTimer + deltaTime > 15.0f && shuttleTransportTimer <= 15.0f && NilMod.NilModEventChatter.ChatShuttleLeaving015 && maxTransportTime != 15.0f)
            {
                NilMod.NilModEventChatter.SendRespawnLeavingWarning(15f);
            }
            else if (shuttleTransportTimer + deltaTime > 30.0f && shuttleTransportTimer <= 30.0f && NilMod.NilModEventChatter.ChatShuttleLeaving030 && maxTransportTime != 30.0f)
            {
                NilMod.NilModEventChatter.SendRespawnLeavingWarning(30f);
            }
            else if (shuttleTransportTimer + deltaTime > 60.0f && shuttleTransportTimer <= 60.0f && NilMod.NilModEventChatter.ChatShuttleLeaving100 && maxTransportTime != 60.0f)
            {
                NilMod.NilModEventChatter.SendRespawnLeavingWarning(60f);
            }
            else if (shuttleTransportTimer + deltaTime > 90.0f && shuttleTransportTimer <= 90.0f && NilMod.NilModEventChatter.ChatShuttleLeaving130 && maxTransportTime != 90.0f)
            {
                NilMod.NilModEventChatter.SendRespawnLeavingWarning(90f);
            }
            else if (shuttleTransportTimer + deltaTime > 120.0f && shuttleTransportTimer <= 120.0f && NilMod.NilModEventChatter.ChatShuttleLeaving200 && maxTransportTime != 120.0f)
            {
                NilMod.NilModEventChatter.SendRespawnLeavingWarning(120f);
            }
            else if (shuttleTransportTimer + deltaTime > 180.0f && shuttleTransportTimer <= 180.0f && NilMod.NilModEventChatter.ChatShuttleLeaving300 && maxTransportTime != 180.0f)
            {
                NilMod.NilModEventChatter.SendRespawnLeavingWarning(180f);
            }
            else if (shuttleTransportTimer + deltaTime > 240.0f && shuttleTransportTimer <= 240.0f && NilMod.NilModEventChatter.ChatShuttleLeaving400 && maxTransportTime != 240.0f)
            {
                NilMod.NilModEventChatter.SendRespawnLeavingWarning(240f);
            }
            else if (shuttleTransportTimer + deltaTime > 300.0f && shuttleTransportTimer <= 300.0f && NilMod.NilModEventChatter.ChatShuttleLeaving500 && maxTransportTime != 300.0f)
            {
                NilMod.NilModEventChatter.SendRespawnLeavingWarning(300f);
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
                if (GameMain.NilMod.RespawnShuttleLeaveAtTime >= 0.0f)
                {
                    shuttleReturnTimer = GameMain.NilMod.RespawnShuttleLeaveAtTime;
                }
                else
                {
                    shuttleReturnTimer = maxTransportTime;
                }
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

                //Default return behaviour
                if (GameMain.NilMod.RespawnLeavingAutoPilotMode == 0)
                {
                    shuttleSteering.SetDestinationLevelStart();
                }
                //Maintain Position
                else if (GameMain.NilMod.RespawnLeavingAutoPilotMode == 1)
                {
                    shuttleSteering.AutoPilot = false;
                    shuttleSteering.MaintainPos = false;
                    shuttleSteering.MaintainPos = true;
                    shuttleSteering.AutoPilot = true;
                }
                //Any other value does nothing aka 2, can code more later

                if (GameMain.NilMod.RespawnShuttleLeavingCloseDoors)
                {
                    foreach (Door door in shuttleDoors)
                    {
                        if (door.IsOpen) door.SetState(false, false, true);
                    }
                }

                var shuttleGaps = Gap.GapList.FindAll(g => g.Submarine == respawnShuttle && g.ConnectedWall != null);
                shuttleGaps.ForEach(g => g.Remove());

                if (GameMain.NilMod.RespawnShuttleLeavingUndock)
                {
                    var dockingPorts = Item.ItemList.FindAll(i => i.Submarine == respawnShuttle && i.GetComponent<DockingPort>() != null);
                    dockingPorts.ForEach(d => d.GetComponent<DockingPort>().Undock());
                }

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
                    GameServer.Log("The respawn shuttle has left.", ServerLog.MessageType.Spawns);
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

            if (respawnShuttle != null)
            {
                state = State.Transporting;
                server.CreateEntityEvent(this);

                ResetShuttle();

                shuttleSteering.TargetVelocity = Vector2.Zero;

                GameServer.Log("Dispatching the respawn shuttle.", ServerLog.MessageType.Spawns);

                RespawnCharacters();

                CoroutineManager.StopCoroutines("forcepos");
                CoroutineManager.StartCoroutine(ForceShuttleToPos(Level.Loaded.StartPosition - Vector2.UnitY * Level.ShaftHeight, 100.0f), "forcepos");
            }
            else
            {
                state = State.Waiting;
                GameServer.Log("Respawning everyone in main sub.", ServerLog.MessageType.Spawns);
                server.CreateEntityEvent(this);

                RespawnCharacters();
            }
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

                //Items originally part of the respawn are no longer removed.
                if (item.body != null && item.body.Enabled && item.ParentInventory == null && !item.HasTag("RespawnItem"))
                {
                    Entity.Spawner.AddToRemoveQueue(item);
                    continue;
                }

                item.Condition = item.Prefab.Health;

                var powerContainer = item.GetComponent<PowerContainer>();
                if (powerContainer != null)
                {
                    powerContainer.charge = powerContainer.Capacity;
                    //Just in case force a syncing event - had reports of depleted respawn shuttles on spawn which is a likely sync issue.
                    powerContainer.Item.CreateServerEvent(powerContainer);
                    powerContainer.lastSentCharge = powerContainer.Capacity;
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
                hull.WaterVolume = 0.0f;
            }

            if (GameMain.NilMod.EnableEventChatterSystem)
            {
                if (NilMod.NilModEventChatter.ChatShuttleRespawn) SendKillMessages();
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
                    //Need to use hide here
                    //Entity.Spawner.AddToRemoveQueue(c);
                }
            }

            respawnShuttle.SetPosition(new Vector2(Level.Loaded.StartPosition.X, Level.Loaded.Size.Y + respawnShuttle.Borders.Height));

            respawnShuttle.Velocity = Vector2.Zero;

            respawnShuttle.PhysicsBody.FarseerBody.RestoreCollisionWith(Level.Loaded.TopBarrier);

        }

        public void RespawnCharacters()
        {
            if (GameMain.NilMod.EnableEventChatterSystem)
            {
                if (NilMod.NilModEventChatter.ChatShuttleRespawn) SendRespawnMessages();
            }
            var server = networkMember as GameServer;
            if (server == null) return;

            var respawnSub = respawnShuttle != null ? respawnShuttle : Submarine.MainSub;
            
            var clients = GetClientsToRespawn();
            foreach (Client c in clients)
            {
                //all characters are in Team 1 in game modes/missions with only one team.
                //if at some point we add a game mode with multiple teams where respawning is possible, this needs to be reworked
                c.TeamID = 1;
                if (c.CharacterInfo == null) c.CharacterInfo = new CharacterInfo(Character.HumanConfigFile, c.Name);
            }

            List<CharacterInfo> characterInfos = clients.Select(c => c.CharacterInfo).ToList();
            if (server.Character != null && server.Character.IsDead)
            {
                characterInfos.Add(server.CharacterInfo);
            }

            //NilMod Jobs fix attempt
            server.AssignJobs(clients, server.Character != null && server.Character.IsDead);
            foreach (Client c in clients)
            {
                if (c.AssignedJob != null)
                {
                    c.CharacterInfo.Job = new Job(c.AssignedJob);
                }
                else
                {
                    DebugConsole.NewMessage("Error - Server Attempted to respawn player: " + c.Name + " Without an assigned job.", Color.Red);
                    DebugConsole.NewMessage("Retrying job assignment. . .", Color.Red);
                    server.AssignJobs(clients, server.Character != null && server.Character.IsDead);
                    if (c.AssignedJob != null)
                    {
                        c.CharacterInfo.Job = new Job(c.AssignedJob);
                    }
                    else
                    {
                        DebugConsole.NewMessage("Error - Player: " + c.Name + " Still has no assigned job, skipping job assignment.", Color.Red);
                    }
                }
            }

            //the spawnpoints where the characters will spawn
            var shuttleSpawnPoints = WayPoint.SelectCrewSpawnPoints(characterInfos, respawnSub);
            //the spawnpoints where they would spawn if they were spawned inside the main sub
            //(in order to give them appropriate ID card tags)
            var mainSubSpawnPoints = WayPoint.SelectCrewSpawnPoints(characterInfos, Submarine.MainSub);

            ItemPrefab divingSuitPrefab = MapEntityPrefab.Find("Diving Suit") as ItemPrefab;
            ItemPrefab oxyPrefab        = MapEntityPrefab.Find("Oxygen Tank") as ItemPrefab;
            ItemPrefab scooterPrefab    = MapEntityPrefab.Find("Underwater Scooter") as ItemPrefab;
            ItemPrefab batteryPrefab    = MapEntityPrefab.Find("Battery Cell") as ItemPrefab;

            var cargoSp = WayPoint.WayPointList.Find(wp => wp.Submarine == respawnSub && wp.SpawnType == SpawnType.Cargo);

            if (GameMain.NilMod.RespawnOnMainSub || respawnSub == null)
            {
                GameServer.Log("MainSub Respawn crew added:", ServerLog.MessageType.Spawns);
            }
            else
            {
                GameServer.Log("Shuttle Respawn crew:", ServerLog.MessageType.Spawns);
            }

            for (int i = 0; i < characterInfos.Count; i++)
            {
                bool myCharacter = false;
#if CLIENT
                myCharacter = i >= clients.Count;
#endif

                Character character;
                if (!GameMain.NilMod.RespawnOnMainSub)
                {
                    character = Character.Create(characterInfos[i], shuttleSpawnPoints[i].WorldPosition, !myCharacter, false);
                }
                else
                {
                    character = Character.Create(characterInfos[i], mainSubSpawnPoints[i].WorldPosition, !myCharacter, false);
                }

                character.TeamID = 1;

#if CLIENT
                if (myCharacter)
                {
                    server.Character = character;
                    Character.Controlled = character;

                    GameSession.inGameInfo.AddNoneClientCharacter(character, true);

                    if (GameMain.NilMod.DisableLOSOnStart)
                    {
                        GameMain.LightManager.LosEnabled = false;
                    }
                    else
                    {
                        GameMain.LightManager.LosEnabled = true;
                    }
                    if (GameMain.NilMod.DisableLightsOnStart)
                    {
                        GameMain.LightManager.LightingEnabled = false;
                    }
                    else
                    {
                        GameMain.LightManager.LightingEnabled = true;
                    }
                    GameServer.Log("Respawn: " + character.Name + " As " + character.Info.Job.Name + " As Host", ServerLog.MessageType.Spawns);
                }
                else
                {
#endif
                    clients[i].Character = character;
                    GameServer.Log("Respawn: " + clients[i].Character.Name + " As " + clients[i].Character.Info.Job.Name + " On " + clients[i].Connection.RemoteEndPoint.Address, ServerLog.MessageType.Spawns);

#if CLIENT
                    GameSession.inGameInfo.UpdateClientCharacter(clients[i], character, true);
                }
#endif

                Vector2 pos = cargoSp == null ? character.Position : cargoSp.Position;

                if (divingSuitPrefab != null && oxyPrefab != null)
                {
                    var divingSuit  = new Item(divingSuitPrefab, pos, respawnSub);
                    Spawner.CreateNetworkEvent(divingSuit, false);

                    var oxyTank     = new Item(oxyPrefab, pos, respawnSub);
                    Spawner.CreateNetworkEvent(oxyTank, false);
                    divingSuit.Combine(oxyTank);

                    if (batteryPrefab != null)
                    {
                        var battery = new Item(batteryPrefab, pos, respawnSub);
                        Spawner.CreateNetworkEvent(battery, false);
                        divingSuit.Combine(battery);
                    }
                }

                if (scooterPrefab != null && batteryPrefab != null)
                {
                    var scooter     = new Item(scooterPrefab, pos, respawnSub);
                    Spawner.CreateNetworkEvent(scooter, false);

                    var battery     = new Item(batteryPrefab, pos, respawnSub);
                    Spawner.CreateNetworkEvent(battery, false);

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
                    if (!string.IsNullOrWhiteSpace(shuttleSpawnPoints[i].IdCardDesc))
                        item.Description = shuttleSpawnPoints[i].IdCardDesc;
                }
#if CLIENT
                GameSession.inGameInfo.UpdateGameInfoGUIList();
                GameMain.GameSession.CrewManager.AddCharacter(character);
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

        //Nilmod Respawning Player Message Handler

        public void SendRespawnMessages()
        {
            var respawningclients = GetClientsToRespawn();

            foreach (Client client in respawningclients)
            {
                if (NilMod.NilModEventChatter.NilShuttleRespawn.Count() > 0 && NilMod.NilModEventChatter.ChatShuttleRespawn)
                {
                    foreach (string message in NilMod.NilModEventChatter.NilShuttleRespawn)
                    {
                        NilMod.NilModEventChatter.SendServerMessage(message, client);
                    }
                }
            }
        }

        public void SendKillMessages()
        {
            foreach (Client client in networkMember.ConnectedClients)
            {
                if (client.Character != null)
                {
                    if (client.Character.Submarine == respawnShuttle && client.Character.Enabled)
                    {
                        if (NilMod.NilModEventChatter.NilShuttleLeavingKill.Count() > 0 && NilMod.NilModEventChatter.ChatShuttleLeavingKill)
                        {
                            foreach (string message in NilMod.NilModEventChatter.NilShuttleLeavingKill)
                            {
                                NilMod.NilModEventChatter.SendServerMessage(message, client);
                            }
                        }
                    }
                }
            }
        }

        //NilMod Shuttle Commands
        public void ForceShuttle()
        {
            DispatchShuttle();
        }

        public void RecallShuttle()
        {
            ResetShuttle();
        }
    }
}
