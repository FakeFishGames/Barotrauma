using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ImeSharp.Native;
using SharpGen.Runtime;
using SharpGen.Runtime.Win32;
using TsfSharp;

namespace ImeSharp
{
    internal class TextStore : CallbackBase,
                             ITextStoreACP,
                             ITfContextOwnerCompositionSink,
                             ITfTextEditSink,
                             ITfUIElementSink
    {
        public static readonly Guid IID_ITextStoreACPSink = new Guid(0x22d44c94, 0xa419, 0x4542, 0xa2, 0x72, 0xae, 0x26, 0x09, 0x3e, 0xce, 0xcf);
        public static readonly Guid GUID_PROP_COMPOSING = new Guid("e12ac060-af15-11d2-afc5-00105a2799b5");

        //------------------------------------------------------
        //
        //  Constructors
        //
        //------------------------------------------------------

        #region Constructors

        // Creates a TextStore instance.
        public TextStore(IntPtr windowHandle)
        {
            _windowHandle = windowHandle;


            _viewCookie = Environment.TickCount;

            _editCookie = Tsf.TF_INVALID_COOKIE;
            _uiElementSinkCookie = Tsf.TF_INVALID_COOKIE;
            _textEditSinkCookie = Tsf.TF_INVALID_COOKIE;

            _IMEStringPool = ArrayPool<IMEString>.Shared;
        }

        #endregion Constructors

        //------------------------------------------------------
        //
        //  Methods - ITextStoreACP
        //
        //------------------------------------------------------

        #region ITextStoreACP

        public void AdviseSink(Guid riid, IUnknown obj, int flags)
        {
            ITextStoreACPSink sink;

            if (riid != IID_ITextStoreACPSink)
                throw new COMException("TextStore_CONNECT_E_CANNOTCONNECT");

            sink = (obj as ComObject).QueryInterface<ITextStoreACPSink>();

            if (sink == null)
                throw new COMException("TextStore_E_NOINTERFACE");

            // It's legal to replace existing sink.
            if (_sink != null)
                _sink.Dispose();

            (obj as ComObject).Dispose();

            _sink = sink;
        }

        public void UnadviseSink(IUnknown obj)
        {
            var sink = (obj as ComObject).QueryInterface<ITextStoreACPSink>();
            if (sink.NativePointer != _sink.NativePointer)
                throw new COMException("TextStore_CONNECT_E_NOCONNECTION");

            _sink.Release();
            _sink = null;
        }

        private bool _LockDocument(TsfSharp.TsLfFlags dwLockFlags)
        {
            if (_locked)
                return false;

            _locked = true;
            _lockFlags = dwLockFlags;

            return true;
        }

        private void ResetIfRequired()
        {
            if (!_commited)
                return;

            _commited = false;

            TsTextchange textChange;
            textChange.AcpStart = 0;
            textChange.AcpOldEnd = _inputBuffer.Count;
            textChange.AcpNewEnd = 0;
            _inputBuffer.Clear();

            _sink.OnTextChange(0, textChange);

            _acpStart = _acpEnd = 0;
            _sink.OnSelectionChange();
            _commitStart = _commitLength = 0;

            //Debug.WriteLine("TextStore reset!!!");
        }

        private void _UnlockDocument()
        {
            Result hr;
            _locked = false;
            _lockFlags = 0;

            ResetIfRequired();

            //if there is a queued lock, grant it
            if (_lockRequestQueue.Count > 0)
            {
                hr = RequestLock(_lockRequestQueue.Dequeue());
            }

            //if any layout changes occurred during the lock, notify the manager
            if (_layoutChanged)
            {
                _layoutChanged = false;
                _sink.OnLayoutChange(TsLayoutCode.TsLcChange, _viewCookie);
            }
        }

        private bool _IsLocked(TsfSharp.TsLfFlags dwLockType)
        {
            return _locked && (_lockFlags & dwLockType) != 0;
        }

        public Result RequestLock(TsfSharp.TsLfFlags dwLockFlags)
        {
            Result hrSession;

            if (_sink == null)
                throw new COMException("TextStore_NoSink");

            if (dwLockFlags == 0)
                throw new COMException("TextStore_BadLockFlags");

            hrSession = Result.Fail;

            if (_locked)
            {
                //the document is locked

                if ((dwLockFlags & TsfSharp.TsLfFlags.Sync) == TsfSharp.TsLfFlags.Sync)
                {
                    /*
                    The caller wants an immediate lock, but this cannot be granted because
                    the document is already locked.
                    */
                    hrSession = (int)TsErrors.TsESynchronous;
                }
                else
                {
                    //the request is asynchronous

                    //Queue the lock request
                    _lockRequestQueue.Enqueue(dwLockFlags);
                    hrSession = (int)TsErrors.TsSAsync;
                }

                return hrSession;
            }

            //lock the document
            _LockDocument(dwLockFlags);

            //call OnLockGranted
            hrSession = _sink.OnLockGranted(dwLockFlags);

            //unlock the document
            _UnlockDocument();

            return hrSession;
        }

        public TsStatus GetStatus()
        {
            TsStatus status = new TsStatus();
            status.DynamicFlags = 0;
            status.StaticFlags = 0;

            return status;
        }

        public void QueryInsert(int acpTestStart, int acpTestEnd, uint cch, out int acpResultStart, out int acpResultEnd)
        {
            acpResultStart = acpResultEnd = 0;

            // Fix possible crash
            if (_inputBuffer.Count == 0)
                return;

            //Queryins
            if (acpTestStart > _inputBuffer.Count || acpTestEnd > _inputBuffer.Count)
                throw new COMException("", Result.InvalidArg.Code);

            //Microsoft Pinyin seems does not init the result value, so we set the test value here, in case crash
            acpResultStart = acpTestStart;
            acpResultEnd = acpTestEnd;
        }

        public uint GetSelection(uint index, ref TsSelectionAcp selection)
        {
            //does the caller have a lock
            if (!_IsLocked(TsLfFlags.Read))
            {
                //the caller doesn't have a lock
                //return NativeMethods.TS_E_NOLOCK;
                throw new COMException("", (int)TsErrors.TsENolock);
            }

            //check the requested index
            if (-1 == (int)index)
            {
                index = 0;
            }
            else if (index > 1)
            {
                /*
                The index is too high. This app only supports one selection.
                */
                throw new COMException("", Result.InvalidArg.Code);
            }

            selection.AcpStart = _acpStart;
            selection.AcpEnd = _acpEnd;
            selection.Style.InterimCharFlag = _interimChar;

            if (_interimChar)
            {
                /*
                fInterimChar will be set when an intermediate character has been
                set. One example of when this will happen is when an IME is being
                used to enter characters and a character has been set, but the IME
                is still active.
                */
                selection.Style.Ase = TsActiveSelEnd.TsAeNone;
            }
            else
            {
                selection.Style.Ase = _activeSelectionEnd;
            }

            return 1;
        }

        public void SetSelection(uint count, ref TsSelectionAcp selections)
        {
            //this implementaiton only supports a single selection
            if (count != 1)
                throw new COMException("", Result.InvalidArg.Code);

            //does the caller have a lock
            if (!_IsLocked(TsLfFlags.Readwrite))
            {
                //the caller doesn't have a lock
                //return NativeMethods.TS_E_NOLOCK;
                throw new COMException("", (int)TsErrors.TsENolock);
            }

            _acpStart = selections.AcpStart;
            _acpEnd = selections.AcpEnd;
            _interimChar = selections.Style.InterimCharFlag;

            if (_interimChar)
            {
                /*
                fInterimChar will be set when an intermediate character has been
                set. One example of when this will happen is when an IME is being
                used to enter characters and a character has been set, but the IME
                is still active.
                */
                _activeSelectionEnd = TsActiveSelEnd.TsAeNone;
            }
            else
            {
                _activeSelectionEnd = selections.Style.Ase;
            }

            //if the selection end is at the start of the selection, reverse the parameters
            int lStart = _acpStart;
            int lEnd = _acpEnd;

            if (TsActiveSelEnd.TsAeStart == _activeSelectionEnd)
            {
                lStart = _acpEnd;
                lEnd = _acpStart;
            }
        }


        public void GetText(int acpStart, int acpEnd, System.IntPtr pchPlain, uint cchPlainReq, out uint cchPlainRet,
            ref TsfSharp.TsRuninfo rgRunInfo, uint cRunInfoReq, out uint cRunInfoRet, out int acpNext)
        {
            cchPlainRet = 0;
            cRunInfoRet = 0;
            acpNext = 0;

            //does the caller have a lock
            if (!_IsLocked(TsLfFlags.Read))
            {
                //the caller doesn't have a lock
                throw new COMException("", (int)TsErrors.TsENolock);
            }

            bool fDoText = cchPlainReq > 0;
            bool fDoRunInfo = cRunInfoReq > 0;
            int cchTotal;

            cchPlainRet = 0;
            acpNext = acpStart;

            cchTotal = _inputBuffer.Count;

            //validate the start pos
            if ((acpStart < 0) || (acpStart > cchTotal))
            {
                throw new COMException("", Result.InvalidArg.Code);
            }
            else
            {
                //are we at the end of the document
                if (acpStart == cchTotal)
                {
                    return;
                }
                else
                {
                    int cchReq;

                    /*
                    acpEnd will be -1 if all of the text up to the end is being requested.
                    */

                    if (acpEnd >= acpStart)
                    {
                        cchReq = acpEnd - acpStart;
                    }
                    else
                    {
                        cchReq = cchTotal - acpStart;
                    }

                    if (fDoText)
                    {
                        if (cchReq > cchPlainReq)
                        {
                            cchReq = (int)cchPlainReq;
                        }

                        //extract the specified text range
                        if (pchPlain != IntPtr.Zero && cchPlainReq > 0)
                        {
                            //_inputBuffer.CopyTo(acpStart, pchPlain, 0, cchReq);

                            unsafe
                            {
                                var ptr = (char*)pchPlain;

                                for (int i = acpStart; i < cchReq; i++)
                                {
                                    *ptr = _inputBuffer[i];
                                    ptr++;
                                }
                            }
                        }
                    }

                    //it is possible that only the length of the text is being requested
                    cchPlainRet = (uint)cchReq;

                    if (fDoRunInfo)
                    {
                        /*
                        Runs are used to separate text characters from formatting characters.

                        In this example, sequences inside and including the <> are treated as
                        control sequences and are not displayed.

                        Plain text = "Text formatting."
                        Actual text = "Text <B><I>formatting</I></B>."

                        If all of this text were requested, the run sequence would look like this:

                        prgRunInfo[0].type = TS_RT_PLAIN;   //"Text "
                        prgRunInfo[0].uCount = 5;

                        prgRunInfo[1].type = TS_RT_HIDDEN;  //<B><I>
                        prgRunInfo[1].uCount = 6;

                        prgRunInfo[2].type = TS_RT_PLAIN;   //"formatting"
                        prgRunInfo[2].uCount = 10;

                        prgRunInfo[3].type = TS_RT_HIDDEN;  //</B></I>
                        prgRunInfo[3].uCount = 8;

                        prgRunInfo[4].type = TS_RT_PLAIN;   //"."
                        prgRunInfo[4].uCount = 1;

                        TS_RT_OPAQUE is used to indicate characters or character sequences
                        that are in the document, but are used privately by the application
                        and do not map to text.  Runs of text tagged with TS_RT_OPAQUE should
                        NOT be included in the pchPlain or cchPlainOut [out] parameters.
                        */

                        /*
                        This implementation is plain text, so the text only consists of one run.
                        If there were multiple runs, it would be an error to have consecuative runs
                        of the same type.
                        */
                        rgRunInfo.Type = TsRunType.TsRtPlain;
                        rgRunInfo.Count = (uint)cchReq;
                    }

                    acpNext = acpStart + cchReq;
                }
            }
        }

        public TsTextchange SetText(int dwFlags, int acpStart, int acpEnd, string pchText, uint cch)
        {
            /*
            dwFlags can be:
            TS_ST_CORRECTION
            */
            TsTextchange change = new TsTextchange();

            //set the selection to the specified range
            TsSelectionAcp tsa = new TsSelectionAcp();
            tsa.AcpStart = acpStart;
            tsa.AcpEnd = acpEnd;
            tsa.Style.Ase = TsActiveSelEnd.TsAeStart;
            tsa.Style.InterimCharFlag = false;

            SetSelection(1, ref tsa);

            int start, end;
            InsertTextAtSelection(TsIasFlags.Noquery, pchText, cch, out start, out end, out change);

            return change;
        }

        public IDataObject GetFormattedText(int startIndex, int endIndex)
        {
            throw new COMException("", Result.NotImplemented.Code);
        }

        public IUnknown GetEmbedded(int index, Guid guidService, Guid riid)
        {
            throw new COMException("", Result.NotImplemented.Code);
        }

        public RawBool QueryInsertEmbedded(Guid guidService, ref Formatetc formatEtc)
        {
            throw new COMException("", Result.NotImplemented.Code);
        }

        public TsTextchange InsertEmbedded(int flags, int startIndex, int endIndex, TsfSharp.IDataObject dataObjectRef)
        {
            throw new COMException("", Result.NotImplemented.Code);
        }

        public void InsertTextAtSelection(TsfSharp.TsIasFlags dwFlags, string pchText, uint cch, out int pacpStart, out int pacpEnd, out TsfSharp.TsTextchange pChange)
        {
            pacpStart = pacpEnd = 0;
            pChange = new TsTextchange();

            //does the caller have a lock
            if (!_IsLocked(TsLfFlags.Readwrite))
            {
                //the caller doesn't have a lock
                throw new COMException("", (int)TsErrors.TsENolock);
            }

            int acpStart;
            int acpOldEnd;
            int acpNewEnd;

            acpOldEnd = _acpEnd;

            //set the start point after the insertion
            acpStart = _acpStart;

            //set the end point after the insertion
            acpNewEnd = _acpStart + (int)cch;

            if ((dwFlags & TsIasFlags.Queryonly) == TsIasFlags.Queryonly)
            {
                pacpStart = acpStart;
                pacpEnd = acpOldEnd;
                return;
            }

            //insert the text
            _inputBuffer.RemoveRange(acpStart, acpOldEnd - acpStart);
            _inputBuffer.InsertRange(acpStart, pchText);

            //set the selection
            _acpStart = acpStart;
            _acpEnd = acpNewEnd;

            if ((dwFlags & TsIasFlags.Noquery) != TsIasFlags.Noquery)
            {
                pacpStart = acpStart;
                pacpEnd = acpNewEnd;
            }

            //set the TS_TEXTCHANGE members
            pChange.AcpStart = acpStart;
            pChange.AcpOldEnd = acpOldEnd;
            pChange.AcpNewEnd = acpNewEnd;

            //defer the layout change notification until the document is unlocked
            _layoutChanged = true;
        }

        public void InsertEmbeddedAtSelection(int flags, IDataObject obj, out int startIndex, out int endIndex, out TsTextchange change)
        {
            startIndex = endIndex = 0;
            change = new TsTextchange();
            throw new COMException("", Result.NotImplemented.Code);
        }

        public void RequestSupportedAttrs(int flags, uint cFilterAttrs, ref Guid filterAttributes)
        {
        }

        public void RequestAttrsAtPosition(int index, uint cFilterAttrs, ref Guid filterAttributes, int flags)
        {
            throw new COMException("", Result.NotImplemented.Code);
        }


        public void RequestAttrsTransitioningAtPosition(int position, uint cFilterAttrs, ref Guid filterAttributes, int flags)
        {
            throw new COMException("", Result.NotImplemented.Code);
        }

        public void FindNextAttrTransition(int startIndex, int haltIndex, uint cFilterAttrs, ref Guid filterAttributes, int flags, out int acpNext, out RawBool found, out int foundOffset)
        {
            acpNext = 0;
            found = false;
            foundOffset = 0;
        }

        public uint RetrieveRequestedAttrs(uint ulCount, ref TsfSharp.TsAttrval aAttrValsRef)
        {
            return 0;
        }

        public int GetEndACP()
        {
            int acp = 0;
            //does the caller have a lock
            if (!_IsLocked(TsLfFlags.Read))
            {
                //the caller doesn't have a lock
                throw new COMException("", (int)TsErrors.TsENolock);
            }

            acp = _inputBuffer.Count;

            return acp;
        }

        public int GetActiveView()
        {
            return _viewCookie;
        }

        public int GetACPFromPoint(int viewCookie, TsfSharp.Point tsfPoint, int dwFlags)
        {
            throw new COMException("", Result.NotImplemented.Code);
        }

        public void GetTextExt(int viewCookie, int acpStart, int acpEnd, out Rect rect, out RawBool clipped)
        {
            clipped = false;
            rect = InputMethod.TextInputRect;

            if (_viewCookie != viewCookie)
                throw new COMException("", Result.InvalidArg.Code);

            //does the caller have a lock
            if (!_IsLocked(TsLfFlags.Read))
            {
                //the caller doesn't have a lock
                throw new COMException("", (int)TsErrors.TsENolock);
            }

            //According to Microsoft's doc, an ime should not make empty request,
            //but some ime draw comp text themseleves, when empty req will be make
            //Check empty request
            //if (acpStart == acpEnd) {
            //	return E_INVALIDARG;
            //}

            NativeMethods.MapWindowPoints(_windowHandle, IntPtr.Zero, ref rect, 2);
        }

        public Rect GetScreenExt(int viewCookie)
        {
            Rect rect = new Rect();

            if (_viewCookie != viewCookie)
                throw new COMException("", Result.InvalidArg.Code);

            NativeMethods.GetWindowRect(_windowHandle, out rect);

            return rect;
        }

        public IntPtr GetWnd(int viewCookie)
        {
            if (viewCookie != _viewCookie)
            {
                throw new COMException("", Result.False.Code);
            }

            return _windowHandle;
        }

        #endregion ITextStoreACP2


        //------------------------------------------------------
        //
        //  Public Methods - ITfContextOwnerCompositionSink
        //
        //------------------------------------------------------

        #region ITfContextOwnerCompositionSink

        public RawBool OnStartComposition(ITfCompositionView view)
        {
            // Return true in ok to start the composition.
            RawBool ok = true;
            _compositionStart = _compositionLength = 0;
            _currentComposition.Clear();

            InputMethod.OnTextCompositionStarted(this);
            _compViews.Add(view);

            return ok;
        }

        public void OnUpdateComposition(ITfCompositionView view, ITfRange rangeNew)
        {
            var range = view.Range;
            var rangeacp = range.QueryInterface<ITfRangeACP>();

            rangeacp.GetExtent(out _compositionStart, out _compositionLength);
            rangeacp.Dispose();
            range.Dispose();
            _compViews.Add(view);
        }

        public void OnEndComposition(ITfCompositionView view)
        {
            var range = view.Range;
            var rangeacp = range.QueryInterface<ITfRangeACP>();

            rangeacp.GetExtent(out _commitStart, out _commitLength);
            rangeacp.Dispose();
            range.Dispose();

            // Ensure composition string reset
            _compositionStart = _compositionLength = 0;
            _currentComposition.Clear();

            InputMethod.ClearCandidates();
            InputMethod.OnTextCompositionEnded(this);
            view.Dispose();
            foreach(var item in _compViews)
                item.Dispose();
            _compViews.Clear();
        }

        #endregion ITfContextOwnerCompositionSink

        #region ITfTextEditSink

        public void OnEndEdit(ITfContext context, int ecReadOnly, ITfEditRecord editRecord)
        {
            ITfProperty property = context.GetProperty(GUID_PROP_COMPOSING);

            ITfRangeACP rangeACP = TextServicesContext.Current.ContextOwnerServices.CreateRange(_compositionStart, _compositionStart + _compositionLength);
            Variant val = property.GetValue(ecReadOnly, rangeACP);
            property.Dispose();
            rangeACP.Dispose();
            if (val.Value == null || (int)val.Value == 0)
            {
                if (_commitLength == 0 || _inputBuffer.Count == 0)
                    return;

                //Debug.WriteLine("Composition result: {0}", new object[] { new string(_inputBuffer.GetRange(_commitStart, _commitLength).ToArray()) });

                _commited = true;
                for (int i = 0; i < _commitLength; i++)
                    InputMethod.OnTextCompositionResult(this, new string(_inputBuffer.GetRange(_commitStart, _commitLength).ToArray()));
            }

            if (_commited)
                return;

            if (_inputBuffer.Count == 0 && _compositionLength > 0) // Composition just ended
                return;

            _currentComposition.Clear();
            for (int i = 0; i < _compositionLength; i++)
                _currentComposition.Add(_inputBuffer[_compositionStart + i]);

            InputMethod.OnTextComposition(this, new IMEString(_currentComposition), _acpEnd);

            //var compStr = new string(_currentComposition.ToArray());
            //compStr = compStr.Insert(_acpEnd, "|");
            //Debug.WriteLine("Composition string: {0}, cursor pos: {1}", compStr, _acpEnd);
        }

        #endregion ITfTextEditSink

        //------------------------------------------------------
        //
        //  Public Methods - ITfUIElementSink
        //
        //------------------------------------------------------

        #region ITfUIElementSink

        public RawBool BeginUIElement(int dwUIElementId)
        {
            // Hide OS rendered Candidate list Window
            RawBool pbShow = InputMethod.ShowOSImeWindow;

            OnUIElement(dwUIElementId, true);

            return pbShow;
        }

        public void UpdateUIElement(int dwUIElementId)
        {
            OnUIElement(dwUIElementId, false);
        }

        public void EndUIElement(int dwUIElementId)
        {
        }

        private void OnUIElement(int uiElementId, bool onStart)
        {
            if (InputMethod.ShowOSImeWindow || !_supportUIElement) return;

            ITfUIElement uiElement = TextServicesContext.Current.UIElementMgr.GetUIElement(uiElementId);

            ITfCandidateListUIElementBehavior candList;

            try
            {
                candList = uiElement.QueryInterface<ITfCandidateListUIElementBehavior>();
            }
            catch (SharpGenException)
            {
                _supportUIElement = false;
                return;
            }
            finally
            {
                uiElement.Dispose();
            }

            uint selection = 0;
            uint currentPage = 0;
            uint count = 0;
            uint pageCount = 0;
            uint pageStart = 0;
            uint pageSize = 0;
            uint i, j;

            selection = candList.GetSelection();
            currentPage = candList.GetCurrentPage();

            count = candList.GetCount();

            pageCount = candList.GetPageIndex(null, 0);

            if (pageCount > 0)
            {
                uint[] pageStartIndexes = ArrayPool<uint>.Shared.Rent((int)pageCount);
                pageCount = candList.GetPageIndex(pageStartIndexes, pageCount);
                pageStart = pageStartIndexes[currentPage];

                if (pageStart >= count - 1)
                {
                    candList.Abort();
                    ArrayPool<uint>.Shared.Return(pageStartIndexes);
                    return;
                }

                if (currentPage < pageCount - 1)
                    pageSize = Math.Min(count, pageStartIndexes[currentPage + 1]) - pageStart;
                else
                    pageSize = count - pageStart;

                ArrayPool<uint>.Shared.Return(pageStartIndexes);
            }

            selection -= pageStart;

            IMEString[] candidates = _IMEStringPool.Rent((int)pageSize);

            IntPtr bStrPtr;
            for (i = pageStart, j = 0; i < count && j < pageSize; i++, j++)
            {
                bStrPtr = candList.GetString(i);
                candidates[j] = new IMEString(bStrPtr);
            }

            //Debug.WriteLine("TSF========TSF");
            //Debug.WriteLine("pageStart: {0}, pageSize: {1}, selection: {2}, currentPage: {3} candidates:", pageStart, pageSize, selection, currentPage);
            //for (int k = 0; k < candidates.Length; k++)
            //    Debug.WriteLine("  {2}{0}.{1}", k + 1, candidates[k], k == selection ? "*" : "");
            //Debug.WriteLine("TSF++++++++TSF");

            InputMethod.CandidatePageStart = (int)pageStart;
            InputMethod.CandidatePageSize = (int)pageSize;
            InputMethod.CandidateSelection = (int)selection;
            InputMethod.CandidateList = candidates;

            if (_currentComposition != null)
            {
                InputMethod.OnTextComposition(this, new IMEString(_currentComposition), _acpEnd);
                _IMEStringPool.Return(candidates);
            }

            candList.Dispose();
        }

        #endregion ITfUIElementSink

        //------------------------------------------------------
        //
        //  Public Properties
        //
        //------------------------------------------------------

        public static TextStore Current
        {
            get
            {
                TextStore defaultTextStore = InputMethod.DefaultTextStore;
                if (defaultTextStore == null)
                {
                    defaultTextStore = InputMethod.DefaultTextStore = new TextStore(InputMethod.WindowHandle);

                    defaultTextStore.Register();
                }

                return defaultTextStore;
            }
        }

        public ITfDocumentMgr DocumentManager
        {
            get { return _documentMgr; }
            set { _documentMgr = value; }
        }

        // EditCookie for ITfContext.
        public int EditCookie
        {
            // get { return _editCookie; }
            set { _editCookie = value; }
        }

        public int UIElementSinkCookie
        {
            get { return _uiElementSinkCookie; }
            set { _uiElementSinkCookie = value; }
        }

        public int TextEditSinkCookie
        {
            get { return _textEditSinkCookie; }
            set { _textEditSinkCookie = value; }
        }

        public bool SupportUIElement { get { return _supportUIElement; } }


        //------------------------------------------------------
        //
        //  Private Methods
        //
        //------------------------------------------------------

        // This function calls TextServicesContext to create TSF document and start transitory extension.
        private void Register()
        {
            // Create TSF document and advise the sink to it.
            TextServicesContext.Current.RegisterTextStore(this);
        }

        //------------------------------------------------------
        //
        //  Private Fields
        //
        //------------------------------------------------------

        // The TSF document object.  This is a native resource.
        private ITfDocumentMgr _documentMgr;

        private int _viewCookie;

        // The edit cookie TSF returns from CreateContext.
        private int _editCookie;
        private int _uiElementSinkCookie;
        private int _textEditSinkCookie;

        private ITextStoreACPSink _sink;
        private IntPtr _windowHandle;
        private int _acpStart;
        private int _acpEnd;
        private bool _interimChar;
        private TsActiveSelEnd _activeSelectionEnd;
        private List<char> _inputBuffer = new List<char>();

        private bool _locked;
        private TsLfFlags _lockFlags;
        private Queue<TsLfFlags> _lockRequestQueue = new Queue<TsLfFlags>();
        private bool _layoutChanged;

        private List<char> _currentComposition = new List<char>();
        private int _compositionStart;
        private int _compositionLength;
        private int _commitStart;
        private int _commitLength;
        private bool _commited;

        private bool _supportUIElement = true;
        private List<ITfCompositionView> _compViews = new List<ITfCompositionView>();

        private ArrayPool<IMEString> _IMEStringPool;

    }
}
