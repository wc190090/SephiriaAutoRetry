using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SephiriaAutoRetry.Installer;

internal sealed class InstallResult
{
    internal required string ModPath { get; init; }
    internal required string ModSha256 { get; init; }
    internal string? BackupPath { get; init; }
    internal bool InstalledBepInEx { get; init; }
}

internal static class InstallerEngine
{
    internal const string ModVersion = "0.2.0";
    internal const string BepInExVersion = "5.4.23.5";
    private const string BepInExResource = "SephiriaAutoRetry.Installer.Payload.BepInEx.zip";
    private const string ModResource = "SephiriaAutoRetry.Installer.Payload.Mod.dll";

    internal static IReadOnlyList<string> FindGameDirectories()
    {
        HashSet<string> steamRoots = new(StringComparer.OrdinalIgnoreCase);
        AddRegistryPath(steamRoots, Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
        AddRegistryPath(steamRoots, Registry.LocalMachine, @"Software\Valve\Steam", "InstallPath");
        AddRegistryPath(steamRoots, Registry.LocalMachine, @"Software\WOW6432Node\Valve\Steam", "InstallPath");

        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        AddExistingDirectory(steamRoots, Path.Combine(programFilesX86, "Steam"));

        HashSet<string> libraries = new(steamRoots, StringComparer.OrdinalIgnoreCase);
        foreach (string steamRoot in steamRoots.ToArray())
        {
            string vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf))
            {
                continue;
            }

            try
            {
                foreach (Match match in Regex.Matches(
                             File.ReadAllText(vdf),
                             "\\\"path\\\"\\s*\\\"([^\\\"]+)\\\"",
                             RegexOptions.IgnoreCase))
                {
                    AddExistingDirectory(libraries, match.Groups[1].Value.Replace("\\\\", "\\"));
                }
            }
            catch
            {
                // Manual selection remains available.
            }
        }

        return libraries
            .Select(root => Path.Combine(root, "steamapps", "common", "Sephiria"))
            .Where(IsValidGameRoot)
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static InstallResult Install(
        string requestedRoot,
        Action<string> log,
        bool checkGameProcess = true,
        bool backupSave = true,
        string? backupRootOverride = null)
    {
        string gameRoot = ValidateGameRoot(requestedRoot);
        if (checkGameProcess)
        {
            EnsureGameStopped();
        }

        string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        string? backupPath = null;
        string savePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Saved Games",
            "Sephiria");
        if (backupSave && Directory.Exists(savePath))
        {
            backupPath = CreateBackupDirectory(timestamp, backupRootOverride);
            CopyDirectory(savePath, Path.Combine(backupPath, "Save"));
            log($"存档已备份：{Path.Combine(backupPath, "Save")}");
        }

        string coreDll = Path.Combine(gameRoot, "BepInEx", "core", "BepInEx.dll");
        string winHttp = Path.Combine(gameRoot, "winhttp.dll");
        string doorstop = Path.Combine(gameRoot, "doorstop_config.ini");
        bool installedBepInEx = false;
        if (File.Exists(coreDll))
        {
            if (!File.Exists(winHttp) || !File.Exists(doorstop))
            {
                throw new InvalidOperationException("检测到不完整的 BepInEx，安装器为避免覆盖其他加载器而停止。");
            }

            string version = FileVersionInfo.GetVersionInfo(coreDll).FileVersion ?? "未知版本";
            log($"检测到 BepInEx {version}，保留现有加载器。");
        }
        else
        {
            bool partial = File.Exists(winHttp) || File.Exists(doorstop) || Directory.Exists(Path.Combine(gameRoot, "BepInEx"));
            if (partial)
            {
                throw new InvalidOperationException(
                    "检测到部分 BepInEx/其他 Doorstop 文件，但缺少 BepInEx\\core\\BepInEx.dll；请先修复加载器。");
            }

            ExtractBepInEx(gameRoot);
            if (!File.Exists(coreDll))
            {
                throw new InvalidDataException("内置 BepInEx 解压后缺少核心 DLL。");
            }

            installedBepInEx = true;
            log($"已离线安装 BepInEx {BepInExVersion} x64。");
        }

        string pluginDirectory = GetPluginDirectory(gameRoot);
        if (Directory.Exists(pluginDirectory))
        {
            RejectReparsePoint(pluginDirectory);
            backupPath ??= CreateBackupDirectory(timestamp, backupRootOverride);
            CopyDirectory(pluginDirectory, Path.Combine(backupPath, "PreviousPlugin"));
            log($"旧版插件已备份：{Path.Combine(backupPath, "PreviousPlugin")}");
        }

        Directory.CreateDirectory(pluginDirectory);
        string modPath = Path.Combine(pluginDirectory, "SephiriaAutoRetry.dll");
        string temporaryPath = modPath + ".installing";
        string expectedHash = ComputeResourceSha256(ModResource);
        try
        {
            WriteResource(ModResource, temporaryPath);
            if (!string.Equals(expectedHash, ComputeFileSha256(temporaryPath), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Mod DLL 临时文件 SHA-256 校验失败。");
            }

            File.Move(temporaryPath, modPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        string actualHash = ComputeFileSha256(modPath);
        if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Mod DLL 安装后 SHA-256 校验失败。");
        }

        ValidateModVersion(modPath);
        log($"Sephiria Auto Retry {ModVersion} 安装完成。");
        log($"SHA-256：{actualHash}");
        return new InstallResult
        {
            ModPath = modPath,
            ModSha256 = actualHash,
            BackupPath = backupPath,
            InstalledBepInEx = installedBepInEx,
        };
    }

    internal static string? Uninstall(
        string requestedRoot,
        Action<string> log,
        bool checkGameProcess = true,
        string? backupRootOverride = null)
    {
        string gameRoot = ValidateGameRoot(requestedRoot);
        if (checkGameProcess)
        {
            EnsureGameStopped();
        }

        string pluginDirectory = GetPluginDirectory(gameRoot);
        if (!Directory.Exists(pluginDirectory))
        {
            log("没有找到本 Mod，无需卸载。");
            return null;
        }

        RejectReparsePoint(pluginDirectory);
        string backup = CreateBackupDirectory(DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"), backupRootOverride);
        CopyDirectory(pluginDirectory, Path.Combine(backup, "UninstalledPlugin"));
        Directory.Delete(pluginDirectory, recursive: true);
        log("已卸载本 Mod；BepInEx、其他 Mod、配置和存档均未删除。");
        return backup;
    }

    internal static void RunSelfTest(string requestedRoot)
    {
        string root = NormalizePath(requestedRoot);
        if (!Path.GetFileName(root).StartsWith("SephiriaAutoRetryInstallerSelfTest-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("自检目录名称不安全。");
        }

        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Sephiria.exe"), "installer self test");
        List<string> messages = new();
        InstallResult first = Install(
            root,
            messages.Add,
            checkGameProcess: false,
            backupSave: false,
            backupRootOverride: Path.Combine(root, "Backups"));
        if (!first.InstalledBepInEx || !File.Exists(first.ModPath))
        {
            throw new InvalidOperationException("首次安装自检失败。");
        }

        string sentinel = Path.Combine(root, "BepInEx", "plugins", "OtherMod", "keep.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        File.WriteAllText(sentinel, "keep");
        File.WriteAllText(Path.Combine(GetPluginDirectory(root), "old-sidecar.txt"), "backup me");
        InstallResult update = Install(
            root,
            messages.Add,
            checkGameProcess: false,
            backupSave: false,
            backupRootOverride: Path.Combine(root, "Backups"));
        if (update.InstalledBepInEx || update.BackupPath == null || !File.Exists(sentinel))
        {
            throw new InvalidOperationException("更新/保留其他 Mod 自检失败。");
        }

        Uninstall(root, messages.Add, checkGameProcess: false, backupRootOverride: Path.Combine(root, "Backups"));
        if (Directory.Exists(GetPluginDirectory(root)) || !File.Exists(sentinel) ||
            !File.Exists(Path.Combine(root, "BepInEx", "core", "BepInEx.dll")))
        {
            throw new InvalidOperationException("卸载范围自检失败。");
        }

        messages.Add("SELF_TEST_OK");
        File.WriteAllLines(Path.Combine(root, "self-test-ok.txt"), messages);
    }

    internal static bool IsValidGameRoot(string? path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) && File.Exists(Path.Combine(NormalizePath(path), "Sephiria.exe"));
        }
        catch
        {
            return false;
        }
    }

    internal static string? GetInstalledVersion(string gameRoot)
    {
        string path = Path.Combine(GetPluginDirectory(NormalizePath(gameRoot)), "SephiriaAutoRetry.dll");
        if (!File.Exists(path))
        {
            return null;
        }

        string? value = FileVersionInfo.GetVersionInfo(path).ProductVersion;
        return Version.TryParse(value, out Version? version) ? version.ToString(3) : value;
    }

    internal static string GetLogPath(string gameRoot) =>
        Path.Combine(ValidateGameRoot(gameRoot), "BepInEx", "LogOutput.log");

    private static string ValidateGameRoot(string path)
    {
        string root = NormalizePath(path);
        if (!File.Exists(Path.Combine(root, "Sephiria.exe")))
        {
            throw new DirectoryNotFoundException("所选目录中没有 Sephiria.exe。");
        }

        return root;
    }

    private static string NormalizePath(string path) => Path.GetFullPath(path.Trim().Trim('"')).TrimEnd(Path.DirectorySeparatorChar);

    private static string GetPluginDirectory(string gameRoot)
    {
        string directory = Path.GetFullPath(Path.Combine(gameRoot, "BepInEx", "plugins", "SephiriaAutoRetry"));
        EnsureContained(gameRoot, directory);
        return directory;
    }

    private static void EnsureGameStopped()
    {
        if (Process.GetProcessesByName("Sephiria").Length > 0)
        {
            throw new InvalidOperationException("检测到 Sephiria 正在运行。请完全退出游戏后再安装或卸载。");
        }
    }

    private static void ExtractBepInEx(string gameRoot)
    {
        using Stream stream = GetResource(BepInExResource);
        using ZipArchive archive = new(stream, ZipArchiveMode.Read);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string destination = Path.GetFullPath(Path.Combine(gameRoot, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            EnsureContained(gameRoot, destination);
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using Stream input = entry.Open();
            using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
        }
    }

    private static void ValidateModVersion(string path)
    {
        string? raw = FileVersionInfo.GetVersionInfo(path).ProductVersion;
        if (!Version.TryParse(raw, out Version? actual) || !Version.TryParse(ModVersion, out Version? expected) ||
            actual.Major != expected.Major || actual.Minor != expected.Minor || actual.Build != expected.Build)
        {
            throw new InvalidDataException($"Mod DLL 版本校验失败：期望 {ModVersion}，实际 {raw ?? "未知"}。");
        }
    }

    private static void WriteResource(string name, string path)
    {
        using Stream input = GetResource(name);
        using FileStream output = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
    }

    private static Stream GetResource(string name) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
        ?? throw new InvalidDataException($"安装器缺少内置资源：{name}");

    private static string ComputeResourceSha256(string name)
    {
        using Stream stream = GetResource(name);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string ComputeFileSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string CreateBackupDirectory(string timestamp, string? overrideRoot)
    {
        string baseRoot = overrideRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Saved Games",
            "Sephiria",
            "ModBackups",
            "SephiriaAutoRetry");
        string result = Path.Combine(Path.GetFullPath(baseRoot), timestamp);
        Directory.CreateDirectory(result);
        return result;
    }

    private static void CopyDirectory(string source, string destination)
    {
        DirectoryInfo sourceInfo = new(source);
        RejectReparsePoint(sourceInfo.FullName);
        Directory.CreateDirectory(destination);
        foreach (FileInfo file in sourceInfo.GetFiles())
        {
            file.CopyTo(Path.Combine(destination, file.Name), overwrite: true);
        }

        foreach (DirectoryInfo child in sourceInfo.GetDirectories())
        {
            RejectReparsePoint(child.FullName);
            CopyDirectory(child.FullName, Path.Combine(destination, child.Name));
        }
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException($"拒绝处理链接/重解析点：{path}");
        }
    }

    private static void EnsureContained(string root, string target)
    {
        string prefix = NormalizePath(root) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(target).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"目标路径超出游戏目录：{target}");
        }
    }

    private static void AddRegistryPath(HashSet<string> roots, RegistryKey hive, string keyName, string valueName)
    {
        try
        {
            using RegistryKey? key = hive.OpenSubKey(keyName);
            if (key?.GetValue(valueName) is string path)
            {
                AddExistingDirectory(roots, path);
            }
        }
        catch
        {
            // Other discovery paths and manual selection remain available.
        }
    }

    private static void AddExistingDirectory(HashSet<string> roots, string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            roots.Add(NormalizePath(path));
        }
    }
}
