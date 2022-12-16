#nullable enable
using System.Net;
using Barotrauma.Networking;
using Xunit;

namespace TestProject;

public class EndpointComparisonTests
{
    [Fact]
    public void TestLidgrenAddress()
    {
        Assert.True(new LidgrenAddress(IPAddress.Loopback) == new LidgrenAddress(IPAddress.IPv6Loopback));
    }
}