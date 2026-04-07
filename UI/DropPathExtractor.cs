using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace DropAndForget.UI;

internal static class DropPathExtractor
{
    public static List<string> Extract(IDataTransfer dataObject)
    {
        var paths = ExtractFiles(dataObject.TryGetFiles());
        if (paths.Count > 0)
        {
            return paths;
        }

        return ExtractText(dataObject.TryGetText());
    }

    internal static List<string> ExtractFiles(IEnumerable<IStorageItem>? files)
    {
        var paths = new List<string>();
        if (files is null)
        {
            return paths;
        }

        foreach (var file in files)
        {
            if (!string.IsNullOrWhiteSpace(file.Path.LocalPath))
            {
                paths.Add(file.Path.LocalPath);
            }
        }

        return paths;
    }

    internal static List<string> ExtractText(string? text)
    {
        var paths = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return paths;
        }

        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rawLine.StartsWith('#'))
            {
                continue;
            }

            if (Uri.TryCreate(rawLine, UriKind.Absolute, out var uri) && uri.IsFile)
            {
                paths.Add(uri.LocalPath);
                continue;
            }

            if (Path.IsPathRooted(rawLine))
            {
                paths.Add(rawLine);
            }
        }

        return paths;
    }
}
