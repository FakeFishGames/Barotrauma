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

        private GUIFrame guiFrame;

        protected GUIFrame GuiFrame
        {
            get
            {
                if (guiFrame == null)
                {
                    DebugConsole.ThrowError("Error: the component " + name + " in " + item.Name + " doesn't have a GuiFrame component");
                    guiFrame = new GUIFrame(new Rectangle(0, 0, 100, 100), Color.Black);
                }
                return guiFrame;
            }
        }

        private ItemSound loopingSound;
        private SoundChannel loopingSoundChannel;
        public void PlaySound(ActionType type, Vector2 position)
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

            List<ItemSound> matchingSounds;
            if (!sounds.TryGetValue(type, out matchingSounds)) return;

            ItemSound itemSound = null;
            if (loopingSoundChannel==null || !loopingSoundChannel.IsPlaying)
            {
                int index = Rand.Int(matchingSounds.Count);
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
                SoundChannel tempChannel = itemSound.Sound.Play(new Vector3(position.X,position.Y,0.0f), volume);
                tempChannel.Near = itemSound.Range * 0.4f;
                tempChannel.Far = itemSound.Range;
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

            SerializableProperty property = null;
            if (properties.TryGetValue(sound.VolumeProperty.ToLowerInvariant(), out property))
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

                return MathHelper.Clamp(newVolume, 0.0f, 1.0f);
            }

            return 0.0f;
        }
        
        public virtual void DrawHUD(SpriteBatch spriteBatch, Character character) { }

        public virtual void AddToGUIUpdateList() { }

        public virtual void UpdateHUD(Character character) { }

        private bool LoadElemProjSpecific(XElement subElement)
        {
            switch (subElement.Name.ToString().ToLowerInvariant())
            {
                case "guiframe":
                    string rectStr = subElement.GetAttributeString("rect", "0.0,0.0,0.5,0.5");

                    string[] components = rectStr.Split(',');
                    if (components.Length < 4) break;

                    Vector4 rect = subElement.GetAttributeVector4("rect", Vector4.One);
                    if (components[0].Contains(".")) rect.X *= GameMain.GraphicsWidth;
                    if (components[1].Contains(".")) rect.Y *= GameMain.GraphicsHeight;
                    if (components[2].Contains(".")) rect.Z *= GameMain.GraphicsWidth;
                    if (components[3].Contains(".")) rect.W *= GameMain.GraphicsHeight;

                    string style = subElement.GetAttributeString("style", "");

                    Vector4 color = subElement.GetAttributeVector4("color", Vector4.One);

                    Alignment alignment = Alignment.Center;
                    try
                    {
                        alignment = (Alignment)Enum.Parse(typeof(Alignment),
                            subElement.GetAttributeString("alignment", "Center"), true);
                    }
                    catch
                    {
                        DebugConsole.ThrowError("Error in " + subElement.Parent + "! \"" + subElement.Parent.Attribute("type").Value + "\" is not a valid alignment");
                    }

                    guiFrame = new GUIFrame(
                        new Rectangle((int)rect.X, (int)rect.Y, (int)rect.Z, (int)rect.W),
                        new Color(color.X, color.Y, color.Z) * color.W,
                        alignment, style);

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
        protected void StartDelayedCorrection(ServerNetObject type, NetBuffer buffer, float sendingTime)
        {
            if (delayedCorrectionCoroutine != null) CoroutineManager.StopCoroutines(delayedCorrectionCoroutine);

            delayedCorrectionCoroutine = CoroutineManager.StartCoroutine(DoDelayedCorrection(type, buffer, sendingTime));
        }

        private IEnumerable<object> DoDelayedCorrection(ServerNetObject type, NetBuffer buffer, float sendingTime)
        {
            while (correctionTimer > 0.0f)
            {
                correctionTimer -= CoroutineManager.DeltaTime;
                yield return CoroutineStatus.Running;
            }

            ((IServerSerializable)this).ClientRead(type, buffer, sendingTime);

            correctionTimer = 0.0f;
            delayedCorrectionCoroutine = null;

            yield return CoroutineStatus.Success;
        }
    }
}
