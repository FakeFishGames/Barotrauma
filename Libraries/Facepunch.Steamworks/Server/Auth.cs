using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Facepunch.Steamworks
{
    public class ServerAuth
    {
        internal Server server;

        /// <summary>
        /// Steamid, Ownerid, Status
        /// </summary>
        public Action<ulong, ulong, Status> OnAuthChange;

        /// <summary>
        /// Steam authentication statuses
        /// </summary>
        public enum Status : int
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

        public enum StartAuthSessionResult : int
        {
            OK = 0,
            InvalidTicket = 1,
            DuplicateRequest = 2,
            InvalidVersion = 3,
            GameMismatch = 4,
            ExpiredTicket = 5,
        }

        internal ServerAuth( Server s )
        {
            server = s;

            server.RegisterCallback<SteamNative.ValidateAuthTicketResponse_t>( OnAuthTicketValidate );
        }

        void OnAuthTicketValidate( SteamNative.ValidateAuthTicketResponse_t data )
        {
            if ( OnAuthChange != null )
                OnAuthChange( data.SteamID, data.OwnerSteamID, (Status) data.AuthSessionResponse );
        }

        /// <summary>
        /// Start authorizing a ticket. This user isn't authorized yet. Wait for a call to OnAuthChange.
        /// </summary>
        public unsafe StartAuthSessionResult StartSession( byte[] data, ulong steamid )
        {
            fixed ( byte* p = data )
            {
                var result = server.native.gameServer.BeginAuthSession( (IntPtr)p, data.Length, steamid );

                return (StartAuthSessionResult)result;
            }
        }

        /// <summary>
        /// Forget this guy. They're no longer in the game.
        /// </summary>
        public void EndSession( ulong steamid )
        {
            server.native.gameServer.EndAuthSession( steamid );
        }

    }
}
