using Cnidaria.X86;
using System;
using System.Collections.Concurrent;
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
        public const string AllocHGlobalSymbol = "RhpAllocHGlobal";
        public const string FreeHGlobalSymbol = "RhpFreeHGlobal";
        public const string DelegateCombineSymbol = "RhpDelegateCombine";
        public const string DelegateRemoveSymbol = "RhpDelegateRemove";
        public const string ArrayGetLengthSymbol = "RhpArrayGetLength";
        public const string ArrayClearSymbol = "RhpArrayClear";
        public const string ArrayCopySymbol = "RhpArrayCopy";
        public const string NewStringFromCharSymbol = "RhpNewStringFromChar";
        public const string NewStringFromUtf16Symbol = "RhpNewStringFromUtf16";
        public const string NewStringFromCharArraySymbol = "RhpNewStringFromCharArray";
        public const string NewStringFromCharArrayRangeSymbol = "RhpNewStringFromCharArrayRange";
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
        private const string StringGetLengthSymbol = "RhpStringGetLength";
        private const string StringGetDataSymbol = "RhpStringGetData";
        private const string ConsoleWriteUtf16Symbol = "RhpConsoleWriteUtf16";
        private const string ConsoleWriteUtf16ZSymbol = "RhpConsoleWriteUtf16Z";
        private const string ConsoleWriteStringSymbol = "RhpConsoleWriteString";

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

            if (IsSystemType(method.DeclaringType, "String"))
            {
                if (method.HasThis &&
                    StringComparer.Ordinal.Equals(method.Name, "get_Length") &&
                    method.ParameterTypes.Length == 0)
                {
                    return StringGetLengthSymbol;
                }

                if (method.HasThis &&
                    (StringComparer.Ordinal.Equals(method.Name, "GetPinnableReference") ||
                     StringComparer.Ordinal.Equals(method.Name, "GetRawStringData")) &&
                    method.ParameterTypes.Length == 0)
                {
                    return StringGetDataSymbol;
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

        public static bool IsGcSafePointInternalCall(RuntimeMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

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
            var cfg = Cnidaria.C.ControlFlowGraph.Build(semanticModel);
            var ssa = Cnidaria.C.SsaGraph.Build(cfg);
            var lir = Cnidaria.C.LirModule.Lower(ssa);
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
