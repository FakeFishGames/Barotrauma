using System;

namespace ImeSharp
{
    /// <summary>
    /// Arguments for the <see cref="IImmService.TextComposition" /> event.
    /// </summary>
    public struct IMETextCompositionEventArgs
    {
        /// <summary>
        // Construct a TextCompositionEventArgs with composition infos.
        /// </summary>
        public IMETextCompositionEventArgs(IMEString compositionText,
                                        int cursorPosition,
                                        IMEString[] candidateList = null,
                                        int candidatePageStart = 0,
                                        int candidatePageSize = 0,
                                        int candidateSelection = 0)
        {
            CompositionText = compositionText;
            CursorPosition = cursorPosition;

            CandidateList = candidateList;
            CandidatePageStart = candidatePageStart;
            CandidatePageSize = candidatePageSize;
            CandidateSelection = candidateSelection;
        }

        /// <summary>
        /// The full string as it's composed by the IMM.
        /// </summary>    
        public readonly IMEString CompositionText;

        /// <summary>
        /// The position of the cursor inside the composed string.
        /// </summary>    
        public readonly int CursorPosition;

        /// <summary>
        /// The candidate text list for the composition.
        /// This property is only supported on WindowsDX and WindowsUniversal.
        /// If the composition string does not generate candidates this array is empty.
        /// </summary>    
        public readonly IMEString[] CandidateList;

        /// <summary>
        /// First candidate index of current page.
        /// </summary>
        public readonly int CandidatePageStart;

        /// <summary>
        /// How many candidates should display per page.
        /// </summary>
        public readonly int CandidatePageSize;

        /// <summary>
        /// The selected candidate index.
        /// </summary>
        public readonly int CandidateSelection;
    }
}
