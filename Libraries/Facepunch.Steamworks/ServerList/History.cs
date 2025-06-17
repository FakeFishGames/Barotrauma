using System;

namespace Steamworks.ServerList
{
	public class History : Base
	{
		internal override void LaunchQuery()
		{
			if (Internal is null) { return; }
			using var filters = new ServerFilterMarshaler( GetFilters() );
			request = Internal.RequestHistoryServerList( AppId.Value, filters.Pointer, (uint)filters.Count, IntPtr.Zero );
		}
	}
}
