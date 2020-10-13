using NLog;
using NLog.Targets;
using Barotrauma;
using Microsoft.Xna.Framework;
using Barotrauma.Networking;

[Target("DebugConsole")]
public sealed class DebugConsoleTarget : TargetWithLayout
{
    public DebugConsoleTarget()
    {
    }

    private Color GetColorForMessage(LogEventInfo logEvent)
    {
        if(logEvent.Level == LogLevel.Info)
        {
            return Color.Gray;
        }
        else if(logEvent.Level == LogLevel.Warn)
        {
            return Color.Yellow;
        }
        else if(logEvent.Level == LogLevel.Error)
        {
            return Color.Red;
        }
        return Color.Gray;
    }

    protected override void Write(LogEventInfo logEvent)
    {
        // Both DebugConsole and ServerLog capture anything logged the "old" way, and send it to an NLog logger.
        // Since we're capturing messages from the NLog loggers, we don't want to double-log antyhing that we logged already.
        var shouldLogToConsole = logEvent.LoggerName != DebugConsole.InternalLoggerName;
#if SERVER
        shouldLogToConsole = shouldLogToConsole && logEvent.LoggerName != ServerLog.InternalLoggerName;
#endif
        if (shouldLogToConsole)
        {
            string logMessage = this.Layout.Render(logEvent);

            if (logEvent.Level == LogLevel.Error || logEvent.Level == LogLevel.Fatal)
            {
                DebugConsole.ThrowError(logMessage, logToNLog: false);
            }
            else
            {
                DebugConsole.NewMessage(logMessage, GetColorForMessage(logEvent), logToNLog: false);
            }
        }
    }
}