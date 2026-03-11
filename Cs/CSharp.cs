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
        public readonly static (IMetadataView meta, Dictionary<int, BytecodeFunction> funcs) 
            StandartLibrary = CompileCoreLibrary(GetCoreBCLSource());
        public readonly static (IMetadataView meta, Dictionary<int, BytecodeFunction> funcs, List<IDiagnostic> diags) 
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
        private readonly struct CompiledModuleData
        {
            public readonly byte[] Image;
            public readonly IMetadataView Meta;
            public readonly Dictionary<int, BytecodeFunction> Funcs;

            public CompiledModuleData(byte[] image, IMetadataView meta, Dictionary<int, BytecodeFunction> funcs)
            {
                Image = image;
                Meta = meta;
                Funcs = funcs;
            }

            public static CompiledModuleData Load(byte[] image)
            {
                var (_, meta, funcs) = BytecodeSerializer.DeserializeCompiledModule(image);
                return new CompiledModuleData(image, meta, funcs);
            }
        }
        private readonly struct CompiledRunnableApplicationData
        {
            public readonly byte[] Image;
            public readonly CompiledModuleData Module;

            public CompiledRunnableApplicationData(byte[] image, CompiledModuleData module)
            {
                Image = image;
                Module = module;
            }

            public static CompiledRunnableApplicationData Load(byte[] image)
            {
                return new CompiledRunnableApplicationData(image, CompiledModuleData.Load(image));
            }
        }
        public static (byte[]? image, List<IDiagnostic> diagnostics) CompileApplicationToRunnableBytes(
            string source,
            byte[]? externalLibImage = null,
            bool allowInlining = true)
        {
            var diagnostics = new List<IDiagnostic>(ExtendedLibrary.diags);

            var parser = new Parser(source);
            var root = parser.Parse();

            foreach (var diag in parser.LexerDiagnostics)
                diagnostics.Add(diag);
            foreach (var diag in parser.Diagnostics)
                diagnostics.Add(diag);
            if (HasErrors(diagnostics))
                return (null, diagnostics);

            var tree = new SyntaxTree(root, "app");
            var trees = ImmutableArray.Create(tree);

            CompiledModuleData? ext = null;
            MetadataReferenceSet refs;

            if (externalLibImage != null)
            {
                ext = CompiledModuleData.Load(externalLibImage);
                refs = new MetadataReferenceSet(new[] { StandartLibrary.meta, ExtendedLibrary.meta, ext.Value.Meta });
            }
            else
            {
                refs = new MetadataReferenceSet(new[] { StandartLibrary.meta, ExtendedLibrary.meta });
            }

            var compilation = CompilationFactory.Create(trees, refs, out var declDiag);
            foreach (var diag in declDiag)
                diagnostics.Add(diag);
            if (HasErrors(diagnostics))
                return (null, diagnostics);

            var (md, funcs, diags, ex) = compilation.BuildModule(
                moduleName: "app",
                tree: tree,
                includeCoreTypesInTypeDefs: false,
                defaultExternalAssemblyName: "std",
                allowInlining: allowInlining,
                externalAssemblyResolver: refs.ResolveAssemblyName,
                print: false);

            if (ex != null)
                diagnostics.Add(new Diagnostic("BUILD", DiagnosticSeverity.Error, ex.ToString(), default));

            foreach (var diag in diags)
                diagnostics.Add(diag);
            if (HasErrors(diagnostics))
                return (null, diagnostics);

            byte[] flatMd = FlatMetadataBuilder.Build(md);
            byte[] moduleImage = BytecodeSerializer.SerializeCompiledModule(flatMd, funcs);

            return (moduleImage, diagnostics);
        }
        public static (string output, List<IDiagnostic> diagnostics, ExecutionContext context) Interpret(
            byte[] runnableAppImage,
            CancellationTokenSource cts,
            int heapSize = 32 * 1024,
            int stackSize = 4 * 1024,
            int metaSize = 0,
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
                var app = CompiledRunnableApplicationData.Load(runnableAppImage);
                CompiledModuleData? ext = null;

                if (externalLibImage != null)
                    ext = CompiledModuleData.Load(externalLibImage);

                return ExecuteCompiledApplication(
                    app,
                    ext,
                    cts,
                    heapSize,
                    stackSize,
                    metaSize,
                    outputLimit,
                    execLimits,
                    host,
                    streamAction,
                    entryAttributeTypeName,
                    entryAttributeArgs,
                    entryMethodArgs);
            }
            catch (Exception ex)
            {
                diagnostics.Add(new Diagnostic("INTERNAL", DiagnosticSeverity.Error, ex.ToString(), default));
                return (output.ToString(), diagnostics, ExecutionContext.Empty);
            }
        }
        private static (string output, List<IDiagnostic> diagnostics, ExecutionContext context) ExecuteCompiledApplication(
            CompiledRunnableApplicationData app,
            CompiledModuleData? ext,
            CancellationTokenSource cts,
            int heapSize,
            int stackSize,
            int metaSize,
            int outputLimit,
            ExecutionLimits? execLimits,
            Action<Cnidaria.Cs.HostInterface>? host,
            Action<string>? streamAction,
            string? entryAttributeTypeName,
            string[]? entryAttributeArgs,
            string[]? entryMethodArgs)
        {
            execLimits ??= new ExecutionLimits();
            var output = new StringBuilder();
            var diagnostics = new List<IDiagnostic>(ExtendedLibrary.diags);

            try
            {
                var domain = new Domain();

                var stdModule = new RuntimeModule(StandartLibrary.meta.ModuleName, StandartLibrary.meta, StandartLibrary.funcs);
                var extStdModule = new RuntimeModule(ExtendedLibrary.meta.ModuleName, ExtendedLibrary.meta, ExtendedLibrary.funcs);
                var appModule = new RuntimeModule(app.Module.Meta.ModuleName, app.Module.Meta, app.Module.Funcs);

                var modules = new Dictionary<string, RuntimeModule>(StringComparer.Ordinal);

                void AddUnique(RuntimeModule m)
                {
                    if (!modules.TryAdd(m.Name, m))
                        throw new InvalidOperationException($"Duplicate module loaded: '{m.Name}'");
                    domain.Add(m);
                }

                AddUnique(stdModule);
                AddUnique(extStdModule);
                AddUnique(appModule);

                if (ext != null)
                {
                    var extModule = new RuntimeModule(ext.Value.Meta.ModuleName, ext.Value.Meta, ext.Value.Funcs);
                    AddUnique(extModule);
                }

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
                            app.Module.Meta,
                            entryAttributeTypeName!,
                            attrArgs,
                            callArgs,
                            out entryTok,
                            out selectedValues,
                            out var err))
                    {
                        var diagnostic = new Diagnostic("ENTRYPOINT", DiagnosticSeverity.Error, err, default);
                        diagnostics.Add(diagnostic);
                        output.AppendLine(diagnostic.GetMessage());
                        return (output.ToString(), diagnostics, ExecutionContext.Empty);
                    }
                }

                var rts = new RuntimeTypeSystem(modules);

                byte[] mem = new byte[stackSize + heapSize + metaSize];
                int metaEnd = metaSize;
                int stackBase = metaEnd;
                int stackEnd = stackBase + stackSize;

                var sb = new StringBuilder();
                long instructionCount = -1;
                long stackUsage = -1;
                long heapUsage = -1;
                TimeSpan timeElapsed = TimeSpan.MinValue;

                using var stringWriter = new StringWriter(sb);
                using var writer = new BoundedTextWriter(
                    inner: stringWriter,
                    maxChars: outputLimit,
                    mode: BoundedTextWriter.OverflowMode.Truncate,
                    onChunk: streamAction,
                    streamOnlyWhatWasWritten: true);

                var vm = new Vm(
                    memory: mem,
                    metaEnd: metaEnd,
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

                    initialArgs = BuildInitialArgs(vm, rts, rm, selectedValues);
                }

                var t = Stopwatch.StartNew();
                try
                {
                    host?.Invoke(new HostInterface(vm, rts, modules));
                    if (initialArgs != null)
                        vm.Execute(appModule, entryFn, cts.Token, execLimits, initialArgs);
                    else
                        vm.Execute(appModule, entryFn, cts.Token, execLimits);
                }
                catch (Exception e)
                {
                    var diagnostic = new Diagnostic("INTERNAL", DiagnosticSeverity.Error, e.ToString(), default);
                    diagnostics.Add(diagnostic);
                    output.AppendLine(diagnostic.GetMessage());
                    return (sb.ToString() + output.ToString(), diagnostics, ExecutionContext.Empty);
                }

                t.Stop();

                instructionCount = vm.InctructionsElapsed;
                stackUsage = vm.StackPeakBytes;
                heapUsage = vm.HeapPeakBytes;
                timeElapsed = t.Elapsed;

                return (
                    sb.ToString(),
                    diagnostics,
                    new ExecutionContext(Math.Max(instructionCount, -1), timeElapsed, stackUsage, heapUsage));
            }
            catch (Exception ex)
            {
                diagnostics.Add(new Diagnostic("INTERNAL", DiagnosticSeverity.Error, ex.ToString(), default));
                return (output.ToString(), diagnostics, ExecutionContext.Empty);
            }
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
            string[]? entryMethodArgs = null,
            bool allowInlining = true)
        {
            execLimits ??= new ExecutionLimits();
            var output = new StringBuilder();
            var diagnostics = new List<IDiagnostic>(ExtendedLibrary.diags);
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
                    return (output.ToString(), diagnostics, ExecutionContext.Empty); 
                }
                var syntaxTree = new SyntaxTree(root, "std");
                var trees = ImmutableArray.Create(new SyntaxTree[] { syntaxTree });
                MetadataReferenceSet appRefs;
                (IMetadataView meta, Dictionary<int, BytecodeFunction> funcs, List<IDiagnostic> diags)? externLib = null;
                if (externalLibSource != null)
                {
                    externLib = CompileLibrary(externalLibSource, "external");
                    appRefs = new MetadataReferenceSet(new[] { StandartLibrary.meta, ExtendedLibrary.meta, externLib.Value.meta });
                }
                else
                {
                    appRefs = new MetadataReferenceSet(new[] { StandartLibrary.meta, ExtendedLibrary.meta });
                }
                
                Compilation compilation = CompilationFactory.Create(trees, appRefs, out var declDiag);
                foreach (var diag in declDiag)
                {
                    diagnostics.Add(diag);
                }
                if (diagnostics.Any(x => x.GetSeverity() == DiagnosticSeverity.Error))
                {
                    return (output.ToString(), diagnostics, ExecutionContext.Empty);
                }
                var (appMd, appFuncs, diags, ex) = compilation.BuildModule(
                    moduleName: "app",
                    tree: trees[0],
                    includeCoreTypesInTypeDefs: false,
                    defaultExternalAssemblyName: "std",
                    allowInlining: allowInlining,
                    externalAssemblyResolver: appRefs.ResolveAssemblyName,
                    print: false);
                if (ex != null)
                {
                    var diagnostic = new Diagnostic("BUILD", DiagnosticSeverity.Error, ex.ToString(), default);
                    diagnostics.Add(diagnostic);
                }
                foreach (var diag in diags)
                {
                    diagnostics.Add(diag);
                }
                if (diagnostics.Any(x => x.GetSeverity() == DiagnosticSeverity.Error))
                {
                    return (output.ToString(), diagnostics, ExecutionContext.Empty);
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

                if (externalLibSource != null && externLib != null)
                {
                    var externModule = new Cnidaria.Cs.RuntimeModule("external", externLib.Value.meta, externLib.Value.funcs);
                    AddUnique(externModule);
                }

                int entryTok;
                object?[]? selectedValues = null;

                if (string.IsNullOrWhiteSpace(entryAttributeTypeName))
                {
                    entryTok = Cnidaria.Cs.BytecodeBuilder.FindEntryPointMethodDef(appModule);
                }
                else
                {
                    var attrArgs = entryAttributeArgs ?? Array.Empty<string>();
                    var callArgs = entryMethodArgs ?? Array.Empty<string>();

                    if (!TryResolveAttributedEntryPoint(
                            appViewFlat,
                            entryAttributeTypeName!,
                            attrArgs,
                            callArgs, 
                            out entryTok,
                            out selectedValues,
                            out var err))
                    {
                        var diagnostic = new Diagnostic("ENTRYPOINT", DiagnosticSeverity.Error, err, default);
                        diagnostics.Add(diagnostic);
                        output.AppendLine(diagnostic.GetMessage());
                        return (output.ToString(), diagnostics, ExecutionContext.Empty);
                    }

                    if (MetadataToken.Table(entryTok) != MetadataToken.MethodDef)
                    {
                        var diagnostic = new Diagnostic("ENTRYPOINT", DiagnosticSeverity.Error,
                            "Selected command method is not a module MethodDef (unexpected token table).", default);
                        diagnostics.Add(diagnostic);
                        output.AppendLine(diagnostic.GetMessage());
                        return (output.ToString(), diagnostics, ExecutionContext.Empty);
                    }
                }
                //Cnidaria.Cs.BytecodeDump.DumpReachable(modules, appModule, entryTok);

                var rts = new Cnidaria.Cs.RuntimeTypeSystem(modules);

                byte[] mem = new byte[stackSize + heapSize + metaSize];
                int metaEnd = metaSize;
                int stackBase = metaEnd;
                int stackEnd = stackBase + stackSize;
                var limits = execLimits;
                var sb = new StringBuilder();
                string result = string.Empty;
                long instructionCount = -1;
                long stackUsage = -1;
                long heapUsage = -1;
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
                    Slot[]? initialArgs = null;
                    if (selectedValues != null)
                    {
                        var rm = rts.ResolveMethod(appModule, entryTok);

                        if (!rm.IsStatic || rm.HasThis)
                            throw new InvalidOperationException("Command entrypoints must be static.");

                        initialArgs = BuildInitialArgs(vm, rts, rm, selectedValues!);
                    }
                    if (diagnostics.Any(x => x.GetSeverity() == DiagnosticSeverity.Error)) 
                        return (output.ToString(), diagnostics, ExecutionContext.Empty);
                    var t = Stopwatch.StartNew();
                    try
                    {
                        host?.Invoke(new Cnidaria.Cs.HostInterface(vm, rts, modules));
                        if (initialArgs != null)
                            vm.Execute(appModule, entryFn, cts.Token, limits, initialArgs);
                        else
                            vm.Execute(appModule, entryFn, cts.Token, limits);
                    }
                    catch (Exception e)
                    {
                        var diagnostic = new Diagnostic("INTERNAL", DiagnosticSeverity.Error, e.ToString(), default);
                        diagnostics.Add(diagnostic);
                        output.AppendLine(diagnostic.GetMessage());

                        return (sb.ToString() + output.ToString(), diagnostics, ExecutionContext.Empty);
                    }
                    t.Stop();
                    result = sb.ToString();
                    instructionCount = vm.InctructionsElapsed;
                    stackUsage = vm.StackPeakBytes;
                    heapUsage = vm.HeapPeakBytes;
                    timeElapsed = t.Elapsed;
                }

                return (result, diagnostics, 
                    new ExecutionContext(Math.Max(instructionCount, -1), timeElapsed, stackUsage, heapUsage));
            }
            catch (Exception ex)
            {
                diagnostics.Add(new Diagnostic("INTERNAL", DiagnosticSeverity.Error, ex.ToString(), default));
                return (output.ToString(), diagnostics, ExecutionContext.Empty);
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
                allowInlining: true,
                print: false);
            byte[] stdFlatMd = Cnidaria.Cs.FlatMetadataBuilder.Build(stdMd);
            IMetadataView stdViewFlat = new FlatMetadataView(stdFlatMd);
            return (stdViewFlat, stdFuncs);
        }
        public static (IMetadataView meta, Dictionary<int, BytecodeFunction> funcs, List<IDiagnostic> diags) CompileLibrary(string source, string modulename)
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
            var (md, funcs, diags, ex) = compilation.BuildModule(
                moduleName: modulename,
                tree: trees[0],
                includeCoreTypesInTypeDefs: false,
                defaultExternalAssemblyName: "std",
                allowInlining: true,
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
            return (viewFlat, funcs, diagnostics);
        }
        private static (byte[]? flatMd, IMetadataView? meta, Dictionary<int, BytecodeFunction>? funcs, List<IDiagnostic> diags)
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

            var (md, funcs, diags, ex) = compilation.BuildModule(
                moduleName: moduleName,
                tree: tree,
                includeCoreTypesInTypeDefs: false,
                defaultExternalAssemblyName: "std",
                allowInlining: true,
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

            return (flatMd, viewFlat, funcs, diagnostics);
        }

        public static (byte[]? image, List<IDiagnostic> diagnostics) CompileExternalLibraryToBytes(
            string source,
            string moduleName = "external")
        {
            var (flatMd, _, funcs, diags) = CompileLibraryCore(source, moduleName);
            if (HasErrors(diags) || flatMd == null || funcs == null)
                return (null, diags);

            return (BytecodeSerializer.SerializeCompiledModule(flatMd, funcs), diags);
        }
        public static (byte[]? image, List<IDiagnostic> diagnostics) CompileApplicationToBytes(
            string source,
            byte[]? externalLibImage = null,
            bool allowInlining = true)
        {
            var diagnostics = new List<IDiagnostic>(ExtendedLibrary.diags);

            var parser = new Parser(source);
            var root = parser.Parse();

            foreach (var diag in parser.LexerDiagnostics)
                diagnostics.Add(diag);
            foreach (var diag in parser.Diagnostics)
                diagnostics.Add(diag);
            if (HasErrors(diagnostics))
                return (null, diagnostics);

            var tree = new SyntaxTree(root, "app");
            var trees = ImmutableArray.Create(tree);

            CompiledModuleData? ext = null;
            MetadataReferenceSet refs;

            if (externalLibImage != null)
            {
                ext = CompiledModuleData.Load(externalLibImage);
                refs = new MetadataReferenceSet(new[] { StandartLibrary.meta, ExtendedLibrary.meta, ext.Value.Meta });
            }
            else
            {
                refs = new MetadataReferenceSet(new[] { StandartLibrary.meta, ExtendedLibrary.meta });
            }

            var compilation = CompilationFactory.Create(trees, refs, out var declDiag);

            foreach (var diag in declDiag)
                diagnostics.Add(diag);
            if (HasErrors(diagnostics))
                return (null, diagnostics);

            var (md, funcs, diags, ex) = compilation.BuildModule(
                moduleName: "app",
                tree: tree,
                includeCoreTypesInTypeDefs: false,
                defaultExternalAssemblyName: "std",
                allowInlining: allowInlining,
                externalAssemblyResolver: refs.ResolveAssemblyName,
                print: false);

            if (ex != null)
                diagnostics.Add(new Diagnostic("BUILD", DiagnosticSeverity.Error, ex.ToString(), default));

            foreach (var diag in diags)
                diagnostics.Add(diag);
            if (HasErrors(diagnostics))
                return (null, diagnostics);

            byte[] flatMd = FlatMetadataBuilder.Build(md);
            byte[] image = BytecodeSerializer.SerializeCompiledModule(flatMd, funcs);
            return (image, diagnostics);
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
        private static bool TryBindPositionalArguments(
    Compilation compilation,
    MethodSymbol m,
    string[] callArgs,
    out object?[] values,
    out int cost,
    out int defaultsUsed,
    out bool usedParams)
        {
            cost = 0;
            defaultsUsed = 0;
            usedParams = false;

            var ps = m.Parameters;
            values = new object?[ps.Length];

            int ai = 0;

            bool hasParams = ps.Length > 0 && ps[^1].IsParams && IsParamsStringArray(ps[^1].Type);
            int fixedCount = hasParams ? ps.Length - 1 : ps.Length;

            // Bind fixed parameters
            for (int pi = 0; pi < fixedCount; pi++)
            {
                var p = ps[pi];

                if (ai < callArgs.Length)
                {
                    if (!TryParseStringToType(callArgs[ai], p.Type, out var v, out var c))
                        return false;

                    values[pi] = v;
                    cost += c;
                    ai++;
                }
                else
                {
                    if (!TryGetOptionalDefault(p, out var dv))
                        return false;

                    values[pi] = dv;
                    defaultsUsed++;
                }
            }

            // Bind params string[]
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

            // Extra args => reject
            return ai == callArgs.Length;
        }
        private static bool IsParamsStringArray(TypeSymbol t)
        {
            if (t is not ArrayTypeSymbol at) return false;
            if (at.Rank != 1) return false;
            return at.ElementType.SpecialType == SpecialType.System_String;
        }

        private static bool TryGetOptionalDefault(ParameterSymbol p, out object? value)
        {
            value = null;

            if (!p.HasExplicitDefault)
                return false;

            if (!p.DefaultValueOpt.HasValue)
                return false;

            value = p.DefaultValueOpt.Value;
            return true;
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
        private static bool TryParseStringToType(string token, TypeSymbol t, out object? value, out int cost)
        {
            value = null;
            cost = 0;

            var k = ClassifyArg(token);

            switch (t.SpecialType)
            {
                case SpecialType.System_String:
                    value = token;
                    cost = (k == ArgLexKind.Other) ? 0 : 10; // prefer numeric overloads
                    return true;

                case SpecialType.System_Boolean:
                    if (bool.TryParse(token, out var b))
                    {
                        value = b;
                        cost = 0;
                        return true;
                    }
                    return false;

                case SpecialType.System_Char:
                    if (token.Length == 1)
                    {
                        value = token[0];
                        cost = 0;
                        return true;
                    }
                    return false;

                case SpecialType.System_Int8:
                    return TryParseInt(token, NumberStyles.Integer, out sbyte sb, out value, out cost, baseCost: 0);
                case SpecialType.System_UInt8:
                    return TryParseInt(token, NumberStyles.Integer, out byte ub, out value, out cost, baseCost: 0);
                case SpecialType.System_Int16:
                    return TryParseInt(token, NumberStyles.Integer, out short s16, out value, out cost, baseCost: 1);
                case SpecialType.System_UInt16:
                    return TryParseInt(token, NumberStyles.Integer, out ushort u16, out value, out cost, baseCost: 1);
                case SpecialType.System_Int32:
                    return TryParseInt(token, NumberStyles.Integer, out int s32, out value, out cost, baseCost: 2);
                case SpecialType.System_UInt32:
                    return TryParseInt(token, NumberStyles.Integer, out uint u32, out value, out cost, baseCost: 2);
                case SpecialType.System_Int64:
                    return TryParseInt(token, NumberStyles.Integer, out long s64, out value, out cost, baseCost: 3);
                case SpecialType.System_UInt64:
                    return TryParseInt(token, NumberStyles.Integer, out ulong u64, out value, out cost, baseCost: 3);

                case SpecialType.System_Single:
                    return TryParseFloat(token, out float f, out value, out cost, k, baseCost: 1);

                case SpecialType.System_Double:
                    return TryParseFloat(token, out double d, out value, out cost, k, baseCost: 0);

                default:
                    return false;
            }
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
        private static Slot[] BuildInitialArgs(Vm vm, RuntimeTypeSystem rts, RuntimeMethod rm, object?[] values)
        {
            if (rm.HasThis) throw new InvalidOperationException("Instance entrypoints are not supported.");
            if (rm.ParameterTypes.Length != values.Length)
                throw new InvalidOperationException("Bound values length mismatch.");

            var args = new Slot[values.Length];

            for (int i = 0; i < values.Length; i++)
            {
                var pt = rm.ParameterTypes[i];
                args[i] = MarshalToSlot(vm, rts, pt, values[i]);
            }

            return args;
        }

        private static Slot MarshalToSlot(Vm vm, RuntimeTypeSystem rts, RuntimeType t, object? v)
        {
            if (t.IsReferenceType)
            {
                if (v is null)
                    return new Slot(SlotKind.Null, 0);

                if (t.TypeId == rts.SystemString.TypeId)
                {
                    var s = (string)v;
                    return vm.HostAllocString(s).ToSlot();
                }

                if (t.Kind == RuntimeTypeKind.Array)
                {
                    if (v is not string?[] ss)
                        throw new NotSupportedException("Only string[] values are supported for array parameters.");

                    var arr = vm.HostAllocStringArray(t, ss);
                    return arr.ToSlot();
                }

                throw new NotSupportedException($"Host marshal not supported for ref type: {t.Namespace}.{t.Name}");
            }

            // Value types
            if (v is null)
                throw new InvalidOperationException($"Null passed to value type {t.Namespace}.{t.Name}");

            if (t.Namespace == "System")
            {
                switch (t.Name)
                {
                    case "Boolean": return new Slot(SlotKind.I4, (bool)v ? 1 : 0);
                    case "Char": return new Slot(SlotKind.I4, (char)v);
                    case "SByte": return new Slot(SlotKind.I4, (sbyte)v);
                    case "Byte": return new Slot(SlotKind.I4, (byte)v);
                    case "Int16": return new Slot(SlotKind.I4, (short)v);
                    case "UInt16": return new Slot(SlotKind.I4, unchecked((int)(ushort)v));
                    case "Int32": return new Slot(SlotKind.I4, (int)v);
                    case "UInt32": return new Slot(SlotKind.I4, unchecked((int)(uint)v));
                    case "Int64": return new Slot(SlotKind.I8, (long)v);
                    case "UInt64": return new Slot(SlotKind.I8, unchecked((long)(ulong)v));
                    case "Single": return new Slot(SlotKind.R8, BitConverter.DoubleToInt64Bits((float)v));
                    case "Double": return new Slot(SlotKind.R8, BitConverter.DoubleToInt64Bits((double)v));
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
