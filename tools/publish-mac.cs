// Сборка DupFinder.app для macOS — можно и из Windows:  dotnet run tools/publish-mac.cs
// На выходе publish/DupFinder-mac-arm64.tar.gz (Apple Silicon) и publish/DupFinder-mac-x64.tar.gz (Intel).
//
// Архив tar, а не zip, и собирается здесь, а не системным tar: на Mac у исполняемого файла должен
// стоять бит x, а Windows его не хранит — права выставляются прямо в записях архива.
// Ad-hoc подпись исполняемого файла (без неё Apple Silicon не запустит) SDK ставит сам при публикации
// под osx-*; подписи у разработчика Apple нет, поэтому при первом запуске Gatekeeper спросит.

using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;

// Корень репозитория — на уровень выше папки скрипта (или текущий каталог, если путь к скрипту неизвестен).
var scriptDir = AppContext.GetData("EntryPointFileDirectoryPath") as string;
var root = scriptDir is null ? Directory.GetCurrentDirectory() : Path.GetFullPath(Path.Combine(scriptDir, ".."));
var project = Path.Combine(root, "src", "DupFinder.Mac", "DupFinder.Mac.csproj");
var plist = Path.Combine(root, "src", "DupFinder.Mac", "Info.plist");
var outDir = Path.Combine(root, "publish");
Directory.CreateDirectory(outDir);

foreach (var arch in new[] { "arm64", "x64" })
{
    var rid = $"osx-{arch}";
    var publishDir = Path.Combine(root, "src", "DupFinder.Mac", "bin", "publish", rid);
    if (Directory.Exists(publishDir))
        Directory.Delete(publishDir, recursive: true);

    Run("dotnet", "publish", project, "-c", "Release", "-r", rid, "--self-contained",
        "-p:PublishSingleFile=true", "-p:DebugType=none", "-p:AllowedReferenceRelatedFileExtensions=none",
        "-o", publishDir);

    var archive = Path.Combine(outDir, $"DupFinder-mac-{arch}.tar.gz");
    using (var file = File.Create(archive))
    using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
    using (var tar = new TarWriter(gzip, TarEntryFormat.Pax))
    {
        AddDirectory(tar, "DupFinder.app/");
        AddDirectory(tar, "DupFinder.app/Contents/");
        AddDirectory(tar, "DupFinder.app/Contents/MacOS/");
        AddFile(tar, plist, "DupFinder.app/Contents/Info.plist", executable: false);
        foreach (var path in Directory.GetFiles(publishDir).Order())
        {
            var name = Path.GetFileName(path);
            AddFile(tar, path, $"DupFinder.app/Contents/MacOS/{name}", executable: name == "DupFinder");
        }
    }
    Console.WriteLine($"{archive}  ({new FileInfo(archive).Length / 1024 / 1024} MB)");
}

static void Run(string exe, params string[] args)
{
    using var process = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false })!;
    process.WaitForExit();
    if (process.ExitCode != 0)
        throw new Exception($"{exe} {string.Join(' ', args)}: код {process.ExitCode}");
}

static void AddDirectory(TarWriter tar, string name) =>
    tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, name) { Mode = Rwx, ModificationTime = DateTimeOffset.Now });

static void AddFile(TarWriter tar, string source, string name, bool executable)
{
    using var data = File.OpenRead(source);
    tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name)
    {
        Mode = executable ? Rwx : RwR,
        ModificationTime = File.GetLastWriteTimeUtc(source),
        DataStream = data,
    });
}

// 0755 и 0644
partial class Program
{
    const UnixFileMode RwR = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
    const UnixFileMode Rwx = RwR | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
}
