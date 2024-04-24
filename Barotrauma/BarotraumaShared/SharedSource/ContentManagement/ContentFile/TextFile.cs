using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    [NotSyncedInMultiplayer]
    public sealed class TextFile : ContentFile
    {
        public TextFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        public override void LoadFile()
        {
            XDocument doc = XMLExtensions.TryLoadXml(Path);
            var mainElement = doc.Root.FromPackage(ContentPackage);

            var languageName = mainElement.GetAttributeIdentifier("language", TextManager.DefaultLanguage.Value);

            LanguageIdentifier language = languageName.ToLanguageIdentifier();
            if (!TextManager.TextPacks.ContainsKey(language))
            {
                TextManager.TextPacks.TryAdd(language, ImmutableList<TextPack>.Empty);
            }
            var newPack = new TextPack(this, mainElement, language);
            var newList = TextManager.TextPacks[language].Add(newPack);
            TextManager.TextPacks.TryRemove(language, out _);
            TextManager.TextPacks.TryAdd(language, newList);
            TextManager.IncrementLanguageVersion();
        }

        public override void UnloadFile()
        {
            foreach (var kvp in TextManager.TextPacks.ToArray())
            {
                var newList = kvp.Value.Where(p => p.ContentFile != this).ToImmutableList();
                TextManager.TextPacks.TryRemove(kvp.Key, out _);
                if (newList.Count != 0) { TextManager.TextPacks.TryAdd(kvp.Key, newList); }
            }
            TextManager.IncrementLanguageVersion();
            if (!TextManager.TextPacks.ContainsKey(GameSettings.CurrentConfig.Language) && 
                GameSettings.CurrentConfig.Language != TextManager.DefaultLanguage)
            {
                DebugConsole.AddWarning($"The language {GameSettings.CurrentConfig.Language} is no longer available. Switching to {TextManager.DefaultLanguage}...");
                var config = GameSettings.CurrentConfig;
                config.Language = TextManager.DefaultLanguage;
                GameSettings.SetCurrentConfig(config);
            }
        }

        public override void Sort()
        {
            foreach (var language in TextManager.TextPacks.Keys.ToList())
            {
                TextManager.TextPacks[language] =
                    TextManager.TextPacks[language].Sort((t1, t2) => (t1.ContentFile.ContentPackage?.Index ?? int.MaxValue) - (t2.ContentFile.ContentPackage?.Index ?? int.MaxValue));                
            }
        }
    }
}
