using Barotrauma.Sounds;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    partial class Client : IDisposable
    {
        public VoipSound VoipSound
        {
            get;
            set;
        }

        private bool mutedLocally;
        public bool MutedLocally
        {
            get { return mutedLocally; }
            set
            {
                if (mutedLocally == value) { return; }
                mutedLocally = value;
#if CLIENT
                GameMain.NetLobbyScreen.SetPlayerVoiceIconState(this, muted, mutedLocally);
                GameMain.GameSession?.CrewManager?.SetPlayerVoiceIconState(this, muted, mutedLocally);
#endif
            }
        }

        public bool IsOwner;

        public bool AllowKicking;

        public bool IsDownloading;

        public float Karma;

        public void UpdateSoundPosition()
        {
            if (VoipSound == null) { return; }
            
            if (!VoipSound.IsPlaying)
            {
                DebugConsole.Log("Destroying voipsound");
                VoipSound.Dispose();
                VoipSound = null;
                return;
            }

            if (character != null)
            {
                if (GameSettings.CurrentConfig.Audio.UseDirectionalVoiceChat)
                {
                    VoipSound.SetPosition(new Vector3(character.WorldPosition.X, character.WorldPosition.Y, 0.0f));
                }
                else
                {
                    VoipSound.SetPosition(null);
                    float dist = Vector3.Distance(new Vector3(character.WorldPosition, 0.0f), GameMain.SoundManager.ListenerPosition);
                    VoipSound.Gain = 1.0f - MathUtils.InverseLerp(VoipSound.Near, VoipSound.Far, dist);
                }
            }
            else
            {
                VoipSound.SetPosition(null);
                VoipSound.Gain = 1.0f;
            }
        }

        partial void InitProjSpecific()
        {
            VoipQueue = null; VoipSound = null;
            if (ID == GameMain.Client.ID) return;
            VoipQueue = new VoipQueue(ID, false, true);
            GameMain.Client?.VoipClient?.RegisterQueue(VoipQueue);
            VoipSound = null;
        }

        public void SetPermissions(ClientPermissions permissions, IEnumerable<string> permittedConsoleCommands)
        {
            List<DebugConsole.Command> permittedCommands = new List<DebugConsole.Command>();
            foreach (string commandName in permittedConsoleCommands)
            {
                var consoleCommand = DebugConsole.Commands.Find(c => c.names.Contains(commandName));
                if (consoleCommand != null)
                {
                    permittedCommands.Add(consoleCommand);
                }
            }
            SetPermissions(permissions, permittedCommands);
        }

        public void SetPermissions(ClientPermissions permissions, IEnumerable<DebugConsole.Command> permittedConsoleCommands)
        {
            if (GameMain.Client == null)
            {
                return;
            }
            Permissions = permissions;
            PermittedConsoleCommands.Clear();
            foreach (var command in permittedConsoleCommands)
            {
                PermittedConsoleCommands.Add(command);
            }
        }

        public void GivePermission(ClientPermissions permission)
        {
            if (GameMain.Client == null || !GameMain.Client.HasPermission(ClientPermissions.ManagePermissions))
            {
                return;
            }
            if (!Permissions.HasFlag(permission)) { Permissions |= permission; }
        }

        public void RemovePermission(ClientPermissions permission)
        {
            if (GameMain.Client == null || !GameMain.Client.HasPermission(ClientPermissions.ManagePermissions))
            {
                return;
            }
            if (Permissions.HasFlag(permission)) { Permissions &= ~permission; }
        }

        public bool HasPermission(ClientPermissions permission)
        {
            if (GameMain.Client == null)
            {
                return false;
            }

            return Permissions.HasFlag(permission);
        }

        partial void DisposeProjSpecific()
        {
            if (VoipQueue != null)
            {
                GameMain.Client.VoipClient.UnregisterQueue(VoipQueue);
            }
            if (VoipSound != null)
            {
                VoipSound.Dispose();
                VoipSound = null;
            }
        }
    }
}
