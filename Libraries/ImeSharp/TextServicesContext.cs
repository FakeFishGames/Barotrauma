using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Diagnostics;
using ImeSharp.Native;
using TsfSharp;

namespace ImeSharp
{
    //------------------------------------------------------
    //
    //  TextServicesContext class
    //
    //------------------------------------------------------

    /// <summary>
    /// This class manages the ITfThreadMgr, EmptyDim and the reference to
    /// the default TextStore.
    /// </summary>
    /// <remarks>
    /// </remarks>
    internal class TextServicesContext
    {
        public const int TF_POPF_ALL = 0x0001;
        public const int TF_INVALID_COOKIE = -1;
        public static readonly Guid IID_ITfUIElementSink = new Guid(0xea1ea136, 0x19df, 0x11d7, 0xa6, 0xd2, 0x00, 0x06, 0x5b, 0x84, 0x43, 0x5c);
        public static readonly Guid IID_ITfTextEditSink = new Guid(0x8127d409, 0xccd3, 0x4683, 0x96, 0x7a, 0xb4, 0x3d, 0x5b, 0x48, 0x2b, 0xf7);


        public static TextServicesContext Current
        {
            get
            {
                if (InputMethod.TextServicesContext == null)
                    InputMethod.TextServicesContext = new TextServicesContext();

                return InputMethod.TextServicesContext;
            }
        }

        //------------------------------------------------------
        //
        //  Constructors
        //
        //------------------------------------------------------

        #region Constructors

        /// <summary>
        /// Instantiates a TextServicesContext.
        /// </summary>
        private TextServicesContext()
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
                Debug.WriteLine("CRASH: ImeSharp won't work on MTA thread!!!");
        }

        #endregion Constructors

        //------------------------------------------------------
        //
        //  public Methods
        //
        //------------------------------------------------------

        #region public Methods

        /// <summary>
        /// Releases all unmanaged resources allocated by the
        /// TextServicesContext.
        /// </summary>
        /// <remarks>
        /// if appDomainShutdown == false, this method must be called on the
        /// Dispatcher thread.  Otherwise, the caller is an AppDomain.Shutdown
        /// listener, and is calling from a worker thread.
        /// </remarks>
        public void Uninitialize(bool appDomainShutdown)
        {
            // Unregister DefaultTextStore.
            if (_defaultTextStore != null)
            {
                UnadviseSinks();
                if (_defaultTextStore.DocumentManager != null)
                {
                    _defaultTextStore.DocumentManager.Pop(TF_POPF_ALL);
                    _defaultTextStore.DocumentManager.Dispose();
                    _defaultTextStore.DocumentManager = null;
                }

                _defaultTextStore = null;
            }

            // Free up any remaining textstores.
            if (_istimactivated == true)
            {
                // Shut down the thread manager when the last TextStore goes away.
                // On XP, if we're called on a worker thread (during AppDomain shutdown)
                // we can't call call any methods on _threadManager.  The problem is
                // that there's no proxy registered for ITfThreadMgr on OS versions
                // previous to Vista.  Not calling Deactivate will leak the IMEs, but
                // in practice (1) they're singletons, so it's not unbounded; and (2)
                // most applications will share the thread with other AppDomains that
                // have a UI, in which case the IME won't be released until the process
                // shuts down in any case.  In theory we could also work around this
                // problem by creating our own XP proxy/stub implementation, which would
                // be added to WPF setup....
                if (!appDomainShutdown || System.Environment.OSVersion.Version.Major >= 6)
                {
                    _threadManager.Deactivate();
                }
                _istimactivated = false;
            }

            // Release the empty dim.
            if (_dimEmpty != null)
            {
                if (_dimEmpty != null)
                {
                    _dimEmpty.Dispose();
                }
                _dimEmpty = null;
            }

            // Release the ThreadManager.
            // We don't do this in UnregisterTextStore because someone may have
            // called get_ThreadManager after the last TextStore was unregistered.
            if (_threadManager != null)
            {
                if (_threadManager != null)
                {
                    _threadManager.Dispose();
                }
                _threadManager = null;
            }
        }

        // Called by framework's TextStore class.  This method registers a
        // document with TSF.  The TextServicesContext must maintain this list
        // to ensure all native resources are released after gc or uninitialization.
        public void RegisterTextStore(TextStore defaultTextStore)
        {
            _defaultTextStore = defaultTextStore;

            ITfThreadMgrEx threadManager = ThreadManager;

            if (threadManager != null)
            {
                ITfDocumentMgr doc;
                int editCookie = TF_INVALID_COOKIE;

                // Activate TSF on this thread if this is the first TextStore.
                if (_istimactivated == false)
                {
                    //temp variable created to retrieve the value
                    // which is then stored in the critical data.
                    if (InputMethod.ShowOSImeWindow)
                        _clientId = threadManager.Activate();
                    else
                        _clientId = threadManager.ActivateEx(TfTmaeFlags.Uielementenabledonly);

                    _istimactivated = true;
                }

                // Create a TSF document.
                doc = threadManager.CreateDocumentMgr();
                _defaultTextStore.DocumentManager = doc;

                doc.CreateContext(_clientId, 0 /* flags */, _defaultTextStore, out _editContext, out editCookie);
                _defaultTextStore.EditCookie = editCookie;
                _contextOwnerServices = _editContext.QueryInterface<ITfContextOwnerServices>();

                doc.Push(_editContext);

                AdviseSinks();
            }
        }


        public void SetFocusOnDefaultTextStore()
        {
            SetFocusOnDim(TextStore.Current.DocumentManager);
        }

        public void SetFocusOnEmptyDim()
        {
            SetFocusOnDim(EmptyDocumentManager);
        }


        #endregion public Methods

        //------------------------------------------------------
        //
        //  public Properties
        //
        //------------------------------------------------------

        /// <summary>
        /// The default ITfThreadMgrEx object.
        /// </summary>
        public ITfThreadMgrEx ThreadManager
        {
            // The ITfThreadMgr for this thread.
            get
            {
                if (_threadManager == null)
                {
                    ITfThreadMgr threadMgr = null;
                    try
                    {
                        // This might fail in CoreRT
                        threadMgr = Tsf.GetThreadMgr();
                    }
                    catch (SharpGen.Runtime.SharpGenException)
                    {
                        threadMgr = null;
                    }

                    // Dispose previous ITfThreadMgr in case something weird happens
                    if (threadMgr != null)
                    {
                        if (threadMgr.IsThreadFocus)
                            threadMgr.Deactivate();
                        threadMgr.Dispose();
                    }

                    _threadManager = TextServicesLoader.Load();

                    _uiElementMgr = _threadManager.QueryInterface<ITfUIElementMgr>();
                }

                return _threadManager;
            }
        }

        /// <summary>
        /// Return the created ITfContext object.
        /// </summary>
        public ITfContext EditContext
        {
            get { return _editContext; }
        }

        /// <summary>
        /// Return the created ITfUIElementMgr object.
        /// </summary>
        public ITfUIElementMgr UIElementMgr
        {
            get { return _uiElementMgr; }
        }

        /// <summary>
        /// Return the created ITfContextOwnerServices object.
        /// </summary>
        public ITfContextOwnerServices ContextOwnerServices
        {
            get { return _contextOwnerServices; }
        }

        //------------------------------------------------------
        //
        //  public Events
        //
        //------------------------------------------------------

        //------------------------------------------------------
        //
        //  Private Methods
        //
        //------------------------------------------------------

        private void SetFocusOnDim(ITfDocumentMgr dim)
        {
            ITfThreadMgrEx threadmgr = ThreadManager;

            if (threadmgr != null)
            {
                ITfDocumentMgr prevDocMgr = threadmgr.AssociateFocus(InputMethod.WindowHandle, dim);
            }
        }

        private void AdviseSinks()
        {
            var source = _uiElementMgr.QueryInterface<ITfSource>();
            var guid = IID_ITfUIElementSink;
            int sinkCookie = source.AdviseSink(guid, _defaultTextStore);
            _defaultTextStore.UIElementSinkCookie = sinkCookie;
            source.Dispose();

            source = _editContext.QueryInterface<ITfSource>();
            guid = IID_ITfTextEditSink;
            sinkCookie = source.AdviseSink(guid, _defaultTextStore);
            _defaultTextStore.TextEditSinkCookie = sinkCookie;
            source.Dispose();
        }

        private void UnadviseSinks()
        {
            var source = _uiElementMgr.QueryInterface<ITfSource>();

            if (_defaultTextStore.UIElementSinkCookie != TF_INVALID_COOKIE)
            {
                source.UnadviseSink(_defaultTextStore.UIElementSinkCookie);
                _defaultTextStore.UIElementSinkCookie = TF_INVALID_COOKIE;
            }
            source.Dispose();

            source = _editContext.QueryInterface<ITfSource>();
            if (_defaultTextStore.TextEditSinkCookie != TF_INVALID_COOKIE)
            {
                source.UnadviseSink(_defaultTextStore.TextEditSinkCookie);
                _defaultTextStore.TextEditSinkCookie = TF_INVALID_COOKIE;
            }
            source.Dispose();
        }

        //------------------------------------------------------
        //
        //  Private Properties
        //
        //------------------------------------------------------

        // Create an empty dim on demand.
        private ITfDocumentMgr EmptyDocumentManager
        {
            get
            {
                if (_dimEmpty == null)
                {
                    ITfThreadMgrEx threadManager = ThreadManager;
                    if (threadManager == null)
                    {
                        return null;
                    }

                    ITfDocumentMgr dimEmptyTemp;
                    // Create a TSF document.
                    dimEmptyTemp = threadManager.CreateDocumentMgr();
                    _dimEmpty = dimEmptyTemp;
                }
                return _dimEmpty;
            }
        }


        //------------------------------------------------------
        //
        //  Private Fields
        //
        //------------------------------------------------------

        #region Private Fields

        private TextStore _defaultTextStore;

        private ITfContext _editContext;
        private ITfUIElementMgr _uiElementMgr;
        private ITfContextOwnerServices _contextOwnerServices;

        // This is true if thread manager is activated.
        private bool _istimactivated;

        // The root TSF object, created on demand.
        private ITfThreadMgrEx _threadManager;

        // TSF ClientId from Activate call.
        private int _clientId;

        // The empty dim for this thread. Created on demand.
        private ITfDocumentMgr _dimEmpty;

        #endregion Private Fields
    }
}
