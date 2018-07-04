using Microsoft.Xna.Framework;

namespace Barotrauma.Networking
{
    partial class ServerInfo
    {
        public void CreatePreviewWindow(GUIMessageBox messageBox)
        {
            var title = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), messageBox.Content.RectTransform), ServerName, textAlignment: Alignment.Center, font: GUI.LargeFont, wrap: true);

            var serverMsg = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.2f), messageBox.Content.RectTransform));
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), serverMsg.Content.RectTransform), ServerMessage, wrap: true);

            var columnContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.5f), messageBox.Content.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            var columnLeft = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), columnContainer.RectTransform))
            {
                RelativeSpacing = 0.02f
            };
            var columnRight = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), columnContainer.RectTransform))
            {
                RelativeSpacing = 0.02f
            };

            float elementHeight = 0.1f;

            // left column -----------------------------------------------------------------------------

            //new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), columnLeft.RectTransform), IP + ":" + Port);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), columnLeft.RectTransform),
                TextManager.Get("ServerListVersion") + ": " + (string.IsNullOrEmpty(GameVersion) ? TextManager.Get("Unknown") : GameVersion));
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), columnLeft.RectTransform),
                TextManager.Get("ServerListContentPackages") + ": " + (ContentPackageNames.Count == 0 ? TextManager.Get("Unknown") : string.Join(", ", ContentPackageNames)), wrap: true);

            new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), columnLeft.RectTransform), TextManager.Get("ServerListHasPassword"))
            {
                Selected = HasPassword,
                CanBeFocused = false
            };

            var usingWhiteList = new GUITickBox(new RectTransform(new Vector2(1, elementHeight), columnLeft.RectTransform), TextManager.Get("ServerListUsingWhitelist"))
            {
                CanBeFocused = false
            };
            if (!UsingWhiteList.HasValue)
                new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.8f), usingWhiteList.Box.RectTransform, Anchor.Center), "?", textAlignment: Alignment.Center);
            else
                usingWhiteList.Selected = UsingWhiteList.Value;


            // right column -----------------------------------------------------------------------------

            new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), columnRight.RectTransform), TextManager.Get("ServerListPlayers") + ": " + PlayerCount + "/" + MaxPlayers);

            new GUITickBox(new RectTransform(new Vector2(1, elementHeight), columnRight.RectTransform), "Round running")
            {
                Selected = GameStarted,
                CanBeFocused = false
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), columnRight.RectTransform), TextManager.Get("GameMode") + ": " + (string.IsNullOrEmpty(GameMode) ? "Unknown" : GameMode));
            new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), columnRight.RectTransform), TextManager.Get("Traitors") + ": " + (!TraitorsEnabled.HasValue ? "Unknown" : TraitorsEnabled.Value.ToString()));

            new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), columnRight.RectTransform), TextManager.Get("ServerListSubSelection") + ": " + (!SubSelectionMode.HasValue ? "Unknown" : SubSelectionMode.Value.ToString()));
            new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), columnRight.RectTransform), TextManager.Get("ServerListModeSelection") + ": " + (!ModeSelectionMode.HasValue ? "Unknown" : ModeSelectionMode.Value.ToString()));

            var allowSpectating = new GUITickBox(new RectTransform(new Vector2(1, elementHeight), columnRight.RectTransform), TextManager.Get("ServerListAllowSpectating"))
            {
                CanBeFocused = false
            };
            if (!AllowSpectating.HasValue)
                new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.8f), allowSpectating.Box.RectTransform, Anchor.Center), "?", textAlignment: Alignment.Center);
            else
                allowSpectating.Selected = AllowSpectating.Value;

            var allowRespawn = new GUITickBox(new RectTransform(new Vector2(1, elementHeight), columnRight.RectTransform), "Allow respawn")
            {
                CanBeFocused = false
            };
            if (!AllowRespawn.HasValue)
                new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.8f), allowRespawn.Box.RectTransform, Anchor.Center), "?", textAlignment: Alignment.Center);
            else
                allowRespawn.Selected = AllowRespawn.Value;

            foreach (GUIComponent c in columnLeft.Children)
            {
                if (c is GUITextBlock textBlock) textBlock.Padding = Vector4.Zero;
            }
            foreach (GUIComponent c in columnRight.Children)
            {
                if (c is GUITextBlock textBlock) textBlock.Padding = Vector4.Zero;
            }
        }
    }
}
