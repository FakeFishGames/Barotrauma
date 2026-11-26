#nullable enable

using Barotrauma.Extensions;
using Barotrauma.Networking;
using Barotrauma.Plugins;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Barotrauma.Plugins
{
    partial class SettingsService
    {
        private enum SettingMessageType : byte
        {
            Data,
            Request
        }

        /// <summary>
        /// Tries to resolve a settings service from a plugin hash identifier.
        /// </summary>
        private static bool TryResolveService(uint pluginHash, [NotNullWhen(returnValue: true)] out SettingsService? service)
        {
            service = null;

            if (!PluginLoader.GetPluginDataForHash(pluginHash).TryUnwrap(out PluginData? pluginData))
            {
                // Don't throw any errors, it might be intentional (e.g. server-side plugins with an optional client) for the
                // settings to not exist on one side in some cases
                return false;
            }

            service = PluginData.SettingsServices
                .FirstOrDefault(s => s.ContentPackage == pluginData.ContentPackage);

            if (service == null)
            {
                DebugConsole.ThrowError($"Failed to find settings service for {pluginData.ContentPackage.Name}.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Gets all settings that currently need to be written in a network update
        /// </summary>
        private IEnumerable<ISetting> GetSettingsToWrite(bool onlyDirty = true)
        {
            IEnumerable<ISetting> settingsToWrite = RegisteredSettings.Where(s => s.SyncMode != SettingSyncMode.NoSync && (s.IsDirty || !onlyDirty));
#if CLIENT
            settingsToWrite = settingsToWrite.Where(static s => s.SyncMode != SettingSyncMode.ClientPermissiveDesync);
#endif
            return settingsToWrite;
        }

        public static void Update()
        {
            PluginData.SettingsServices.ForEach(static s => s.SendNetworkUpdate());
        }

        /// <summary>
        /// Sends out a network update informing all clients (or the server) about setting changes.
        /// </summary>
        public void SendNetworkUpdate()
        {
            if (GameMain.NetworkMember == null) { return; }

            IEnumerable<ISetting> settingsToWrite = GetSettingsToWrite(onlyDirty: true);

            if (settingsToWrite.Count() == 0)
            {
                // Nothing to send
                return;
            }

            IWriteMessage message = new WriteOnlyMessage()
#if SERVER
            .WithHeader(ServerPacketHeader.PLUGIN_SETTINGS);
#elif CLIENT
            .WithHeader(ClientPacketHeader.PLUGIN_SETTINGS);
#endif
            message.WriteByte((byte)SettingMessageType.Data);
            message.WriteUInt32(pluginMetadata.IdentifierHash);
            WriteSettings(message, settingsToWrite);

#if SERVER
            foreach (Client client in GameMain.Server.ConnectedClients)
            {
                GameMain.Server.ServerPeer.Send(message, client.Connection, DeliveryMethod.Reliable);
            }
#elif CLIENT
            GameMain.Client.ClientPeer.Send(message, DeliveryMethod.Reliable);
#endif

            settingsToWrite.ForEach(static s => s.IsDirty = false);
        }

        private void WriteSettings(IWriteMessage message, IEnumerable<ISetting> settingsToWrite)
        {
            // Each setting is written into its own segment so the receiver can skip over settings
            // it doesn't know about (e.g. ones only registered on one side) without breaking the rest of the message
            using var segmentTable = SegmentTableWriter<Identifier>.StartWriting(message);
            foreach (ISetting setting in settingsToWrite)
            {
                segmentTable.StartNewSegment(setting.Identifier);
                setting.WriteMessage(message);
            }
        }

        private void ReadSettings(IReadMessage message, Client client)
        {
            SegmentTableReader<Identifier>.Read(message,
                segmentDataReader: (identifier, segmentMessage) => ReadSetting(segmentMessage, identifier, client),
                exceptionHandler: (segment, _, exception) =>
                {
                    DebugConsole.ThrowError($"Failed to read a plugin setting with id {segment.Identifier}.", exception, contentPackage: ContentPackage);
                });
        }

        /// <summary>
        /// Reads the value of a single setting from the message. Returns whether the rest of the settings in the message should be skipped.
        /// </summary>
        private SegmentTableReader<Identifier>.BreakSegmentReading ReadSetting(IReadMessage message, Identifier identifier, Client client)
        {
            ISetting? setting = RetrieveSetting<ISetting>(identifier);
            if (setting == null)
            {
                // Don't throw any errors, it might be intentional for a setting to only be registered on one side
                return SegmentTableReader<Identifier>.BreakSegmentReading.No;
            }

            if (setting.SyncMode == SettingSyncMode.NoSync)
            {
                DebugConsole.ThrowError($"Received a plugin setting with id {setting.Identifier}, but {nameof(ISetting.SyncMode)} is set to {nameof(SettingSyncMode.NoSync)}");
                return SegmentTableReader<Identifier>.BreakSegmentReading.No;
            }

#if SERVER
            if (setting.SyncMode != SettingSyncMode.ServerAuthority || !setting.IsAllowedToSet(setting, client))
            {
                DebugConsole.ThrowError($"Received a plugin setting with id {setting.Identifier} from {NetworkMember.ClientLogName(client)}, " +
                    $"but the client is not allowed to modify this setting, potential malicious client?");

                return SegmentTableReader<Identifier>.BreakSegmentReading.Yes;
            }

            GameServer.Log($"{GameServer.ClientLogName(client)} changed setting with id {setting.Identifier}", ServerLog.MessageType.ConsoleUsage);
#endif

            setting.ReadMessage(message);
#if SERVER
            // Mark setting as dirty so it's sent to other clients
            setting.IsDirty = true;
#endif
            return SegmentTableReader<Identifier>.BreakSegmentReading.No;
        }


#if CLIENT
        /// <summary>
        /// Sends a net message to the server requesting all settings to be sent to this client.
        /// </summary>
        public static void ClientRequestSettings()
        {
            var message = new WriteOnlyMessage().WithHeader(ClientPacketHeader.PLUGIN_SETTINGS);
            message.WriteByte((byte)SettingMessageType.Request);
            GameMain.Client.ClientPeer.Send(message, DeliveryMethod.Reliable);
        }

        public static void ClientRead(IReadMessage message)
        {
            SettingMessageType messageType = (SettingMessageType)message.ReadByte();
            switch (messageType)
            {
                case SettingMessageType.Data:
                    UInt32 identifierHash = message.ReadUInt32();

                    if (TryResolveService(identifierHash, out SettingsService? settingsService))
                    {
                        settingsService.ReadSettings(message, GameMain.Client.MyClient);
                    }
                    break;
                default:
                    break;
            }
        }
#elif SERVER
        private void SendAllSettingsToClient(Client client)
        {
            IEnumerable<ISetting> settingsToWrite = GetSettingsToWrite(onlyDirty: false);

            if (settingsToWrite.Count() == 0)
            {
                // Nothing to send
                return;
            }

            IWriteMessage message = new WriteOnlyMessage().WithHeader(ServerPacketHeader.PLUGIN_SETTINGS);
            message.WriteByte((byte)SettingMessageType.Data);
            message.WriteUInt32(pluginMetadata.IdentifierHash);
            WriteSettings(message, settingsToWrite);

            GameMain.Server.ServerPeer.Send(message, client.Connection, DeliveryMethod.Reliable);
        }

        public static void ServerRead(IReadMessage message)
        {
            Client? client = GameMain.Server.ConnectedClients.Where(c => c.Connection == message.Sender).FirstOrDefault();

            if (client == null)
            {
                // Not a connected client, is this even possible?
                return;
            }

            SettingMessageType messageType = (SettingMessageType)message.ReadByte();
            switch (messageType)
            {
                case SettingMessageType.Data:
                    UInt32 identifierHash = message.ReadUInt32();

                    if (TryResolveService(identifierHash, out SettingsService? settingsService))
                    {
                        settingsService.ReadSettings(message, client);
                    }
                    break;
                case SettingMessageType.Request:
                    PluginData.SettingsServices.ForEach(s => s.SendAllSettingsToClient(client));
                    break;
                default:
                    break;
            }
        }
#endif

    }
}