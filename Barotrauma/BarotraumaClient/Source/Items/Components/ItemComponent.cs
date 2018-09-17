using Barotrauma.Networking;
using Barotrauma.Sounds;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class ItemSound
    {
        public readonly Sound Sound;
        public readonly ActionType Type;

        public string VolumeProperty;
        public float VolumeMultiplier;
        
        public readonly float Range;

        public readonly bool Loop;

        public ItemSound(Sound sound, ActionType type, float range, bool loop = false)
        {
            this.Sound = sound;
            this.Type = type;
            this.Range = range;

            this.Loop = loop;
        }
    }

    partial class ItemComponent : ISerializableEntity
    {
        private Dictionary<ActionType, List<ItemSound>> sounds;
        private Dictionary<ActionType, SoundSelectionMode> soundSelectionModes;

        protected GUIFrame guiFrame;

        enum SoundSelectionMode
        {
            Random, 
            CharacterSpecific,
            ItemSpecific
        }

        public GUIFrame GuiFrame
        {
            get
            {
                /*if (guiFrame == null)
                {
                    DebugConsole.ThrowError("Error: the component " + name + " in " + item.Name + " doesn't have a GuiFrame component");
                    guiFrame = new GUIFrame(new Rectangle(0, 0, 100, 100), Color.Black);
                }*/
                return guiFrame;
            }
        }

        [Serialize(false, false)]
        public bool AllowUIOverlap
        {
            get;
            set;
        }

        [Serialize(-1, false)]
        public int LinkUIToComponent
        {
            get;
            set;
        }

        [Serialize(0, false)]
        public int HudPriority
        {
            get;
            private set;
        }
        
        private ItemSound loopingSound;
        private SoundChannel loopingSoundChannel;
        public void PlaySound(ActionType type, Vector2 position, Character user = null)
        {
            if (loopingSound != null)
            {
                if (Vector3.DistanceSquared(GameMain.SoundManager.ListenerPosition, new Vector3(position.X, position.Y, 0.0f)) > loopingSound.Range * loopingSound.Range)
                {
                    if (loopingSoundChannel != null)
                    {
                        loopingSoundChannel.Dispose(); loopingSoundChannel = null;
                    }
                    return;
                }

                if (loopingSoundChannel != null && loopingSoundChannel.Sound != loopingSound.Sound)
                {
                    loopingSoundChannel.Dispose(); loopingSoundChannel = null;
                }
                if (loopingSoundChannel == null || !loopingSoundChannel.IsPlaying)
                {
                    loopingSoundChannel = loopingSound.Sound.Play(new Vector3(position.X, position.Y, 0.0f), GetSoundVolume(loopingSound));
                    loopingSoundChannel.Looping = true;
                    //TODO: tweak
                    loopingSoundChannel.Near = loopingSound.Range * 0.4f;
                    loopingSoundChannel.Far = loopingSound.Range;
                }
                if (loopingSoundChannel != null)
                {
                    loopingSoundChannel.Gain = GetSoundVolume(loopingSound);
                    loopingSoundChannel.Position = new Vector3(position.X, position.Y, 0.0f);
                }
                return;
            }

            if (!sounds.TryGetValue(type, out List<ItemSound> matchingSounds)) return;

            ItemSound itemSound = null;
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
                else
                {
                    index = Rand.Int(matchingSounds.Count);
                }
                    
                itemSound = matchingSounds[index];
            }

            if (itemSound == null) return;

            if (Vector3.DistanceSquared(GameMain.SoundManager.ListenerPosition, new Vector3(position.X, position.Y, 0.0f)) > itemSound.Range * itemSound.Range)
            {
                return;
            }

            if (itemSound.Loop)
            {
                loopingSound = itemSound;
                if (loopingSoundChannel != null && loopingSoundChannel.Sound != loopingSound.Sound)
                {
                    loopingSoundChannel.Dispose(); loopingSoundChannel = null;
                }
                if (loopingSoundChannel == null || !loopingSoundChannel.IsPlaying)
                {
                    loopingSoundChannel = loopingSound.Sound.Play(new Vector3(position.X, position.Y, 0.0f), GetSoundVolume(loopingSound));
                    loopingSoundChannel.Looping = true;
                    //TODO: tweak
                    loopingSoundChannel.Near = loopingSound.Range * 0.4f;
                    loopingSoundChannel.Far = loopingSound.Range;
                }
            }
            else
            {
                float volume = GetSoundVolume(itemSound);
                if (volume == 0.0f) return;
                SoundPlayer.PlaySound(itemSound.Sound, volume, itemSound.Range, position, item.CurrentHull);
            }
        }

        public void StopSounds(ActionType type)
        {
            if (loopingSound == null) return;

            if (loopingSound.Type != type) return;

            if (loopingSoundChannel != null)
            {
                loopingSoundChannel.Dispose();
                loopingSoundChannel = null;
                loopingSound = null;
            }
        }

        private float GetSoundVolume(ItemSound sound)
        {
            if (sound == null) return 0.0f;
            if (sound.VolumeProperty == "") return 1.0f;

            if (properties.TryGetValue(sound.VolumeProperty.ToLowerInvariant(), out SerializableProperty property))
            {
                float newVolume = 0.0f;
                try
                {
                    newVolume = (float)property.GetValue();
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
                        GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
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

        public virtual void DrawHUD(SpriteBatch spriteBatch, Character character) { }

        public virtual void AddToGUIUpdateList() { }

        public virtual void UpdateHUD(Character character, float deltaTime, Camera cam) { }

        private bool LoadElemProjSpecific(XElement subElement)
        {
            switch (subElement.Name.ToString().ToLowerInvariant())
            {
                case "guiframe":
                    if (subElement.Attribute("rect") !=null)
                    {
                        DebugConsole.ThrowError("Error in item config \"" + item.ConfigFile + "\" - GUIFrame defined as rect, use RectTransform instead.");
                        break;
                    }

                    Color? color = null;
                    if (subElement.Attribute("color") != null) color = subElement.GetAttributeColor("color", Color.White);
                    string style = subElement.Attribute("style") == null ?
                        null : subElement.GetAttributeString("style", "");

                    guiFrame = new GUIFrame(RectTransform.Load(subElement, GUI.Canvas), style, color);
                    break;
                case "sound":
                    string filePath = subElement.GetAttributeString("file", "");

                    if (filePath == "") filePath = subElement.GetAttributeString("sound", "");

                    if (filePath == "")
                    {
                        DebugConsole.ThrowError("Error when instantiating item \"" + item.Name + "\" - sound with no file path set");
                        break;
                    }

                    if (!filePath.Contains("/") && !filePath.Contains("\\") && !filePath.Contains(Path.DirectorySeparatorChar))
                    {
                        filePath = Path.Combine(Path.GetDirectoryName(item.Prefab.ConfigFile), filePath);
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
                    
                    Sound sound = Submarine.LoadRoundSound(filePath);
                    float range = subElement.GetAttributeFloat("range", 800.0f);
                    bool loop = subElement.GetAttributeBool("loop", false);
                    ItemSound itemSound = new ItemSound(sound, type, range, loop);
                    itemSound.VolumeProperty = subElement.GetAttributeString("volume", "");
                    itemSound.VolumeMultiplier = subElement.GetAttributeFloat("volumemultiplier", 1.0f);

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
                    }

                    soundList.Add(itemSound);
                    break;
                default:
                    return false; //unknown element
            }
            return true; //element processed
        }

        //Starts a coroutine that will read the correct state of the component from the NetBuffer when correctionTimer reaches zero.
        protected void StartDelayedCorrection(ServerNetObject type, NetBuffer buffer, float sendingTime, bool waitForMidRoundSync = false)
        {
            if (delayedCorrectionCoroutine != null) CoroutineManager.StopCoroutines(delayedCorrectionCoroutine);

            delayedCorrectionCoroutine = CoroutineManager.StartCoroutine(DoDelayedCorrection(type, buffer, sendingTime, waitForMidRoundSync));
        }

        private IEnumerable<object> DoDelayedCorrection(ServerNetObject type, NetBuffer buffer, float sendingTime, bool waitForMidRoundSync)
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
    }
}
