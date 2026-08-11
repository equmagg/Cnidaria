using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using Cnidaria.Arm;
using Cnidaria.RiscV;
using Cnidaria.X86;

namespace Cnidaria.C
{
    public sealed class SourceFile
    {
        public string FilePath { get; }
        public string Text { get; }

        public SourceFile(string filePath, string text)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("A translation unit must have a file path.", nameof(filePath));

            FilePath = filePath;
            Text = text ?? throw new ArgumentNullException(nameof(text));
        }
    }

    public enum LinkerStage : byte
    {
        Frontend,
        Lowering,
        CodeGeneration,
        Linking,
    }

    public sealed class LinkerDiagnostic
    {
        public DiagnosticSeverity Severity { get; }
        public LinkerStage Stage { get; }
        public string Message { get; }
        public string? FilePath { get; }
        public TextSpan Position { get; }

        public LinkerDiagnostic(
            DiagnosticSeverity severity,
            LinkerStage stage,
            string message,
            string? filePath = null,
            TextSpan position = default)
        {
            Severity = severity;
            Stage = stage;
            Message = message ?? string.Empty;
            FilePath = filePath;
            Position = position;
        }

        public override string ToString()
            => string.IsNullOrEmpty(FilePath)
                ? Message
                : $"{FilePath}: {Message}";
    }

    public sealed class StaticLinkerOptions
    {
        public CompilationOptions CompilationOptions { get; }
        public ImmutableArray<IncludeFile> IncludeFiles { get; }
        public ImmutableArray<string> IncludeSearchPaths { get; }
        public IIncludeResolver? IncludeResolver { get; }
        public ImmutableDictionary<string, string> PredefinedMacros { get; }
        public ImmutableHashSet<string> AllowedUndefinedSymbols { get; }
        public ImmutableArray<SourceFile> LibraryTranslationUnits { get; }
        public bool IncludeStandardHeaders { get; }
        public bool LinkStandardLibrary { get; }
        public bool EmitStartup { get; }
        public bool AllowUndefinedSymbols { get; }
        public string EntryFunctionName { get; }

        public StaticLinkerOptions(
            TargetInfo target,
            IEnumerable<IncludeFile>? includeFiles = null,
            IEnumerable<string>? includeSearchPaths = null,
            IIncludeResolver? includeResolver = null,
            IReadOnlyDictionary<string, string>? predefinedMacros = null,
            IEnumerable<string>? allowedUndefinedSymbols = null,
            bool includeStandardHeaders = true,
            bool linkStandardLibrary = true,
            bool emitStartup = true,
            bool allowUndefinedSymbols = false,
            string entryFunctionName = "main",
            InliningOptions? inlining = null,
            TrimmingOptions? trimming = null,
            IEnumerable<SourceFile>? libraryTranslationUnits = null)
        {
            var requestedTrimming = trimming ?? TrimmingOptions.Default;
            var effectiveTrimming = new TrimmingOptions(
                enabled: requestedTrimming.Enabled,
                preserveExternallyVisibleSymbols: true,
                rootSymbols: requestedTrimming.RootSymbols);
            CompilationOptions = new CompilationOptions(
                target ?? throw new ArgumentNullException(nameof(target)),
                inlining,
                effectiveTrimming);
            IncludeFiles = includeFiles?.ToImmutableArray() ?? ImmutableArray<IncludeFile>.Empty;
            IncludeSearchPaths = includeSearchPaths?.ToImmutableArray() ?? ImmutableArray<string>.Empty;
            IncludeResolver = includeResolver;
            PredefinedMacros = predefinedMacros is null
                ? ImmutableDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal)
                : predefinedMacros.ToImmutableDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            AllowedUndefinedSymbols = allowedUndefinedSymbols is null
                ? ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal)
                : allowedUndefinedSymbols.ToImmutableHashSet(StringComparer.Ordinal);
            LibraryTranslationUnits = libraryTranslationUnits?.ToImmutableArray() ?? ImmutableArray<SourceFile>.Empty;
            if (LibraryTranslationUnits.Any(static source => source is null))
                throw new ArgumentException("A library translation unit cannot be null.", nameof(libraryTranslationUnits));
            IncludeStandardHeaders = includeStandardHeaders;
            LinkStandardLibrary = linkStandardLibrary;
            EmitStartup = emitStartup;
            AllowUndefinedSymbols = allowUndefinedSymbols;
            EntryFunctionName = string.IsNullOrWhiteSpace(entryFunctionName)
                ? throw new ArgumentException("The entry function name cannot be empty.", nameof(entryFunctionName))
                : entryFunctionName;
        }
    }

    public sealed class StaticLinkResult<TProgram>
        where TProgram : class
    {
        public TProgram? Program { get; }
        public ImmutableArray<LinkerDiagnostic> Diagnostics { get; }
        public ImmutableArray<string> LinkedTranslationUnits { get; }
        public bool Succeeded => Program is not null && Diagnostics.All(static diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);

        internal StaticLinkResult(
            TProgram? program,
            ImmutableArray<LinkerDiagnostic> diagnostics,
            ImmutableArray<string> linkedTranslationUnits)
        {
            Program = program;
            Diagnostics = diagnostics.IsDefault ? ImmutableArray<LinkerDiagnostic>.Empty : diagnostics;
            LinkedTranslationUnits = linkedTranslationUnits.IsDefault ? ImmutableArray<string>.Empty : linkedTranslationUnits;
        }
    }

    public static class StaticLinker
    {
        public static StaticLinkResult<RegisterBytecodeProgram> LinkRegisterBytecode(IEnumerable<SourceFile> translationUnits, TargetInfo target)
            => LinkRegisterBytecode(translationUnits, new StaticLinkerOptions(target));

        public static StaticLinkResult<RegisterBytecodeProgram> LinkRegisterBytecode(IEnumerable<SourceFile> translationUnits, StaticLinkerOptions options)
        {
            if (options is null)
                throw new ArgumentNullException(nameof(options));
            if (!options.CompilationOptions.Target.IsRegisterBytecode)
                throw new ArgumentException("The linker target is not register bytecode.", nameof(options));

            var diagnostics = ImmutableArray.CreateBuilder<LinkerDiagnostic>();
            var inputs = CompileInputs(translationUnits, options, diagnostics);
            if (HasErrors(diagnostics) || inputs.UserUnits.Length == 0)
                return Failure<RegisterBytecodeProgram>(diagnostics);

            var primaryIndex = FindPrimaryUnit(inputs.UserUnits, options, diagnostics);
            if (HasErrors(diagnostics))
                return Failure<RegisterBytecodeProgram>(diagnostics);

            var orderedUsers = MovePrimaryFirst(inputs.UserUnits, primaryIndex);
            var selected = SelectRegisterBytecodeArchiveMembers(orderedUsers, inputs.LibraryUnits);
            ValidateDeclarations(selected, diagnostics);
            if (HasErrors(diagnostics))
                return Failure<RegisterBytecodeProgram>(diagnostics);

            AddUndefinedDiagnostics(GetRegisterBytecodeUndefinedReferences(selected), options, diagnostics);
            if (HasErrors(diagnostics))
                return Failure<RegisterBytecodeProgram>(diagnostics);

            try
            {
                var units = selected
                    .Select(static unit => new RegisterBytecodeCompilationUnit(unit.Lir, unit.Linkage))
                    .ToImmutableArray();
                var program = RegisterBytecodeCodeGenerator.Generate(units, options.EntryFunctionName);
                return Success(program, selected, diagnostics);
            }
            catch (Exception exception)
            {
                diagnostics.Add(CodeGenerationError(selected[0].Source.FilePath, exception));
                return Failure<RegisterBytecodeProgram>(diagnostics);
            }
        }

        public static StaticLinkResult<X86Program> LinkX86(IEnumerable<SourceFile> translationUnits, TargetInfo target)
            => LinkX86(translationUnits, new StaticLinkerOptions(target));
        public static StaticLinkResult<X86Program> LinkX86(IEnumerable<SourceFile> translationUnits, StaticLinkerOptions options)
        {
            if (options is null)
                throw new ArgumentNullException(nameof(options));
            if (!options.CompilationOptions.Target.IsX86)
                throw new ArgumentException("The linker target is not x86.", nameof(options));

            var diagnostics = ImmutableArray.CreateBuilder<LinkerDiagnostic>();
            var inputs = CompileInputs(translationUnits, options, diagnostics);
            if (HasErrors(diagnostics) || inputs.UserUnits.Length == 0)
                return Failure<X86Program>(diagnostics);

            var primaryIndex = FindPrimaryUnit(inputs.UserUnits, options, diagnostics);
            if (HasErrors(diagnostics))
                return Failure<X86Program>(diagnostics);

            var userObjects = GenerateX86Objects(inputs.UserUnits, primaryIndex, options, diagnostics);
            var libraryObjects = GenerateX86Objects(inputs.LibraryUnits, -1, options, diagnostics);
            if (HasErrors(diagnostics))
                return Failure<X86Program>(diagnostics);

            var orderedUsers = MovePrimaryFirst(userObjects, primaryIndex);
            var selected = SelectArchiveMembers(
                orderedUsers,
                libraryObjects,
                GetX86Definitions,
                GetX86UndefinedReferences);
            ValidateDeclarations(selected.Select(static item => item.Unit).ToImmutableArray(), diagnostics);
            if (HasErrors(diagnostics))
                return Failure<X86Program>(diagnostics);

            AddUndefinedDiagnostics(
                GetUndefinedReferences(selected, GetX86Definitions, GetX86UndefinedReferences),
                options,
                diagnostics);
            if (HasErrors(diagnostics))
                return Failure<X86Program>(diagnostics);

            try
            {
                var program = X86ObjectComposer.Compose(selected[0].Object, selected.Skip(1).Select(static item => item.Object).ToArray());
                return Success(program, selected, diagnostics);
            }
            catch (Exception exception)
            {
                diagnostics.Add(LinkerError(exception.Message));
                return Failure<X86Program>(diagnostics);
            }
        }

        public static StaticLinkResult<RiscVProgram> LinkRiscV(IEnumerable<SourceFile> translationUnits, TargetInfo target)
            => LinkRiscV(translationUnits, new StaticLinkerOptions(target));
        public static StaticLinkResult<RiscVProgram> LinkRiscV(IEnumerable<SourceFile> translationUnits, StaticLinkerOptions options)
        {
            if (options is null)
                throw new ArgumentNullException(nameof(options));
            if (!options.CompilationOptions.Target.IsRiscV)
                throw new ArgumentException("The linker target is not RISC-V.", nameof(options));

            var diagnostics = ImmutableArray.CreateBuilder<LinkerDiagnostic>();
            var inputs = CompileInputs(translationUnits, options, diagnostics);
            if (HasErrors(diagnostics) || inputs.UserUnits.Length == 0)
                return Failure<RiscVProgram>(diagnostics);

            var primaryIndex = FindPrimaryUnit(inputs.UserUnits, options, diagnostics);
            if (HasErrors(diagnostics))
                return Failure<RiscVProgram>(diagnostics);

            var userObjects = GenerateRiscVObjects(inputs.UserUnits, primaryIndex, options, diagnostics);
            var libraryObjects = GenerateRiscVObjects(inputs.LibraryUnits, -1, options, diagnostics);
            if (HasErrors(diagnostics))
                return Failure<RiscVProgram>(diagnostics);

            var orderedUsers = MovePrimaryFirst(userObjects, primaryIndex);
            var selected = SelectArchiveMembers(
                orderedUsers,
                libraryObjects,
                GetRiscVDefinitions,
                GetRiscVUndefinedReferences);
            ValidateDeclarations(selected.Select(static item => item.Unit).ToImmutableArray(), diagnostics);
            if (HasErrors(diagnostics))
                return Failure<RiscVProgram>(diagnostics);

            AddUndefinedDiagnostics(
                GetUndefinedReferences(selected, GetRiscVDefinitions, GetRiscVUndefinedReferences),
                options,
                diagnostics);
            if (HasErrors(diagnostics))
                return Failure<RiscVProgram>(diagnostics);

            try
            {
                var program = RiscVObjectComposer.Compose(selected[0].Object, selected.Skip(1).Select(static item => item.Object).ToArray());
                return Success(program, selected, diagnostics);
            }
            catch (Exception exception)
            {
                diagnostics.Add(LinkerError(exception.Message));
                return Failure<RiscVProgram>(diagnostics);
            }
        }

        public static StaticLinkResult<ArmProgram> LinkArm(IEnumerable<SourceFile> translationUnits, TargetInfo target)
            => LinkArm(translationUnits, new StaticLinkerOptions(target));
        public static StaticLinkResult<ArmProgram> LinkArm(IEnumerable<SourceFile> translationUnits, StaticLinkerOptions options)
        {
            if (options is null)
                throw new ArgumentNullException(nameof(options));
            if (!options.CompilationOptions.Target.IsArm)
                throw new ArgumentException("The linker target is not ARM.", nameof(options));

            var diagnostics = ImmutableArray.CreateBuilder<LinkerDiagnostic>();
            var inputs = CompileInputs(translationUnits, options, diagnostics);
            if (HasErrors(diagnostics) || inputs.UserUnits.Length == 0)
                return Failure<ArmProgram>(diagnostics);

            var primaryIndex = FindPrimaryUnit(inputs.UserUnits, options, diagnostics);
            if (HasErrors(diagnostics))
                return Failure<ArmProgram>(diagnostics);

            var userObjects = GenerateArmObjects(inputs.UserUnits, primaryIndex, options, diagnostics);
            var libraryObjects = GenerateArmObjects(inputs.LibraryUnits, -1, options, diagnostics);
            if (HasErrors(diagnostics))
                return Failure<ArmProgram>(diagnostics);

            var orderedUsers = MovePrimaryFirst(userObjects, primaryIndex);
            var selected = SelectArchiveMembers(
                orderedUsers,
                libraryObjects,
                GetArmDefinitions,
                GetArmUndefinedReferences);
            ValidateDeclarations(selected.Select(static item => item.Unit).ToImmutableArray(), diagnostics);
            if (HasErrors(diagnostics))
                return Failure<ArmProgram>(diagnostics);

            AddUndefinedDiagnostics(
                GetUndefinedReferences(selected, GetArmDefinitions, GetArmUndefinedReferences),
                options,
                diagnostics);
            if (HasErrors(diagnostics))
                return Failure<ArmProgram>(diagnostics);

            try
            {
                var program = ArmObjectComposer.Compose(selected[0].Object, selected.Skip(1).Select(static item => item.Object).ToArray());
                return Success(program, selected, diagnostics);
            }
            catch (Exception exception)
            {
                diagnostics.Add(LinkerError(exception.Message));
                return Failure<ArmProgram>(diagnostics);
            }
        }

        private static CompiledInputs CompileInputs(
            IEnumerable<SourceFile> translationUnits,
            StaticLinkerOptions options,
            ImmutableArray<LinkerDiagnostic>.Builder diagnostics)
        {
            if (translationUnits is null)
                throw new ArgumentNullException(nameof(translationUnits));

            var userSources = translationUnits.ToImmutableArray();
            if (userSources.Any(static source => source is null))
                throw new ArgumentException("A translation unit cannot be null.", nameof(translationUnits));
            if (userSources.Length == 0)
            {
                diagnostics.Add(LinkerError("No translation units were supplied."));
                return new CompiledInputs(ImmutableArray<CompiledUnit>.Empty, ImmutableArray<CompiledUnit>.Empty);
            }

            var userUnits = CompileUnits(userSources, options, diagnostics);
            var librarySources = options.LibraryTranslationUnits.ToBuilder();
            if (options.LinkStandardLibrary)
            {
                try
                {
                    librarySources.AddRange(StandardLibrarySources.CreateFiles());
                }
                catch (Exception exception)
                {
                    diagnostics.Add(new LinkerDiagnostic(
                        DiagnosticSeverity.Error,
                        LinkerStage.Frontend,
                        exception.Message));
                }
            }

            var libraryUnits = CompileUnits(librarySources.ToImmutable(), options, diagnostics);
            return new CompiledInputs(userUnits, libraryUnits);
        }

        private static ImmutableArray<CompiledUnit> CompileUnits(
            ImmutableArray<SourceFile> sources,
            StaticLinkerOptions options,
            ImmutableArray<LinkerDiagnostic>.Builder diagnostics)
        {
            var units = ImmutableArray.CreateBuilder<CompiledUnit>(sources.Length);
            foreach (var source in sources)
            {
                SyntaxTree tree;
                Compilation compilation;
                try
                {
                    tree = SyntaxTree.ParseSource(
                        source.Text,
                        source.FilePath,
                        options.IncludeFiles,
                        options.IncludeSearchPaths,
                        options.IncludeResolver,
                        options.PredefinedMacros,
                        PreprocessorEnvironment.ForTarget(options.CompilationOptions.Target),
                        options.IncludeStandardHeaders);
                    compilation = Compilation.Create(
                        new[] { tree },
                        source.FilePath,
                        options.CompilationOptions);
                }
                catch (Exception exception)
                {
                    diagnostics.Add(new LinkerDiagnostic(
                        DiagnosticSeverity.Error,
                        LinkerStage.Frontend,
                        exception.Message,
                        source.FilePath));
                    continue;
                }

                var unitHasErrors = false;
                try
                {
                    foreach (var diagnostic in compilation.GetDiagnostics())
                    {
                        diagnostics.Add(new LinkerDiagnostic(
                            diagnostic.Severity,
                            LinkerStage.Frontend,
                            diagnostic.Message,
                            source.FilePath,
                            diagnostic.Position));
                        unitHasErrors |= diagnostic.Severity == DiagnosticSeverity.Error;
                    }
                }
                catch (Exception exception)
                {
                    diagnostics.Add(new LinkerDiagnostic(
                        DiagnosticSeverity.Error,
                        LinkerStage.Frontend,
                        exception.Message,
                        source.FilePath));
                    continue;
                }
                if (unitHasErrors)
                    continue;

                SemanticModel semanticModel;
                FileScopeLinkageMap linkage;
                try
                {
                    semanticModel = compilation.GetSemanticModel(tree);
                    linkage = FileScopeLinkageMap.Create(semanticModel);
                }
                catch (Exception exception)
                {
                    diagnostics.Add(new LinkerDiagnostic(
                        DiagnosticSeverity.Error,
                        LinkerStage.Frontend,
                        exception.Message,
                        source.FilePath));
                    continue;
                }

                try
                {
                    var lir = LirModule.Lower(semanticModel);
                    foreach (var problem in lir.Problems)
                    {
                        diagnostics.Add(new LinkerDiagnostic(
                            DiagnosticSeverity.Error,
                            LinkerStage.Lowering,
                            problem.Message,
                            source.FilePath));
                        unitHasErrors = true;
                    }
                    if (!unitHasErrors)
                        units.Add(new CompiledUnit(source, semanticModel, linkage, lir));
                }
                catch (Exception exception)
                {
                    diagnostics.Add(new LinkerDiagnostic(
                        DiagnosticSeverity.Error,
                        LinkerStage.Lowering,
                        exception.Message,
                        source.FilePath));
                }
            }
            return units.ToImmutable();
        }

        private static int FindPrimaryUnit(
            ImmutableArray<CompiledUnit> units,
            StaticLinkerOptions options,
            ImmutableArray<LinkerDiagnostic>.Builder diagnostics)
        {
            var primary = -1;
            for (var i = 0; i < units.Length; i++)
            {
                var containsEntry = units[i].Lir.Functions.Any(function =>
                    function.Symbol is { } symbol &&
                    !units[i].Linkage.IsInternal(symbol) &&
                    string.Equals(symbol.Name, options.EntryFunctionName, StringComparison.Ordinal));
                if (!containsEntry)
                    continue;

                if (primary >= 0)
                {
                    diagnostics.Add(LinkerError($"Multiple definitions of entry function '{options.EntryFunctionName}'."));
                    return primary;
                }
                primary = i;
            }

            if (options.EmitStartup && primary < 0)
                diagnostics.Add(LinkerError($"Entry function '{options.EntryFunctionName}' was not found."));

            return primary >= 0 ? primary : 0;
        }

        private static ImmutableArray<ObjectUnit<X86Program>> GenerateX86Objects(
            ImmutableArray<CompiledUnit> units,
            int primaryIndex,
            StaticLinkerOptions options,
            ImmutableArray<LinkerDiagnostic>.Builder diagnostics)
        {
            var result = ImmutableArray.CreateBuilder<ObjectUnit<X86Program>>(units.Length);
            for (var i = 0; i < units.Length; i++)
            {
                try
                {
                    var program = X86CodeGenerator.Generate(
                        units[i].Lir,
                        options: new X86CodeGeneratorOptions
                        {
                            EmitStartup = options.EmitStartup && i == primaryIndex,
                            EntryFunctionName = options.EntryFunctionName,
                        });
                    result.Add(new ObjectUnit<X86Program>(units[i], program));
                }
                catch (Exception exception)
                {
                    diagnostics.Add(CodeGenerationError(units[i].Source.FilePath, exception));
                }
            }
            return result.ToImmutable();
        }

        private static ImmutableArray<ObjectUnit<RiscVProgram>> GenerateRiscVObjects(
            ImmutableArray<CompiledUnit> units,
            int primaryIndex,
            StaticLinkerOptions options,
            ImmutableArray<LinkerDiagnostic>.Builder diagnostics)
        {
            var result = ImmutableArray.CreateBuilder<ObjectUnit<RiscVProgram>>(units.Length);
            for (var i = 0; i < units.Length; i++)
            {
                try
                {
                    var program = RiscVCodeGenerator.Generate(
                        units[i].Lir,
                        options: new RiscVCodeGeneratorOptions
                        {
                            EmitStartup = options.EmitStartup && i == primaryIndex,
                            EntryFunctionName = options.EntryFunctionName,
                        });
                    result.Add(new ObjectUnit<RiscVProgram>(units[i], program));
                }
                catch (Exception exception)
                {
                    diagnostics.Add(CodeGenerationError(units[i].Source.FilePath, exception));
                }
            }
            return result.ToImmutable();
        }

        private static ImmutableArray<ObjectUnit<ArmProgram>> GenerateArmObjects(
            ImmutableArray<CompiledUnit> units,
            int primaryIndex,
            StaticLinkerOptions options,
            ImmutableArray<LinkerDiagnostic>.Builder diagnostics)
        {
            var result = ImmutableArray.CreateBuilder<ObjectUnit<ArmProgram>>(units.Length);
            for (var i = 0; i < units.Length; i++)
            {
                try
                {
                    var program = ArmCodeGenerator.Generate(
                        units[i].Lir,
                        options: new ArmCodeGeneratorOptions
                        {
                            EmitStartup = options.EmitStartup && i == primaryIndex,
                            EntryFunctionName = options.EntryFunctionName,
                        });
                    result.Add(new ObjectUnit<ArmProgram>(units[i], program));
                }
                catch (Exception exception)
                {
                    diagnostics.Add(CodeGenerationError(units[i].Source.FilePath, exception));
                }
            }
            return result.ToImmutable();
        }

        private static ImmutableArray<CompiledUnit> MovePrimaryFirst(
            ImmutableArray<CompiledUnit> units,
            int primaryIndex)
        {
            if (units.Length == 0 || primaryIndex <= 0)
                return units;

            var result = ImmutableArray.CreateBuilder<CompiledUnit>(units.Length);
            result.Add(units[primaryIndex]);
            for (var index = 0; index < units.Length; index++)
            {
                if (index != primaryIndex)
                    result.Add(units[index]);
            }
            return result.ToImmutable();
        }

        private static ImmutableArray<CompiledUnit> SelectRegisterBytecodeArchiveMembers(
            ImmutableArray<CompiledUnit> mandatoryUnits,
            ImmutableArray<CompiledUnit> archiveMembers)
        {
            var selected = mandatoryUnits.ToBuilder();
            var included = new bool[archiveMembers.Length];
            var changed = true;
            while (changed)
            {
                changed = false;
                var unresolved = GetRegisterBytecodeUndefinedReferences(selected);
                if (unresolved.Count == 0)
                    break;

                for (var index = 0; index < archiveMembers.Length; index++)
                {
                    if (included[index] || !GetRegisterBytecodeDefinitions(archiveMembers[index]).Overlaps(unresolved))
                        continue;

                    included[index] = true;
                    selected.Add(archiveMembers[index]);
                    changed = true;
                    unresolved = GetRegisterBytecodeUndefinedReferences(selected);
                }
            }
            return selected.ToImmutable();
        }

        private static ImmutableHashSet<string> GetRegisterBytecodeDefinitions(CompiledUnit unit)
        {
            var definitions = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            foreach (var function in unit.Lir.Functions)
            {
                if (function.Symbol is { } symbol && !unit.Linkage.IsInternal(symbol))
                    definitions.Add(symbol.Name);
            }

            foreach (var global in unit.Lir.Globals)
            {
                if (global.Symbol is null || unit.Linkage.IsInternal(global.Symbol))
                    continue;
                if (global.Initializer is not null || global.StorageClass != StorageClass.Extern)
                    definitions.Add(global.Symbol.Name);
            }
            return definitions.ToImmutable();
        }

        private static ImmutableHashSet<string> GetRegisterBytecodeUndefinedReferences(
            IEnumerable<CompiledUnit> units)
        {
            var definitions = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            var references = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            foreach (var unit in units)
            {
                definitions.UnionWith(GetRegisterBytecodeDefinitions(unit));
                references.UnionWith(GetRegisterBytecodeUnitReferences(unit));
            }
            references.ExceptWith(definitions);
            return references.ToImmutable();
        }

        private static ImmutableHashSet<string> GetRegisterBytecodeUnitReferences(CompiledUnit unit)
        {
            var localDefinitions = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            foreach (var function in unit.Lir.Functions)
            {
                if (function.Symbol is not null)
                    localDefinitions.Add(function.Symbol.Name);
            }
            foreach (var global in unit.Lir.Globals)
            {
                if (global.Symbol is not null && (global.Initializer is not null || global.StorageClass != StorageClass.Extern))
                    localDefinitions.Add(global.Symbol.Name);
            }

            var references = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            foreach (var function in unit.Lir.Functions)
            {
                foreach (var block in function.Blocks)
                {
                    foreach (var instruction in block.Instructions)
                    {
                        foreach (var operand in instruction.Operands)
                            AddRegisterBytecodeOperandReference(operand, unit, references);
                        foreach (var copy in instruction.ParallelCopies)
                            AddRegisterBytecodeOperandReference(copy.Source, unit, references);
                        foreach (var @case in instruction.SwitchCases)
                            AddRegisterBytecodeOperandReference(@case.Value, unit, references);
                        if (instruction.Address is not null)
                            AddRegisterBytecodeAddressReferences(instruction.Address, unit, references);
                    }
                }
            }

            foreach (var global in unit.Lir.Globals)
            {
                if (global.Initializer is not null)
                    AddRegisterBytecodeInitializerReferences(global.Initializer, unit, references);
            }

            references.ExceptWith(localDefinitions);
            return references.ToImmutable();
        }

        private static void AddRegisterBytecodeOperandReference(
            LirOperand operand,
            CompiledUnit unit,
            ImmutableHashSet<string>.Builder references)
        {
            if (operand.Symbol is not null)
                AddRegisterBytecodeSymbolReference(operand.Symbol, unit, references);
            if (operand.Address is not null)
                AddRegisterBytecodeAddressReferences(operand.Address, unit, references);
        }

        private static void AddRegisterBytecodeAddressReferences(
            LirAddress address,
            CompiledUnit unit,
            ImmutableHashSet<string>.Builder references)
        {
            if (address.Symbol is not null)
                AddRegisterBytecodeSymbolReference(address.Symbol, unit, references);
            if (address.BaseOperand is not null)
                AddRegisterBytecodeOperandReference(address.BaseOperand, unit, references);
            if (address.BaseAddress is not null)
                AddRegisterBytecodeAddressReferences(address.BaseAddress, unit, references);
            if (address.Index is not null)
                AddRegisterBytecodeOperandReference(address.Index, unit, references);
        }

        private static void AddRegisterBytecodeInitializerReferences(
            GimpleInitializer initializer,
            CompiledUnit unit,
            ImmutableHashSet<string>.Builder references)
        {
            if (initializer is GimpleExpressionInitializer expressionInitializer)
            {
                AddRegisterBytecodeValueReferences(expressionInitializer.Expression, unit, references);
                return;
            }

            if (initializer is GimpleInitializerList list)
            {
                foreach (var item in list.Items)
                    AddRegisterBytecodeInitializerReferences(item.Initializer, unit, references);
            }
        }

        private static void AddRegisterBytecodeValueReferences(
            GimpleValue value,
            CompiledUnit unit,
            ImmutableHashSet<string>.Builder references)
        {
            switch (value)
            {
                case GimpleSymbolValue symbolValue:
                    AddRegisterBytecodeSymbolReference(symbolValue.Symbol, unit, references);
                    break;
                case GimpleUnaryExpression unary:
                    AddRegisterBytecodeValueReferences(unary.Operand, unit, references);
                    break;
                case GimpleBinaryExpression binary:
                    AddRegisterBytecodeValueReferences(binary.Left, unit, references);
                    AddRegisterBytecodeValueReferences(binary.Right, unit, references);
                    break;
                case GimpleConversionExpression conversion:
                    AddRegisterBytecodeValueReferences(conversion.Operand, unit, references);
                    break;
                case GimpleCastExpression cast:
                    AddRegisterBytecodeValueReferences(cast.Operand, unit, references);
                    break;
                case GimpleAddressOfExpression addressOf:
                    AddRegisterBytecodeValueReferences(addressOf.Target, unit, references);
                    break;
                case GimpleIndirectExpression indirect:
                    AddRegisterBytecodeValueReferences(indirect.Address, unit, references);
                    break;
                case GimpleElementAccessExpression element:
                    AddRegisterBytecodeValueReferences(element.Expression, unit, references);
                    if (element.Index is not null)
                        AddRegisterBytecodeValueReferences(element.Index, unit, references);
                    break;
                case GimpleMemberAccessExpression member:
                    AddRegisterBytecodeValueReferences(member.Expression, unit, references);
                    break;
                case GimpleCallExpression call:
                    AddRegisterBytecodeValueReferences(call.Callee, unit, references);
                    foreach (var argument in call.Arguments)
                        AddRegisterBytecodeValueReferences(argument, unit, references);
                    break;
            }
        }

        private static void AddRegisterBytecodeSymbolReference(
            Symbol symbol,
            CompiledUnit unit,
            ImmutableHashSet<string>.Builder references)
        {
            if (symbol is not FunctionSymbol &&
                !unit.Linkage.Declarations.Any(declaration => ReferenceEquals(declaration.Symbol, symbol)))
            {
                return;
            }
            if (unit.Linkage.IsInternal(symbol))
                return;
            if (symbol is FunctionSymbol { IntrinsicKind: RuntimeIntrinsicKind.BuiltinVaStart or RuntimeIntrinsicKind.BuiltinVaArg or RuntimeIntrinsicKind.CStringWrite })
                return;
            references.Add(symbol.Name);
        }

        private static ImmutableArray<ObjectUnit<TProgram>> MovePrimaryFirst<TProgram>(
            ImmutableArray<ObjectUnit<TProgram>> objects,
            int primaryIndex)
            where TProgram : class
        {
            if (objects.Length == 0 || primaryIndex <= 0)
                return objects;

            var result = ImmutableArray.CreateBuilder<ObjectUnit<TProgram>>(objects.Length);
            result.Add(objects[primaryIndex]);
            for (var i = 0; i < objects.Length; i++)
            {
                if (i != primaryIndex)
                    result.Add(objects[i]);
            }
            return result.ToImmutable();
        }

        private static ImmutableArray<ObjectUnit<TProgram>> SelectArchiveMembers<TProgram>(
            ImmutableArray<ObjectUnit<TProgram>> mandatoryObjects,
            ImmutableArray<ObjectUnit<TProgram>> archiveMembers,
            Func<TProgram, ImmutableHashSet<string>> getDefinitions,
            Func<TProgram, ImmutableHashSet<string>> getUndefinedReferences)
            where TProgram : class
        {
            var selected = mandatoryObjects.ToBuilder();
            var included = new bool[archiveMembers.Length];
            var changed = true;
            while (changed)
            {
                changed = false;
                var unresolved = GetUndefinedReferences(selected, getDefinitions, getUndefinedReferences);
                if (unresolved.Count == 0)
                    break;

                for (var i = 0; i < archiveMembers.Length; i++)
                {
                    if (included[i] || !getDefinitions(archiveMembers[i].Object).Overlaps(unresolved))
                        continue;

                    included[i] = true;
                    selected.Add(archiveMembers[i]);
                    changed = true;
                    unresolved = GetUndefinedReferences(selected, getDefinitions, getUndefinedReferences);
                }
            }
            return selected.ToImmutable();
        }

        private static ImmutableHashSet<string> GetUndefinedReferences<TProgram>(
            IEnumerable<ObjectUnit<TProgram>> objects,
            Func<TProgram, ImmutableHashSet<string>> getDefinitions,
            Func<TProgram, ImmutableHashSet<string>> getUndefinedReferences)
            where TProgram : class
        {
            var definitions = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            var references = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            foreach (var item in objects)
            {
                definitions.UnionWith(getDefinitions(item.Object));
                references.UnionWith(getUndefinedReferences(item.Object));
            }
            references.ExceptWith(definitions);
            return references.ToImmutable();
        }

        private static ImmutableHashSet<string> GetX86Definitions(X86Program program)
            => program.Symbols
                .Where(static symbol => symbol.Binding == X86ObjectSymbolBinding.Global && symbol.Kind != X86ObjectSymbolKind.Section)
                .Select(static symbol => symbol.Name)
                .ToImmutableHashSet(StringComparer.Ordinal);

        private static ImmutableHashSet<string> GetX86UndefinedReferences(X86Program program)
        {
            var localDefinitions = program.Symbols
                .Where(static symbol => symbol.Binding != X86ObjectSymbolBinding.External)
                .Select(static symbol => symbol.Name)
                .Concat(program.Text.Labels.Keys)
                .ToImmutableHashSet(StringComparer.Ordinal);
            var references = program.Text.Instructions.SelectMany(static instruction => GetX86InstructionReferences(instruction));
            references = references.Concat(program.Text.Relocations.Select(static relocation => relocation.SymbolName));
            references = references.Concat(program.DataSections.SelectMany(static section => section.Relocations).Select(static relocation => relocation.SymbolName));
            return references.Where(reference => !localDefinitions.Contains(reference)).ToImmutableHashSet(StringComparer.Ordinal);
        }

        private static IEnumerable<string> GetX86InstructionReferences(X86Instruction instruction)
        {
            if (instruction.Operand0.Symbol is { Length: > 0 } operand0)
                yield return operand0;
            if (instruction.Operand1.Symbol is { Length: > 0 } operand1)
                yield return operand1;
            if (instruction.Operand2.Symbol is { Length: > 0 } operand2)
                yield return operand2;
        }

        private static ImmutableHashSet<string> GetRiscVDefinitions(RiscVProgram program)
            => program.Symbols
                .Where(static symbol => symbol.Binding == RVObjectSymbolBinding.Global && symbol.Kind != RVObjectSymbolKind.Section)
                .Select(static symbol => symbol.Name)
                .ToImmutableHashSet(StringComparer.Ordinal);

        private static ImmutableHashSet<string> GetRiscVUndefinedReferences(RiscVProgram program)
        {
            var localDefinitions = program.Symbols
                .Where(static symbol => symbol.Binding != RVObjectSymbolBinding.External)
                .Select(static symbol => symbol.Name)
                .Concat(program.Text.Labels.Keys)
                .ToImmutableHashSet(StringComparer.Ordinal);
            var references = program.Text.Instructions
                .Where(static instruction => instruction.Symbol is not null)
                .Select(static instruction => instruction.Symbol!);
            references = references.Concat(program.Text.Relocations.Select(static relocation => relocation.SymbolName));
            references = references.Concat(program.DataSections.SelectMany(static section => section.Relocations).Select(static relocation => relocation.SymbolName));
            return references.Where(reference => !localDefinitions.Contains(reference)).ToImmutableHashSet(StringComparer.Ordinal);
        }

        private static ImmutableHashSet<string> GetArmDefinitions(ArmProgram program)
            => program.Symbols
                .Where(static symbol => symbol.Binding == ArmObjectSymbolBinding.Global && symbol.Kind != ArmObjectSymbolKind.Section)
                .Select(static symbol => symbol.Name)
                .ToImmutableHashSet(StringComparer.Ordinal);

        private static ImmutableHashSet<string> GetArmUndefinedReferences(ArmProgram program)
        {
            var localDefinitions = program.Symbols
                .Where(static symbol => symbol.Binding != ArmObjectSymbolBinding.External)
                .Select(static symbol => symbol.Name)
                .Concat(program.Text.Labels.Keys)
                .ToImmutableHashSet(StringComparer.Ordinal);
            var references = program.Text.Instructions.SelectMany(static instruction => GetArmInstructionReferences(instruction));
            references = references.Concat(program.Text.Relocations.Select(static relocation => relocation.SymbolName));
            references = references.Concat(program.DataSections.SelectMany(static section => section.Relocations).Select(static relocation => relocation.SymbolName));
            return references.Where(reference => !localDefinitions.Contains(reference)).ToImmutableHashSet(StringComparer.Ordinal);
        }

        private static IEnumerable<string> GetArmInstructionReferences(ArmInstruction instruction)
        {
            if (instruction.Operand0.Symbol is { Length: > 0 } operand0)
                yield return operand0;
            if (instruction.Operand1.Symbol is { Length: > 0 } operand1)
                yield return operand1;
            if (instruction.Operand2.Symbol is { Length: > 0 } operand2)
                yield return operand2;
            if (instruction.Operand3.Symbol is { Length: > 0 } operand3)
                yield return operand3;
        }

        private static void AddUndefinedDiagnostics(
            ImmutableHashSet<string> undefinedSymbols,
            StaticLinkerOptions options,
            ImmutableArray<LinkerDiagnostic>.Builder diagnostics)
        {
            if (options.AllowUndefinedSymbols)
                return;

            foreach (var symbol in undefinedSymbols.OrderBy(static name => name, StringComparer.Ordinal))
            {
                if (options.AllowedUndefinedSymbols.Contains(symbol))
                    continue;
                if (options.CompilationOptions.Target.OperatingSystem == OperatingSystemKind.Windows &&
                    symbol.StartsWith("__imp_", StringComparison.Ordinal))
                {
                    continue;
                }
                diagnostics.Add(LinkerError($"Undefined symbol: {symbol}"));
            }
        }

        private static void ValidateDeclarations(
            ImmutableArray<CompiledUnit> units,
            ImmutableArray<LinkerDiagnostic>.Builder diagnostics)
        {
            var externalDeclarations = new Dictionary<string, List<LinkDeclaration>>(StringComparer.Ordinal);
            for (var unitIndex = 0; unitIndex < units.Length; unitIndex++)
            {
                var unit = units[unitIndex];
                var localDeclarations = new Dictionary<string, List<LinkDeclaration>>(StringComparer.Ordinal);
                foreach (var declaration in unit.Linkage.Declarations)
                {
                    var current = new LinkDeclaration(
                        declaration.Symbol.Name,
                        declaration.Symbol.Kind,
                        ((TypedSymbol)declaration.Symbol).Type,
                        unit.Source.FilePath,
                        declaration.Position,
                        unitIndex);

                    ValidateDeclarationAgainst(current, localDeclarations, diagnostics);
                    AddDeclaration(current, localDeclarations);

                    if (declaration.IsInternal)
                        continue;

                    if (externalDeclarations.TryGetValue(current.Name, out var previousDeclarations))
                    {
                        foreach (var previous in previousDeclarations)
                        {
                            if (previous.UnitIndex == unitIndex)
                                continue;
                            if (!AreCompatibleDeclarations(previous, current, diagnostics))
                                break;
                        }
                    }
                    AddDeclaration(current, externalDeclarations);
                }
            }
        }

        private static void ValidateDeclarationAgainst(
            LinkDeclaration declaration,
            Dictionary<string, List<LinkDeclaration>> declarations,
            ImmutableArray<LinkerDiagnostic>.Builder diagnostics)
        {
            if (!declarations.TryGetValue(declaration.Name, out var previousDeclarations))
                return;

            foreach (var previous in previousDeclarations)
            {
                if (!AreCompatibleDeclarations(previous, declaration, diagnostics))
                    break;
            }
        }

        private static bool AreCompatibleDeclarations(
            LinkDeclaration previous,
            LinkDeclaration declaration,
            ImmutableArray<LinkerDiagnostic>.Builder diagnostics)
        {
            if (previous.SymbolKind != declaration.SymbolKind)
            {
                diagnostics.Add(new LinkerDiagnostic(
                    DiagnosticSeverity.Error,
                    LinkerStage.Linking,
                    $"Symbol '{declaration.Name}' is declared as both an object and a function.",
                    declaration.FilePath,
                    declaration.Position));
                return false;
            }

            if (new TypeCompatibilityComparer().AreCompatible(previous.Type, declaration.Type))
                return true;

            diagnostics.Add(new LinkerDiagnostic(
                DiagnosticSeverity.Error,
                LinkerStage.Linking,
                $"Incompatible declarations of symbol '{declaration.Name}': '" +
                $"{previous.Type.ToDisplayString()}' and '{declaration.Type.ToDisplayString()}'.",
                declaration.FilePath,
                declaration.Position));
            return false;
        }

        private static void AddDeclaration(
            LinkDeclaration declaration,
            Dictionary<string, List<LinkDeclaration>> declarations)
        {
            if (!declarations.TryGetValue(declaration.Name, out var list))
            {
                list = new List<LinkDeclaration>();
                declarations.Add(declaration.Name, list);
            }
            list.Add(declaration);
        }

        private sealed class TypeCompatibilityComparer
        {
            private readonly HashSet<TypePair> _activePairs = new HashSet<TypePair>();

            public bool AreCompatible(
                QualifiedType left,
                QualifiedType right,
                bool ignoreTopLevelQualifiers = false)
            {
                if (!ignoreTopLevelQualifiers && left.Qualifiers != right.Qualifiers)
                    return false;
                if (ReferenceEquals(left.Type, right.Type))
                    return true;
                if (left.Type.Kind != right.Type.Kind)
                    return false;

                var pair = new TypePair(left.Type, right.Type);
                if (!_activePairs.Add(pair))
                    return true;

                try
                {
                    return (left.Type, right.Type) switch
                    {
                        (CErrorType, CErrorType) => true,
                        (BuiltinType l, BuiltinType r) => l.BuiltinKind == r.BuiltinKind,
                        (RVVectorType l, RVVectorType r) => l.VectorKind == r.VectorKind,
                        (PointerType l, PointerType r) => AreCompatible(l.PointeeType, r.PointeeType),
                        (ArrayType l, ArrayType r) =>
                            AreCompatible(l.ElementType, r.ElementType) &&
                            (!l.Length.HasValue || !r.Length.HasValue || l.Length.Value == r.Length.Value),
                        (FunctionType l, FunctionType r) => AreCompatibleFunctions(l, r),
                        (TagType l, TagType r) => AreCompatibleTags(l.Symbol, r.Symbol),
                        (EnumType l, EnumType r) => AreCompatibleEnums(l.Symbol, r.Symbol),
                        _ => string.Equals(left.ToDisplayString(), right.ToDisplayString(), StringComparison.Ordinal),
                    };
                }
                finally
                {
                    _activePairs.Remove(pair);
                }
            }

            private bool AreCompatibleFunctions(FunctionType left, FunctionType right)
            {
                if (!AreCompatible(left.ReturnType, right.ReturnType))
                    return false;
                if (!left.HasPrototype && !right.HasPrototype)
                    return true;
                if (!left.HasPrototype || !right.HasPrototype)
                    return IsCompatibleWithUnprototyped(left.HasPrototype ? left : right);
                if (left.IsVariadic != right.IsVariadic || left.Parameters.Length != right.Parameters.Length)
                    return false;

                for (var i = 0; i < left.Parameters.Length; i++)
                {
                    if (!AreCompatible(left.Parameters[i].Type, right.Parameters[i].Type, ignoreTopLevelQualifiers: true))
                        return false;
                }
                return true;
            }

            private bool IsCompatibleWithUnprototyped(FunctionType prototype)
            {
                if (prototype.IsVariadic)
                    return false;

                foreach (var parameter in prototype.Parameters)
                {
                    var parameterType = parameter.Type;
                    if (!AreCompatible(parameterType, ApplyDefaultArgumentPromotions(parameterType), ignoreTopLevelQualifiers: true))
                        return false;
                }
                return true;
            }

            private static QualifiedType ApplyDefaultArgumentPromotions(QualifiedType type)
            {
                if (type.Type is BuiltinType builtin)
                {
                    return builtin.BuiltinKind switch
                    {
                        BuiltinTypeKind.Float => new QualifiedType(TypeCatalog.Instance.Double),
                        BuiltinTypeKind.Bool or
                        BuiltinTypeKind.Char or
                        BuiltinTypeKind.SignedChar or
                        BuiltinTypeKind.UnsignedChar or
                        BuiltinTypeKind.Short or
                        BuiltinTypeKind.UnsignedShort => new QualifiedType(TypeCatalog.Instance.Int),
                        _ => new QualifiedType(type.Type),
                    };
                }

                return new QualifiedType(type.Type);
            }

            private bool AreCompatibleTags(TagSymbol left, TagSymbol right)
            {
                if (left.TagKind != right.TagKind)
                    return false;
                if (!AreCompatibleTagNames(left.Name, right.Name))
                    return false;
                if (!left.IsComplete || !right.IsComplete)
                    return true;
                if (left.Fields.Length != right.Fields.Length)
                    return false;

                for (var i = 0; i < left.Fields.Length; i++)
                {
                    var leftField = left.Fields[i];
                    var rightField = right.Fields[i];
                    if (!string.Equals(leftField.Name, rightField.Name, StringComparison.Ordinal) ||
                        !AreCompatible(leftField.Type, rightField.Type))
                    {
                        return false;
                    }
                }
                return true;
            }

            private static bool AreCompatibleEnums(TagSymbol left, TagSymbol right)
                => left.TagKind == TagKind.Enum &&
                   right.TagKind == TagKind.Enum &&
                   AreCompatibleTagNames(left.Name, right.Name);

            private static bool AreCompatibleTagNames(string left, string right)
            {
                var leftAnonymous = left.StartsWith("<anonymous", StringComparison.Ordinal);
                var rightAnonymous = right.StartsWith("<anonymous", StringComparison.Ordinal);
                if (leftAnonymous || rightAnonymous)
                    return leftAnonymous && rightAnonymous;
                return string.Equals(left, right, StringComparison.Ordinal);
            }
        }

        private readonly struct TypePair : IEquatable<TypePair>
        {
            private readonly CType _left;
            private readonly CType _right;

            public TypePair(CType left, CType right)
            {
                _left = left;
                _right = right;
            }

            public bool Equals(TypePair other)
                => ReferenceEquals(_left, other._left) && ReferenceEquals(_right, other._right);

            public override bool Equals(object? obj)
                => obj is TypePair other && Equals(other);

            public override int GetHashCode()
                => HashCode.Combine(RuntimeHelpers.GetHashCode(_left), RuntimeHelpers.GetHashCode(_right));
        }

        private static bool HasErrors(ImmutableArray<LinkerDiagnostic>.Builder diagnostics)
            => diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        private static LinkerDiagnostic LinkerError(string message)
            => new LinkerDiagnostic(DiagnosticSeverity.Error, LinkerStage.Linking, message);

        private static LinkerDiagnostic CodeGenerationError(string filePath, Exception exception)
            => new LinkerDiagnostic(
                DiagnosticSeverity.Error,
                LinkerStage.CodeGeneration,
                exception.Message,
                filePath);

        private static StaticLinkResult<TProgram> Failure<TProgram>(
            ImmutableArray<LinkerDiagnostic>.Builder diagnostics)
            where TProgram : class
            => new StaticLinkResult<TProgram>(null, diagnostics.ToImmutable(), ImmutableArray<string>.Empty);

        private static StaticLinkResult<TProgram> Success<TProgram>(
            TProgram program,
            ImmutableArray<CompiledUnit> selected,
            ImmutableArray<LinkerDiagnostic>.Builder diagnostics)
            where TProgram : class
            => new StaticLinkResult<TProgram>(
                program,
                diagnostics.ToImmutable(),
                selected.Select(static unit => unit.Source.FilePath).ToImmutableArray());

        private static StaticLinkResult<TProgram> Success<TProgram>(
            TProgram program,
            ImmutableArray<ObjectUnit<TProgram>> selected,
            ImmutableArray<LinkerDiagnostic>.Builder diagnostics)
            where TProgram : class
            => new StaticLinkResult<TProgram>(
                program,
                diagnostics.ToImmutable(),
                selected.Select(static item => item.FilePath).ToImmutableArray());

        private sealed class CompiledUnit
        {
            public SourceFile Source { get; }
            public SemanticModel SemanticModel { get; }
            public FileScopeLinkageMap Linkage { get; }
            public LirModule Lir { get; }

            public CompiledUnit(
                SourceFile source,
                SemanticModel semanticModel,
                FileScopeLinkageMap linkage,
                LirModule lir)
            {
                Source = source;
                SemanticModel = semanticModel;
                Linkage = linkage;
                Lir = lir;
            }
        }

        private readonly struct CompiledInputs
        {
            public ImmutableArray<CompiledUnit> UserUnits { get; }
            public ImmutableArray<CompiledUnit> LibraryUnits { get; }

            public CompiledInputs(
                ImmutableArray<CompiledUnit> userUnits,
                ImmutableArray<CompiledUnit> libraryUnits)
            {
                UserUnits = userUnits;
                LibraryUnits = libraryUnits;
            }
        }

        private readonly struct ObjectUnit<TProgram>
            where TProgram : class
        {
            public CompiledUnit Unit { get; }
            public string FilePath => Unit.Source.FilePath;
            public TProgram Object { get; }

            public ObjectUnit(CompiledUnit unit, TProgram @object)
            {
                Unit = unit;
                Object = @object;
            }
        }

        private readonly struct LinkDeclaration
        {
            public string Name { get; }
            public SymbolKind SymbolKind { get; }
            public QualifiedType Type { get; }
            public string FilePath { get; }
            public TextSpan Position { get; }
            public int UnitIndex { get; }

            public LinkDeclaration(
                string name,
                SymbolKind symbolKind,
                QualifiedType type,
                string filePath,
                TextSpan position,
                int unitIndex)
            {
                Name = name;
                SymbolKind = symbolKind;
                Type = type;
                FilePath = filePath;
                Position = position;
                UnitIndex = unitIndex;
            }
        }
    }

    internal readonly struct FileScopeSymbolDeclaration
    {
        public Symbol Symbol { get; }
        public bool IsInternal { get; }
        public TextSpan Position { get; }

        public FileScopeSymbolDeclaration(Symbol symbol, bool isInternal, TextSpan position)
        {
            Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
            IsInternal = isInternal;
            Position = position;
        }
    }

    internal sealed class FileScopeLinkageMap
    {
        private readonly Dictionary<Symbol, bool> _internalBySymbol;

        public ImmutableArray<FileScopeSymbolDeclaration> Declarations { get; }

        private FileScopeLinkageMap(
            Dictionary<Symbol, bool> internalBySymbol,
            ImmutableArray<FileScopeSymbolDeclaration> declarations)
        {
            _internalBySymbol = internalBySymbol;
            Declarations = declarations;
        }

        public static FileScopeLinkageMap Create(SemanticModel semanticModel)
        {
            if (semanticModel is null)
                throw new ArgumentNullException(nameof(semanticModel));

            var states = new Dictionary<string, LinkageState>(StringComparer.Ordinal);
            var internalBySymbol = new Dictionary<Symbol, bool>();
            var declarations = ImmutableArray.CreateBuilder<FileScopeSymbolDeclaration>();

            foreach (var member in semanticModel.Root.Members)
            {
                if (member is FunctionDefinitionSyntax functionDefinition)
                {
                    if (semanticModel.GetDeclaredSymbol(functionDefinition) is FunctionSymbol function)
                    {
                        AddDeclaration(
                            function,
                            function.StorageClass,
                            functionDefinition.Declarator.Identifier?.Span ?? default,
                            isDefinition: true,
                            states,
                            internalBySymbol,
                            declarations);
                    }
                    continue;
                }

                if (member is not DeclarationSyntax declaration)
                    continue;

                foreach (var initDeclarator in declaration.Declarators)
                {
                    var symbol = semanticModel.GetDeclaredSymbol(initDeclarator);
                    switch (symbol)
                    {
                        case FunctionSymbol function:
                            AddDeclaration(
                                function,
                                function.StorageClass,
                                initDeclarator.Declarator.Identifier?.Span ?? default,
                                isDefinition: false,
                                states,
                                internalBySymbol,
                                declarations);
                            break;
                        case VariableSymbol variable:
                            AddDeclaration(
                                variable,
                                variable.StorageClass,
                                initDeclarator.Declarator.Identifier?.Span ?? default,
                                isDefinition: initDeclarator.Initializer is not null,
                                states,
                                internalBySymbol,
                                declarations);
                            break;
                    }
                }
            }

            return new FileScopeLinkageMap(internalBySymbol, declarations.ToImmutable());
        }

        public bool IsInternal(Symbol symbol)
        {
            if (symbol is null)
                throw new ArgumentNullException(nameof(symbol));

            if (_internalBySymbol.TryGetValue(symbol, out var isInternal))
                return isInternal;

            return symbol switch
            {
                FunctionSymbol function => function.StorageClass == StorageClass.Static,
                VariableSymbol variable => variable.StorageClass == StorageClass.Static,
                _ => false,
            };
        }

        private static void AddDeclaration(
            Symbol symbol,
            StorageClass storageClass,
            TextSpan position,
            bool isDefinition,
            Dictionary<string, LinkageState> states,
            Dictionary<Symbol, bool> internalBySymbol,
            ImmutableArray<FileScopeSymbolDeclaration>.Builder declarations)
        {
            if (!states.TryGetValue(symbol.Name, out var state))
            {
                state = new LinkageState(symbol.Kind, storageClass == StorageClass.Static);
                states.Add(symbol.Name, state);
            }
            else
            {
                if (state.SymbolKind != symbol.Kind)
                    throw new InvalidOperationException($"Symbol '{symbol.Name}' is declared as both an object and a function.");

                var inheritsPriorLinkage = storageClass == StorageClass.Extern ||
                    symbol.Kind == SymbolKind.Function && storageClass == StorageClass.None;
                if (!inheritsPriorLinkage)
                {
                    var declarationIsInternal = storageClass == StorageClass.Static;
                    if (declarationIsInternal != state.IsInternal)
                        throw new InvalidOperationException($"Conflicting linkage for symbol '{symbol.Name}'.");
                }
            }

            if (isDefinition)
            {
                if (state.HasDefinition)
                    throw new InvalidOperationException($"Duplicate definition of symbol '{symbol.Name}'.");
                state.HasDefinition = true;
            }

            internalBySymbol[symbol] = state.IsInternal;
            declarations.Add(new FileScopeSymbolDeclaration(symbol, state.IsInternal, position));
        }

        private sealed class LinkageState
        {
            public SymbolKind SymbolKind { get; }
            public bool IsInternal { get; }
            public bool HasDefinition { get; set; }

            public LinkageState(SymbolKind symbolKind, bool isInternal)
            {
                SymbolKind = symbolKind;
                IsInternal = isInternal;
            }
        }
    }
}
