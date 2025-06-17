using System;

namespace Steamworks.ServerList
{
	public class Internet : Base
	{
		internal override void LaunchQuery()
		{
			if ( Internal is null ) { return; }
			using var filters = new ServerFilterMarshaler( GetFilters() );
			request = Internal.RequestInternetServerList( AppId.Value, filters.Pointer, (uint)filters.Count, IntPtr.Zero );
		}
	}
}
