using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;

namespace Cnidaria.C;

public static class StandardHeaders
{
    public const string BuiltinVaStartName = "__builtin_va_start";
    public const string BuiltinVaArgName = "__builtin_va_arg";
    public const string PrintfIntrinsicName = "__printf"; // classified as intrinsic for Register Bytecode only
    public const string MallocIntrinsicName = "malloc"; // classified as intrinsic for Register Bytecode only
    public const string FreeIntrinsicName = "free"; // classified as intrinsic for Register Bytecode only

    internal static string CtypeH { get; } = ReadEmbeddedText("ctype.h");
    internal static string ErrnoH { get; } = ReadEmbeddedText("errno.h");
    internal static string FcntlH { get; } = ReadEmbeddedText("fcntl.h");
    internal static string LimitsH { get; } = ReadEmbeddedText("limits.h");
    internal static string StdargH { get; } = ReadEmbeddedText("stdarg.h");
    internal static string StdboolH { get; } = ReadEmbeddedText("stdbool.h");
    internal static string StddefH { get; } = ReadEmbeddedText("stddef.h");
    internal static string StdintH { get; } = ReadEmbeddedText("stdint.h");
    internal static string StdioH { get; } = ReadEmbeddedText("stdio.h");
    internal static string StdlibH { get; } = ReadEmbeddedText("stdlib.h");
    internal static string StringH { get; } = ReadEmbeddedText("string.h");
    internal static string SysStatH { get; } = ReadEmbeddedText("stat.h");
    internal static string SysTypesH { get; } = ReadEmbeddedText("types.h");
    internal static string UnistdH { get; } = ReadEmbeddedText("unistd.h");
    internal static string RiscVVectorH { get; } = ReadEmbeddedText("riscv_vector.h");
    private static string ReadEmbeddedText(string fileName)
    {
        var assembly = typeof(StandardHeaders).Assembly;
        var suffix = "." + fileName;
        var resourceName = assembly
            .GetManifestResourceNames()
            .Where(name => name.EndsWith(suffix, StringComparison.Ordinal))
            .OrderByDescending(name => name.Contains(".StandardLibrary.", StringComparison.Ordinal))
            .ThenBy(static name => name, StringComparer.Ordinal)
            .FirstOrDefault();

        if (resourceName is null)
            throw new FileNotFoundException($"Embedded standard header not found: {fileName}");

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
            new IncludeFile("stdarg.h", StdargH),
            new IncludeFile("stdbool.h", StdboolH),
            new IncludeFile("stddef.h", StddefH),
            new IncludeFile("stdint.h", StdintH),
            new IncludeFile("stdio.h", StdioH),
            new IncludeFile("stdlib.h", StdlibH),
            new IncludeFile("string.h", StringH),
            new IncludeFile("sys/stat.h", SysStatH),
            new IncludeFile("sys/types.h", SysTypesH),
            new IncludeFile("unistd.h", UnistdH),
            new IncludeFile("riscv_vector.h", RiscVVectorH));
    }

    public static IIncludeResolver CreateResolver()
        => new InMemoryIncludeResolver(CreateFiles());

    public static IReadOnlyDictionary<string, string> CreateFileMap()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in CreateFiles())
            files[file.Path] = file.Text;
        return files;
    }
}
public static class StandardLibrarySources
{
    public static ImmutableArray<SourceFile> CreateFiles()
        => ImmutableArray.Create(
            new SourceFile("C/StandardLibrary/ctype.c", ReadEmbeddedText("ctype.c")),
            new SourceFile("C/StandardLibrary/io.c", ReadEmbeddedText("io.c")),
            new SourceFile("C/StandardLibrary/libc.c", ReadEmbeddedText("libc.c")),
            new SourceFile("C/StandardLibrary/stdio.c", ReadEmbeddedText("stdio.c")),
            new SourceFile("C/StandardLibrary/stdlib.c", ReadEmbeddedText("stdlib.c")),
            new SourceFile("C/StandardLibrary/string.c", ReadEmbeddedText("string.c")));

    private static string ReadEmbeddedText(string fileName)
    {
        var assembly = typeof(StandardLibrarySources).Assembly;
        var suffix = "." + fileName;
        var resourceName = assembly
            .GetManifestResourceNames()
            .Where(name => name.EndsWith(suffix, StringComparison.Ordinal))
            .OrderByDescending(name => name.Contains(".StandardLibrary.", StringComparison.Ordinal))
            .ThenBy(static name => name, StringComparer.Ordinal)
            .FirstOrDefault();

        if (resourceName is null)
            throw new FileNotFoundException($"Embedded standard library source not found: {fileName}");

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            throw new FileNotFoundException($"Embedded resource not found: {resourceName}");

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
