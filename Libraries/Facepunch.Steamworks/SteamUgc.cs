using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Steamworks.Data;

namespace Steamworks
{
	/// <summary>
	/// Functions for accessing and manipulating Steam user information.
	/// This is also where the APIs for Steam Voice are exposed.
	/// </summary>
	public static class SteamUGC
	{
		static ISteamUGC _internal;
		internal static ISteamUGC Internal
		{
			get
			{
				if ( _internal == null )
				{
					_internal = new ISteamUGC();
					_internal.Init();
				}

				return _internal;
			}
		}

		internal static void Shutdown()
		{
			_internal = null;
		}

        internal static void InstallEvents(bool server=false)
        {
            ItemInstalled_t.Install(x => {
                if (x.AppID == SteamClient.AppId) 
                {
                    if (onItemInstalled?.ContainsKey(x.PublishedFileId) ?? false)
                    {
                        onItemInstalled[x.PublishedFileId]?.Invoke();
                        onItemInstalled.Remove(x.PublishedFileId);
                    }
                }
            }, server);
        }

		public static async Task<bool> DeleteFileAsync( PublishedFileId fileId )
		{
			var r = await Internal.DeleteItem( fileId );
			return r?.Result == Result.OK;
		}

		public static bool Download( PublishedFileId fileId, Action onInstalled = null, bool highPriority = false )
		{
            if (onInstalled != null)
            {
                onItemInstalled ??= new Dictionary<PublishedFileId, Action>();
                if (!onItemInstalled.ContainsKey(fileId))
                {
                    onItemInstalled.Add(fileId, onInstalled);
                }
                else
                {
                    onItemInstalled[fileId] += onInstalled;
                }
            }
			return Internal.DownloadItem( fileId, highPriority );
		}

		/// <summary>
		/// Utility function to fetch a single item. Internally this uses Ugc.FileQuery -
		/// which you can use to query multiple items if you need to.
		/// </summary>
		public static async Task<Ugc.Item?> QueryFileAsync( PublishedFileId fileId )
		{
			var result = await Ugc.Query.All
									.WithFileId( fileId )
									.GetPageAsync( 1 );

			if ( !result.HasValue || result.Value.ResultCount != 1 )
				return null;

			var item = result.Value.Entries.First();

			result.Value.Dispose();

			return item;
		}

        private static Dictionary<PublishedFileId, Action> onItemInstalled;
    }
}