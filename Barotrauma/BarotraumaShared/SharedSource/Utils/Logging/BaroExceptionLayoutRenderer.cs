using NLog;
using NLog.LayoutRenderers;
using System.Text;

[LayoutRenderer("exception")]
public class BaroExceptionLayoutRenderer : ExceptionLayoutRenderer
{
    protected override void Append(StringBuilder builder, LogEventInfo logEvent)
    {
        StringBuilder stackTraceBuilder = new StringBuilder();
        base.Append(stackTraceBuilder, logEvent);
        stackTraceBuilder.Replace(AssemblyInfo.ProjectDir, "<DEV>");
        builder.Append(stackTraceBuilder);
    }
}
