using Cnidaria.X86;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Cnidaria.Cs
{
    internal static class X86Runtime
    {
        public const string InitializeSymbol = "RhpInitialize";
        public const string GcPollSymbol = "RhpGcPoll";
        public const string GcPollRequestedSymbol = "RhpGcPollRequested";
        public const string CurrentSafePointSymbol = "RhpCurrentSafePoint";
        public const string CurrentFramePointerSymbol = "RhpCurrentFramePointer";
        public const string NewFastSymbol = "RhpNewFast";
        public const string NewArraySymbol = "RhpNewArray";
        public const string NewUninitializedArraySymbol = "RhpNewArrayUninitialized";
        public const string AllocHGlobalSymbol = "RhpAllocHGlobal";
        public const string FreeHGlobalSymbol = "RhpFreeHGlobal";
        public const string MonitorEnterSymbol = "RhpMonitorEnter";
        public const string MonitorExitSymbol = "RhpMonitorExit";
        public const string DelegateCombineSymbol = "RhpDelegateCombine";
        public const string DelegateRemoveSymbol = "RhpDelegateRemove";
        public const string ArrayGetLengthSymbol = "RhpArrayGetLength";
        public const string ArrayClearSymbol = "RhpArrayClear";
        public const string ArrayCopySymbol = "RhpArrayCopy";
        public const string NewStringFromCharSymbol = "RhpNewStringFromChar";
        public const string NewStringFromUtf16Symbol = "RhpNewStringFromUtf16";
        public const string NewStringFromCharArraySymbol = "RhpNewStringFromCharArray";
        public const string NewStringFromCharArrayRangeSymbol = "RhpNewStringFromCharArrayRange";
        public const string NewStringFromReadOnlySpanSymbol = "RhpNewStringFromReadOnlySpan";
        public const string ThrowSymbol = "RhpThrowEx";
        public const string RethrowSymbol = "RhpRethrow";
        public const string LeaveSymbol = "RhpLeave";
        public const string EndFinallySymbol = "RhpEndFinally";
        public const string EhTransferSymbol = "RhpEhTransfer";
        public const string EhFrameCountSymbol = "RhpEhFrameCount";
        public const string EhFramesSymbol = "RhpEhFrames";
        public const string EhRegisterContextsSymbol = "RhpEhRegisterContexts";
        public const string CurrentExceptionSymbol = "RhpCurrentException";
        public const string FailFastSymbol = "RhpFallbackFailFast";
        public const string FloatingRemainderSingleSymbol = "RhpFmodF";
        public const string FloatingRemainderDoubleSymbol = "RhpFmod";
        private const string ConsoleWriteUtf16Symbol = "RhpConsoleWriteUtf16";
        private const string ConsoleWriteUtf16ZSymbol = "RhpConsoleWriteUtf16Z";
        private const string ConsoleWriteStringSymbol = "RhpConsoleWriteString";
        private const string MemsetSymbol = "RhpMemset";
        private const string GetCurrentProcessorNumberSymbol = "RhpGetCurrentProcessorNumber";

        private const string TextSectionName = ".text";

        private sealed class TrimAnalysis
        {
            public ObjectTrimmer Trimmer { get; }
            public ConcurrentDictionary<string, X86Program> Results { get; } = new ConcurrentDictionary<string, X86Program>(StringComparer.Ordinal);
            public int[] InstructionOffsets { get; }
            public int TextSize => InstructionOffsets.Length == 0 ? 0 : InstructionOffsets[^1];

            public TrimAnalysis(ObjectTrimmer trimmer, int[] instructionOffsets)
            {
                Trimmer = trimmer;
                InstructionOffsets = instructionOffsets;
            }
        }

        private static readonly ConditionalWeakTable<X86Program, TrimAnalysis> TrimAnalyses = new ConditionalWeakTable<X86Program, TrimAnalysis>();


        private static readonly ConcurrentDictionary<string, Lazy<X86Program>> RuntimeObjects =
            new ConcurrentDictionary<string, Lazy<X86Program>>(StringComparer.Ordinal);

        public static X86Program GetObject(TargetInfo target)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));
            if (!target.IsX86 || target.OperatingSystem is not (OperatingSystemKind.Linux or OperatingSystemKind.Windows))
                throw new NotSupportedException("The embedded x86 runtime supports Linux and Windows targets only.");

            string key = $"{target.Architecture}:{target.OperatingSystem}:{(ulong)target.ArchitectureFeatures}:{target.Endianness}";
            return RuntimeObjects.GetOrAdd(
                key,
                _ => new Lazy<X86Program>(
                    () => Compile(target),
                    LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }

        private static TrimAnalysis Analyze(X86Program runtime)
        {
            var instructionOffsets = new int[checked(runtime.Text.Instructions.Length + 1)];
            for (int i = 0; i < runtime.Text.Instructions.Length; i++)
            {
                instructionOffsets[i + 1] = checked(
                    instructionOffsets[i] + X86CodeEncoder.GetEncodedLength(runtime.Text.Instructions[i], runtime.Target));
            }

            var trimmer = new ObjectTrimmer();
            trimmer.AddSection(TextSectionName, instructionOffsets[^1], 1);
            foreach (var section in runtime.DataSections)
                trimmer.AddSection(section.Name, checked(section.Data.Length + section.BssSize), section.Alignment);

            foreach (var symbol in runtime.Symbols)
            {
                if (symbol.Kind == X86ObjectSymbolKind.Section || symbol.Binding == X86ObjectSymbolBinding.External)
                    continue;
                trimmer.AddDefinition(symbol.Name, symbol.SectionName, symbol.Offset, symbol.Size);
            }

            for (int i = 0; i < runtime.Text.Instructions.Length; i++)
            {
                X86Instruction instruction = runtime.Text.Instructions[i];
                int offset = instructionOffsets[i];
                AddOperandReference(trimmer, offset, instruction.Operand0);
                AddOperandReference(trimmer, offset, instruction.Operand1);
                AddOperandReference(trimmer, offset, instruction.Operand2);
            }

            foreach (var relocation in runtime.Text.Relocations)
                trimmer.AddReference(TextSectionName, relocation.Offset, relocation.SymbolName);
            foreach (var section in runtime.DataSections)
            {
                foreach (var relocation in section.Relocations)
                    trimmer.AddReference(section.Name, relocation.Offset, relocation.SymbolName);
            }

            return new TrimAnalysis(trimmer, instructionOffsets);
        }

        public static X86Program Trim(X86Program runtime, IEnumerable<string> rootSymbols)
        {
            if (runtime is null)
                throw new ArgumentNullException(nameof(runtime));
            if (rootSymbols is null)
                throw new ArgumentNullException(nameof(rootSymbols));

            TrimAnalysis analysis = TrimAnalyses.GetValue(runtime, Analyze);
            ObjectTrimmer trimmer = analysis.Trimmer;
            ObjectTrimLayout layout = trimmer.Trim(rootSymbols);
            if (!layout.RemovedAnything)
                return runtime;
            if (analysis.Results.TryGetValue(layout.LiveKey, out X86Program? cached))
                return cached;

            var instructions = ImmutableArray.CreateBuilder<X86Instruction>();
            for (int i = 0; i < runtime.Text.Instructions.Length; i++)
            {
                if (layout.IsLive(TextSectionName, analysis.InstructionOffsets[i]))
                    instructions.Add(runtime.Text.Instructions[i]);
            }

            var labels = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var label in runtime.Text.Labels)
            {
                if (layout.IsLive(TextSectionName, label.Value) || label.Value == analysis.TextSize)
                    labels[label.Key] = layout.Map(TextSectionName, label.Value);
            }

            var textRelocations = ImmutableArray.CreateBuilder<X86ObjectRelocation>();
            var referenced = new HashSet<string>(StringComparer.Ordinal);
            foreach (var relocation in runtime.Text.Relocations)
            {
                if (!layout.IsLive(TextSectionName, relocation.Offset))
                    continue;
                referenced.Add(relocation.SymbolName);
                textRelocations.Add(new X86ObjectRelocation(
                    relocation.SectionName,
                    layout.Map(TextSectionName, relocation.Offset),
                    relocation.SymbolName,
                    relocation.Addend,
                    relocation.Kind));
            }

            var dataSections = ImmutableArray.CreateBuilder<X86DataSection>(runtime.DataSections.Length);
            foreach (var section in runtime.DataSections)
            {
                var data = ImmutableArray.CreateBuilder<byte>();
                for (int i = 0; i < section.Data.Length; i++)
                {
                    if (layout.IsLive(section.Name, i))
                        data.Add(section.Data[i]);
                }

                int bssSize = 0;
                for (int i = 0; i < section.BssSize; i++)
                {
                    if (layout.IsLive(section.Name, checked(section.Data.Length + i)))
                        bssSize++;
                }

                var relocations = ImmutableArray.CreateBuilder<X86ObjectRelocation>();
                foreach (var relocation in section.Relocations)
                {
                    if (!layout.IsLive(section.Name, relocation.Offset))
                        continue;
                    referenced.Add(relocation.SymbolName);
                    relocations.Add(new X86ObjectRelocation(
                        relocation.SectionName,
                        layout.Map(section.Name, relocation.Offset),
                        relocation.SymbolName,
                        relocation.Addend,
                        relocation.Kind));
                }

                dataSections.Add(new X86DataSection(
                    section.Name,
                    section.Kind,
                    section.Alignment,
                    data.ToImmutable(),
                    bssSize,
                    relocations.ToImmutable()));
            }

            var symbols = ImmutableArray.CreateBuilder<X86ObjectSymbol>();
            foreach (var symbol in runtime.Symbols)
            {
                if (symbol.Binding == X86ObjectSymbolBinding.External)
                {
                    if (referenced.Contains(symbol.Name))
                        symbols.Add(symbol);
                    continue;
                }

                int originalSize = symbol.SectionName.Length == 0
                    ? symbol.Size
                    : symbol.Kind == X86ObjectSymbolKind.Section
                        ? SectionOriginalSize(runtime, symbol.SectionName, analysis.TextSize)
                        : symbol.Size;

                if (symbol.Kind != X86ObjectSymbolKind.Section)
                {
                    bool keep = symbol.Size > 0
                        ? layout.IsDefinitionLive(symbol.Name)
                        : layout.IsLive(symbol.SectionName, symbol.Offset);
                    if (!keep)
                        continue;
                }

                int start = layout.Map(symbol.SectionName, symbol.Offset);
                int end = layout.Map(symbol.SectionName, checked(symbol.Offset + originalSize));
                symbols.Add(new X86ObjectSymbol(
                    symbol.Name,
                    symbol.SectionName,
                    start,
                    Math.Max(0, end - start),
                    symbol.Binding,
                    symbol.Kind,
                    symbol.IsTentative));
            }

            var trimmed = new X86Program(
                runtime.Target,
                new X86TextSection(instructions.ToImmutable(), labels, textRelocations.ToImmutable()),
                dataSections.ToImmutable(),
                symbols.ToImmutable(),
                runtime.EntrySymbol);
            analysis.Results.TryAdd(layout.LiveKey, trimmed);
            return trimmed;
        }

        private static void AddOperandReference(ObjectTrimmer trimmer, int offset, X86Operand operand)
        {
            if (!string.IsNullOrEmpty(operand.Symbol))
                trimmer.AddReference(TextSectionName, offset, operand.Symbol!);
        }

        private static int SectionOriginalSize(X86Program runtime, string sectionName, int textSize)
        {
            if (StringComparer.Ordinal.Equals(sectionName, TextSectionName))
                return textSize;
            foreach (var section in runtime.DataSections)
            {
                if (StringComparer.Ordinal.Equals(section.Name, sectionName))
                    return checked(section.Data.Length + section.BssSize);
            }
            return 0;
        }

        public static string ResolveInternalCall(RuntimeMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));
            if (!method.HasInternalCall)
                throw new ArgumentException("Method is not marked InternalCall.", nameof(method));

            if (IsSystemType(method.DeclaringType, "Array"))
            {
                if (method.HasThis &&
                   !method.IsStatic &&
                    method.ParameterTypes.Length == 0 &&
                    method.ReturnType.PrimitiveKind == RuntimePrimitiveKind.Int32 &&
                    StringComparer.Ordinal.Equals(method.Name, "get_Length") &&
                    StringComparer.Ordinal.Equals(method.DeclaringType.Namespace, "System") &&
                    StringComparer.Ordinal.Equals(method.DeclaringType.Name, "Array"))
                    return ArrayGetLengthSymbol;

                if (method.IsStatic &&
                    StringComparer.Ordinal.Equals(method.Name, "ClearInternal") &&
                    method.ParameterTypes.Length == 3 &&
                    IsSystemType(method.ParameterTypes[0], "Array") &&
                    IsSystemType(method.ParameterTypes[1], "Int32") &&
                    IsSystemType(method.ParameterTypes[2], "Int32") &&
                    IsVoid(method.ReturnType))
                {
                    return ArrayClearSymbol;
                }

                if (method.IsStatic &&
                    StringComparer.Ordinal.Equals(method.Name, "CopyInternal") &&
                    method.ParameterTypes.Length == 5 &&
                    IsSystemType(method.ParameterTypes[0], "Array") &&
                    IsSystemType(method.ParameterTypes[1], "Int32") &&
                    IsSystemType(method.ParameterTypes[2], "Array") &&
                    IsSystemType(method.ParameterTypes[3], "Int32") &&
                    IsSystemType(method.ParameterTypes[4], "Int32") &&
                    IsSystemType(method.ReturnType, "Boolean"))
                {
                    return ArrayCopySymbol;
                }
            }

            if (IsSystemType(method.DeclaringType, "Console") &&
                StringComparer.Ordinal.Equals(method.Name, "_Write") &&
                method.IsStatic &&
                IsVoid(method.ReturnType) &&
                method.ParameterTypes.Length == 1)
            {
                var parameter = method.ParameterTypes[0];
                if (parameter.Kind == RuntimeTypeKind.Pointer && IsChar(parameter.ElementType))
                    return ConsoleWriteUtf16ZSymbol;
                if (IsSystemType(parameter, "String"))
                    return ConsoleWriteStringSymbol;
                if (IsReadOnlyCharSpan(parameter))
                    return ConsoleWriteUtf16Symbol;
            }

            if (StringComparer.Ordinal.Equals(method.DeclaringType.Namespace, "System") &&
                StringComparer.Ordinal.Equals(method.DeclaringType.Name, "SpanHelpers") &&
                StringComparer.Ordinal.Equals(method.Name, "memset") &&
                method.IsStatic &&
                !method.HasThis &&
                method.ParameterTypes.Length == 3 &&
                method.ParameterTypes[0].Kind == RuntimeTypeKind.Pointer &&
                IsSystemType(method.ParameterTypes[1], "Int32") &&
                IsSystemType(method.ParameterTypes[2], "UIntPtr") &&
                IsVoid(method.ReturnType))
            {
                return MemsetSymbol;
            }

            if (StringComparer.Ordinal.Equals(method.DeclaringType.Namespace, "System.Threading") &&
                StringComparer.Ordinal.Equals(method.DeclaringType.Name, "Thread") &&
                StringComparer.Ordinal.Equals(method.Name, "GetCurrentProcessorNumber") &&
                method.IsStatic &&
                !method.HasThis &&
                method.ParameterTypes.Length == 0 &&
                IsSystemType(method.ReturnType, "Int32"))
            {
                return GetCurrentProcessorNumberSymbol;
            }

            if (StringComparer.Ordinal.Equals(method.DeclaringType.Namespace, "System.Threading") &&
                StringComparer.Ordinal.Equals(method.DeclaringType.Name, "ObjectHeader") &&
                method.IsStatic &&
                !method.HasThis &&
                method.ParameterTypes.Length == 1 &&
                IsSystemType(method.ParameterTypes[0], "Object") &&
                IsVoid(method.ReturnType))
            {
                if (StringComparer.Ordinal.Equals(method.Name, "AcquireThinLock"))
                    return MonitorEnterSymbol;
                if (StringComparer.Ordinal.Equals(method.Name, "Release"))
                    return MonitorExitSymbol;
            }

            if (StringComparer.Ordinal.Equals(method.DeclaringType.Namespace, "System.Runtime.InteropServices") &&
                StringComparer.Ordinal.Equals(method.DeclaringType.Name, "Marshal") &&
                method.IsStatic &&
                !method.HasThis &&
                method.ParameterTypes.Length == 1 &&
                IsSystemType(method.ParameterTypes[0], "IntPtr"))
            {
                if (StringComparer.Ordinal.Equals(method.Name, "AllocHGlobal") &&
                    IsSystemType(method.ReturnType, "IntPtr"))
                {
                    return AllocHGlobalSymbol;
                }

                if (StringComparer.Ordinal.Equals(method.Name, "FreeHGlobal") &&
                    IsVoid(method.ReturnType))
                {
                    return FreeHGlobalSymbol;
                }
            }

            if (IsAllocateNewArrayInternalCall(method))
                return NewUninitializedArraySymbol;

            throw new MissingMethodException(
                $"InternalCall implementation is missing: {method.DeclaringType.Namespace}.{method.DeclaringType.Name}.{method.Name}");
        }

        public static bool TryEvaluateIsReferenceOrContainsReferences(RuntimeMethod method, out bool result)
        {
            result = false;
            if (method is null ||
                !method.HasInternalCall ||
                !method.IsStatic ||
                method.HasThis ||
                !StringComparer.Ordinal.Equals(method.DeclaringType.Namespace, "System.Runtime.CompilerServices") ||
                !StringComparer.Ordinal.Equals(method.DeclaringType.Name, "RuntimeHelpers") ||
                !StringComparer.Ordinal.Equals(method.Name, "IsReferenceOrContainsReferences") ||
                method.ParameterTypes.Length != 0 ||
                method.MethodGenericArguments.Length != 1 ||
                !IsSystemType(method.ReturnType, "Boolean"))
            {
                return false;
            }

            RuntimeType type = method.MethodGenericArguments[0];
            result = type.IsReferenceType ||
                     type.Kind == RuntimeTypeKind.ByRef ||
                     type.ContainsGcPointers;
            return true;
        }

        public static bool IsAllocateNewArrayInternalCall(RuntimeMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            return method.HasInternalCall &&
                   method.IsStatic &&
                   !method.HasThis &&
                   StringComparer.Ordinal.Equals(method.DeclaringType.Namespace, "System.Runtime") &&
                   StringComparer.Ordinal.Equals(method.DeclaringType.Name, "RuntimeImports") &&
                   StringComparer.Ordinal.Equals(method.Name, "RhAllocateNewArray") &&
                   method.ParameterTypes.Length == 2 &&
                   IsSystemType(method.ParameterTypes[0], "Int32") &&
                   IsSystemType(method.ParameterTypes[1], "UInt32") &&
                   method.ReturnType.Kind == RuntimeTypeKind.Array &&
                   method.ReturnType.IsSzArray;
        }

        public static bool IsGcSafePointInternalCall(RuntimeMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            if (IsAllocateNewArrayInternalCall(method))
                return true;

            return method.HasInternalCall &&
                   method.IsStatic &&
                   IsSystemType(method.DeclaringType, "String") &&
                   IsSystemType(method.ReturnType, "String") &&
                   StringComparer.Ordinal.Equals(method.Name, "FastAllocateString") &&
                   method.ParameterTypes.Length == 1 &&
                   IsSystemType(method.ParameterTypes[0], "Int32");
        }

        private static bool IsReadOnlyCharSpan(RuntimeType type)
        {
            if (!StringComparer.Ordinal.Equals(type.Namespace, "System") ||
                !type.Name.StartsWith("ReadOnlySpan", StringComparison.Ordinal))
            {
                return false;
            }

            var arguments = type.GenericTypeArguments;
            return arguments.Length == 1 && IsChar(arguments[0]);
        }

        private static bool IsChar(RuntimeType? type)
            => type is not null &&
               (type.PrimitiveKind == RuntimePrimitiveKind.Char || IsSystemType(type, "Char"));

        private static bool IsVoid(RuntimeType type)
            => type.PrimitiveKind == RuntimePrimitiveKind.Void || IsSystemType(type, "Void");

        private static bool IsSystemType(RuntimeType type, string name)
            => StringComparer.Ordinal.Equals(type.Namespace, "System") &&
               StringComparer.Ordinal.Equals(type.Name, name);

        private static X86Program Compile(TargetInfo target)
        {
            var cTarget = Cnidaria.C.TargetInfo
                .ForArchitecture(target.Architecture, target.OperatingSystem, target.ArchitectureFeatures)
                .WithFeatures(target.ArchitectureFeatures);
            string source = ReadRuntimeSource("CLRSource.c");
            var compilation = Cnidaria.C.Compilation.CreateFromSource(
                source,
                filePath: $"runtime/{(target.Is32Bit ? "x86" : "x64")}_{(target.OperatingSystem == OperatingSystemKind.Windows ? "windows" : "linux")}_runtime.c",
                includeStandardHeaders: false,
                options: new Cnidaria.C.CompilationOptions(cTarget));
            var errors = compilation.GetDiagnostics()
                .Where(static diagnostic => diagnostic.Severity == Cnidaria.C.DiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.GetMessage(source))
                .ToArray();
            if (errors.Length != 0)
                throw new InvalidOperationException($"x86 runtime compilation failed: {string.Join("\n", errors)}");

            var semanticModel = compilation.GetSemanticModel(compilation.SyntaxTrees[0]);
            var gimple = Cnidaria.C.GimplePipeline.Run(semanticModel);
            var lir = Cnidaria.C.LirModule.Lower(gimple);
            return Cnidaria.C.X86CodeGenerator.Generate(
                lir,
                options: new Cnidaria.C.X86CodeGeneratorOptions
                {
                    EmitStartup = false,
                    EntryFunctionName = InitializeSymbol,
                });
        }
        private static string ReadRuntimeSource(string fileName)
        {
            var asm = typeof(X86Runtime).Assembly;
            string resourceName = $"Cnidaria.Cs.Backend.CLR.{fileName}";
            using (var s = asm.GetManifestResourceStream(resourceName))
            {
                if (s != null)
                {
                    using var r = new StreamReader(s, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    return r.ReadToEnd();
                }
            }

            throw new FileNotFoundException($"CLR source not found: {fileName}");
        }
    }
}
