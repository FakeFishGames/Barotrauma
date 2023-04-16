#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    internal partial class ReadyCheck
    {
        private static LocalizedString readyCheckBody(string name) => string.IsNullOrWhiteSpace(name) ? TextManager.Get("readycheck.serverbody") : TextManager.GetWithVariable("readycheck.body", "[player]", name);

        private static LocalizedString readyCheckStatus(int ready, int total) => TextManager.GetWithVariables("readycheck.readycount",
            ("[ready]", ready.ToString()),
            ("[total]", total.ToString()));
        private static LocalizedString readyCheckPleaseWait(int seconds) => TextManager.GetWithVariable("readycheck.pleasewait", "[seconds]", seconds.ToString());

        private static readonly LocalizedString readyCheckHeader = TextManager.Get("ReadyCheck.Title");

        private static readonly LocalizedString noButton = TextManager.Get("No"),
                                                yesButton = TextManager.Get("Yes"),
                                                closeButton = TextManager.Get("Close");

        private const string TimerData = "Timer",
                             PromptData = "ReadyCheck",
                             ResultData = "ReadyCheckResults",
                             UserListData = "ReadyUserList",
                             ReadySpriteData = "ReadySprite";

        private int lastSecond = 1;

        private GUIMessageBox? msgBox;
        private GUIMessageBox? resultsBox;

        public static DateTime ReadyCheckCooldown = DateTime.MinValue;

        public static bool IsReadyCheck(GUIComponent? msgBox) => msgBox?.UserData as string == PromptData || msgBox?.UserData as string == ResultData;

        private void CreateMessageBox(string author)
        {
            Vector2 relativeSize = new Vector2(0.3f * GUI.AspectRatioAdjustment, 0.15f);
            Point minSize = new Point(300, 200);
            msgBox = new GUIMessageBox(readyCheckHeader, readyCheckBody(author), new[] { yesButton, noButton }, relativeSize, minSize, type: GUIMessageBox.Type.Vote) { UserData = PromptData, Draggable = true };

            GUILayoutGroup contentLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.125f), msgBox.Content.RectTransform), childAnchor: Anchor.Center);
            new GUIProgressBar(new RectTransform(new Vector2(0.8f, 1f), contentLayout.RectTransform), 0.0f, GUIStyle.Orange) { UserData = TimerData };

            // Yes
            msgBox.Buttons[0].OnClicked = delegate
            {
                msgBox.Close();
                if (GameMain.Client == null) { return true; }
                SendState(ReadyStatus.Yes);
                CreateResultsMessage();
                return true;
            };

            // No
            msgBox.Buttons[1].OnClicked = delegate
            {
                msgBox.Close();
                if (GameMain.Client == null) { return true; }
                SendState(ReadyStatus.No);
                CreateResultsMessage();
                return true;
            };
        }

        private void CreateResultsMessage()
        {
            if (GameMain.Client == null) { return; }

            Vector2 relativeSize = new Vector2(0.2f, 0.3f);
            Point minSize = new Point(300, 400);
            resultsBox = new GUIMessageBox(readyCheckHeader, string.Empty, new[] { closeButton }, relativeSize, minSize, type: GUIMessageBox.Type.Vote) { UserData = ResultData, Draggable = true };
            if (msgBox != null)
            {
                resultsBox.RectTransform.ScreenSpaceOffset = msgBox.RectTransform.ScreenSpaceOffset;
            }

            GUIListBox listBox = new GUIListBox(new RectTransform(new Vector2(1f, 0.8f), resultsBox.Content.RectTransform)) { UserData = UserListData };

            foreach (var (id, _) in Clients)
            {
                Client? client = GameMain.Client.ConnectedClients.FirstOrDefault(c => c.SessionId == id);
                GUIFrame container = new GUIFrame(new RectTransform(new Vector2(1f, 0.15f), listBox.Content.RectTransform), style: "ListBoxElement") { UserData = id };
                GUILayoutGroup frame = new GUILayoutGroup(new RectTransform(Vector2.One, container.RectTransform), isHorizontal: true) { Stretch = true };

                int height = frame.Rect.Height;

                JobPrefab? jobPrefab = client?.Character?.Info?.Job?.Prefab;

                if (client == null)
                {
                    string list = GameMain.Client.ConnectedClients.Aggregate("Available clients:\n", (current, c) => current + $"{c.SessionId}: {c.Name}\n");
                    DebugConsole.ThrowError($"Client ID {id} was reported in ready check but was not found.\n" + list.TrimEnd('\n'));
                }

                if (jobPrefab?.Icon != null)
                {
                    // job icon
                    new GUIImage(new RectTransform(new Point(height, height), frame.RectTransform), jobPrefab.Icon, scaleToFit: true) { Color = jobPrefab.UIColor };
                }

                new GUITextBlock(new RectTransform(new Vector2(0.75f, 1), frame.RectTransform), client?.Name ?? $"Unknown ID {id}", jobPrefab?.UIColor ?? Color.White, textAlignment: Alignment.Center) { AutoScaleHorizontal = true };
                new GUIImage(new RectTransform(new Point(height, height), frame.RectTransform), null, scaleToFit: true) { UserData = ReadySpriteData };
            }

            resultsBox.Buttons[0].OnClicked = delegate
            {
                resultsBox.Close();
                return true;
            };
        }

        private void UpdateBar()
        {
            double elapsedTime = (DateTime.Now - startTime).TotalSeconds;
            if (msgBox is { Closed: false } && GUIMessageBox.MessageBoxes.Contains(msgBox))
            {
                if (msgBox.FindChild(TimerData, true) is GUIProgressBar bar)
                {
                    bar.BarSize = (float)(elapsedTime / (endTime - startTime).TotalSeconds);
                }
            }

            // play click sound after a second has passed
            int second = (int)Math.Ceiling(elapsedTime);
            if (second > lastSecond)
            {
                if (msgBox is { Closed: false })
                {
                    SoundPlayer.PlayUISound(GUISoundType.PopupMenu);
                }
                lastSecond = second;
            }
        }

        private static void CloseLingeringPopups()
        {
            foreach (GUIComponent box in GUIMessageBox.MessageBoxes.ToImmutableArray())
            {
                if (box is not GUIMessageBox msgBox) { continue; }

                if (msgBox.UserData is PromptData or ResultData)
                {
                    msgBox.Close();
                }
            }
        }

        public static void ClientRead(IReadMessage inc)
        {
            ReadyCheckState state = (ReadyCheckState)inc.ReadByte();
            CrewManager? crewManager = GameMain.GameSession?.CrewManager;
            var otherClients = GameMain.Client.ConnectedClients;
            if (crewManager == null || otherClients == null)
            {
                if (state == ReadyCheckState.Start)
                {
                    SendState(ReadyStatus.No);
                }
                return; 
            }

            switch (state)
            {
                case ReadyCheckState.Start:
                    CloseLingeringPopups();

                    bool isOwn = false;
                    byte authorId = 0;

                    long startTime = inc.ReadInt64();
                    long endTime = inc.ReadInt64();
                    string author = inc.ReadString();
                    bool hasAuthor = inc.ReadBoolean();

                    if (hasAuthor)
                    {
                        authorId = inc.ReadByte();
                        isOwn = authorId == GameMain.Client.SessionId;
                    }

                    ushort clientCount = inc.ReadUInt16();
                    List<byte> clients = new List<byte>();
                    for (int i = 0; i < clientCount; i++)
                    {
                        clients.Add(inc.ReadByte());
                    }

                    ReadyCheck rCheck = new ReadyCheck(clients,
                        DateTimeOffset.FromUnixTimeSeconds(startTime).LocalDateTime,
                        DateTimeOffset.FromUnixTimeSeconds(endTime).LocalDateTime);
                    crewManager.ActiveReadyCheck = rCheck;

                    if (isOwn)
                    {
                        SendState(ReadyStatus.Yes);
                        rCheck.CreateResultsMessage();
                    }
                    else
                    {
                        rCheck.CreateMessageBox(author);
                    }

                    if (hasAuthor && rCheck.Clients.ContainsKey(authorId))
                    {
                        rCheck.Clients[authorId] = ReadyStatus.Yes;
                    }
                    break;
                case ReadyCheckState.Update:
                    ReadyStatus newState = (ReadyStatus)inc.ReadByte();
                    byte targetId = inc.ReadByte();
                    if (crewManager.ActiveReadyCheck != null)
                    {
                        crewManager.ActiveReadyCheck?.UpdateState(targetId, newState);
                    }
                    break;
                case ReadyCheckState.End:
                    ushort count = inc.ReadUInt16();
                    for (int i = 0; i < count; i++)
                    {
                        byte id = inc.ReadByte();
                        ReadyStatus status = (ReadyStatus)inc.ReadByte();
                        crewManager.ActiveReadyCheck?.UpdateState(id, status);
                    }

                    crewManager.ActiveReadyCheck?.EndReadyCheck();
                    crewManager.ActiveReadyCheck?.msgBox?.Close();
                    crewManager.ActiveReadyCheck = null;
                    break;
            }
        }

        partial void EndReadyCheck()
        {
            if (IsFinished) { return; }
            IsFinished = true;

            int readyCount = Clients.Count(static pair => pair.Value == ReadyStatus.Yes);
            int totalCount = Clients.Count;
            GameMain.Client.AddChatMessage(ChatMessage.Create(string.Empty, readyCheckStatus(readyCount, totalCount).Value, ChatMessageType.Server, null));
        }

        private void UpdateState(byte id, ReadyStatus status)
        {
            if (Clients.ContainsKey(id))
            {
                Clients[id] = status;
            }

            if (resultsBox == null || resultsBox.Closed || !GUIMessageBox.MessageBoxes.Contains(resultsBox)) { return; }

            if (resultsBox.Content.FindChild(UserListData) is not GUIListBox userList) { return; }

            // for some reason FindChild doesn't work here?
            foreach (GUIComponent child in userList.Content.Children)
            {
                if (child.UserData is not byte b || b != id) { continue; }

                if (child.GetChild<GUILayoutGroup>().FindChild(ReadySpriteData) is not GUIImage image) { continue; }

                string style;
                switch (status)
                {
                    case ReadyStatus.Yes:
                        style = "MissionCompletedIcon";
                        break;
                    case ReadyStatus.No:
                        style = "MissionFailedIcon";
                        break;
                    default:
                        return;
                }

                image.ApplyStyle(GUIStyle.GetComponentStyle(style));
            }
        }

        private static void SendState(ReadyStatus status)
        {
            IWriteMessage msg = new WriteOnlyMessage();
            msg.WriteByte((byte)ClientPacketHeader.READY_CHECK);
            msg.WriteByte((byte)ReadyCheckState.Update);
            msg.WriteByte((byte)status);
            GameMain.Client?.ClientPeer?.Send(msg, DeliveryMethod.Reliable);
        }

        public static void CreateReadyCheck()
        {
            if (ReadyCheckCooldown < DateTime.Now)
            {
#if !DEBUG
                ReadyCheckCooldown = DateTime.Now.AddMinutes(1);
#endif
                IWriteMessage msg = new WriteOnlyMessage();
                msg.WriteByte((byte)ClientPacketHeader.READY_CHECK);
                msg.WriteByte((byte)ReadyCheckState.Start);
                GameMain.Client?.ClientPeer?.Send(msg, DeliveryMethod.Reliable);
                return;
            }

            GUIMessageBox msgBox = new GUIMessageBox(readyCheckHeader, readyCheckPleaseWait((ReadyCheckCooldown - DateTime.Now).Seconds), new[] { closeButton });
            msgBox.Buttons[0].OnClicked = delegate
            {
                msgBox.Close();
                return true;
            };
        }
    }
}