using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace WarfareTweaks;

internal static class WarfareTweaksDefaultYamlResources
{
    private const string ResourcePrefix = "WarfareTweaks.Resources.Defaults.";

    internal static string Load(string fileName)
    {
        string resourceName = ResourcePrefix + fileName;
        Assembly assembly = typeof(WarfareTweaksDefaultYamlResources).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            throw new InvalidOperationException($"Embedded default YAML resource '{resourceName}' was not found.");
        }

        using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
