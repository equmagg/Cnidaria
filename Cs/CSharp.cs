using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.ServerSentEvents;
using System.Reflection;
using System.Text;

namespace Cnidaria.Cs
{
    public static class CSharp
    {

        internal readonly static (IMetadataView meta, Dictionary<int, BytecodeFunction> funcs) StandartLibrary = CompileCoreLibrary(GetCoreBCLSource());
        internal readonly static (IMetadataView meta, Dictionary<int, BytecodeFunction> funcs) ExtendedLibrary = CompileLibrary(GetExtendedBCLSource());
        public static (string output, long instructionsCount, TimeSpan timeElapsed) Interpret(
            string source,
            CancellationTokenSource cts,
            int heapSize = 32 * 1024,
            int stackSize = 4 * 1024,
            int metaSize = 0,
            int outputLimit = 4 * 1024 - 1,
            Action<string>? streamAction = null)
        {
            var output = new StringBuilder();
            var diagnostics = new List<IDiagnostic>();
            try
            {
                var parser = new Cnidaria.Cs.Parser(source);
                CompilationUnitSyntax root = parser.Parse();
                foreach (var diag in parser.LexerDiagnostics)
                {
                    diagnostics.Add(diag);
                }
                foreach (var diag in parser.Diagnostics)
                {
                    diagnostics.Add(diag);
                }
                if (diagnostics.Count > 0)
                {
                    foreach (var diag in diagnostics)
                    {
                        output.AppendLine(diag.GetMessage());
                    }
                    return (output.ToString(), -1, TimeSpan.MinValue);
                }
                var syntaxTree = new SyntaxTree(root, "std");
                var trees = ImmutableArray.Create(new SyntaxTree[] { syntaxTree });
                var appRefs = new MetadataReferenceSet(new[] { StandartLibrary.meta, ExtendedLibrary.meta });
                Compilation compilation = CompilationFactory.Create(trees, appRefs, out var declDiag);
                foreach (var diag in declDiag)
                {
                    diagnostics.Add(diag);
                }
                if (diagnostics.Count > 0)
                {
                    foreach (var diag in diagnostics)
                    {
                        output.AppendLine(diag.GetMessage());
                    }
                    return (output.ToString(), -1, TimeSpan.MinValue);
                }
                var (appMd, appFuncs, diags, ex) = compilation.BuildModule(
                    moduleName: "app",
                    tree: trees[0],
                    includeCoreTypesInTypeDefs: false,
                    defaultExternalAssemblyName: "std",
                    externalAssemblyResolver: appRefs.ResolveAssemblyName,
                    print: false);
                if (ex != null)
                {
                    var diagnostic = new Diagnostic("INTERNAL", DiagnosticSeverity.Error, ex.Message, default);
                    diagnostics.Add(diagnostic);
                }
                foreach (var diag in diags)
                {
                    diagnostics.Add(diag);
                }
                if (diagnostics.Count > 0)
                {
                    foreach (var diag in diagnostics)
                    {
                        output.AppendLine(diag.GetMessage());
                    }
                    return (output.ToString(), -1, TimeSpan.MinValue);
                }
                IMetadataView appViewFlat = new FlatMetadataView(FlatMetadataBuilder.Build(appMd));

                var domain = new Cnidaria.Cs.Domain();
                var stdModule = new Cnidaria.Cs.RuntimeModule("std", StandartLibrary.meta, StandartLibrary.funcs);
                var extModule = new Cnidaria.Cs.RuntimeModule("extendedStd", ExtendedLibrary.meta, ExtendedLibrary.funcs);
                var appModule = new Cnidaria.Cs.RuntimeModule("app", appViewFlat, appFuncs);

                var modules = new Dictionary<string, Cnidaria.Cs.RuntimeModule>(StringComparer.Ordinal);
                void AddUnique(Cnidaria.Cs.RuntimeModule m)
                {
                    if (!modules.TryAdd(m.Name, m))
                        throw new InvalidOperationException($"Duplicate module loaded: '{m.Name}'");
                    domain.Add(m);
                }

                AddUnique(stdModule);
                AddUnique(extModule);
                AddUnique(appModule);

                int entryTok = Cnidaria.Cs.BytecodeDump.FindEntryPointMethodDef(appModule);
                //Cnidaria.Cs.BytecodeDump.DumpReachable(modules, appModule, entryTok);

                var rts = new Cnidaria.Cs.RuntimeTypeSystem(modules);

                byte[] mem = new byte[stackSize + heapSize + metaSize];
                int metaEnd = metaSize;
                int stackBase = metaEnd;
                int stackEnd = stackBase + stackSize;
                var limits = new Cnidaria.Cs.ExecutionLimits
                {
                    MaxCallDepth = 128,
                    MaxInstructions = 100_000_000,
                    TokenCheckPeriod = 256,
                };
                var sb = new StringBuilder();
                string result = string.Empty;
                long instructionCount = -1;
                TimeSpan timeElapsed = TimeSpan.MinValue;
                using (var stringWriter = new StringWriter(sb))
                {
                    using var writer = new BoundedTextWriter(
                        inner: stringWriter,
                        maxChars: outputLimit,
                        mode: BoundedTextWriter.OverflowMode.Truncate,
                        onChunk: streamAction,
                        streamOnlyWhatWasWritten: true);
                    var vm = new Cnidaria.Cs.Vm(
                    memory: mem,
                    metaEnd: metaEnd,
                    stackBase: stackBase,
                    stackEnd: stackEnd,
                    domain: domain,
                    rts: rts,
                    modules: modules,
                    textWriter: writer);
                    var entryFn = appModule.MethodsByDefToken[entryTok];
                    foreach (var diag in diagnostics)
                    {
                        output.AppendLine(diag.GetMessage());
                    }
                    if (diagnostics.Count > 0) return (output.ToString(), -1, TimeSpan.MinValue);
                    var t = Stopwatch.StartNew();
                    try
                    {
                        vm.Execute(appModule, entryFn, cts.Token, limits);
                    }
                    catch (Exception e)
                    {
                        var diagnostic = new Diagnostic("INTERNAL", DiagnosticSeverity.Error, e.Message, default);
                        diagnostics.Add(diagnostic);
                        output.AppendLine(diagnostic.GetMessage());

                        return (sb.ToString() + output.ToString(), -1, TimeSpan.MinValue);
                    }
                    t.Stop();
                    result = sb.ToString();
                    instructionCount = vm.InctructionsElapsed;
                    timeElapsed = t.Elapsed;
                }

                return (result, Math.Max(instructionCount, -1), timeElapsed);
            }
            catch (Exception ex)
            {
                diagnostics.Add(new Diagnostic("INTERNAL", DiagnosticSeverity.Error, ex.Message, default));
                foreach (var diag in diagnostics)
                {
                    output.AppendLine(diag.GetMessage());
                }
                return (output.ToString(), -1, TimeSpan.MinValue);
            }
        }
        internal static (IMetadataView meta, Dictionary<int, BytecodeFunction> funcs) CompileCoreLibrary(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("standart library source code is empty");
            var stdParser = new Cnidaria.Cs.Parser(source);
            var stdRoot = stdParser.Parse();
            var stdSyntaxTree = new SyntaxTree(stdRoot, "std");
            var stdTrees = ImmutableArray.Create(new SyntaxTree[] { stdSyntaxTree });
            var stdCompilation = CompilationFactory.CreateCoreLibrary(stdTrees, out _);
            var (stdMd, stdFuncs, stdDiags, stdEx) = stdCompilation.BuildModule(
                moduleName: "std",
                tree: stdTrees[0],
                includeCoreTypesInTypeDefs: true,
                defaultExternalAssemblyName: "std",
                print: false);
            byte[] stdFlatMd = Cnidaria.Cs.FlatMetadataBuilder.Build(stdMd);
            IMetadataView stdViewFlat = new FlatMetadataView(stdFlatMd);
            return (stdViewFlat, stdFuncs);
        }
        internal static (IMetadataView meta, Dictionary<int, BytecodeFunction> funcs) CompileLibrary(string source)
        {
            var parser = new Cnidaria.Cs.Parser(source);
            var root = parser.Parse();
            var tree = new SyntaxTree(root, "extendedStd");
            var trees = ImmutableArray.Create(new[] { tree });
            var refs = new Cnidaria.Cs.MetadataReferenceSet(new[] { StandartLibrary.meta });
            var compilation = CompilationFactory.Create(trees, refs, out var extDeclDiag);
            var (md, funcs, diags, ex) = compilation.BuildModule(
                moduleName: "extendedStd",
                tree: trees[0],
                includeCoreTypesInTypeDefs: false,
                defaultExternalAssemblyName: "std",
                print: false,
                externalAssemblyResolver: refs.ResolveAssemblyName);

            byte[] flatMd = Cnidaria.Cs.FlatMetadataBuilder.Build(md);
            IMetadataView viewFlat = new FlatMetadataView(flatMd);
            return (viewFlat, funcs);
        }
        internal static string GetCoreBCLSource()
        {
            return ReadEmbeddedText("Cnidaria.Cs.BCL.CoreBCL.cs");
        }
        internal static string GetExtendedBCLSource()
        {
            return ReadEmbeddedText("Cnidaria.Cs.BCL.ExtendedBCL.cs");
        }
        private static string ReadEmbeddedText(string resourceName)
        {
            var asm = typeof(Cnidaria.Cs.CSharp).Assembly;
            using var s = asm.GetManifestResourceStream(resourceName);
            if (s == null)
                throw new FileNotFoundException($"Embedded resource not found: {resourceName}");
            using var r = new StreamReader(s, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return r.ReadToEnd();
        }
    }
    public sealed class BoundedTextWriter : TextWriter
    {
        public enum OverflowMode
        {
            Truncate,
            Drop
        }
        private readonly TextWriter _inner;
        private readonly Action<string>? _onChunk;
        private readonly bool _streamOnlyWhatWasWritten;
        private readonly OverflowMode _mode;

        private int _remaining;

        public BoundedTextWriter(
            TextWriter inner,
            int maxChars,
            OverflowMode mode = OverflowMode.Truncate,
            Action<string>? onChunk = null,
            bool streamOnlyWhatWasWritten = true)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            if (maxChars < 0) throw new ArgumentOutOfRangeException(nameof(maxChars));
            _remaining = maxChars;
            _mode = mode;
            _onChunk = onChunk;
            _streamOnlyWhatWasWritten = streamOnlyWhatWasWritten;
        }

        public override Encoding Encoding => _inner.Encoding;
        public override IFormatProvider FormatProvider => _inner.FormatProvider;
        public int Remaining => _remaining;
        [AllowNull]
        public override string NewLine
        {
            get => _inner.NewLine;
            set { _inner.NewLine = value; }
        }
        public override void Flush() => _inner.Flush();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }

        public override void Write(char value)
        {
            if (_remaining <= 0) return;

            _inner.Write(value);
            _remaining--;

            _onChunk?.Invoke(new string(value, 1));
        }

        public override void Write(string? value)
        {
            if (_remaining <= 0) return;
            if (string.IsNullOrEmpty(value)) return;
            WriteSpan(value.AsSpan());
        }

        public override void Write(char[] buffer, int index, int count)
        {
            if (_remaining <= 0) return;
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if ((uint)index > (uint)buffer.Length) throw new ArgumentOutOfRangeException(nameof(index));
            if ((uint)count > (uint)(buffer.Length - index)) throw new ArgumentOutOfRangeException(nameof(count));
            if (count == 0) return;

            WriteSpan(buffer.AsSpan(index, count));
        }

        public override void Write(ReadOnlySpan<char> buffer)
        {
            if (_remaining <= 0) return;
            if (buffer.Length == 0) return;

            WriteSpan(buffer);
        }

        public override void WriteLine() => Write(NewLine);
        public override void WriteLine(string? value) { Write(value); WriteLine(); }
        public override void WriteLine(char value) { Write(value); WriteLine(); }
        public override void WriteLine(ReadOnlySpan<char> buffer) { Write(buffer); WriteLine(); }

        private void WriteSpan(ReadOnlySpan<char> span)
        {
            if (_remaining <= 0) return;

            int len = span.Length;
            if (len == 0) return;

            // Entire span doesn't fit
            if (len > _remaining)
            {
                if (_mode == OverflowMode.Drop)
                    return;

                // Truncate
                span = span.Slice(0, _remaining);
                len = span.Length; // == remaining
            }

            if (len == 0) return;

            _inner.Write(span);
            _remaining -= len;

            if (_onChunk != null)
            {
                _onChunk(_streamOnlyWhatWasWritten ? span.ToString() : span.ToString());
            }
        }
    }

}
