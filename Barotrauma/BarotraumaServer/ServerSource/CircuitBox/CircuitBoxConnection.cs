#nullable enable

using Barotrauma.Items.Components;

namespace Barotrauma
{
    internal abstract partial class CircuitBoxConnection
    {
        public string Name => Connection.Name;

        private partial void InitProjSpecific(CircuitBox circuitBox)
        {
            Length = 100f;
        }
    }
}