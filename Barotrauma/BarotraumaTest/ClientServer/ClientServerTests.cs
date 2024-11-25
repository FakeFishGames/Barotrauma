extern alias Client;
extern alias Server;
using System;
using System.Linq;
using System.Net;
using System.Threading;
using FsCheck;
using Server::Barotrauma;
using Server::Barotrauma.Networking;
using Xunit;
using Xunit.Abstractions;
using DeliveryMethod = Barotrauma.Networking.DeliveryMethod;
using Option = Barotrauma.Option;
using PeerDisconnectPacket = Client::Barotrauma.Networking.PeerDisconnectPacket;
using Random = System.Random;
using WriteOnlyMessage = Client::Barotrauma.Networking.WriteOnlyMessage;


namespace TestProject;

public class ClientServerTests : IDisposable
{
    private readonly ITestOutputHelper testOutputHelper;

    private readonly GameMain serverGame;
    private GameServer? currentServer;
    private readonly string generatedPassword;

    private const string ServerName = "UnitTestServer";

    public ClientServerTests(ITestOutputHelper testOutputHelper)
    {
        this.testOutputHelper = testOutputHelper;

        serverGame = new GameMain(Array.Empty<string>());
        serverGame.Init();
        var random = new Random();
        generatedPassword = new string(Enumerable.Range(0, 24).Select(_ => (char)random.Next(minValue: 'A', maxValue: 'z')).ToArray());
    }

    [Fact]
    public void TestLidgren()
    {
        var server = CreateServer();
        var client = CreateClient(server);
        Prop.ForAll<byte[]>(data =>
        {
            var msg = new WriteOnlyMessage();
            foreach (byte b in data)
            {
                msg.WriteByte(b);
            }
            client.ClientPeer.Send(msg, DeliveryMethod.Reliable);
            string logBytes = ToolBox.BytesToHexString(data);
            if (logBytes.Length > 10) { logBytes = logBytes[..10] + "..."; }
            testOutputHelper.WriteLine($"Sent {data.Length} byte(s) to server: {logBytes}");
            client.Update();
            UpdateServer(server);

            // some data will make the client disconnect,
            // for example, if the generated data lands on a disconnect packet
            // so don't treat this as a failure, just reconnect
            if (!client.IsConnected)
            {
                client = CreateClient(server);
            }
        }).VerboseCheckThrowOnFailure();

        Thread.Sleep(1000);
        client.ClientPeer.Close(PeerDisconnectPacket.Custom("Test complete"));
    }

    private HeadlessNetworkClient CreateClient(GameServer server)
    {
        var client = new HeadlessNetworkClient(IPAddress.Loopback, server.Port, generatedPassword, testOutputHelper);

        while (!client.IsConnected)
        {
            client.Update();
            UpdateServer(server);
            Thread.Sleep(17);
        }

        return client;
    }

    private GameServer CreateServer()
    {
        if (currentServer != null)
        {
            serverGame.CloseServer();
        }

        currentServer = new GameServer(
            name: ServerName,
            port: NetConfig.DefaultPort,
            queryPort: NetConfig.DefaultQueryPort,
            maxPlayers: 1,
            password: generatedPassword,
            isPublic: false,
            attemptUPnP: false,
            ownerKey: Option.None,
            ownerEndpoint: Option.None);

        GameMain.Server = currentServer;
        currentServer.StartServer(registerToServerList: false);
        return currentServer;
    }

    private void UpdateServer(GameServer server)
    {
        server.Update((float)Timing.Step);
        Timing.TotalTime += Timing.Step;
    }

    ~ClientServerTests()
    {
        OnTestsFinished();
    }

    private void OnTestsFinished()
        => serverGame.CloseServer();

    public void Dispose()
    {
        OnTestsFinished();
        GC.SuppressFinalize(this);
    }
}