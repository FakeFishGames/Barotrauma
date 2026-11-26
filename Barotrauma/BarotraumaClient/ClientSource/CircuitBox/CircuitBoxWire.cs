#nullable enable

namespace Barotrauma
{
    public partial class CircuitBoxWire
    {
        public CircuitBoxWireRenderer Renderer;

        public void Update() => Renderer.Recompute(From.AnchorPoint, To.AnchorPoint, Color);
    }
}