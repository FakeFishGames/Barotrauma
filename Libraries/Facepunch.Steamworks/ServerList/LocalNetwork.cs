using System;

namespace Steamworks.ServerList
{
	public class LocalNetwork : Base
	{
		internal override void LaunchQuery()
		{
			if (Internal is null) { return; }
			request = Internal.RequestLANServerList( AppId.Value, IntPtr.Zero );
		}
	}
}
