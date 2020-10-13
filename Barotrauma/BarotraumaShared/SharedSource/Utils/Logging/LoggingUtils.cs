using Barotrauma.IO;
using Microsoft.Xna.Framework;
using NLog;
using NLog.LayoutRenderers;
using NLog.Targets;
using NLog.Targets.Wrappers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Barotrauma
{
    static class LoggingUtils
    {
        private static readonly string rootLogsDirectory = "Logs";
        private static readonly string eventErrorLogDirectory = rootLogsDirectory + "\\EventErrors";

        public static void InitializeLogging()
        {
#if DEBUG
            GlobalDiagnosticsContext.Set("release", "Debug");
#else
            GlobalDiagnosticsContext.Set("release", "Release");
#endif

#if CLIENT
            GlobalDiagnosticsContext.Set("project", "Client");
#else
            GlobalDiagnosticsContext.Set("project", "Server");
#endif
            GlobalDiagnosticsContext.Set("serverName", "UNKNOWN");

            Target.Register("DebugConsole", typeof(DebugConsoleTarget));
            LayoutRenderer.Register("exception", typeof(BaroExceptionLayoutRenderer));
            LayoutRenderer.Register("stacktrace", typeof(BaroStackTraceLayoutRenderer));
        }

        public static void ShutdownLogging()
        {
            LogManager.Shutdown();
        }

        public static bool IsNLogConfigLoaded()
        {
            return LogManager.Configuration.AllTargets.Count > 0;
        }

        public static MemoryTarget GetCrashDumpRecorder()
        {
            Target target = LogManager.Configuration.FindTargetByName("crashdumprecorder");
            if (target == null)
            {
                return null;
            }

            WrapperTargetBase wrapperTarget = target as WrapperTargetBase;

            // Unwrap the target if necessary.
            if (wrapperTarget == null)
            {
                return target as MemoryTarget;
            }
            else
            {
                return wrapperTarget.WrappedTarget as MemoryTarget;
            }
        }

        public static void AppendLastLogMessages(StringBuilder sb)
        {
            var target = LoggingUtils.GetCrashDumpRecorder();
            if (target == null)
            {
                sb.AppendLine("Unable to find or cast crashdumprecorder target! Check the NLog config. Defaulting to reading DebugConsole messages.");
                try
                {
#if SERVER
                    DebugConsole.Clear();
#else
                    DebugConsole.DequeueMessages();
#endif
                }
                catch(Exception e)
                {
                    sb.AppendLine("Exception encountered while attempting to dequeue remaining messages: " + e.ToString());
                }

                for (int i = DebugConsole.Messages.Count - 1; i > 0 && i > DebugConsole.Messages.Count - 50; i--)
                {
                    sb.AppendLine("   " + DebugConsole.Messages[i].Time + " - " + DebugConsole.Messages[i].Text);
                }
            }
            else
            {
                foreach (var message in target.Logs.Reverse())
                {
                    sb.AppendLine("   " + message);
                }
            }
        }

        public static void SetIncomingServerName(string serverName)
        {
            GlobalDiagnosticsContext.Set("serverName", ToolBox.RemoveInvalidFileNameChars(serverName));
        }

        public static void WriteEventErrorLog(string fileName, StringBuilder sb)
        {
            if(!Directory.Exists(eventErrorLogDirectory))
            {
                try
                {
                    Directory.CreateDirectory(eventErrorLogDirectory);
                }
                catch(Exception e)
                {
                    DebugConsole.ThrowError("Unable to create directory for saving event error log.", e);
                    return;
                }
            }

            var sanitizedFileName = ToolBox.RemoveInvalidFileNameChars(fileName);
            string fileNameNoExt = Path.GetFileNameWithoutExtension(sanitizedFileName);
            string fileExt = Path.GetExtension(sanitizedFileName);

            var filePath = Path.Combine(eventErrorLogDirectory, sanitizedFileName);

            var copyNumber = 1;
            while (File.Exists(filePath))
            {
                filePath = Path.Combine(eventErrorLogDirectory, fileNameNoExt + " (" + copyNumber++ + ")" + fileExt);
            }

            try
            {
                File.WriteAllText(filePath, sb.ToString());
            }
            catch(Exception e)
            {
                DebugConsole.ThrowError("Unable to write event error log " + filePath, e);
            }
        }

        private static string ToRelativePath(string fullPath, string basePath)
        {
            if(fullPath.StartsWith(basePath))
            {
                return fullPath.Substring(basePath.Length);
            }
            return fullPath;
        }

        private static void RecursiveDelete(System.IO.DirectoryInfo baseDir, string currentDir)
        {
            if (!baseDir.Exists)
                return;

            if (!Validation.CanWrite(baseDir.FullName))
            {
                DebugConsole.ThrowError("Can't delete directory (failed validation): " + ToRelativePath(baseDir.FullName, currentDir), logToNLog: false);
                return;
            }

            var subDirectories = new System.IO.DirectoryInfo[0];
            try
            {
                subDirectories = baseDir.GetDirectories();
            }
            catch(Exception e)
            {
                DebugConsole.ThrowError("Unable to list subdirectories in directory " + ToRelativePath(baseDir.FullName, currentDir) + ": " + e.Message, logToNLog: false);
            }

            foreach (var dir in subDirectories)
            {
                RecursiveDelete(dir, currentDir);
            }

            var files = new System.IO.FileInfo[0];
            try
            {
                files = baseDir.GetFiles();
            }
            catch(Exception e)
            {
                DebugConsole.ThrowError("Unable to list files in directory " + ToRelativePath(baseDir.FullName, currentDir) + ": " + e.Message, logToNLog: false);
            }
            foreach (var file in files)
            {
                try
                {
                    file.Delete();
                    DebugConsole.NewMessage("Deleted file " + ToRelativePath(file.FullName, currentDir), color: Color.Green, logToNLog: false);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Unable to delete file " + ToRelativePath(file.FullName, currentDir) + ": " + e.Message, logToNLog: false);
                }
            }

            try
            {
                baseDir.Delete(true);
                DebugConsole.NewMessage("Deleted directory " + ToRelativePath(baseDir.FullName, currentDir), color: Color.Green, logToNLog: false);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Unable to delete directory " + ToRelativePath(baseDir.FullName, currentDir) + ": " + e.Message, logToNLog: false);
            }
        }

        public static void DeleteLogsDirectory()
        {
            LogManager.DisableLogging();
            LogManager.Flush();

            var currentDir = Directory.GetCurrentDirectory();

            System.IO.DirectoryInfo baseDir = new System.IO.DirectoryInfo(rootLogsDirectory);

            DebugConsole.NewMessage("Attempting to delete logging directory " + baseDir.FullName, Color.White, logToNLog: false);

            RecursiveDelete(baseDir, currentDir);

            LogManager.EnableLogging();
        }
    }
}
