#nullable enable
using Barotrauma.IO;
using Microsoft.Xna.Framework;
using Steamworks;
using System;
using System.Diagnostics.CodeAnalysis;
namespace Barotrauma.Steam;

internal static partial class RemoteStorageHelper
{
    public static readonly Color SteamColor = Color.DodgerBlue;
    public static readonly string DebugPrefix = $"‖color:{SteamColor.ToStringHex()}‖[Remote Storage]‖end‖";

    /// <summary>Attempts to read a file from remote storage into a byte array.</summary>
    /// <param name="remoteFile">The remote file to read from.</param>
    /// <param name="bytes">The bytes read from the remote file. Returns <see langword="null"/> if the operation failed.</param>
    /// <returns>
    /// <see langword="true"/> if the operation was successful.<br/>
    /// <see langword="false"/> if the operation failed.
    /// </returns>
    public static bool TryRead(this SteamRemoteStorage.RemoteFile remoteFile, [NotNullWhen(returnValue: true)] out byte[]? bytes, bool logError = true)
    {
        bytes = SteamRemoteStorage.FileRead(remoteFile.Filename);
        bool success = bytes != null;

        if (logError && !success) 
        { 
            DebugConsole.ThrowError($"{DebugPrefix} Failed to read file \"{remoteFile.Filename}\" from remote storage: operation failed."); 
        }

        return success;
    }

    /// <summary>Attempts to write a file to remote storage.</summary>
    /// <param name="localPath">The path of the local file to read from.</param>
    /// <param name="saveAs">The name of the remote file to write to. If <see langword="null"/>, the file name of <paramref name="localPath"/> is used.</param>
    /// <param name="allowOverwrite">If <see langword="true"/>, overwriting existing remote files is allowed.</param>
    /// <returns>
    /// <see langword="true"/> if the operation was successful.<br/>
    /// <see langword="false"/> if the operation failed.
    /// </returns>
    public static bool TryWrite(string localPath, string? saveAs = null, bool allowOverwrite = false, bool logError = true)
    {
        string fileName = saveAs ?? Path.GetFileName(localPath);

        if (!allowOverwrite && SteamRemoteStorage.FileExists(fileName))
        {
            if (logError)
            {
                DebugConsole.ThrowError($"{DebugPrefix} Failed to write file \"{fileName}\" to remote storage: file already exists.");
            }
            return false;
        }

        byte[] data;

        try
        {
            data = File.ReadAllBytes(localPath);
        }
        catch (Exception exception)
        {
            if (logError)
            {
                DebugConsole.ThrowError($"{DebugPrefix} Failed to read file \"{fileName}\" while writing to remote storage: {exception}");
            }
            return false;
        }

        bool success = SteamRemoteStorage.FileWrite(fileName, data);

        if (logError && !success) 
        { 
            DebugConsole.ThrowError($"{DebugPrefix} Failed to write file \"{fileName}\" to remote storage: operation failed."); 
        }

        return success;
    }

    /// <summary>Attempts to delete a file from remote storage.</summary>
    /// <param name="fileName">The name of the remote file to delete.</param>
    /// <returns>
    /// <see langword="true"/> if the operation was successful.<br/>
    /// <see langword="false"/> if the operation failed.
    /// </returns>
    public static bool TryDelete(string fileName, bool logError = true)
    {
        bool success = SteamRemoteStorage.FileDelete(fileName);

        if (logError && !success) 
        { 
            DebugConsole.ThrowError($"{DebugPrefix} Failed to delete file \"{fileName}\" from remote storage: operation failed."); 
        }

        return success;
    }

    /// <summary>Checks if a file is stored remotely.</summary>
    /// <param name="fileName">The name of the remote file to check.</param>
    /// <returns>
    /// <see langword="true"/> if the file is stored.<br/>
    /// <see langword="false"/> if the file is not stored or the operation failed.
    /// </returns>
    public static bool IsStored(string fileName) => SteamRemoteStorage.FileExists(fileName);
}
