﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using ITHit.FileSystem;
using ITHit.FileSystem.Samples.Common;
using ITHit.WebDAV.Client;

namespace WebDAVDrive
{
    /// <summary>
    /// Represents a file in the remote storage.
    /// </summary>
    /// <remarks>You will change methods of this class to read/write data from/to your remote storage.</remarks>
    internal class UserFile : UserFileSystemItem, IUserFile
    {
        /// <summary>
        /// Creates instance of this class.
        /// </summary>
        /// <param name="userFileSystemFilePath">Path of this file in the user file system.</param>
        /// <param name="lockInfo">Information about file lock. Pass null if the item is not locked.</param>
        public UserFile(string userFileSystemFilePath) : base(userFileSystemFilePath)
        {

        }

        /// <summary>
        /// Reads file content from the remote storage.
        /// </summary>
        /// <param name="offset">Offset in bytes in file content to start reading from.</param>
        /// <param name="length">Lenth in bytes of the file content to read.</param>
        /// <returns>File content that corresponds to the provided offset and length.</returns>
        public async Task<byte[]> ReadAsync(long offset, long length)
        {
            // This method has a 60 sec timeout. 
            // To process longer requests modify the IFolder.TransferDataAsync() implementation.

            IFileAsync file = await Program.DavClient.OpenFileAsync(RemoteStorageUri);
            using (Stream stream = await file.GetReadStreamAsync(offset, length))
            {
                byte[] buffer = new byte[length];
                int bufferPos = 0;
                int bytesRead = 0;
                while ((bytesRead = await stream.ReadAsync(buffer, bufferPos, (int)length)) > 0)
                {
                    bufferPos += bytesRead;
                    length -= bytesRead;
                }
                return buffer;
            }
        }

        public async Task<bool> ValidateDataAsync(long offset, long length)
        {
            return true;
        }

        /// <summary>
        /// Updates file in the remote storage.
        /// </summary>
        /// <param name="fileInfo">New information about the file, such as creation date, modification date, attributes, etc.</param>
        /// <param name="content">New file content or null if the file content is not modified.</param>
        /// <param name="lockInfo">Information about the lock. Caller passes null if the item is not locked.</param>
        /// <returns>New ETag returned from the remote storage.</returns>
        public async Task<string> UpdateAsync(IFileBasicInfo fileInfo, Stream content = null, ServerLockInfo lockInfo = null)
        {
            return await CreateOrUpdateFileAsync(new Uri(RemoteStorageUri), fileInfo, FileMode.Open, content, lockInfo);
        }
    }
}
