using Barotrauma.Networking;
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

    partial class ItemComponent : IPropertyObject
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
        private int loopingSoundIndex;
        public void PlaySound(ActionType type, Vector2 position)
        {
            if (loopingSound != null)
            {
                loopingSoundIndex = loopingSound.Sound.Loop(loopingSoundIndex, GetSoundVolume(loopingSound), position, loopingSound.Range);
                return;
            }

            List<ItemSound> matchingSounds;
            if (!sounds.TryGetValue(type, out matchingSounds)) return;

            ItemSound itemSound = null;
            if (!Sounds.SoundManager.IsPlaying(loopingSoundIndex))
            {
                int index = Rand.Int(matchingSounds.Count);
                itemSound = matchingSounds[index];
            }

            if (itemSound == null) return;

            if (itemSound.Loop)
            {
                loopingSound = itemSound;

                loopingSoundIndex = loopingSound.Sound.Loop(loopingSoundIndex, GetSoundVolume(loopingSound), position, loopingSound.Range);
            }
            else
            {
                float volume = GetSoundVolume(itemSound);
                if (volume == 0.0f) return;
                itemSound.Sound.Play(volume, itemSound.Range, position);
            }
        }

        public void StopSounds(ActionType type)
        {
            if (loopingSoundIndex <= 0) return;

            if (loopingSound == null) return;

            if (loopingSound.Type != type) return;

            if (Sounds.SoundManager.IsPlaying(loopingSoundIndex))
            {
                Sounds.SoundManager.Stop(loopingSoundIndex);
                loopingSound = null;
                loopingSoundIndex = -1;
            }
        }

        private float GetSoundVolume(ItemSound sound)
        {
            if (sound == null) return 0.0f;
            if (sound.VolumeProperty == "") return 1.0f;

            ObjectProperty op = null;
            if (properties.TryGetValue(sound.VolumeProperty.ToLowerInvariant(), out op))
            {
                float newVolume = 0.0f;
                try
                {
                    newVolume = (float)op.GetValue();
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

        //public virtual void Draw(SpriteBatch spriteBatch, bool editing = false) 
        //{
        //    item.drawableComponents = Array.FindAll(item.drawableComponents, i => i != this);
        //}

        public virtual void DrawHUD(SpriteBatch spriteBatch, Character character) { }

        public virtual void AddToGUIUpdateList() { }

        public virtual void UpdateHUD(Character character) { }

        private bool LoadElemProjSpecific(XElement subElement)
        {
            switch (subElement.Name.ToString().ToLowerInvariant())
            {
                case "guiframe":
                    string rectStr = ToolBox.GetAttributeString(subElement, "rect", "0.0,0.0,0.5,0.5");

                    string[] components = rectStr.Split(',');
                    if (components.Length < 4) break;

                    Vector4 rect = ToolBox.GetAttributeVector4(subElement, "rect", Vector4.One);
                    if (components[0].Contains(".")) rect.X *= GameMain.GraphicsWidth;
                    if (components[1].Contains(".")) rect.Y *= GameMain.GraphicsHeight;
                    if (components[2].Contains(".")) rect.Z *= GameMain.GraphicsWidth;
                    if (components[3].Contains(".")) rect.W *= GameMain.GraphicsHeight;

                    string style = ToolBox.GetAttributeString(subElement, "style", "");

                    Vector4 color = ToolBox.GetAttributeVector4(subElement, "color", Vector4.One);

                    Alignment alignment = Alignment.Center;
                    try
                    {
                        alignment = (Alignment)Enum.Parse(typeof(Alignment),
                            ToolBox.GetAttributeString(subElement, "alignment", "Center"), true);
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
                    string filePath = ToolBox.GetAttributeString(subElement, "file", "");

                    if (filePath == "") filePath = ToolBox.GetAttributeString(subElement, "sound", "");

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
                        type = (ActionType)Enum.Parse(typeof(ActionType), ToolBox.GetAttributeString(subElement, "type", ""), true);
                    }
                    catch (Exception e)
                    {
                        DebugConsole.ThrowError("Invalid sound type in " + subElement + "!", e);
                        break;
                    }

                    Sound sound = Sound.Load(filePath);

                    float range = ToolBox.GetAttributeFloat(subElement, "range", 800.0f);
                    bool loop = ToolBox.GetAttributeBool(subElement, "loop", false);
                    ItemSound itemSound = new ItemSound(sound, type, range, loop);
                    itemSound.VolumeProperty = ToolBox.GetAttributeString(subElement, "volume", "");
                    itemSound.VolumeMultiplier = ToolBox.GetAttributeFloat(subElement, "volumemultiplier", 1.0f);

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

        public virtual XElement Save(XElement parentElement)
        {
            XElement componentElement = new XElement(name);

            foreach (RelatedItem ri in requiredItems)
            {
                XElement newElement = new XElement("requireditem");
                ri.Save(newElement);
                componentElement.Add(newElement);
            }

            ObjectProperty.SaveProperties(this, componentElement);

            parentElement.Add(componentElement);
            return componentElement;
        }
    }
}
