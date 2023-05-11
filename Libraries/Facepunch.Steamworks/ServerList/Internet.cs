﻿using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Steamworks.ServerList
{
	public class Internet : Base
	{
		internal override void LaunchQuery()
		{
			if (Internal is null) { return; }
			var filters = GetFilters();
			request = Internal.RequestInternetServerList( AppId.Value, filters, (uint)filters.Length, IntPtr.Zero );
		}
	}
}