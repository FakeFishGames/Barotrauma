namespace Barotrauma.Items.Components
{
    sealed class OrComponent : BooleanOperatorComponent
    {
        public OrComponent(Item item, ContentXElement element)
            : base(item, element) { }

        protected override bool GetOutput(int numTrueInputs) => numTrueInputs > 0;
    }
}
