using Barotrauma.Networking;
using System;
using System.Text.RegularExpressions;

namespace Barotrauma;

[Flags]
public enum ForceSayOptions
{
    None = 0,
    RemoveQuotes = 1,
    RemoveSquaresBrackets = 2
}

public interface ISpeakable
{
    void ForceSay(LocalizedString messageToSay, bool sayInRadio, ForceSayOptions options = ForceSayOptions.None, bool ignoreSpeechImpediments = false, float delay = 0.0f, Client ignoredClient = null);

    private static readonly Regex RemoveBracketsRegex = new Regex(@"\[.*?\]", RegexOptions.Compiled);
    public static LocalizedString ParseMessage(LocalizedString messageToSay, ForceSayOptions options)
    {
        if (options.HasFlag(ForceSayOptions.RemoveQuotes))
        {
            messageToSay = new TrimQuotesLString(messageToSay);
        }

        if (options.HasFlag(ForceSayOptions.RemoveSquaresBrackets))
        {
            messageToSay = new RegexReplaceLString(messageToSay, RemoveBracketsRegex, string.Empty);
        }

        return messageToSay;
    }
}