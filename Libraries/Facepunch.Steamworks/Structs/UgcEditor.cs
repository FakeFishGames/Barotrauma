using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Steamworks.Data;

using QueryType = Steamworks.Ugc.Query;

namespace Steamworks.Ugc
{
    public struct Editor
    {
        public PublishedFileId FileId { get; private set; }

		bool creatingNew;
        WorkshopFileType creatingType;
        AppId consumerAppId;

        internal Editor(WorkshopFileType filetype) : this()
        {
            this.creatingNew = true;
            this.creatingType = filetype;
        }

        public Editor(PublishedFileId fileId) : this()
        {
            this.FileId = fileId;
        }

        /// <summary>
        /// Create a Normal Workshop item that can be subscribed to
        /// </summary>
        public static Editor CreateCommunityFile() { return new Editor(WorkshopFileType.Community); }

        /// <summary>
        /// Workshop item that is meant to be voted on for the purpose of selling in-game
        /// </summary>
        public static Editor CreateMicrotransactionFile() { return new Editor(WorkshopFileType.Microtransaction ); }
		
		public Editor ForAppId( AppId id ) { this.consumerAppId = id; return this; }

		public string Title { get; private set; }
		public Editor WithTitle( string t ) { this.Title = t; return this; }

		public string Description { get; private set; }
		public Editor WithDescription( string t ) { this.Description = t; return this; }

		string MetaData;
		public Editor WithMetaData( string t ) { this.MetaData = t; return this; }

		string ChangeLog;
		public Editor WithChangeLog( string t ) { this.ChangeLog = t; return this; }

		string Language;
		public Editor InLanguage( string t ) { this.Language = t; return this; }

		public string PreviewFile { get; private set; }
		public Editor WithPreviewFile( string t ) { this.PreviewFile = t; return this; }

		public System.IO.DirectoryInfo ContentFolder { get; private set; }
		public Editor WithContent( System.IO.DirectoryInfo t ) { this.ContentFolder = t; return this; }
		public Editor WithContent( string folderName ) { return WithContent( new System.IO.DirectoryInfo( folderName ) ); }

		RemoteStoragePublishedFileVisibility? Visibility;

		public Editor WithPublicVisibility() { Visibility = RemoteStoragePublishedFileVisibility.Public; return this; }
		public Editor WithFriendsOnlyVisibility() { Visibility = RemoteStoragePublishedFileVisibility.FriendsOnly; return this; }
		public Editor WithPrivateVisibility() { Visibility = RemoteStoragePublishedFileVisibility.Private; return this; }

		public bool IsPublic => Visibility == RemoteStoragePublishedFileVisibility.Public;
		public bool IsFriendsOnly => Visibility == RemoteStoragePublishedFileVisibility.FriendsOnly;
		public bool IsPrivate => Visibility == RemoteStoragePublishedFileVisibility.Private;

		public List<string> Tags { get; private set; }
		public Editor WithTag( string tag )
		{
			if ( Tags == null ) Tags = new List<string>();

			Tags.Add( tag );

			return this;
		}

        public Editor WithTags( IEnumerable<string> tags )
        {
            if (Tags == null) Tags = new List<string>();

            Tags.AddRange(tags);

            return this;
        }

		public async Task<PublishResult> SubmitAsync( IProgress<float> progress = null )
		{
			var result = default( PublishResult );

			progress?.Report( 0 );

			if ( consumerAppId == 0 )
				consumerAppId = SteamClient.AppId;

			//
			// Item Create
			//
			if ( creatingNew )
			{
				result.Result = Steamworks.Result.Fail;

				var created = await SteamUGC.Internal.CreateItem( consumerAppId, creatingType );
				if ( !created.HasValue ) return result;

				result.Result = created.Value.Result;

				if ( result.Result != Steamworks.Result.OK )
					return result;

                FileId = created.Value.PublishedFileId;
				result.NeedsWorkshopAgreement = created.Value.UserNeedsToAcceptWorkshopLegalAgreement;
				result.FileId = FileId;
			}

			result.FileId = FileId;

			//
			// Item Update
			//
			{
				var handle = SteamUGC.Internal.StartItemUpdate( consumerAppId, FileId);
				if ( handle == 0xffffffffffffffff )
					return result;

				if ( Title != null ) SteamUGC.Internal.SetItemTitle( handle, Title );
				if ( Description != null ) SteamUGC.Internal.SetItemDescription( handle, Description );
				if ( MetaData != null ) SteamUGC.Internal.SetItemMetadata( handle, MetaData );
				if ( Language != null ) SteamUGC.Internal.SetItemUpdateLanguage( handle, Language );
				if ( ContentFolder != null ) SteamUGC.Internal.SetItemContent( handle, ContentFolder.FullName );
				if ( PreviewFile != null ) SteamUGC.Internal.SetItemPreview( handle, PreviewFile );
				if ( Visibility.HasValue ) SteamUGC.Internal.SetItemVisibility( handle, Visibility.Value );
				if ( Tags != null && Tags.Count > 0 )
				{
					using ( var a = SteamParamStringArray.From( Tags.ToArray() ) )
					{
						var val = a.Value;
						SteamUGC.Internal.SetItemTags( handle, ref val );
					}
				}

				result.Result = Steamworks.Result.Fail;

				if ( ChangeLog == null )
					ChangeLog = "";

			   var updating = SteamUGC.Internal.SubmitItemUpdate( handle, ChangeLog );

				while ( !updating.IsCompleted )
				{
					if ( progress != null )
					{
						ulong total = 0;
						ulong processed = 0;

						var r = SteamUGC.Internal.GetItemUpdateProgress( handle, ref processed, ref total );

						switch ( r )
						{
							case ItemUpdateStatus.PreparingConfig:
								{
									progress?.Report( 0.1f );
									break;
								}

							case ItemUpdateStatus.PreparingContent:
								{
									progress?.Report( 0.2f );
									break;
								}
							case ItemUpdateStatus.UploadingContent:
								{
									var uploaded = total > 0 ? ((float)processed / (float)total) : 0.0f;
									progress?.Report( 0.2f + uploaded * 0.7f );
									break;
								}
							case ItemUpdateStatus.UploadingPreviewFile:
								{
									progress?.Report( 8f );
									break;
								}
							case ItemUpdateStatus.CommittingChanges:
								{
									progress?.Report( 1 );
									break;
								}
						}
					}

					await Task.Delay( 1000 / 60 );
				}

				progress?.Report( 1 );

				var updated = updating.Result;

				if ( !updated.HasValue ) return result;

				result.Result = updated.Value.Result;

				if ( result.Result != Steamworks.Result.OK )
					return result;

				result.NeedsWorkshopAgreement = updated.Value.UserNeedsToAcceptWorkshopLegalAgreement;
				result.FileId = FileId;

			}

			return result;
		}
	}

	public struct PublishResult
	{
		public bool Success => Result == Steamworks.Result.OK;

		public Steamworks.Result Result;
		public PublishedFileId FileId;

		/// <summary>
		/// https://partner.steamgames.com/doc/features/workshop/implementation#Legal
		/// </summary>
		public bool NeedsWorkshopAgreement;
	}
}