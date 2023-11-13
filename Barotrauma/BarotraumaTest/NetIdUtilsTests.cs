using System;
using Barotrauma.Networking;
using FluentAssertions;
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