using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    public sealed class TextFile : ContentFile
    {
        public TextFile(ContentPackage contentPackage, ContentPath path) : base(contentPackage, path) { }

        public override void LoadFile()
        {
            XDocument doc = XMLExtensions.TryLoadXml(Path);
            var mainElement = doc.Root.FromContent(Path);

            var languageName = mainElement.GetAttributeIdentifier("language", TextManager.DefaultLanguage.Value);

            LanguageIdentifier language = languageName.ToLanguageIdentifier();
            if (!TextManager.TextPacks.ContainsKey(language))
            {
                TextManager.TextPacks.TryAdd(language, ImmutableHashSet<TextPack>.Empty);
            }

            var newPack = new TextPack(this, mainElement, language);
            var newHashSet = TextManager.TextPacks[language].Add(newPack);
            TextManager.TextPacks.TryRemove(language, out _);
            TextManager.TextPacks.TryAdd(language, newHashSet);
            TextManager.IncrementLanguageVersion();
        }

        public override void UnloadFile()
        {
            foreach (var kvp in TextManager.TextPacks.ToArray())
            {
                var newHashSet = kvp.Value.Where(p => p.ContentFile != this).ToImmutableHashSet();
                TextManager.TextPacks.TryRemove(kvp.Key, out _);
                if (newHashSet.Count != 0) { TextManager.TextPacks.TryAdd(kvp.Key, newHashSet); }
            }
            TextManager.IncrementLanguageVersion();
        }

        public override void Sort()
        {
            //Overrides for text packs don't exist! Should we change this?
        }
    }
}
