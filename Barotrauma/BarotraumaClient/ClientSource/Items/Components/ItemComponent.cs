using Barotrauma.Networking;
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

        public ItemSound(RoundSound sound, ActionType type, bool loop = false)
        {
            this.RoundSound = sound;
            this.Type = type;
            this.Loop = loop;
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

        protected float correctionTimer;

        public float IsActiveTimer;

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

        [Serialize(false, IsPropertySaveable.No)]
        public bool AllowUIOverlap
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
        private List<SoundChannel> playingOneshotSoundChannels = new List<SoundChannel>();
        public ItemComponent ReplacedBy;

        public ItemComponent GetReplacementOrThis()
        {
            return ReplacedBy?.GetReplacementOrThis() ?? this;
        }

        public void UpdateSounds()
        {
            if (!isActive || item.Condition <= 0.0f)
            {
                StopSounds(ActionType.OnActive);
            }

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
                    loopingSoundChannel.Looping = true;
                    //TODO: tweak
                    loopingSoundChannel.Near = loopingSound.Range * 0.4f;
                    loopingSoundChannel.Far = loopingSound.Range;
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
                    index = Math.Clamp(ManuallySelectedSound, 0, matchingSounds.Count);
                }
                else
                {
                    index = Rand.Int(matchingSounds.Count);
                }

                PlaySound(matchingSounds[index], item.WorldPosition);
            }
        }
        private void PlaySound(ItemSound itemSound, Vector2 position)
        {
            if (Vector2.DistanceSquared(new Vector2(GameMain.SoundManager.ListenerPosition.X, GameMain.SoundManager.ListenerPosition.Y), position) > itemSound.Range * itemSound.Range)
            {
                return;
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
                    loopingSoundChannel = loopingSound.RoundSound.Sound.Play(
                        new Vector3(position.X, position.Y, 0.0f), 
                        0.01f,
                        muffle: SoundPlayer.ShouldMuffleSound(Character.Controlled, position, loopingSound.Range, Character.Controlled?.CurrentHull));
                    loopingSoundChannel.Looping = true;
                    //TODO: tweak
                    loopingSoundChannel.Near = loopingSound.Range * 0.4f;
                    loopingSoundChannel.Far = loopingSound.Range;
                }
            }
            else
            {
                float volume = GetSoundVolume(itemSound);
                if (volume <= 0.0001f) { return; }
                var channel = SoundPlayer.PlaySound(itemSound.RoundSound.Sound, position, volume, itemSound.Range, itemSound.RoundSound.GetRandomFrequencyMultiplier(), item.CurrentHull, ignoreMuffling: itemSound.RoundSound.IgnoreMuffling);
                if (channel != null) { playingOneshotSoundChannels.Add(channel); }
            }
        }

        public void StopSounds(ActionType type)
        {
            if (loopingSound == null) { return; }

            if (loopingSound.Type != type) { return; }

            if (loopingSoundChannel != null)
            {
                loopingSoundChannel.FadeOutAndDispose();
                loopingSoundChannel = null;
                loopingSound = null;
            }
        }

        private float GetSoundVolume(ItemSound sound)
        {
            if (sound == null) { return 0.0f; }
            if (sound.VolumeProperty == "") { return sound.VolumeMultiplier; }

            if (SerializableProperties.TryGetValue(sound.VolumeProperty, out SerializableProperty property))
            {
                float newVolume;
                try
                {
                    newVolume = property.GetFloatValue(this);
                }
                catch
                {
                    return 0.0f;
                }
                newVolume *= sound.VolumeMultiplier;

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

        public virtual void DrawHUD(SpriteBatch spriteBatch, Character character) { }

        public virtual void AddToGUIUpdateList(int order = 0)
        {
            GuiFrame?.AddToGUIUpdateList(order: order);
        }

        public virtual void UpdateHUD(Character character, float deltaTime, Camera cam) { }

        public virtual void UpdateEditing(float deltaTime) { }

        public virtual void CreateEditingHUD(SerializableEntityEditor editor)
        {
        }

        private bool LoadElemProjSpecific(ContentXElement subElement)
        {
            switch (subElement.Name.ToString().ToLowerInvariant())
            {
                case "guiframe":
                    if (subElement.Attribute("rect") != null)
                    {
                        DebugConsole.ThrowError($"Error in item config \"{item.ConfigFilePath}\" - GUIFrame defined as rect, use RectTransform instead.");
                        break;
                    }
                    GuiFrameSource = subElement;
                    ReloadGuiFrame();
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
                            $"Error when instantiating item \"{item.Name}\" - sound with no file path set");
                        break;
                    }

                    ActionType type;
                    try
                    {
                        type = (ActionType)Enum.Parse(typeof(ActionType), subElement.GetAttributeString("type", ""), true);
                    }
                    catch (Exception e)
                    {
                        DebugConsole.ThrowError("Invalid sound type in " + subElement + "!", e);
                        break;
                    }
                    
                    RoundSound sound = RoundSound.Load(subElement);
                    if (sound == null) { break; }
                    ItemSound itemSound = new ItemSound(sound, type, subElement.GetAttributeBool("loop", false))
                    {
                        VolumeProperty = subElement.GetAttributeIdentifier("volumeproperty", "")
                    };

                    if (soundSelectionModes == null) soundSelectionModes = new Dictionary<ActionType, SoundSelectionMode>();
                    if (!soundSelectionModes.ContainsKey(type) || soundSelectionModes[type] == SoundSelectionMode.Random)
                    {
                        SoundSelectionMode selectionMode = SoundSelectionMode.Random;
                        Enum.TryParse(subElement.GetAttributeString("selectionmode", "Random"), out selectionMode);
                        soundSelectionModes[type] = selectionMode;
                    }

                    List<ItemSound> soundList = null;
                    if (!sounds.TryGetValue(itemSound.Type, out soundList))
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
            DefaultLayout = GUILayoutSettings.Load(GuiFrameSource);
            if (GuiFrame != null)
            {
                GuiFrame.RectTransform.ParentChanged += OnGUIParentChanged;
            }
            GameMain.Instance.ResolutionChanged += OnResolutionChanged;
        }

        /// <summary>
        /// Overload this method and implement. The method is automatically called when the resolution changes.
        /// </summary>
        protected virtual void CreateGUI() { }

        //Starts a coroutine that will read the correct state of the component from the NetBuffer when correctionTimer reaches zero.
        protected void StartDelayedCorrection(ServerNetObject type, IReadMessage buffer, float sendingTime, bool waitForMidRoundSync = false)
        {
            if (delayedCorrectionCoroutine != null) CoroutineManager.StopCoroutines(delayedCorrectionCoroutine);

            delayedCorrectionCoroutine = CoroutineManager.StartCoroutine(DoDelayedCorrection(type, buffer, sendingTime, waitForMidRoundSync));
        }

        private IEnumerable<CoroutineStatus> DoDelayedCorrection(ServerNetObject type, IReadMessage buffer, float sendingTime, bool waitForMidRoundSync)
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

            ((IServerSerializable)this).ClientRead(type, buffer, sendingTime);

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
        }

        public virtual void AddTooltipInfo(ref LocalizedString name, ref LocalizedString description) { }
    }
}
