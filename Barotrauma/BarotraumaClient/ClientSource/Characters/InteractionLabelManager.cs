#nullable enable
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using Barotrauma.Items.Components;

namespace Barotrauma;

public static class InteractionLabelManager
{
   
    private class LabelData
    {
        private readonly Camera drawCamera;
        public readonly Item Item;

        public RectangleF TextRect { get; set; }

        public readonly Vector2 OriginalItemPosition;

        public bool OverlapPreventionDone;

        public LabelData(Item item, RectangleF textRect, Camera drawCamera)
        {
            Item = item;
            TextRect = textRect;
            OriginalItemPosition = item.Position;
            this.drawCamera = drawCamera;
        }
        
        public RectangleF GetScreenDrawRect(Camera cam)
        {
            float scale = cam.Zoom;
            RectangleF screenDrawRect = TextRect;
                screenDrawRect.Location = drawCamera
                    .WorldToScreen(screenDrawRect.Location + (Item.Submarine?.DrawPosition ?? Vector2.Zero));
                
            return new RectangleF(
                screenDrawRect.X, 
                screenDrawRect.Y,
                screenDrawRect.Width * scale,
                screenDrawRect.Height * scale);
        }

        public Vector2 GetInteractableDrawPositionScreen()
        {
            return drawCamera.WorldToScreen(Item.DrawPosition);
        }
    }
    
    private static readonly List<LabelData> labels = new();

    private const int TextBoxMarginPx = 4;

    /// <summary>
    /// Multiplier on the scale of the labels. Ad-hoc formula: since the zoom affects the size of the labels, 
    /// and high resolutions are more zoomed in to keep the view range the same, let's scale down the labels on large resolutions to compensate.
    /// </summary>
    private static float LabelScale => 1.0f / GUI.Scale;

    private static InteractionLabelDisplayMode displayMode;

    private static int graphicsWidth, graphicsHeight;

    private static bool shouldRecalculate;
    private static bool recalculateEverything;
    
    private static readonly  List<Item> interactablesInRange = new();

    internal static Item? HoveredItem { get; private set; }
    
    internal static void RefreshInteractablesInRange(List<Item> interactables)
    {
        interactablesInRange.Clear();
        interactablesInRange.AddRange(interactables);
        
        shouldRecalculate = true;
    }

    private static void RecalculateLabelPositions(Camera cam, Character character)
    {
        if (recalculateEverything)
        {
            labels.Clear();
            recalculateEverything = false;
        }

        labels.RemoveAll(l => !interactablesInRange.Contains(l.Item));
        
        // for every interactable, create a label data object with relevant info for real-time drawing
        foreach (var interactableInRange in interactablesInRange)
        {
            // this removes the hidden vents from the list
            if (interactableInRange.HasTag(Tags.HiddenItemContainer)) { continue; }
            
            // filter out items depending on visibility filter setting
            switch (displayMode)
            {
                case InteractionLabelDisplayMode.InteractionAvailable when !interactableInRange.HasVisibleInteraction(character):
                case InteractionLabelDisplayMode.LooseItems when !IsLooseItem(interactableInRange):
                    continue;
            }

            RectangleF textRect = GetLabelRect(interactableInRange, cam);

            if (labels.None(l => l.Item == interactableInRange))
            {
                var labelData = new LabelData(interactableInRange, textRect, cam);
                labels.Add(labelData);
            }
        }
        
        PreventInteractionLabelOverlap(centerPos: character.Position);
    }
    
    private static bool IsLooseItem(Item item)
    {
        bool hasActivePhysics = item.body is { Enabled: true };
        bool hasPickableComponent = item.GetComponent<Pickable>() != null;
        return hasActivePhysics && hasPickableComponent;
    }

    private static RectangleF GetLabelRect(Item item, Camera cam)
    {
        // create rectangle for overlap prevention
        Vector2 itemTextSizeScreen = GUIStyle.SubHeadingFont.MeasureString(item.Name) * LabelScale;
        Vector2 interactablePosScreen = cam.WorldToScreen(item.Position);
        RectangleF textRect = new RectangleF(interactablePosScreen.X, interactablePosScreen.Y, itemTextSizeScreen.X, itemTextSizeScreen.Y);
        // center the rectangle on the item
        textRect.X -= textRect.Width / 2;
        textRect.Y += textRect.Height / 2;

        // inflate by a bit, because the text is drawn with padding
        textRect.Inflate(TextBoxMarginPx * LabelScale, TextBoxMarginPx * LabelScale);

        // the rect has screen space size, and sub-relative position
        textRect.Location = cam.ScreenToWorld(textRect.Location);
        return textRect;
    }

    private static void PreventInteractionLabelOverlap(Vector2 centerPos)
    {
        //sort by distance from "centerPos": moving labels further away from the character (or whatever the center is) is preferred
        labels.Sort((l1, l2) =>
            Vector2.DistanceSquared(l1.TextRect.Center, centerPos).CompareTo(
            Vector2.DistanceSquared(l2.TextRect.Center, centerPos)));

        const float MoveStep = 10.0f;
        bool intersections = true;
        int iterations = 0;
        int maxIterations = System.Math.Max(labels.Count * labels.Count, 100);

        while (intersections && iterations < maxIterations)
        {
            intersections = false;
            foreach (var label in labels)
            {
                if (label.OverlapPreventionDone) { continue; }
                foreach (var otherLabel in labels)
                {
                    if (label == otherLabel) { continue; }

                    //allow labels to overlap if there's multiple instances of the same item at (roughly) the same position
                    if (label.Item.Prefab == otherLabel.Item.Prefab &&
                        Vector2.DistanceSquared(label.Item.WorldPosition, otherLabel.Item.WorldPosition) < 1.0f)
                    {
                        continue;
                    }

                    if (!label.TextRect.Intersects(otherLabel.TextRect))
                    {
                        continue;
                    }
                    intersections = true;
                    Vector2 moveAmount = Vector2.Normalize(label.TextRect.Center - centerPos) * MoveStep;
                    label.TextRect = new RectangleF(label.TextRect.Location + moveAmount, label.TextRect.Size);
                }
                if (intersections) { break; }
            }
            iterations++;
        }

        foreach (var labelData in labels)
        {
            labelData.OverlapPreventionDone = true;
        }
    }

    private static int GetMouseHoveredLabelIndex(Camera cam)
    {
        for (int i = 0; i < labels.Count; i++)
        {
            var labelData = labels[i];
            var drawRect = labelData.GetScreenDrawRect(cam);
            if (drawRect.Contains(PlayerInput.MousePosition))
            {
                return i;
            }
        }        
        return -1;
    }

    private static bool RefreshSettings()
    {
        bool settingsChanged = false;
        
        if (GameSettings.CurrentConfig.InteractionLabelDisplayMode != displayMode)
        {
            displayMode = GameSettings.CurrentConfig.InteractionLabelDisplayMode;
            settingsChanged = true;
        }
        
        if (GameMain.GraphicsWidth != graphicsWidth || GameMain.GraphicsHeight != graphicsHeight)
        {
            graphicsWidth = GameMain.GraphicsWidth;
            graphicsHeight = GameMain.GraphicsHeight;
            settingsChanged = true;
        }

        return settingsChanged;
    }

    internal static void Update(Character character, Camera cam)
    {
        if (RefreshSettings()) { shouldRecalculate = true; recalculateEverything = true; }
        
        if (shouldRecalculate)
        {
            RecalculateLabelPositions(cam, character);
        }
    }

    internal static void DrawLabels(SpriteBatch spriteBatch, Camera cam, Character character)
    {
        //if any item changes subs or moves significantly, we need to recalculate the label position
        foreach (var label in labels)
        {
            const float MoveThreshold = 150.0f;
            if (Vector2.DistanceSquared(label.OriginalItemPosition, label.Item.Position) > MoveThreshold * MoveThreshold)
            {
                label.TextRect = GetLabelRect(label.Item, cam);
            }
        }

        // find out if mouse is on top of any of the labels
        int mouseOnLabelIndex = GetMouseHoveredLabelIndex(cam);
        bool isMouseOnLabel = mouseOnLabelIndex >= 0;

        const float LineAlpha = 0.5f;

        if (!isMouseOnLabel)
        {
            HoveredItem = null;
        }

        // draw order: draw lines for labels first
        for (int i = 0; i < labels.Count; i++)
        {
            // Skip the box that the mouse is on, it will be drawn last
            if (i == mouseOnLabelIndex) { continue; }
            
            DrawLineForLabel(spriteBatch, cam, labels[i], GUIStyle.InteractionLabelColor * LineAlpha);
        }

        // Then draw labels
        for (int i = 0; i < labels.Count; i++)
        {
            // Skip the box that the mouse is on, it will be drawn last
            if (i == mouseOnLabelIndex) { continue; }
            
            DrawLabelForItem(spriteBatch, cam, labels[i], GUIStyle.InteractionLabelColor);
        }

        // Draw the label and line that the mouse is on last (for draw order)
        if (isMouseOnLabel)
        {
            var labelData = labels[mouseOnLabelIndex];
            
            HoveredItem = labelData.Item;
            
            DrawLineForLabel(spriteBatch, cam, labelData, GUIStyle.InteractionLabelHoverColor * LineAlpha);
            DrawLabelForItem(spriteBatch, cam,labelData, GUIStyle.InteractionLabelHoverColor);
        }
    }

    private static void DrawLineForLabel(SpriteBatch spriteBatch, Camera cam, LabelData labelData, Color color)
    {
        var drawRect = labelData.GetScreenDrawRect(cam);
        // deflate by one pixel to avoid gap between line and box graphic edge
        const int lineAnchorInsetPx = 1;
        var deflateAmount = lineAnchorInsetPx * GUI.Scale;
        deflateAmount = MathHelper.Max(deflateAmount * Screen.Selected.Cam.Zoom, lineAnchorInsetPx);
        drawRect.Inflate(-deflateAmount, -deflateAmount);

        var itemDrawPosScreen = labelData.GetInteractableDrawPositionScreen();

        // if item position is inside the box, don't draw a line
        if (drawRect.Contains(itemDrawPosScreen)) { return; }

        // find the point on the box edge that is closest to the item
        Vector2 textLineAnchorScreenPos = new Vector2(
            MathHelper.Clamp(itemDrawPosScreen.X, drawRect.Left, drawRect.Right),
            MathHelper.Clamp(itemDrawPosScreen.Y, drawRect.Top, drawRect.Bottom));

        // draw line from label to item in the world
        GUI.DrawLine(spriteBatch, textLineAnchorScreenPos, itemDrawPosScreen, color, depth: 0f, width: 2f);
    }

    private static void DrawLabelForItem(SpriteBatch spriteBatch, Camera cam, LabelData labelData, Color color)
    {
        float scale = Screen.Selected.Cam.Zoom * LabelScale;

        var textDrawRect = labelData.GetScreenDrawRect(cam);
        RectangleF backgroundRect = textDrawRect;

        // remove margin from the box the text is drawn in
        textDrawRect.Inflate(-TextBoxMarginPx * scale, -TextBoxMarginPx * scale);
        Vector2 textDrawPosScreen = new Vector2(textDrawRect.X, textDrawRect.Y);
        
        GUIStyle.InteractionLabelBackground.Draw(spriteBatch, backgroundRect, color * 0.7f);

        GUIStyle.SubHeadingFont.DrawString(spriteBatch,
            labelData.Item.Name, 
            textDrawPosScreen, color, rotation: 0, origin: Vector2.Zero, scale, spriteEffects: SpriteEffects.None, layerDepth: 0.0f,
            forceUpperCase: ForceUpperCase.No);
    }
}