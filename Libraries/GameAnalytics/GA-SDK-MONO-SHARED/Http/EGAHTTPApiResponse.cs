namespace GameAnalyticsSDK.Net.Http
{
	internal enum EGAHTTPApiResponse
	{
		// client
		NoResponse,
		BadResponse,
		RequestTimeout, // 408
		JsonEncodeFailed,
		JsonDecodeFailed,
		// server
		InternalServerError,
		BadRequest, // 400
		Unauthorized, // 401
		UnknownResponseCode,
		Ok
	}
}

