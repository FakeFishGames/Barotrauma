#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    public class TagLString : LocalizedString
    {
        private readonly ImmutableArray<Identifier> tags;

        /// <summary>
        /// Did we end up using the text in the default language (English) due to the text not being found in the selected language?
        /// </summary>
        public bool UsingDefaultLanguageAsFallback { get; private set; }

        public TagLString(params Identifier[] tags)
        {
            this.tags = tags.ToImmutableArray();
        }

        private LoadedSuccessfully loadedSuccessfully = LoadedSuccessfully.Unknown;

        public override bool Loaded
        {
            get
            {
                if (loadedSuccessfully == LoadedSuccessfully.Unknown) { RetrieveValue(); }
                return loadedSuccessfully == LoadedSuccessfully.Yes;
            }
        }

        public override void RetrieveValue()
        {
            UpdateLanguage();

            UsingDefaultLanguageAsFallback = false;

            (string value, bool loaded) tryLoad(LanguageIdentifier lang)
            {
                IReadOnlyList<TextPack.Text> candidates = Array.Empty<TextPack.Text>();
                int tagIndex = 0;
            
                if (TextManager.TextPacks.TryGetValue(lang, out var packs))
                {
                    while (candidates.Count == 0 && tagIndex < tags.Length)
                    {
                        foreach (var pack in packs)
                        {
                            if (pack.Texts.TryGetValue(tags[tagIndex], out var texts))
                            {
                                candidates = candidates.ListConcat(texts);
                            }
                        }
                        tagIndex++;
                    }
                }

                if (candidates.Count == 0) { return (string.Empty, loaded: false); }
                var firstOverride = candidates.FirstOrDefault(c => c.IsOverride);
                if (firstOverride != default)
                {
                    //if there's overrides defined, choose from the first pack that defines overrides
                    return (candidates.Where(static c => c.IsOverride).Where(c => c.TextPack == firstOverride.TextPack).GetRandomUnsynced().String, loaded: true);
                }
                else
                {
                    return (candidates.GetRandomUnsynced().String, loaded: true);
                }
            }

            var (value, loaded) = tryLoad(Language);
            loadedSuccessfully = loaded ? LoadedSuccessfully.Yes : LoadedSuccessfully.No;
            cachedValue = value;
            if (!loaded && Language != TextManager.DefaultLanguage)
            {
                (value, bool fallbackLoaded) = tryLoad(TextManager.DefaultLanguage);
                cachedValue = value;
                UsingDefaultLanguageAsFallback = fallbackLoaded;
                //Notice how we don't set loadedSuccessfully again here.
                //This is by design; falling back to English means that
                //this text did NOT load successfully, so Loaded must
                //return false.
            }
        }
    }
}