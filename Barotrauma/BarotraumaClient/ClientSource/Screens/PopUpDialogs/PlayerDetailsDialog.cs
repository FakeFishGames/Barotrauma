using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace Barotrauma.ClientSource.Screens.PopUpDialogs
{
    /// <summary>
    /// Dialog which displays details and allows management of a player. This includes muting, changing their permissions, banning, and kicking.
    /// </summary>
    class PlayerDetailsDialog : PopUpDialog
    {
        private GUIDropDown rankDropDown;
        private GUIListBox permissionsBox;
        private GUIListBox commandList;
        private Client client;

        // This is just syntactic sugar since calling new without assignment looks odd in this case.
        public static PlayerDetailsDialog CreateDialog(Client selectedClient)
        {
            return new PlayerDetailsDialog(selectedClient);
        }

        public PlayerDetailsDialog(Client selectedClient)
        {
            this.client = selectedClient;

            bool isMyClient = selectedClient.ID == GameMain.Client.ID;
            bool hasManagePermissions = GameMain.Client.HasPermission(ClientPermissions.ManagePermissions);

            Vector2 frameSize = hasManagePermissions ? new Vector2(.28f, .5f) : new Vector2(.28f, .15f);

            var playerFrameInner = new GUIFrame(new RectTransform(frameSize, RootFrame.RectTransform, Anchor.Center) { MinSize = new Point(550, 0) });

            var paddedPlayerFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.88f), playerFrameInner.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.03f
            };

            AddHeaderToLayout(paddedPlayerFrame.RectTransform, selectedClient, isMyClient, hasManagePermissions);

            if (hasManagePermissions)
            {
                AddPermissionsControlsToLayout(paddedPlayerFrame.RectTransform, selectedClient, isMyClient);
            }

            AddButtonAreaToLayout(paddedPlayerFrame.RectTransform, selectedClient, isMyClient);
        }

        private void AddHeaderToLayout(RectTransform parent, Client selectedClient, bool isMyClient, bool hasManagePermissions)
        {
            var headerContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, hasManagePermissions ? 0.1f : 0.25f), parent), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                Stretch = true
            };

            // Add name of the player, trimming if necessary
            var nameText = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), headerContainer.RectTransform),
                text: selectedClient.Name, font: GUI.LargeFont);
            nameText.Text = ToolBox.LimitString(nameText.Text, nameText.Font, (int)(nameText.Rect.Width * 0.95f));

            if (!isMyClient)
            {
                // Add mute checkbox
                new GUITickBox(new RectTransform(new Vector2(0.175f, 1.0f), headerContainer.RectTransform, Anchor.TopRight),
                    TextManager.Get("Mute"))
                {
                    Selected = selectedClient.MutedLocally,
                    OnSelected = (tickBox) => { selectedClient.MutedLocally = tickBox.Selected; return true; }
                };
            }

            // Add button to view steam profile
            if (selectedClient.SteamID != 0 && Steam.SteamManager.IsInitialized)
            {
                var viewSteamProfileButton = new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), headerContainer.RectTransform, Anchor.TopCenter) { MaxSize = new Point(int.MaxValue, (int)(40 * GUI.Scale)) },
                        TextManager.Get("ViewSteamProfile"))
                {
                    UserData = selectedClient
                };
                viewSteamProfileButton.TextBlock.AutoScaleHorizontal = true;
                viewSteamProfileButton.OnClicked = (bt, userdata) =>
                {
                    Steamworks.SteamFriends.OpenWebOverlay("https://steamcommunity.com/profiles/" + selectedClient.SteamID.ToString());
                    return true;
                };
            }
        }

        private void AddPermissionsControlsToLayout(RectTransform parent, Client selectedClient, bool isMyClient)
        {
            // Add the Rank drop down.
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), parent),
                TextManager.Get("Rank"), font: GUI.SubHeadingFont);
            rankDropDown = new GUIDropDown(new RectTransform(new Vector2(1.0f, 0.1f), parent),
                TextManager.Get("Rank"))
            {
                UserData = selectedClient,
                Enabled = !isMyClient
            };
            foreach (PermissionPreset permissionPreset in PermissionPreset.List)
            {
                rankDropDown.AddItem(permissionPreset.Name, permissionPreset, permissionPreset.Description);
            }
            rankDropDown.AddItem(TextManager.Get("CustomRank"), null);

            PermissionPreset currentPreset = PermissionPreset.List.Find(p =>
                p.Permissions == selectedClient.Permissions &&
                p.PermittedCommands.Count == selectedClient.PermittedConsoleCommands.Count && !p.PermittedCommands.Except(selectedClient.PermittedConsoleCommands).Any());
            rankDropDown.SelectItem(currentPreset);

            rankDropDown.OnSelected += (c, userdata) =>
            {
                PermissionPreset selectedPreset = (PermissionPreset)userdata;
                if (selectedPreset != null)
                {
                    client.SetPermissions(selectedPreset.Permissions, selectedPreset.PermittedCommands);
                    GameMain.Client.UpdateClientPermissions(client);

                    UpdatePermissions(client);
                    UpdatePermittedCommands(client);
                }
                return true;
            };

            // Add the labels for the permissions and permitted commands lists; done outside 
            // the functions for adding the lists themselves so we can auto-scale the text.
            var permissionLabels = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), parent), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };
            var permissionLabel = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), permissionLabels.RectTransform), TextManager.Get("Permissions"), font: GUI.SubHeadingFont);
            var consoleCommandLabel = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), permissionLabels.RectTransform),
                TextManager.Get("PermittedConsoleCommands"), wrap: true, font: GUI.SubHeadingFont);
            GUITextBlock.AutoScaleAndNormalize(permissionLabel, consoleCommandLabel);

            // Add the permissions and permitted commands lists
            var permissionContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.4f), parent), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            AddPermissionsToLayout(permissionContainer.RectTransform, selectedClient, isMyClient);
            AddPermittedConsoleCommandsToLayout(permissionContainer.RectTransform, selectedClient, isMyClient);

        }

        private void AddPermissionsToLayout(RectTransform parent, Client selectedClient, bool isMyClient)
        {
            var listBoxContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), parent))
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            // Add the select all/none tick box
            new GUITickBox(new RectTransform(new Vector2(0.15f, 0.15f), listBoxContainer.RectTransform), TextManager.Get("all", fallBackTag: "clientpermission.all"))
            {
                Enabled = !isMyClient,
                OnSelected = (tickbox) =>
                {
                    foreach (GUITickBox permissionTickBox in tickbox.Parent.GetChild<GUIListBox>().Content.Children)
                    {
                        permissionTickBox.Enabled = false;
                        permissionTickBox.Selected = tickbox.Selected;
                        permissionTickBox.Enabled = true;
                    }
                    GameMain.Client.UpdateClientPermissions(client);
                    OnPermissionChange();

                    return true;
                }
            };

            // Add the list of permissions
            permissionsBox = new GUIListBox(new RectTransform(Vector2.One, listBoxContainer.RectTransform))
            {
                UserData = selectedClient
            };

            foreach (ClientPermissions permission in Enum.GetValues(typeof(ClientPermissions)))
            {
                if (permission == ClientPermissions.None || permission == ClientPermissions.All) continue;

                var permissionTick = new GUITickBox(new RectTransform(new Vector2(0.15f, 0.15f), permissionsBox.Content.RectTransform),
                    TextManager.Get("ClientPermission." + permission), font: GUI.SmallFont)
                {
                    UserData = permission,
                    Selected = selectedClient.HasPermission(permission),
                    Enabled = !isMyClient,
                    OnSelected = (tickBox) =>
                    {
                        var thisPermission = (ClientPermissions)tickBox.UserData;
                        if (tickBox.Selected)
                        {
                            client.GivePermission(thisPermission);
                        }
                        else
                        {
                            client.RemovePermission(thisPermission);
                        }

                        // If tickBox is disabled, we are doing a batch update so suppress the updates unless it's enabled.
                        if (tickBox.Enabled)
                        {
                            GameMain.Client.UpdateClientPermissions(client);
                            OnPermissionChange();
                        }

                        return true;
                    }
                };
            }
        }

        private void UpdatePermissions(Client client)
        {
            foreach (GUITickBox permissionTickBox in permissionsBox.Content.Children)
            {
                var thisPermission = (ClientPermissions)permissionTickBox.UserData;
                permissionTickBox.Enabled = false;
                permissionTickBox.Selected = client.HasPermission(thisPermission);
                permissionTickBox.Enabled = true;
            }
        }

        private void AddPermittedConsoleCommandsToLayout(RectTransform parent, Client selectedClient, bool isMyClient)
        {
            var listBoxContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), parent))
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            // Add the select all/none tick box
            new GUITickBox(new RectTransform(new Vector2(0.15f, 0.15f), listBoxContainer.RectTransform), TextManager.Get("all", fallBackTag: "clientpermission.all"))
            {
                Enabled = !isMyClient,
                OnSelected = (tickbox) =>
                {
                    foreach (GUITickBox commandTickBox in tickbox.Parent.GetChild<GUIListBox>().Content.Children)
                    {
                        commandTickBox.Enabled = false;
                        commandTickBox.Selected = tickbox.Selected;
                        commandTickBox.Enabled = true;
                    }
                    GameMain.Client.UpdateClientPermissions(client);
                    OnPermissionChange();

                    return true;
                }
            };

            // Add the list of permitted commands
            commandList = new GUIListBox(new RectTransform(Vector2.One, listBoxContainer.RectTransform))
            {
                UserData = selectedClient
            };
            foreach (DebugConsole.Command command in DebugConsole.Commands)
            {
                var commandTickBox = new GUITickBox(new RectTransform(new Vector2(0.15f, 0.15f), commandList.Content.RectTransform),
                    command.names[0], font: GUI.SmallFont)
                {
                    Selected = selectedClient.PermittedConsoleCommands.Contains(command),
                    Enabled = !isMyClient,
                    ToolTip = command.help,
                    UserData = command
                };
                commandTickBox.OnSelected += (GUITickBox tickBox) =>
                {
                    DebugConsole.Command selectedCommand = tickBox.UserData as DebugConsole.Command;

                    if (!tickBox.Selected)
                    {
                        client.PermittedConsoleCommands.Remove(selectedCommand);
                    }
                    else if (!client.PermittedConsoleCommands.Contains(selectedCommand))
                    {
                        client.PermittedConsoleCommands.Add(selectedCommand);
                    }

                    // If tickBox is disabled, we are doing a batch update so suppress the updates unless it's enabled.
                    if (tickBox.Enabled)
                    {
                        GameMain.Client.UpdateClientPermissions(client);
                        OnPermissionChange();
                    }

                    return true;
                };
            }
        }

        private void UpdatePermittedCommands(Client client)
        {
            foreach (GUITickBox commandTickBox in commandList.Content.Children)
            {
                DebugConsole.Command command = commandTickBox.UserData as DebugConsole.Command;
                commandTickBox.Enabled = false;
                commandTickBox.Selected = client.PermittedConsoleCommands.Contains(command);
                commandTickBox.Enabled = true;
            }
        }

        private void AddButtonAreaToLayout(RectTransform parent, Client selectedClient, bool isMyClient)
        {
            var buttonAreaUpper = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.08f), parent), isHorizontal: true);
            var buttonAreaLower = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.08f), parent), isHorizontal: true, childAnchor: Anchor.CenterLeft);

            AddUpperButtons(buttonAreaUpper.RectTransform, selectedClient, isMyClient);
            AddLowerButtons(buttonAreaLower.RectTransform, selectedClient, isMyClient);

            // Now make all the buttons the same size
            float xSize = 1f / Math.Max(buttonAreaUpper.CountChildren, buttonAreaLower.CountChildren);

            for (int i = 0; i < buttonAreaUpper.CountChildren; i++)
            {
                buttonAreaUpper.GetChild(i).RectTransform.RelativeSize = new Vector2(xSize, 1f);
            }
            for (int i = 0; i < buttonAreaLower.CountChildren; i++)
            {
                buttonAreaLower.GetChild(i).RectTransform.RelativeSize = new Vector2(xSize, 1f);
            }

            var maxNonScaledYSizeUpper = buttonAreaUpper.CountChildren > 0 ? buttonAreaUpper.RectTransform.Children.Max(c => c.NonScaledSize.Y) : 0;
            var maxNonScaledYSizeLower = buttonAreaLower.CountChildren > 0 ? buttonAreaLower.RectTransform.Children.Max(c => c.NonScaledSize.Y) : 0;
            var maxNonScaledYSize = Math.Max(maxNonScaledYSizeUpper, maxNonScaledYSizeLower);
            buttonAreaUpper.RectTransform.NonScaledSize = buttonAreaLower.RectTransform.NonScaledSize = new Point(buttonAreaLower.Rect.Width, maxNonScaledYSize);

            // Also make all the buttons have the same size text
            if (buttonAreaUpper.CountChildren + buttonAreaLower.CountChildren > 0)
            {
                GUITextBlock.AutoScaleAndNormalize(buttonAreaUpper.Children.Select(c => ((GUIButton)c).TextBlock).Concat(buttonAreaLower.Children.Select(c => ((GUIButton)c).TextBlock)));
            }

            if (buttonAreaUpper.CountChildren == 0)
            {
                parent.GUIComponent.RemoveChild(buttonAreaUpper);
            }
        }

        private void AddUpperButtons(RectTransform parent, Client selectedClient, bool isMyClient)
        {
            if (!isMyClient)
            {
                if (GameMain.Client.HasPermission(ClientPermissions.Ban))
                {
                    // Add ban button
                    var banButton = new GUIButton(new RectTransform(new Vector2(0.34f, 1.0f), parent),
                        TextManager.Get("Ban"))
                    {
                        UserData = selectedClient
                    };
                    banButton.OnClicked = (bt, userdata) => { CrewManager.BanPlayer(selectedClient); CloseDialog(); return true; };

                    // Add range ban button
                    var rangebanButton = new GUIButton(new RectTransform(new Vector2(0.34f, 1.0f), parent),
                        TextManager.Get("BanRange"))
                    {
                        UserData = selectedClient
                    };
                    rangebanButton.OnClicked = (bt, userdata) => { CrewManager.BanPlayerRange(selectedClient); CloseDialog(); return true; };
                }
            }
        }

        private void AddLowerButtons(RectTransform parent, Client selectedClient, bool isMyClient)
        {
            if (!isMyClient)
            {
                if (GameMain.Client.ServerSettings.Voting.AllowVoteKick && selectedClient.AllowKicking)
                {
                    // Add vote to kick button
                    var kickVoteButton = new GUIButton(new RectTransform(new Vector2(0.34f, 1.0f), parent),
                        TextManager.Get("VoteToKick"))
                    {
                        Enabled = !selectedClient.HasKickVoteFromID(GameMain.Client.ID),
                        OnClicked = (btn, userdata) => { GameMain.Client.VoteForKick(selectedClient); btn.Enabled = false; return true; },
                        UserData = selectedClient
                    };
                }

                if (GameMain.Client.HasPermission(ClientPermissions.Kick) && selectedClient.AllowKicking)
                {
                    // Add kick button
                    var kickButton = new GUIButton(new RectTransform(new Vector2(0.34f, 1.0f), parent), TextManager.Get("Kick"))
                    {
                        UserData = selectedClient
                    };
                    kickButton.OnClicked = (bt, userdata) => { CrewManager.KickPlayer(selectedClient); CloseDialog(); return true; };
                }
            }

            // Add close button
            var closeButton = new GUIButton(new RectTransform(new Vector2(0f, 1.0f), parent, Anchor.CenterRight),
                TextManager.Get("Close"))
            {
                IgnoreLayoutGroups = true,
                OnClicked = (_, __) => { CloseDialog(); return true; }
            };

        }

        private void OnPermissionChange()
        {
            // Reset rank to custom
            rankDropDown.SelectItem(null);

            //TODO: Should this be the logic for finding a matching preset instead?
            PermissionPreset currentPreset = PermissionPreset.List.Find(p =>
                p.Permissions == client.Permissions &&
                p.PermittedCommands.Count == client.PermittedConsoleCommands.Count && !p.PermittedCommands.Except(client.PermittedConsoleCommands).Any());
            rankDropDown.SelectItem(currentPreset);
        }

    }
}
