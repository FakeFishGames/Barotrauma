namespace Barotrauma.Items.Components
{
    sealed class XorComponent : BooleanOperatorComponent
    {
        public XorComponent(Item item, ContentXElement element)
            : base(item, element) { }

        protected override bool GetOutput(int numTrueInputs) => numTrueInputs == 1;
    }
}
