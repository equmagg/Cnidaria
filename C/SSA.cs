using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Cnidaria.C
{
    public sealed class SsaOptions
    {
        public static SsaOptions Default { get; } = new SsaOptions();

        public bool TrackMemory { get; }
        public bool PromoteTemporaries { get; }
        public bool PromoteAddressTakenVariables { get; }
        public bool PromoteAggregateVariables { get; }
        public bool PromoteVolatileVariables { get; }

        public SsaOptions(
            bool trackMemory = true,
            bool promoteTemporaries = true,
            bool promoteAddressTakenVariables = false,
            bool promoteAggregateVariables = false,
            bool promoteVolatileVariables = false)
        {
            TrackMemory = trackMemory;
            PromoteTemporaries = promoteTemporaries;
            PromoteAddressTakenVariables = promoteAddressTakenVariables;
            PromoteAggregateVariables = promoteAggregateVariables;
            PromoteVolatileVariables = promoteVolatileVariables;
        }
    }

    public sealed class SsaOptimizationOptions
    {
        public static SsaOptimizationOptions Default { get; } = new SsaOptimizationOptions();

        public bool EnableConstantFolding { get; }
        public bool EnableCopyPropagation { get; }
        public bool EnableBranchFolding { get; }
        public bool EnableDeadCodeElimination { get; }
        public bool EnableCommonSubexpressionElimination { get; }
        public bool EnableLoopInvariantCodeMotion { get; }
        public int MaxLoopHoistsPerLoop { get; }
        public int MaxLoopAnalysisWork { get; }
        public int MaxIterations { get; }

        public SsaOptimizationOptions(
            bool enableConstantFolding = true,
            bool enableCopyPropagation = true,
            bool enableBranchFolding = true,
            bool enableDeadCodeElimination = true,
            int maxIterations = 3,
            bool enableCommonSubexpressionElimination = true,
            bool enableLoopInvariantCodeMotion = true,
            int maxLoopHoistsPerLoop = 16,
            int maxLoopAnalysisWork = 100_000)
        {
            EnableConstantFolding = enableConstantFolding;
            EnableCopyPropagation = enableCopyPropagation;
            EnableBranchFolding = enableBranchFolding;
            EnableDeadCodeElimination = enableDeadCodeElimination;
            EnableCommonSubexpressionElimination = enableCommonSubexpressionElimination;
            EnableLoopInvariantCodeMotion = enableLoopInvariantCodeMotion;
            MaxLoopHoistsPerLoop = Math.Max(0, maxLoopHoistsPerLoop);
            MaxLoopAnalysisWork = Math.Max(0, maxLoopAnalysisWork);
            MaxIterations = maxIterations < 1 ? 1 : maxIterations;
        }
    }

    public enum GimpleVariableKind : byte
    {
        Symbol,
        Temporary,
        Memory,
    }

    public enum GimpleDefinitionKind : byte
    {
        Undefined,
        Entry,
        Phi,
        Statement,
        MemoryStatement,
    }

    public enum GimpleUseKind : byte
    {
        Value,
        Address,
        Memory,
        Phi,
    }

    public enum GimpleOperandRole : byte
    {
        Value,
        Address,
    }

    [Flags]
    public enum GimpleStatementFlags : byte
    {
        None = 0,
        ReadsMemory = 1,
        WritesMemory = 2,
        ContainsCall = 4,
    }

    public enum GimpleProblemKind : byte
    {
        ControlFlowProblem,
        MissingPhiInput,
        InvalidStatement,
        InvalidOperand,
        InvalidPhi,
    }

    public sealed class GimplePipelineResult
    {
        public ControlFlowGraph ControlFlowGraph { get; }
        public SemanticModel SemanticModel => ControlFlowGraph.SemanticModel;
        public GimpleTree InputTree => ControlFlowGraph.GimpleTree;
        public ImmutableArray<GimpleFunctionAnnotations> Functions { get; }
        public ImmutableArray<GimpleProblem> Problems { get; }

        internal GimplePipelineResult(
            ControlFlowGraph controlFlowGraph,
            ImmutableArray<GimpleFunctionAnnotations> functions,
            ImmutableArray<GimpleProblem> verificationProblems = default)
        {
            ControlFlowGraph = controlFlowGraph ?? throw new ArgumentNullException(nameof(controlFlowGraph));
            Functions = functions.IsDefault ? ImmutableArray<GimpleFunctionAnnotations>.Empty : functions;

            var problems = ImmutableArray.CreateBuilder<GimpleProblem>();
            foreach (var problem in controlFlowGraph.Problems)
                problems.Add(GimpleProblem.FromControlFlowProblem(problem));
            foreach (var function in Functions)
                problems.AddRange(function.Problems);
            if (!verificationProblems.IsDefault)
                problems.AddRange(verificationProblems);
            Problems = problems.ToImmutable();
        }

        public static GimplePipelineResult Build(
            ControlFlowGraph controlFlowGraph,
            SsaOptions? options = null,
            ValueNumberingOptions? valueNumberingOptions = null)
        {
            if (controlFlowGraph is null)
                throw new ArgumentNullException(nameof(controlFlowGraph));

            options ??= SsaOptions.Default;
            valueNumberingOptions ??= ValueNumberingOptions.Default;
            var functions = ImmutableArray.CreateBuilder<GimpleFunctionAnnotations>(controlFlowGraph.Functions.Length);
            foreach (var function in controlFlowGraph.Functions)
                functions.Add(GimpleAnnotationBuilder.Build(function, options, valueNumberingOptions, controlFlowGraph.SemanticModel.Compilation.Options.Target));

            return new GimplePipelineResult(controlFlowGraph, functions.ToImmutable());
        }

        public GimplePipelineResult Optimize(
            SsaOptimizationOptions? optimizationOptions = null,
            ValueNumberingOptions? valueNumberingOptions = null)
        {
            optimizationOptions ??= SsaOptimizationOptions.Default;
            valueNumberingOptions ??= ValueNumberingOptions.Default;

            var target = SemanticModel.Compilation.Options.Target;
            var functions = ImmutableArray.CreateBuilder<GimpleFunctionAnnotations>(Functions.Length);
            foreach (var function in Functions)
                functions.Add(SsaOptimizer.Optimize(function, target, optimizationOptions, valueNumberingOptions));

            return new GimplePipelineResult(ControlFlowGraph, functions.ToImmutable());
        }

        public GimplePipelineResult Trim(TrimmingOptions? options = null)
        {
            options ??= SemanticModel.Compilation.Options.Trimming;
            var trimResult = Trimmer.Trim(ControlFlowGraph, Functions, options);
            if (ReferenceEquals(trimResult.ControlFlowGraph, ControlFlowGraph) &&
                trimResult.Functions.Equals(Functions))
            {
                return this;
            }

            return new GimplePipelineResult(trimResult.ControlFlowGraph, trimResult.Functions);
        }

        /// <summary>Checks the GIMPLE invariants and reports every violation as a problem</summary>
        public GimplePipelineResult Verify(GimpleVerificationLevel level = GimpleVerificationLevel.Full)
        {
            if (level == GimpleVerificationLevel.None)
                return this;

            var problems = ImmutableArray.CreateBuilder<GimpleProblem>();
            foreach (var function in Functions)
                problems.AddRange(GimpleVerifier.Verify(function, level));

            return problems.Count == 0
                ? this
                : new GimplePipelineResult(ControlFlowGraph, Functions, problems.ToImmutable());
        }
    }

    public sealed class GimpleFunctionAnnotations
    {
        private readonly Dictionary<ControlFlowBlock, GimpleBlockAnnotations> _blocksByControlFlowBlock;
        private readonly Dictionary<GimpleVariable, GimpleName> _undefinedNames;
        private readonly Dictionary<GimpleName, GimpleDefinition> _definitionsByName;
        private readonly Dictionary<GimpleName, ImmutableArray<GimpleUse>> _immediateUsesByName;

        public ControlFlowFunction ControlFlowFunction { get; }
        public GimpleFunctionDefinition InputFunction => ControlFlowFunction.Function;
        public FunctionSymbol? Symbol => ControlFlowFunction.Symbol;
        public GimpleVariable? MemoryVariable { get; }
        public ImmutableArray<GimpleVariable> Variables { get; }
        public ImmutableArray<GimpleBlockAnnotations> Blocks { get; }
        public ImmutableArray<GimpleDefinition> Definitions { get; }
        public ImmutableArray<GimpleUse> Uses { get; }
        public ImmutableArray<GimpleProblem> Problems { get; }
        public GimpleValueNumbering ValueNumbering { get; }
        internal TargetInfo Target { get; }

        internal GimpleFunctionAnnotations(
            ControlFlowFunction controlFlowFunction,
            GimpleVariable? memoryVariable,
            ImmutableArray<GimpleVariable> variables,
            ImmutableArray<GimpleBlockAnnotations> blocks,
            ImmutableArray<GimpleDefinition> definitions,
            ImmutableArray<GimpleUse> uses,
            ImmutableArray<GimpleProblem> problems,
            Dictionary<GimpleVariable, GimpleName> undefinedNames,
            ValueNumberingOptions valueNumberingOptions,
            TargetInfo target)
        {
            ControlFlowFunction = controlFlowFunction ?? throw new ArgumentNullException(nameof(controlFlowFunction));
            Target = target;
            MemoryVariable = memoryVariable;
            Variables = variables.IsDefault ? ImmutableArray<GimpleVariable>.Empty : variables;
            Blocks = blocks.IsDefault ? ImmutableArray<GimpleBlockAnnotations>.Empty : blocks;
            Definitions = definitions.IsDefault ? ImmutableArray<GimpleDefinition>.Empty : definitions;
            Uses = uses.IsDefault ? ImmutableArray<GimpleUse>.Empty : uses;
            Problems = problems.IsDefault ? ImmutableArray<GimpleProblem>.Empty : problems;
            _undefinedNames = undefinedNames is null
                ? new Dictionary<GimpleVariable, GimpleName>()
                : new Dictionary<GimpleVariable, GimpleName>(undefinedNames);
            _blocksByControlFlowBlock = Blocks.ToDictionary(static block => block.ControlFlowBlock);
            _definitionsByName = Definitions.ToDictionary(static definition => definition.Name);
            _immediateUsesByName = BuildImmediateUses(Uses, Blocks);
            ValueNumbering = GimpleValueNumbering.Build(this, valueNumberingOptions);
        }

        public bool TryGetBlock(ControlFlowBlock controlFlowBlock, out GimpleBlockAnnotations? block)
        {
            if (controlFlowBlock is null)
                throw new ArgumentNullException(nameof(controlFlowBlock));

            return _blocksByControlFlowBlock.TryGetValue(controlFlowBlock, out block);
        }

        public bool TryGetDefinition(GimpleName name, out GimpleDefinition? definition)
        {
            if (name is null)
                throw new ArgumentNullException(nameof(name));

            return _definitionsByName.TryGetValue(name, out definition);
        }

        public GimpleName GetUndefinedName(GimpleVariable variable)
        {
            if (variable is null)
                throw new ArgumentNullException(nameof(variable));

            return _undefinedNames[variable];
        }

        public ImmutableArray<GimpleUse> GetImmediateUses(GimpleName name)
        {
            if (name is null)
                throw new ArgumentNullException(nameof(name));

            return _immediateUsesByName.TryGetValue(name, out var uses)
                ? uses
                : ImmutableArray<GimpleUse>.Empty;
        }

        public bool HasZeroUses(GimpleName name)
            => GetImmediateUses(name).Length == 0;

        public bool HasSingleUse(GimpleName name)
            => GetImmediateUses(name).Length == 1;

        public bool TryGetSingleUse(GimpleName name, out GimpleUse? use)
        {
            var uses = GetImmediateUses(name);
            if (uses.Length == 1)
            {
                use = uses[0];
                return true;
            }

            use = null;
            return false;
        }

        private static Dictionary<GimpleName, ImmutableArray<GimpleUse>> BuildImmediateUses(
            ImmutableArray<GimpleUse> uses,
            ImmutableArray<GimpleBlockAnnotations> blocks)
        {
            var builders = new Dictionary<GimpleName, ImmutableArray<GimpleUse>.Builder>();
            foreach (var use in uses)
                AddImmediateUse(builders, use);

            foreach (var block in blocks)
            {
                foreach (var phi in block.Phis)
                {
                    foreach (var operand in phi.Operands)
                    {
                        AddImmediateUse(
                            builders,
                            new GimpleUse(operand.Value, GimpleUseKind.Phi, phi.Block, phi, operand.Value, operand.Edge));
                    }
                }
            }

            var result = new Dictionary<GimpleName, ImmutableArray<GimpleUse>>(builders.Count);
            foreach (var pair in builders)
                result.Add(pair.Key, pair.Value.ToImmutable());
            return result;
        }

        private static void AddImmediateUse(
            Dictionary<GimpleName, ImmutableArray<GimpleUse>.Builder> builders,
            GimpleUse use)
        {
            if (!builders.TryGetValue(use.Name, out var builder))
            {
                builder = ImmutableArray.CreateBuilder<GimpleUse>();
                builders.Add(use.Name, builder);
            }

            builder.Add(use);
        }

        public override string ToString()
            => Symbol?.Name ?? "<anonymous-function>";
    }

    public sealed class GimpleVariable
    {
        public int Ordinal { get; }
        public GimpleVariableKind Kind { get; }
        public Symbol? Symbol { get; }
        public GimpleTemporaryValue? Temporary { get; }
        public QualifiedType Type { get; }
        public string Name { get; }

        internal GimpleVariable(
            int ordinal,
            GimpleVariableKind kind,
            Symbol? symbol,
            GimpleTemporaryValue? temporary,
            QualifiedType type,
            string name)
        {
            if (ordinal < 0)
                throw new ArgumentOutOfRangeException(nameof(ordinal));

            Ordinal = ordinal;
            Kind = kind;
            Symbol = symbol;
            Temporary = temporary;
            Type = GimpleTypeHelpers.Normalize(type);
            Name = string.IsNullOrWhiteSpace(name) ? $"v{ordinal.ToString(CultureInfo.InvariantCulture)}" : name;
        }

        public override string ToString() => Name;
    }

    public sealed class GimpleName : GimplePlace
    {
        private GimpleDefinition? _definition;

        public override GimpleNodeKind Kind => GimpleNodeKind.GimpleName;
        public GimpleVariable Variable { get; }
        public int Version { get; }
        public bool IsUndefined { get; }
        public GimpleDefinition? Definition => _definition;
        public bool IsDefaultDefinition => _definition?.Kind is GimpleDefinitionKind.Undefined or GimpleDefinitionKind.Entry;

        internal GimpleName(GimpleVariable variable, int version, bool isUndefined)
            : base(GetSyntax(variable), variable?.Type ?? default)
        {
            if (version < 0)
                throw new ArgumentOutOfRangeException(nameof(version));

            Variable = variable ?? throw new ArgumentNullException(nameof(variable));
            Version = version;
            IsUndefined = isUndefined;
        }

        internal void BindDefinition(GimpleDefinition definition)
            => _definition = definition ?? throw new ArgumentNullException(nameof(definition));

        private static SyntaxNode? GetSyntax(GimpleVariable? variable)
            => variable?.Temporary?.Syntax ?? (variable?.Symbol as TypedSymbol)?.DeclaringSyntax;

        public override string ToString()
            => IsUndefined
                ? $"{Variable.Name}_undef"
                : $"{Variable.Name}_{Version.ToString(CultureInfo.InvariantCulture)}";
    }

    public sealed class GimpleDefinition
    {
        public GimpleName Name { get; }
        public GimpleDefinitionKind Kind { get; }
        public ControlFlowBlock? Block { get; }
        public GimpleStatement? Statement { get; private set; }
        public GimplePlace? Target { get; private set; }
        public ParameterSymbol? Parameter { get; }

        internal GimpleDefinition(
            GimpleName name,
            GimpleDefinitionKind kind,
            ControlFlowBlock? block,
            GimpleStatement? statement,
            GimplePlace? target,
            ParameterSymbol? parameter)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Kind = kind;
            Block = block;
            Statement = statement;
            Target = target;
            Parameter = parameter;
            Name.BindDefinition(this);
        }

        internal void BindStatement(GimpleStatement? statement, GimplePlace? target = null)
        {
            Statement = statement;
            if (target is not null)
                Target = target;
        }

        public override string ToString()
            => Kind == GimpleDefinitionKind.Phi
                ? $"{Name} = phi"
                : $"{Name} = {Kind}";
    }

    public sealed class GimpleUse
    {
        public GimpleName Name { get; }
        public GimpleUseKind Kind { get; }
        public ControlFlowBlock Block { get; }
        public GimpleStatement? Statement { get; private set; }
        public GimpleValue? Value { get; }
        public ControlFlowEdge? Edge { get; }

        internal GimpleUse(
            GimpleName name,
            GimpleUseKind kind,
            ControlFlowBlock block,
            GimpleStatement? statement,
            GimpleValue? value,
            ControlFlowEdge? edge = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Kind = kind;
            Block = block ?? throw new ArgumentNullException(nameof(block));
            Statement = statement;
            Value = value;
            Edge = edge;
        }

        internal void BindStatement(GimpleStatement? statement)
            => Statement = statement;

        public override string ToString()
            => $"{Name} ({Kind})";
    }

    public sealed class GimplePhi : GimpleStatement
    {
        public override GimpleNodeKind Kind => GimpleNodeKind.PhiStatement;
        public int Ordinal { get; }
        public ControlFlowBlock Block { get; }
        public GimpleVariable Variable { get; }
        public GimpleName Result { get; }
        public ImmutableArray<GimplePhiOperand> Operands { get; }

        internal GimplePhi(
            int ordinal,
            ControlFlowBlock block,
            GimpleVariable variable,
            GimpleName result,
            ImmutableArray<GimplePhiOperand> operands)
            : base(result?.Syntax)
        {
            if (ordinal < 0)
                throw new ArgumentOutOfRangeException(nameof(ordinal));

            Ordinal = ordinal;
            Block = block ?? throw new ArgumentNullException(nameof(block));
            Variable = variable ?? throw new ArgumentNullException(nameof(variable));
            Result = result ?? throw new ArgumentNullException(nameof(result));
            Operands = operands.IsDefault ? ImmutableArray<GimplePhiOperand>.Empty : operands;
            Result.Definition?.BindStatement(this, Result);
        }

        public override string ToString()
            => $"{Result} = phi({string.Join(", ", Operands.Select(static operand => operand.Value.ToString()))})";
    }

    public readonly struct GimplePhiOperand
    {
        public ControlFlowEdge Edge { get; }
        public ControlFlowBlock Predecessor => Edge.Source;
        public GimpleName Value { get; }

        public GimplePhiOperand(ControlFlowEdge edge, GimpleName value)
        {
            Edge = edge ?? throw new ArgumentNullException(nameof(edge));
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public override string ToString()
            => $"{Predecessor}: {Value}";
    }

    public sealed class GimpleOperandInfo
    {
        public GimpleValue Original { get; }
        public GimpleName? Name { get; }
        public ImmutableArray<GimpleOperandInfo> Children { get; }
        public GimpleOperandRole Role { get; }
        public bool IsAddress => Role == GimpleOperandRole.Address;
        public bool ReadsMemory { get; }
        public bool WritesMemory { get; }
        public bool ContainsCall { get; }

        internal GimpleOperandInfo(
            GimpleValue original,
            GimpleName? name,
            ImmutableArray<GimpleOperandInfo> children,
            bool readsMemory,
            bool writesMemory,
            bool containsCall,
            GimpleOperandRole role = GimpleOperandRole.Value)
        {
            Original = original ?? throw new ArgumentNullException(nameof(original));
            Name = name;
            Children = children.IsDefault ? ImmutableArray<GimpleOperandInfo>.Empty : children;
            Role = role;
            ReadsMemory = readsMemory;
            WritesMemory = writesMemory;
            ContainsCall = containsCall;
        }

        public override string ToString()
            => Name?.ToString() ?? Original.ToString() ?? Original.Kind.ToString();
    }

    public sealed class GimpleStatementAnnotations
    {
        public int Ordinal { get; }
        public ControlFlowBlock Block { get; }
        public GimpleStatement Statement { get; }
        public GimpleStatement InputStatement { get; }
        public ImmutableArray<GimpleOperandInfo> Operands { get; }
        public ImmutableArray<GimpleUse> Uses { get; }
        public ImmutableArray<GimpleDefinition> Definitions { get; }
        public GimpleName? MemoryInput { get; }
        public GimpleName? MemoryOutput { get; }
        public GimpleName? VUse => MemoryInput;
        public GimpleName? VDef => MemoryOutput;
        public GimpleStatementFlags Flags { get; }

        internal GimpleStatementAnnotations(
            int ordinal,
            ControlFlowBlock block,
            GimpleStatement statement,
            GimpleStatement inputStatement,
            ImmutableArray<GimpleOperandInfo> operands,
            ImmutableArray<GimpleUse> uses,
            ImmutableArray<GimpleDefinition> definitions,
            GimpleName? memoryInput,
            GimpleName? memoryOutput,
            GimpleStatementFlags flags)
        {
            if (ordinal < 0)
                throw new ArgumentOutOfRangeException(nameof(ordinal));

            Ordinal = ordinal;
            Block = block ?? throw new ArgumentNullException(nameof(block));
            Statement = statement ?? throw new ArgumentNullException(nameof(statement));
            InputStatement = inputStatement ?? throw new ArgumentNullException(nameof(inputStatement));
            Operands = operands.IsDefault ? ImmutableArray<GimpleOperandInfo>.Empty : operands;
            Uses = uses.IsDefault ? ImmutableArray<GimpleUse>.Empty : uses;
            Definitions = definitions.IsDefault ? ImmutableArray<GimpleDefinition>.Empty : definitions;
            MemoryInput = memoryInput;
            MemoryOutput = memoryOutput;
            Flags = flags;
        }

        public override string ToString()
            => Statement.Kind.ToString();
    }

    public sealed class GimpleBlockAnnotations
    {
        public ControlFlowBlock ControlFlowBlock { get; }
        public GimpleBasicBlock? GimpleBlock => ControlFlowBlock.GimpleBlock;
        public ImmutableArray<GimplePhi> Phis { get; }
        public ImmutableArray<GimpleStatementAnnotations> Statements { get; }
        public bool IsReachable => ControlFlowBlock.IsReachable;

        internal GimpleBlockAnnotations(
            ControlFlowBlock controlFlowBlock,
            ImmutableArray<GimplePhi> phis,
            ImmutableArray<GimpleStatementAnnotations> statements)
        {
            ControlFlowBlock = controlFlowBlock ?? throw new ArgumentNullException(nameof(controlFlowBlock));
            Phis = phis.IsDefault ? ImmutableArray<GimplePhi>.Empty : phis;
            Statements = statements.IsDefault ? ImmutableArray<GimpleStatementAnnotations>.Empty : statements;
        }

        public override string ToString()
            => ControlFlowBlock.ToString();
    }

    public sealed class GimpleProblem
    {
        public GimpleProblemKind Kind { get; }
        public ControlFlowBlock? Block { get; }
        public string Message { get; }

        internal GimpleProblem(GimpleProblemKind kind, ControlFlowBlock? block, string message)
        {
            Kind = kind;
            Block = block;
            Message = message ?? string.Empty;
        }

        internal static GimpleProblem FromControlFlowProblem(ControlFlowProblem problem)
        {
            if (problem is null)
                throw new ArgumentNullException(nameof(problem));

            return new GimpleProblem(GimpleProblemKind.ControlFlowProblem, problem.Block, problem.Message);
        }

        public override string ToString() => Message;
    }


    internal static class GimpleNameMaterializer
    {
        public static GimpleStatement MaterializeStatement(
            GimpleStatement statement,
            ImmutableArray<GimpleOperandInfo> expressions,
            ImmutableArray<GimpleDefinition> definitions)
        {
            if (statement is null)
                throw new ArgumentNullException(nameof(statement));

            var definition = GetPrimaryDefinition(definitions);
            switch (statement)
            {
                case GimpleAssignStatement assign:
                    {
                        var lhs = definition?.Name ?? MaterializePlace(expressions, 0, assign.Lhs);
                        var operandStart = definition is null ? 1 : 0;
                        var operands = MaterializeOperands(expressions, operandStart, assign.Operands);

                        if (ReferenceEquals(lhs, assign.Lhs) && operands.IsDefault)
                            return statement;

                        return assign.WithOperands(lhs, operands.IsDefault ? assign.Operands : operands);
                    }

                case GimpleCallStatement call:
                    {
                        var hasLhsAddress = call.Lhs is not null && definition is null;
                        var lhs = definition?.Name ?? (call.Lhs is null ? null : MaterializePlace(expressions, 0, call.Lhs));
                        var index = hasLhsAddress ? 1 : 0;
                        var function = index < expressions.Length ? MaterializeValue(expressions[index]) : call.Function;
                        var arguments = MaterializeOperands(expressions, index + 1, call.Arguments);

                        if (ReferenceEquals(lhs, call.Lhs) && ReferenceEquals(function, call.Function) && arguments.IsDefault)
                            return statement;

                        return call.WithOperands(lhs, function, arguments.IsDefault ? call.Arguments : arguments);
                    }

                case GimpleCondStatement conditional when expressions.Length > 1:
                    {
                        var left = MaterializeValue(expressions[0]);
                        var right = MaterializeValue(expressions[1]);
                        return ReferenceEquals(left, conditional.Lhs) && ReferenceEquals(right, conditional.Rhs)
                            ? statement
                            : conditional.WithOperands(conditional.Code, left, right);
                    }

                case GimpleSwitchStatement switchStatement when expressions.Length != 0:
                    {
                        var expression = MaterializeValue(expressions[0]);
                        return ReferenceEquals(expression, switchStatement.Expression)
                            ? statement
                            : new GimpleSwitchStatement(expression, switchStatement.Cases, switchStatement.DefaultLabel, statement.Syntax);
                    }

                case GimpleReturnStatement returnStatement when returnStatement.Expression is not null && expressions.Length != 0:
                    {
                        var expression = MaterializeValue(expressions[0]);
                        return ReferenceEquals(expression, returnStatement.Expression)
                            ? statement
                            : new GimpleReturnStatement(returnStatement.Function, expression, statement.Syntax);
                    }

                default:
                    return statement;
            }
        }

        // A default array signals that nothing changed and the original operands stand
        private static ImmutableArray<GimpleValue> MaterializeOperands(
            ImmutableArray<GimpleOperandInfo> expressions,
            int start,
            ImmutableArray<GimpleValue> originals)
        {
            if (originals.Length == 0 || start + originals.Length > expressions.Length)
                return default;

            ImmutableArray<GimpleValue>.Builder? builder = null;
            for (var i = 0; i < originals.Length; i++)
            {
                var materialized = MaterializeValue(expressions[start + i]);
                if (builder is null)
                {
                    if (ReferenceEquals(materialized, originals[i]))
                        continue;

                    builder = ImmutableArray.CreateBuilder<GimpleValue>(originals.Length);
                    for (var seen = 0; seen < i; seen++)
                        builder.Add(originals[seen]);
                }

                builder.Add(materialized);
            }

            return builder is null ? default : builder.ToImmutable();
        }

        private static GimplePlace MaterializePlace(ImmutableArray<GimpleOperandInfo> expressions, int index, GimplePlace original)
        {
            if (index >= expressions.Length)
                return original;

            return MaterializeValue(expressions[index]) as GimplePlace ?? original;
        }

        public static GimpleValue MaterializeValue(GimpleOperandInfo expression)
        {
            if (expression is null)
                throw new ArgumentNullException(nameof(expression));

            if (expression.Name is not null)
                return expression.IsAddress ? expression.Original : expression.Name;

            if (expression.Children.Length == 0)
                return expression.Original;

            var children = ImmutableArray.CreateBuilder<GimpleValue>(expression.Children.Length);
            foreach (var child in expression.Children)
                children.Add(MaterializeValue(child));

            switch (expression.Original)
            {
                case GimpleUnaryExpression unary when children.Count == 1:
                    return new GimpleUnaryExpression(unary.Code, children[0], unary.Type, unary.Syntax);

                case GimpleBinaryExpression binary when children.Count == 2:
                    return new GimpleBinaryExpression(children[0], binary.Code, children[1], binary.Type, binary.Syntax);

                case GimpleConversionExpression conversion when children.Count == 1:
                    return new GimpleConversionExpression(children[0], conversion.Type, conversion.ConversionKind, conversion.Syntax);

                case GimpleCastExpression cast when children.Count == 1:
                    return new GimpleCastExpression(children[0], cast.Type, cast.Syntax);

                case GimpleAddressOfExpression addressOf when children.Count == 1 && children[0] is GimplePlace target:
                    return new GimpleAddressOfExpression(target, addressOf.Type, addressOf.Syntax);

                case GimpleIndirectExpression indirect when children.Count == 1:
                    return new GimpleIndirectExpression(children[0], indirect.Type, indirect.Syntax);

                case GimpleElementAccessExpression elementAccess when elementAccess.Index is null && children.Count == 1:
                    return new GimpleElementAccessExpression(children[0], null, elementAccess.Type, elementAccess.Syntax);

                case GimpleElementAccessExpression elementAccess when elementAccess.Index is not null && children.Count == 2:
                    return new GimpleElementAccessExpression(children[0], children[1], elementAccess.Type, elementAccess.Syntax);

                case GimpleMemberAccessExpression memberAccess when children.Count == 1:
                    return new GimpleMemberAccessExpression(
                        children[0],
                        memberAccess.ThroughPointer,
                        memberAccess.NameToken,
                        memberAccess.Field,
                        memberAccess.Type,
                        memberAccess.Syntax);

                default:
                    return expression.Original;
            }
        }

        private static GimpleDefinition? GetPrimaryDefinition(ImmutableArray<GimpleDefinition> definitions)
        {
            foreach (var definition in definitions)
            {
                if (definition.Name.Variable.Kind != GimpleVariableKind.Memory)
                    return definition;
            }

            return null;
        }
    }


    internal sealed class GimpleAnnotationBuilder
    {
        private readonly TargetInfo _target;
        private readonly ControlFlowFunction _controlFlowFunction;
        private readonly SsaOptions _options;
        private readonly ValueNumberingOptions _valueNumberingOptions;
        private readonly Dictionary<GimpleVariableKey, CandidateInfo> _candidates = new();
        private readonly List<CandidateInfo> _candidateOrder = new();
        private readonly HashSet<GimpleVariableKey> _addressTaken = new();
        private readonly HashSet<Symbol> _functionLocalSymbols = new();
        private readonly Dictionary<GimpleVariableKey, GimpleVariable> _variablesByKey = new();
        private readonly Dictionary<GimpleVariable, HashSet<ControlFlowBlock>> _definitionBlocks = new();
        private readonly Dictionary<GimpleVariable, HashSet<ControlFlowBlock>> _liveInBlocks = new();
        private readonly Dictionary<ControlFlowBlock, List<PhiBuilder>> _phisByBlock = new();
        private readonly Dictionary<ControlFlowBlock, List<GimpleStatementAnnotations>> _instructionsByBlock = new();
        private readonly Dictionary<GimpleVariable, Stack<GimpleName>> _stacks = new();
        private readonly Dictionary<GimpleVariable, int> _nextVersions = new();
        private readonly Dictionary<GimpleVariable, GimpleName> _undefinedNames = new();
        private readonly Dictionary<ParameterSymbol, GimpleVariable> _parameterVariables = new();
        private readonly List<GimpleVariable> _variables = new();
        private readonly List<GimpleDefinition> _definitions = new();
        private readonly List<GimpleUse> _uses = new();
        private readonly List<GimpleProblem> _problems = new();

        private GimpleVariable? _memoryVariable;
        private ControlFlowBlock? _currentBlock;
        private GimpleStatement? _currentStatement;
        private int _phiOrdinal;
        private int _instructionOrdinal;

        private GimpleAnnotationBuilder(
            ControlFlowFunction controlFlowFunction,
            SsaOptions options,
            ValueNumberingOptions valueNumberingOptions,
            TargetInfo target)
        {
            _target = target;
            _controlFlowFunction = controlFlowFunction ?? throw new ArgumentNullException(nameof(controlFlowFunction));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _valueNumberingOptions = valueNumberingOptions ?? throw new ArgumentNullException(nameof(valueNumberingOptions));
        }

        public static GimpleFunctionAnnotations Build(
            ControlFlowFunction controlFlowFunction,
            SsaOptions options,
            ValueNumberingOptions valueNumberingOptions,
            TargetInfo target)
            => new GimpleAnnotationBuilder(controlFlowFunction, options, valueNumberingOptions, target).Build();

        private GimpleFunctionAnnotations Build()
        {
            ScanCandidatesAndAddressTaken();
            CreateVariables();
            CollectDefinitionBlocks();
            ComputeLiveInBlocks();
            InsertPhis();
            InitializeStacks();

            if (_controlFlowFunction.Entry.IsReachable)
                RenameBlock(_controlFlowFunction.Entry);

            var blocks = ImmutableArray.CreateBuilder<GimpleBlockAnnotations>();
            foreach (var block in _controlFlowFunction.RealBlocks)
            {
                var phis = ImmutableArray<GimplePhi>.Empty;
                if (_phisByBlock.TryGetValue(block, out var phiBuilders))
                {
                    var phiArray = ImmutableArray.CreateBuilder<GimplePhi>(phiBuilders.Count);
                    foreach (var phiBuilder in phiBuilders)
                        phiArray.Add(phiBuilder.Build(_undefinedNames[phiBuilder.Variable], _problems));
                    phis = phiArray.ToImmutable();
                }

                var instructions = _instructionsByBlock.TryGetValue(block, out var instructionList)
                    ? instructionList.ToImmutableArray()
                    : ImmutableArray<GimpleStatementAnnotations>.Empty;

                blocks.Add(new GimpleBlockAnnotations(block, phis, instructions));
            }

            return new GimpleFunctionAnnotations(
                _controlFlowFunction,
                _memoryVariable,
                _variables.ToImmutableArray(),
                blocks.ToImmutable(),
                _definitions.ToImmutableArray(),
                _uses.ToImmutableArray(),
                _problems.ToImmutableArray(),
                _undefinedNames,
                _valueNumberingOptions,
                _target);
        }

        private void ScanCandidatesAndAddressTaken()
        {
            var functionType = _controlFlowFunction.Symbol?.FunctionType;
            if (functionType is not null)
            {
                foreach (var parameter in functionType.Parameters)
                {
                    _functionLocalSymbols.Add(parameter);
                    AddCandidate(GimpleVariableKey.FromSymbol(parameter), parameter.Type, parameter.Name, StorageClass.Auto);
                }
            }

            foreach (var temporary in _controlFlowFunction.Function.Temporaries)
                AddCandidate(GimpleVariableKey.FromTemporary(temporary), temporary.Type, temporary.Name, StorageClass.Auto);

            foreach (var block in _controlFlowFunction.RealBlocks)
            {
                foreach (var statement in block.Statements)
                    ScanStatement(statement);
            }
        }

        private void ScanStatement(GimpleStatement statement)
        {
            switch (statement)
            {
                case GimpleDeclarationStatement declaration:
                    if (declaration.Symbol is TypedSymbol typed && declaration.Symbol is VariableSymbol or ParameterSymbol)
                    {
                        _functionLocalSymbols.Add(declaration.Symbol);
                        AddCandidate(GimpleVariableKey.FromSymbol(declaration.Symbol), typed.Type, declaration.Symbol.Name, declaration.StorageClass);
                    }
                    break;

                case GimpleAssignStatement assign:
                    ScanPlace(assign.Lhs, addressContext: false);
                    foreach (var operand in assign.Operands)
                        ScanValue(operand);
                    break;

                case GimpleCallStatement call:
                    if (call.Lhs is not null)
                        ScanPlace(call.Lhs, addressContext: false);
                    ScanValue(call.Function);
                    foreach (var argument in call.Arguments)
                        ScanValue(argument);
                    break;

                case GimpleAsmStatement asmStatement:
                    foreach (var output in asmStatement.Outputs)
                    {
                        if (output.IsReadWrite && output.Value is not null)
                            ScanValue(output.Value);

                        if (output.Target is null)
                            continue;

                        if (InlineAsmConstraints.PreferredStorage(output.Constraint, output.Target.Type) == InlineAsmOperandStorage.Memory)
                        {
                            MarkAddressTaken(output.Target);
                            ScanPlace(output.Target, addressContext: true);
                        }
                        else
                        {
                            ScanPlace(output.Target, addressContext: false);
                        }
                    }

                    foreach (var input in asmStatement.Inputs)
                    {
                        if (input.Value is GimplePlace inputPlace &&
                            InlineAsmConstraints.PreferredStorage(input.Constraint, input.Value.Type) == InlineAsmOperandStorage.Memory)
                        {
                            MarkAddressTaken(inputPlace);
                            ScanPlace(inputPlace, addressContext: true);
                        }
                        else if (input.Value is not null)
                        {
                            ScanValue(input.Value);
                        }
                    }
                    break;

                case GimpleCondStatement conditional:
                    ScanValue(conditional.Lhs);
                    ScanValue(conditional.Rhs);
                    break;

                case GimpleSwitchStatement switchStatement:
                    ScanValue(switchStatement.Expression);
                    break;

                case GimpleReturnStatement returnStatement when returnStatement.Expression is not null:
                    ScanValue(returnStatement.Expression);
                    break;
            }
        }

        private void ScanValue(GimpleValue value)
        {
            switch (value)
            {
                case GimpleSymbolValue symbolValue:
                    if (TryCreateKey(symbolValue, out var symbolKey))
                        AddCandidate(symbolKey, symbolValue.Type, symbolValue.ToString(), GetStorageClass(symbolValue.Symbol));
                    break;

                case GimpleTemporaryValue temporary:
                    AddCandidate(GimpleVariableKey.FromTemporary(temporary), temporary.Type, temporary.Name, StorageClass.Auto);
                    break;

                case GimpleUnaryExpression unary:
                    ScanValue(unary.Operand);
                    break;

                case GimpleBinaryExpression binary:
                    ScanValue(binary.Left);
                    ScanValue(binary.Right);
                    break;

                case GimpleConversionExpression conversion:
                    ScanValue(conversion.Operand);
                    break;

                case GimpleCastExpression cast:
                    ScanValue(cast.Operand);
                    break;

                case GimpleAddressOfExpression addressOf:
                    MarkAddressTaken(addressOf.Target);
                    ScanPlace(addressOf.Target, addressContext: true);
                    break;

                case GimpleIndirectExpression indirect:
                    ScanValue(indirect.Address);
                    break;

                case GimpleElementAccessExpression elementAccess:
                    ScanValue(elementAccess.Expression);
                    if (elementAccess.Index is not null)
                        ScanValue(elementAccess.Index);
                    break;

                case GimpleMemberAccessExpression memberAccess:
                    ScanValue(memberAccess.Expression);
                    break;
            }
        }

        private void ScanPlace(GimplePlace place, bool addressContext)
        {
            if (addressContext)
            {
                switch (place)
                {
                    case GimpleSymbolValue:
                    case GimpleTemporaryValue:
                        return;
                }
            }

            ScanValue(place);
        }

        private void MarkAddressTaken(GimplePlace place)
        {
            switch (place)
            {
                case GimpleSymbolValue symbolValue when TryCreateKey(symbolValue, out var key):
                    _addressTaken.Add(key);
                    break;

                case GimpleTemporaryValue temporary:
                    _addressTaken.Add(GimpleVariableKey.FromTemporary(temporary));
                    break;

                case GimpleIndirectExpression indirect:
                    ScanValue(indirect.Address);
                    break;

                case GimpleElementAccessExpression elementAccess:
                    MarkAddressTakenBase(elementAccess.Expression);
                    if (elementAccess.Index is not null)
                        ScanValue(elementAccess.Index);
                    break;

                case GimpleMemberAccessExpression memberAccess:
                    MarkAddressTakenBase(memberAccess.Expression);
                    break;
            }
        }

        private void MarkAddressTakenBase(GimpleValue value)
        {
            switch (value)
            {
                case GimpleSymbolValue symbolValue when TryCreateKey(symbolValue, out var key):
                    _addressTaken.Add(key);
                    break;

                case GimpleTemporaryValue temporary:
                    _addressTaken.Add(GimpleVariableKey.FromTemporary(temporary));
                    break;

                default:
                    ScanValue(value);
                    break;
            }
        }

        private void AddCandidate(GimpleVariableKey key, QualifiedType type, string name, StorageClass storageClass)
        {
            if (!_candidates.TryGetValue(key, out var candidate))
            {
                candidate = new CandidateInfo(key, GimpleTypeHelpers.Normalize(type), name, storageClass);
                _candidates.Add(key, candidate);
                _candidateOrder.Add(candidate);
                return;
            }

            candidate.Type = GimpleTypeHelpers.Normalize(type);
            if (candidate.StorageClass == StorageClass.None && storageClass != StorageClass.None)
                candidate.StorageClass = storageClass;
        }

        private void CreateVariables()
        {
            var ordinal = 0;
            if (_options.TrackMemory)
            {
                _memoryVariable = new GimpleVariable(
                    ordinal++,
                    GimpleVariableKind.Memory,
                    symbol: null,
                    temporary: null,
                    new QualifiedType(TypeCatalog.Instance.Void),
                    ".MEM");
                _variables.Add(_memoryVariable);
            }

            foreach (var candidate in _candidateOrder)
            {
                if (!IsPromotable(candidate))
                    continue;

                var variable = new GimpleVariable(
                    ordinal++,
                    candidate.Key.Kind,
                    candidate.Key.Symbol,
                    candidate.Key.Temporary,
                    candidate.Type,
                    candidate.Name);

                _variablesByKey.Add(candidate.Key, variable);
                _variables.Add(variable);

                if (candidate.Key.Symbol is ParameterSymbol parameter)
                    _parameterVariables[parameter] = variable;
            }
        }

        private bool IsPromotable(CandidateInfo candidate)
        {
            if (candidate.Key.Kind == GimpleVariableKind.Temporary && !_options.PromoteTemporaries)
                return false;

            if (!_options.PromoteAddressTakenVariables && _addressTaken.Contains(candidate.Key))
                return false;

            if (!_options.PromoteVolatileVariables &&
                (candidate.Type.Qualifiers & (TypeQualifiers.Volatile | TypeQualifiers.Atomic)) != 0)
                return false;

            if (!_options.PromoteAggregateVariables && IsAggregate(candidate.Type))
                return false;

            if (candidate.Type.Type.Kind == TypeKind.Function)
                return false;

            if (candidate.Type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Void })
                return false;

            if (candidate.Key.Symbol is VariableSymbol variableSymbol)
            {
                if (!_functionLocalSymbols.Contains(variableSymbol))
                    return false;

                var storageClass = variableSymbol.StorageClass == StorageClass.None
                    ? candidate.StorageClass
                    : variableSymbol.StorageClass;

                if (storageClass is StorageClass.Static or StorageClass.Extern or StorageClass.ThreadLocal)
                    return false;
            }

            return true;
        }

        private static bool IsAggregate(QualifiedType type)
            => type.Type.Kind is TypeKind.Array or TypeKind.Struct or TypeKind.Union;

        private void CollectDefinitionBlocks()
        {
            foreach (var variable in _variables)
                _definitionBlocks.Add(variable, new HashSet<ControlFlowBlock>());

            foreach (var pair in _parameterVariables)
                _definitionBlocks[pair.Value].Add(_controlFlowFunction.Entry);

            if (_memoryVariable is not null)
                _definitionBlocks[_memoryVariable].Add(_controlFlowFunction.Entry);

            foreach (var block in _controlFlowFunction.RealBlocks)
            {
                if (!block.IsReachable)
                    continue;

                foreach (var statement in block.Statements)
                    CollectStatementDefinitionBlocks(block, statement);
            }
        }

        private void CollectStatementDefinitionBlocks(ControlFlowBlock block, GimpleStatement statement)
        {
            if (GimpleMemoryEffects.HasOrderedAccess(statement))
                AddMemoryDefinitionBlock(block);
            switch (statement)
            {
                case GimpleAssignStatement assign:
                    if (TryGetVariable(assign.Lhs, out var assignTargetVariable))
                        _definitionBlocks[assignTargetVariable].Add(block);
                    else
                        AddMemoryDefinitionBlock(block);
                    break;

                case GimpleCallStatement call:
                    if (call.Lhs is not null && TryGetVariable(call.Lhs, out var callTargetVariable))
                        _definitionBlocks[callTargetVariable].Add(block);

                    AddMemoryDefinitionBlock(block);
                    break;

                case GimpleAsmStatement asmStatement:
                    foreach (var output in asmStatement.Outputs)
                    {
                        if (output.Target is not null &&
                            InlineAsmConstraints.PreferredStorage(output.Constraint, output.Target.Type) != InlineAsmOperandStorage.Memory &&
                            TryGetVariable(output.Target, out var outputVariable))
                            _definitionBlocks[outputVariable].Add(block);
                        else
                            AddMemoryDefinitionBlock(block);
                    }
                    AddMemoryDefinitionBlock(block);
                    break;

            }
        }

        private void AddMemoryDefinitionBlock(ControlFlowBlock block)
        {
            if (_memoryVariable is not null)
                _definitionBlocks[_memoryVariable].Add(block);
        }

        private void ComputeLiveInBlocks()
        {
            var blockUses = new Dictionary<ControlFlowBlock, HashSet<GimpleVariable>>();
            var blockDefs = new Dictionary<ControlFlowBlock, HashSet<GimpleVariable>>();
            var liveOut = new Dictionary<ControlFlowBlock, HashSet<GimpleVariable>>();

            foreach (var variable in _variables)
                _liveInBlocks[variable] = new HashSet<ControlFlowBlock>();

            foreach (var block in _controlFlowFunction.RealBlocks)
            {
                var uses = new HashSet<GimpleVariable>();
                var defs = new HashSet<GimpleVariable>();

                if (ReferenceEquals(block, _controlFlowFunction.Entry))
                {
                    foreach (var parameterVariable in _parameterVariables.Values)
                        defs.Add(parameterVariable);

                    if (_memoryVariable is not null)
                        defs.Add(_memoryVariable);
                }

                if (block.IsReachable)
                {
                    foreach (var statement in block.Statements)
                        CollectStatementLiveUsesAndDefs(statement, uses, defs);
                }

                blockUses[block] = uses;
                blockDefs[block] = defs;
                liveOut[block] = new HashSet<GimpleVariable>();
            }

            var changed = true;
            while (changed)
            {
                changed = false;

                foreach (var block in _controlFlowFunction.ReversePostOrder.Reverse())
                {
                    if (!blockUses.TryGetValue(block, out var uses))
                        continue;

                    var newOut = new HashSet<GimpleVariable>();
                    foreach (var successor in block.UniqueSuccessors)
                    {
                        if (!blockUses.TryGetValue(successor, out _))
                            continue;

                        foreach (var variable in _liveInBlocks.Where(pair => pair.Value.Contains(successor)).Select(static pair => pair.Key))
                            newOut.Add(variable);
                    }

                    var newIn = new HashSet<GimpleVariable>(uses);
                    foreach (var variable in newOut)
                    {
                        if (!blockDefs[block].Contains(variable))
                            newIn.Add(variable);
                    }

                    if (!SetEquals(liveOut[block], newOut))
                    {
                        liveOut[block] = newOut;
                        changed = true;
                    }

                    foreach (var variable in _variables)
                    {
                        var liveSet = _liveInBlocks[variable];
                        var shouldContain = newIn.Contains(variable);
                        if (shouldContain && liveSet.Add(block))
                            changed = true;
                        else if (!shouldContain && liveSet.Remove(block))
                            changed = true;
                    }
                }
            }
        }

        private void CollectStatementLiveUsesAndDefs(
            GimpleStatement statement,
            HashSet<GimpleVariable> uses,
            HashSet<GimpleVariable> defs)
        {
            switch (statement)
            {
                case GimpleAssignStatement assign:
                    if (TryGetVariable(assign.Lhs, out var assignTargetVariable))
                    {
                        foreach (var operand in assign.Operands)
                            CollectValueLiveUses(operand, uses, defs);

                        defs.Add(assignTargetVariable);
                    }
                    else
                    {
                        CollectPlaceAddressLiveUses(assign.Lhs, uses, defs);
                        foreach (var operand in assign.Operands)
                            CollectValueLiveUses(operand, uses, defs);

                        if (_memoryVariable is not null)
                            defs.Add(_memoryVariable);
                    }
                    break;

                case GimpleCallStatement call:
                    CollectValueLiveUses(call.Function, uses, defs);
                    foreach (var argument in call.Arguments)
                        CollectValueLiveUses(argument, uses, defs);

                    if (call.Lhs is not null && TryGetVariable(call.Lhs, out var callTargetVariable))
                        defs.Add(callTargetVariable);
                    else if (call.Lhs is not null)
                        CollectPlaceAddressLiveUses(call.Lhs, uses, defs);

                    if (_memoryVariable is not null)
                    {
                        MarkLiveUse(_memoryVariable, uses, defs);
                        defs.Add(_memoryVariable);
                    }
                    break;

                case GimpleAsmStatement asmStatement:
                    foreach (var output in asmStatement.Outputs)
                    {
                        if (output.IsReadWrite && output.Value is not null)
                            CollectValueLiveUses(output.Value, uses, defs);

                        if (output.Target is null)
                            continue;

                        if (InlineAsmConstraints.PreferredStorage(output.Constraint, output.Target.Type) != InlineAsmOperandStorage.Memory &&
                            TryGetVariable(output.Target, out var outputVariable))
                            defs.Add(outputVariable);
                        else
                            CollectPlaceAddressLiveUses(output.Target, uses, defs);
                    }

                    foreach (var input in asmStatement.Inputs)
                    {
                        if (input.Value is GimplePlace inputPlace &&
                            InlineAsmConstraints.PreferredStorage(input.Constraint, input.Value.Type) == InlineAsmOperandStorage.Memory)
                            CollectPlaceAddressLiveUses(inputPlace, uses, defs);
                        else if (input.Value is not null)
                            CollectValueLiveUses(input.Value, uses, defs);
                    }

                    if (_memoryVariable is not null)
                    {
                        uses.Add(_memoryVariable);
                        defs.Add(_memoryVariable);
                    }
                    break;

                case GimpleCondStatement conditional:
                    CollectValueLiveUses(conditional.Lhs, uses, defs);
                    CollectValueLiveUses(conditional.Rhs, uses, defs);
                    break;

                case GimpleSwitchStatement switchStatement:
                    CollectValueLiveUses(switchStatement.Expression, uses, defs);
                    break;

                case GimpleReturnStatement returnStatement when returnStatement.Expression is not null:
                    CollectValueLiveUses(returnStatement.Expression, uses, defs);
                    break;
            }
        }

        private void CollectValueLiveUses(
            GimpleValue value,
            HashSet<GimpleVariable> uses,
            HashSet<GimpleVariable> defs)
        {
            switch (value)
            {
                case GimpleSymbolValue symbolValue when TryGetVariable(symbolValue, out var variable):
                    MarkLiveUse(variable, uses, defs);
                    break;

                case GimpleTemporaryValue temporary when TryGetVariable(temporary, out var variable):
                    MarkLiveUse(variable, uses, defs);
                    break;

                case GimpleSymbolValue symbolValue when IsMemoryBackedDirectRead(symbolValue):
                    if (_memoryVariable is not null)
                        MarkLiveUse(_memoryVariable, uses, defs);
                    break;

                case GimpleTemporaryValue:
                    if (_memoryVariable is not null)
                        MarkLiveUse(_memoryVariable, uses, defs);
                    break;

                case GimpleUnaryExpression unary:
                    CollectValueLiveUses(unary.Operand, uses, defs);
                    break;

                case GimpleBinaryExpression binary:
                    CollectValueLiveUses(binary.Left, uses, defs);
                    CollectValueLiveUses(binary.Right, uses, defs);
                    break;

                case GimpleConversionExpression conversion:
                    CollectValueLiveUses(conversion.Operand, uses, defs);
                    break;

                case GimpleCastExpression cast:
                    CollectValueLiveUses(cast.Operand, uses, defs);
                    break;

                case GimpleAddressOfExpression addressOf:
                    CollectPlaceAddressLiveUses(addressOf.Target, uses, defs);
                    break;

                case GimpleIndirectExpression indirect:
                    CollectValueLiveUses(indirect.Address, uses, defs);
                    if (_memoryVariable is not null)
                        MarkLiveUse(_memoryVariable, uses, defs);
                    break;

                case GimpleElementAccessExpression elementAccess:
                    CollectValueLiveUses(elementAccess.Expression, uses, defs);
                    if (elementAccess.Index is not null)
                        CollectValueLiveUses(elementAccess.Index, uses, defs);
                    if (_memoryVariable is not null)
                        MarkLiveUse(_memoryVariable, uses, defs);
                    break;

                case GimpleMemberAccessExpression memberAccess:
                    CollectValueLiveUses(memberAccess.Expression, uses, defs);
                    if (_memoryVariable is not null)
                        MarkLiveUse(_memoryVariable, uses, defs);
                    break;
            }
        }

        private void CollectPlaceAddressLiveUses(
            GimplePlace place,
            HashSet<GimpleVariable> uses,
            HashSet<GimpleVariable> defs)
        {
            switch (place)
            {
                case GimpleSymbolValue symbolValue when TryGetVariable(symbolValue, out var variable):
                    MarkLiveUse(variable, uses, defs);
                    break;

                case GimpleTemporaryValue temporary when TryGetVariable(temporary, out var variable):
                    MarkLiveUse(variable, uses, defs);
                    break;

                case GimpleIndirectExpression indirect:
                    CollectValueLiveUses(indirect.Address, uses, defs);
                    break;

                case GimpleElementAccessExpression elementAccess:
                    CollectPlaceAddressLiveUsesForBase(elementAccess.Expression, uses, defs);
                    if (elementAccess.Index is not null)
                        CollectValueLiveUses(elementAccess.Index, uses, defs);
                    break;

                case GimpleMemberAccessExpression memberAccess:
                    CollectPlaceAddressLiveUsesForBase(memberAccess.Expression, uses, defs);
                    break;
            }
        }

        private void CollectPlaceAddressLiveUsesForBase(
            GimpleValue value,
            HashSet<GimpleVariable> uses,
            HashSet<GimpleVariable> defs)
        {
            if (value is GimplePlace place)
                CollectPlaceAddressLiveUses(place, uses, defs);
            else
                CollectValueLiveUses(value, uses, defs);
        }

        private static void MarkLiveUse(
            GimpleVariable variable,
            HashSet<GimpleVariable> uses,
            HashSet<GimpleVariable> defs)
        {
            if (!defs.Contains(variable))
                uses.Add(variable);
        }

        private static bool SetEquals<T>(HashSet<T> left, HashSet<T> right)
            => left.Count == right.Count && left.SetEquals(right);

        private bool ShouldInsertPhi(GimpleVariable variable, ControlFlowBlock block)
        {
            if (variable.Kind == GimpleVariableKind.Memory)
                return true;

            return _liveInBlocks.TryGetValue(variable, out var liveIn) && liveIn.Contains(block);
        }

        private void InsertPhis()
        {
            foreach (var variable in _variables)
            {
                if (!_definitionBlocks.TryGetValue(variable, out var blocks) || blocks.Count == 0)
                    continue;

                var workList = new Queue<ControlFlowBlock>(blocks.OrderBy(static block => block.Ordinal));
                var queuedOrProcessed = new HashSet<ControlFlowBlock>(blocks);
                var hasPhi = new HashSet<ControlFlowBlock>();

                while (workList.Count != 0)
                {
                    var block = workList.Dequeue();
                    foreach (var frontier in block.DominanceFrontier)
                    {
                        if (!frontier.IsReachable || frontier.IsExit)
                            continue;

                        if (!ShouldInsertPhi(variable, frontier))
                            continue;

                        if (!hasPhi.Add(frontier))
                            continue;

                        AddPhi(frontier, variable);
                        if (queuedOrProcessed.Add(frontier))
                            workList.Enqueue(frontier);
                    }
                }
            }
        }

        private void AddPhi(ControlFlowBlock block, GimpleVariable variable)
        {
            if (!_phisByBlock.TryGetValue(block, out var phis))
            {
                phis = new List<PhiBuilder>();
                _phisByBlock.Add(block, phis);
            }

            phis.Add(new PhiBuilder(_phiOrdinal++, block, variable));
            phis.Sort(static (left, right) => left.Variable.Ordinal.CompareTo(right.Variable.Ordinal));
        }

        private void InitializeStacks()
        {
            foreach (var variable in _variables)
            {
                _stacks.Add(variable, new Stack<GimpleName>());
                _nextVersions.Add(variable, 0);
                var undefined = CreateName(variable, isUndefined: true);
                _undefinedNames.Add(variable, undefined);
                _stacks[variable].Push(undefined);
                _definitions.Add(new GimpleDefinition(undefined, GimpleDefinitionKind.Undefined, block: null, statement: null, target: null, parameter: null));
            }
        }

        private void RenameBlock(ControlFlowBlock block)
        {
            var pushed = new List<GimpleVariable>();

            if (ReferenceEquals(block, _controlFlowFunction.Entry))
            {
                foreach (var parameter in _controlFlowFunction.Symbol?.FunctionType?.Parameters ?? ImmutableArray<ParameterSymbol>.Empty)
                {
                    if (_parameterVariables.TryGetValue(parameter, out var variable))
                    {
                        _ = PushDefinition(variable, GimpleDefinitionKind.Entry, block, statement: null, target: null, parameter: parameter);
                        pushed.Add(variable);
                    }
                }

                if (_memoryVariable is not null)
                {
                    _ = PushDefinition(_memoryVariable, GimpleDefinitionKind.Entry, block, statement: null, target: null, parameter: null);
                    pushed.Add(_memoryVariable);
                }
            }

            if (_phisByBlock.TryGetValue(block, out var phis))
            {
                foreach (var phi in phis)
                {
                    phi.Result = PushDefinition(phi.Variable, GimpleDefinitionKind.Phi, block, statement: null, target: null, parameter: null).Name;
                    pushed.Add(phi.Variable);
                }
            }

            foreach (var statement in block.Statements)
            {
                var instruction = RenameStatement(block, statement, pushed);
                if (!_instructionsByBlock.TryGetValue(block, out var instructions))
                {
                    instructions = new List<GimpleStatementAnnotations>();
                    _instructionsByBlock.Add(block, instructions);
                }

                instructions.Add(instruction);
            }

            AddPhiInputsToSuccessors(block);

            foreach (var child in block.DominatorChildren)
            {
                if (child.IsReachable && !child.IsExit)
                    RenameBlock(child);
            }

            for (var i = pushed.Count - 1; i >= 0; i--)
                _stacks[pushed[i]].Pop();
        }

        private GimpleStatementAnnotations RenameStatement(ControlFlowBlock block, GimpleStatement statement, List<GimpleVariable> pushed)
        {
            _currentBlock = block;
            _currentStatement = statement;

            var uses = ImmutableArray.CreateBuilder<GimpleUse>();
            var definitions = ImmutableArray.CreateBuilder<GimpleDefinition>();
            var expressions = ImmutableArray.CreateBuilder<GimpleOperandInfo>();
            var flags = GimpleStatementFlags.None;
            bool explicitMemoryWrite = false;

            switch (statement)
            {
                case GimpleAssignStatement assign:
                    if (!TryGetVariable(assign.Lhs, out _))
                    {
                        expressions.Add(RewritePlaceAddress(assign.Lhs, uses));
                        explicitMemoryWrite = true;
                    }

                    foreach (var operand in assign.Operands)
                        expressions.Add(RewriteValue(operand, uses));
                    break;

                case GimpleCallStatement call:
                    if (call.Lhs is not null && !TryGetVariable(call.Lhs, out _))
                    {
                        expressions.Add(RewritePlaceAddress(call.Lhs, uses));
                        explicitMemoryWrite = true;
                    }

                    expressions.Add(RewriteValue(call.Function, uses));
                    foreach (var argument in call.Arguments)
                        expressions.Add(RewriteValue(argument, uses));

                    flags |= GimpleStatementFlags.ReadsMemory | GimpleStatementFlags.WritesMemory | GimpleStatementFlags.ContainsCall;
                    break;

                case GimpleAsmStatement asmStatement:
                    foreach (var output in asmStatement.Outputs)
                    {
                        if (output.IsReadWrite && output.Value is not null)
                            expressions.Add(RewriteValue(output.Value, uses));
                        if (output.Target is not null &&
                            (InlineAsmConstraints.PreferredStorage(output.Constraint, output.Target.Type) == InlineAsmOperandStorage.Memory ||
                             !TryGetVariable(output.Target, out _)))
                        {
                            expressions.Add(RewritePlaceAddress(output.Target, uses));
                            explicitMemoryWrite = true;
                        }
                    }

                    foreach (var input in asmStatement.Inputs)
                    {
                        if (input.Value is GimplePlace inputPlace &&
                            InlineAsmConstraints.PreferredStorage(input.Constraint, input.Value.Type) == InlineAsmOperandStorage.Memory)
                            expressions.Add(RewritePlaceAddress(inputPlace, uses));
                        else if (input.Value is not null)
                            expressions.Add(RewriteValue(input.Value, uses));
                    }

                    flags |= GimpleStatementFlags.ReadsMemory | GimpleStatementFlags.WritesMemory | GimpleStatementFlags.ContainsCall;
                    break;

                case GimpleCondStatement conditional:
                    expressions.Add(RewriteValue(conditional.Lhs, uses));
                    expressions.Add(RewriteValue(conditional.Rhs, uses));
                    break;

                case GimpleSwitchStatement switchStatement:
                    expressions.Add(RewriteValue(switchStatement.Expression, uses));
                    break;

                case GimpleReturnStatement returnStatement when returnStatement.Expression is not null:
                    expressions.Add(RewriteValue(returnStatement.Expression, uses));
                    break;
            }

            foreach (var expression in expressions)
            {
                if (expression.ReadsMemory)
                    flags |= GimpleStatementFlags.ReadsMemory;
                if (expression.WritesMemory)
                    flags |= GimpleStatementFlags.WritesMemory;
                if (expression.ContainsCall)
                    flags |= GimpleStatementFlags.ContainsCall;
            }

            if (explicitMemoryWrite || GimpleMemoryEffects.HasOrderedAccess(statement))
                flags |= GimpleStatementFlags.WritesMemory;

            GimpleName? memoryInput = null;
            if (_memoryVariable is not null && (flags & (GimpleStatementFlags.ReadsMemory | GimpleStatementFlags.WritesMemory)) != 0)
            {
                memoryInput = Peek(_memoryVariable);
                var memoryUse = new GimpleUse(memoryInput, GimpleUseKind.Memory, block, statement, value: null);
                uses.Add(memoryUse);
                _uses.Add(memoryUse);
            }

            switch (statement)
            {
                case GimpleAssignStatement assign when TryGetVariable(assign.Lhs, out var targetVariable):
                    {
                        var definition = PushDefinition(targetVariable, GimpleDefinitionKind.Statement, block, statement, assign.Lhs, parameter: null);
                        definitions.Add(definition);
                        pushed.Add(targetVariable);
                    }
                    break;

                case GimpleCallStatement call when call.Lhs is not null && TryGetVariable(call.Lhs, out var targetVariable):
                    {
                        var definition = PushDefinition(targetVariable, GimpleDefinitionKind.Statement, block, statement, call.Lhs, parameter: null);
                        definitions.Add(definition);
                        pushed.Add(targetVariable);
                    }
                    break;

                case GimpleAsmStatement asmStatement:
                    foreach (var output in asmStatement.Outputs)
                    {
                        if (output.Target is not null &&
                            InlineAsmConstraints.PreferredStorage(output.Constraint, output.Target.Type) != InlineAsmOperandStorage.Memory &&
                            TryGetVariable(output.Target, out var outputVariable))
                        {
                            var definition = PushDefinition(outputVariable, GimpleDefinitionKind.Statement, block, statement, output.Target, parameter: null);
                            definitions.Add(definition);
                            pushed.Add(outputVariable);
                        }
                    }
                    break;
            }

            GimpleName? memoryOutput = null;
            if (_memoryVariable is not null && (flags & GimpleStatementFlags.WritesMemory) != 0)
            {
                var memoryDefinition = PushDefinition(_memoryVariable, GimpleDefinitionKind.MemoryStatement, block, statement, target: null, parameter: null);
                memoryOutput = memoryDefinition.Name;
                definitions.Add(memoryDefinition);
                pushed.Add(_memoryVariable);
            }

            var expressionArray = expressions.ToImmutable();
            var definitionArray = definitions.ToImmutable();
            var useArray = uses.ToImmutable();
            var gimpleStatement = GimpleNameMaterializer.MaterializeStatement(statement, expressionArray, definitionArray);

            foreach (var definition in definitionArray)
            {
                var target = definition.Name.Variable.Kind != GimpleVariableKind.Memory &&
                    gimpleStatement is GimpleAssignStatement or GimpleCallStatement
                        ? definition.Name
                        : null;
                definition.BindStatement(gimpleStatement, target);
            }

            foreach (var use in useArray)
                use.BindStatement(gimpleStatement);

            var instruction = new GimpleStatementAnnotations(
                _instructionOrdinal++,
                block,
                gimpleStatement,
                statement,
                expressionArray,
                useArray,
                definitionArray,
                memoryInput,
                memoryOutput,
                flags);

            _currentBlock = null;
            _currentStatement = null;
            return instruction;
        }

        private GimpleOperandInfo RewriteValue(GimpleValue value, ImmutableArray<GimpleUse>.Builder uses)
        {
            switch (value)
            {
                case GimpleSymbolValue symbolValue when TryGetVariable(symbolValue, out var variable):
                    return CreateNameExpression(symbolValue, variable, uses, GimpleUseKind.Value);

                case GimpleTemporaryValue temporary when TryGetVariable(temporary, out var variable):
                    return CreateNameExpression(temporary, variable, uses, GimpleUseKind.Value);

                case GimpleSymbolValue symbolValue:
                    return new GimpleOperandInfo(symbolValue, name: null, ImmutableArray<GimpleOperandInfo>.Empty, IsMemoryBackedDirectRead(symbolValue), writesMemory: false, containsCall: false);

                case GimpleTemporaryValue temporary:
                    return new GimpleOperandInfo(temporary, name: null, ImmutableArray<GimpleOperandInfo>.Empty, readsMemory: true, writesMemory: false, containsCall: false);

                case GimpleConstantValue constant:
                    return new GimpleOperandInfo(constant, name: null, ImmutableArray<GimpleOperandInfo>.Empty, readsMemory: false, writesMemory: false, containsCall: false);

                case GimpleUnaryExpression unary:
                    return CreateCompositeExpression(unary, RewriteValue(unary.Operand, uses));

                case GimpleBinaryExpression binary:
                    return CreateCompositeExpression(binary, RewriteValue(binary.Left, uses), RewriteValue(binary.Right, uses));

                case GimpleConversionExpression conversion:
                    return CreateCompositeExpression(conversion, RewriteValue(conversion.Operand, uses));

                case GimpleCastExpression cast:
                    return CreateCompositeExpression(cast, RewriteValue(cast.Operand, uses));

                case GimpleAddressOfExpression addressOf:
                    return CreateCompositeExpression(addressOf, RewritePlaceAddress(addressOf.Target, uses), readsMemoryOverride: false);

                case GimpleIndirectExpression indirect:
                    return CreateCompositeExpression(indirect, RewriteValue(indirect.Address, uses), readsMemoryOverride: true);

                case GimpleElementAccessExpression elementAccess:
                    return elementAccess.Index is null
                        ? CreateCompositeExpression(elementAccess, RewriteValue(elementAccess.Expression, uses), readsMemoryOverride: true)
                        : CreateCompositeExpression(elementAccess, RewriteValue(elementAccess.Expression, uses), RewriteValue(elementAccess.Index, uses), readsMemoryOverride: true);

                case GimpleMemberAccessExpression memberAccess:
                    return CreateCompositeExpression(memberAccess, RewriteValue(memberAccess.Expression, uses), readsMemoryOverride: true);

                default:
                    return new GimpleOperandInfo(value, name: null, ImmutableArray<GimpleOperandInfo>.Empty, readsMemory: false, writesMemory: false, containsCall: false);
            }
        }

        private GimpleOperandInfo RewritePlaceAddress(GimplePlace place, ImmutableArray<GimpleUse>.Builder uses)
        {
            switch (place)
            {
                case GimpleSymbolValue symbolValue when TryGetVariable(symbolValue, out var variable):
                    return CreateNameExpression(symbolValue, variable, uses, GimpleUseKind.Address);

                case GimpleTemporaryValue temporary when TryGetVariable(temporary, out var variable):
                    return CreateNameExpression(temporary, variable, uses, GimpleUseKind.Address);

                case GimpleSymbolValue symbolValue:
                    return new GimpleOperandInfo(symbolValue, name: null, ImmutableArray<GimpleOperandInfo>.Empty, readsMemory: false, writesMemory: false, containsCall: false, role: GimpleOperandRole.Address);

                case GimpleTemporaryValue temporary:
                    return new GimpleOperandInfo(temporary, name: null, ImmutableArray<GimpleOperandInfo>.Empty, readsMemory: false, writesMemory: false, containsCall: false, role: GimpleOperandRole.Address);

                case GimpleIndirectExpression indirect:
                    return CreateCompositeExpression(indirect, RewriteValue(indirect.Address, uses), role: GimpleOperandRole.Address);

                case GimpleElementAccessExpression elementAccess:
                    return elementAccess.Index is null
                        ? CreateCompositeExpression(elementAccess, RewriteAddressBase(elementAccess.Expression, uses), role: GimpleOperandRole.Address)
                        : CreateCompositeExpression(elementAccess, RewriteAddressBase(elementAccess.Expression, uses), RewriteValue(elementAccess.Index, uses), role: GimpleOperandRole.Address);

                case GimpleMemberAccessExpression memberAccess:
                    if (memberAccess.ThroughPointer)
                        return CreateCompositeExpression(memberAccess, RewriteValue(memberAccess.Expression, uses), role: GimpleOperandRole.Address);

                    return CreateCompositeExpression(memberAccess, RewriteAddressBase(memberAccess.Expression, uses), role: GimpleOperandRole.Address);

                default:
                    return RewriteValue(place, uses);
            }
        }

        private GimpleOperandInfo RewriteAddressBase(GimpleValue value, ImmutableArray<GimpleUse>.Builder uses)
        {
            return value.Type.Type is not PointerType && value is GimplePlace place
                ? RewritePlaceAddress(place, uses)
                : RewriteValue(value, uses);
        }

        private GimpleOperandInfo CreateNameExpression(GimpleValue original, GimpleVariable variable, ImmutableArray<GimpleUse>.Builder uses, GimpleUseKind kind)
        {
            var name = Peek(variable);
            var use = new GimpleUse(name, kind, _currentBlock!, _currentStatement, original);
            uses.Add(use);
            _uses.Add(use);
            var role = kind == GimpleUseKind.Address ? GimpleOperandRole.Address : GimpleOperandRole.Value;
            return new GimpleOperandInfo(original, name, ImmutableArray<GimpleOperandInfo>.Empty, readsMemory: false, writesMemory: false, containsCall: false, role);
        }

        private static GimpleOperandInfo CreateCompositeExpression(GimpleValue original, params GimpleOperandInfo[] children)
            => CreateCompositeExpression(original, children.ToImmutableArray(), readsMemoryOverride: null, writesMemoryOverride: null, containsCallOverride: null, role: GimpleOperandRole.Value);

        private static GimpleOperandInfo CreateCompositeExpression(GimpleValue original, GimpleOperandInfo child, bool? readsMemoryOverride = null, GimpleOperandRole role = GimpleOperandRole.Value)
            => CreateCompositeExpression(original, ImmutableArray.Create(child), readsMemoryOverride, writesMemoryOverride: null, containsCallOverride: null, role);

        private static GimpleOperandInfo CreateCompositeExpression(GimpleValue original, GimpleOperandInfo left, GimpleOperandInfo right, bool? readsMemoryOverride = null, GimpleOperandRole role = GimpleOperandRole.Value)
            => CreateCompositeExpression(original, ImmutableArray.Create(left, right), readsMemoryOverride, writesMemoryOverride: null, containsCallOverride: null, role);

        private static GimpleOperandInfo CreateCompositeExpression(
            GimpleValue original,
            ImmutableArray<GimpleOperandInfo> children,
            bool? readsMemoryOverride,
            bool? writesMemoryOverride,
            bool? containsCallOverride,
            GimpleOperandRole role)
        {
            var readsMemory = children.Any(static child => child.ReadsMemory);
            var writesMemory = children.Any(static child => child.WritesMemory);
            var containsCall = children.Any(static child => child.ContainsCall);

            if (readsMemoryOverride.HasValue)
                readsMemory = readsMemoryOverride.Value || readsMemory;
            if (writesMemoryOverride.HasValue)
                writesMemory = writesMemoryOverride.Value || writesMemory;
            if (containsCallOverride.HasValue)
                containsCall = containsCallOverride.Value || containsCall;

            return new GimpleOperandInfo(original, name: null, children, readsMemory, writesMemory, containsCall, role);
        }

        private void AddPhiInputsToSuccessors(ControlFlowBlock block)
        {
            foreach (var successor in block.UniqueSuccessors)
            {
                if (!_phisByBlock.TryGetValue(successor, out var phis))
                    continue;

                foreach (var phi in phis)
                    phi.SetInput(block, Peek(phi.Variable));
            }
        }

        private GimpleDefinition PushDefinition(
            GimpleVariable variable,
            GimpleDefinitionKind kind,
            ControlFlowBlock? block,
            GimpleStatement? statement,
            GimplePlace? target,
            ParameterSymbol? parameter)
        {
            var name = CreateName(variable, isUndefined: false);
            _stacks[variable].Push(name);
            var definition = new GimpleDefinition(name, kind, block, statement, target, parameter);
            _definitions.Add(definition);
            return definition;
        }

        private GimpleName CreateName(GimpleVariable variable, bool isUndefined)
        {
            var version = _nextVersions[variable];
            _nextVersions[variable] = version + 1;
            return new GimpleName(variable, version, isUndefined);
        }

        private GimpleName Peek(GimpleVariable variable)
            => _stacks[variable].Peek();

        private bool TryGetVariable(GimpleValue value, out GimpleVariable variable)
        {
            if (TryCreateKey(value, out var key) && _variablesByKey.TryGetValue(key, out variable!))
                return true;

            variable = null!;
            return false;
        }

        private static bool TryCreateKey(GimpleValue value, out GimpleVariableKey key)
        {
            switch (value)
            {
                case GimpleSymbolValue { Symbol: VariableSymbol or ParameterSymbol } symbolValue:
                    key = GimpleVariableKey.FromSymbol(symbolValue.Symbol);
                    return true;

                case GimpleTemporaryValue temporary:
                    key = GimpleVariableKey.FromTemporary(temporary);
                    return true;

                default:
                    key = default;
                    return false;
            }
        }

        private static StorageClass GetStorageClass(Symbol symbol)
        {
            return symbol is VariableSymbol variable
                ? variable.StorageClass
                : StorageClass.Auto;
        }

        private static bool IsMemoryBackedDirectRead(GimpleSymbolValue value)
            => value.Symbol is VariableSymbol or ParameterSymbol;

        private sealed class CandidateInfo
        {
            public GimpleVariableKey Key { get; }
            public QualifiedType Type { get; set; }
            public string Name { get; }
            public StorageClass StorageClass { get; set; }

            public CandidateInfo(GimpleVariableKey key, QualifiedType type, string name, StorageClass storageClass)
            {
                Key = key;
                Type = type;
                Name = name ?? string.Empty;
                StorageClass = storageClass;
            }
        }

        private sealed class PhiBuilder
        {
            private readonly Dictionary<ControlFlowBlock, GimpleName> _inputs = new();

            public int Ordinal { get; }
            public ControlFlowBlock Block { get; }
            public GimpleVariable Variable { get; }
            public GimpleName? Result { get; set; }

            public PhiBuilder(int ordinal, ControlFlowBlock block, GimpleVariable variable)
            {
                if (ordinal < 0)
                    throw new ArgumentOutOfRangeException(nameof(ordinal));

                Ordinal = ordinal;
                Block = block ?? throw new ArgumentNullException(nameof(block));
                Variable = variable ?? throw new ArgumentNullException(nameof(variable));
            }

            public void SetInput(ControlFlowBlock predecessor, GimpleName name)
                => _inputs[predecessor] = name;

            public GimplePhi Build(GimpleName undefinedName, List<GimpleProblem> problems)
            {
                var operands = ImmutableArray.CreateBuilder<GimplePhiOperand>(Block.Predecessors.Length);
                foreach (var edge in Block.Predecessors)
                {
                    if (!_inputs.TryGetValue(edge.Source, out var value))
                    {
                        value = undefinedName;
                        if (edge.Source.IsReachable)
                        {
                            problems.Add(new GimpleProblem(
                                GimpleProblemKind.MissingPhiInput,
                                Block,
                                $"Missing GIMPLE phi input for {Variable.Name} from {edge.Source}."));
                        }
                    }

                    operands.Add(new GimplePhiOperand(edge, value));
                }

                return new GimplePhi(Ordinal, Block, Variable, Result!, operands.ToImmutable());
            }
        }
    }

    internal readonly struct GimpleVariableKey : IEquatable<GimpleVariableKey>
    {
        public GimpleVariableKind Kind { get; }
        public Symbol? Symbol { get; }
        public GimpleTemporaryValue? Temporary { get; }

        private GimpleVariableKey(GimpleVariableKind kind, Symbol? symbol, GimpleTemporaryValue? temporary)
        {
            Kind = kind;
            Symbol = symbol;
            Temporary = temporary;
        }

        public static GimpleVariableKey FromSymbol(Symbol symbol)
            => new GimpleVariableKey(GimpleVariableKind.Symbol, symbol ?? throw new ArgumentNullException(nameof(symbol)), temporary: null);

        public static GimpleVariableKey FromTemporary(GimpleTemporaryValue temporary)
            => new GimpleVariableKey(GimpleVariableKind.Temporary, symbol: null, temporary ?? throw new ArgumentNullException(nameof(temporary)));

        public bool Equals(GimpleVariableKey other)
            => Kind == other.Kind && ReferenceEquals(Symbol, other.Symbol) && ReferenceEquals(Temporary, other.Temporary);

        public override bool Equals(object? obj)
            => obj is GimpleVariableKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)Kind;
                hash = (hash * 397) ^ (Symbol is null ? 0 : RuntimeHelpers.GetHashCode(Symbol));
                hash = (hash * 397) ^ (Temporary is null ? 0 : RuntimeHelpers.GetHashCode(Temporary));
                return hash;
            }
        }
    }
}
