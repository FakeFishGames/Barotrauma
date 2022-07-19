namespace Barotrauma
{
    partial class DamageModifier
    {
        [Serialize("", IsPropertySaveable.No), Editable]
        public string DamageSound
        {
            get;
            private set;
        }

        [Serialize("", IsPropertySaveable.No), Editable]
        public string DamageParticle
        {
            get;
            private set;
        }
    }
}
