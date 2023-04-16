using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Steamworks
{
	internal static class SourceServerQuery
	{
		private enum Status
		{
			Pending,
			Failure,
			Success
		}

		private static readonly HashSet<SteamMatchmakingRulesResponse> ruleResponseHandlers
			= new HashSet<SteamMatchmakingRulesResponse>();
		
		internal static async Task<Dictionary<string, string>> GetRules(Steamworks.Data.ServerInfo server)
		{
			Status status = Status.Pending;

			var rules = new Dictionary<string, string>();

			SteamMatchmakingRulesResponse responseHandler = null;

			void onRulesResponded(string key, string value)
				=> rules.Add(key, value);

			void onRulesFailToRespond()
			{
				finish(Status.Failure);
			}

			void onRulesRefreshComplete()
			{
				finish(Status.Success);
			}

			void finish(Status stat)
			{
				if (status == Status.Pending) { status = stat; }

				var handler = responseHandler;
				if (handler is null) { return; }

				lock (ruleResponseHandlers)
				{
					ruleResponseHandlers.Remove(handler);
				}
				responseHandler = null;
			}

			responseHandler = new SteamMatchmakingRulesResponse(
				onRulesResponded,
				onRulesFailToRespond,
				onRulesRefreshComplete);
			lock (ruleResponseHandlers)
			{
				ruleResponseHandlers.Add(responseHandler);
			}

			var query = SteamMatchmakingServers.Internal.ServerRules(
				server.AddressRaw, (ushort)server.QueryPort, (IntPtr)responseHandler);

			while (status == Status.Pending)
			{
				await Task.Delay(25);
			}

			SteamMatchmakingServers.Internal.CancelServerQuery(query);

			return status == Status.Success ? rules : null;
		}
	};

}
