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
        internal float signalStrength;
    }
}
