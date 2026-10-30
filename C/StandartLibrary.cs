using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text;

namespace Cnidaria.C;

public static class StandardHeaders
{
    public const string BuiltinVaStartName = "__builtin_va_start";
    public const string BuiltinVaArgName = "__builtin_va_arg";
    public const string PrintfIntrinsicName = "__printf"; // classified as intrinsic for Register Bytecode only
    public const string MallocIntrinsicName = "malloc"; // classified as intrinsic for Register Bytecode only
    public const string FreeIntrinsicName = "free"; // classified as intrinsic for Register Bytecode only

    internal static string CtypeH { get; } = ReadEmbeddedText("include/ctype.h");
    internal static string ErrnoH { get; } = ReadEmbeddedText("include/errno.h");
    internal static string FcntlH { get; } = ReadEmbeddedText("include/fcntl.h");
    internal static string LimitsH { get; } = ReadEmbeddedText("include/limits.h");
    internal static string StdargH { get; } = ReadEmbeddedText("include/stdarg.h");
    internal static string StdboolH { get; } = ReadEmbeddedText("include/stdbool.h");
    internal static string StddefH { get; } = ReadEmbeddedText("include/stddef.h");
    internal static string StdintH { get; } = ReadEmbeddedText("include/stdint.h");
    internal static string StdioH { get; } = ReadEmbeddedText("include/stdio.h");
    internal static string ScanfInternalH { get; } = ReadEmbeddedText("src/stdio/scanf-internal.h");
    internal static string StdlibH { get; } = ReadEmbeddedText("include/stdlib.h");
    internal static string TimeH { get; } = ReadEmbeddedText("include/time.h");
    internal static string SignalH { get; } = ReadEmbeddedText("include/signal.h");
    internal static string StringH { get; } = ReadEmbeddedText("include/string.h");
    internal static string SysStatH { get; } = ReadEmbeddedText("include/sys/stat.h");
    internal static string SysTimeH { get; } = ReadEmbeddedText("include/sys/time.h");
    internal static string SysIoctlH { get; } = ReadEmbeddedText("include/sys/ioctl.h");
    internal static string TermiosH { get; } = ReadEmbeddedText("include/termios.h");
    internal static string SysTypesH { get; } = ReadEmbeddedText("include/sys/types.h");
    internal static string UnistdH { get; } = ReadEmbeddedText("include/unistd.h");
    internal static string RiscVVectorH { get; } = ReadEmbeddedText("include/riscv_vector.h");
    internal static string DirentH { get; } = ReadEmbeddedText("include/dirent.h");
    internal static string SysWaitH { get; } = ReadEmbeddedText("include/sys/wait.h");
    internal static string SysUioH { get; } = ReadEmbeddedText("include/sys/uio.h");
    internal static string SysMmanH { get; } = ReadEmbeddedText("include/sys/mman.h");
    internal static string PollH { get; } = ReadEmbeddedText("include/poll.h");
    internal static string SysUtsnameH { get; } = ReadEmbeddedText("include/sys/utsname.h");
    internal static string SysSoundcardH { get; } = ReadEmbeddedText("include/sys/soundcard.h");
    internal static string LinuxFbH { get; } = ReadEmbeddedText("include/linux/fb.h");
    internal static string LinuxKdH { get; } = ReadEmbeddedText("include/linux/kd.h");
    internal static string LinuxIoctlH { get; } = ReadEmbeddedText("include/linux/ioctl.h");
    internal static string LinuxInputH { get; } = ReadEmbeddedText("include/linux/input.h");
    internal static string LinuxInputEventCodesH { get; } = ReadEmbeddedText("include/linux/input-event-codes.h");
    internal static string ReadEmbeddedText(string fileName)
    {
        var assembly = typeof(StandardHeaders).Assembly;
        string resourceName = "Cnidaria.C.StandardLibrary." + fileName.Replace('/', '.');

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            throw new FileNotFoundException($"Embedded resource not found: {resourceName}");

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
    public static ImmutableArray<IncludeFile> CreateFiles()
    {
        return ImmutableArray.Create(
            new IncludeFile("ctype.h", CtypeH),
            new IncludeFile("errno.h", ErrnoH),
            new IncludeFile("fcntl.h", FcntlH),
            new IncludeFile("limits.h", LimitsH),
            new IncludeFile("signal.h", SignalH),
            new IncludeFile("time.h", TimeH),
            new IncludeFile("stdarg.h", StdargH),
            new IncludeFile("stdbool.h", StdboolH),
            new IncludeFile("stddef.h", StddefH),
            new IncludeFile("stdint.h", StdintH),
            new IncludeFile("stdio.h", StdioH),
            new IncludeFile("stdlib.h", StdlibH),
            new IncludeFile("string.h", StringH),
            new IncludeFile("sys/stat.h", SysStatH),
            new IncludeFile("sys/time.h", SysTimeH),
            new IncludeFile("sys/ioctl.h", SysIoctlH),
            new IncludeFile("termios.h", TermiosH),
            new IncludeFile("sys/types.h", SysTypesH),
            new IncludeFile("unistd.h", UnistdH),
            new IncludeFile("riscv_vector.h", RiscVVectorH),
            new IncludeFile("dirent.h", DirentH),
            new IncludeFile("sys/wait.h", SysWaitH),
            new IncludeFile("sys/mman.h", SysMmanH),
            new IncludeFile("sys/uio.h", SysUioH),
            new IncludeFile("poll.h", PollH),
            new IncludeFile("sys/utsname.h", SysUtsnameH),
            new IncludeFile("sys/soundcard.h", SysSoundcardH),
            new IncludeFile("linux/fb.h", LinuxFbH),
            new IncludeFile("linux/kd.h", LinuxKdH),
            new IncludeFile("linux/ioctl.h", LinuxIoctlH),
            new IncludeFile("linux/input.h", LinuxInputH),
            new IncludeFile("linux/input-event-codes.h", LinuxInputEventCodesH));
    }

    public static IIncludeResolver CreateResolver()
        => new InMemoryIncludeResolver(CreateFiles().Add(new IncludeFile("C/StandardLibrary/src/stdio/scanf-internal.h", ScanfInternalH)));

    public static IReadOnlyDictionary<string, string> CreateFileMap()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in CreateFiles())
            files[file.Path] = file.Text;
        return files;
    }
}
internal static class RiscVVectorIntrinsics
{
    private static readonly Lazy<Dictionary<string, string>> Declarations = new(Load);

    public static bool TryGetDeclaration(string name, out string declaration)
        => Declarations.Value.TryGetValue(name, out declaration!);

    private static Dictionary<string, string> Load()
    {
        var declarations = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in StandardHeaders.ReadEmbeddedText("intrinsics/riscv_vector.def").Split('\n'))
        {
            var text = line.TrimEnd('\r');
            var open = text.IndexOf('(');
            if (open > 0)
                declarations.TryAdd(text[(text.LastIndexOf(' ', open) + 1)..open], text);
        }
        return declarations;
    }
}

public static class StandardLibrarySources
{
    public static ImmutableArray<SourceFile> CreateFiles()
        => ImmutableArray.Create(
            CreateFile("src/ctype/ctype.c"),
            CreateFile("src/io/io.c"),
            CreateFile("src/startup/libc.c"),
            CreateFile("src/process/process.c"),
            CreateFile("src/signal/signal.c"),
            CreateFile("src/stdio/stdio.c"),
            CreateFile("src/stdio/vfscanf-internal.c"),
            CreateFile("src/stdio/scanf.c"),
            CreateFile("src/stdio/fscanf.c"),
            CreateFile("src/stdio/sscanf.c"),
            CreateFile("src/stdio/vscanf.c"),
            CreateFile("src/stdio/vfscanf.c"),
            CreateFile("src/stdio/vsscanf.c"),
            CreateFile("src/stdlib/stdlib.c"),
            CreateFile("src/string/string.c"),
            CreateFile("src/termios/termios.c"),
            CreateFile("src/time/time.c"));

    private static SourceFile CreateFile(string path)
        => new SourceFile("C/StandardLibrary/" + path, StandardHeaders.ReadEmbeddedText(path));
}
