#nullable enable
namespace Barotrauma
{
    public class FallbackLString : LocalizedString
    {
        private readonly LocalizedString primary;
        private readonly LocalizedString fallback;

        public bool PrimaryIsLoaded { get; private set; }

        public FallbackLString(LocalizedString primary, LocalizedString fallback)
        {
            if (primary is FallbackLString { primary: { } innerPrimary, fallback: { } innerFallback })
            {
                this.primary = innerPrimary;
                this.fallback = innerFallback.Fallback(fallback);
            }
            else
            {
                this.primary = primary;
                this.fallback = fallback;
            }
        }

        protected override bool MustRetrieveValue()
        {
            return base.MustRetrieveValue()
                   || MustRetrieveValue(primary)
                   || MustRetrieveValue(fallback)
                   || PrimaryIsLoaded != primary.Loaded;
        }

        public override bool Loaded => primary.Loaded || fallback.Loaded;
        public override void RetrieveValue()
        {
            cachedValue = primary.Value;
            PrimaryIsLoaded = primary.Loaded;
            if (!primary.Loaded)
            {
                cachedValue = fallback.Value;
            }
        }

        public LocalizedString GetLastFallback()
        {
            if (fallback is FallbackLString innerFallback)
            {
                return innerFallback.GetLastFallback();
            }
            return fallback;
        }
    }
}