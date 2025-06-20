using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Steamworks.Data;


namespace Steamworks
{
	internal unsafe partial class ISteamNetworkingSockets : SteamInterface
	{
		public const string Version = "SteamNetworkingSockets012";
		
		internal ISteamNetworkingSockets( bool IsGameServer )
		{
			SetupInterface( IsGameServer );
		}
		
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_SteamNetworkingSockets_SteamAPI_v012", CallingConvention = Platform.CC)]
		internal static extern IntPtr SteamAPI_SteamNetworkingSockets_SteamAPI_v012();
		public override IntPtr GetUserInterfacePointer() => SteamAPI_SteamNetworkingSockets_SteamAPI_v012();
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_SteamGameServerNetworkingSockets_SteamAPI_v012", CallingConvention = Platform.CC)]
		internal static extern IntPtr SteamAPI_SteamGameServerNetworkingSockets_SteamAPI_v012();
		public override IntPtr GetServerInterfacePointer() => SteamAPI_SteamGameServerNetworkingSockets_SteamAPI_v012();
		
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_CreateListenSocketIP", CallingConvention = Platform.CC)]
		private static extern Socket _CreateListenSocketIP( IntPtr self, ref NetAddress localAddress, int nOptions, [In,Out] NetKeyValue[]  pOptions );
		
		#endregion
		internal Socket CreateListenSocketIP( ref NetAddress localAddress, int nOptions, [In,Out] NetKeyValue[]  pOptions )
		{
			var returnValue = _CreateListenSocketIP( Self, ref localAddress, nOptions, pOptions );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_ConnectByIPAddress", CallingConvention = Platform.CC)]
		private static extern Connection _ConnectByIPAddress( IntPtr self, ref NetAddress address, int nOptions, [In,Out] NetKeyValue[]  pOptions );
		
		#endregion
		internal Connection ConnectByIPAddress( ref NetAddress address, int nOptions, [In,Out] NetKeyValue[]  pOptions )
		{
			var returnValue = _ConnectByIPAddress( Self, ref address, nOptions, pOptions );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_CreateListenSocketP2P", CallingConvention = Platform.CC)]
		private static extern Socket _CreateListenSocketP2P( IntPtr self, int nLocalVirtualPort, int nOptions, [In,Out] NetKeyValue[]  pOptions );
		
		#endregion
		internal Socket CreateListenSocketP2P( int nLocalVirtualPort, int nOptions, [In,Out] NetKeyValue[]  pOptions )
		{
			var returnValue = _CreateListenSocketP2P( Self, nLocalVirtualPort, nOptions, pOptions );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_ConnectP2P", CallingConvention = Platform.CC)]
		private static extern Connection _ConnectP2P( IntPtr self, ref NetIdentity identityRemote, int nRemoteVirtualPort, int nOptions, [In,Out] NetKeyValue[]  pOptions );
		
		#endregion
		internal Connection ConnectP2P( ref NetIdentity identityRemote, int nRemoteVirtualPort, int nOptions, [In,Out] NetKeyValue[]  pOptions )
		{
			var returnValue = _ConnectP2P( Self, ref identityRemote, nRemoteVirtualPort, nOptions, pOptions );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_AcceptConnection", CallingConvention = Platform.CC)]
		private static extern Result _AcceptConnection( IntPtr self, Connection hConn );
		
		#endregion
		internal Result AcceptConnection( Connection hConn )
		{
			var returnValue = _AcceptConnection( Self, hConn );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_CloseConnection", CallingConvention = Platform.CC)]
		[return: MarshalAs( UnmanagedType.I1 )]
		private static extern bool _CloseConnection( IntPtr self, Connection hPeer, int nReason, IntPtr pszDebug, [MarshalAs( UnmanagedType.U1 )] bool bEnableLinger );
		
		#endregion
		internal bool CloseConnection( Connection hPeer, int nReason, string pszDebug, [MarshalAs( UnmanagedType.U1 )] bool bEnableLinger )
		{
			using var str__pszDebug = new Utf8StringToNative( pszDebug );
			var returnValue = _CloseConnection( Self, hPeer, nReason, str__pszDebug.Pointer, bEnableLinger );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_CloseListenSocket", CallingConvention = Platform.CC)]
		[return: MarshalAs( UnmanagedType.I1 )]
		private static extern bool _CloseListenSocket( IntPtr self, Socket hSocket );
		
		#endregion
		internal bool CloseListenSocket( Socket hSocket )
		{
			var returnValue = _CloseListenSocket( Self, hSocket );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_SetConnectionUserData", CallingConvention = Platform.CC)]
		[return: MarshalAs( UnmanagedType.I1 )]
		private static extern bool _SetConnectionUserData( IntPtr self, Connection hPeer, long nUserData );
		
		#endregion
		internal bool SetConnectionUserData( Connection hPeer, long nUserData )
		{
			var returnValue = _SetConnectionUserData( Self, hPeer, nUserData );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_GetConnectionUserData", CallingConvention = Platform.CC)]
		private static extern long _GetConnectionUserData( IntPtr self, Connection hPeer );
		
		#endregion
		internal long GetConnectionUserData( Connection hPeer )
		{
			var returnValue = _GetConnectionUserData( Self, hPeer );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_SetConnectionName", CallingConvention = Platform.CC)]
		private static extern void _SetConnectionName( IntPtr self, Connection hPeer, IntPtr pszName );
		
		#endregion
		internal void SetConnectionName( Connection hPeer, string pszName )
		{
			using var str__pszName = new Utf8StringToNative( pszName );
			_SetConnectionName( Self, hPeer, str__pszName.Pointer );
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_GetConnectionName", CallingConvention = Platform.CC)]
		[return: MarshalAs( UnmanagedType.I1 )]
		private static extern bool _GetConnectionName( IntPtr self, Connection hPeer, IntPtr pszName, int nMaxLen );
		
		#endregion
		internal bool GetConnectionName( Connection hPeer, out string pszName )
		{
			using var mem__pszName = Helpers.TakeMemory();
			var returnValue = _GetConnectionName( Self, hPeer, mem__pszName, (1024 * 32) );
			pszName = Helpers.MemoryToString( mem__pszName );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_SendMessageToConnection", CallingConvention = Platform.CC)]
		private static extern Result _SendMessageToConnection( IntPtr self, Connection hConn, IntPtr pData, uint cbData, int nSendFlags, ref long pOutMessageNumber );
		
		#endregion
		internal Result SendMessageToConnection( Connection hConn, IntPtr pData, uint cbData, int nSendFlags, ref long pOutMessageNumber )
		{
			var returnValue = _SendMessageToConnection( Self, hConn, pData, cbData, nSendFlags, ref pOutMessageNumber );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_SendMessages", CallingConvention = Platform.CC)]
		private static extern void _SendMessages( IntPtr self, int nMessages, NetMsg** pMessages, long* pOutMessageNumberOrResult );
		
		#endregion
		internal void SendMessages( int nMessages, NetMsg** pMessages, long* pOutMessageNumberOrResult )
		{
			_SendMessages( Self, nMessages, pMessages, pOutMessageNumberOrResult );
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_FlushMessagesOnConnection", CallingConvention = Platform.CC)]
		private static extern Result _FlushMessagesOnConnection( IntPtr self, Connection hConn );
		
		#endregion
		internal Result FlushMessagesOnConnection( Connection hConn )
		{
			var returnValue = _FlushMessagesOnConnection( Self, hConn );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_ReceiveMessagesOnConnection", CallingConvention = Platform.CC)]
		private static extern int _ReceiveMessagesOnConnection( IntPtr self, Connection hConn, IntPtr ppOutMessages, int nMaxMessages );
		
		#endregion
		internal int ReceiveMessagesOnConnection( Connection hConn, IntPtr ppOutMessages, int nMaxMessages )
		{
			var returnValue = _ReceiveMessagesOnConnection( Self, hConn, ppOutMessages, nMaxMessages );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_GetConnectionInfo", CallingConvention = Platform.CC)]
		[return: MarshalAs( UnmanagedType.I1 )]
		private static extern bool _GetConnectionInfo( IntPtr self, Connection hConn, ref ConnectionInfo pInfo );
		
		#endregion
		internal bool GetConnectionInfo( Connection hConn, ref ConnectionInfo pInfo )
		{
			var returnValue = _GetConnectionInfo( Self, hConn, ref pInfo );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_GetConnectionRealTimeStatus", CallingConvention = Platform.CC)]
		private static extern Result _GetConnectionRealTimeStatus( IntPtr self, Connection hConn, ref ConnectionStatus pStatus, int nLanes, [In,Out] ConnectionLaneStatus[]?  pLanes );
		
		#endregion
		internal Result GetConnectionRealTimeStatus( Connection hConn, ref ConnectionStatus pStatus, int nLanes, [In,Out] ConnectionLaneStatus[]?  pLanes )
		{
			var returnValue = _GetConnectionRealTimeStatus( Self, hConn, ref pStatus, nLanes, pLanes );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_GetDetailedConnectionStatus", CallingConvention = Platform.CC)]
		private static extern int _GetDetailedConnectionStatus( IntPtr self, Connection hConn, IntPtr pszBuf, int cbBuf );
		
		#endregion
		internal int GetDetailedConnectionStatus( Connection hConn, out string pszBuf )
		{
			using var mem__pszBuf = Helpers.TakeMemory();
			var returnValue = _GetDetailedConnectionStatus( Self, hConn, mem__pszBuf, (1024 * 32) );
			pszBuf = Helpers.MemoryToString( mem__pszBuf );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_GetListenSocketAddress", CallingConvention = Platform.CC)]
		[return: MarshalAs( UnmanagedType.I1 )]
		private static extern bool _GetListenSocketAddress( IntPtr self, Socket hSocket, ref NetAddress address );
		
		#endregion
		internal bool GetListenSocketAddress( Socket hSocket, ref NetAddress address )
		{
			var returnValue = _GetListenSocketAddress( Self, hSocket, ref address );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_CreateSocketPair", CallingConvention = Platform.CC)]
		[return: MarshalAs( UnmanagedType.I1 )]
		private static extern bool _CreateSocketPair( IntPtr self, [In,Out] Connection[]  pOutConnection1, [In,Out] Connection[]  pOutConnection2, [MarshalAs( UnmanagedType.U1 )] bool bUseNetworkLoopback, ref NetIdentity pIdentity1, ref NetIdentity pIdentity2 );
		
		#endregion
		internal bool CreateSocketPair( [In,Out] Connection[]  pOutConnection1, [In,Out] Connection[]  pOutConnection2, [MarshalAs( UnmanagedType.U1 )] bool bUseNetworkLoopback, ref NetIdentity pIdentity1, ref NetIdentity pIdentity2 )
		{
			var returnValue = _CreateSocketPair( Self, pOutConnection1, pOutConnection2, bUseNetworkLoopback, ref pIdentity1, ref pIdentity2 );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_ConfigureConnectionLanes", CallingConvention = Platform.CC)]
		private static extern Result _ConfigureConnectionLanes( IntPtr self, Connection hConn, int nNumLanes, [In,Out] int[]  pLanePriorities, [In,Out] ushort[]  pLaneWeights );
		
		#endregion
		internal Result ConfigureConnectionLanes( Connection hConn, int nNumLanes, [In,Out] int[]  pLanePriorities, [In,Out] ushort[]  pLaneWeights )
		{
			var returnValue = _ConfigureConnectionLanes( Self, hConn, nNumLanes, pLanePriorities, pLaneWeights );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_GetIdentity", CallingConvention = Platform.CC)]
		[return: MarshalAs( UnmanagedType.I1 )]
		private static extern bool _GetIdentity( IntPtr self, ref NetIdentity pIdentity );
		
		#endregion
		internal bool GetIdentity( ref NetIdentity pIdentity )
		{
			var returnValue = _GetIdentity( Self, ref pIdentity );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_InitAuthentication", CallingConvention = Platform.CC)]
		private static extern SteamNetworkingAvailability _InitAuthentication( IntPtr self );
		
		#endregion
		internal SteamNetworkingAvailability InitAuthentication()
		{
			var returnValue = _InitAuthentication( Self );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_GetAuthenticationStatus", CallingConvention = Platform.CC)]
		private static extern SteamNetworkingAvailability _GetAuthenticationStatus( IntPtr self, ref SteamNetAuthenticationStatus_t pDetails );
		
		#endregion
		internal SteamNetworkingAvailability GetAuthenticationStatus( ref SteamNetAuthenticationStatus_t pDetails )
		{
			var returnValue = _GetAuthenticationStatus( Self, ref pDetails );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_CreatePollGroup", CallingConvention = Platform.CC)]
		private static extern HSteamNetPollGroup _CreatePollGroup( IntPtr self );
		
		#endregion
		internal HSteamNetPollGroup CreatePollGroup()
		{
			var returnValue = _CreatePollGroup( Self );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_DestroyPollGroup", CallingConvention = Platform.CC)]
		[return: MarshalAs( UnmanagedType.I1 )]
		private static extern bool _DestroyPollGroup( IntPtr self, HSteamNetPollGroup hPollGroup );
		
		#endregion
		internal bool DestroyPollGroup( HSteamNetPollGroup hPollGroup )
		{
			var returnValue = _DestroyPollGroup( Self, hPollGroup );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_SetConnectionPollGroup", CallingConvention = Platform.CC)]
		[return: MarshalAs( UnmanagedType.I1 )]
		private static extern bool _SetConnectionPollGroup( IntPtr self, Connection hConn, HSteamNetPollGroup hPollGroup );
		
		#endregion
		internal bool SetConnectionPollGroup( Connection hConn, HSteamNetPollGroup hPollGroup )
		{
			var returnValue = _SetConnectionPollGroup( Self, hConn, hPollGroup );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_ReceiveMessagesOnPollGroup", CallingConvention = Platform.CC)]
		private static extern int _ReceiveMessagesOnPollGroup( IntPtr self, HSteamNetPollGroup hPollGroup, IntPtr ppOutMessages, int nMaxMessages );
		
		#endregion
		internal int ReceiveMessagesOnPollGroup( HSteamNetPollGroup hPollGroup, IntPtr ppOutMessages, int nMaxMessages )
		{
			var returnValue = _ReceiveMessagesOnPollGroup( Self, hPollGroup, ppOutMessages, nMaxMessages );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_ReceivedRelayAuthTicket", CallingConvention = Platform.CC)]
		[return: MarshalAs( UnmanagedType.I1 )]
		private static extern bool _ReceivedRelayAuthTicket( IntPtr self, IntPtr pvTicket, int cbTicket, [In,Out] SteamDatagramRelayAuthTicket[]  pOutParsedTicket );
		
		#endregion
		internal bool ReceivedRelayAuthTicket( IntPtr pvTicket, int cbTicket, [In,Out] SteamDatagramRelayAuthTicket[]  pOutParsedTicket )
		{
			var returnValue = _ReceivedRelayAuthTicket( Self, pvTicket, cbTicket, pOutParsedTicket );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_FindRelayAuthTicketForServer", CallingConvention = Platform.CC)]
		private static extern int _FindRelayAuthTicketForServer( IntPtr self, ref NetIdentity identityGameServer, int nRemoteVirtualPort, [In,Out] SteamDatagramRelayAuthTicket[]  pOutParsedTicket );
		
		#endregion
		internal int FindRelayAuthTicketForServer( ref NetIdentity identityGameServer, int nRemoteVirtualPort, [In,Out] SteamDatagramRelayAuthTicket[]  pOutParsedTicket )
		{
			var returnValue = _FindRelayAuthTicketForServer( Self, ref identityGameServer, nRemoteVirtualPort, pOutParsedTicket );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_ConnectToHostedDedicatedServer", CallingConvention = Platform.CC)]
		private static extern Connection _ConnectToHostedDedicatedServer( IntPtr self, ref NetIdentity identityTarget, int nRemoteVirtualPort, int nOptions, [In,Out] NetKeyValue[]  pOptions );
		
		#endregion
		internal Connection ConnectToHostedDedicatedServer( ref NetIdentity identityTarget, int nRemoteVirtualPort, int nOptions, [In,Out] NetKeyValue[]  pOptions )
		{
			var returnValue = _ConnectToHostedDedicatedServer( Self, ref identityTarget, nRemoteVirtualPort, nOptions, pOptions );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_GetHostedDedicatedServerPort", CallingConvention = Platform.CC)]
		private static extern ushort _GetHostedDedicatedServerPort( IntPtr self );
		
		#endregion
		internal ushort GetHostedDedicatedServerPort()
		{
			var returnValue = _GetHostedDedicatedServerPort( Self );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_GetHostedDedicatedServerPOPID", CallingConvention = Platform.CC)]
		private static extern SteamNetworkingPOPID _GetHostedDedicatedServerPOPID( IntPtr self );
		
		#endregion
		internal SteamNetworkingPOPID GetHostedDedicatedServerPOPID()
		{
			var returnValue = _GetHostedDedicatedServerPOPID( Self );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_GetHostedDedicatedServerAddress", CallingConvention = Platform.CC)]
		private static extern Result _GetHostedDedicatedServerAddress( IntPtr self, ref SteamDatagramHostedAddress pRouting );
		
		#endregion
		internal Result GetHostedDedicatedServerAddress( ref SteamDatagramHostedAddress pRouting )
		{
			var returnValue = _GetHostedDedicatedServerAddress( Self, ref pRouting );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_CreateHostedDedicatedServerListenSocket", CallingConvention = Platform.CC)]
		private static extern Socket _CreateHostedDedicatedServerListenSocket( IntPtr self, int nLocalVirtualPort, int nOptions, [In,Out] NetKeyValue[]  pOptions );
		
		#endregion
		internal Socket CreateHostedDedicatedServerListenSocket( int nLocalVirtualPort, int nOptions, [In,Out] NetKeyValue[]  pOptions )
		{
			var returnValue = _CreateHostedDedicatedServerListenSocket( Self, nLocalVirtualPort, nOptions, pOptions );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_GetGameCoordinatorServerLogin", CallingConvention = Platform.CC)]
		private static extern Result _GetGameCoordinatorServerLogin( IntPtr self, ref SteamDatagramGameCoordinatorServerLogin pLoginInfo, ref int pcbSignedBlob, IntPtr pBlob );
		
		#endregion
		internal Result GetGameCoordinatorServerLogin( ref SteamDatagramGameCoordinatorServerLogin pLoginInfo, ref int pcbSignedBlob, IntPtr pBlob )
		{
			var returnValue = _GetGameCoordinatorServerLogin( Self, ref pLoginInfo, ref pcbSignedBlob, pBlob );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_ConnectP2PCustomSignaling", CallingConvention = Platform.CC)]
		private static extern Connection _ConnectP2PCustomSignaling( IntPtr self, IntPtr pSignaling, ref NetIdentity pPeerIdentity, int nRemoteVirtualPort, int nOptions, [In,Out] NetKeyValue[]  pOptions );
		
		#endregion
		internal Connection ConnectP2PCustomSignaling( IntPtr pSignaling, ref NetIdentity pPeerIdentity, int nRemoteVirtualPort, int nOptions, [In,Out] NetKeyValue[]  pOptions )
		{
			var returnValue = _ConnectP2PCustomSignaling( Self, pSignaling, ref pPeerIdentity, nRemoteVirtualPort, nOptions, pOptions );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_ReceivedP2PCustomSignal", CallingConvention = Platform.CC)]
		[return: MarshalAs( UnmanagedType.I1 )]
		private static extern bool _ReceivedP2PCustomSignal( IntPtr self, IntPtr pMsg, int cbMsg, IntPtr pContext );
		
		#endregion
		internal bool ReceivedP2PCustomSignal( IntPtr pMsg, int cbMsg, IntPtr pContext )
		{
			var returnValue = _ReceivedP2PCustomSignal( Self, pMsg, cbMsg, pContext );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_GetCertificateRequest", CallingConvention = Platform.CC)]
		[return: MarshalAs( UnmanagedType.I1 )]
		private static extern bool _GetCertificateRequest( IntPtr self, ref int pcbBlob, IntPtr pBlob, ref NetErrorMessage errMsg );
		
		#endregion
		internal bool GetCertificateRequest( ref int pcbBlob, IntPtr pBlob, ref NetErrorMessage errMsg )
		{
			var returnValue = _GetCertificateRequest( Self, ref pcbBlob, pBlob, ref errMsg );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_SetCertificate", CallingConvention = Platform.CC)]
		[return: MarshalAs( UnmanagedType.I1 )]
		private static extern bool _SetCertificate( IntPtr self, IntPtr pCertificate, int cbCertificate, ref NetErrorMessage errMsg );
		
		#endregion
		internal bool SetCertificate( IntPtr pCertificate, int cbCertificate, ref NetErrorMessage errMsg )
		{
			var returnValue = _SetCertificate( Self, pCertificate, cbCertificate, ref errMsg );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_ResetIdentity", CallingConvention = Platform.CC)]
		private static extern void _ResetIdentity( IntPtr self, ref NetIdentity pIdentity );
		
		#endregion
		internal void ResetIdentity( ref NetIdentity pIdentity )
		{
			_ResetIdentity( Self, ref pIdentity );
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_RunCallbacks", CallingConvention = Platform.CC)]
		private static extern void _RunCallbacks( IntPtr self );
		
		#endregion
		internal void RunCallbacks()
		{
			_RunCallbacks( Self );
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_BeginAsyncRequestFakeIP", CallingConvention = Platform.CC)]
		[return: MarshalAs( UnmanagedType.I1 )]
		private static extern bool _BeginAsyncRequestFakeIP( IntPtr self, int nNumPorts );
		
		#endregion
		internal bool BeginAsyncRequestFakeIP( int nNumPorts )
		{
			var returnValue = _BeginAsyncRequestFakeIP( Self, nNumPorts );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_GetFakeIP", CallingConvention = Platform.CC)]
		private static extern void _GetFakeIP( IntPtr self, int idxFirstPort, ref SteamNetworkingFakeIPResult_t pInfo );
		
		#endregion
		internal void GetFakeIP( int idxFirstPort, ref SteamNetworkingFakeIPResult_t pInfo )
		{
			_GetFakeIP( Self, idxFirstPort, ref pInfo );
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_CreateListenSocketP2PFakeIP", CallingConvention = Platform.CC)]
		private static extern Socket _CreateListenSocketP2PFakeIP( IntPtr self, int idxFakePort, int nOptions, [In,Out] NetKeyValue[]  pOptions );
		
		#endregion
		internal Socket CreateListenSocketP2PFakeIP( int idxFakePort, int nOptions, [In,Out] NetKeyValue[]  pOptions )
		{
			var returnValue = _CreateListenSocketP2PFakeIP( Self, idxFakePort, nOptions, pOptions );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_GetRemoteFakeIPForConnection", CallingConvention = Platform.CC)]
		private static extern Result _GetRemoteFakeIPForConnection( IntPtr self, Connection hConn, [In,Out] NetAddress[]  pOutAddr );
		
		#endregion
		internal Result GetRemoteFakeIPForConnection( Connection hConn, [In,Out] NetAddress[]  pOutAddr )
		{
			var returnValue = _GetRemoteFakeIPForConnection( Self, hConn, pOutAddr );
			return returnValue;
		}
		
		#region FunctionMeta
		[DllImport( Platform.LibraryName, EntryPoint = "SteamAPI_ISteamNetworkingSockets_CreateFakeUDPPort", CallingConvention = Platform.CC)]
		private static extern IntPtr _CreateFakeUDPPort( IntPtr self, int idxFakeServerPort );
		
		#endregion
		internal IntPtr CreateFakeUDPPort( int idxFakeServerPort )
		{
			var returnValue = _CreateFakeUDPPort( Self, idxFakeServerPort );
			return returnValue;
		}
		
	}
}
