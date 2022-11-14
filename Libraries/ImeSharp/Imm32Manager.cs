using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Globalization;
using System.Diagnostics;
using ImeSharp.Native;

namespace ImeSharp
{
    internal class Imm32Manager
    {

        // If the system is IMM enabled, this is true.
        private static bool _immEnabled = SafeSystemMetrics.IsImmEnabled;

        public static bool ImmEnabled { get { return _immEnabled; } }

        public const int LANG_CHINESE = 0x04;
        public const int LANG_KOREAN = 0x12;
        public const int LANG_JAPANESE = 0x11;

        public static int PRIMARYLANGID(int lgid)
        {
            return ((ushort)(lgid) & 0x3ff);
        }

        static Imm32Manager()
        {
            SetCurrentCulture();
        }

        /// <summary>
        /// return true if the current keyboard layout is a real IMM32-IME.
        /// </summary>
        public static bool IsImm32ImeCurrent()
        {
            if (!_immEnabled)
                return false;

            IntPtr hkl = NativeMethods.GetKeyboardLayout(0);

            return IsImm32Ime(hkl);
        }

        /// <summary>
        /// return true if the keyboard layout is a real IMM32-IME.
        /// </summary>
        public static bool IsImm32Ime(IntPtr hkl)
        {
            if (hkl == IntPtr.Zero)
                return false;

            return ((NativeMethods.IntPtrToInt32(hkl) & 0xf0000000) == 0xe0000000);
        }

        private static int _inputLanguageId;

        internal static void SetCurrentCulture()
        {
            var hkl = NativeMethods.GetKeyboardLayout(0);
            _inputLanguageId = NativeMethods.IntPtrToInt32(hkl) & 0xFFFF;
        }

        private IntPtr _windowHandle;

        private IntPtr _defaultImc;
        private IntPtr DefaultImc
        {
            get
            {
                if (_defaultImc == IntPtr.Zero)
                {
                    IntPtr himc = NativeMethods.ImmCreateContext();

                    // Store the default imc to _defaultImc.
                    _defaultImc = himc;
                }
                return _defaultImc;
            }
        }

        private static ImmCompositionStringHandler _compositionStringHandler;
        private static ImmCompositionIntHandler _compositionCursorHandler;

        private bool _lastImmOpenStatus;

        public Imm32Manager(IntPtr windowHandle)
        {
            _windowHandle = windowHandle;

            _compositionStringHandler = new ImmCompositionStringHandler(DefaultImc, NativeMethods.GCS_COMPSTR);
            _compositionCursorHandler = new ImmCompositionIntHandler(DefaultImc, NativeMethods.GCS_CURSORPOS);
        }

        public static Imm32Manager Current
        {
            get
            {
                var defaultImm32Manager = InputMethod.DefaultImm32Manager;

                if (defaultImm32Manager == null)
                {
                    defaultImm32Manager = new Imm32Manager(InputMethod.WindowHandle);
                    InputMethod.DefaultImm32Manager = defaultImm32Manager;
                }

                return defaultImm32Manager;
            }
        }

        public void Enable()
        {
            if (DefaultImc != IntPtr.Zero)
            {
                // Create a temporary system caret
                NativeMethods.CreateCaret(_windowHandle, IntPtr.Zero, 2, 10);
                NativeMethods.ImmAssociateContext(_windowHandle, _defaultImc);
            }
        }

        public void Disable()
        {
            NativeMethods.ImmAssociateContext(_windowHandle, IntPtr.Zero);
            NativeMethods.DestroyCaret();
        }

        const int kCaretMargin = 1;

        // Set candidate window position.
        // Borrowed from https://github.com/chromium/chromium/blob/master/ui/base/ime/win/imm32_manager.cc
        public void SetCandidateWindow(TsfSharp.Rect caretRect)
        {
            int x = caretRect.Left;
            int y = caretRect.Top;

            if (PRIMARYLANGID(_inputLanguageId) == LANG_CHINESE)
            {
                // Chinese IMEs ignore function calls to ::ImmSetCandidateWindow()
                // when a user disables TSF (Text Service Framework) and CUAS (Cicero
                // Unaware Application Support).
                // On the other hand, when a user enables TSF and CUAS, Chinese IMEs
                // ignore the position of the current system caret and uses the
                // parameters given to ::ImmSetCandidateWindow() with its 'dwStyle'
                // parameter CFS_CANDIDATEPOS.
                // Therefore, we do not only call ::ImmSetCandidateWindow() but also
                // set the positions of the temporary system caret.
                var candidateForm = new NativeMethods.CANDIDATEFORM();
                candidateForm.dwStyle = NativeMethods.CFS_CANDIDATEPOS;
                candidateForm.ptCurrentPos.X = x;
                candidateForm.ptCurrentPos.Y = y;
                NativeMethods.ImmSetCandidateWindow(_defaultImc, ref candidateForm);
            }

            if (PRIMARYLANGID(_inputLanguageId) == LANG_JAPANESE)
                NativeMethods.SetCaretPos(x, caretRect.Bottom);
            else
                NativeMethods.SetCaretPos(x, y);

            // Set composition window position also to ensure move the candidate window position.
            var compositionForm = new NativeMethods.COMPOSITIONFORM();
            compositionForm.dwStyle = NativeMethods.CFS_POINT;
            compositionForm.ptCurrentPos.X = x;
            compositionForm.ptCurrentPos.Y = y;
            NativeMethods.ImmSetCompositionWindow(_defaultImc, ref compositionForm);

            if (PRIMARYLANGID(_inputLanguageId) == LANG_KOREAN)
            {
                // Chinese IMEs and Japanese IMEs require the upper-left corner of
                // the caret to move the position of their candidate windows.
                // On the other hand, Korean IMEs require the lower-left corner of the
                // caret to move their candidate windows.
                y += kCaretMargin;
            }

            // Need to return here since some Chinese IMEs would stuck if set
            // candidate window position with CFS_EXCLUDE style.
            if (PRIMARYLANGID(_inputLanguageId) == LANG_CHINESE) return;

            // Japanese IMEs and Korean IMEs also use the rectangle given to
            // ::ImmSetCandidateWindow() with its 'dwStyle' parameter CFS_EXCLUDE
            // to move their candidate windows when a user disables TSF and CUAS.
            // Therefore, we also set this parameter here.
            var excludeRectangle = new NativeMethods.CANDIDATEFORM();
            compositionForm.dwStyle = NativeMethods.CFS_EXCLUDE;
            compositionForm.ptCurrentPos.X = x;
            compositionForm.ptCurrentPos.Y = y;
            compositionForm.rcArea.Left = x;
            compositionForm.rcArea.Top = y;
            compositionForm.rcArea.Right = caretRect.Right;
            compositionForm.rcArea.Bottom = caretRect.Bottom;
            NativeMethods.ImmSetCandidateWindow(_defaultImc, ref excludeRectangle);
        }

        internal bool ProcessMessage(IntPtr hWnd, uint msg, ref IntPtr wParam, ref IntPtr lParam)
        {
            switch (msg)
            {
                case NativeMethods.WM_INPUTLANGCHANGE:
                    SetCurrentCulture();
                    break;
                case NativeMethods.WM_IME_SETCONTEXT:
                    if (wParam.ToInt32() == 1 && InputMethod.Enabled)
                    {
                        // Must re-associate ime context or things won't work.
                        NativeMethods.ImmAssociateContext(_windowHandle, DefaultImc);

                        if (_lastImmOpenStatus)
                            NativeMethods.ImmSetOpenStatus(DefaultImc, true);

                        var lParam64 = lParam.ToInt64();
                        if (!InputMethod.ShowOSImeWindow)
                            lParam64 &= ~NativeMethods.ISC_SHOWUICANDIDATEWINDOW;
                        else
                            lParam64 &= ~NativeMethods.ISC_SHOWUICOMPOSITIONWINDOW;
                        lParam = (IntPtr)(int)lParam64;
                    }
                    break;
                case NativeMethods.WM_KILLFOCUS:
                    _lastImmOpenStatus = NativeMethods.ImmGetOpenStatus(DefaultImc);
                    break;
                case NativeMethods.WM_IME_NOTIFY:
                    IMENotify(wParam.ToInt32());
                    if (!InputMethod.ShowOSImeWindow)
                        return true;
                    break;
                case NativeMethods.WM_IME_STARTCOMPOSITION:
                    //Debug.WriteLine("NativeMethods.WM_IME_STARTCOMPOSITION");
                    IMEStartComposion(lParam.ToInt32());
                    // Force to not show composition window, `lParam64 &= ~ISC_SHOWUICOMPOSITIONWINDOW` don't work sometime.
                    return true;
                case NativeMethods.WM_IME_COMPOSITION:
                    //Debug.WriteLine("NativeMethods.WM_IME_COMPOSITION");
                    IMEComposition(lParam.ToInt32());
                    break;
                case NativeMethods.WM_IME_ENDCOMPOSITION:
                    //Debug.WriteLine("NativeMethods.WM_IME_ENDCOMPOSITION");
                    IMEEndComposition(lParam.ToInt32());
                    if (!InputMethod.ShowOSImeWindow)
                        return true;
                    break;
            }

            return false;
        }

        private void IMENotify(int WParam)
        {
            switch (WParam)
            {
                case NativeMethods.IMN_OPENCANDIDATE:
                case NativeMethods.IMN_CHANGECANDIDATE:
                    IMEChangeCandidate();
                    break;
                case NativeMethods.IMN_CLOSECANDIDATE:
                    InputMethod.ClearCandidates();
                    break;
                default:
                    break;
            }
        }

        private void IMEChangeCandidate()
        {
            if (TextServicesLoader.ServicesInstalled) // TSF is enabled
            {
                if (!TextStore.Current.SupportUIElement) // But active IME not support UIElement
                    UpdateCandidates(); // We have to fetch candidate list here.

                return;
            }

            // Normal candidate list fetch in IMM32
            UpdateCandidates();
            // Send event on candidate updates
            InputMethod.OnTextComposition(this, new IMEString(_compositionStringHandler.Values, _compositionStringHandler.Count), _compositionCursorHandler.Value);

            if (InputMethod.CandidateList != null)
                ArrayPool<IMEString>.Shared.Return(InputMethod.CandidateList);
        }

        private unsafe void UpdateCandidates()
        {
            uint length = NativeMethods.ImmGetCandidateList(DefaultImc, 0, IntPtr.Zero, 0);
            if (length > 0)
            {
                IntPtr pointer = Marshal.AllocHGlobal((int)length);
                length = NativeMethods.ImmGetCandidateList(DefaultImc, 0, pointer, length);
                NativeMethods.CANDIDATELIST* cList = (NativeMethods.CANDIDATELIST*)pointer;

                var selection = (int)cList->dwSelection;
                var pageStart = (int)cList->dwPageStart;
                var pageSize = (int)cList->dwPageSize;

                selection -= pageStart;

                IMEString[] candidates = ArrayPool<IMEString>.Shared.Rent(pageSize);

                int i, j;
                for (i = pageStart, j = 0; i < cList->dwCount && j < pageSize; i++, j++)
                {
                    int sOffset = Marshal.ReadInt32(pointer, 24 + 4 * i);
                    candidates[j] = new IMEString(pointer + sOffset);
                }

                //Debug.WriteLine("IMM========IMM");
                //Debug.WriteLine("pageStart: {0}, pageSize: {1}, selection: {2}, candidates:", pageStart, pageSize, selection);
                //for (int k = 0; k < candidates.Length; k++)
                //    Debug.WriteLine("  {2}{0}.{1}", k + 1, candidates[k], k == selection ? "*" : "");
                //Debug.WriteLine("IMM++++++++IMM");

                InputMethod.CandidatePageStart = pageStart;
                InputMethod.CandidatePageSize = pageSize;
                InputMethod.CandidateSelection = selection;
                InputMethod.CandidateList = candidates;

                Marshal.FreeHGlobal(pointer);
            }
        }

        private void ClearComposition()
        {
            _compositionStringHandler.Clear();
        }

        private void IMEStartComposion(int lParam)
        {
            InputMethod.OnTextCompositionStarted(this);
            ClearComposition();
        }

        private void IMEComposition(int lParam)
        {
            if (_compositionStringHandler.Update(lParam))
            {
                _compositionCursorHandler.Update();

                InputMethod.OnTextComposition(this, new IMEString(_compositionStringHandler.Values, _compositionStringHandler.Count), _compositionCursorHandler.Value);
            }
        }

        private void IMEEndComposition(int lParam)
        {
            InputMethod.ClearCandidates();
            ClearComposition();

            InputMethod.OnTextCompositionEnded(this);
        }
    }
}
