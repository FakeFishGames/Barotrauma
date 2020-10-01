namespace Barotrauma
{
    partial class DamageModifier
    {
        [Serialize("", false), Editable]
        public string DamageSound
        {
            get;
            private set;
        }

        [Serialize("", false), Editable]
        public string DamageParticle
        {
            get;
            private set;
        }
    }
}
