using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

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