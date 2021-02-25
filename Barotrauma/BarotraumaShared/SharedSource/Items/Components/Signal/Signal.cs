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
    }
}
