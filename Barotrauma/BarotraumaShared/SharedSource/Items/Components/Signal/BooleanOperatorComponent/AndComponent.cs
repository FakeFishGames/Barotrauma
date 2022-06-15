namespace Barotrauma.Items.Components
{
    sealed class AndComponent : BooleanOperatorComponent
    {
        public AndComponent(Item item, ContentXElement element)
            : base(item, element) { }

        protected override bool GetOutput(int numTrueInputs) => numTrueInputs >= 2;
    }
}
