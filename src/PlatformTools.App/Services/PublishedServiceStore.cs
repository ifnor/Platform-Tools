using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using PlatformTools.App.Models;

namespace PlatformTools.App.Services;

internal sealed class PublishedServiceStore(string path)
{
    public List<PublishedServiceDefinition> Load()
    {
        if (!File.Exists(path)) return [];
        var definitions = JsonSerializer.Deserialize<List<PublishedServiceDefinition>>(File.ReadAllText(path))
            ?? throw new InvalidDataException("服务配置为空或已损坏。");
        var checkedItems = new List<PublishedServiceDefinition>();
        foreach (var definition in definitions)
        {
            if (definition is null || checkedItems.Exists(x => x.Id == definition.Id)) throw new InvalidDataException("服务配置包含重复或无效 ID。");
            checkedItems.Add(definition.NormalizeAndValidate(checkedItems));
        }
        return checkedItems;
    }

    public void Save(IEnumerable<PublishedServiceDefinition> definitions)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(definitions, new JsonSerializerOptions { WriteIndented = true }));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
