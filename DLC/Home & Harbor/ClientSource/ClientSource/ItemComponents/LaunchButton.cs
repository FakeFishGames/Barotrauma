using Barotrauma;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Barotrauma.Sounds;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Xml.Linq;
namespace HomeAndHarbor;

public partial class LaunchButton : ItemComponent
{
    private const float BackgroundFadeDuration = 0.5f;
    private const float ArrowPulseSpeed = 4.0f;
    private const float LaunchDelay = 0.5f;

    private readonly struct LaunchButtonEventData : IEventData
    {
        public readonly bool Launch;

        public LaunchButtonEventData(bool launch) => Launch = launch;
    }

    private bool launching;

    private RoundSound? lidOpenSound, pressSound;

    /// <summary>
    /// The launch sound isn't loaded as a <see cref="RoundSound"/>, because round sounds are disposed when the submarine unloads,
    /// and the launch sound needs to keep playing on the ending screen. Instead it's loaded on first use and kept loaded
    /// </summary>
    private ContentXElement? launchSoundElement;
    private static Sound? launchSound;

    partial void InitProjectSpecific(ContentXElement element)
    {
        launchSoundElement = element.GetChildElement("launchsound");
        if (element.GetChildElement("lidopensound") is { } lidOpenSoundElement)
        {
            lidOpenSound = RoundSound.Load(lidOpenSoundElement);
        }
        if (element.GetChildElement("presssound") is { } pressSoundElement)
        {
            pressSound = RoundSound.Load(pressSoundElement);
        }
    }

    protected override void CreateGUI()
    {
        if (GuiFrame == null) { return; }

        new GUICustomComponent(new RectTransform(Vector2.One, GuiFrame.RectTransform), onDraw: DrawBackground) { CanBeFocused = false };

        var launchButton = new GUIButton(new RectTransform(new Vector2(0.6f), GuiFrame.RectTransform, Anchor.Center, scaleBasis: ScaleBasis.Smallest), style: null)
        {
            ToolTip = TextManager.Get(ButtonText).Fallback(ButtonText),
            PlaySoundOnSelect = false,
            OnClicked = (btn, userdata) =>
            {
                if (!lidOpen)
                {
                    OpenLid();
                    Item.CreateClientEvent(this, new LaunchButtonEventData(launch: false));
                    return false;
                }
                if (launching || IsActive) { return false; }
                launching = true;
                if (pressSound != null)
                {
                    SoundPlayer.PlaySound(pressSound, Item.WorldPosition, hullGuess: Item.CurrentHull);
                }
                PlayLaunchSound();
                DlcEndingManager.StartEndingMusic();
                //wait a moment before launching (which deselects the item) so the player gets to see the button being pressed down
                Character? character = Character.Controlled;
                CoroutineManager.Invoke(() =>
                {
                    launching = false;
                    if (Item.Removed || character == null || character.Removed) { return; }
                    Launch(character);
                    Item.CreateClientEvent(this, new LaunchButtonEventData(launch: true));
                }, LaunchDelay);
                return true;
            }
        };
        new GUICustomComponent(new RectTransform(Vector2.One, launchButton.RectTransform), onDraw: (spriteBatch, component) => DrawButton(spriteBatch, component.Rect, launchButton.State)) { CanBeFocused = false };
    }

    private void DrawBackground(SpriteBatch spriteBatch, GUICustomComponent component)
    {
        //fade back and forth between the two background frames (0 = first frame, 1 = second frame)
        float cycle = (float)(Timing.TotalTime / BackgroundFadeDuration % 2.0);
        float t = MathHelper.SmoothStep(0.0f, 1.0f, cycle < 1.0f ? cycle : 2.0f - cycle);
        //both frames are fully opaque halfway through the fade, so the background never becomes see-through
        //(and the parts of the first frame that extend past the second one fade out gradually instead of popping out)
        DrawFitted(spriteBatch, GUIStyle.GetComponentStyle("LaunchButtonBackground")?.GetDefaultSprite(), component.Rect, Color.White * Math.Min(2.0f * (1.0f - t), 1.0f));
        DrawFitted(spriteBatch, GUIStyle.GetComponentStyle("LaunchButtonBackgroundAlt")?.GetDefaultSprite(), component.Rect, Color.White * Math.Min(2.0f * t, 1.0f));
    }

    private void DrawButton(SpriteBatch spriteBatch, Rectangle area, GUIComponent.ComponentState buttonState)
    {
        if (!lidOpen)
        {
            float glowAlpha = (MathF.Sin((float)Timing.TotalTime * ArrowPulseSpeed) + 1.0f) / 2.0f;
            DrawFitted(spriteBatch, GUIStyle.GetComponentStyle("LaunchButtonClosed")?.GetDefaultSprite(), area, Color.White);
            DrawFitted(spriteBatch, GUIStyle.GetComponentStyle("LaunchButtonClosedGlow")?.GetDefaultSprite(), area, Color.White * glowAlpha);
            return;
        }

        var state = IsActive || launching ? GUIComponent.ComponentState.Pressed : buttonState;
        DrawFitted(spriteBatch, GUIStyle.GetComponentStyle("LaunchButton")?.GetSprite(state), area, Color.White);
    }

    private static void DrawFitted(SpriteBatch spriteBatch, Sprite? sprite, Rectangle area, Color color)
    {
        if (sprite == null) { return; }
        float scale = Math.Min(area.Width / sprite.size.X, area.Height / sprite.size.Y);
        sprite.Draw(spriteBatch, area.Center.ToVector2(), color, sprite.size / 2, rotate: 0.0f, scale: scale);
    }

    private void OpenLid()
    {
        if (lidOpen) { return; }
        lidOpen = true;
        if (lidOpenSound != null)
        {
            SoundPlayer.PlaySound(lidOpenSound, Item.WorldPosition, hullGuess: Item.CurrentHull);
        }
    }

    private void PlayLaunchSound()
    {
        if (launchSoundElement == null) { return; }
        if (launchSound is not { Disposed: false })
        {
            try
            {
                launchSound = GameMain.SoundManager?.LoadSound(launchSoundElement, stream: launchSoundElement.GetAttributeBool("stream", true));
            }
            catch (System.IO.FileNotFoundException e)
            {
                DebugConsole.ThrowError("Failed to load the rocket launch sound.", e);
                return;
            }
        }
        launchSound?.Play();
    }

    public void ClientEventWrite(IWriteMessage msg, NetEntityEvent.IData? extraData = null)
    {
        msg.WriteBoolean(extraData is Item.ComponentStateEventData { ComponentData: LaunchButtonEventData eventData } && eventData.Launch);
    }

    public void ClientEventRead(IReadMessage msg, float sendingTime)
    {
        bool lidOpenReceived = msg.ReadBoolean();
        bool launched = msg.ReadBoolean();
        if (lidOpenReceived) { OpenLid(); }
        if (launched)
        {
            if (!IsActive) { PlayLaunchSound(); }
            IsActive = true;
            DlcEndingManager.StartEndingMusic();
        }
    }
}
