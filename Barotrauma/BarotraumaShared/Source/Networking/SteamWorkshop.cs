using System.Collections.Generic;
using Steamworks;

namespace Barotrauma.Networking
{
    /// <summary>
    /// This class was created by Vitor Pêgas on 01/01/2017
    /// </summary>
    public class SteamWorkshop
    {
        private CallResult<RemoteStoragePublishFileResult_t> RemoteStoragePublishFileResult;
        private CallResult<RemoteStorageEnumerateUserSubscribedFilesResult_t> RemoteStorageEnumerateUserSubscribedFilesResult;
        private CallResult<RemoteStorageGetPublishedFileDetailsResult_t> RemoteStorageGetPublishedFileDetailsResult;
        private CallResult<RemoteStorageDownloadUGCResult_t> RemoteStorageDownloadUGCResult;
        private CallResult<RemoteStorageUnsubscribePublishedFileResult_t> RemoteStorageUnsubscribePublishedFileResult;

        private PublishedFileId_t publishedFileID;
        private UGCHandle_t UGCHandle;

        public List<PublishedFileId_t> subscribedItemList;

        public bool FetchedContent;
        private string itemContent;

        private string lastFileName;

        public SteamWorkshop()
        {
            subscribedItemList = new List<PublishedFileId_t>();
        }

        void OnEnable()
        {
            RemoteStoragePublishFileResult = CallResult<RemoteStoragePublishFileResult_t>.Create(OnRemoteStoragePublishFileResult);
            RemoteStorageEnumerateUserSubscribedFilesResult = CallResult<RemoteStorageEnumerateUserSubscribedFilesResult_t>.Create(OnRemoteStorageEnumerateUserSubscribedFilesResult);
            RemoteStorageGetPublishedFileDetailsResult = CallResult<RemoteStorageGetPublishedFileDetailsResult_t>.Create(OnRemoteStorageGetPublishedFileDetailsResult);
            RemoteStorageDownloadUGCResult = CallResult<RemoteStorageDownloadUGCResult_t>.Create(OnRemoteStorageDownloadUGCResult);
            RemoteStorageUnsubscribePublishedFileResult = CallResult<RemoteStorageUnsubscribePublishedFileResult_t>.Create(OnRemoteStorageUnsubscribePublishedFileResult);
        }

        public string GetContent()
        {
            return itemContent;
        }

        public void GetSubscribedItems()
        {
            SteamAPICall_t handle = SteamRemoteStorage.EnumerateUserSubscribedFiles(0);
            RemoteStorageEnumerateUserSubscribedFilesResult.Set(handle);
        }

        /// <summary>
        /// Gets the Item content (subscribed) to variable itemContent
        /// When done downloading, fetchedContent will be TRUE.
        /// </summary>
        /// <param name="ItemID"></param>
        public void GetItemContent(int ItemID)
        {
            FetchedContent = false;
            publishedFileID = subscribedItemList[ItemID];

            SteamAPICall_t handle = SteamRemoteStorage.GetPublishedFileDetails(publishedFileID, 0);
            RemoteStorageGetPublishedFileDetailsResult.Set(handle);
        }

        public void DeleteFile(string filename)
        {
            bool ret = SteamRemoteStorage.FileDelete(filename);
        }

        /// <summary>
        /// This functions saves a file to the workshop.
        /// Make sure file size doesn't pass the steamworks limit on your app settings.
        /// </summary>
        /// <param name="fileName">File Name (actual physical file) example: map.txt</param>
        /// <param name="fileData">File Data (actual file data)</param>
        /// <param name="workshopTitle">Workshop Item Title</param>
        /// <param name="workshopDescription">Workshop Item Description</param>
        /// <param name="tags">Tags</param>
        public void SaveToWorkshop(string fileName, string fileData, string workshopTitle, string workshopDescription, string[] tags)
        {
            lastFileName = fileName;
            bool fileExists = SteamRemoteStorage.FileExists(fileName);

            if (fileExists)
            {
                DebugConsole.Log("A file already exists with that name!");
            }
            else
            {
                //Try to upload to Steam Cloud
                bool upload = UploadFile(fileName, fileData);

                if (!upload)
                {
                    DebugConsole.Log("Upload failed!");
                }
                else
                {
                    UploadToWorkshop(fileName, workshopTitle, workshopDescription, tags);
                }
            }
        }

        private bool UploadFile(string fileName, string fileData)
        {
            byte[] Data = new byte[System.Text.Encoding.UTF8.GetByteCount(fileData)];
            System.Text.Encoding.UTF8.GetBytes(fileData, 0, fileData.Length, Data, 0);
            bool ret = SteamRemoteStorage.FileWrite(fileName, Data, Data.Length);

            return ret;
        }

        private void UploadToWorkshop(string fileName, string workshopTitle, string workshopDescription, string[] tags)
        {
            SteamAPICall_t handle = SteamRemoteStorage.PublishWorkshopFile(fileName, null, SteamUtils.GetAppID(), workshopTitle, workshopDescription, ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic, tags, EWorkshopFileType.k_EWorkshopFileTypeCommunity);
            RemoteStoragePublishFileResult.Set(handle);
        }

        public void Unsubscribe(PublishedFileId_t file)
        {
            SteamAPICall_t handle = SteamRemoteStorage.UnsubscribePublishedFile(file);
            RemoteStorageUnsubscribePublishedFileResult.Set(handle);
        }


        ///CallBacks

        void OnRemoteStorageUnsubscribePublishedFileResult(RemoteStorageUnsubscribePublishedFileResult_t pCallback, bool bIOFailure)
        {
            DebugConsole.Log("[" + RemoteStorageUnsubscribePublishedFileResult_t.k_iCallback + " - RemoteStorageUnsubscribePublishedFileResult] - " + pCallback.m_eResult + " -- " + pCallback.m_nPublishedFileId);
        }

        void OnRemoteStoragePublishFileResult(RemoteStoragePublishFileResult_t pCallback, bool bIOFailure)
        {
            if (pCallback.m_eResult == EResult.k_EResultOK)
            {
                publishedFileID = pCallback.m_nPublishedFileId;
                DeleteFile(lastFileName);
            }
        }

        void OnRemoteStorageEnumerateUserSubscribedFilesResult(RemoteStorageEnumerateUserSubscribedFilesResult_t pCallback, bool bIOFailure)
        {
            subscribedItemList = new List<PublishedFileId_t>();

            for (int i = 0; i < pCallback.m_nTotalResultCount; i++)
            {
                PublishedFileId_t f = pCallback.m_rgPublishedFileId[i];
                DebugConsole.Log(f.ToString());
                subscribedItemList.Add(f);
            }
        }

        void OnRemoteStorageGetPublishedFileDetailsResult(RemoteStorageGetPublishedFileDetailsResult_t pCallback, bool bIOFailure)
        {
            if (pCallback.m_eResult == EResult.k_EResultOK)
            {
                UGCHandle = pCallback.m_hFile;
                SteamAPICall_t handle = SteamRemoteStorage.UGCDownload(UGCHandle, 0);
                RemoteStorageDownloadUGCResult.Set(handle);
            }
        }

        void OnRemoteStorageDownloadUGCResult(RemoteStorageDownloadUGCResult_t pCallback, bool bIOFailure)
        {
            byte[] Data = new byte[pCallback.m_nSizeInBytes];
            int ret = SteamRemoteStorage.UGCRead(UGCHandle, Data, pCallback.m_nSizeInBytes, 0, EUGCReadAction.k_EUGCRead_Close);

            itemContent = System.Text.Encoding.UTF8.GetString(Data, 0, ret);

            FetchedContent = true;
            DebugConsole.Log("content:" + itemContent);
        }
    }
}
