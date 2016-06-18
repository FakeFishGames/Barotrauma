using Barotrauma.Items.Components;
using FarseerPhysics;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Barotrauma.Networking
{
    class RespawnManager
    {
        const int MinCharactersToRespawn = 1;

        const float RespawnInterval = 20.0f;

        enum State
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

        private float respawnTimer, shuttleReturnTimer;

        public RespawnManager(NetworkMember server)
        {
            this.networkMember = server;

            respawnShuttle = new Submarine("Submarines/Shuttle Mark I.sub");
            respawnShuttle.Load(false);

            ResetShuttlePos();

            respawnShuttle.GodMode = true;
            
            shuttleDoors = new List<Door>();
            foreach (Item item in Item.ItemList)
            {
                if (item.Submarine != respawnShuttle) continue;

                var steering = item.GetComponent<Steering>();
                if (steering != null) shuttleSteering = steering;

                var door = item.GetComponent<Door>();
                if (door != null) shuttleDoors.Add(door);
            }

            shuttleSteering.TargetPosition = ConvertUnits.ToSimUnits(Level.Loaded.StartPosition);

            respawnTimer = RespawnInterval;
        }
        
        private List<Client> GetClientsToRespawn()
        {
            return networkMember.ConnectedClients.FindAll(c => c.inGame && (c.Character == null || c.Character.IsDead));
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
            if (server == null) return;

            respawnShuttle.Velocity = Vector2.Zero;

            shuttleSteering.AutoPilot = false;
            shuttleSteering.MaintainPos = false;

            if (GetClientsToRespawn().Count < MinCharactersToRespawn) return;

            if (respawnTimer % 10.0f < 5.0f && (respawnTimer - deltaTime) % 10.0f > 5.0f)
            {
                string time = respawnTimer <= 60.0f ?
                    (int)respawnTimer + " seconds" :
                    (int)Math.Floor(respawnTimer / 60.0f) + " minutes";

                server.SendChatMessage("Transportation shuttle dispatching in " + time, ChatMessageType.Server);
            }

            respawnTimer -= deltaTime;
            if (respawnTimer <= 0.0f)
            {
                Respawn();

                respawnTimer = RespawnInterval;
                state = State.Transporting;
            }
        }

        private void UpdateTransporting(float deltaTime)
        {
            if (Character.CharacterList.Any(c => c.Submarine == respawnShuttle && !c.IsDead)) return;

            shuttleReturnTimer += deltaTime;
            if (shuttleReturnTimer > 10.0f)
            {
                state = State.Returning;
                shuttleReturnTimer = 0.0f;
            }
        }

        private void UpdateReturning(float deltaTime)
        {
            shuttleReturnTimer += deltaTime;
            
            if (shuttleReturnTimer > 1.0f)
            {
                shuttleSteering.AutoPilot = true;
                shuttleSteering.MaintainPos = false;

                shuttleDoors.ForEach(s => s.IsOpen = false);
                
                if (shuttleSteering.SteeringPath != null && shuttleSteering.SteeringPath.CurrentIndex == shuttleSteering.SteeringPath.Nodes.Count-1)
                {
                    CoroutineManager.StartCoroutine(
                        ForceShuttleToPos(new Vector2(Level.Loaded.StartPosition.X, Level.Loaded.Size.Y + 1000.0f), 100.0f));

                    state = State.Waiting;
                }

                shuttleReturnTimer = 0.0f;
            }
        }

        private void Respawn()
        {
            var server = networkMember as GameServer;
            if (server == null) return;

            ResetShuttlePos();

            server.SendChatMessage("Transportation shuttle dispatched");

            server.RespawnClients();
            
            CoroutineManager.StartCoroutine(ForceShuttleToPos(Level.Loaded.StartPosition, 100.0f));
        }

        private IEnumerable<object> ForceShuttleToPos(Vector2 position, float speed)
        {
            respawnShuttle.SubBody.Body.IgnoreCollisionWith(Level.Loaded.ShaftBodies[0]);

            while (Math.Abs(position.Y - respawnShuttle.WorldPosition.Y) > 100.0f)
            {
                Vector2 displayVel = Vector2.Normalize(position - respawnShuttle.WorldPosition) * speed;
                respawnShuttle.SubBody.Body.LinearVelocity = ConvertUnits.ToSimUnits(displayVel);
                yield return CoroutineStatus.Running;
            }

            respawnShuttle.SubBody.Body.RestoreCollisionWith(Level.Loaded.ShaftBodies[0]);

        }

        private void ResetShuttlePos()
        {
            respawnShuttle.SetPosition(new Vector2(Level.Loaded.StartPosition.X, Level.Loaded.Size.Y + 1000.0f));

            respawnShuttle.Velocity = Vector2.Zero;
        }

        public void WriteNetworkEvent(NetOutgoingMessage msg)
        {
            var server = networkMember as GameServer;
            var clients = GetClientsToRespawn();
            
            msg.Write((byte)PacketTypes.Respawn);

            var waypoints = WayPoint.SelectCrewSpawnPoints(clients.Select(c => c.characterInfo).ToList(), respawnShuttle);

            msg.Write((byte)clients.Count);
            for (int i = 0; i < clients.Count; i++)
            {
                msg.Write((byte)clients[i].ID);
                clients[i].Character = Character.Create(clients[i].characterInfo, waypoints[i].WorldPosition, true, false);
                clients[i].Character.GiveJobItems(waypoints[i]);

                GameMain.GameSession.CrewManager.characters.Add(clients[i].Character);

                server.WriteCharacterData(msg, clients[i].Character.Name, clients[i].Character);
            }
        }

        public void ReadNetworkEvent(NetIncomingMessage inc)
        {
            ResetShuttlePos();

            var client = networkMember as GameClient;

            int clientCount = inc.ReadByte();
            for (int i = 0; i<clientCount; i++)
            {
                byte clientId = inc.ReadByte();

                client.ReadCharacterData(inc, clientId == client.ID);
            }

            CoroutineManager.StartCoroutine(ForceShuttleToPos(Level.Loaded.StartPosition, 100.0f));
        }
    }
}
