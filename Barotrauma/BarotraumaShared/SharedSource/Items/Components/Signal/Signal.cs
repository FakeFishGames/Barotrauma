namespace Barotrauma.Items.Components
{
    public struct Signal
    {
        internal int stepsTaken;
        internal string value;
        internal Connection connection;
        internal Item source;
        internal Character sender;
        internal float power;
        internal float strength;

        internal Signal(int stepsTaken, string value, Connection connection, Character sender, Item source = null, float power = 0.0f, float strength = 1.0f)
        {
            this.stepsTaken = stepsTaken;
            this.value = value;
            this.connection = connection;
            this.source = source;
            this.sender = sender;
            this.power = power;
            this.strength = strength;
        }
    }
}
