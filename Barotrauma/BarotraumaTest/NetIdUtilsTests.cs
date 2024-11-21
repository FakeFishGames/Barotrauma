extern alias Client;
using System;
using Client::Barotrauma.Networking;
using FsCheck;
using Xunit;

namespace TestProject;

public class NetIdUtilsTests
{
    [Fact]
    public void TestGetIdOlderThan()
    {
        Prop.ForAll<UInt16>(id =>
        {
            var olderId = NetIdUtils.GetIdOlderThan(id);
            Assert.True(NetIdUtils.IdMoreRecent(id, olderId));
        }).QuickCheckThrowOnFailure();
    }
}