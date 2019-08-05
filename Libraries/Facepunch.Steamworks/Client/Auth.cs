using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Facepunch.Steamworks
{
    public partial class Client : IDisposable
    {
        Auth _auth;

        public Auth Auth
        {
            get
            {
                if ( _auth == null )
                    _auth = new Auth(this);

                return _auth;
            }
        }
    }

    /// <summary>
    /// Steam authentication statuses
    /// </summary>
    public enum ClientAuthStatus : int
    {
        OK = 0,
        UserNotConnectedToSteam = 1,
        NoLicenseOrExpired = 2,
        VACBanned = 3,
        LoggedInElseWhere = 4,
        VACCheckTimedOut = 5,
        AuthTicketCanceled = 6,
        AuthTicketInvalidAlreadyUsed = 7,
        AuthTicketInvalid = 8,
        PublisherIssuedBan = 9,
    }

    public enum ClientStartAuthSessionResult : int
    {
        OK = 0,
        InvalidTicket = 1,
        DuplicateRequest = 2,
        InvalidVersion = 3,
        GameMismatch = 4,
        ExpiredTicket = 5,
        ServerNotConnectedToSteam = 6,
    }

    public class Auth
    {
        public Auth(Client c)
        {
            client = c;

            client.RegisterCallback<SteamNative.ValidateAuthTicketResponse_t>(OnAuthTicketValidate);
        }

        void OnAuthTicketValidate(SteamNative.ValidateAuthTicketResponse_t data)
        {
            if (OnAuthChange != null)
                OnAuthChange(data.SteamID, data.OwnerSteamID, (ClientAuthStatus)data.AuthSessionResponse);
        }

        internal Client client;

        public Action<ulong, ulong, ClientAuthStatus> OnAuthChange;

        public class Ticket : IDisposable
        {
            internal Client client;

            public byte[] Data;
            public uint Handle;

            /// <summary>
            /// Cancels a ticket. 
            /// You should cancel your ticket when you close the game or leave a server.
            /// </summary>
            public void Cancel()
            {
                if ( client.IsValid && Handle != 0 )
                {
                    client.native.user.CancelAuthTicket( Handle );
                    Handle = 0;
                    Data = null;
                }
            }

            public void Dispose()
            {
                Cancel();
            }
        }

        /// <summary>
        /// Creates an auth ticket. 
        /// Which you can send to a server to authenticate that you are who you say you are.
        /// </summary>
        public unsafe Ticket GetAuthSessionTicket()
        {
            var data = new byte[1024];

            fixed ( byte* b = data )
            {
                uint ticketLength = 0;
                uint ticket = client.native.user.GetAuthSessionTicket( (IntPtr) b, data.Length, out ticketLength );

                if ( ticket == 0 )
                    return null;

                return new Ticket()
                {
                    client = client,
                    Data = data.Take( (int)ticketLength ).ToArray(),
                    Handle = ticket
                };
            }
        }

        /// <summary>
        /// Start authorizing a ticket. This user isn't authorized yet. Wait for a call to OnAuthChange.
        /// </summary>
        public unsafe ClientStartAuthSessionResult StartSession(byte[] data, ulong steamid)
        {
            fixed (byte* p = data)
            {
                var result = client.native.user.BeginAuthSession((IntPtr)p, data.Length, steamid);

                return (ClientStartAuthSessionResult)result;
            }
        }

        /// <summary>
        /// Forget this guy. They're no longer in the game.
        /// </summary>
        public void EndSession(ulong steamid)
        {
            client.native.user.EndAuthSession(steamid);
        }


    }
}
