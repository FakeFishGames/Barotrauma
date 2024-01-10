using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Steamworks.Data;

namespace Steamworks
{
	/// <summary>
	/// Functions for accessing and manipulating Steam user information.
	/// This is also where the APIs for Steam Voice are exposed.
	/// </summary>
	public class SteamUGC : SteamSharedClass<SteamUGC>
	{
		internal static ISteamUGC? Internal => Interface as ISteamUGC;

		internal override bool InitializeInterface( bool server )
		{
			SetInterface( server, new ISteamUGC( server ) );
			if ( Interface is null || Interface.Self == IntPtr.Zero ) return false;

			InstallEvents( server );

			return true;
		}

		internal static void InstallEvents( bool server )
		{
			Dispatch.Install<DownloadItemResult_t>( x =>
			{
				if ( x.AppID == SteamClient.AppId )
				{
					OnDownloadItemResult?.Invoke( x.Result, x.PublishedFileId );
				}
			}, server);
			Dispatch.Install<RemoteStoragePublishedFileSubscribed_t>( x =>
			{
				if ( x.AppID == SteamClient.AppId )
				{
					OnItemSubscribed?.Invoke( x.AppID.Value, x.PublishedFileId );
				}
			}, server );
			Dispatch.Install<RemoteStoragePublishedFileUnsubscribed_t>( x =>
			{
				if ( x.AppID == SteamClient.AppId )
				{
					OnItemUnsubscribed?.Invoke( x.AppID.Value, x.PublishedFileId );
				}
			}, server );
			Dispatch.Install<ItemInstalled_t>( x =>
			{
				if ( x.AppID == SteamClient.AppId )
				{
					OnItemInstalled?.Invoke( x.AppID.Value, x.PublishedFileId );
				}
			}, server );
		}

		/// <summary>
		/// Invoked after an item is downloaded.
		/// </summary>
		public static event Action<Result, PublishedFileId>? OnDownloadItemResult;
		
		/// <summary>
		/// Invoked when a new item is subscribed.
		/// </summary>
		public static event Action<AppId, PublishedFileId>? OnItemSubscribed;
		public static event Action<AppId, PublishedFileId>? OnItemUnsubscribed;
		public static event Action<AppId, PublishedFileId>? OnItemInstalled;

		public static async Task<bool> DeleteFileAsync( PublishedFileId fileId )
		{
			if (Internal is null) { return false; }
			var r = await Internal.DeleteItem( fileId );
			return r?.Result == Result.OK;
		}

		/// <summary>
		/// Start downloading this item. You'll get notified of completion via <see cref="OnDownloadItemResult"/>.
		/// </summary>
		/// <param name="fileId">The ID of the file to download.</param>
		/// <param name="highPriority">If <see langword="true"/> this should go straight to the top of the download list.</param>
		/// <returns><see langword="true"/> if nothing went wrong and the download is started.</returns>
		public static bool Download( PublishedFileId fileId, bool highPriority = false )
		{
			return Internal != null && Internal.DownloadItem( fileId, highPriority );
		}

		/// <summary>
		/// Will attempt to download this item asyncronously - allowing you to instantly react to its installation.
		/// </summary>
		/// <param name="fileId">The ID of the file you download.</param>
		/// <param name="progress">An optional callback</param>
		/// <param name="ct">Allows to send a message to cancel the download anywhere during the process.</param>
		/// <param name="milisecondsUpdateDelay">How often to call the progress function.</param>
		/// <returns><see langword="true"/> if downloaded and installed properly.</returns>
		public static async Task<bool> DownloadAsync(
				PublishedFileId fileId,
				Action<float>? progress = null,
				int millisecondsUpdateDelay = 60,
				CancellationToken? ct = default )
		{
			var item = new Steamworks.Ugc.Item( fileId );

			var cancellationToken = ct ?? new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token;

			async Task waitOrCancel()
			{
				cancellationToken.ThrowIfCancellationRequested();
				await Task.Delay(millisecondsUpdateDelay);
			}
			
			progress?.Invoke( 0.0f );

			Result downloadStartResult = Result.None;

			void onDownloadFinished(Result r, PublishedFileId id)
			{
				if (id != item.Id) { return; }
				downloadStartResult = r;
			}
			OnDownloadItemResult += onDownloadFinished;

			if (!Download(fileId, highPriority: true)) { return item.IsInstalled; }

			await Task.Delay(500);

			try
			{
				while (true)
				{
					cancellationToken.ThrowIfCancellationRequested();

					progress?.Invoke(item.DownloadAmount);

					if (downloadStartResult != Result.None)
					{
						if (downloadStartResult != Result.OK) { return false; }
						break;
					}

					if (!item.IsDownloadPending && !item.IsDownloading)
					{
						if (item.IsInstalled)
						{
							break;
						}
						if (!Download(fileId, highPriority: true))
						{
							return item.IsInstalled;
						}
					}

					await Task.Delay( millisecondsUpdateDelay );
				}
			}
			finally
			{
				OnDownloadItemResult -= onDownloadFinished;
			}

			progress?.Invoke( 1.0f );

			return item.IsInstalled;
		}

		/// <summary>
		/// Utility function to fetch a single item. Internally this uses <c>Ugc.FileQuery</c> -
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

		public static async Task<bool> StartPlaytimeTracking(PublishedFileId fileId)
		{
			if (Internal is null) { return false; }
			var result = await Internal.StartPlaytimeTracking(new[] {fileId}, 1);
			return result?.Result == Result.OK;
		}
		
		public static async Task<bool> StopPlaytimeTracking(PublishedFileId fileId)
		{
			if (Internal is null) { return false; }
			var result = await Internal.StopPlaytimeTracking(new[] {fileId}, 1);
			return result?.Result == Result.OK;
		}
		
		public static async Task<bool> StopPlaytimeTrackingForAllItems()
		{
			if (Internal is null) { return false; }
			var result = await Internal.StopPlaytimeTrackingForAllItems();
			return result?.Result == Result.OK;
		}

		public static uint NumSubscribedItems { get { return Internal?.GetNumSubscribedItems() ?? 0; } }

		public static PublishedFileId[] GetSubscribedItems()
		{
			if (Internal is null) { return Array.Empty<PublishedFileId>(); }
			uint numSubscribed = NumSubscribedItems;
			PublishedFileId[] ids = new PublishedFileId[numSubscribed];
			Internal.GetSubscribedItems(ids, numSubscribed);
			return ids;
		}

		/// <summary>
		/// Suspends all workshop downloads.
		/// Downloads will be suspended until you resume them by calling <see cref="ResumeDownloads"/> or when the game ends.
		/// </summary>
		public static void SuspendDownloads() => Internal?.SuspendDownloads(true);

		/// <summary>
		/// Resumes all workshop downloads.
		/// </summary>
		public static void ResumeDownloads() => Internal?.SuspendDownloads(false);

		/// <summary>
		/// Show the app's latest Workshop EULA to the user in an overlay window, where they can accept it or not.
		/// </summary>
		public static bool ShowWorkshopEula()
		{
			return Internal != null && Internal.ShowWorkshopEULA();
		}

		/// <summary>
		/// Retrieve information related to the user's acceptance or not of the app's specific Workshop EULA.
		/// </summary>
		public static async Task<bool?> GetWorkshopEulaStatus()
		{
			if ( Internal is null ) { return null; }
			var status = await Internal.GetWorkshopEULAStatus();
			return status?.Accepted;
		}

	}
}

