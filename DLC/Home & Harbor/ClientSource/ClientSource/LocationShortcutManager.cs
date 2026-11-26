#nullable enable

using Barotrauma;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace HomeAndHarbor;

public partial class LocationShortcutManager
{
    private static GUIComponent? locationShortcutsPanel;
    private static RectTransform? shortcutPanelRectTransform;

    private static readonly GUIComponentStyle? ShortcutIconVisitedStyle;
    private static readonly GUIComponentStyle? ShortcutIconUnvisitedStyle;
    
    // How many multiples of the location icon size should the shortcut icon's distance from the location icon be
    private const float ShortcutIconDistanceMultiplier = 0.75f;
    private static readonly float ShortcutIconScale = 0.875f;
    private static readonly float ShortcutIconCurrentHighlight = 1.66f;
    
    private static readonly Color HighlightSpriteColor = Color.LightBlue * 0.5f;
    private static readonly float HighlightSizeMultiplier = 1.7f; // 1.7 is used in Map for the normal location indicator
    private static readonly float HighlightFlashDuration = 0.23f;
    private static readonly Vector2 LocationNameOffset = new Vector2(0, 45);
    private static readonly float OffscreenIndicatorMargin = 60f;
    
    private static float highlightPhase = 0;
    private static Location? highlightedShortcutLocation;

    private static Location? shortcutIconHighlightedOnLocation;

    static LocationShortcutManager()
    {
        GUIStyle.ComponentStyles.TryGet("ShortcutIndicatorUnvisited", out ShortcutIconUnvisitedStyle);
        GUIStyle.ComponentStyles.TryGet("ShortcutIndicatorVisited", out ShortcutIconVisitedStyle);
    }
    
    public static void OnRoundStarted(GameSession _)
    {
        highlightedShortcutLocation = null;
        shortcutIconHighlightedOnLocation = null;
    }

    public static void OnRoundEnded(GameSession _)
    {
        highlightedShortcutLocation = null;
        shortcutIconHighlightedOnLocation = null;
    }

    public static void OnBeforeDrawMap(CampaignMode campaign, Map map, SpriteBatch spriteBatch, GUICustomComponent mapContainer, float zoom, MapGenerationParams generationParams)
    {
        if (shortcutIconHighlightedOnLocation != null)
        {
            map.IsCustomElementBlocking = true;
        }
        shortcutIconHighlightedOnLocation = null;
    }

    public static void OnAfterDrawMap(CampaignMode campaign, Map map, SpriteBatch spriteBatch, GUICustomComponent mapContainer, float zoom, MapGenerationParams generationParams)
    {
        if (highlightedShortcutLocation != null && locationShortcutsPanel is { Visible: true } )
        {
            Rectangle mapRect = mapContainer.Rect; // Map bounds in screen space
            Vector2 mapCenter = new Vector2(mapRect.Center.X, mapRect.Center.Y); // Center of map in screen space
            Vector2 viewOffset = map.DrawOffset; // Current view offset (without the offset noise, but it shouldn't matter)
            Vector2 locationScreenPos = mapCenter + (highlightedShortcutLocation.MapPosition + viewOffset) * zoom;
            Vector2 mapIconPos = mapCenter + (highlightedShortcutLocation.MapPosition + viewOffset) * zoom;
            
            if (!mapRect.Contains(locationScreenPos)) // show direction indicator at edge of screen
            {
                if (MathUtils.GetLineRectangleIntersection(mapCenter, locationScreenPos, mapRect, out Vector2 indicatorPos))
                {
                    Vector2 direction = Vector2.Normalize(locationScreenPos - mapCenter);
                    DrawDirectionIndicator(spriteBatch, indicatorPos, direction, highlightedShortcutLocation);
                }
            }
            else // otherwise just highlight the location normally
            {
                Color highlightColor = highlightPhase > 0 // initial flash still ongoing?
                    ? Color.Lerp(Color.Transparent, HighlightSpriteColor, 1f - (highlightPhase / HighlightFlashDuration))
                    : HighlightSpriteColor;
                generationParams.SelectedLocationIndicator.Draw(spriteBatch,
                    mapIconPos,
                    highlightColor,
                    scale: (generationParams.LocationIconSize / generationParams.SelectedLocationIndicator.size.X) * HighlightSizeMultiplier * zoom); // same size as in Map
            }
        }
    }


    public static void OnDrawMapLocation(Location location, CampaignMode campaignMode, Map map, GUICustomComponent mapContainer, SpriteBatch spriteBatch, Vector2 mapIconPosition, float zoom, MapGenerationParams generationParams)
    {
        var outpostData = PersistentOutpostData.GetPersistentOutpostData(location);

        if (GameMain.DebugDraw)
        {
            // Show all shortcuts in debug mode
            if (!outpostData.HasShortcut) { return; }
        }
        else
        {
            if (!location.Discovered)
            {
                //don't show the shortcut icon if the location hasn't been discovered and is more than 2 connections away
                //(current location not present in the connections, nor the connections of the connected locations
                if (location.Connections.None(c => c.Locations.Contains(map.CurrentLocation)) &&
                    location.Connections.None(c => c.OtherLocation(location).Connections.Any(c2 => c2.Locations.Contains(map.CurrentLocation))))
                {
                    return;
                }
            }
            if (!IsShortcutAvailable(location, outpostData)) { return; }
        }

        bool isCurrentLocation = location == map.CurrentLocation;

        // Draw shortcut icon

        var shortCutStyle = location.Visited ? ShortcutIconVisitedStyle : ShortcutIconUnvisitedStyle;

        if (shortCutStyle?.GetDefaultSprite() is Sprite shortcutSprite)
        {
            Vector2 shortcutIconPos = mapIconPosition + new Vector2(20, 20) * zoom;

            float currIconScale = ShortcutIconScale * zoom;

            float iconRadius = Math.Max(shortcutSprite.size.X, shortcutSprite.size.Y) / 2 * currIconScale;

            bool isHighlighted =
                !map.HasBlockingTooltip && // Avoid highlight-fighting
                Vector2.Distance(PlayerInput.MousePosition, shortcutIconPos) < iconRadius;

            Color iconColor =
                isHighlighted ?
                (PlayerInput.PrimaryMouseButtonHeld() ? shortCutStyle.PressedColor : shortCutStyle.HoverColor) :
                shortCutStyle.Color;

            shortcutSprite.Draw(spriteBatch,
                shortcutIconPos,
                color: isCurrentLocation ? iconColor * ShortcutIconCurrentHighlight : iconColor,
                scale: currIconScale);
        
            if (locationShortcutsPanel is { Visible: true } )
            {
                Vector2 nameOffset = LocationNameOffset;
                //make sure the name is not on the same side as the shortcut icon so they don't overlap
                if (shortcutIconPos.Y > mapIconPosition.Y)
                {
                    nameOffset.Y = -nameOffset.Y;
                }
                // Draw location name for locations with shortcut, so it's easier to match them with the named list
                DrawLocationName(spriteBatch, location, mapIconPosition, textColor: outpostData.IsPlayerOwned ? location.Type.SpriteColor : Color.White,  offset: nameOffset);
            }

            // Handle mouse over icon
            if (isHighlighted)
            {
                shortcutIconHighlightedOnLocation = location;
                GUIComponent.DrawToolTip(spriteBatch,
                    TextManager.Get(location.Visited? "locationshortcut.mapicon.tooltip" : "locationshortcut.mapicon.undiscovered.tooltip"),
                    new Rectangle(shortcutIconPos.ToPoint(), new Point((int)iconRadius)), // will get resized based on content
                    Anchor.Center);
            }
        }        
    }

    public static void OnUpdateMap(CampaignMode campaign, Map map, float deltaTime)
    {
        if (highlightPhase > 0)
        {
            highlightPhase -= deltaTime;
        }
        if (shortcutIconHighlightedOnLocation != null)
        {
            if (PlayerInput.PrimaryMouseButtonClicked())
            {
                bool isCurrentLocation = shortcutIconHighlightedOnLocation == map.CurrentLocation;
                if (locationShortcutsPanel != null)
                {
                    CloseShortcutsPanel();
                }
                if (isCurrentLocation)
                {
                    // Just open the menu without a destination selected
                    DrawLocationShortcutMenu(map.CurrentLocation, campaign);
                }
                else
                {
                    DrawLocationShortcutMenu(map.CurrentLocation, campaign, preselectedDestination: shortcutIconHighlightedOnLocation);
                }
            }            
        }
    }

    private static void HighlightLocation(Location location)
    {
        highlightedShortcutLocation = location;
        if (location != highlightedShortcutLocation)
        {
            highlightPhase = HighlightFlashDuration;
        }
    }

    private static void RemoveLocationHighlight()
    {
        highlightedShortcutLocation = null;
        shortcutIconHighlightedOnLocation = null;
        highlightPhase = 0;
    }
    
    public static void OnDrawCampaignUI(CampaignMode campaign, CampaignUI campaignUI)
    {
        // Initialize RectTransform correctly
        DrawLocationShortcutMenu(campaign.Map?.HighlightedLocation, campaign, parent: campaignUI.GetTabContainer(CampaignMode.InteractionType.Map).RectTransform);
    }
    
    public static void OnCampaignSelectTab(CampaignUI campaignUI, CampaignMode.InteractionType selectedTab)
    {
        // Make sure the shortcut panel is hidden when the map is opened
        if (locationShortcutsPanel != null)
        {
            locationShortcutsPanel.Visible = false;
        }
    }

    private static void CloseShortcutsPanel()
    {
        if (locationShortcutsPanel != null)
        {
            locationShortcutsPanel.RectTransform.Parent = null;
            locationShortcutsPanel = null;
        }
    }

    public static void DrawLocationShortcutMenu(Location? currentLocation, CampaignMode campaign, Location? preselectedDestination = null, RectTransform? parent = null)
    {
        if (parent is RectTransform panelParent)
        {
            if (shortcutPanelRectTransform != panelParent)
            {
                CloseShortcutsPanel();
                shortcutPanelRectTransform = panelParent;
            }
        }
        
        if (currentLocation == null) { return; }
        
        // Use the location info panel's position, because it's been designed to not overlap with selected locations
        campaign.CampaignUI.HideLocationInfoPanel();
        campaign.Map.ClearAllSelections();

        if (locationShortcutsPanel == null)
        {
            locationShortcutsPanel = new GUIFrame(
                new RectTransform(new Vector2(0.25f, 0.55f), shortcutPanelRectTransform, Anchor.CenterRight) { RelativeOffset = new Vector2(0.02f, 0.02f) }, 
                color: Color.Black)
            {
                Visible = false
            };
        }
        
        if (!IsShortcutAvailable(currentLocation))
        {
            locationShortcutsPanel.Visible = false;
            return;
        }
        
        locationShortcutsPanel.Visible = true;
        locationShortcutsPanel.ClearChildren();
        
        List<(Location Location, float Distance)> discoveredShortcuts = campaign.Map.Locations
            .Where(l => l.Visited && IsShortcutAvailable(l))
            .Select(location => (
                Location: location,
                Distance: Vector2.Distance(currentLocation.MapPosition, location.MapPosition)
            ))
            .ToList();

        var content = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.9f), locationShortcutsPanel.RectTransform, Anchor.Center), childAnchor: Anchor.TopCenter)
        {
            Stretch = true,
            RelativeSpacing = 0.02f,
        };
        
        new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform),
            currentLocation.DisplayName, font: GUIStyle.LargeFont, textAlignment: Alignment.Center)
        {
            AutoScaleHorizontal = true
        };

        HashSet<Location> discoveredPlayerOwnedShortcuts = new HashSet<Location>();
        foreach ((Location location, float _) in discoveredShortcuts)
        {
            var outpostData = PersistentOutpostData.GetPersistentOutpostData(location);
            if (outpostData is { IsPlayerOwned: true})
            {
                discoveredPlayerOwnedShortcuts.Add(location);
            }
        }

        discoveredShortcuts.Sort((location1, location2) => 
        {
            bool location1PlayerOwned = discoveredPlayerOwnedShortcuts.Contains(location1.Location);
            bool location2PlayerOwned = discoveredPlayerOwnedShortcuts.Contains(location2.Location);
            if (location1PlayerOwned != location2PlayerOwned) { return location2PlayerOwned.CompareTo(location1PlayerOwned); }
            return location1.Distance.CompareTo(location2.Distance);
        });

        // If this is the first shortcut discovered, only show a tutorial text
        if (discoveredShortcuts.Count == 1)
        {
            new GUITextBlock(new RectTransform(new Vector2(0.9f, 0.5f), content.RectTransform, Anchor.TopCenter),
                TextManager.Get("locationshortcut.firstdiscovered"))
            {
                TextAlignment = Alignment.TopCenter,
                Wrap = true
            };
            
            new GUIButton(new RectTransform(new Vector2(1.0f, 0.05f), content.RectTransform, Anchor.Center),
                TextManager.Get("ok"))
            {
                OnClicked = (btn, userData) =>
                {
                    locationShortcutsPanel.Visible = false;
                    return true;
                }
            };
            
            return;
        }

        new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform),
            TextManager.Get("locationshortcut.listheading"), font: GUIStyle.SmallFont)
        {
            Wrap = true
        };
        var shortcutList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.4f), content.RectTransform))
        {
            Spacing = (int)(5 * GUI.yScale)
        };

        bool wasPreviousPlayerOwned = false;
        foreach ((Location locationWithShortcut, float distance) in discoveredShortcuts)
        {
            if (locationWithShortcut == campaign.CurrentLocation) { continue; }

            bool isPlayerOwned = discoveredPlayerOwnedShortcuts.Contains(locationWithShortcut);
            if (!isPlayerOwned && wasPreviousPlayerOwned)
            {
                new GUIFrame(new RectTransform(new Vector2(0.8f, 0.01f), shortcutList.Content.RectTransform), style: "HorizontalLine")
                {
                    CanBeFocused = false
                };
            }

            var row = new GUIFrame(new RectTransform(new Vector2(1, 0.1f), shortcutList.Content.RectTransform), style: "ListBoxElement")
            {
                UserData = locationWithShortcut
            };
            var rowContent = new GUILayoutGroup(new RectTransform(Vector2.One, row.RectTransform), isHorizontal: true)
            {
                Stretch = true, 
                RelativeSpacing = 0.02f
            };
    
            Color iconColor = Color.White;
            if (locationWithShortcut.Faction?.Prefab is FactionPrefab factionPrefab)
            {
                iconColor = factionPrefab.IconColor;
            }
            var icon = new GUIImage(new RectTransform(new Vector2(0.1f, 1.0f), rowContent.RectTransform),
                locationWithShortcut.Type.Sprite, scaleToFit: true)
            {
                Color = iconColor,
                ToolTip = locationWithShortcut.Type.Name
            };

            var locationNameText = new GUITextBlock(new RectTransform(new Vector2(0.7f, 1.0f), rowContent.RectTransform),
                locationWithShortcut.DisplayName)
            {
                OverflowClip = true,
                TextAlignment = Alignment.CenterLeft
            };
            if (isPlayerOwned)
            {
                icon.Color = locationNameText.TextColor = locationWithShortcut.Type.SpriteColor;
                locationNameText.HoverTextColor = Color.Lerp(locationWithShortcut.Type.SpriteColor, Color.White, 0.5f);
            }
            wasPreviousPlayerOwned = isPlayerOwned;
            //shorten the name if necessary and show the full name in a tooltip
            locationNameText.Text = ToolBox.LimitString(locationWithShortcut.DisplayName.Value, locationNameText.Font, locationNameText.Rect.Width);
            if (locationNameText.Text != locationWithShortcut.DisplayName)
            {
                locationNameText.ToolTip = locationWithShortcut.DisplayName;
            }

            new GUITextBlock(new RectTransform(new Vector2(0.2f, 1.0f), rowContent.RectTransform),
                $"({TextManager.GetWithVariable("kilometerformatted", "[value]", MathF.Round(distance / MapGenerationParams.Instance.MapPixelsPerKilometer, 1).ToString("F1"))})", textColor: Color.LightGray, textAlignment: Alignment.Right);
        }
        
        new GUIButton(new RectTransform(new Vector2(1.0f, 0.05f), content.RectTransform),
            TextManager.Get("cancel"))
        {
            OnClicked = (btn, userData) =>
            {
                RemoveLocationHighlight();
                locationShortcutsPanel.Visible = false;
                return true;
            }
        };
        
        var takeShortcutButton = new GUIButton(new RectTransform(new Vector2(1.0f, 0.05f), content.RectTransform),
            TextManager.Get("locationshortcuts.takeshortcut"))
        {
            OnClicked = (btn, userData) =>
            {
                if (userData is Location targetLocation && CampaignMode.AllowedToManageCampaign(Barotrauma.Networking.ClientPermissions.ManageMap))
                {
                    CampaignMode.TryLeaveOutpostWithSubmarineSafetyCheck(onConfirm: () =>
                    {
                        campaign.Map.Teleport(campaign.GetCurrentDisplayLocation(), targetLocation, campaign, loadNewLevel: true);
                        if (locationShortcutsPanel != null)
                        {
                            locationShortcutsPanel.Visible = false;
                        }
                    });
                }
                return true;
            },
            Enabled = false // need to select a destination on the list first
        };
        
        shortcutList.OnSelected = (GUIComponent selected, object userdata) =>
        {
            if (userdata is Location targetLocation && CampaignMode.AllowedToManageCampaign(Barotrauma.Networking.ClientPermissions.ManageMap))
            {
                shortcutList.ScrollToElement(selected);

                takeShortcutButton.UserData = targetLocation;
                takeShortcutButton.Enabled = true;

                if (shortcutList.SelectedData != targetLocation)
                {
                    campaign.Map.PanToLocation(targetLocation);
                    HighlightLocation(targetLocation);
                }
            }
            else
            {
                takeShortcutButton.UserData = null;
                takeShortcutButton.Enabled = false;
            }
            return true;
        };
        shortcutList.OnHoverStarted = (GUIComponent hovered, object userdata) =>
        {
            if (userdata is Location targetLocation && CampaignMode.AllowedToManageCampaign(Barotrauma.Networking.ClientPermissions.ManageMap))
            {
                HighlightLocation(targetLocation);
            }
            return true;
        };
        shortcutList.OnHoverEnded = () =>
        {
            RemoveLocationHighlight();
            return true;
        };
        
        // When shortcut menu was opened from the map by clicking on a location's shortcut icon (not the current one)
        if (preselectedDestination != null)
        {
            shortcutList.Select(preselectedDestination);
            shortcutList.SelectedComponent?.Flash(HighlightSpriteColor, HighlightFlashDuration, useRectangleFlash: true);
        }
        else // opened from the current location
        {
            RemoveLocationHighlight();
            campaign.Map.PanToLocation(currentLocation);
        }
    }

    private static Vector2 LocationShortcutIconPos(Vector2 locationIconPos, float leastCrowdedDirection, float zoom, float distanceFromCenter)
    {
        return locationIconPos + (zoom * new Vector2(
            distanceFromCenter * MathF.Cos(leastCrowdedDirection),
            distanceFromCenter * MathF.Sin(leastCrowdedDirection)));
    }
    
    private static void DrawDirectionIndicator(SpriteBatch spriteBatch, Vector2 position, Vector2 direction, Location targetLocation)
    {
        Color factionColor = targetLocation.Faction?.Prefab.IconColor ?? Color.White;

        Vector2 iconPos = position - direction * OffscreenIndicatorMargin * GUI.Scale;
        float scale = GUI.Scale * 0.5f;

        GUI.Arrow.Draw(
            spriteBatch,
            iconPos + direction * (targetLocation.Type.Sprite.size.X * scale) / 2,
            factionColor,
            scale: scale,
            rotate: MathUtils.VectorToAngle(direction) + MathHelper.PiOver2
        );
        targetLocation.Type.Sprite.Draw(
            spriteBatch,
            iconPos,
            factionColor,
            scale: scale
        );
    }

    private static void DrawLocationName(SpriteBatch spriteBatch, Location location, Vector2 locationPosition, Color textColor, Vector2? offset = null)
    {
        GUI.DrawString(
            spriteBatch,
            locationPosition + (offset ?? Vector2.Zero) - GUIStyle.Font.MeasureString(location.DisplayName) / 2,
            location.DisplayName,
            textColor,
            backgroundColor: Color.Black * 0.75f,
            backgroundPadding: GUI.IntScale(5)
        );
    }
}
