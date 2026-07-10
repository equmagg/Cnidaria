using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text;

namespace Cnidaria.C
{
    public static class StandardHeaders
    {
        public const string BuiltinVaStartName = "__builtin_va_start";
        public const string BuiltinVaArgName = "__builtin_va_arg";
        public const string PrintfIntrinsicName = "__printf";
        public const string MallocIntrinsicName = "malloc";
        public const string FreeIntrinsicName = "free";

        internal static string StddefH { get; } = ReadEmbeddedText("Cnidaria.C.StandardHeaders.stddef.h");
        internal static string StdargH { get; } = ReadEmbeddedText("Cnidaria.C.StandardHeaders.stdarg.h");
        internal static string StdioH { get; } = ReadEmbeddedText("Cnidaria.C.StandardHeaders.stdio.h");
        internal static string RiscVVectorH { get; } = ReadEmbeddedText("Cnidaria.C.StandardHeaders.riscv_vector.h");
        private static string ReadEmbeddedText(string resourceName)
        {
            var asm = typeof(Cnidaria.C.StandardHeaders).Assembly;
            using var s = asm.GetManifestResourceStream(resourceName);
            if (s == null)
                throw new FileNotFoundException($"Embedded resource not found: {resourceName}");
            using var r = new StreamReader(s, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return r.ReadToEnd();
        }
        public static ImmutableArray<IncludeFile> CreateFiles()
        {
            return ImmutableArray.Create(
                new IncludeFile("stddef.h", StddefH),
                new IncludeFile("stdio.h", StdioH),
                new IncludeFile("stdarg.h", StdargH),
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
}
