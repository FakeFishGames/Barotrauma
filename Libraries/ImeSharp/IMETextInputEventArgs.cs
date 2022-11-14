namespace ImeSharp
{
    public struct IMETextInputEventArgs
    {
        public IMETextInputEventArgs(char character)
        {
            Character = character;
        }

        public readonly char Character;
    }
}