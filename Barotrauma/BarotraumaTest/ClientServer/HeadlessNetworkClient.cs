extern alias Client;
using System.Net;
using Client::Barotrauma;
using Client::Barotrauma.Networking;
using Xunit.Abstractions;
using Option = Barotrauma.Option;
using TaskPool = Barotrauma.TaskPool;

namespace TestProject
{
    /// <summary>
    /// We don't join a server with fully initialized client since it's not needed,
    /// and we only want to test if the server crashes or not.
    /// </summary>
    public class HeadlessNetworkClient
    {
        internal readonly ClientPeer ClientPeer;
        public bool IsConnected { get; private set; }

        private readonly ITestOutputHelper testOutputHelper;

        public HeadlessNetworkClient(IPAddress address, int port, string password, ITestOutputHelper testOutputHelper)
        {
            var callbacks = new ClientPeer.Callbacks(
                OnMessageReceived,
                Disconnect,
                InitializationComplete);

            ClientPeer = new LidgrenClientPeer(new LidgrenEndpoint(address, port), callbacks, Option.None);
            ClientPeer.Start();
            TaskPool.Update(); // Start() adds a task to the TaskPool, so we need to update it to continue
            IsConnected = false;
            this.testOutputHelper = testOutputHelper;
            ClientPeer.AutomaticallyAttemptedPassword = password;
        }

        private void InitializationComplete()
        {
            IsConnected = true;
            testOutputHelper.WriteLine("Headless client successfully connected to server");
        }

        private void Disconnect(PeerDisconnectPacket packet)
        {
            testOutputHelper.WriteLine($"Headless client was disconnected with reason: {packet.DisconnectReason} ({packet.AdditionalInformation})");
            IsConnected = false;
        }

        private void OnMessageReceived(IReadMessage msg)
        {
            ClientPacketHeader header = (ClientPacketHeader)msg.ReadByte();
            testOutputHelper.WriteLine($"Headless client received message: {header}");
        }

        public void Update()
        {
            ClientPeer.Update((float)Timing.Step);
            TaskPool.Update();
            Timing.TotalTime += Timing.Step;
        }
    }
}