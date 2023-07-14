#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using Barotrauma.Sounds;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class RoundSound
    {
        public Sound? Sound;
        public readonly float Volume;
        public readonly float Range;
        public readonly Vector2 FrequencyMultiplierRange;
        public readonly bool Stream;
        public readonly bool IgnoreMuffling;

        public readonly string? Filename;

        private RoundSound(ContentXElement element, Sound sound)
        {
            Filename = sound.Filename;
            Sound = sound;
            Stream = sound.Stream;
            Range = element.GetAttributeFloat("range", 1000.0f);
            Volume = element.GetAttributeFloat("volume", 1.0f);
            FrequencyMultiplierRange = new Vector2(1.0f);
            string freqMultAttr = element.GetAttributeString("frequencymultiplier", element.GetAttributeString("frequency", "1.0"))!;
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
                DebugConsole.ThrowError($"Loaded frequency range exceeds max value: {FrequencyMultiplierRange} (original string was \"{freqMultAttr}\")");
            }
            IgnoreMuffling = element.GetAttributeBool("dontmuffle", false);
        }

        public float GetRandomFrequencyMultiplier()
        {
            return Rand.Range(FrequencyMultiplierRange.X, FrequencyMultiplierRange.Y);
        }
        
        private static readonly List<RoundSound> roundSounds = new List<RoundSound>();
        private static readonly Dictionary<string, RoundSound> roundSoundByPath = new Dictionary<string, RoundSound>();
        public static RoundSound? Load(ContentXElement element, bool stream = false)
        {
            if (GameMain.SoundManager?.Disabled ?? true) { return null; }

            var filename = element.GetAttributeContentPath("file") ?? element.GetAttributeContentPath("sound");
            if (filename is null)
            {
                string errorMsg = "Error when loading round sound (" + element + ") - file path not set";
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("RoundSound.LoadRoundSound:FilePathEmpty" + element.ToString(), GameAnalyticsManager.ErrorSeverity.Error, errorMsg + "\n" + Environment.StackTrace.CleanupStackTrace());
                return null;
            }

            Sound? existingSound = null;
            if (roundSoundByPath.TryGetValue(filename.FullPath, out RoundSound? rs) && rs.Sound is { Disposed: false })
            {
                existingSound = rs.Sound;
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
                    DebugConsole.ThrowError(errorMsg, e);
                    if (!ContentPackageManager.ModsEnabled)
                    {
                        GameAnalyticsManager.AddErrorEventOnce("RoundSound.LoadRoundSound:FileNotFound" + filename, GameAnalyticsManager.ErrorSeverity.Error, errorMsg + "\n" + Environment.StackTrace.CleanupStackTrace());
                    }
                    return null;
                }
                catch (System.IO.InvalidDataException e)
                {
                    string errorMsg = "Failed to load sound file \"" + filename + "\" (invalid data).";
                    DebugConsole.ThrowError(errorMsg, e);
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
                    DebugConsole.ThrowError(errorMsg, e);
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