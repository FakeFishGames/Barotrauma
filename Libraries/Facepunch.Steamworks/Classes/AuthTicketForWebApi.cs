using System;

namespace Steamworks;

public class AuthTicketForWebApi : IDisposable
{
	public byte[]? Data { get; private set; }
	public uint Handle { get; private set; }

	public bool Canceled { get; private set; }

	public AuthTicketForWebApi( byte[] data, uint handle )
	{
		Data = data;
		Handle = handle;
	}

	/// <summary>
	/// Cancels a ticket. 
	/// You should cancel your ticket when you close the game or leave a server.
	/// </summary>
	public void Cancel()
	{
		if (Handle != 0)
		{
			SteamUser.Internal?.CancelAuthTicket(Handle);
		}

		Handle = 0;
		Data = null;
		Canceled = true;
	}

	public void Dispose()
	{
		Cancel();
	}
}
