using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Cnidaria.Cs
{
    public static class CSharp
    {
        private static bool HasErrors(List<IDiagnostic> diags)
            => diags.Any(x => x.GetSeverity() == DiagnosticSeverity.Error);

        private static void AddDiagnostics<TDiagnostic>(List<IDiagnostic> target, IEnumerable<TDiagnostic> source)
        {
            foreach (var diagnostic in source)
            {
                object? boxed = diagnostic;
                if (boxed is IDiagnostic typed)
                {
                    target.Add(typed);
                }
                else if (boxed != null)
                {
                    target.Add(new Diagnostic("DIAGNOSTIC", DiagnosticSeverity.Error, boxed.ToString() ?? string.Empty, default));
                }
            }
        }
        public readonly static (IMetadataView meta, Dictionary<int, Cnidaria.Cs.BytecodeFunction> funcs)
            StandartLibrary = CompileCoreLibrary(GetCoreBCLSource());
        public readonly static (IMetadataView meta, Dictionary<int, Cnidaria.Cs.BytecodeFunction> funcs, List<IDiagnostic> diags)
            ExtendedLibrary = CompileLibrary(GetExtendedBCLSource(), "extendedStd");
        public readonly struct ExecutionContext
        {
            public readonly long InstructionsCount;
            public readonly TimeSpan TimeElapsed;
            public readonly long StackMemoryUsed;
            public readonly long HeapMemoryUsed;
            public ExecutionContext(long instructionsCount, TimeSpan timeElapsed, long stackMemoryUsed, long heapMemoryUsed)
            {
                InstructionsCount = instructionsCount;
                TimeElapsed = timeElapsed;
                StackMemoryUsed = stackMemoryUsed;
                HeapMemoryUsed = heapMemoryUsed;
            }
            public static ExecutionContext Empty => new ExecutionContext(-1, TimeSpan.MinValue, -1, -1);
        }
        public static (byte[]? image, List<IDiagnostic> diagnostics) CompileStackApplicationToRunnableBytes(
            string source,
            byte[]? externalLibImage = null)
        {
            var diagnostics = new List<IDiagnostic>(ExtendedLibrary.diags);

            try
            {
                var parser = new Parser(source);
                var root = parser.Parse();
                AddDiagnostics(diagnostics, parser.LexerDiagnostics);
                AddDiagnostics(diagnostics, parser.Diagnostics);
                if (HasErrors(diagnostics))
                    return (null, diagnostics);

                IMetadataView? externalMeta = null;
                if (externalLibImage != null)
                {
                    var (_, meta, _) = BytecodeSerializer.DeserializeCompiledModule(externalLibImage);
                    externalMeta = meta;
                }

                var tree = new SyntaxTree(root, "app");
                var trees = ImmutableArray.Create(tree);
                var refs = externalMeta != null
                    ? new MetadataReferenceSet(new[] { StandartLibrary.meta, ExtendedLibrary.meta, externalMeta })
                    : new MetadataReferenceSet(new[] { StandartLibrary.meta, ExtendedLibrary.meta });

                var compilation = CompilationFactory.Create(trees, refs, out var declDiag);
                AddDiagnostics(diagnostics, declDiag);
                if (HasErrors(diagnostics))
                    return (null, diagnostics);

                var (md, builtFuncs, diags, ex) = compilation.BuildModule(
                    moduleName: "app",
                    tree: tree,
                    includeCoreTypesInTypeDefs: false,
                    defaultExternalAssemblyName: "std",
                    externalAssemblyResolver: refs.ResolveAssemblyName,
                    print: false);

                if (ex != null)
                    diagnostics.Add(new Diagnostic("BUILD", DiagnosticSeverity.Error, ex.ToString(), default));
                AddDiagnostics(diagnostics, diags);
                if (HasErrors(diagnostics))
                    return (null, diagnostics);

                byte[] flatMd = FlatMetadataBuilder.Build(md);
                return (BytecodeSerializer.SerializeCompiledModule(flatMd, builtFuncs), diagnostics);
            }
            catch (Exception ex)
            {
                diagnostics.Add(new Diagnostic("INTERNAL", DiagnosticSeverity.Error, ex.ToString(), default));
                return (null, diagnostics);
            }
        }

        public static (string output, List<IDiagnostic> diagnostics, ExecutionContext context) InterpretOnStack(
            byte[] runnableAppImage,
            CancellationTokenSource cts,
            int heapSize = 32 * 1024,
            int stackSize = 4 * 1024,
            int staticRegionLimit = 0,
            int outputLimit = 4 * 1024 - 1,
            ExecutionLimits? execLimits = null,
            byte[]? externalLibImage = null,
            Action<Cnidaria.Cs.HostInterface>? host = null,
            Action<string>? streamAction = null,
            string? entryAttributeTypeName = null,
            string[]? entryAttributeArgs = null,
            string[]? entryMethodArgs = null)
        {
            execLimits ??= new ExecutionLimits();
            var output = new StringBuilder();
            var diagnostics = new List<IDiagnostic>(ExtendedLibrary.diags);

            try
            {
                var (_, appMeta, appFuncs) = BytecodeSerializer.DeserializeCompiledModule(runnableAppImage);
                (IMetadataView meta, Dictionary<int, BytecodeFunction> functions)? external = null;
                if (externalLibImage != null)
                {
                    var (_, meta, externalFuncs) = BytecodeSerializer.DeserializeCompiledModule(externalLibImage);
                    external = (meta, externalFuncs);
                }

                var domain = new Domain();
                var stdModule = new RuntimeModule(StandartLibrary.meta.ModuleName, StandartLibrary.meta, StandartLibrary.funcs);
                var extStdModule = new RuntimeModule(ExtendedLibrary.meta.ModuleName, ExtendedLibrary.meta, ExtendedLibrary.funcs);
                var appModule = new RuntimeModule(appMeta.ModuleName, appMeta, appFuncs);
                var modules = new Dictionary<string, RuntimeModule>(StringComparer.Ordinal);

                void AddUnique(RuntimeModule m)
                {
                    if (!modules.TryAdd(m.Name, m))
                        throw new InvalidOperationException($"Duplicate module loaded: '{m.Name}'");
                    domain.Add(m);
                }

                AddUnique(stdModule);
                AddUnique(extStdModule);
                if (external != null)
                    AddUnique(new RuntimeModule(external.Value.meta.ModuleName, external.Value.meta, external.Value.functions));
                AddUnique(appModule);

                int entryTok;
                object?[]? selectedValues = null;
                if (string.IsNullOrWhiteSpace(entryAttributeTypeName))
                {
                    entryTok = BytecodeBuilder.FindEntryPointMethodDef(appModule);
                }
                else
                {
                    var attrArgs = entryAttributeArgs ?? Array.Empty<string>();
                    var callArgs = entryMethodArgs ?? Array.Empty<string>();
                    if (!TryResolveAttributedEntryPoint(appMeta, entryAttributeTypeName!, attrArgs, callArgs, out entryTok, out selectedValues, out var err))
                    {
                        var diagnostic = new Diagnostic("ENTRYPOINT", DiagnosticSeverity.Error, err, default);
                        diagnostics.Add(diagnostic);
                        output.AppendLine(diagnostic.GetMessage());
                        return (output.ToString(), diagnostics, ExecutionContext.Empty);
                    }
                }

                var rts = new RuntimeTypeSystem(modules);
                byte[] mem = new byte[stackSize + heapSize + staticRegionLimit];
                int staticEnd = staticRegionLimit;
                int stackBase = staticEnd;
                int stackEnd = stackBase + stackSize;

                var sb = new StringBuilder();
                using var stringWriter = new StringWriter(sb);
                using var writer = new BoundedTextWriter(
                    inner: stringWriter,
                    maxChars: outputLimit,
                    mode: BoundedTextWriter.OverflowMode.Truncate,
                    onChunk: streamAction,
                    streamOnlyWhatWasWritten: true);

                var stVm = new StackBasedVm(
                    memory: mem,
                    staticEnd: staticEnd,
                    stackBase: stackBase,
                    stackEnd: stackEnd,
                    domain: domain,
                    rts: rts,
                    modules: modules,
                    textWriter: writer);

                var entryFn = appModule.MethodsByDefToken[entryTok];
                Slot[]? initialArgs = null;
                if (selectedValues != null)
                {
                    var rm = rts.ResolveMethod(appModule, entryTok);
                    if (!rm.IsStatic || rm.HasThis)
                        throw new InvalidOperationException("Command entrypoints must be static.");
                    initialArgs = BuildStackInitialArgs(stVm, rts, rm, selectedValues);
                }

                host?.Invoke(new HostInterface(stVm, rts, modules));

                var sw = Stopwatch.StartNew();
                if (initialArgs != null)
                    stVm.Execute(appModule, entryFn, cts.Token, execLimits, initialArgs);
                else
                    stVm.Execute(appModule, entryFn, cts.Token, execLimits);
                sw.Stop();

                return (
                    sb.ToString(),
                    diagnostics,
                    new ExecutionContext(stVm.InctructionsElapsed, sw.Elapsed, stVm.StackPeakBytes, stVm.HeapPeakBytes));
            }
            catch (Exception ex)
            {
                diagnostics.Add(new Diagnostic("INTERNAL", DiagnosticSeverity.Error, ex.ToString(), default));
                return (output.ToString(), diagnostics, ExecutionContext.Empty);
            }
        }
        /// <summary>
        /// Compiles source code to register based bytecode image as a byte array
        /// </summary>
        public static (byte[]? image, List<IDiagnostic> diagnostics) CompileApplicationToRunnableBytes(
            string source,
            byte[]? externalLibImage = null)
        {
            var diagnostics = new List<IDiagnostic>(ExtendedLibrary.diags);

            try
            {
                var parser = new Parser(source);
                var root = parser.Parse();
                AddDiagnostics(diagnostics, parser.LexerDiagnostics);
                AddDiagnostics(diagnostics, parser.Diagnostics);
                if (HasErrors(diagnostics))
                    return (null, diagnostics);

                (IMetadataView meta, Dictionary<int, BytecodeFunction> functions)? external = null;
                if (externalLibImage != null)
                {
                    var (_, meta, externalFuncs) = BytecodeSerializer.DeserializeCompiledModule(externalLibImage);
                    external = (meta, externalFuncs);
                }

                var tree = new SyntaxTree(root, "app");
                var trees = ImmutableArray.Create(tree);
                var refs = external != null
                    ? new MetadataReferenceSet(new[] { StandartLibrary.meta, ExtendedLibrary.meta, external.Value.meta })
                    : new MetadataReferenceSet(new[] { StandartLibrary.meta, ExtendedLibrary.meta });

                var compilation = CompilationFactory.Create(trees, refs, out var declDiag);
                AddDiagnostics(diagnostics, declDiag);
                if (HasErrors(diagnostics))
                    return (null, diagnostics);

                var (md, builtFuncs, diags, ex) = compilation.BuildModule(
                    moduleName: "app",
                    tree: tree,
                    includeCoreTypesInTypeDefs: false,
                    defaultExternalAssemblyName: "std",
                    externalAssemblyResolver: refs.ResolveAssemblyName,
                    print: false);

                if (ex != null)
                    diagnostics.Add(new Diagnostic("BUILD", DiagnosticSeverity.Error, ex.ToString(), default));
                AddDiagnostics(diagnostics, diags);
                if (HasErrors(diagnostics))
                    return (null, diagnostics);

                byte[] flatMd = FlatMetadataBuilder.Build(md);
                IMetadataView appMeta = new FlatMetadataView(flatMd);

                var stdModule = new RuntimeModule(StandartLibrary.meta.ModuleName, StandartLibrary.meta, StandartLibrary.funcs);
                var extStdModule = new RuntimeModule(ExtendedLibrary.meta.ModuleName, ExtendedLibrary.meta, ExtendedLibrary.funcs);
                var appModule = new RuntimeModule(appMeta.ModuleName, appMeta, builtFuncs);
                var modules = new Dictionary<string, RuntimeModule>(StringComparer.Ordinal);

                void AddUnique(RuntimeModule m)
                {
                    if (!modules.TryAdd(m.Name, m))
                        throw new InvalidOperationException($"Duplicate module loaded: '{m.Name}'");
                }

                AddUnique(stdModule);
                AddUnique(extStdModule);
                if (external != null)
                    AddUnique(new RuntimeModule(external.Value.meta.ModuleName, external.Value.meta, external.Value.functions));
                AddUnique(appModule);

                var rts = new RuntimeTypeSystem(modules);
                ImmutableArray<int> entryRoots = FindRunnableRegisterRoots(appMeta);
                //int entryTok = BytecodeBuilder.FindEntryPointMethodDef(appModule);
                var genTreeProgram = GenTreeBuilder.BuildReachableProgram(modules, rts, appModule, entryRoots);
                var backend = BackendPipeline.CompileProgram(genTreeProgram);
                byte[] registerImage = ImageSerializer.ToBytes(backend.Image);
                byte[] stackFunctions = BytecodeSerializer.SerializeStackFunctions(builtFuncs);

                return (SerializeRegisterRunnableApplication(flatMd, stackFunctions, registerImage), diagnostics);
            }
            catch (Exception ex)
            {
                diagnostics.Add(new Diagnostic("INTERNAL", DiagnosticSeverity.Error, ex.ToString(), default));
                return (null, diagnostics);
            }
        }
        private static ImmutableArray<int> FindRunnableRegisterRoots(IMetadataView appMeta)
        {
            if (appMeta is null)
                throw new ArgumentNullException(nameof(appMeta));

            var roots = ImmutableArray.CreateBuilder<int>();
            var seen = new HashSet<int>();

            void Add(int methodToken)
            {
                if (MetadataToken.Table(methodToken) != MetadataToken.MethodDef)
                    return;

                int rid = MetadataToken.Rid(methodToken);
                if (rid <= 0 || rid > appMeta.GetRowCount(MetadataTableKind.MethodDef))
                    return;

                if (seen.Add(methodToken))
                    roots.Add(methodToken);
            }

            if (BytecodeBuilder.TryFindEntryPointMethodDef(appMeta, out int mainToken))
                Add(mainToken);

            var attributesByParent = BuildAttributeMap(appMeta);

            foreach (int methodToken in EnumerateMethodDefTokens(appMeta))
            {
                if (!attributesByParent.TryGetValue(methodToken, out var attrs))
                    continue;

                for (int i = 0; i < attrs.Count; i++)
                {
                    var attr = attrs[i];

                    if (attr.Target != AttributeApplicationTarget.Method)
                        continue;

                    string name = NormalizeAttrName(attr.Name);

                    if (StringComparer.Ordinal.Equals(name, "Command") ||
                        StringComparer.Ordinal.Equals(name, "Button"))
                    {
                        Add(methodToken);
                        break;
                    }
                }
            }

            if (roots.Count == 0)
                throw new InvalidOperationException("No runnable entry roots found in module metadata.");

            return roots.ToImmutable();
        }
        public static (string output, List<IDiagnostic> diagnostics, ExecutionContext context) Interpret(
            byte[] runnableAppImage,
            CancellationTokenSource cts,
            int heapSize = 32 * 1024,
            int stackSize = 4 * 1024,
            int staticRegionLimit = 0,
            int outputLimit = 4 * 1024 - 1,
            ExecutionLimits? execLimits = null,
            byte[]? externalLibImage = null,
            Action<Cnidaria.Cs.HostInterface>? host = null,
            Action<string>? streamAction = null,
            string? entryAttributeTypeName = null,
            string[]? entryAttributeArgs = null,
            string[]? entryMethodArgs = null)
        {
            execLimits ??= new ExecutionLimits();
            var output = new StringBuilder();
            var diagnostics = new List<IDiagnostic>(ExtendedLibrary.diags);

            try
            {
                var app = DeserializeRegisterRunnableApplication(runnableAppImage);
                var appMeta = new FlatMetadataView(app.flatMetadata);
                var appFuncs = BytecodeSerializer.DeserializeFunctions(app.stackFunctions);
                var image = ImageSerializer.FromBytes(app.registerImage);

                (IMetadataView meta, Dictionary<int, BytecodeFunction> functions)? external = null;
                if (externalLibImage != null)
                {
                    var (_, meta, externalFuncs) = BytecodeSerializer.DeserializeCompiledModule(externalLibImage);
                    external = (meta, externalFuncs);
                }

                var stdModule = new RuntimeModule(StandartLibrary.meta.ModuleName, StandartLibrary.meta, StandartLibrary.funcs);
                var extStdModule = new RuntimeModule(ExtendedLibrary.meta.ModuleName, ExtendedLibrary.meta, ExtendedLibrary.funcs);
                var appModule = new RuntimeModule(appMeta.ModuleName, appMeta, appFuncs);
                var modules = new Dictionary<string, RuntimeModule>(StringComparer.Ordinal);

                void AddUnique(RuntimeModule m)
                {
                    if (!modules.TryAdd(m.Name, m))
                        throw new InvalidOperationException($"Duplicate module loaded: '{m.Name}'");
                }

                AddUnique(stdModule);
                AddUnique(extStdModule);
                if (external != null)
                    AddUnique(new RuntimeModule(external.Value.meta.ModuleName, external.Value.meta, external.Value.functions));
                AddUnique(appModule);

                int entryTok;
                object?[]? selectedValues = null;
                if (string.IsNullOrWhiteSpace(entryAttributeTypeName))
                {
                    entryTok = BytecodeBuilder.FindEntryPointMethodDef(appModule);
                }
                else
                {
                    var attrArgs = entryAttributeArgs ?? Array.Empty<string>();
                    var callArgs = entryMethodArgs ?? Array.Empty<string>();
                    if (!TryResolveAttributedEntryPoint(appMeta, entryAttributeTypeName!, attrArgs, callArgs, out entryTok, out selectedValues, out var err))
                    {
                        var diagnostic = new Diagnostic("ENTRYPOINT", DiagnosticSeverity.Error, err, default);
                        diagnostics.Add(diagnostic);
                        output.AppendLine(diagnostic.GetMessage());
                        return (output.ToString(), diagnostics, ExecutionContext.Empty);
                    }
                }

                var rts = new RuntimeTypeSystem(modules);
                HydrateRegisterRuntimeIds(modules, rts, appModule, appMeta);
                var entryRuntimeMethod = rts.ResolveMethod(appModule, entryTok);

                byte[] mem = GC.AllocateUninitializedArray<byte>(stackSize + heapSize + staticRegionLimit);
                int staticEnd = staticRegionLimit;
                int stackBase = staticEnd;
                int stackEnd = stackBase + stackSize;

                var sb = new StringBuilder();
                using var stringWriter = new StringWriter(sb);
                using var writer = new BoundedTextWriter(
                    inner: stringWriter,
                    maxChars: outputLimit,
                    mode: BoundedTextWriter.OverflowMode.Truncate,
                    onChunk: streamAction,
                    streamOnlyWhatWasWritten: true);

                var regVm = new RegisterBasedVm(
                    memory: mem,
                    staticEnd: staticEnd,
                    stackBase: stackBase,
                    stackEnd: stackEnd,
                    rts: rts,
                    modules: modules,
                    image: image,
                    textWriter: writer);

                VmValue[]? initialArgs = null;
                if (selectedValues != null)
                    initialArgs = BuildRegisterInitialArgs(regVm, rts, entryRuntimeMethod, selectedValues);

                host?.Invoke(new HostInterface(regVm, rts, modules));

                var sw = Stopwatch.StartNew();
                if (initialArgs != null)
                    regVm.Execute(entryRuntimeMethod, cts.Token, execLimits, initialArgs);
                else
                    regVm.Execute(entryRuntimeMethod, cts.Token, execLimits);
                sw.Stop();

                return (
                    sb.ToString(),
                    diagnostics,
                    new ExecutionContext(regVm.InctructionsElapsed, sw.Elapsed, regVm.StackPeakBytes, regVm.HeapPeakBytes));
            }
            catch (Exception ex)
            {
                diagnostics.Add(new Diagnostic("INTERNAL", DiagnosticSeverity.Error, ex.ToString(), default));
                return (output.ToString(), diagnostics, ExecutionContext.Empty);
            }
        }
        private static void HydrateRegisterRuntimeIds(
            IReadOnlyDictionary<string, RuntimeModule> modules,
            RuntimeTypeSystem rts,
            RuntimeModule appModule,
            IMetadataView appMeta)
        {
            ImmutableArray<int> entryRoots = FindRunnableRegisterRoots(appMeta);
            _ = GenTreeBuilder.BuildReachableProgram(modules, rts, appModule, entryRoots);
        }
        private static byte[] SerializeRegisterRunnableApplication(byte[] flatMetadata, byte[] stackFunctions, byte[] registerImage)
        {
            using var ms = new MemoryStream(24 + flatMetadata.Length + stackFunctions.Length + registerImage.Length);
            using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
            bw.Write((ushort)0);
            bw.Write(flatMetadata.Length);
            bw.Write(stackFunctions.Length);
            bw.Write(registerImage.Length);
            bw.Write(flatMetadata);
            bw.Write(stackFunctions);
            bw.Write(registerImage);
            bw.Flush();
            return ms.ToArray();
        }

        private static (byte[] flatMetadata, byte[] stackFunctions, byte[] registerImage) DeserializeRegisterRunnableApplication(byte[] image)
        {
            using var ms = new MemoryStream(image, writable: false);
            using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
            _ = br.ReadUInt16();
            int metadataSize = br.ReadInt32();
            int stackFunctionsSize = br.ReadInt32();
            int registerImageSize = br.ReadInt32();
            if (metadataSize < 0 || stackFunctionsSize < 0 || registerImageSize < 0)
                throw new InvalidDataException("Negative register runnable section size.");
            byte[] metadata = br.ReadBytes(metadataSize);
            byte[] stackFunctions = br.ReadBytes(stackFunctionsSize);
            byte[] registerImage = br.ReadBytes(registerImageSize);
            if (metadata.Length != metadataSize || stackFunctions.Length != stackFunctionsSize || registerImage.Length != registerImageSize)
                throw new EndOfStreamException("Truncated register runnable image.");
            if (ms.Position != ms.Length)
                throw new InvalidDataException("Trailing bytes found in register runnable image.");
            return (metadata, stackFunctions, registerImage);
        }

        private readonly struct MetadataEntryParameterSpec
        {
            public readonly SpecialType SpecialType;
            public readonly bool IsParamsStringArray;
            public readonly bool HasDefault;
            public readonly object? DefaultValue;

            public MetadataEntryParameterSpec(
                SpecialType specialType,
                bool isParamsStringArray,
                bool hasDefault,
                object? defaultValue)
            {
                SpecialType = specialType;
                IsParamsStringArray = isParamsStringArray;
                HasDefault = hasDefault;
                DefaultValue = defaultValue;
            }
        }

        private readonly struct MetadataAttributeSpec
        {
            public readonly string Namespace;
            public readonly string Name;
            public readonly string[] CtorArgs;
            public readonly AttributeApplicationTarget Target;

            public MetadataAttributeSpec(
                string @namespace,
                string name,
                string[] ctorArgs,
                AttributeApplicationTarget target)
            {
                Namespace = @namespace ?? string.Empty;
                Name = name ?? string.Empty;
                CtorArgs = ctorArgs ?? Array.Empty<string>();
                Target = target;
            }
        }




        private static bool TryBindPositionalArguments(
            MetadataEntryParameterSpec[] ps,
            string[] callArgs,
            out object?[] values,
            out int cost,
            out int defaultsUsed,
            out bool usedParams)
        {
            cost = 0;
            defaultsUsed = 0;
            usedParams = false;
            values = new object?[ps.Length];

            int ai = 0;
            bool hasParams = ps.Length > 0 && ps[^1].IsParamsStringArray;
            int fixedCount = hasParams ? ps.Length - 1 : ps.Length;

            for (int pi = 0; pi < fixedCount; pi++)
            {
                var p = ps[pi];

                if (ai < callArgs.Length)
                {
                    if (!TryParseStringToSpecialType(callArgs[ai], p.SpecialType, out var v, out var c))
                        return false;

                    values[pi] = v;
                    cost += c;
                    ai++;
                }
                else
                {
                    if (!p.HasDefault)
                        return false;

                    values[pi] = p.DefaultValue;
                    defaultsUsed++;
                }
            }

            if (hasParams)
            {
                usedParams = true;

                int rest = callArgs.Length - ai;
                if (rest < 0) rest = 0;

                var arr = new string?[rest];
                for (int i = 0; i < rest; i++)
                    arr[i] = callArgs[ai + i];

                values[^1] = arr;
                cost += 5;
                ai += rest;
            }

            return ai == callArgs.Length;
        }

        private static bool TryParseStringToSpecialType(string token, SpecialType st, out object? value, out int cost)
        {
            value = null;
            cost = 0;

            var k = ClassifyArg(token);

            switch (st)
            {
                case SpecialType.System_String:
                    value = token;
                    cost = (k == ArgLexKind.Other) ? 0 : 10;
                    return true;

                case SpecialType.System_Boolean:
                    if (bool.TryParse(token, out var b))
                    {
                        value = b;
                        return true;
                    }
                    return false;

                case SpecialType.System_Char:
                    if (token.Length == 1)
                    {
                        value = token[0];
                        return true;
                    }
                    return false;

                case SpecialType.System_Int8:
                    return TryParseInt(token, NumberStyles.Integer, out sbyte _, out value, out cost, 0);
                case SpecialType.System_UInt8:
                    return TryParseInt(token, NumberStyles.Integer, out byte _, out value, out cost, 0);
                case SpecialType.System_Int16:
                    return TryParseInt(token, NumberStyles.Integer, out short _, out value, out cost, 1);
                case SpecialType.System_UInt16:
                    return TryParseInt(token, NumberStyles.Integer, out ushort _, out value, out cost, 1);
                case SpecialType.System_Int32:
                    return TryParseInt(token, NumberStyles.Integer, out int _, out value, out cost, 2);
                case SpecialType.System_UInt32:
                    return TryParseInt(token, NumberStyles.Integer, out uint _, out value, out cost, 2);
                case SpecialType.System_Int64:
                    return TryParseInt(token, NumberStyles.Integer, out long _, out value, out cost, 3);
                case SpecialType.System_UInt64:
                    return TryParseInt(token, NumberStyles.Integer, out ulong _, out value, out cost, 3);

                case SpecialType.System_Single:
                    return TryParseFloat(token, out float _, out value, out cost, k, 1);

                case SpecialType.System_Double:
                    return TryParseFloat(token, out double _, out value, out cost, k, 0);

                default:
                    return false;
            }
        }


        public static (string output, List<IDiagnostic> diagnostics, ExecutionContext context) Interpret(
            string source,
            CancellationTokenSource cts,
            int heapSize = 32 * 1024,
            int stackSize = 4 * 1024,
            int metaSize = 0,
            int outputLimit = 4 * 1024 - 1,
            ExecutionLimits? execLimits = null,
            string? externalLibSource = null,
            Action<Cnidaria.Cs.HostInterface>? host = null,
            Action<string>? streamAction = null,
            string? entryAttributeTypeName = null,
            string[]? entryAttributeArgs = null,
            string[]? entryMethodArgs = null)
        {
            execLimits ??= new ExecutionLimits();
            var output = new StringBuilder();
            var diagnostics = new List<IDiagnostic>(ExtendedLibrary.diags);

            try
            {
                (IMetadataView meta, Dictionary<int, BytecodeFunction> functions)? external = null;
                if (externalLibSource != null)
                {
                    var (_, extMeta, extFuncs, extDiags) = CompileLibraryCore(externalLibSource, "external");
                    AddDiagnostics(diagnostics, extDiags);
                    if (HasErrors(diagnostics) || extMeta == null || extFuncs == null)
                        return (string.Empty, diagnostics, ExecutionContext.Empty);
                    external = (extMeta, extFuncs);
                }

                var parser = new Parser(source);
                var root = parser.Parse();
                AddDiagnostics(diagnostics, parser.LexerDiagnostics);
                AddDiagnostics(diagnostics, parser.Diagnostics);
                if (HasErrors(diagnostics))
                    return (string.Empty, diagnostics, ExecutionContext.Empty);

                var tree = new SyntaxTree(root, "app");
                var trees = ImmutableArray.Create(tree);
                var refs = external != null
                    ? new MetadataReferenceSet(new[] { StandartLibrary.meta, ExtendedLibrary.meta, external.Value.meta })
                    : new MetadataReferenceSet(new[] { StandartLibrary.meta, ExtendedLibrary.meta });

                var compilation = CompilationFactory.Create(trees, refs, out var declDiag);
                AddDiagnostics(diagnostics, declDiag);
                if (HasErrors(diagnostics))
                    return (string.Empty, diagnostics, ExecutionContext.Empty);

                var (md, builtFuncs, diags, ex) = compilation.BuildModule(
                    moduleName: "app",
                    tree: tree,
                    includeCoreTypesInTypeDefs: false,
                    defaultExternalAssemblyName: "std",
                    externalAssemblyResolver: refs.ResolveAssemblyName,
                    print: false);

                if (ex != null)
                    diagnostics.Add(new Diagnostic("BUILD", DiagnosticSeverity.Error, ex.ToString(), default));
                AddDiagnostics(diagnostics, diags);
                if (HasErrors(diagnostics))
                    return (string.Empty, diagnostics, ExecutionContext.Empty);

                byte[] flatMd = FlatMetadataBuilder.Build(md);
                IMetadataView appMeta = new FlatMetadataView(flatMd);

                var stdModule = new RuntimeModule(StandartLibrary.meta.ModuleName, StandartLibrary.meta, StandartLibrary.funcs);
                var extStdModule = new RuntimeModule(ExtendedLibrary.meta.ModuleName, ExtendedLibrary.meta, ExtendedLibrary.funcs);
                var appModule = new RuntimeModule(appMeta.ModuleName, appMeta, builtFuncs);
                var modules = new Dictionary<string, RuntimeModule>(StringComparer.Ordinal);

                void AddUnique(RuntimeModule m)
                {
                    if (!modules.TryAdd(m.Name, m))
                        throw new InvalidOperationException($"Duplicate module loaded: '{m.Name}'");
                }

                AddUnique(stdModule);
                AddUnique(extStdModule);
                if (external != null)
                    AddUnique(new RuntimeModule(external.Value.meta.ModuleName, external.Value.meta, external.Value.functions));
                AddUnique(appModule);

                int entryTok;
                object?[]? selectedValues = null;
                if (string.IsNullOrWhiteSpace(entryAttributeTypeName))
                {
                    entryTok = BytecodeBuilder.FindEntryPointMethodDef(appModule);
                }
                else
                {
                    var attrArgs = entryAttributeArgs ?? Array.Empty<string>();
                    var callArgs = entryMethodArgs ?? Array.Empty<string>();
                    if (!TryResolveAttributedEntryPoint(
                        appMeta, entryAttributeTypeName!, attrArgs, callArgs, out entryTok, out selectedValues, out var err))
                    {
                        var diagnostic = new Diagnostic("ENTRYPOINT", DiagnosticSeverity.Error, err, default);
                        diagnostics.Add(diagnostic);
                        output.AppendLine(diagnostic.GetMessage());
                        return (output.ToString(), diagnostics, ExecutionContext.Empty);
                    }
                }

                var rts = new RuntimeTypeSystem(modules);
                var entryRuntimeMethod = rts.ResolveMethod(appModule, entryTok);
                var genTreeProgram = GenTreeBuilder.BuildReachableProgram(modules, rts, appModule, entryTok);
                var backend = BackendPipeline.CompileProgram(genTreeProgram);

                byte[] mem = GC.AllocateUninitializedArray<byte>(stackSize + heapSize + metaSize);
                int staticEnd = metaSize;
                int stackBase = staticEnd;
                int stackEnd = stackBase + stackSize;

                var sb = new StringBuilder();
                using var stringWriter = new StringWriter(sb);
                using var writer = new BoundedTextWriter(
                    inner: stringWriter,
                    maxChars: outputLimit,
                    mode: BoundedTextWriter.OverflowMode.Truncate,
                    onChunk: streamAction,
                    streamOnlyWhatWasWritten: true);

                var regVm = new RegisterBasedVm(
                    memory: mem,
                    staticEnd: staticEnd,
                    stackBase: stackBase,
                    stackEnd: stackEnd,
                    rts: rts,
                    modules: modules,
                    image: backend.Image,
                    textWriter: writer);

                VmValue[]? initialArgs = null;
                if (selectedValues != null)
                    initialArgs = BuildRegisterInitialArgs(regVm, rts, entryRuntimeMethod, selectedValues);

                host?.Invoke(new HostInterface(regVm, rts, modules));

                var sw = Stopwatch.StartNew();
                if (initialArgs != null)
                    regVm.Execute(entryRuntimeMethod, cts.Token, execLimits, initialArgs);
                else
                    regVm.Execute(entryRuntimeMethod, cts.Token, execLimits);
                sw.Stop();

                return (
                    sb.ToString(),
                    diagnostics,
                    new ExecutionContext(regVm.InctructionsElapsed, sw.Elapsed, regVm.StackPeakBytes, regVm.HeapPeakBytes));
            }
            catch (Exception ex)
            {
                diagnostics.Add(new Diagnostic("INTERNAL", DiagnosticSeverity.Error, ex.ToString(), default));
                return (output.ToString(), diagnostics, ExecutionContext.Empty);
            }
        }

        public static (string output, List<IDiagnostic> diagnostics, ExecutionContext context) InterpretOnStack(
            string source,
            CancellationTokenSource cts,
            int heapSize = 32 * 1024,
            int stackSize = 4 * 1024,
            int metaSize = 0,
            int outputLimit = 4 * 1024 - 1,
            ExecutionLimits? execLimits = null,
            string? externalLibSource = null,
            Action<Cnidaria.Cs.HostInterface>? host = null,
            Action<string>? streamAction = null,
            string? entryAttributeTypeName = null,
            string[]? entryAttributeArgs = null,
            string[]? entryMethodArgs = null)
        {
            execLimits ??= new ExecutionLimits();
            var output = new StringBuilder();
            var diagnostics = new List<IDiagnostic>(ExtendedLibrary.diags);

            try
            {
                (IMetadataView meta, Dictionary<int, BytecodeFunction> functions)? external = null;
                if (externalLibSource != null)
                {
                    var (_, extMeta, extFuncs, extDiags) = CompileLibraryCore(externalLibSource, "external");
                    AddDiagnostics(diagnostics, extDiags);
                    if (HasErrors(diagnostics) || extMeta == null || extFuncs == null)
                        return (string.Empty, diagnostics, ExecutionContext.Empty);
                    external = (extMeta, extFuncs);
                }

                var parser = new Parser(source);
                var root = parser.Parse();
                AddDiagnostics(diagnostics, parser.LexerDiagnostics);
                AddDiagnostics(diagnostics, parser.Diagnostics);
                if (HasErrors(diagnostics))
                    return (string.Empty, diagnostics, ExecutionContext.Empty);

                var tree = new SyntaxTree(root, "app");
                var trees = ImmutableArray.Create(tree);
                var refs = external != null
                    ? new MetadataReferenceSet(new[] { StandartLibrary.meta, ExtendedLibrary.meta, external.Value.meta })
                    : new MetadataReferenceSet(new[] { StandartLibrary.meta, ExtendedLibrary.meta });

                var compilation = CompilationFactory.Create(trees, refs, out var declDiag);
                AddDiagnostics(diagnostics, declDiag);
                if (HasErrors(diagnostics))
                    return (string.Empty, diagnostics, ExecutionContext.Empty);

                var (md, builtFuncs, diags, ex) = compilation.BuildModule(
                    moduleName: "app",
                    tree: tree,
                    includeCoreTypesInTypeDefs: false,
                    defaultExternalAssemblyName: "std",
                    externalAssemblyResolver: refs.ResolveAssemblyName,
                    print: false);

                if (ex != null)
                    diagnostics.Add(new Diagnostic("BUILD", DiagnosticSeverity.Error, ex.ToString(), default));
                AddDiagnostics(diagnostics, diags);
                if (HasErrors(diagnostics))
                    return (string.Empty, diagnostics, ExecutionContext.Empty);

                byte[] flatMd = FlatMetadataBuilder.Build(md);
                IMetadataView appMeta = new FlatMetadataView(flatMd);

                var domain = new Domain();
                var stdModule = new RuntimeModule(StandartLibrary.meta.ModuleName, StandartLibrary.meta, StandartLibrary.funcs);
                var extStdModule = new RuntimeModule(ExtendedLibrary.meta.ModuleName, ExtendedLibrary.meta, ExtendedLibrary.funcs);
                var appModule = new RuntimeModule(appMeta.ModuleName, appMeta, builtFuncs);
                var modules = new Dictionary<string, RuntimeModule>(StringComparer.Ordinal);

                void AddUnique(RuntimeModule m)
                {
                    if (!modules.TryAdd(m.Name, m))
                        throw new InvalidOperationException($"Duplicate module loaded: '{m.Name}'");
                    domain.Add(m);
                }

                AddUnique(stdModule);
                AddUnique(extStdModule);
                if (external != null)
                    AddUnique(new RuntimeModule(external.Value.meta.ModuleName, external.Value.meta, external.Value.functions));
                AddUnique(appModule);

                int entryTok;
                object?[]? selectedValues = null;
                if (string.IsNullOrWhiteSpace(entryAttributeTypeName))
                {
                    entryTok = BytecodeBuilder.FindEntryPointMethodDef(appModule);
                }
                else
                {
                    var attrArgs = entryAttributeArgs ?? Array.Empty<string>();
                    var callArgs = entryMethodArgs ?? Array.Empty<string>();
                    if (!TryResolveAttributedEntryPoint(appMeta, entryAttributeTypeName!, attrArgs, callArgs, out entryTok, out selectedValues, out var err))
                    {
                        var diagnostic = new Diagnostic("ENTRYPOINT", DiagnosticSeverity.Error, err, default);
                        diagnostics.Add(diagnostic);
                        output.AppendLine(diagnostic.GetMessage());
                        return (output.ToString(), diagnostics, ExecutionContext.Empty);
                    }
                }

                var rts = new RuntimeTypeSystem(modules);
                byte[] mem = new byte[stackSize + heapSize + metaSize];
                int staticEnd = metaSize;
                int stackBase = staticEnd;
                int stackEnd = stackBase + stackSize;

                var sb = new StringBuilder();
                using var stringWriter = new StringWriter(sb);
                using var writer = new BoundedTextWriter(
                    inner: stringWriter,
                    maxChars: outputLimit,
                    mode: BoundedTextWriter.OverflowMode.Truncate,
                    onChunk: streamAction,
                    streamOnlyWhatWasWritten: true);

                var stVm = new StackBasedVm(
                    memory: mem,
                    staticEnd: staticEnd,
                    stackBase: stackBase,
                    stackEnd: stackEnd,
                    domain: domain,
                    rts: rts,
                    modules: modules,
                    textWriter: writer);

                var entryFn = appModule.MethodsByDefToken[entryTok];
                Slot[]? initialArgs = null;
                if (selectedValues != null)
                {
                    var rm = rts.ResolveMethod(appModule, entryTok);
                    if (!rm.IsStatic || rm.HasThis)
                        throw new InvalidOperationException("Command entrypoints must be static.");
                    initialArgs = BuildStackInitialArgs(stVm, rts, rm, selectedValues);
                }

                host?.Invoke(new HostInterface(stVm, rts, modules));

                var sw = Stopwatch.StartNew();
                if (initialArgs != null)
                    stVm.Execute(appModule, entryFn, cts.Token, execLimits, initialArgs);
                else
                    stVm.Execute(appModule, entryFn, cts.Token, execLimits);
                sw.Stop();

                return (
                    sb.ToString(),
                    diagnostics,
                    new ExecutionContext(stVm.InctructionsElapsed, sw.Elapsed, stVm.StackPeakBytes, stVm.HeapPeakBytes));
            }
            catch (Exception ex)
            {
                diagnostics.Add(new Diagnostic("INTERNAL", DiagnosticSeverity.Error, ex.ToString(), default));
                return (output.ToString(), diagnostics, ExecutionContext.Empty);
            }
        }

        internal static (IMetadataView meta, Dictionary<int, Cnidaria.Cs.BytecodeFunction> funcs) CompileCoreLibrary(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("standart library source code is empty");
            var stdParser = new Cnidaria.Cs.Parser(source);
            var stdRoot = stdParser.Parse();
            foreach (var diag in stdParser.LexerDiagnostics)
                throw new InvalidOperationException($"std lexer error: {diag.GetMessage(source)}");
            foreach (var diag in stdParser.Diagnostics)
                throw new InvalidOperationException($"std parser error: {diag.GetMessage(source)}");
            var stdSyntaxTree = new SyntaxTree(stdRoot, "std");
            var stdTrees = ImmutableArray.Create(new SyntaxTree[] { stdSyntaxTree });
            var stdCompilation = CompilationFactory.CreateCoreLibrary(stdTrees, out var stdDiagnostics);
            foreach (var diag in stdDiagnostics)
                throw new InvalidOperationException($"std declaration error: {diag.GetMessage(source)}");
            var (stdMd, stdFuncs, stdDiags, stdEx) = stdCompilation.BuildModule(
                moduleName: "std",
                tree: stdTrees[0],
                includeCoreTypesInTypeDefs: true,
                defaultExternalAssemblyName: "std",
                print: false);
            if (stdEx != null)
                throw new InvalidOperationException("std build internal error", stdEx);
            foreach (var diag in stdDiags)
            {
                if (diag.Severity == DiagnosticSeverity.Error)
                    throw new InvalidOperationException($"std build error: {diag.GetMessage(source)}");
            }
            byte[] stdFlatMd = Cnidaria.Cs.FlatMetadataBuilder.Build(stdMd);
            IMetadataView stdViewFlat = new FlatMetadataView(stdFlatMd);
            return (stdViewFlat, stdFuncs);
        }
        public static (IMetadataView meta, Dictionary<int, Cnidaria.Cs.BytecodeFunction> funcs, List<IDiagnostic> diags) CompileLibrary(string source, string modulename)
        {
            var diagnostics = new List<IDiagnostic>();
            var parser = new Cnidaria.Cs.Parser(source);
            var root = parser.Parse();
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
                return (null!, null!, diagnostics);
            }
            var tree = new SyntaxTree(root, modulename);
            var trees = ImmutableArray.Create(new[] { tree });
            var refs = new Cnidaria.Cs.MetadataReferenceSet(new[] { StandartLibrary.meta });
            var compilation = CompilationFactory.Create(trees, refs, out var declDiag);

            foreach (var diag in declDiag)
            {
                diagnostics.Add(diag);
            }
            if (diagnostics.Any(x => x.GetSeverity() == DiagnosticSeverity.Error))
            {
                return (null!, null!, diagnostics);
            }
            var (md, builtFuncs, diags, ex) = compilation.BuildModule(
                moduleName: modulename,
                tree: trees[0],
                includeCoreTypesInTypeDefs: false,
                defaultExternalAssemblyName: "std",
                print: false,
                externalAssemblyResolver: refs.ResolveAssemblyName);
            if (ex != null)
            {
                var diagnostic = new Diagnostic("LIBINT", DiagnosticSeverity.Error, ex.Message, default);
                diagnostics.Add(diagnostic);
            }
            foreach (var diag in diags)
            {
                diagnostics.Add(diag);
            }
            if (diagnostics.Any(x => x.GetSeverity() == DiagnosticSeverity.Error))
            {
                return (null!, null!, diagnostics);
            }
            byte[] flatMd = Cnidaria.Cs.FlatMetadataBuilder.Build(md);
            IMetadataView viewFlat = new FlatMetadataView(flatMd);
            return (viewFlat, builtFuncs, diagnostics);
        }
        private static (byte[]? flatMd, IMetadataView? meta, Dictionary<int, Cnidaria.Cs.BytecodeFunction>? funcs, List<IDiagnostic> diags)
            CompileLibraryCore(string source, string moduleName)
        {
            var diagnostics = new List<IDiagnostic>();
            var parser = new Parser(source);
            var root = parser.Parse();

            foreach (var diag in parser.LexerDiagnostics)
                diagnostics.Add(diag);
            foreach (var diag in parser.Diagnostics)
                diagnostics.Add(diag);
            if (HasErrors(diagnostics))
                return (null, null, null, diagnostics);

            var tree = new SyntaxTree(root, moduleName);
            var trees = ImmutableArray.Create(tree);
            var refs = new MetadataReferenceSet(new[] { StandartLibrary.meta });
            var compilation = CompilationFactory.Create(trees, refs, out var declDiag);

            foreach (var diag in declDiag)
                diagnostics.Add(diag);
            if (HasErrors(diagnostics))
                return (null, null, null, diagnostics);

            var (md, builtFuncs, diags, ex) = compilation.BuildModule(
                moduleName: moduleName,
                tree: tree,
                includeCoreTypesInTypeDefs: false,
                defaultExternalAssemblyName: "std",
                print: false,
                externalAssemblyResolver: refs.ResolveAssemblyName);

            if (ex != null)
                diagnostics.Add(new Diagnostic("LIBINT", DiagnosticSeverity.Error, ex.Message, default));

            foreach (var diag in diags)
                diagnostics.Add(diag);
            if (HasErrors(diagnostics))
                return (null, null, null, diagnostics);

            byte[] flatMd = FlatMetadataBuilder.Build(md);
            IMetadataView viewFlat = new FlatMetadataView(flatMd);

            return (flatMd, viewFlat, builtFuncs, diagnostics);
        }
        public static (byte[]? image, List<IDiagnostic> diagnostics) CompileExternalLibraryToBytes(
            string source,
            string moduleName = "external")
        {
            var (flatMd, _, extFuncs, diags) = CompileLibraryCore(source, moduleName);
            if (HasErrors(diags) || flatMd == null || extFuncs == null)
                return (null, diags);

            return (BytecodeSerializer.SerializeCompiledModule(flatMd, extFuncs), diags);
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
        private enum ArgLexKind { Other, Integer, Floating }
        private static bool TryResolveAttributedEntryPoint(
            IMetadataView metadata,
            string attributeTypeName,
            string[] attributeArgs,
            string[] callArgs,
            out int entryTok,
            out object?[]? boundValues,
            out string error)
        {
            entryTok = 0;
            boundValues = null;
            error = string.Empty;

            string wantAttr = NormalizeAttrName(attributeTypeName);
            var constantsByParent = BuildConstantMap(metadata);
            var attributesByParent = BuildAttributeMap(metadata);

            var candidates = new List<(int methodToken, object?[] values, int cost, int defaultsUsed, bool usedParams)>();

            foreach (int methodToken in EnumerateMethodDefTokens(metadata))
            {
                if (!HasMatchingAttribute(attributesByParent, methodToken, wantAttr, attributeArgs))
                    continue;

                if (!TryGetEntryParameterSpecs(metadata, methodToken, constantsByParent, attributesByParent, out var parameters))
                    continue;

                if (!TryBindPositionalArguments(parameters, callArgs, out var values, out var cost, out var defaultsUsed, out var usedParams))
                    continue;

                candidates.Add((methodToken, values, cost, defaultsUsed, usedParams));
            }

            if (candidates.Count == 0)
            {
                error = $"No method found with attribute '{attributeTypeName}' and matching arguments.";
                return false;
            }

            candidates.Sort((a, b) =>
            {
                int c = a.cost.CompareTo(b.cost);
                if (c != 0) return c;

                c = a.usedParams.CompareTo(b.usedParams);
                if (c != 0) return c;

                c = a.defaultsUsed.CompareTo(b.defaultsUsed);
                if (c != 0) return c;

                return b.values.Length.CompareTo(a.values.Length);
            });

            if (candidates.Count > 1)
            {
                var x = candidates[0];
                var y = candidates[1];

                if (x.cost == y.cost &&
                    x.defaultsUsed == y.defaultsUsed &&
                    x.usedParams == y.usedParams &&
                    x.values.Length == y.values.Length)
                {
                    error = $"Ambiguous command entrypoint: multiple overloads match for '{attributeTypeName}'.";
                    return false;
                }
            }

            entryTok = candidates[0].methodToken;
            boundValues = candidates[0].values;
            return true;
        }
        private static Dictionary<int, ConstantRow> BuildConstantMap(IMetadataView metadata)
        {
            var result = new Dictionary<int, ConstantRow>();
            int count = metadata.GetRowCount(MetadataTableKind.Constant);

            for (int rid = 1; rid <= count; rid++)
            {
                var row = metadata.GetConstant(rid);
                result[row.ParentToken] = row;
            }

            return result;
        }

        private static Dictionary<int, List<MetadataAttributeSpec>> BuildAttributeMap(IMetadataView metadata)
        {
            var result = new Dictionary<int, List<MetadataAttributeSpec>>();
            int count = metadata.GetRowCount(MetadataTableKind.CustomAttribute);

            for (int rid = 1; rid <= count; rid++)
            {
                var row = metadata.GetCustomAttribute(rid);
                if (!TryDecodeAttribute(metadata, row, out var spec))
                    continue;

                if (!result.TryGetValue(row.ParentToken, out var list))
                {
                    list = new List<MetadataAttributeSpec>();
                    result.Add(row.ParentToken, list);
                }

                list.Add(spec);
            }

            return result;
        }

        private static IEnumerable<int> EnumerateMethodDefTokens(IMetadataView metadata)
        {
            int count = metadata.GetRowCount(MetadataTableKind.MethodDef);
            for (int rid = 1; rid <= count; rid++)
                yield return MetadataToken.Make(MetadataToken.MethodDef, rid);
        }

        private static bool HasMatchingAttribute(
            Dictionary<int, List<MetadataAttributeSpec>> attributesByParent,
            int methodToken,
            string wantAttrShort,
            string[] wantCtorArgs)
        {
            if (!attributesByParent.TryGetValue(methodToken, out var attrs))
                return false;

            for (int i = 0; i < attrs.Count; i++)
            {
                var a = attrs[i];
                if (a.Target != AttributeApplicationTarget.Method)
                    continue;

                if (!StringComparer.Ordinal.Equals(NormalizeAttrName(a.Name), wantAttrShort))
                    continue;

                if (a.CtorArgs.Length != wantCtorArgs.Length)
                    continue;

                bool same = true;
                for (int j = 0; j < wantCtorArgs.Length; j++)
                {
                    if (!StringComparer.Ordinal.Equals(a.CtorArgs[j], wantCtorArgs[j]))
                    {
                        same = false;
                        break;
                    }
                }

                if (same)
                    return true;
            }

            return false;
        }

        private static bool TryGetEntryParameterSpecs(
            IMetadataView metadata,
            int methodToken,
            Dictionary<int, ConstantRow> constantsByParent,
            Dictionary<int, List<MetadataAttributeSpec>> attributesByParent,
            out MetadataEntryParameterSpec[] parameters)
        {
            parameters = Array.Empty<MetadataEntryParameterSpec>();

            if (MetadataToken.Table(methodToken) != MetadataToken.MethodDef)
                return false;

            int methodRid = MetadataToken.Rid(methodToken);
            var method = metadata.GetMethodDef(methodRid);
            string methodName = metadata.GetString(method.Name);

            if ((method.Flags & 0x0800) != 0) // MethodAttributes.SpecialName
                return false;

            if (methodName is ".ctor" or ".cctor")
                return false;

            var sig = new SigReader(metadata.GetBlob(method.Signature));
            byte callConv = sig.ReadByte();

            bool hasThis = (callConv & 0x20) != 0;
            if (hasThis)
                return false;

            if ((callConv & 0x10) != 0) // GENERIC
                _ = sig.ReadCompressedUInt();

            uint paramCount = sig.ReadCompressedUInt();

            if (!TryReadVoidReturnType(ref sig))
                return false;

            int totalParamRows = metadata.GetRowCount(MetadataTableKind.Param);
            int paramListRid = method.ParamList;
            if (paramCount == 0)
            {
                parameters = Array.Empty<MetadataEntryParameterSpec>();
                return true;
            }

            if (paramListRid <= 0 || paramListRid > totalParamRows)
                return false;

            var result = new MetadataEntryParameterSpec[checked((int)paramCount)];

            for (int i = 0; i < (int)paramCount; i++)
            {
                if (!TryReadSupportedEntryParameterType(ref sig, out var specialType, out var isStringArray))
                    return false;

                int paramRid = paramListRid + i;
                if (paramRid > totalParamRows)
                    return false;

                var param = metadata.GetParam(paramRid);
                if (param.Sequence != (ushort)(i + 1))
                    return false;

                int paramToken = MetadataToken.Make(MetadataToken.ParamDef, paramRid);

                bool isParamsStringArray = false;
                if (isStringArray)
                {
                    if (i != (int)paramCount - 1)
                        return false;

                    isParamsStringArray = HasParamArrayAttribute(attributesByParent, paramToken);
                    if (!isParamsStringArray)
                        return false;
                }

                bool hasDefault = false;
                object? defaultValue = null;

                if (constantsByParent.TryGetValue(paramToken, out var constant))
                {
                    if (!TryDecodeConstant(metadata, constant, out defaultValue))
                        return false;

                    hasDefault = true;
                }

                if (isParamsStringArray && hasDefault)
                    return false;

                result[i] = new MetadataEntryParameterSpec(
                    specialType: isStringArray ? SpecialType.System_String : specialType,
                    isParamsStringArray: isParamsStringArray,
                    hasDefault: hasDefault,
                    defaultValue: defaultValue);
            }

            parameters = result;
            return true;
        }

        private static bool HasParamArrayAttribute(
            Dictionary<int, List<MetadataAttributeSpec>> attributesByParent,
            int paramToken)
        {
            if (!attributesByParent.TryGetValue(paramToken, out var attrs))
                return false;

            for (int i = 0; i < attrs.Count; i++)
            {
                var a = attrs[i];
                if (a.Target != AttributeApplicationTarget.Parameter)
                    continue;

                if (!StringComparer.Ordinal.Equals(a.Namespace, "System"))
                    continue;

                if (StringComparer.Ordinal.Equals(a.Name, "ParamArrayAttribute"))
                    return true;
            }

            return false;
        }

        private static bool TryDecodeAttribute(
            IMetadataView metadata,
            CustomAttributeRow row,
            out MetadataAttributeSpec spec)
        {
            spec = default;

            if (!TryGetTypeTokenName(metadata, row.AttributeTypeToken, out var @namespace, out var name))
                return false;

            if (!TryReadAttributeCtorArgs(metadata, row.Value, out var ctorArgs))
                return false;

            spec = new MetadataAttributeSpec(
                @namespace: @namespace,
                name: name,
                ctorArgs: ctorArgs,
                target: (AttributeApplicationTarget)row.Target);

            return true;
        }

        private static bool TryReadAttributeCtorArgs(
            IMetadataView metadata,
            int blobIndex,
            out string[] ctorArgs)
        {
            ctorArgs = Array.Empty<string>();

            try
            {
                var reader = new AttrBlobReader(metadata.GetBlob(blobIndex));

                int ctorParamCount = reader.ReadInt32();
                for (int i = 0; i < ctorParamCount; i++)
                    _ = reader.ReadInt32();

                int ctorArgCount = reader.ReadInt32();
                var args = new string[ctorArgCount];

                for (int i = 0; i < ctorArgCount; i++)
                {
                    if (!TryReadAttributeCtorArg(metadata, ref reader, out var value))
                        return false;

                    args[i] = value is null
                        ? "null"
                        : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                }

                ctorArgs = args;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadAttributeCtorArg(
            IMetadataView metadata,
            ref AttrBlobReader reader,
            out object? value)
        {
            value = null;

            _ = reader.ReadInt32(); // type token
            byte kind = reader.ReadByte();

            switch (kind)
            {
                case 0:
                    value = null;
                    return true;
                case 1:
                    value = reader.ReadByte() != 0;
                    return true;
                case 2:
                    value = (char)reader.ReadUInt16();
                    return true;
                case 3:
                    value = reader.ReadSByte();
                    return true;
                case 4:
                    value = reader.ReadByte();
                    return true;
                case 5:
                    value = reader.ReadInt16();
                    return true;
                case 6:
                    value = reader.ReadUInt16();
                    return true;
                case 7:
                    value = reader.ReadInt32();
                    return true;
                case 8:
                    value = reader.ReadUInt32();
                    return true;
                case 9:
                    value = reader.ReadInt64();
                    return true;
                case 10:
                    value = reader.ReadUInt64();
                    return true;
                case 11:
                    value = reader.ReadSingle();
                    return true;
                case 12:
                    value = reader.ReadDouble();
                    return true;
                case 13:
                    value = metadata.GetString(reader.ReadInt32());
                    return true;
                case 14:
                    if (!TryGetTypeTokenName(metadata, reader.ReadInt32(), out var @namespace, out var name))
                        return false;

                    value = string.IsNullOrEmpty(@namespace) ? name : @namespace + "." + name;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryGetTypeTokenName(
            IMetadataView metadata,
            int token,
            out string @namespace,
            out string name)
        {
            @namespace = string.Empty;
            name = string.Empty;

            int table = MetadataToken.Table(token);
            int rid = MetadataToken.Rid(token);

            switch (table)
            {
                case MetadataToken.TypeDef:
                    if (rid <= 0 || rid > metadata.GetRowCount(MetadataTableKind.TypeDef))
                        return false;

                    var td = metadata.GetTypeDef(rid);
                    @namespace = metadata.GetString(td.Namespace);
                    name = metadata.GetString(td.Name);
                    return true;

                case MetadataToken.TypeRef:
                    if (rid <= 0 || rid > metadata.GetRowCount(MetadataTableKind.TypeRef))
                        return false;

                    var tr = metadata.GetTypeRef(rid);
                    @namespace = metadata.GetString(tr.Namespace);
                    name = metadata.GetString(tr.Name);
                    return true;

                default:
                    return false;
            }
        }

        private static bool TryDecodeConstant(
            IMetadataView metadata,
            ConstantRow row,
            out object? value)
        {
            value = null;
            var blob = metadata.GetBlob(row.Value);

            if (row.TypeCode == 0 && blob.Length == 0)
            {
                value = null;
                return true;
            }

            switch (row.TypeCode)
            {
                case 0x02: value = blob[0] != 0; return true;
                case 0x03: value = BitConverter.ToChar(blob); return true;
                case 0x04: value = unchecked((sbyte)blob[0]); return true;
                case 0x05: value = blob[0]; return true;
                case 0x06: value = BitConverter.ToInt16(blob); return true;
                case 0x07: value = BitConverter.ToUInt16(blob); return true;
                case 0x08: value = BitConverter.ToInt32(blob); return true;
                case 0x09: value = BitConverter.ToUInt32(blob); return true;
                case 0x0A: value = BitConverter.ToInt64(blob); return true;
                case 0x0B: value = BitConverter.ToUInt64(blob); return true;
                case 0x0C: value = BitConverter.ToSingle(blob); return true;
                case 0x0D: value = BitConverter.ToDouble(blob); return true;
                case 0x0E: value = Encoding.UTF8.GetString(blob); return true;
                default: return false;
            }
        }

        private static bool TryReadVoidReturnType(ref SigReader reader)
        {
            return (SigElementType)reader.ReadByte() == SigElementType.VOID;
        }

        private static bool TryReadSupportedEntryParameterType(
            ref SigReader reader,
            out SpecialType specialType,
            out bool isStringArray)
        {
            specialType = SpecialType.None;
            isStringArray = false;

            switch ((SigElementType)reader.ReadByte())
            {
                case SigElementType.STRING:
                    specialType = SpecialType.System_String;
                    return true;

                case SigElementType.BOOLEAN:
                    specialType = SpecialType.System_Boolean;
                    return true;

                case SigElementType.CHAR:
                    specialType = SpecialType.System_Char;
                    return true;

                case SigElementType.I1:
                    specialType = SpecialType.System_Int8;
                    return true;

                case SigElementType.U1:
                    specialType = SpecialType.System_UInt8;
                    return true;

                case SigElementType.I2:
                    specialType = SpecialType.System_Int16;
                    return true;

                case SigElementType.U2:
                    specialType = SpecialType.System_UInt16;
                    return true;

                case SigElementType.I4:
                    specialType = SpecialType.System_Int32;
                    return true;

                case SigElementType.U4:
                    specialType = SpecialType.System_UInt32;
                    return true;

                case SigElementType.I8:
                    specialType = SpecialType.System_Int64;
                    return true;

                case SigElementType.U8:
                    specialType = SpecialType.System_UInt64;
                    return true;

                case SigElementType.R4:
                    specialType = SpecialType.System_Single;
                    return true;

                case SigElementType.R8:
                    specialType = SpecialType.System_Double;
                    return true;

                case SigElementType.SZARRAY:
                    if ((SigElementType)reader.ReadByte() != SigElementType.STRING)
                        return false;

                    isStringArray = true;
                    return true;

                default:
                    return false;
            }
        }
        private static bool IsValidCommandMethodShape(Compilation compilation, MethodSymbol m)
        {
            if (!m.IsStatic) return false;
            if (m.IsConstructor) return false;
            if (m.IsSpecialName) return false;
            if (m.ReturnType.SpecialType != SpecialType.System_Void) return false;

            var ps = m.Parameters;
            for (int i = 0; i < ps.Length; i++)
                if (ps[i].RefKind != ParameterRefKind.None) return false;

            return true;
        }

        private static bool HasMatchingAttribute(MethodSymbol m, string wantAttrShort, string[] wantCtorArgs)
        {
            var attrs = m.GetAttributes();
            for (int i = 0; i < attrs.Length; i++)
            {
                var a = attrs[i];
                if (a.Target != AttributeApplicationTarget.Method)
                    continue;

                string got = NormalizeAttrName(a.AttributeClass.Name);
                if (!StringComparer.Ordinal.Equals(got, wantAttrShort))
                    continue;

                if (!CtorArgsMatch(a.ConstructorArguments, wantCtorArgs))
                    continue;

                return true;
            }
            return false;
        }
        private static bool CtorArgsMatch(ImmutableArray<TypedConstant> got, string[] want)
        {
            if (got.Length != want.Length) return false;

            for (int i = 0; i < want.Length; i++)
            {
                object? v = got[i].Value;
                string s = v is null ? "null" : Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty;
                if (!StringComparer.Ordinal.Equals(s, want[i]))
                    return false;
            }
            return true;
        }

        private static string NormalizeAttrName(string name)
        {
            if (name.EndsWith("Attribute", StringComparison.Ordinal))
                return name.Substring(0, name.Length - "Attribute".Length);
            return name;
        }

        private static IEnumerable<MethodSymbol> EnumerateAllSourceMethods(NamespaceSymbol root)
        {
            var nsStack = new Stack<NamespaceSymbol>();
            nsStack.Push(root);

            while (nsStack.Count != 0)
            {
                var ns = nsStack.Pop();

                var childNs = ns.GetNamespaceMembers();
                for (int i = 0; i < childNs.Length; i++)
                    nsStack.Push(childNs[i]);

                var types = ns.GetTypeMembers();
                for (int i = 0; i < types.Length; i++)
                {
                    foreach (var m in EnumerateMethodsInType(types[i]))
                        yield return m;
                }
            }
        }

        private static IEnumerable<MethodSymbol> EnumerateMethodsInType(NamedTypeSymbol t)
        {
            var members = t.GetMembers();
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] is MethodSymbol ms)
                    yield return ms;

                if (members[i] is NamedTypeSymbol nt)
                {
                    foreach (var mm in EnumerateMethodsInType(nt))
                        yield return mm;
                }
            }
        }
        private static ArgLexKind ClassifyArg(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return ArgLexKind.Other;

            // Common floating literals
            if (string.Equals(s, "NaN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "Infinity", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "+Infinity", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "-Infinity", StringComparison.OrdinalIgnoreCase))
                return ArgLexKind.Floating;

            int i = 0;
            if (s[0] == '+' || s[0] == '-') i++;

            bool anyDigit = false;
            bool hasDot = false;
            bool hasExp = false;

            for (; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= '0' && c <= '9') { anyDigit = true; continue; }
                if (c == '.') { hasDot = true; continue; }
                if (c == 'e' || c == 'E') { hasExp = true; continue; }
                return ArgLexKind.Other;
            }

            if (!anyDigit) return ArgLexKind.Other;
            return (hasDot || hasExp) ? ArgLexKind.Floating : ArgLexKind.Integer;
        }
        private static bool TryParseInt<T>(
            string token,
            NumberStyles styles,
            out T parsed,
            out object? value,
            out int cost,
            int baseCost) where T : struct
        {
            parsed = default;
            value = null;
            cost = 0;

            var k = ClassifyArg(token);
            if (k != ArgLexKind.Integer) return false;

            if (typeof(T) == typeof(sbyte))
            {
                if (!sbyte.TryParse(token, styles, CultureInfo.InvariantCulture, out var x)) return false;
                parsed = (T)(object)x;
                value = x;
                cost = baseCost;
                return true;
            }
            if (typeof(T) == typeof(byte))
            {
                if (!byte.TryParse(token, styles, CultureInfo.InvariantCulture, out var x)) return false;
                parsed = (T)(object)x;
                value = x;
                cost = baseCost;
                return true;
            }
            if (typeof(T) == typeof(short))
            {
                if (!short.TryParse(token, styles, CultureInfo.InvariantCulture, out var x)) return false;
                parsed = (T)(object)x;
                value = x;
                cost = baseCost;
                return true;
            }
            if (typeof(T) == typeof(ushort))
            {
                if (!ushort.TryParse(token, styles, CultureInfo.InvariantCulture, out var x)) return false;
                parsed = (T)(object)x;
                value = x;
                cost = baseCost;
                return true;
            }
            if (typeof(T) == typeof(int))
            {
                if (!int.TryParse(token, styles, CultureInfo.InvariantCulture, out var x)) return false;
                parsed = (T)(object)x;
                value = x;
                cost = baseCost;
                return true;
            }
            if (typeof(T) == typeof(uint))
            {
                if (!uint.TryParse(token, styles, CultureInfo.InvariantCulture, out var x)) return false;
                parsed = (T)(object)x;
                value = x;
                cost = baseCost;
                return true;
            }
            if (typeof(T) == typeof(long))
            {
                if (!long.TryParse(token, styles, CultureInfo.InvariantCulture, out var x)) return false;
                parsed = (T)(object)x;
                value = x;
                cost = baseCost;
                return true;
            }
            if (typeof(T) == typeof(ulong))
            {
                if (!ulong.TryParse(token, styles, CultureInfo.InvariantCulture, out var x)) return false;
                parsed = (T)(object)x;
                value = x;
                cost = baseCost;
                return true;
            }

            return false;
        }
        private static bool TryParseFloat<T>(
            string token,
            out T parsed,
            out object? value,
            out int cost,
            ArgLexKind kind,
            int baseCost) where T : struct
        {
            parsed = default;
            value = null;
            cost = 0;

            if (typeof(T) == typeof(float))
            {
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var dd))
                    return false;
                parsed = (T)(object)(float)dd;
                value = (float)dd;
                cost = baseCost + (kind == ArgLexKind.Integer ? 5 : 0);
                return true;
            }

            if (typeof(T) == typeof(double))
            {
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var dd))
                    return false;
                parsed = (T)(object)dd;
                value = dd;
                cost = baseCost + (kind == ArgLexKind.Integer ? 5 : 0);
                return true;
            }

            return false;
        }



        private static Cnidaria.Cs.Slot[] BuildStackInitialArgs(Cnidaria.Cs.StackBasedVm vm, Cnidaria.Cs.RuntimeTypeSystem rts,
            Cnidaria.Cs.RuntimeMethod rm, object?[] values)
        {
            if (rm.HasThis) throw new InvalidOperationException("Instance entrypoints are not supported.");
            if (rm.ParameterTypes.Length != values.Length)
                throw new InvalidOperationException("Bound values length mismatch.");

            var args = new Cnidaria.Cs.Slot[values.Length];
            for (int i = 0; i < values.Length; i++)
                args[i] = MarshalToStackSlot(vm, rts, rm.ParameterTypes[i], values[i]);
            return args;
        }

        private static Cnidaria.Cs.Slot MarshalToStackSlot(Cnidaria.Cs.StackBasedVm vm, Cnidaria.Cs.RuntimeTypeSystem rts, Cnidaria.Cs.RuntimeType t, object? v)
        {
            if (t.IsReferenceType)
            {
                if (v is null)
                    return new Cnidaria.Cs.Slot(Cnidaria.Cs.SlotKind.Null, 0);

                if (t.TypeId == rts.SystemString.TypeId)
                    return vm.HostAllocString((string)v).ToSlot();

                if (t.Kind == Cnidaria.Cs.RuntimeTypeKind.Array)
                {
                    if (v is not string?[] ss)
                        throw new NotSupportedException("Only string[] values are supported for array parameters.");
                    return vm.HostAllocStringArray(t, ss).ToSlot();
                }

                throw new NotSupportedException($"Host marshal not supported for ref type: {t.Namespace}.{t.Name}");
            }

            if (v is null)
                throw new InvalidOperationException($"Null passed to value type {t.Namespace}.{t.Name}");

            if (t.Namespace == "System")
            {
                switch (t.Name)
                {
                    case "Boolean": return new Cnidaria.Cs.Slot(Cnidaria.Cs.SlotKind.I4, (bool)v ? 1 : 0);
                    case "Char": return new Cnidaria.Cs.Slot(Cnidaria.Cs.SlotKind.I4, (char)v);
                    case "SByte": return new Cnidaria.Cs.Slot(Cnidaria.Cs.SlotKind.I4, (sbyte)v);
                    case "Byte": return new Cnidaria.Cs.Slot(Cnidaria.Cs.SlotKind.I4, (byte)v);
                    case "Int16": return new Cnidaria.Cs.Slot(Cnidaria.Cs.SlotKind.I4, (short)v);
                    case "UInt16": return new Cnidaria.Cs.Slot(Cnidaria.Cs.SlotKind.I4, unchecked((int)(ushort)v));
                    case "Int32": return new Cnidaria.Cs.Slot(Cnidaria.Cs.SlotKind.I4, (int)v);
                    case "UInt32": return new Cnidaria.Cs.Slot(Cnidaria.Cs.SlotKind.I4, unchecked((int)(uint)v));
                    case "Int64": return new Cnidaria.Cs.Slot(Cnidaria.Cs.SlotKind.I8, (long)v);
                    case "UInt64": return new Cnidaria.Cs.Slot(Cnidaria.Cs.SlotKind.I8, unchecked((long)(ulong)v));
                    case "Single": return new Cnidaria.Cs.Slot(Cnidaria.Cs.SlotKind.R8, BitConverter.DoubleToInt64Bits((float)v));
                    case "Double": return new Cnidaria.Cs.Slot(Cnidaria.Cs.SlotKind.R8, BitConverter.DoubleToInt64Bits((double)v));
                }
            }

            throw new NotSupportedException($"Host marshal not supported for value type: {t.Namespace}.{t.Name}");
        }

        private static Cnidaria.Cs.VmValue[] BuildRegisterInitialArgs(Cnidaria.Cs.RegisterBasedVm vm, Cnidaria.Cs.RuntimeTypeSystem rts,
            Cnidaria.Cs.RuntimeMethod rm, object?[] values)
        {
            if (rm.HasThis) throw new InvalidOperationException("Instance entrypoints are not supported.");
            if (rm.ParameterTypes.Length != values.Length)
                throw new InvalidOperationException("Bound values length mismatch.");

            var args = new Cnidaria.Cs.VmValue[values.Length];
            for (int i = 0; i < values.Length; i++)
                args[i] = MarshalToRegisterValue(vm, rts, rm.ParameterTypes[i], values[i]);
            return args;
        }

        private static Cnidaria.Cs.VmValue MarshalToRegisterValue(Cnidaria.Cs.RegisterBasedVm vm, Cnidaria.Cs.RuntimeTypeSystem rts, Cnidaria.Cs.RuntimeType t, object? v)
        {
            if (t.IsReferenceType)
            {
                if (v is null)
                    return Cnidaria.Cs.VmValue.Null;

                if (t.TypeId == rts.SystemString.TypeId)
                    return vm.HostAllocString((string)v);

                if (t.Kind == Cnidaria.Cs.RuntimeTypeKind.Array)
                {
                    if (v is not string?[] ss)
                        throw new NotSupportedException("Only string[] values are supported for array parameters.");

                    var arr = vm.HostAllocArray(t, ss.Length);
                    for (int i = 0; i < ss.Length; i++)
                        vm.HostSetArrayElement(arr, i, vm.HostAllocString(ss[i]));
                    return arr;
                }

                throw new NotSupportedException($"Host marshal not supported for ref type: {t.Namespace}.{t.Name}");
            }

            if (v is null)
                throw new InvalidOperationException($"Null passed to value type {t.Namespace}.{t.Name}");

            if (t.Namespace == "System")
            {
                switch (t.Name)
                {
                    case "Boolean": return Cnidaria.Cs.VmValue.FromInt32((bool)v ? 1 : 0);
                    case "Char": return Cnidaria.Cs.VmValue.FromInt32((char)v);
                    case "SByte": return Cnidaria.Cs.VmValue.FromInt32((sbyte)v);
                    case "Byte": return Cnidaria.Cs.VmValue.FromInt32((byte)v);
                    case "Int16": return Cnidaria.Cs.VmValue.FromInt32((short)v);
                    case "UInt16": return Cnidaria.Cs.VmValue.FromInt32(unchecked((ushort)v));
                    case "Int32": return Cnidaria.Cs.VmValue.FromInt32((int)v);
                    case "UInt32": return Cnidaria.Cs.VmValue.FromInt32(unchecked((int)(uint)v));
                    case "Int64": return Cnidaria.Cs.VmValue.FromInt64((long)v);
                    case "UInt64": return Cnidaria.Cs.VmValue.FromInt64(unchecked((long)(ulong)v));
                    case "Single": return Cnidaria.Cs.VmValue.FromDouble((float)v);
                    case "Double": return Cnidaria.Cs.VmValue.FromDouble((double)v);
                }
            }

            throw new NotSupportedException($"Host marshal not supported for value type: {t.Namespace}.{t.Name}");
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
