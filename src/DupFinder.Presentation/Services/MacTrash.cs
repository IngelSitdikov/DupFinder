using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DupFinder.Presentation.Services;

/// <summary>
/// Корзина macOS: [[NSFileManager defaultManager] trashItemAtURL:resultingItemURL:error:] через
/// Objective-C runtime. Так же поступает Finder: файл уезжает в корзину своего тома, его можно вернуть
/// «Вернуть»/«Put Back».
/// </summary>
[SupportedOSPlatform("macos")]
internal static partial class MacTrash
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    static MacTrash()
    {
        // NSFileManager и NSURL живут в Foundation; окно Avalonia его обычно уже загрузило, но не обязано.
        NativeLibrary.Load("/System/Library/Frameworks/Foundation.framework/Foundation");
    }

    /// <summary>Текст ошибки или null при успехе.</summary>
    public static string? Move(string path)
    {
        // Вызов идёт из пула потоков, где своего autorelease pool нет.
        var pool = AutoreleasePoolPush();
        try
        {
            var str = SendString(Class("NSString"), Sel("stringWithUTF8String:"), path);
            var url = Send(Class("NSURL"), Sel("fileURLWithPath:"), str);
            var manager = Send(Class("NSFileManager"), Sel("defaultManager"));
            if (SendTrash(manager, Sel("trashItemAtURL:resultingItemURL:error:"), url, IntPtr.Zero, out var error) != 0)
                return null;
            if (error == IntPtr.Zero)
                return Strings.TrashUnsupported;
            var description = Send(error, Sel("localizedDescription"));
            return Marshal.PtrToStringUTF8(Send(description, Sel("UTF8String")));
        }
        finally
        {
            AutoreleasePoolPop(pool);
        }
    }

    private static IntPtr Class(string name) => GetClass(name);
    private static IntPtr Sel(string name) => RegisterName(name);

    [LibraryImport(ObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr GetClass(string name);

    [LibraryImport(ObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr RegisterName(string name);

    [LibraryImport(ObjC, EntryPoint = "objc_autoreleasePoolPush")]
    private static partial IntPtr AutoreleasePoolPush();

    [LibraryImport(ObjC, EntryPoint = "objc_autoreleasePoolPop")]
    private static partial void AutoreleasePoolPop(IntPtr pool);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr Send(IntPtr receiver, IntPtr selector);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr SendString(IntPtr receiver, IntPtr selector, string arg);

    /// <summary>BOOL — один байт и на arm64, и на x64.</summary>
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial byte SendTrash(IntPtr receiver, IntPtr selector, IntPtr url, IntPtr resultingUrl,
        out IntPtr error);
}
