using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace PlatformTools.App.Services;

internal sealed record UpdateFile(string Name, string Sha256);
internal sealed record UpdatePlan(string Target, string Payload, string Backup, string ResultPath, int ParentId, long ParentStartTicks, UpdateFile[] Files);

internal static class AppUpdateInstaller
{
    internal static bool AllowedFile(string name) => name == "PlatformTools.exe" || name == "THIRD_PARTY_NOTICES.md" || name == "tools/cloudflared.exe" ||
        !name.Contains('/') && !name.Contains('\\') && !name.Contains(':') && (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase));

    internal static UpdateFile[] Extract(string archivePath, string payload)
    {
        Directory.CreateDirectory(payload);
        using var zip = ZipFile.OpenRead(archivePath);
        var files = new List<UpdateFile>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name == "tools/" && entry.Length == 0) continue;
            if (!AllowedFile(name) || !names.Add(name) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("更新包包含不允许的路径或重复文件。");
            total += entry.Length;
            if (entry.Length > 512L * 1024 * 1024 || total > 1024L * 1024 * 1024) throw new InvalidDataException("更新包解压大小异常。");
            var destination = Path.Combine(payload, name);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: false);
            using var stream = File.OpenRead(destination);
            files.Add(new(name, Convert.ToHexString(SHA256.HashData(stream))));
        }
        if (!names.Contains("PlatformTools.exe") || !names.Contains("tools/cloudflared.exe")) throw new InvalidDataException("更新包缺少主程序或 cloudflared。");
        return files.ToArray();
    }

    public static string Prepare(AppUpdateDownload download)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var directory = Path.GetDirectoryName(download.FilePath)!;
        var payload = Path.Combine(directory, "payload");
        var files = Extract(download.FilePath, payload);
        var currentExe = Environment.ProcessPath ?? throw new InvalidOperationException("无法定位当前程序。");
        if (!Path.GetFileName(currentExe).Equals("PlatformTools.exe", StringComparison.OrdinalIgnoreCase) || File.Exists(Path.Combine(AppContext.BaseDirectory, "PlatformTools.runtimeconfig.json"))) throw new InvalidOperationException("开发运行模式不支持自动安装，请使用正式发布包。");
        using var process = Process.GetCurrentProcess();
        var target = Path.GetFullPath(AppContext.BaseDirectory);
        var probe = Path.Combine(target, ".update-probe-" + Guid.NewGuid().ToString("N"));
        using (File.Create(probe)) { }
        File.Delete(probe);
        var workerDirectory = Path.Combine(directory, "worker"); Directory.CreateDirectory(workerDirectory);
        File.Copy(currentExe, Path.Combine(workerDirectory, "PlatformTools.exe"));
        var plan = new UpdatePlan(target, payload, Path.Combine(directory, "backup"), Path.Combine(AppPaths.Current.ConfigDirectory, "update-result.txt"), process.Id, process.StartTime.ToUniversalTime().Ticks, files);
        var planPath = Path.Combine(directory, "plan.json");
        File.WriteAllText(planPath, JsonSerializer.Serialize(plan));
        return planPath;
    }

    public static void Launch(string planPath)
    {
        var worker = Path.Combine(Path.GetDirectoryName(planPath)!, "worker", "PlatformTools.exe");
        var info = new ProcessStartInfo(worker) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(worker)! };
        info.ArgumentList.Add("--apply-app-update"); info.ArgumentList.Add(planPath);
        using var child = Process.Start(info) ?? throw new InvalidOperationException("无法启动软件更新程序。");
    }

    internal static void Apply(UpdatePlan plan, Action<string, string>? copy = null)
    {
        copy ??= (source, destination) => File.Copy(source, destination, overwrite: true);
        if (plan.Files.Length == 0 || !plan.Files.Any(f => f.Name == "PlatformTools.exe")) throw new InvalidDataException("更新文件列表无效。");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in plan.Files)
        {
            if (!AllowedFile(file.Name) || !names.Add(file.Name)) throw new InvalidDataException("更新路径无效。");
            using var stream = File.OpenRead(Path.Combine(plan.Payload, file.Name));
            if (Convert.ToHexString(SHA256.HashData(stream)) != file.Sha256) throw new InvalidDataException("待安装文件校验失败。");
            var destination = Path.Combine(plan.Target, file.Name);
            for (var directory = new DirectoryInfo(Path.GetDirectoryName(destination)!); directory is not null; directory = directory.Parent)
                if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("自动更新不支持链接目录。");
            if (File.Exists(destination) && File.GetAttributes(destination).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("自动更新不支持链接文件。");
        }
        var backedUp = new List<string>(); var installed = new List<string>();
        try
        {
            foreach (var file in plan.Files)
            {
                var destination = Path.Combine(plan.Target, file.Name);
                var backup = Path.Combine(plan.Backup, file.Name);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                if (File.Exists(destination)) { File.Move(destination, backup); backedUp.Add(file.Name); }
                installed.Add(file.Name);
                copy(Path.Combine(plan.Payload, file.Name), destination);
            }
        }
        catch
        {
            foreach (var name in installed) if (File.Exists(Path.Combine(plan.Target, name))) File.Delete(Path.Combine(plan.Target, name));
            foreach (var name in backedUp) File.Move(Path.Combine(plan.Backup, name), Path.Combine(plan.Target, name));
            throw;
        }
    }

    public static async Task RunWorkerAsync(string planPath)
    {
        var plan = JsonSerializer.Deserialize<UpdatePlan>(await File.ReadAllTextAsync(planPath)) ?? throw new InvalidDataException("无效更新计划。");
        var parentExited = false;
        try
        {
            Process? parent = null;
            try { parent = Process.GetProcessById(plan.ParentId); } catch (ArgumentException) { }
            try
            {
                using (parent)
                {
                    if (parent is not null && !parent.HasExited && parent.StartTime.ToUniversalTime().Ticks == plan.ParentStartTicks)
                        await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
                }
            }
            catch (InvalidOperationException) { /* Parent exited before its start time could be read. */ }
            parentExited = true;
            Apply(plan);
            await File.WriteAllTextAsync(plan.ResultPath, "软件更新已完成。配置与凭据已保留。");
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(plan.ResultPath, "软件更新失败：" + ex.Message + "。旧版备份目录：" + plan.Backup);
        }
        if (parentExited)
        {
            var info = new ProcessStartInfo(Path.Combine(plan.Target, "PlatformTools.exe")) { UseShellExecute = false, WorkingDirectory = plan.Target };
            using var restarted = Process.Start(info);
        }
    }
}
