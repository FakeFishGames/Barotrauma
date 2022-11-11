namespace ImeSharp
{
    public delegate void TextInputCallback(char character);
    public delegate void TextCompositionCallback(IMEString compositionText, int cursorPosition, IMEString[] candidateList, int candidatePageStart, int candidatePageSize, int candidateSelection);
    public delegate void CommitTextCompositionCallback(string text);
}
