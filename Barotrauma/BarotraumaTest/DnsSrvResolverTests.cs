// Add extern alias to pick up WindowsClient reference
extern alias Client;

using System;
using Xunit;

namespace WindowsTest
{
    public class DnsSrvResolverTests
    {
        [Fact]
        public void DefaultTryResolve_NoSrvRecord_ReturnsFalse()
            => Assert.False(Client::Barotrauma.Networking.DnsSrvResolver.TryResolve("example.com", out _, out _));

        [Fact]
        public void OverloadedTryResolve_XmppJabber_ReturnsTrue()
        {
            // jabber.org provides a stable XMPP server SRV record
            bool ok = Client::Barotrauma.Networking.DnsSrvResolver.TryResolve(
                "jabber.org", "_xmpp-server", "_tcp", out var host, out var port);
            Assert.True(ok, "Expected SRV record for _xmpp-server._tcp.jabber.org");
            Assert.EndsWith("jabber.org", host);
            Assert.True(port > 0);
        }
    }
}