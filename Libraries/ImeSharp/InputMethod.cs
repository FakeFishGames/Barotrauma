using System;
using System.Globalization;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ImeSharp.Native;

namespace ImeSharp
{
    public static class InputMethod
    {
        private static IntPtr _windowHandle;
        public static IntPtr WindowHandle { get { return _windowHandle; } }

        private static IntPtr _prevWndProc;
        private static NativeMethods.WndProcDelegate _wndProcDelegate;

        private static TextServicesContext _textServicesContext;
        internal static TextServicesContext TextServicesContext
        {
            get { return _textServicesContext; }
            set { _textServicesContext = value; }
        }

        private static TextStore _defaultTextStore;
        internal static TextStore DefaultTextStore
        {
            get { return _defaultTextStore; }
            set { _defaultTextStore = value; }
        }

        private static Imm32Manager _defaultImm32Manager;
        internal static Imm32Manager DefaultImm32Manager
        {
            get { return _defaultImm32Manager; }
            set { _defaultImm32Manager = value; }
        }

        private static bool _enabled;
        public static bool Enabled
        {
            get { return _enabled; }
            set
            {
                if (_enabled == value) return;

                _enabled = value;

                EnableOrDisableInputMethod(_enabled);
            }
        }

        internal static TsfSharp.Rect TextInputRect;

        /// <summary>
        /// Set the position of the candidate window rendered by the OS.
        /// Let the OS render the candidate window by set param "showOSImeWindow" to <c>true</c> on <see cref="Initialize"/>.
        /// </summary>
        public static void SetTextInputRect(int x, int y, int width, int height)
        {
            if (!_showOSImeWindow) return;

            TextInputRect.Left = x;
            TextInputRect.Top = y;
            TextInputRect.Right = x + width;
            TextInputRect.Bottom = y + height;

            if (Imm32Manager.ImmEnabled)
                Imm32Manager.Current.SetCandidateWindow(TextInputRect);
        }

        private static bool _showOSImeWindow = false;

        /// <summary>
        /// Return if let OS render IME Candidate window or not.
        /// </summary>
        public static bool ShowOSImeWindow { get { return _showOSImeWindow; } }

        internal static int CandidatePageStart;
        internal static int CandidatePageSize;
        internal static int CandidateSelection;
        internal static IMEString[] CandidateList;

        internal static void ClearCandidates()
        {
            CandidateList = null;
            CandidatePageStart = 0;
            CandidatePageSize = 0;
            CandidateSelection = 0;
        }

        public static event EventHandler<IMETextCompositionEventArgs> TextComposition;
        public static event EventHandler<IMETextInputEventArgs> TextInput;
        public static event EventHandler<string> CommitTextComposition;

        public static TextInputCallback TextInputCallback { get; set; }
        public static TextCompositionCallback TextCompositionCallback { get; set; }
        public static CommitTextCompositionCallback CommitTextCompositionCallback { get; set; }

        /// <summary>
        /// Initialize InputMethod with a Window Handle.
        /// Let the OS render the candidate window by set <see paramref="showOSImeWindow"/> to <c>true</c>.
        /// </summary>
        public static void Initialize(IntPtr windowHandle, bool showOSImeWindow = true)
        {
            if (_windowHandle != IntPtr.Zero)
                throw new InvalidOperationException("InputMethod can only be initialized once!");

            _windowHandle = windowHandle;
            _showOSImeWindow = showOSImeWindow;

            _wndProcDelegate = new NativeMethods.WndProcDelegate(WndProc);
            _prevWndProc = (IntPtr)NativeMethods.SetWindowLongPtr(_windowHandle, NativeMethods.GWL_WNDPROC,
                Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
        }

        internal static void OnTextInput(object sender, char character)
        {
            if (TextInput != null)
                TextInput.Invoke(sender, new IMETextInputEventArgs(character));

            if (TextInputCallback != null)
                TextInputCallback(character);
        }

        // Some Chinese IME only send composition start event but no composition update event.
        // We need this to ensure candidate window position can be set in time.
        internal static void OnTextCompositionStarted(object sender)
        {
            if (TextComposition != null)
                TextComposition.Invoke(sender, new IMETextCompositionEventArgs(IMEString.Empty, 0));

            if (TextCompositionCallback != null)
                TextCompositionCallback(IMEString.Empty, 0, null, 0, 0, 0);
        }

        // On text composition update.
        internal static void OnTextComposition(object sender, IMEString compositionText, int cursorPos)
        {
            if (compositionText.Count == 0) // Crash guard
                cursorPos = 0;

            if (cursorPos > compositionText.Count)  // Another crash guard
                cursorPos = compositionText.Count;

            if (TextComposition != null)
            {
                TextComposition.Invoke(sender,
                    new IMETextCompositionEventArgs(compositionText, cursorPos, CandidateList, CandidatePageStart, CandidatePageSize, CandidateSelection));
            }

            if (TextCompositionCallback != null)
                TextCompositionCallback(compositionText, cursorPos, CandidateList, CandidatePageStart, CandidatePageSize, CandidateSelection);
        }

        internal static void OnTextCompositionResult(object sender, string compositionResult)
        {
            if (CommitTextComposition != null)
                CommitTextComposition.Invoke(sender, compositionResult);

            if (CommitTextCompositionCallback != null)
                CommitTextCompositionCallback(compositionResult);
        }

        internal static void OnTextCompositionEnded(object sender)
        {
            if (TextComposition != null)
                TextComposition.Invoke(sender, new IMETextCompositionEventArgs(IMEString.Empty, 0));

            if (TextCompositionCallback != null)
                TextCompositionCallback(IMEString.Empty, 0, null, 0, 0, 0);
        }

        private static void EnableOrDisableInputMethod(bool bEnabled)
        {
            // InputMethod enable/disabled status was changed on the current focus Element.
            if (TextServicesLoader.ServicesInstalled)
            {
                if (bEnabled)
                    TextServicesContext.Current.SetFocusOnDefaultTextStore();
                else
                    TextServicesContext.Current.SetFocusOnEmptyDim();
            }

            // Under IMM32 enabled system, we associate default hIMC or null hIMC.
            if (Imm32Manager.ImmEnabled)
            {
                if (bEnabled)
                    Imm32Manager.Current.Enable();
                else
                    Imm32Manager.Current.Disable();
            }
        }

        private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (Imm32Manager.ImmEnabled)
            {
                if (Imm32Manager.Current.ProcessMessage(hWnd, msg, ref wParam, ref lParam))
                    return IntPtr.Zero;
            }

            switch (msg)
            {
                case NativeMethods.WM_DESTROY:
                    TextServicesContext.Current.Uninitialize(true);
                    break;
                case NativeMethods.WM_CHAR:
                    {
                        if (InputMethod.Enabled)
                            InputMethod.OnTextInput(null, (char)wParam.ToInt32());

                        break;
                    }
            }

            return NativeMethods.CallWindowProc(_prevWndProc, hWnd, msg, wParam, lParam);
        }

        /// <summary>
        /// Custom windows message pumping to fix frame stuck issue.
        /// Normally, you need call this method in <see cref="Application.Idle" /> handler.
        /// </summary>
        public static void PumpMessage()
        {
            if (!Enabled) return;
            if (!TextServicesLoader.ServicesInstalled) return;

            bool result;
            var msg = new NativeMethods.NativeMessage();

            do
            {
                result = NativeMethods.PeekMessage(out msg, _windowHandle, 0, 0, NativeMethods.PM_REMOVE);

                if (result)
                {
                    NativeMethods.TranslateMessage(ref msg);
                    NativeMethods.DispatchMessage(ref msg);
                }
            } while (result);

            NativeMethods.PostMessage(_windowHandle, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
