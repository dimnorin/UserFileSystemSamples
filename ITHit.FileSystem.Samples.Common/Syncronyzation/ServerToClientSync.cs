﻿using ITHit.FileSystem;
using ITHit.FileSystem.Windows;
using log4net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Provider;

namespace ITHit.FileSystem.Samples.Common.Syncronyzation
{
    /// <summary>
    /// Synchronizes files and folders from remote storage to user file system.
    /// </summary>
    /// <remarks>In most cases you can use this class in your project without any changes.</remarks>
    internal class ServerToClientSync : Logger
    {
        /// <summary>
        /// Virtual drive.
        /// </summary>
        private VirtualDriveBase virtualDrive;

        /// <summary>
        /// Creates instance of this class.
        /// </summary>
        /// <param name="virtualDrive"><see cref="VirtualDriveBase"/> instance.</param>
        /// <param name="log">Logger.</param>
        internal ServerToClientSync(VirtualDriveBase virtualDrive, ILog log) : base("UFS <- RS Sync", log)
        {
            this.virtualDrive = virtualDrive;
        }

        /// <summary>
        /// Recursively synchronizes all files and folders from server to client. 
        /// Synchronizes only folders already loaded into the user file system.
        /// </summary>
        /// <param name="userFileSystemFolderPath">Folder path in user file system.</param>
        internal async Task SyncronizeFolderAsync(string userFileSystemFolderPath)
        {
            // In case of on-demand loading the user file system contains only a subset of the server files and folders.
            // Here we sync folder only if its content already loaded into user file system (folder is not offline).
            // The folder content is loaded inside IFolder.GetChildrenAsync() method.
            if (new DirectoryInfo(userFileSystemFolderPath).Attributes.HasFlag(System.IO.FileAttributes.Offline))
            {
                // LogMessage("Folder offline, skipping:", userFileSystemFolderPath);
                return;
            }

            IEnumerable<string> userFileSystemChildren = Directory.EnumerateFileSystemEntries(userFileSystemFolderPath, "*");
            //LogMessage("Synchronizing:", userFileSystemFolderPath);

            IUserFolder userFolder =  await virtualDrive.GetItemAsync<IUserFolder>(userFileSystemFolderPath);
            IEnumerable<FileSystemItemBasicInfo> remoteStorageChildrenItems = await userFolder.EnumerateChildrenAsync("*");

            // Create new files/folders in the user file system.
            foreach (FileSystemItemBasicInfo remoteStorageItem in remoteStorageChildrenItems)
            {
                string userFileSystemPath = Path.Combine(userFileSystemFolderPath, remoteStorageItem.Name);
                try
                {
                    // We do not want to sync MS Office temp files, etc. from remote storage.
                    // We also do not want to create MS Office files during transactional save in user file system.
                    if (!FsPath.AvoidSync(remoteStorageItem.Name) && !FsPath.AvoidSync(userFileSystemPath))
                    {
                        if (!FsPath.Exists(userFileSystemPath))
                        {
                            LogMessage($"Creating", userFileSystemPath);

                            // If the file is moved/renamed and the app is not running this will help us 
                            // to sync the file/folder to remote storage after app starts.
                            remoteStorageItem.CustomData = new CustomData
                            {
                                OriginalPath = userFileSystemPath
                            }.Serialize();

                            await UserFileSystemRawItem.CreateAsync(userFileSystemFolderPath, new[] { remoteStorageItem });
                            LogMessage($"Created succesefully", userFileSystemPath);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogError("Creation failed", userFileSystemPath, null, ex);
                }
            }
            
            // Update files/folders in user file system and sync subfolders.
            userFileSystemChildren = Directory.EnumerateFileSystemEntries(userFileSystemFolderPath, "*");
            foreach (string userFileSystemPath in userFileSystemChildren)
            {
                try
                {
                    string itemName = Path.GetFileName(userFileSystemPath);
                    FileSystemItemBasicInfo remoteStorageItem = remoteStorageChildrenItems.FirstOrDefault(x => x.Name.Equals(itemName, StringComparison.InvariantCultureIgnoreCase));

                    
                    if (!FsPath.AvoidSync(userFileSystemPath))
                    {
                        if (remoteStorageItem == null)
                        {   
                            if (PlaceholderItem.GetItem(userFileSystemPath).GetInSync())
                            {
                                // Delete the file/folder in user file system.
                                LogMessage("Deleting item", userFileSystemPath);
                                await new UserFileSystemRawItem(userFileSystemPath).DeleteAsync();
                                LogMessage("Deleted succesefully", userFileSystemPath);
                            }                            
                        }
                        else
                        {
                            if (PlaceholderItem.GetItem(userFileSystemPath).GetInSync()
                                && !await ETag.ETagEqualsAsync(userFileSystemPath, remoteStorageItem))
                            {
                                // User file system <- remote storage update.
                                LogMessage("Remote item modified", userFileSystemPath);
                                await new UserFileSystemRawItem(userFileSystemPath).UpdateAsync(remoteStorageItem);
                                LogMessage("Updated succesefully", userFileSystemPath);
                            }

                            // Set the "locked by another user" icon and all custom columns data.
                            if(PlaceholderItem.GetItem(userFileSystemPath).GetInSync())
                            {
                                await new UserFileSystemRawItem(userFileSystemPath).SetLockedByAnotherUserAsync(remoteStorageItem.LockedByAnotherUser);
                                await new UserFileSystemRawItem(userFileSystemPath).SetCustomColumnsDataAsync(remoteStorageItem.CustomProperties); 
                            }

                            // Hydrate / dehydrate the file.
                            if (new UserFileSystemRawItem(userFileSystemPath).HydrationRequired())
                            {
                                LogMessage("Hydrating", userFileSystemPath);
                                new PlaceholderFile(userFileSystemPath).Hydrate(0, -1);
                                LogMessage("Hydrated succesefully", userFileSystemPath);
                            }
                            else if (new UserFileSystemRawItem(userFileSystemPath).DehydrationRequired())
                            {
                                LogMessage("Dehydrating", userFileSystemPath);
                                new PlaceholderFile(userFileSystemPath).Dehydrate(0, -1);
                                LogMessage("Dehydrated succesefully", userFileSystemPath);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogError("Update failed", userFileSystemPath, null, ex);
                }

                // Synchronize subfolders.
                try
                {                    
                    if (Directory.Exists(userFileSystemPath))
                    {
                        await SyncronizeFolderAsync(userFileSystemPath);
                    }
                }
                catch (Exception ex)
                {
                    LogError("Folder sync failed:", userFileSystemPath, null, ex);
                }
            }
        }
    }
}
