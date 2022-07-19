#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;

namespace Barotrauma
{
    public class ServerMsgLString : LocalizedString
    {
        private static readonly Regex reFormattedMessage =
            new Regex(@"^(?<variable>[\[\].a-z0-9_]+?)=(?<formatter>[a-z0-9_]+?)\((?<value>.+?)\)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex reReplacedMessage = new Regex(@"^(?<variable>[\[\].a-z0-9_]+?)=(?<message>.*)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly ImmutableDictionary<Identifier, Func<string, string?>> messageFormatters =
            new Dictionary<Identifier, Func<string, string?>>
            {
                {
                    "duration".ToIdentifier(),
                    secondsValue => double.TryParse(secondsValue, out var seconds)
                        ? $"{TimeSpan.FromSeconds(seconds):g}"
                        : null
                }
            }.ToImmutableDictionary();

        private static readonly ImmutableHashSet<char> serverMessageCharacters =
            new [] {'~', '[', ']', '='}.ToImmutableHashSet();

        private readonly string serverMessage;
        private readonly ImmutableArray<string> messageSplit;

        public ServerMsgLString(string serverMsg)
        {
            serverMessage = serverMsg;
            messageSplit = serverMessage.Split("/").ToImmutableArray();
        }

        private static bool IsServerMessageWithVariables(string message) =>
            serverMessageCharacters.All(message.Contains);

        private LoadedSuccessfully loadedSuccessfully = LoadedSuccessfully.Unknown;
        public override bool Loaded
        {
            get
            {
                if (loadedSuccessfully == LoadedSuccessfully.Unknown) { RetrieveValue(); }
                return loadedSuccessfully == LoadedSuccessfully.Yes;
            }
        }

        public override void RetrieveValue()
        {
            Dictionary<string, string> replacedMessages = new Dictionary<string, string>();
            bool translationsFound = false;

            string? TranslateMessage(string input)
            {
                string? message = input;
                if (message.EndsWith("~", StringComparison.Ordinal))
                {
                    message = message.Substring(0, message.Length - 1);
                }

                if (!IsServerMessageWithVariables(message) && !message.Contains('=')) // No variables, try to translate
                {
                    foreach (var replacedMessage in replacedMessages)
                    {
                        message = message.Replace(replacedMessage.Key, replacedMessage.Value);
                    }

                    if (message.Contains(" "))
                    {
                        return message;
                    } // Spaces found, do not translate

                    var msg = TextManager.Get(message);
                    if (msg.Loaded) // If a translation was found, otherwise use the original
                    {
                        message = msg.Value;
                        translationsFound = true;
                    }
                }
                else
                {
                    string? messageVariable = null;
                    var matchFormatted = reFormattedMessage.Match(message);
                    if (matchFormatted.Success)
                    {
                        var formatter = matchFormatted.Groups["formatter"].ToString().ToIdentifier();
                        if (messageFormatters.TryGetValue(formatter, out var formatterFn))
                        {
                            var formattedValue = formatterFn(matchFormatted.Groups["value"].ToString());
                            if (formattedValue != null)
                            {
                                messageVariable = matchFormatted.Groups["variable"].ToString();
                                message = formattedValue;
                            }
                        }
                    }

                    if (messageVariable == null)
                    {
                        var matchReplaced = reReplacedMessage.Match(message);
                        if (matchReplaced.Success)
                        {
                            messageVariable = matchReplaced.Groups["variable"].ToString();
                            message = matchReplaced.Groups["message"].ToString();
                        }
                    }

                    foreach (var replacedMessage in replacedMessages)
                    {
                        message = message.Replace(replacedMessage.Key, replacedMessage.Value);
                    }

                    string[] messageWithVariables = message.Split('~');

                    var msg = TextManager.Get(messageWithVariables[0]);

                    if (msg.Loaded) // If a translation was found, otherwise use the original
                    {
                        message = msg.Value;
                        translationsFound = true;
                    }
                    else if (messageVariable == null)
                    {
                        return message; // No translation found, probably caused by player input -> skip variable handling
                    }

                    // First index is always the message identifier -> start at 1
                    for (int j = 1; j < messageWithVariables.Length; j++)
                    {
                        string[] variableAndValue = messageWithVariables[j].Split('=');
                        message = message.Replace(variableAndValue[0],
                            variableAndValue[1].Length > 1 && variableAndValue[1][0] == '§'
                                ? TextManager.Get(variableAndValue[1].Substring(1)).Value
                                : variableAndValue[1]);
                    }

                    if (messageVariable != null)
                    {
                        replacedMessages[messageVariable] = message;
                        message = null;
                    }
                }

                return message;
            }

            try
            {
                string translatedServerMessage = "";

                for (int i = 0; i < messageSplit.Length; i++)
                {
                    string? message = TranslateMessage(messageSplit[i]);
                    if (message != null)
                    {
                        translatedServerMessage += message;
                    }
                }

                cachedValue = translationsFound ? translatedServerMessage : serverMessage;
                loadedSuccessfully = LoadedSuccessfully.Yes;
            }
            catch (IndexOutOfRangeException exception)
            {
                string errorMsg = $"Failed to translate server message \"{serverMessage}\".";
#if DEBUG
                DebugConsole.ThrowError(errorMsg, exception);
#endif
                GameAnalyticsManager.AddErrorEventOnce($"TextManager.GetServerMessage:{serverMessage}",
                    GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                cachedValue = errorMsg;
                loadedSuccessfully = LoadedSuccessfully.No;
            }

            UpdateLanguage();
        }
    }
}