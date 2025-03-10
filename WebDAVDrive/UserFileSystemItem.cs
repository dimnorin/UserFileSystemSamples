﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using ITHit.FileSystem;
using ITHit.FileSystem.Samples.Common;
using ITHit.WebDAV.Client;

namespace WebDAVDrive
{
    /// <summary>
    /// Represents a file or a folder in the remote storage. Contains methods common for both files and folders.
    /// </summary>
    /// <remarks>You will change methods of this class to read/write data from/to your remote storage.</remarks>
    internal class UserFileSystemItem : IUserFileSystemItem
    {
        /// <summary>
        /// Path of this file of folder in the user file system.
        /// </summary>
        protected string UserFileSystemPath;

        /// <summary>
        /// Path of this file or folder in the remote storage.
        /// </summary>
        protected string RemoteStorageUri;

        /// <summary>
        /// Creates instance of this class.
        /// </summary>
        /// <param name="userFileSystemPath">Path of this file of folder in the user file system.</param>
        public UserFileSystemItem(string userFileSystemPath)
        {
            this.UserFileSystemPath = userFileSystemPath;
            this.RemoteStorageUri = Mapping.MapPath(userFileSystemPath);
        }

        /// <summary>
        /// Renames or moves file or folder to a new location in the remote storage.
        /// </summary>
        /// <param name="userFileSystemNewPath">Target path of this file or folder in the user file system.</param>
        public async Task MoveToAsync(string userFileSystemNewPath)
        {
            string remoteStorageOldPath = RemoteStorageUri;
            string remoteStorageNewPath = Mapping.MapPath(userFileSystemNewPath);

            await Program.DavClient.MoveToAsync(new Uri(remoteStorageOldPath), new Uri(remoteStorageNewPath), true);
        }

        /// <summary>
        /// Deletes this file or folder in the remote storage.
        /// </summary>
        public async Task DeleteAsync()
        {
            await Program.DavClient.DeleteAsync(new Uri(RemoteStorageUri));
        }

        /// <summary>
        /// Creates or updates file in the remote storage.
        /// </summary>
        /// <param name="remoteStorageUri">Uri of the file to be created or updated in the remote storage.</param>
        /// <param name="newInfo">New information about the file, such as modification date, attributes, custom data, etc.</param>
        /// <param name="mode">Specifies if a new file should be created or existing file should be updated.</param>
        /// <param name="content">New file content or null if the file content is not modified.</param>
        /// <param name="lockInfo">Information about the lock. Caller passes null if the item is not locked.</param>
        /// <returns>New ETag returned from the remote storage.</returns>
        protected async Task<string> CreateOrUpdateFileAsync(Uri remoteStorageUri, IFileBasicInfo newInfo, FileMode mode, Stream content = null, ServerLockInfo lockInfo = null)
        {
            string eTag = null;
            string lockToken = null;
            // Get ETag and lock-token here and send it to the remote storage with the new item content/info.
            if (mode == FileMode.Open)
            {
                // Get ETag.
                eTag = await ETag.GetETagAsync(UserFileSystemPath);

                // Get lock-token.
                lockToken = lockInfo?.LockToken;
            }

            if (content != null || mode == FileMode.CreateNew)
            {
                long contentLength = content != null ? content.Length : 0;

                IWebRequestAsync request = await Program.DavClient.GetFileWriteRequestAsync(remoteStorageUri, null, contentLength, 0, -1, lockToken, eTag);

                // Update remote storage file content.
                using (Stream davContentStream = await request.GetRequestStreamAsync())
                {
                    if (content != null)
                    {
                        await content.CopyToAsync(davContentStream);
                    }

                    // Get ETag returned by the server, if any.
                    IWebResponseAsync response = await request.GetResponseAsync();
                    eTag = response.Headers["ETag"];
                    response.Close();
                }

            }
            return eTag;
        }

        /// <summary>
        /// Locks the item in the remote storage.
        /// </summary>
        /// <returns>Lock info that conains lock-token returned by the remote storage.</returns>
        /// <remarks>
        /// Lock your item in the remote storage in this method and receive the lock-token.
        /// Return a new <see cref="ServerLockInfo"/> object with the <see cref="ServerLockInfo.LockToken"/> being set from this function.
        /// The <see cref="ServerLockInfo"/> will become available via methods parameter when the 
        /// item in the remote storage should be updated. Supply the lock-token during the update request in 
        /// <see cref="UserFile.UpdateAsync"/> and <see cref="UserFolder.UpdateAsync"/> method calls.
        /// </remarks>
        public async Task<ServerLockInfo> LockAsync()
        {
            LockInfo lockInfo = await Program.DavClient.LockAsync(new Uri(RemoteStorageUri), LockScope.Exclusive, false, null, TimeSpan.MaxValue);
            return new ServerLockInfo {
                LockToken = lockInfo.LockToken.LockToken,
                Exclusive = lockInfo.LockScope == LockScope.Exclusive,
                Owner = lockInfo.Owner,
                LockExpirationDateUtc = DateTimeOffset.Now.Add(lockInfo.TimeOut)
            };
        }

        /// <summary>
        /// Unlocks the item in the remote storage.
        /// </summary>
        /// <param name="lockToken">Lock token to unlock the item in the remote storage.</param>
        /// <remarks>
        /// Unlock your item in the remote storage in this method using the 
        /// <paramref name="lockToken"/> parameter.
        /// </remarks>
        public async Task UnlockAsync(string lockToken)
        {
            try
            {
                await Program.DavClient.UnlockAsync(new Uri(RemoteStorageUri), lockToken);
            }
            catch(ITHit.WebDAV.Client.Exceptions.ConflictException)
            {
                // The item is already unlocked.
            }
        }
    }
}
