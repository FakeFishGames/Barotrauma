using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Xml.Linq;

namespace DeployAll;

public static class Util
{
    public static void DeleteFiles(string path, params string[] patterns)
    {
        foreach (var file in patterns.SelectMany(p => Directory.GetFiles(path, p, SearchOption.AllDirectories)))
        {
            File.Delete(file);
            string dir = file;
            do
            {
                dir = Path.GetDirectoryName(dir) ?? "";
                if (Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length == 0)
                {
                    Directory.Delete(dir, recursive: false);
                }
                else
                {
                    break;
                }
            } while (dir.LastIndexOf('/') > 0);
        }
    }
    
    public static void CopyDirectory(string sourceDir, string destinationDir)
    {
        var dir = new DirectoryInfo(sourceDir);

        DirectoryInfo[] dirs = dir.GetDirectories();

        Directory.CreateDirectory(destinationDir);

        foreach (FileInfo file in dir.GetFiles())
        {
            string targetFilePath = Path.Combine(destinationDir, file.Name);
            file.CopyTo(targetFilePath);
        }

        foreach (DirectoryInfo subDir in dirs)
        {
            string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
            CopyDirectory(subDir.FullName, newDestinationDir);
        }
    }
    
    public static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    public static void RecreateDirectory(string path)
    {
        DeleteDirectory(path);
        Directory.CreateDirectory(path);
    }

    public static IReadOnlyList<byte> DownloadFile(string url, out string finalUrl)
    {
        finalUrl = url;

        using var httpClient = new HttpClient();
        var response = httpClient.Send(new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(url)));
        finalUrl = response.RequestMessage?.RequestUri?.AbsoluteUri ?? url;
        using var stream = response.Content.ReadAsStream();

        using var reader = new BinaryReader(stream);
        var contents = new List<byte>();
        while (true)
        {
            byte[] bytesRead = reader.ReadBytes(1024);
            if (bytesRead.Length == 0) { break; }
            contents.AddRange(bytesRead);
        }

        return contents;
    }

    public static string AskQuestion(string question)
    {
        Console.WriteLine(question);
        Console.Write("> ");
        string answer = Console.ReadLine() ?? "";
        Console.WriteLine("");
        return answer;
    }

    public static bool AnsweredYes(this string answer)
        => answer.Equals("y", StringComparison.InvariantCulture);

    public static bool AnsweredNo(this string answer)
        => !answer.AnsweredYes();

    public static bool IsValidEpicCfg(this char c)
        => char.IsDigit(c)
           || c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '-' or '_' or '+' or '/';

    public static bool IsValidEpicCfg(this string s)
        => !string.IsNullOrEmpty(s) && s.All(IsValidEpicCfg);

    public static bool TryLoadXml(string path, [NotNullWhen(returnValue: true)]out XDocument? doc)
    {
        try
        {
            doc = XDocument.Load(path);
            return true;
        }
        catch
        {
            doc = null;
            return false;
        }
    }

    public static string GetAttributeOrThrow(this XElement element, string attributeName)
    {
        var attribute = element
            .Attributes()
            .FirstOrDefault(e => e.Name.LocalName.Equals(attributeName, StringComparison.OrdinalIgnoreCase));
        if (attribute != null
            && !string.IsNullOrEmpty(attribute.Value))
        {
            return attribute.Value;
        }

        throw new Exception($"{attributeName} is not set");
    }

    public static string ThrowIfNullOrEmpty(this string? s, string msg)
    {
        if (string.IsNullOrEmpty(s)) { throw new Exception(msg); }
        return s;
    }

    public static string NormalizePathSeparators(this string s)
        => s.Replace("\\", "/");

    public static Process StartProcess(ProcessStartInfo info)
        => Process.Start(info)
            ?? throw new Exception($"Failed to start process \"{info.FileName}\"");
}