namespace Barotrauma.Items.Components
{
    public struct Signal
    {
        internal string value;
        internal int stepsTaken;
        internal Character sender;
        internal Item source;
        internal float power;
        internal float strength;

        internal Signal(string value, int stepsTaken = 0, Character sender = null,
                        Item source = null, float power = 0.0f, float strength = 1.0f)
        {
            this.value = value;
            this.stepsTaken = stepsTaken;
            this.sender = sender;
            this.source = source;
            this.power = power;
            this.strength = strength;
        }

        internal Signal WithStepsTaken(int stepsTaken)
        {
            Signal retVal = this;
            retVal.stepsTaken = stepsTaken;
            return retVal;
        }

        public static bool operator ==(Signal a, Signal b) =>
            a.value == b.value &&
            a.stepsTaken == b.stepsTaken &&
            a.sender == b.sender &&
            a.source == b.source &&
            MathUtils.NearlyEqual(a.power, b.power) &&
            MathUtils.NearlyEqual(a.strength, b.strength);

        public static bool operator !=(Signal a, Signal b) => !(a == b);
    }
}
