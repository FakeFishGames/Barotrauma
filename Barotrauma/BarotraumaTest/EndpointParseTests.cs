#nullable enable
using System.Net;
using Barotrauma;
using Xunit;
using Barotrauma.Networking;
using FluentAssertions;

namespace TestProject;

public class EndpointParseTests
{
    [Fact]
    public void TestLidgrenEndpoint()
    {
        Endpoint.Parse("127.0.0.1:27015")
            .Should()
            .BeOfType<Some<Endpoint>>()
            .And.BeEquivalentTo(
                Option<Endpoint>.Some(new LidgrenEndpoint(IPAddress.Loopback, 27015)),
                options => options.RespectingRuntimeTypes());
    }
    
    [Fact]
    public void TestLidgrenEndpointHostName()
    {
        Endpoint.Parse("localhost:27015")
            .Should()
            .BeOfType<Some<Endpoint>>()
            .And.BeEquivalentTo(
                Option<Endpoint>.Some(new LidgrenEndpoint(IPAddress.Loopback, 27015)),
                options => options.RespectingRuntimeTypes());
    }
    
    [Fact]
    public void TestLidgrenAddress()
    {
        Address.Parse("127.0.0.1")
            .Should()
            .BeOfType<Some<Address>>()
            .And.BeEquivalentTo(
                Option<Address>.Some(new LidgrenAddress(IPAddress.Loopback)),
                options => options.RespectingRuntimeTypes());
    }
    
    [Fact]
    public void TestSteamP2PEndpoint()
    {
        Endpoint.Parse("STEAM_1:1:508792388")
            .Should()
            .BeOfType<Some<Endpoint>>()
            .And.BeEquivalentTo(
                Option<Endpoint>.Some(new SteamP2PEndpoint(new SteamId(76561198977850505))),
                options => options.RespectingRuntimeTypes());
    }
    
    [Fact]
    public void TestSteamP2PAddress()
    {
        Address.Parse("STEAM_1:1:508792388")
            .Should()
            .BeOfType<Some<Address>>()
            .And.BeEquivalentTo(
                Option<Address>.Some(new SteamP2PAddress(new SteamId(76561198977850505))),
                options => options.RespectingRuntimeTypes());
        new SteamId(76561198977850505).StringRepresentation.Should().BeEquivalentTo("STEAM_1:1:508792388");
    }
}
