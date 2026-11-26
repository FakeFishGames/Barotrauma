using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Barotrauma;

public sealed class SpeechBubble
{
    public float LifeTime;
    public Vector2 PrevPosition;
    public Vector2 Position;
    public Vector2 DrawPosition;
    public float MoveUpAmount;
    public readonly RichString Text;
    public ImmutableArray<RichTextData>? RichTextData { get; private set; }

    public readonly ISpatialEntity Entity;
    public readonly Submarine Submarine;
    public readonly Vector2 TextSize;

    public Color Color;
    public bool Moving;

    public SpeechBubble(ISpatialEntity targetEntity, float lifeTime, Color color, string text = "")
    {
        var richStr = RichString.Rich(text);
        Text = ToolBox.WrapText(richStr.SanitizedValue, GUI.IntScale(300), GUIStyle.SmallFont.GetFontForStr(text));
        TextSize = GUIStyle.SmallFont.MeasureString(Text);
        RichTextData = richStr.RichTextData;

        Entity = targetEntity;
        Position = GetDesiredPosition();
        Submarine = targetEntity.Submarine;
        LifeTime = lifeTime;
        Color = color;
    }

    public Vector2 GetDesiredPosition()
    {
        if (Entity is MapEntity mapEntity)
        {
            return mapEntity.Position + Vector2.UnitY * mapEntity.Rect.Height;
        }

        return Entity.Position + Vector2.UnitY * 100;
    }
}

public class SpeechBubbleManager
{
    public static IReadOnlyList<SpeechBubble> SpeechBubbles => speechBubbles;

    private static List<SpeechBubble> speechBubbles = [];

    public static void ShowSpeechBubble(SpeechBubble speechBubble)
    {
        speechBubbles.Add(speechBubble);
    }

    public static void DrawSpeechBubbles(SpriteBatch spriteBatch, Camera cam)
    {
        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearWrap, DepthStencilState.None, null, null, cam.Transform);
        foreach (var bubble in speechBubbles)
        {
            Vector2 iconPos = Timing.Interpolate(bubble.PrevPosition, bubble.Position);
            iconPos += Vector2.UnitY * bubble.MoveUpAmount;
            if (bubble.Submarine != null)
            {
                iconPos += bubble.Submarine.DrawPosition;
            }

            float alpha = 1.0f;
            float mouseDist = Vector2.Distance(cam.WorldToScreen(iconPos), PlayerInput.MousePosition);
            //treat the size of the bubble from corner to corner as the 
            float textSize = bubble.TextSize.Length();
            if (mouseDist < textSize)
            {
                alpha *= Math.Max(mouseDist / textSize, 0.5f);
            }

            iconPos.Y = -iconPos.Y;
            if (GUIStyle.SpeechBubbleIconSliced.Value is { } speechBubbleIconSliced)
            {
                Vector2 bubbleSize = bubble.TextSize + Vector2.One * GUI.IntScale(15);
                speechBubbleIconSliced.Draw(spriteBatch, new RectangleF(iconPos - bubbleSize / 2, bubbleSize), bubble.Color * Math.Min(bubble.LifeTime, 1.0f) * alpha);
            }
            GUI.DrawStringWithColors(spriteBatch, iconPos - bubble.TextSize / 2, bubble.Text.SanitizedValue, bubble.Color * Math.Min(bubble.LifeTime, 1.0f) * alpha, bubble.RichTextData, font: GUIStyle.SmallFont);
        }
        spriteBatch.End();
    }


    public static void UpdateSpeechBubbles(float deltaTime)
    {
        for (int i = speechBubbles.Count - 1; i >= 0; i--)
        {
            var bubble = speechBubbles[i];
            bubble.LifeTime -= deltaTime;
            if (bubble.LifeTime <= 0 || bubble.Entity is Entity { Removed: true })
            {
                speechBubbles.RemoveAt(i);
                continue;
            }

            bubble.PrevPosition = bubble.Position;

            Vector2 desiredPos = bubble.GetDesiredPosition();
            Vector2 diff = desiredPos - bubble.Position;
            float dist = diff.Length();
            //how far the bubble needs to be from the desired position to start moving
            const float MoveThreshold = 100.0f;
            const float MaxSpeed = 1000.0f;
            if (dist < 1)
            {
                bubble.Moving = false;
            }
            else if (dist > MoveThreshold || bubble.Moving)
            {
                Vector2 moveAmount = diff / dist * MathHelper.Clamp(dist * 5, 0, MaxSpeed) * deltaTime;
                //slower vertical movement (don't want to interfere too much with the bubbles floating up
                //and the overlap prevention which works vertically)
                moveAmount.Y *= 0.1f;
                bubble.Position += moveAmount;
                bubble.Moving = true;
            }

            bubble.MoveUpAmount += deltaTime * 5.0f;
            //go through the newer bubbles, move this one out of the way if one is overlapping
            for (int j = i + 1; j < speechBubbles.Count; j++)
            {
                var otherBubble = speechBubbles[j];
                {
                    if (Math.Abs(bubble.Position.X - otherBubble.Position.X) < (bubble.TextSize.X + otherBubble.TextSize.X) / 2 &&
                        Math.Abs(bubble.Position.Y - otherBubble.Position.Y) < (bubble.TextSize.Y + otherBubble.TextSize.Y) / 2 + 10)
                    {
                        bubble.Position += Vector2.UnitY * deltaTime * 50.0f;
                    }
                }
            }
        }
    }

    public static void ClearSpeechBubbles()
    {
        speechBubbles.Clear();
    }
}