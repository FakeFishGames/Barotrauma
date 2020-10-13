using NLog;
using NLog.LayoutRenderers;
using System.Text;

[LayoutRenderer("exception")]
public class BaroStackTraceLayoutRenderer : StackTraceLayoutRenderer
{
    protected override void Append(StringBuilder builder, LogEventInfo logEvent)
    {
        StringBuilder exceptionBuilder = new StringBuilder();
        base.Append(exceptionBuilder, logEvent);
        exceptionBuilder.Replace(AssemblyInfo.ProjectDir, "<DEV>");
        builder.Append(exceptionBuilder);
    }
}
