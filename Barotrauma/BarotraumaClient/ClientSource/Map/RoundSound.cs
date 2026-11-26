#nullable enable
using Barotrauma.Items.Components;
using Barotrauma.Sounds;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Barotrauma
{
    public class RoundSound
    {
        public Sound? Sound;
        public readonly float Volume;
        public readonly float Range;
        public readonly Vector2 FrequencyMultiplierRange;
        public readonly bool Stream;
        public readonly bool IgnoreMuffling;

        public Identifier VolumeProperty;

        /// <summary>
        /// Determines which of the item's components the <see cref="VolumeProperty"/> is checked on. Only relevant if <see cref="VolumeProperty"/> is set.
        /// </summary>
        public string TargetItemComponent;


        public int LastStreamSeekPos;

        public readonly bool MuteBackgroundMusic;

        public readonly string? Filename;

        private RoundSound(ContentXElement element, Sound sound)
        {
            Filename = sound.Filename;
            Sound = sound;
            Stream = sound.Stream;
            Range = element.GetAttributeFloat("range", 1000.0f);
            Volume = element.GetAttributeFloat("volume", 1.0f);
            IgnoreMuffling = element.GetAttributeBool("dontmuffle", false);
            MuteBackgroundMusic = element.GetAttributeBool("MuteBackgroundMusic", false);

            VolumeProperty = element.GetAttributeIdentifier("volumeproperty", "");
            TargetItemComponent = element.GetAttributeString("targetitemcomponent", "");

            if (!Stream && Sound.DurationSeconds > 60.0f)
            {
                DebugConsole.AddWarning(
                    $"Potential issue in content package: a large audio clip \"{System.IO.Path.GetFileName(Filename)}\" is set to be loaded into memory instead of streaming it from the disk. "+
                    "This can lead to excessive memory usage. Large clips should generally be streamed, while small and frequently played sounds should be loaded to memory to avoid the IO overhead of streaming. "+
                    "Consider adding stream=\"true\" to the sound's XML element.",
                contentPackage: element.ContentPackage);
            }
            
            FrequencyMultiplierRange = new Vector2(1.0f);
            string freqMultAttr = element.GetAttributeString("frequencymultiplier", element.GetAttributeString("frequency", "1.0"));
            if (!freqMultAttr.Contains(','))
            {
                if (float.TryParse(freqMultAttr, NumberStyles.Any, CultureInfo.InvariantCulture, out float freqMult))
                {
                    FrequencyMultiplierRange = new Vector2(freqMult);
                }
            }
            else
            {
                var freqMult = XMLExtensions.ParseVector2(freqMultAttr, false);
                if (freqMult.Y >= 0.25f)
                {
                    FrequencyMultiplierRange = freqMult;
                }
            }
            if (FrequencyMultiplierRange.Y > 4.0f)
            {
                DebugConsole.ThrowError($"Loaded frequency range exceeds max value: {FrequencyMultiplierRange} (original string was \"{freqMultAttr}\")",
                    contentPackage: element.ContentPackage);
            }
        }

        public float GetRandomFrequencyMultiplier()
        {
            return Rand.Range(FrequencyMultiplierRange.X, FrequencyMultiplierRange.Y);
        }

        public float GetSoundVolume(ISerializableEntity targetEntity)
        {
            if (VolumeProperty == "" || targetEntity == null) { return Volume; }

            SerializableProperty? property = null;
            if (TargetItemComponent.IsNullOrEmpty())
            {
                //try to find the property from the specified entity (can be e.g. a Character, Item, ItemComponent)
                if (!targetEntity.SerializableProperties.TryGetValue(VolumeProperty, out property) &&
                    //if that fails, check if the entity is an ItemComponent and try to find the property from the item it belongs to
                    targetEntity is ItemComponent itemComponent && itemComponent.Item.SerializableProperties.TryGetValue(VolumeProperty, out property))
                {
                    targetEntity = itemComponent.Item;
                }
            }
            else if ((targetEntity as Item ?? (targetEntity as ItemComponent)?.Item) is Item targetItem)
            {
                foreach (var itemComponent in targetItem.Components)
                {
                    if (itemComponent.Name == TargetItemComponent &&
                        itemComponent.SerializableProperties.TryGetValue(VolumeProperty, out property))
                    {
                        targetEntity = itemComponent;
                        break;
                    }
                }
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
                newVolume = Math.Min(newVolume * Volume, 1.0f);

                if (!MathUtils.IsValid(newVolume))
                {
                    DebugConsole.Log("Invalid sound volume (entity " + targetEntity.Name + ", " + GetType().ToString() + "): " + newVolume);
                    return 0.0f;
                }

                return MathHelper.Clamp(newVolume, 0.0f, 1.0f);
            }
            else
            {
#if DEBUG
                DebugConsole.ThrowError(
                    $"[DEBUG-ONLY error added in #7929] Failed to find the property {VolumeProperty} in entity {targetEntity?.ToString() ?? "null"} for determining the volume of a sound."+
                    " This may either be a sign that the PR caused issues, or that there are some incorrectly configured sounds or sounds that try to fetch the volume from something that sometimes exists, sometimes not."+
                    " If you see this error, please show it to Joonas so we can investigate.");
                //if you're stumbing on this code in 2027 or later and you've never seen or heard of the error above popping up, feel free to remove it
#endif
            }

            return 0.0f;
        }

        public void Play(Vector2 worldPos, float? volume = null, Hull? hullGuess = null) => SoundPlayer.PlaySound(this, worldPos, volume, hullGuess);
        
        private static readonly List<RoundSound> roundSounds = new List<RoundSound>();
        private static readonly Dictionary<string, RoundSound> roundSoundByPath = new Dictionary<string, RoundSound>();
        public static RoundSound? Load(ContentXElement element)
        {
            if (GameMain.SoundManager?.Disabled ?? true) { return null; }

            bool stream = element.GetAttributeBool(nameof(Stream), false);
            var filename = element.GetAttributeContentPath("file") ?? element.GetAttributeContentPath("sound");
            if (filename is null)
            {
                string errorMsg = "Error when loading round sound (" + element + ") - file path not set";
                DebugConsole.ThrowError(errorMsg,
                    contentPackage: element.ContentPackage);
                GameAnalyticsManager.AddErrorEventOnce("RoundSound.LoadRoundSound:FilePathEmpty" + element.ToString(), GameAnalyticsManager.ErrorSeverity.Error, errorMsg + "\n" + Environment.StackTrace.CleanupStackTrace());
                return null;
            }

            Sound? existingSound = null;
            if (roundSoundByPath.TryGetValue(filename.FullPath, out RoundSound? rs))
            {
                if (rs.Sound is { Disposed: false })
                {
                    existingSound = rs.Sound;
                }
                else
                {
                    roundSoundByPath.Remove(filename.FullPath);
                }
            }

            if (existingSound is null)
            {
                try
                {
                    existingSound = GameMain.SoundManager.LoadSound(filename?.FullPath, stream);
                    if (existingSound == null) { return null; }
                }
                catch (System.IO.FileNotFoundException e)
                {
                    string errorMsg = "Failed to load sound file \"" + filename + "\" (file not found).";
                    DebugConsole.ThrowError(errorMsg, e,
                        contentPackage: element.ContentPackage);
                    if (!ContentPackageManager.ModsEnabled)
                    {
                        GameAnalyticsManager.AddErrorEventOnce("RoundSound.LoadRoundSound:FileNotFound" + filename, GameAnalyticsManager.ErrorSeverity.Error, errorMsg + "\n" + Environment.StackTrace.CleanupStackTrace());
                    }
                    return null;
                }
                catch (System.IO.InvalidDataException e)
                {
                    string errorMsg = "Failed to load sound file \"" + filename + "\" (invalid data).";
                    DebugConsole.ThrowError(errorMsg, e,
                        contentPackage: element.ContentPackage);
                    GameAnalyticsManager.AddErrorEventOnce("RoundSound.LoadRoundSound:InvalidData" + filename, GameAnalyticsManager.ErrorSeverity.Error, errorMsg + "\n" + Environment.StackTrace.CleanupStackTrace());
                    return null;
                }
            }

            RoundSound newSound = new RoundSound(element, existingSound);
            if (filename is not null && !newSound.Stream)
            {
                roundSoundByPath.TryAdd(filename.FullPath, newSound);
            }
            roundSounds.Add(newSound);
            return newSound;
        }

        public static void Reload(RoundSound roundSound)
        {
            Sound? existingSound = roundSounds.Find(s => s.Filename == roundSound.Filename && s.Stream == roundSound.Stream && s.Sound is { Disposed: false })?.Sound;
            if (existingSound == null)
            {
                try
                {
                    existingSound = GameMain.SoundManager.LoadSound(roundSound.Filename, roundSound.Stream);
                }
                catch (System.IO.FileNotFoundException e)
                {
                    string errorMsg = "Failed to load sound file \"" + roundSound.Filename + "\".";
                    DebugConsole.ThrowError(errorMsg, e,
                        contentPackage: roundSound.Sound?.XElement?.ContentPackage);
                    GameAnalyticsManager.AddErrorEventOnce("RoundSound.LoadRoundSound:FileNotFound" + roundSound.Filename, GameAnalyticsManager.ErrorSeverity.Error, errorMsg + "\n" + Environment.StackTrace.CleanupStackTrace());
                    return;
                }
            }
            roundSound.Sound = existingSound;
        }

        public static void RemoveAllRoundSounds()
        {
            foreach (var roundSound in roundSounds)
            {
                roundSound.Sound?.Dispose();
            }
            roundSounds.Clear();
            roundSoundByPath.Clear();
        }
    }
}