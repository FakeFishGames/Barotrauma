using Barotrauma.Extensions;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    public readonly struct LanguageIdentifier
    {
        public static readonly LanguageIdentifier None = "None".ToLanguageIdentifier();

        public readonly Identifier Value;
        public int ValueHash => Value.GetHashCode();
        public LanguageIdentifier(Identifier value) { Value = value; }

        public override bool Equals(object obj)
        {
            if (obj is LanguageIdentifier other) { return this == other; }
            return base.Equals(obj);
        }

        public override int GetHashCode() => ValueHash;

        public static bool operator ==(LanguageIdentifier a, LanguageIdentifier b) => a.Value == b.Value;
        public static bool operator !=(LanguageIdentifier a, LanguageIdentifier b) => !(a==b);

        public override string ToString() => Value.ToString();
    }

    public static class LanguageIdentifierExtensions
    {
        public static LanguageIdentifier ToLanguageIdentifier(this Identifier identifier)
        {
            return new LanguageIdentifier(identifier);
        }

        public static LanguageIdentifier ToLanguageIdentifier(this string str)
        {
            return str.ToIdentifier().ToLanguageIdentifier();
        }
    }

    public class TextPack
    {
        public readonly TextFile ContentFile;

        public readonly LanguageIdentifier Language;

        public readonly ImmutableDictionary<Identifier, ImmutableArray<string>> Texts;
        public readonly string TranslatedName;
        public readonly bool NoWhitespace;

        public TextPack(TextFile file, ContentXElement mainElement, LanguageIdentifier language)
        {
            ContentFile = file;

            var languageName = mainElement.GetAttributeIdentifier("language", TextManager.DefaultLanguage.Value);
            Language = language;
            TranslatedName = mainElement.GetAttributeString("translatedname", languageName.Value);
            NoWhitespace = mainElement.GetAttributeBool("nowhitespace", false);

            Dictionary<Identifier, List<string>> texts = new Dictionary<Identifier, List<string>>();
            foreach (var element in mainElement.Elements())
            {
                Identifier elemName = element.NameAsIdentifier();
                if (!texts.ContainsKey(elemName)) { texts.Add(elemName, new List<string>()); }
                texts[elemName].Add(element.ElementInnerText()
                    .Replace(@"\n", "\n")
                    .Replace("&amp;", "&")
                    .Replace("&lt;", "<")
                    .Replace("&gt;", ">")
                    .Replace("&quot;", "\"")
                    .Replace("&apos;", "'"));
            }
            Texts = texts.Select(kvp => (kvp.Key, kvp.Value.ToImmutableArray())).ToImmutableDictionary();
        }
        
#if DEBUG
        public void CheckForDuplicates(int index)
        {
            Dictionary<Identifier, int> tagCounts = new Dictionary<Identifier, int>();
            Dictionary<string, int> contentCounts = new Dictionary<string, int>();

            XDocument doc = XMLExtensions.TryLoadXml(ContentFile.Path);
            if (doc == null) { return; }

            foreach (var subElement in doc.Root.Elements())
            {
                Identifier infoName = subElement.NameAsIdentifier();
                if (!tagCounts.ContainsKey(infoName))
                {
                    tagCounts.Add(infoName, 1);
                }
                else
                {
                    tagCounts[infoName] += 1;
                }
                
                string infoContent = subElement.Value;
                if (string.IsNullOrEmpty(infoContent)) continue;
                if (!contentCounts.ContainsKey(infoContent))
                {
                    contentCounts.Add(infoContent, 1);
                }
                else
                {
                    contentCounts[infoContent] += 1;
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("Language: " + Language);
            sb.AppendLine();
            sb.Append("Duplicate tags:");
            sb.AppendLine();
            sb.AppendLine();

            for (int i = 0; i < tagCounts.Keys.Count; i++)
            {
                if (tagCounts[Texts.Keys.ElementAt(i)] > 1)
                {
                    sb.Append(Texts.Keys.ElementAt(i) + " | Count: " + tagCounts[Texts.Keys.ElementAt(i)]);
                    sb.AppendLine();
                }
            }

            sb.AppendLine();
            sb.AppendLine();
            sb.Append("Duplicate content:");
            sb.AppendLine();
            sb.AppendLine();

            for (int i = 0; i < contentCounts.Keys.Count; i++)
            {
                if (contentCounts[contentCounts.Keys.ElementAt(i)] > 1)
                {
                    sb.Append(contentCounts.Keys.ElementAt(i) + " | Count: " + contentCounts[contentCounts.Keys.ElementAt(i)]);
                    sb.AppendLine();
                }
            }

            Barotrauma.IO.File.WriteAllText($"duplicate_{Language.ToString().ToLower()}_{index}.txt", sb.ToString());
        }

        public void WriteToCSV(int index)
        {
            StringBuilder sb = new StringBuilder();

            XDocument doc = XMLExtensions.TryLoadXml(ContentFile.Path);
            if (doc == null) { return; }

            List<(string key, string value)> texts = new List<(string key, string value)>();

            foreach (var element in doc.Root.Elements())
            {
                string text = element.ElementInnerText()
                    .Replace("&amp;", "&")
                    .Replace("&lt;", "<")
                    .Replace("&gt;", ">")
                    .Replace("&quot;", "\"")
                    .Replace("&apos;", "'");

                texts.Add((element.Name.ToString(), text));
            }

            foreach ((string key, string value) in texts)
            {
                sb.Append(key); // ID
                sb.Append('*');
                sb.Append(value); // Original
                sb.Append('*');
                // Translated
                sb.Append('*');
                // Comments
                sb.AppendLine();
                
            }

            Barotrauma.IO.File.WriteAllText($"csv_{Language.ToString().ToLower()}_{index}.csv", sb.ToString());
        }
#endif
    }
}