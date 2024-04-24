using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Linq;

namespace DeployAll;

public static class EgsAssistant
{
    private static string BuildToolFilePath
        => Path.Combine(BuildToolExtractRootPath, true switch
        {
            _ when RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                => "Engine/Binaries/Win64/BuildPatchTool.exe",
            _ when RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                => "Engine/Binaries/Linux/BuildPatchTool",
            _ when RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                => "Engine/Binaries/Mac/BuildPatchTool",
            _ => throw new Exception($"Unsupported host platform: {RuntimeInformation.OSDescription}")
        });

    private const string BuildToolExtractRootPath = "Deploy/bin/EpicBuildTool";
    private const string BuildToolConfig = $"{BuildToolExtractRootPath}/epic_build_tool_config.xml";

    private const string CloudDir = $"{BuildToolExtractRootPath}/CloudDir";

    private const string FileIgnoreListPath = $"{BuildToolExtractRootPath}/ignore.txt";
    private const string FileAttributeListPath = $"{BuildToolExtractRootPath}/fileattributes.txt";

    public static void Upload(Version version, string configuration, string revision)
    {
        while (!File.Exists(BuildToolFilePath))
        {
            Directory.CreateDirectory(BuildToolExtractRootPath);
            if (Util.AskQuestion(
                    $"Epic BuildPatchTool not found. Extract it to {BuildToolExtractRootPath}, then enter Y to continue. Enter nothing to cancel.")
                .AnsweredNo())
            {
                return;
            }
        }

        XDocument? cfg = null;
        while (!Util.TryLoadXml(BuildToolConfig, out cfg)
               || cfg.Root!.Attributes().Any(attr => !attr.Value.IsValidEpicCfg()))
        {
            if (!File.Exists(BuildToolConfig))
            {
                var doc = new XDocument(
                    new XElement("config"));
                doc.Root!.Add(new XAttribute("OrganizationId", " ORGANIZATION ID "));
                doc.Root!.Add(new XAttribute("ProductId", " PRODUCT ID "));
                doc.Root!.Add(new XAttribute("ArtifactId", " ARTIFACT ID "));
                doc.Root!.Add(new XAttribute("ClientId", " BUILDPATCHTOOL CLIENT ID "));
                doc.Root!.Add(new XAttribute("ClientSecret", " BUILDPATCHTOOL CLIENT SECRET "));

                var xmlWriterSettings = new XmlWriterSettings
                {
                    Indent = true,
                    OmitXmlDeclaration = true,
                    NewLineOnAttributes = true
                };

                using var writer = XmlWriter.Create(BuildToolConfig, xmlWriterSettings);
                doc.WriteTo(writer);
                writer.Flush();
            }

            if (Util.AskQuestion(
                    $"Parameters for BuildPatchTool are missing or invalid. Fill in {BuildToolConfig} with the appropriate values, then enter Y to continue. Enter nothing to cancel.")
                .AnsweredNo())
            {
                return;
            }
        }

        Directory.CreateDirectory(CloudDir);

        XElement configElement = cfg.Root;
        string organizationId = configElement.GetAttributeOrThrow("OrganizationId");
        string productId = configElement.GetAttributeOrThrow("ProductId");
        string artifactId = configElement.GetAttributeOrThrow("ArtifactId");
        string clientId = configElement.GetAttributeOrThrow("ClientId");
        string clientSecret = configElement.GetAttributeOrThrow("ClientSecret");

        var supportedPlatforms = new (string Platform, string ExecutablePath)[]
        {
            ("Windows", "Barotrauma.exe"),

            // TODO: reevaluate macOS support for the Epic Games Store version of Barotrauma
            // This was dropped because of QA difficulty and missing features on the platform
            // but it may be possible to get it working well enough to be shipped.
            //("Mac", "Barotrauma.app/Contents/MacOS/Barotrauma")
        };

        foreach ((string platform, string executablePath) in supportedPlatforms)
        {
            string RelativeToAbsolute(string relativePath)
                => Path.Combine(Path.GetDirectoryName(executablePath) ?? "", relativePath).NormalizePathSeparators();
            
            var filesToIgnore = new[] { "steam_api64.dll", "libsteam_api64.dylib", "libsteam_api64.so" }
                .Select(RelativeToAbsolute)
                .ToArray();
            File.WriteAllLines(FileIgnoreListPath, filesToIgnore);
            var fileAttributes = platform == "Mac"
                ? new[] { "DedicatedServer" }
                : Array.Empty<string>();
            fileAttributes = fileAttributes
                .Select(RelativeToAbsolute)
                .Select(f => $"\"{f}\" executable")
                .ToArray();
            File.WriteAllLines(FileAttributeListPath, fileAttributes);

            var psi = new ProcessStartInfo
            {
                FileName = BuildToolFilePath,
                ArgumentList =
                {
                    $"-OrganizationId=\"{organizationId}\"",
                    $"-ProductId=\"{productId}\"",
                    $"-ArtifactId=\"{artifactId}\"",
                    $"-ClientId=\"{clientId}\"",
                    $"-ClientSecret=\"{clientSecret}\"",
                    "-mode=UploadBinary",
                    $"-BuildRoot=\"{Path.GetFullPath(Path.Combine(Deployables.ResultPath, platform, "Client")).NormalizePathSeparators()}\"",
                    $"-CloudDir=\"{Path.GetFullPath(CloudDir).NormalizePathSeparators()}\"",
                    $"-BuildVersion=\"{version}-{platform}{configuration}-{revision}\"",
                    $"-AppLaunch=\"{executablePath}\"",
                    "-AppArgs=\"\"",
                    $"-FileIgnoreList={Path.GetFullPath(FileIgnoreListPath).NormalizePathSeparators()}",
                    $"-FileAttributeList={Path.GetFullPath(FileAttributeListPath).NormalizePathSeparators()}"
                },
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };
            var process = Util.StartProcess(psi);
            process.WaitForExit();
        }
    }
}