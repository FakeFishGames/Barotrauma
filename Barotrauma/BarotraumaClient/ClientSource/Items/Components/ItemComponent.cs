﻿using Barotrauma.Networking;
using Barotrauma.Sounds;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    enum SoundSelectionMode
    {
        Random,
        CharacterSpecific,
        ItemSpecific,
        All,
        Manual
    }

    class ItemSound
    {
        public readonly RoundSound RoundSound;
        public readonly ActionType Type;

        public Identifier VolumeProperty;

        public float VolumeMultiplier
        {
            get { return RoundSound.Volume; }
        }
        
        public float Range
        {
            get { return RoundSound.Range; }
        }

        public readonly bool Loop;

        public readonly bool OnlyPlayInSameSub;

        public ItemSound(RoundSound sound, ActionType type, bool loop = false, bool onlyPlayInSameSub = false)
        {
            this.RoundSound = sound;
            this.Type = type;
            this.Loop = loop;
            this.OnlyPlayInSameSub = onlyPlayInSameSub;
        }
    }

    partial class ItemComponent : ISerializableEntity
    {
        public bool HasSounds
        {
            get { return sounds.Count > 0; }
        }

        public bool[] HasSoundsOfType { get { return hasSoundsOfType; }  }

        private readonly bool[] hasSoundsOfType;
        private readonly Dictionary<ActionType, List<ItemSound>> sounds;
        private Dictionary<ActionType, SoundSelectionMode> soundSelectionModes;

        /// <summary>
        /// Starts the timer for  delayed client-side corrections (<see cref="StartDelayedCorrection(IReadMessage, float, bool)"/>) - in other words,
        /// the client will not attempt to read server updates for this component until the timer elapses.
        /// </summary>
        protected float correctionTimer;

        public float IsActiveTimer;

        public virtual bool RecreateGUIOnResolutionChange => false;

        public GUILayoutSettings DefaultLayout { get; protected set; }
        public GUILayoutSettings AlternativeLayout { get; protected set; }

        public class GUILayoutSettings
        {
            public Vector2? RelativeSize { get; private set; }
            public Point? AbsoluteSize { get; private set; }
            public Vector2? RelativeOffset { get; private set; }
            public Point? AbsoluteOffset { get; private set; }
            public Anchor? Anchor { get; private set; }
            public Pivot? Pivot { get; private set; }

            public static GUILayoutSettings Load(XElement element)
            {
                var layout = new GUILayoutSettings();
                var relativeSize = XMLExtensions.GetAttributeVector2(element, "relativesize", Vector2.Zero);
                var absoluteSize = XMLExtensions.GetAttributePoint(element, "absolutesize", new Point(-1000, -1000));
                var relativeOffset = XMLExtensions.GetAttributeVector2(element, "relativeoffset", Vector2.Zero);
                var absoluteOffset = XMLExtensions.GetAttributePoint(element, "absoluteoffset", new Point(-1000, -1000));
                if (relativeSize.Length() > 0)
                {
                    layout.RelativeSize = relativeSize;
                }
                if (absoluteSize.X > 0 && absoluteSize.Y > 0)
                {
                    layout.AbsoluteSize = absoluteSize;
                }
                if (relativeOffset.Length() > 0)
                {
                    layout.RelativeOffset = relativeOffset;
                }
                if (absoluteOffset.X > -1000 && absoluteOffset.Y > -1000)
                {
                    layout.AbsoluteOffset = absoluteOffset;
                }
                if (Enum.TryParse(XMLExtensions.GetAttributeString(element, "anchor", ""), out Anchor a))
                {
                    layout.Anchor = a;
                }
                if (Enum.TryParse(XMLExtensions.GetAttributeString(element, "pivot", ""), out Pivot p))
                {
                    layout.Pivot = p;
                }
                return layout;
            }

            public void ApplyTo(RectTransform target)
            {
                if (RelativeOffset.HasValue)
                {
                    target.RelativeOffset = RelativeOffset.Value;
                }
                else if (AbsoluteOffset.HasValue)
                {
                    target.AbsoluteOffset = AbsoluteOffset.Value;
                }
                if (RelativeSize.HasValue)
                {
                    target.RelativeSize = RelativeSize.Value;
                }
                else if (AbsoluteSize.HasValue)
                {
                    target.NonScaledSize = AbsoluteSize.Value;
                }
                if (Anchor.HasValue)
                {
                    target.Anchor = Anchor.Value;
                }
                if (Pivot.HasValue)
                {
                    target.Pivot = Pivot.Value;
                }
                else
                {
                    target.Pivot = RectTransform.MatchPivotToAnchor(target.Anchor);
                }
                target.RecalculateChildren(true, true);
            }
        }

        public GUIFrame GuiFrame { get; set; }

        /// <summary>
        /// Overlay (just a non-interactable sprite) drawn when the item is selected, equipped or focused to via Controllers (e.g. when operating a turret via a periscope or a camera via a monitor).
        /// </summary>
        public Sprite HUDOverlay { get; set; }

        public float HUDOverlayAnimSpeed
        {
            get;
            set;
        }

        private GUIDragHandle guiFrameDragHandle;

        private bool guiFrameUpdatePending;

        [Serialize(false, IsPropertySaveable.No)]
        public bool AllowUIOverlap
        {
            get;
            set;
        }

        [Serialize(true, IsPropertySaveable.No)]
        public bool CloseByClickingOutsideGUIFrame
        {
            get;
            set;
        }

        private ItemComponent linkToUIComponent;
        [Serialize("", IsPropertySaveable.No)]
        public string LinkUIToComponent
        {
            get;
            set;
        }

        [Serialize(0, IsPropertySaveable.No)]
        public int HudPriority
        {
            get;
            private set;
        }

        [Serialize(0, IsPropertySaveable.No)]
        public int HudLayer
        {
            get;
            private set;
        }

        private bool useAlternativeLayout;
        public bool UseAlternativeLayout
        {
            get { return useAlternativeLayout; }
            set
            {
                if (AlternativeLayout != null)
                {
                    if (value == useAlternativeLayout) { return; }
                    useAlternativeLayout = value;
                    if (useAlternativeLayout)
                    {
                        AlternativeLayout?.ApplyTo(GuiFrame.RectTransform);
                    }
                    else
                    {
                        DefaultLayout?.ApplyTo(GuiFrame.RectTransform);
                    }
                }
            }
        }

        private bool shouldMuffleLooping;
        private float lastMuffleCheckTime;
        private ItemSound loopingSound;
        private SoundChannel loopingSoundChannel;
        private readonly List<SoundChannel> playingOneshotSoundChannels = new List<SoundChannel>();
        public ItemComponent ReplacedBy;

        public ItemComponent GetReplacementOrThis()
        {
            if (ReplacedBy != null && ReplacedBy != this)
            {
                return ReplacedBy.GetReplacementOrThis();
            }
            return this;
        }

        public bool NeedsSoundUpdate()
        {
            if (hasSoundsOfType[(int)ActionType.Always]) { return true; }
            if (loopingSoundChannel != null && loopingSoundChannel.IsPlaying) { return true; }
            if (playingOneshotSoundChannels.Count > 0) { return true; }
            return false;
        }

        public void UpdateSounds()
        {
            if (loopingSound != null && loopingSoundChannel != null && loopingSoundChannel.IsPlaying)
            {
                if (Timing.TotalTime > lastMuffleCheckTime + 0.2f)
                {
                    shouldMuffleLooping = SoundPlayer.ShouldMuffleSound(Character.Controlled, item.WorldPosition, loopingSound.Range, Character.Controlled?.CurrentHull);
                    lastMuffleCheckTime = (float)Timing.TotalTime;
                }
                loopingSoundChannel.Muffled = shouldMuffleLooping;
                float targetGain = GetSoundVolume(loopingSound);
                float gainDiff = targetGain - loopingSoundChannel.Gain;
                loopingSoundChannel.Gain += Math.Abs(gainDiff) < 0.1f ? gainDiff : Math.Sign(gainDiff) * 0.1f;
                loopingSoundChannel.Position = new Vector3(item.WorldPosition, 0.0f);
                loopingSound.RoundSound.LastStreamSeekPos = loopingSoundChannel.StreamSeekPos;
            }
            for (int i = 0; i < playingOneshotSoundChannels.Count; i++)
            {
                if (!playingOneshotSoundChannels[i].IsPlaying)
                {
                    playingOneshotSoundChannels[i].Dispose();
                    playingOneshotSoundChannels[i] = null;
                }
            }
            playingOneshotSoundChannels.RemoveAll(ch => ch == null);
            foreach (SoundChannel channel in playingOneshotSoundChannels)
            {
                channel.Position = new Vector3(item.WorldPosition, 0.0f);
            }
        }

        public void PlaySound(ActionType type, Character user = null)
        {
            if (!hasSoundsOfType[(int)type]) { return; }
            if (GameMain.Client?.MidRoundSyncing ?? false) { return; }

            //above the top boundary of the level (in an inactive respawn shuttle?)
            if (item.Submarine != null && item.Submarine.IsAboveLevel) 
            {
                return; 
            }

            if (loopingSound != null)
            {
                if (Vector3.DistanceSquared(GameMain.SoundManager.ListenerPosition, new Vector3(item.WorldPosition, 0.0f)) > loopingSound.Range * loopingSound.Range ||
                    (GetSoundVolume(loopingSound)) <= 0.0001f)
                {
                    if (loopingSoundChannel != null)
                    {
                        loopingSoundChannel.FadeOutAndDispose(); 
                        loopingSoundChannel = null;
                        loopingSound = null;
                    }
                    return;
                }

                if (loopingSoundChannel != null && loopingSoundChannel.Sound != loopingSound.RoundSound.Sound)
                {
                    loopingSoundChannel.FadeOutAndDispose();
                    loopingSoundChannel = null;
                    loopingSound = null;
                }

                if (loopingSoundChannel == null || !loopingSoundChannel.IsPlaying)
                {
                    loopingSoundChannel = loopingSound.RoundSound.Sound.Play(
                        new Vector3(item.WorldPosition, 0.0f), 
                        0.01f,
                        loopingSound.RoundSound.GetRandomFrequencyMultiplier(),
                        SoundPlayer.ShouldMuffleSound(Character.Controlled, item.WorldPosition, loopingSound.Range, Character.Controlled?.CurrentHull));
                    if (loopingSoundChannel != null) 
                    { 
                        loopingSoundChannel.Looping = true;
                        item.CheckNeedsSoundUpdate(this);
                        loopingSoundChannel.Near = loopingSound.Range * 0.4f;
                        loopingSoundChannel.Far = loopingSound.Range;
                    }
                }

                // Looping sound with manual selection mode should be changed if value of ManuallySelectedSound has changed
                // Otherwise the sound won't change until the sound condition (such as being active) is disabled and re-enabled
                if (loopingSoundChannel != null && loopingSoundChannel.IsPlaying && soundSelectionModes[type] == SoundSelectionMode.Manual)
                {
                    var playingIndex = sounds[type].IndexOf(loopingSound);
                    var shouldBePlayingIndex = Math.Clamp(ManuallySelectedSound, 0, sounds[type].Count);
                    if (playingIndex != shouldBePlayingIndex)
                    {
                        loopingSoundChannel.FadeOutAndDispose();
                        loopingSoundChannel = null;
                        loopingSound = null;
                    }
                }
                return;
            }

            var matchingSounds = sounds[type];
            if (loopingSoundChannel == null || !loopingSoundChannel.IsPlaying)
            {
                SoundSelectionMode soundSelectionMode = soundSelectionModes[type];
                int index;
                if (soundSelectionMode == SoundSelectionMode.CharacterSpecific && user != null)
                {
                    index = user.ID % matchingSounds.Count;
                }
                else if (soundSelectionMode == SoundSelectionMode.ItemSpecific)
                {
                    index = item.ID % matchingSounds.Count;
                }
                else if (soundSelectionMode == SoundSelectionMode.All)
                {
                    foreach (ItemSound sound in matchingSounds)
                    {
                        PlaySound(sound, item.WorldPosition);
                    }
                    return;
                }
                else if (soundSelectionMode == SoundSelectionMode.Manual)
                {
                    index = Math.Clamp(ManuallySelectedSound, 0, matchingSounds.Count - 1);
                }
                else
                {
                    index = Rand.Int(matchingSounds.Count);
                }

                PlaySound(matchingSounds[index], item.WorldPosition);
                item.CheckNeedsSoundUpdate(this);
            }
        }
        private void PlaySound(ItemSound itemSound, Vector2 position)
        {
            if (Vector2.DistanceSquared(new Vector2(GameMain.SoundManager.ListenerPosition.X, GameMain.SoundManager.ListenerPosition.Y), position) > itemSound.Range * itemSound.Range)
            {
                return;
            }

            if (itemSound.OnlyPlayInSameSub && item.Submarine != null && Character.Controlled != null)
            {
                if (Character.Controlled.Submarine == null || !Character.Controlled.Submarine.IsEntityFoundOnThisSub(item, includingConnectedSubs: true)) { return; }
            }

            if (itemSound.Loop)
            {
                if (loopingSoundChannel != null && loopingSoundChannel.Sound != itemSound.RoundSound.Sound)
                {
                    loopingSoundChannel.FadeOutAndDispose(); loopingSoundChannel = null;
                }
                if (loopingSoundChannel == null || !loopingSoundChannel.IsPlaying)
                {
                    float volume = GetSoundVolume(itemSound);
                    if (volume <= 0.0001f) { return; }
                    loopingSound = itemSound;
                    loopingSoundChannel = SoundPlayer.PlaySound(loopingSound.RoundSound, position, volume: 0.01f, hullGuess: item.CurrentHull);
                    if (loopingSoundChannel != null)
                    {
                        loopingSoundChannel.Looping = true;
                        loopingSoundChannel.Near = loopingSound.Range * 0.4f;
                        loopingSoundChannel.Far = loopingSound.Range;
                    }
                    if (loopingSound.RoundSound.Stream)
                    {
                        loopingSoundChannel.StreamSeekPos = loopingSound.RoundSound.LastStreamSeekPos;
                    }
                }
            }
            else
            {
                float volume = GetSoundVolume(itemSound);
                if (volume <= 0.0001f) { return; }
                var channel = SoundPlayer.PlaySound(itemSound.RoundSound, position, volume, hullGuess: item.CurrentHull);
                if (channel != null) { playingOneshotSoundChannels.Add(channel); }
            }
        }

        public void StopLoopingSound()
        {
            if (loopingSound == null) { return; }
            if (loopingSoundChannel != null)
            {
                loopingSoundChannel.FadeOutAndDispose();
                loopingSoundChannel = null;
                loopingSound = null;
            }
        }

        public void StopSounds(ActionType type)
        {
            if (loopingSound == null || loopingSound.Type != type) { return; }
            StopLoopingSound();
        }

        private float GetSoundVolume(ItemSound sound)
        {
            if (sound == null) { return 0.0f; }
            if (sound.VolumeProperty == "") { return sound.VolumeMultiplier; }

            SerializableProperty property = null;
            ISerializableEntity targetEntity = null;
            if (SerializableProperties.TryGetValue(sound.VolumeProperty, out property))
            {
                targetEntity = this;
            }
            else if (Item.SerializableProperties.TryGetValue(sound.VolumeProperty, out property))
            {
                targetEntity = Item;
            }

            if (property != null)
            {
                float newVolume;
                try
                {
                    newVolume = property.GetFloatValue(targetEntity);
                }
                catch
                {
                    return 0.0f;
                }
                newVolume = Math.Min(newVolume * sound.VolumeMultiplier, 1.0f);

                if (!MathUtils.IsValid(newVolume))
                {
                    DebugConsole.Log("Invalid sound volume (item " + item.Name + ", " + GetType().ToString() + "): " + newVolume);
                    GameAnalyticsManager.AddErrorEventOnce(
                        "ItemComponent.PlaySound:" + item.Name + GetType().ToString(),
                        GameAnalyticsManager.ErrorSeverity.Error,
                        "Invalid sound volume (item " + item.Name + ", " + GetType().ToString() + "): " + newVolume);
                    return 0.0f;
                }

                return MathHelper.Clamp(newVolume, 0.0f, 1.0f);
            }

            return 0.0f;
        }
        
        public virtual bool ShouldDrawHUD(Character character)
        {
            return true;
        }

        public ItemComponent GetLinkUIToComponent()
        {
            if (string.IsNullOrEmpty(LinkUIToComponent))
            {
                return null;
            }
            foreach (ItemComponent component in item.Components)
            {
                if (component.name.Equals(LinkUIToComponent, StringComparison.OrdinalIgnoreCase))
                {
                    linkToUIComponent = component;
                }
            }
            if (linkToUIComponent == null)
            {
                DebugConsole.ThrowError("Failed to link the component \"" + Name + "\" to \"" + LinkUIToComponent + "\" in the item \"" + item.Name + "\" - component with a matching name not found.");
            }
            return linkToUIComponent;
        }

        public virtual void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            if (HUDOverlay != null)
            {
                Vector2 screenSize = new Vector2(GameMain.GraphicsWidth, GameMain.GraphicsHeight);
                if (HUDOverlay is SpriteSheet spriteSheet)
                {
                    spriteSheet.Draw(spriteBatch,
                        spriteIndex: spriteSheet.GetAnimatedSpriteIndex(HUDOverlayAnimSpeed),
                        pos: screenSize / 2, color: Color.White, origin: HUDOverlay.Origin, rotate: 0, scale: screenSize / spriteSheet.FrameSize.ToVector2());
                }
                else
                {
                    HUDOverlay.Draw(spriteBatch,
                        pos: screenSize / 2, color: Color.White, origin: HUDOverlay.Origin, rotate: 0, scale: screenSize / HUDOverlay.size);
                }
            }
        }

        public virtual void AddToGUIUpdateList(int order = 0)
        {
            GuiFrame?.AddToGUIUpdateList(order: order);
        }

        public void UpdateHUD(Character character, float deltaTime, Camera cam)
        {
            UpdateHUDComponentSpecific(character, deltaTime, cam);
            if (guiFrameUpdatePending && !PlayerInput.PrimaryMouseButtonHeld())
            {
                //send a guiframe position update once the player stops dragging the frame
                guiFrameUpdatePending = false;
                if (SerializableProperties.TryGetValue(nameof(GuiFrameOffset).ToIdentifier(), out var property))
                {
                    GameMain.Client?.CreateEntityEvent(Item, new Item.ChangePropertyEventData(property, this));
                }
            }
        }

        public virtual void UpdateHUDComponentSpecific(Character character, float deltaTime, Camera cam) { }

        public virtual void UpdateEditing(float deltaTime) { }

        public virtual void CreateEditingHUD(SerializableEntityEditor editor)
        {
        }

        private bool LoadElemProjSpecific(ContentXElement subElement)
        {
            switch (subElement.Name.ToString().ToLowerInvariant())
            {
                case "guiframe":
                    if (subElement.GetAttribute("rect") != null)
                    {
                        DebugConsole.ThrowError($"Error in item config \"{item.ConfigFilePath}\" - GUIFrame defined as rect, use RectTransform instead.",
                            contentPackage: subElement.ContentPackage);
                        break;
                    }
                    GuiFrameSource = subElement;
                    ReloadGuiFrame();
                    break;
                case "hudoverlayanimated":
                    HUDOverlay = new SpriteSheet(subElement);
                    HUDOverlayAnimSpeed = subElement.GetAttributeFloat("animspeed", 1.0f);
                    break;
                case "hudoverlay":
                    HUDOverlay = new Sprite(subElement);
                    break;
                case "alternativelayout":
                    AlternativeLayout = GUILayoutSettings.Load(subElement);
                    break;
                case "itemsound":
                case "sound":
                    //TODO: this validation stuff should probably go somewhere else
                    string filePath = subElement.GetAttributeStringUnrestricted("file", "");

                    if (filePath.IsNullOrEmpty()) { filePath = subElement.GetAttributeStringUnrestricted("sound", ""); }

                    if (filePath.IsNullOrEmpty())
                    {
                        DebugConsole.ThrowError(
                            $"Error when instantiating item \"{item.Name}\" - sound with no file path set",
                            contentPackage: subElement.ContentPackage);
                        break;
                    }

                    ActionType type;
                    string typeStr = subElement.GetAttributeString("type", "");
                    try
                    {
                        type = (ActionType)Enum.Parse(typeof(ActionType), typeStr, true);
                    }
                    catch (Exception e)
                    {
                        DebugConsole.ThrowError($"Invalid sound type \"{typeStr}\" in item \"{item.Prefab.Identifier}\"!", e,
                            contentPackage: subElement.ContentPackage);
                        break;
                    }
                    
                    RoundSound sound = RoundSound.Load(subElement);
                    if (sound == null) { break; }
                    ItemSound itemSound = new ItemSound(sound, type, 
                        subElement.GetAttributeBool("loop", false),
                        subElement.GetAttributeBool("onlyinsamesub", false))
                    {
                        VolumeProperty = subElement.GetAttributeIdentifier("volumeproperty", "")
                    };

                    if (soundSelectionModes == null)
                    {
                        soundSelectionModes = new Dictionary<ActionType, SoundSelectionMode>();
                    }
                    if (!soundSelectionModes.ContainsKey(type) || soundSelectionModes[type] == SoundSelectionMode.Random)
                    {
                        soundSelectionModes[type] = subElement.GetAttributeEnum("selectionmode", SoundSelectionMode.Random);
                    }

                    if (!sounds.TryGetValue(itemSound.Type, out List<ItemSound> soundList))
                    {
                        soundList = new List<ItemSound>();
                        sounds.Add(itemSound.Type, soundList);
                        hasSoundsOfType[(int)itemSound.Type] = true;
                    }

                    soundList.Add(itemSound);
                    break;
                default:
                    return false; //unknown element
            }
            return true; //element processed
        }

        private XElement GuiFrameSource;

        protected void ReleaseGuiFrame()
        {
            if (GuiFrame != null)
            {
                GuiFrame.RectTransform.Parent = null;
            }
        }

        protected void ReloadGuiFrame()
        {
            if (GuiFrame != null)
            {
                ReleaseGuiFrame();
            }
            Color? color = null;
            if (GuiFrameSource.Attribute("color") != null)
            {
                color = GuiFrameSource.GetAttributeColor("color", Color.White);
            }
            string style = GuiFrameSource.Attribute("style") == null ? null : GuiFrameSource.GetAttributeString("style", "");
            GuiFrame = new GUIFrame(RectTransform.Load(GuiFrameSource, GUI.Canvas, Anchor.Center), style, color);
            GuiFrame.RectTransform.ScreenSpaceOffset = GuiFrameOffset;

            TryCreateDragHandle();

            DefaultLayout = GUILayoutSettings.Load(GuiFrameSource);
            if (GuiFrame != null)
            {
                GuiFrame.RectTransform.ParentChanged += OnGUIParentChanged;
            }
            GameMain.Instance.ResolutionChanged += OnResolutionChangedPrivate;
        }

        protected void TryCreateDragHandle()
        {
            if (GuiFrame != null && GuiFrameSource.GetAttributeBool("draggable", true))
            {
                bool hideDragIcons = GuiFrameSource.GetAttributeBool("hidedragicons", false);

                guiFrameDragHandle = new GUIDragHandle(new RectTransform(Vector2.One, GuiFrame.RectTransform, Anchor.Center),
                    GuiFrame.RectTransform, style: null)
                {
                    Enabled = !LockGuiFramePosition,
                    DragArea = HUDLayoutSettings.ItemHUDArea
                };

                int iconHeight = GUIStyle.ItemFrameMargin.Y / 4;
                var dragIcon = new GUIImage(new RectTransform(new Point(GuiFrame.Rect.Width, iconHeight), guiFrameDragHandle.RectTransform, Anchor.TopCenter) { AbsoluteOffset = new Point(0, iconHeight / 2) },
                    style: "GUIDragIndicatorHorizontal");
                dragIcon.RectTransform.MinSize = new Point(0, iconHeight);

                guiFrameDragHandle.ValidatePosition = (RectTransform rectT) =>
                {
                    var activeHuds = Character.Controlled?.SelectedItem?.ActiveHUDs ?? item.ActiveHUDs;
                    foreach (ItemComponent ic in activeHuds)
                    {
                        if (ic == this || ic.GuiFrame == null || !ic.CanBeSelected) { continue; }
                        if (ic.GuiFrame.Rect.Width > GameMain.GraphicsWidth * 0.9f && ic.GuiFrame.Rect.Height > GameMain.GraphicsHeight * 0.9f) 
                        { 
                            //a full-screen GUIFrame (or at least close to one) - this component is doing something weird,
                            //an ItemContainer with no GUIFrame definition that positions itself in some other GUIFrame, some kind of an overlay?
                            // -> allow intersecting
                            continue; 
                        }
                        if (dragIcon.Rect.Intersects(ic.GuiFrame.Rect))
                        {
                            GuiFrame.ImmediateFlash();
                            return false;
                        }
                    }
                    foreach (ItemComponent ic in activeHuds)
                    {
                        //refresh slots to ensure they're rendered at the correct position
                        (ic as ItemContainer)?.Inventory.CreateSlots();
                    }
                    GuiFrameOffset = GuiFrame.RectTransform.ScreenSpaceOffset;
                    guiFrameUpdatePending = true;
                    return true;
                };

                int buttonHeight = (int)(GUIStyle.ItemFrameMargin.Y * 0.4f);
                var settingsIcon = new GUIButton(new RectTransform(new Point(buttonHeight), guiFrameDragHandle.RectTransform, Anchor.TopLeft) { AbsoluteOffset = new Point(buttonHeight / 4), MinSize = new Point(buttonHeight) },
                    style: "GUIButtonSettings")
                {
                    OnClicked = (btn, userdata) =>
                    {
                        GUIContextMenu.CreateContextMenu(
                            new ContextMenuOption("item.resetuiposition", isEnabled: true, onSelected: () =>
                            {
                                foreach (var ic in item.Components)
                                {
                                    if (ic.GuiFrame != null && ic.GuiFrameOffset != Point.Zero)
                                    {
                                        ic.GuiFrameOffset = Point.Zero;
                                        ic.guiFrameUpdatePending = true;
                                    }
                                }
                                if (Character.Controlled?.SelectedItem != null && item != Character.Controlled.SelectedItem)
                                {
                                    Character.Controlled.SelectedItem.ForceHUDLayoutUpdate(ignoreLocking: true);
                                }
                                else
                                {
                                    item.ForceHUDLayoutUpdate(ignoreLocking: true);
                                }
                            }),
                            new ContextMenuOption(TextManager.Get(LockGuiFramePosition ? "item.unlockuiposition" : "item.lockuiposition"), isEnabled: true, onSelected: () =>
                            {
                                //ensure the offset is set to where the frame is now
                                //(it may have been repositioned by the overlap prevention logic, which doesn't set this offset)
                                GuiFrameOffset = GuiFrame.RectTransform.ScreenSpaceOffset;
                                LockGuiFramePosition = !LockGuiFramePosition;
                                guiFrameDragHandle.Enabled = !LockGuiFramePosition;
                                if (SerializableProperties.TryGetValue(nameof(LockGuiFramePosition).ToIdentifier(), out var property))
                                {
                                    GameMain.Client?.CreateEntityEvent(Item, new Item.ChangePropertyEventData(property, this));
                                }
                            }));
                        return true;
                    }
                };

                if (hideDragIcons)
                {
                    dragIcon.Visible = false;
                    settingsIcon.Visible = false;
                }
            }
        }

        /// <summary>
        /// Overload this method and implement. The method is automatically called when the resolution changes.
        /// </summary>
        protected virtual void CreateGUI() { }

        /// <summary>
        /// Starts a coroutine that will read the correct state of the component from the NetBuffer when correctionTimer reaches zero.
        /// Useful in cases where we a client is constantly adjusting some value, and we don't want state updates from the server to interfere with it 
        /// (e.g. setting the value back to what a client just set it to, when the client has already modified the value further).
        /// </summary>
        protected void StartDelayedCorrection(IReadMessage buffer, float sendingTime, bool waitForMidRoundSync = false)
        {
            if (delayedCorrectionCoroutine != null) { CoroutineManager.StopCoroutines(delayedCorrectionCoroutine); }

            delayedCorrectionCoroutine = CoroutineManager.StartCoroutine(DoDelayedCorrection(buffer, sendingTime, waitForMidRoundSync));
        }

        private IEnumerable<CoroutineStatus> DoDelayedCorrection(IReadMessage buffer, float sendingTime, bool waitForMidRoundSync)
        {
            while (GameMain.Client != null && 
                (correctionTimer > 0.0f || (waitForMidRoundSync && GameMain.Client.MidRoundSyncing)))
            {
                correctionTimer -= CoroutineManager.DeltaTime;
                yield return CoroutineStatus.Running;
            }

            if (item.Removed || GameMain.Client == null)
            {
                yield return CoroutineStatus.Success;
            }

            ((IServerSerializable)this).ClientEventRead(buffer, sendingTime);

            correctionTimer = 0.0f;
            delayedCorrectionCoroutine = null;

            yield return CoroutineStatus.Success;
        }

        /// <summary>
        /// Launches when the parent of the GuiFrame is changed.
        /// </summary>
        protected void OnGUIParentChanged(RectTransform newParent)
        {
            if (newParent == null)
            {
                // Make sure to unregister. It doesn't matter if we haven't ever registered to the event.
                GameMain.Instance.ResolutionChanged -= OnResolutionChangedPrivate;
            }
        }

        protected virtual void OnResolutionChanged() { }

        private void OnResolutionChangedPrivate()
        {
            if (RecreateGUIOnResolutionChange)
            {
                ReloadGuiFrame();
                CreateGUI();
            }
            OnResolutionChanged();
            item.ForceHUDLayoutUpdate(ignoreLocking: true);
            if (GuiFrame != null && GuiFrame.GetChild<GUIDragHandle>() is GUIDragHandle dragHandle)
            {
                dragHandle.DragArea = HUDLayoutSettings.ItemHUDArea;
            }
        }

        public virtual void OnPlayerSkillsChanged() { }

        public virtual void AddTooltipInfo(ref LocalizedString name, ref LocalizedString description) { }
    }
}
