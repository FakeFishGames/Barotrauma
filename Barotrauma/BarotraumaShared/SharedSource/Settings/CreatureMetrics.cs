using System;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Linq;
using Barotrauma.IO;
using XmlWriterSettings = System.Xml.XmlWriterSettings;
#nullable enable

namespace Barotrauma
{
    public static class CreatureMetrics
    {
        private const string path = "creature_metrics.xml";

        /// <summary>
        /// Resets every round.
        /// </summary>
        public static HashSet<Identifier> RecentlyEncountered { get; private set; } = new HashSet<Identifier>();
        public static HashSet<Identifier> Encountered { get; private set; } = new HashSet<Identifier>();
        public static HashSet<Identifier> Unlocked { get; private set; } = new HashSet<Identifier>();
        public static HashSet<Identifier> Killed { get; private set; } = new HashSet<Identifier>();
        public static bool IsInitialized { get; private set; }
        public static bool UnlockAll { get; set; }

        public static void Init()
        {
            IsInitialized = true;
            if (File.Exists(path))
            {
                Load();
            }
            Save();
        }

        private static void Load()
        {
            XDocument doc = XMLExtensions.TryLoadXml(path);
            XElement? root = doc?.Root;
            if (root == null)
            {
                DebugConsole.AddWarning($"Failed to load creature metrics from {path}!");
                return;
            }
            UnlockAll = root.GetAttributeBool(nameof(UnlockAll), UnlockAll);
            Unlocked = new HashSet<Identifier>(root.GetAttributeIdentifierArray(nameof(Unlocked), Array.Empty<Identifier>()));
            Encountered = new HashSet<Identifier>(root.GetAttributeIdentifierArray(nameof(Encountered), Array.Empty<Identifier>()));
            Killed = new HashSet<Identifier>(root.GetAttributeIdentifierArray(nameof(Killed), Array.Empty<Identifier>()));
            SyncSets();
        }

        public static void Save()
        {
            if (!IsInitialized)
            {
                throw new Exception("Creature Metrics not yet initialized!");
            }
            SyncSets();
            XDocument configDoc = new XDocument();
            XElement root = new XElement("CreatureMetrics");
            configDoc.Add(root);
            root.SetAttributeValue(nameof(UnlockAll), UnlockAll);
            root.SetAttributeValue(nameof(Unlocked), string.Join(",", Unlocked).Trim().ToLowerInvariant());
            root.SetAttributeValue(nameof(Encountered), string.Join(",", Encountered).Trim().ToLowerInvariant());
            root.SetAttributeValue(nameof(Killed), string.Join(",", Killed).Trim().ToLowerInvariant());
            configDoc.SaveSafe(path);
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = true,
                NewLineOnAttributes = true
            };
            try
            {
                using var writer = XmlWriter.Create(path, settings);
                configDoc.WriteTo(writer);
                writer.Flush();
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Saving creature metrics failed.", e);
                GameAnalyticsManager.AddErrorEventOnce("CreatureMetrics.Save:SaveFailed", GameAnalyticsManager.ErrorSeverity.Error,
                    "Saving creature metrics failed.\n" + e.Message + "\n" + e.StackTrace.CleanupStackTrace());
            }
        }

        public static void RecordKill(Identifier species)
        {
            AddEncounter(species);
            if (!Killed.Contains(species))
            {
                Killed.Add(species);
            }
        }

        public static void AddEncounter(Identifier species)
        {
            if (species == CharacterPrefab.HumanSpeciesName) { return; }
            if (Encountered.Contains(species)) { return; }
            Encountered.Add(species);
            RecentlyEncountered.Add(species);
            UnlockInEditor(species);
        }

        private static IEnumerable<CharacterFile>? vanillaCharacters;
        public static void UnlockInEditor(Identifier species)
        {
            if (species == CharacterPrefab.HumanSpeciesName) { return; }
            if (Unlocked.Contains(species)) { return; }
            vanillaCharacters ??= GameMain.VanillaContent.GetFiles<CharacterFile>();
            var contentFile = CharacterPrefab.FindBySpeciesName(species);
            if (contentFile == null) { return; }
            if (!vanillaCharacters.Contains(contentFile.ContentFile))
            {
                // Don't try to unlock custom characters. They are always unlocked.
                return;
            }
            Unlocked.Add(species);
        }

        private static void SyncSets()
        {
            // Ensure that all killed are also encountered and both unlocked.
            // Otherwise we could permanently hide some creatures by manually adding them to the encountered or by removing from unlocked in the xml file.
            foreach (var species in Killed)
            {
                Encountered.Add(species);
            }
            foreach (var species in Encountered)
            {
                Unlocked.Add(species);
            }
        }
    }
}