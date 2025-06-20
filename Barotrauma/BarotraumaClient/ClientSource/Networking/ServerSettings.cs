﻿using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Linq;

namespace Barotrauma.Networking
{
    partial class ServerSettings : ISerializableEntity
    {
        private static readonly LocalizedString packetAmountTooltip = TextManager.Get("ServerSettingsMaxPacketAmountTooltip");
        private static readonly RichString packetAmountTooltipWarning = RichString.Rich($"{packetAmountTooltip}\n\n‖color:gui.red‖{TextManager.Get("PacketLimitWarning")}‖end‖");

        public static bool SuppressNetworkMessages;

        partial class NetPropertyData
        {
            public GUIComponent GUIComponent;
            public object TempValue;

            public void AssignGUIComponent(GUIComponent component)
            {
                GUIComponent = component;
                GUIComponentValue = property.GetValue(parentObject);
                TempValue = GUIComponentValue;
            }

            public object GUIComponentValue
            {
                get
                {
                    if (GUIComponent == null) { return null; }
                    else if (GUIComponent is GUITickBox tickBox) { return tickBox.Selected; }
                    else if (GUIComponent is GUITextBox textBox) { return textBox.Text; }
                    else if (GUIComponent is GUIScrollBar scrollBar)
                    {
                        if (property.PropertyType == typeof(int))
                        {
                            return (int)MathF.Floor(scrollBar.BarScrollValue);
                        }

                        return scrollBar.BarScrollValue;
                    }
                    else if (GUIComponent is GUIRadioButtonGroup radioButtonGroup) { return radioButtonGroup.Selected; }
                    else if (GUIComponent is GUIDropDown dropdown) { return dropdown.SelectedData; }
                    else if (GUIComponent is GUINumberInput numInput)
                    {
                        if (numInput.InputType == NumberType.Int) { return numInput.IntValue; } else { return numInput.FloatValue; }
                    }
                    else if (GUIComponent is IGUISelectionCarouselAccessor selectionCarousel)
                    {
                        return selectionCarousel.GetSelectedElement();
                    }
                    return null;
                }
                set
                {
                    if (GUIComponent == null) { return; }
                    else if (GUIComponent is GUITickBox tickBox) { tickBox.Selected = (bool)value; }
                    else if (GUIComponent is GUITextBox textBox) { textBox.Text = (string)value; }
                    else if (GUIComponent is GUIScrollBar scrollBar)
                    {
                        if (value is int i)
                        {
                            scrollBar.BarScrollValue = i;
                        }
                        else
                        {
                            scrollBar.BarScrollValue = (float)value;
                        }
                        scrollBar.OnMoved?.Invoke(scrollBar, scrollBar.BarScroll);
                    }
                    else if (GUIComponent is GUIRadioButtonGroup radioButtonGroup) { radioButtonGroup.Selected = (int)value; }
                    else if (GUIComponent is GUIDropDown dropdown) { dropdown.SelectItem(value); }
                    else if (GUIComponent is GUINumberInput numInput)
                    {
                        if (numInput.InputType == NumberType.Int)
                        {
                            numInput.IntValue = (int)value;
                        }
                        else
                        {
                            numInput.FloatValue = (float)value;
                        }
                    }
                    else if (GUIComponent is IGUISelectionCarouselAccessor selectionCarousel)
                    {
                        selectionCarousel.SelectElement(value);
                    }
                }
            }

            public bool ChangedLocally
            {
                get
                {
                    if (GUIComponent == null) { return false; }
                    if (GUIComponent is GUIDropDown dropDown && 
                        dropDown.SelectedIndex == -1)
                    {
                        //nothing selected in the dropdown
                        //it's not possible to select nothing via the UI, which means the client cannot have selected anything locally
                        //(so this must mean that either nothing has been selected yet or that there's nothing in the dropdown)
                        return false;
                    }
                    return !PropEquals(TempValue, GUIComponentValue);
                }
            }
        }
        private Dictionary<Identifier, bool> tempMonsterEnabled;

        partial void InitProjSpecific()
        {
            var properties = TypeDescriptor.GetProperties(GetType()).Cast<PropertyDescriptor>();

            SerializableProperties = new Dictionary<Identifier, SerializableProperty>();

            foreach (var property in properties)
            {
                SerializableProperty objProperty = new SerializableProperty(property);
                SerializableProperties.Add(property.Name.ToIdentifier(), objProperty);
            }
        }

        public void ClientAdminRead(IReadMessage incMsg)
        {
            while (true)
            {
                UInt32 key = incMsg.ReadUInt32();
                if (key == 0) { break; }
                if (netProperties.ContainsKey(key))
                {
                    bool changedLocally = netProperties[key].ChangedLocally;
                    netProperties[key].Read(incMsg);
                    netProperties[key].TempValue = netProperties[key].Value;

                    if (netProperties[key].GUIComponent != null)
                    {
                        if (!changedLocally)
                        {
                            netProperties[key].GUIComponentValue = netProperties[key].Value;
                        }
                    }
                }
                else
                {
                    UInt32 size = incMsg.ReadVariableUInt32();
                    incMsg.BitPosition += (int)(8 * size);
                }
            }

            if (ReadMonsterEnabled(incMsg))
            {
                if (monstersEnabledPanel is { Visible: true })
                {
                    //refresh panel if someone else changes it
                    monstersEnabledPanel.Parent?.RemoveChild(monstersEnabledPanel);
                    monstersEnabledPanel = CreateMonstersEnabledPanel();
                    monstersEnabledPanel.Visible = true;
                }
            }
            BanList.ClientAdminRead(incMsg);
            GameMain.NetLobbyScreen?.RefreshPlaystyleIcons();
        }

        public void ClientRead(IReadMessage incMsg)
        {
            NetFlags requiredFlags = (NetFlags)incMsg.ReadByte();

            PlayStyle = (PlayStyle)incMsg.ReadByte();
            MaxPlayers = incMsg.ReadByte();
            HasPassword = incMsg.ReadBoolean();
            IsPublic = incMsg.ReadBoolean();
            GameClient.SetLobbyPublic(IsPublic);
            AllowFileTransfers = incMsg.ReadBoolean();
            incMsg.ReadPadBits();
            TickRate = incMsg.ReadRangedInteger(1, 60);

            if (requiredFlags.HasFlag(NetFlags.Properties))
            {
                if (ReadExtraCargo(incMsg))
                {
                    if (extraCargoPanel is { Visible: true })
                    {
                        //refresh panel if someone else changes it
                        extraCargoPanel.Parent?.RemoveChild(extraCargoPanel);
                        extraCargoPanel = CreateExtraCargoPanel();
                        extraCargoPanel.Visible = true;
                    }
                }

                if (ReadPerks(incMsg))
                {
                    GameMain.NetLobbyScreen?.UpdateDisembarkPointListFromServerSettings();
                }
            }

            if (requiredFlags.HasFlag(NetFlags.HiddenSubs))
            {
                ReadHiddenSubs(incMsg);
            }
            GameMain.NetLobbyScreen.UpdateSubVisibility();

            bool isAdmin = incMsg.ReadBoolean();
            incMsg.ReadPadBits();
            if (isAdmin)
            {
                ClientAdminRead(incMsg);
            }
        }

        public static bool HasPermissionToChangePerks()
        {
            if (GameMain.Client.HasPermission(Networking.ClientPermissions.ManageSettings)) { return true; }

            bool isPvP = GameMain.NetLobbyScreen?.SelectedMode == GameModePreset.PvP;
            bool hasSelectedTeam = MultiplayerPreferences.Instance.TeamPreference is CharacterTeamType.Team1 or CharacterTeamType.Team2;
            var otherClients = GameMain.Client?.ConnectedClients.Where(static c => c.SessionId != GameMain.Client.SessionId).ToImmutableArray() ?? ImmutableArray<Client>.Empty;

            if (isPvP)
            {
                if (!hasSelectedTeam) { return false; }

                return !otherClients
                        .Where(static c => c.PreferredTeam == MultiplayerPreferences.Instance.TeamPreference)
                        .Any(static c => c.HasPermission(Networking.ClientPermissions.ManageSettings));
            }
            else
            {
                return !otherClients.Any(static c => c.HasPermission(Networking.ClientPermissions.ManageSettings));
            }
        }

        public void ClientAdminWritePerks()
        {
            IWriteMessage outMsg = new WriteOnlyMessage();

            outMsg.WriteByte((byte)ClientPacketHeader.SERVER_SETTINGS_PERKS);
            WritePerks(outMsg);
            GameMain.Client?.ClientPeer?.Send(outMsg, DeliveryMethod.Reliable);
        }

        public void ClientAdminWrite(
                NetFlags dataToSend,
                Identifier addedMissionType = default,
                Identifier removedMissionType = default,
                int traitorDangerLevel = 0)
        {
            if (!GameMain.Client.HasPermission(Networking.ClientPermissions.ManageSettings)) { return; }
            if (SuppressNetworkMessages) { return; }

            IWriteMessage outMsg = new WriteOnlyMessage();

            outMsg.WriteByte((byte)ClientPacketHeader.SERVER_SETTINGS);

            outMsg.WriteByte((byte)dataToSend);

            if (dataToSend.HasFlag(NetFlags.Properties))
            {
                //TODO: split this up?
                WriteExtraCargo(outMsg);

                IEnumerable<KeyValuePair<UInt32, NetPropertyData>> changedProperties = netProperties.Where(kvp => kvp.Value.ChangedLocally);
                UInt32 count = (UInt32)changedProperties.Count();
                bool changedMonsterSettings = tempMonsterEnabled != null && tempMonsterEnabled.Any(p => p.Value != MonsterEnabled[p.Key]);

                outMsg.WriteUInt32(count);
                foreach (KeyValuePair<UInt32, NetPropertyData> prop in changedProperties)
                {
                    DebugConsole.NewMessage($"Changed {prop.Value.Name.Value} to {prop.Value.GUIComponentValue}", Color.Lime);
                    outMsg.WriteUInt32(prop.Key);
                    prop.Value.Write(outMsg, prop.Value.GUIComponentValue);
                }

                outMsg.WriteBoolean(changedMonsterSettings); outMsg.WritePadBits();
                if (changedMonsterSettings) WriteMonsterEnabled(outMsg, tempMonsterEnabled);
                BanList.ClientAdminWrite(outMsg);
            }

            if (dataToSend.HasFlag(NetFlags.HiddenSubs))
            {
                WriteHiddenSubs(outMsg);
            }
            
            if (dataToSend.HasFlag(NetFlags.Misc))
            {
                outMsg.WriteIdentifier(addedMissionType);
                outMsg.WriteIdentifier(removedMissionType);
                outMsg.WriteByte((byte)(traitorDangerLevel + 1));
                outMsg.WritePadBits();
            }

            if (dataToSend.HasFlag(NetFlags.LevelSeed))
            {
                outMsg.WriteString(GameMain.NetLobbyScreen.LevelSeedBox.Text);
            }

            GameMain.Client.ClientPeer.Send(outMsg, DeliveryMethod.Reliable);
        }

        private NetPropertyData GetPropertyData(string name)
        {
            var matchingProperty = netProperties.FirstOrDefault(p => p.Value.Name == name);
            if (matchingProperty.Equals(default(KeyValuePair<UInt32, NetPropertyData>)))
            {
                throw new ArgumentException($"Could not find a {nameof(ServerSettings)} property with the name \"{name}\".");
            }
            else
            {
                return matchingProperty.Value;
            }
        }
    }
}
