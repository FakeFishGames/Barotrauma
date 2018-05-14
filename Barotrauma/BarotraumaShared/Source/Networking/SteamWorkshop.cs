using System;
using System.Collections.Generic;
using System.IO;
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
        
        private List<RemoteStorageGetPublishedFileDetailsResult_t> subscribedItemDetails;
        private Action<List<RemoteStorageGetPublishedFileDetailsResult_t>> onDetailsReceived;
        private Action<int> onItemCountReceived;

        public SteamWorkshop()
        {
            subscribedItemList = new List<PublishedFileId_t>();
            subscribedItemDetails = new List<RemoteStorageGetPublishedFileDetailsResult_t>();

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

        public void GetSubscribedItemDetails(Action<int> onItemCountReceived, Action<List<RemoteStorageGetPublishedFileDetailsResult_t>> onDetailsReceived)
        {
            subscribedItemDetails.Clear();
            this.onDetailsReceived = onDetailsReceived;
            this.onItemCountReceived = onItemCountReceived;
            SteamAPICall_t handle = SteamRemoteStorage.EnumerateUserSubscribedFiles(0);
            RemoteStorageEnumerateUserSubscribedFilesResult.Set(handle);
        }

        public void DownloadSubscribedItem(RemoteStorageGetPublishedFileDetailsResult_t item)
        {
            UGCHandle = item.m_hFile;
            SteamAPICall_t handle = SteamRemoteStorage.UGCDownload(UGCHandle, 0);
            RemoteStorageDownloadUGCResult.Set(handle);
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
        public void SaveToWorkshop(string fileName, string filePath, string workshopTitle, string workshopDescription, string[] tags)
        {
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(filePath);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Could not upload the file \"" + fileName + "\" to Steam Workshop. Reading the file \"" + filePath + "\" failed.", e);
                return;
            }
            SaveToWorkshop(fileName, bytes, workshopTitle, workshopDescription, tags);
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
        public void SaveToWorkshop(string fileName, byte[] fileData, string workshopTitle, string workshopDescription, string[] tags)
        {
            lastFileName = fileName;
            bool fileExists = SteamRemoteStorage.FileExists(fileName);

            if (fileExists)
            {
#if CLIENT
                new GUIMessageBox("Upload failed", "Could not upload the file \"" + fileName + "\" to Steam Workshop. A file with the same name has already been uploaded.");
#else

                DebugConsole.ThrowError("Could not upload the file \""+fileName+"\" to Steam Workshop. A file with the same name has already been uploaded.");
#endif
                return;
            }

            //Try to upload to Steam Cloud
            bool upload = UploadFile(fileName, fileData);
            if (!upload)
            {
                DebugConsole.ThrowError("Upload failed!");
                return;
            }
            try
            {
                UploadToWorkshop(fileName, workshopTitle, workshopDescription, tags);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error while uploading the file \"" + fileName + "\" to Steam Workshop.", e);
            }
        }

        private bool UploadFile(string fileName, byte[] fileData)
        {
            bool ret = SteamRemoteStorage.FileWrite(fileName, fileData, fileData.Length);

            return ret;
        }

        private void UploadToWorkshop(string fileName, string workshopTitle, string workshopDescription, string[] tags)
        {
            SteamAPICall_t handle = SteamRemoteStorage.PublishWorkshopFile(
                fileName,
                null,
                SteamUtils.GetAppID(),
                workshopTitle,
                workshopDescription,
                ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic,
                tags,
                EWorkshopFileType.k_EWorkshopFileTypeCommunity);

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
            onItemCountReceived?.Invoke(pCallback.m_nTotalResultCount);
            for (int i = 0; i < pCallback.m_nTotalResultCount; i++)
            {
                PublishedFileId_t f = pCallback.m_rgPublishedFileId[i];
                DebugConsole.Log(f.ToString());
                subscribedItemList.Add(f);
            }
        }

        private void OnRemoteStorageGetPublishedFileDetailsResult(RemoteStorageGetPublishedFileDetailsResult_t pCallback, bool bIOFailure)
        {
            if (pCallback.m_eResult == EResult.k_EResultOK)
            {
                subscribedItemDetails.Add(pCallback);
                onDetailsReceived?.Invoke(subscribedItemDetails);
            }
        }

        private void OnRemoteStorageDownloadUGCResult(RemoteStorageDownloadUGCResult_t pCallback, bool bIOFailure)
        {
            byte[] Data = new byte[pCallback.m_nSizeInBytes];
            int ret = SteamRemoteStorage.UGCRead(UGCHandle, Data, pCallback.m_nSizeInBytes, 0, EUGCReadAction.k_EUGCRead_Close);

            itemContent = System.Text.Encoding.UTF8.GetString(Data, 0, ret);

            FetchedContent = true;
            DebugConsole.Log("content:" + itemContent);
        }
    }
}
