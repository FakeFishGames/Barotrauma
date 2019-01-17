using Barotrauma.Sounds;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Barotrauma.Networking
{
    struct TempClient
    {
        public string Name;
        public byte ID;
        public UInt16 CharacterID;
        public ClientPermissions Permissions;
        public List<DebugConsole.Command> PermittedConsoleCommands;
    }

    partial class Client : IDisposable
    {
        public VoipSound VoipSound
        {
            get;
            set;
        }
        
        public void UpdateSoundPosition()
        {
            if (VoipSound != null)
            {
                if (!VoipSound.IsPlaying)
                {
                    DebugConsole.NewMessage("destroying voipsound", Color.Lime);
                    VoipSound.Dispose();
                    VoipSound = null;
                    return;
                }

                if (character != null)
                {
                    VoipSound.SetPosition(new Vector3(character.WorldPosition.X, character.WorldPosition.Y, 0.0f));
                }
                else
                {
                    VoipSound.SetPosition(null);
                }
            }
        }

        partial void InitProjSpecific()
        {
            VoipQueue = null; VoipSound = null;
            if (ID == GameMain.Client.ID) return;
            VoipQueue = new VoipQueue(ID, false, true);
            GameMain.Client.VoipClient.RegisterQueue(VoipQueue);
            VoipSound = null;
        }

        public void SetPermissions(ClientPermissions permissions, List<DebugConsole.Command> permittedConsoleCommands)
        {
            if (GameMain.Client == null || !GameMain.Client.HasPermission(ClientPermissions.ManagePermissions))
            {
                return;
            }
            Permissions = permissions;
            PermittedConsoleCommands = new List<DebugConsole.Command>(permittedConsoleCommands);
        }

        public void GivePermission(ClientPermissions permission)
        {
            if (GameMain.Client == null || !GameMain.Client.HasPermission(ClientPermissions.ManagePermissions))
            {
                return;
            }
            if (!Permissions.HasFlag(permission)) Permissions |= permission;
        }

        public void RemovePermission(ClientPermissions permission)
        {
            if (GameMain.Client == null || !GameMain.Client.HasPermission(ClientPermissions.ManagePermissions))
            {
                return;
            }
            if (Permissions.HasFlag(permission)) Permissions &= ~permission;
        }

        public bool HasPermission(ClientPermissions permission)
        {
            if (GameMain.Client == null || !GameMain.Client.HasPermission(ClientPermissions.ManagePermissions))
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
