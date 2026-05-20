using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

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
        public ValueNumberingOptions ValueNumbering { get; }

        public SsaOptions(
            bool trackMemory = true,
            bool promoteTemporaries = true,
            bool promoteAddressTakenVariables = false,
            bool promoteAggregateVariables = false,
            bool promoteVolatileVariables = false,
            ValueNumberingOptions? valueNumbering = null)
        {
            TrackMemory = trackMemory;
            PromoteTemporaries = promoteTemporaries;
            PromoteAddressTakenVariables = promoteAddressTakenVariables;
            PromoteAggregateVariables = promoteAggregateVariables;
            PromoteVolatileVariables = promoteVolatileVariables;
            ValueNumbering = valueNumbering ?? ValueNumberingOptions.Default;
        }
    }

    public enum SsaVariableKind : byte
    {
        Symbol,
        Temporary,
        Memory,
    }

    public enum SsaDefinitionKind : byte
    {
        Undefined,
        Entry,
        Phi,
        Statement,
        MemoryStatement,
    }

    public enum SsaUseKind : byte
    {
        Value,
        Address,
        Memory,
    }

    public enum SsaExpressionRole : byte
    {
        Value,
        Address,
    }

    [Flags]
    public enum SsaInstructionFlags : byte
    {
        None = 0,
        ReadsMemory = 1,
        WritesMemory = 2,
        ContainsCall = 4,
    }

    public enum SsaProblemKind : byte
    {
        ControlFlowProblem,
        MissingPhiInput,
    }

    public sealed class SsaGraph
    {
        public ControlFlowGraph ControlFlowGraph { get; }
        public SemanticModel SemanticModel => ControlFlowGraph.SemanticModel;
        public GimpleTree GimpleTree => ControlFlowGraph.GimpleTree;
        public ImmutableArray<SsaFunction> Functions { get; }
        public ImmutableArray<SsaProblem> Problems { get; }

        private SsaGraph(ControlFlowGraph controlFlowGraph, ImmutableArray<SsaFunction> functions)
        {
            ControlFlowGraph = controlFlowGraph ?? throw new ArgumentNullException(nameof(controlFlowGraph));
            Functions = functions.IsDefault ? ImmutableArray<SsaFunction>.Empty : functions;

            var problems = ImmutableArray.CreateBuilder<SsaProblem>();
            foreach (var problem in controlFlowGraph.Problems)
                problems.Add(SsaProblem.FromControlFlowProblem(problem));
            foreach (var function in Functions)
                problems.AddRange(function.Problems);
            Problems = problems.ToImmutable();
        }

        public static SsaGraph Build(SemanticModel semanticModel, SsaOptions? options = null)
        {
            if (semanticModel is null)
                throw new ArgumentNullException(nameof(semanticModel));

            return Build(ControlFlowGraph.Build(semanticModel), options);
        }

        public static SsaGraph Build(GimpleTree gimpleTree, SsaOptions? options = null)
        {
            if (gimpleTree is null)
                throw new ArgumentNullException(nameof(gimpleTree));

            return Build(ControlFlowGraph.Build(gimpleTree), options);
        }

        public static SsaGraph Build(ControlFlowGraph controlFlowGraph, SsaOptions? options = null)
        {
            if (controlFlowGraph is null)
                throw new ArgumentNullException(nameof(controlFlowGraph));

            options ??= SsaOptions.Default;
            var functions = ImmutableArray.CreateBuilder<SsaFunction>();
            foreach (var function in controlFlowGraph.Functions)
                functions.Add(SsaFunctionBuilder.Build(function, options));

            return new SsaGraph(controlFlowGraph, functions.ToImmutable());
        }
    }

    public sealed class SsaFunction
    {
        private readonly Dictionary<ControlFlowBlock, SsaBlock> _blocksByControlFlowBlock;
        private readonly Dictionary<SsaVariable, SsaName> _undefinedNames;
        private readonly Dictionary<SsaName, SsaDefinition> _definitionsByName;

        public ControlFlowFunction ControlFlowFunction { get; }
        public GimpleFunctionDefinition Function => ControlFlowFunction.Function;
        public FunctionSymbol? Symbol => ControlFlowFunction.Symbol;
        public SsaVariable? MemoryVariable { get; }
        public ImmutableArray<SsaVariable> Variables { get; }
        public ImmutableArray<SsaBlock> Blocks { get; }
        public ImmutableArray<SsaDefinition> Definitions { get; }
        public ImmutableArray<SsaUse> Uses { get; }
        public ImmutableArray<SsaProblem> Problems { get; }
        public SsaValueNumbering ValueNumbering { get; }

        internal SsaFunction(
            ControlFlowFunction controlFlowFunction,
            SsaVariable? memoryVariable,
            ImmutableArray<SsaVariable> variables,
            ImmutableArray<SsaBlock> blocks,
            ImmutableArray<SsaDefinition> definitions,
            ImmutableArray<SsaUse> uses,
            ImmutableArray<SsaProblem> problems,
            Dictionary<SsaVariable, SsaName> undefinedNames,
            ValueNumberingOptions valueNumberingOptions)
        {
            ControlFlowFunction = controlFlowFunction ?? throw new ArgumentNullException(nameof(controlFlowFunction));
            MemoryVariable = memoryVariable;
            Variables = variables.IsDefault ? ImmutableArray<SsaVariable>.Empty : variables;
            Blocks = blocks.IsDefault ? ImmutableArray<SsaBlock>.Empty : blocks;
            Definitions = definitions.IsDefault ? ImmutableArray<SsaDefinition>.Empty : definitions;
            Uses = uses.IsDefault ? ImmutableArray<SsaUse>.Empty : uses;
            Problems = problems.IsDefault ? ImmutableArray<SsaProblem>.Empty : problems;
            _undefinedNames = undefinedNames is null
                ? new Dictionary<SsaVariable, SsaName>()
                : new Dictionary<SsaVariable, SsaName>(undefinedNames);
            _blocksByControlFlowBlock = Blocks.ToDictionary(static block => block.ControlFlowBlock);
            _definitionsByName = Definitions.ToDictionary(static definition => definition.Name);
            ValueNumbering = SsaValueNumbering.Build(this, valueNumberingOptions);
        }

        public bool TryGetBlock(ControlFlowBlock controlFlowBlock, out SsaBlock? block)
        {
            if (controlFlowBlock is null)
                throw new ArgumentNullException(nameof(controlFlowBlock));

            return _blocksByControlFlowBlock.TryGetValue(controlFlowBlock, out block);
        }

        public bool TryGetDefinition(SsaName name, out SsaDefinition? definition)
        {
            if (name is null)
                throw new ArgumentNullException(nameof(name));

            return _definitionsByName.TryGetValue(name, out definition);
        }

        public SsaName GetUndefinedName(SsaVariable variable)
        {
            if (variable is null)
                throw new ArgumentNullException(nameof(variable));

            return _undefinedNames[variable];
        }

        public override string ToString()
            => Symbol?.Name ?? "<anonymous-function>";
    }

    public sealed class SsaVariable
    {
        public int Ordinal { get; }
        public SsaVariableKind Kind { get; }
        public Symbol? Symbol { get; }
        public GimpleTemporaryValue? Temporary { get; }
        public QualifiedType Type { get; }
        public string Name { get; }

        internal SsaVariable(
            int ordinal,
            SsaVariableKind kind,
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

    public sealed class SsaName
    {
        public SsaVariable Variable { get; }
        public int Version { get; }
        public QualifiedType Type => Variable.Type;
        public bool IsUndefined { get; }

        internal SsaName(SsaVariable variable, int version, bool isUndefined)
        {
            if (version < 0)
                throw new ArgumentOutOfRangeException(nameof(version));

            Variable = variable ?? throw new ArgumentNullException(nameof(variable));
            Version = version;
            IsUndefined = isUndefined;
        }

        public override string ToString()
            => IsUndefined
                ? $"{Variable.Name}_undef"
                : $"{Variable.Name}_{Version.ToString(CultureInfo.InvariantCulture)}";
    }

    public sealed class SsaDefinition
    {
        public SsaName Name { get; }
        public SsaDefinitionKind Kind { get; }
        public ControlFlowBlock? Block { get; }
        public GimpleStatement? Statement { get; }
        public GimplePlace? Target { get; }
        public ParameterSymbol? Parameter { get; }

        internal SsaDefinition(
            SsaName name,
            SsaDefinitionKind kind,
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
        }

        public override string ToString()
            => Kind == SsaDefinitionKind.Phi
                ? $"{Name} = phi"
                : $"{Name} = {Kind}";
    }

    public sealed class SsaUse
    {
        public SsaName Name { get; }
        public SsaUseKind Kind { get; }
        public ControlFlowBlock Block { get; }
        public GimpleStatement? Statement { get; }
        public GimpleValue? Value { get; }

        internal SsaUse(
            SsaName name,
            SsaUseKind kind,
            ControlFlowBlock block,
            GimpleStatement? statement,
            GimpleValue? value)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Kind = kind;
            Block = block ?? throw new ArgumentNullException(nameof(block));
            Statement = statement;
            Value = value;
        }

        public override string ToString()
            => $"{Name} ({Kind})";
    }

    public sealed class SsaPhi
    {
        public int Ordinal { get; }
        public ControlFlowBlock Block { get; }
        public SsaVariable Variable { get; }
        public SsaName Result { get; }
        public ImmutableArray<SsaPhiOperand> Operands { get; }

        internal SsaPhi(
            int ordinal,
            ControlFlowBlock block,
            SsaVariable variable,
            SsaName result,
            ImmutableArray<SsaPhiOperand> operands)
        {
            if (ordinal < 0)
                throw new ArgumentOutOfRangeException(nameof(ordinal));

            Ordinal = ordinal;
            Block = block ?? throw new ArgumentNullException(nameof(block));
            Variable = variable ?? throw new ArgumentNullException(nameof(variable));
            Result = result ?? throw new ArgumentNullException(nameof(result));
            Operands = operands.IsDefault ? ImmutableArray<SsaPhiOperand>.Empty : operands;
        }

        public override string ToString()
            => $"{Result} = phi({string.Join(", ", Operands.Select(static operand => operand.Value.ToString()))})";
    }

    public readonly struct SsaPhiOperand
    {
        public ControlFlowBlock Predecessor { get; }
        public SsaName Value { get; }

        public SsaPhiOperand(ControlFlowBlock predecessor, SsaName value)
        {
            Predecessor = predecessor ?? throw new ArgumentNullException(nameof(predecessor));
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public override string ToString()
            => $"{Predecessor}: {Value}";
    }

    public sealed class SsaExpression
    {
        public GimpleValue Original { get; }
        public SsaName? Name { get; }
        public ImmutableArray<SsaExpression> Children { get; }
        public SsaExpressionRole Role { get; }
        public bool IsAddress => Role == SsaExpressionRole.Address;
        public bool ReadsMemory { get; }
        public bool WritesMemory { get; }
        public bool ContainsCall { get; }

        internal SsaExpression(
            GimpleValue original,
            SsaName? name,
            ImmutableArray<SsaExpression> children,
            bool readsMemory,
            bool writesMemory,
            bool containsCall,
            SsaExpressionRole role = SsaExpressionRole.Value)
        {
            Original = original ?? throw new ArgumentNullException(nameof(original));
            Name = name;
            Children = children.IsDefault ? ImmutableArray<SsaExpression>.Empty : children;
            Role = role;
            ReadsMemory = readsMemory;
            WritesMemory = writesMemory;
            ContainsCall = containsCall;
        }

        public override string ToString()
            => Name?.ToString() ?? Original.ToString() ?? Original.Kind.ToString();
    }

    public sealed class SsaInstruction
    {
        public int Ordinal { get; }
        public ControlFlowBlock Block { get; }
        public GimpleStatement Statement { get; }
        public ImmutableArray<SsaExpression> Expressions { get; }
        public ImmutableArray<SsaUse> Uses { get; }
        public ImmutableArray<SsaDefinition> Definitions { get; }
        public SsaName? MemoryInput { get; }
        public SsaName? MemoryOutput { get; }
        public SsaInstructionFlags Flags { get; }

        internal SsaInstruction(
            int ordinal,
            ControlFlowBlock block,
            GimpleStatement statement,
            ImmutableArray<SsaExpression> expressions,
            ImmutableArray<SsaUse> uses,
            ImmutableArray<SsaDefinition> definitions,
            SsaName? memoryInput,
            SsaName? memoryOutput,
            SsaInstructionFlags flags)
        {
            if (ordinal < 0)
                throw new ArgumentOutOfRangeException(nameof(ordinal));

            Ordinal = ordinal;
            Block = block ?? throw new ArgumentNullException(nameof(block));
            Statement = statement ?? throw new ArgumentNullException(nameof(statement));
            Expressions = expressions.IsDefault ? ImmutableArray<SsaExpression>.Empty : expressions;
            Uses = uses.IsDefault ? ImmutableArray<SsaUse>.Empty : uses;
            Definitions = definitions.IsDefault ? ImmutableArray<SsaDefinition>.Empty : definitions;
            MemoryInput = memoryInput;
            MemoryOutput = memoryOutput;
            Flags = flags;
        }

        public override string ToString()
            => Statement.Kind.ToString();
    }

    public sealed class SsaBlock
    {
        public ControlFlowBlock ControlFlowBlock { get; }
        public ImmutableArray<SsaPhi> Phis { get; }
        public ImmutableArray<SsaInstruction> Instructions { get; }
        public bool IsReachable => ControlFlowBlock.IsReachable;

        internal SsaBlock(
            ControlFlowBlock controlFlowBlock,
            ImmutableArray<SsaPhi> phis,
            ImmutableArray<SsaInstruction> instructions)
        {
            ControlFlowBlock = controlFlowBlock ?? throw new ArgumentNullException(nameof(controlFlowBlock));
            Phis = phis.IsDefault ? ImmutableArray<SsaPhi>.Empty : phis;
            Instructions = instructions.IsDefault ? ImmutableArray<SsaInstruction>.Empty : instructions;
        }

        public override string ToString()
            => ControlFlowBlock.ToString();
    }

    public sealed class SsaProblem
    {
        public SsaProblemKind Kind { get; }
        public ControlFlowBlock? Block { get; }
        public string Message { get; }

        internal SsaProblem(SsaProblemKind kind, ControlFlowBlock? block, string message)
        {
            Kind = kind;
            Block = block;
            Message = message ?? string.Empty;
        }

        internal static SsaProblem FromControlFlowProblem(ControlFlowProblem problem)
        {
            if (problem is null)
                throw new ArgumentNullException(nameof(problem));

            return new SsaProblem(SsaProblemKind.ControlFlowProblem, problem.Block, problem.Message);
        }

        public override string ToString() => Message;
    }


    internal sealed class SsaFunctionBuilder
    {
        private readonly ControlFlowFunction _controlFlowFunction;
        private readonly SsaOptions _options;
        private readonly Dictionary<SsaVariableKey, CandidateInfo> _candidates = new();
        private readonly List<CandidateInfo> _candidateOrder = new();
        private readonly HashSet<SsaVariableKey> _addressTaken = new();
        private readonly Dictionary<SsaVariableKey, SsaVariable> _variablesByKey = new();
        private readonly Dictionary<SsaVariable, HashSet<ControlFlowBlock>> _definitionBlocks = new();
        private readonly Dictionary<SsaVariable, HashSet<ControlFlowBlock>> _liveInBlocks = new();
        private readonly Dictionary<ControlFlowBlock, List<PhiBuilder>> _phisByBlock = new();
        private readonly Dictionary<ControlFlowBlock, List<SsaInstruction>> _instructionsByBlock = new();
        private readonly Dictionary<SsaVariable, Stack<SsaName>> _stacks = new();
        private readonly Dictionary<SsaVariable, int> _nextVersions = new();
        private readonly Dictionary<SsaVariable, SsaName> _undefinedNames = new();
        private readonly Dictionary<ParameterSymbol, SsaVariable> _parameterVariables = new();
        private readonly List<SsaVariable> _variables = new();
        private readonly List<SsaDefinition> _definitions = new();
        private readonly List<SsaUse> _uses = new();
        private readonly List<SsaProblem> _problems = new();

        private SsaVariable? _memoryVariable;
        private ControlFlowBlock? _currentBlock;
        private GimpleStatement? _currentStatement;
        private int _phiOrdinal;
        private int _instructionOrdinal;

        private SsaFunctionBuilder(ControlFlowFunction controlFlowFunction, SsaOptions options)
        {
            _controlFlowFunction = controlFlowFunction ?? throw new ArgumentNullException(nameof(controlFlowFunction));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public static SsaFunction Build(ControlFlowFunction controlFlowFunction, SsaOptions options)
            => new SsaFunctionBuilder(controlFlowFunction, options).Build();

        private SsaFunction Build()
        {
            ScanCandidatesAndAddressTaken();
            CreateVariables();
            CollectDefinitionBlocks();
            ComputeLiveInBlocks();
            InsertPhis();
            InitializeStacks();

            if (_controlFlowFunction.Entry.IsReachable)
                RenameBlock(_controlFlowFunction.Entry);

            var blocks = ImmutableArray.CreateBuilder<SsaBlock>();
            foreach (var block in _controlFlowFunction.RealBlocks)
            {
                var phis = ImmutableArray<SsaPhi>.Empty;
                if (_phisByBlock.TryGetValue(block, out var phiBuilders))
                    phis = phiBuilders.Select(static phi => phi.Build()).ToImmutableArray();

                var instructions = _instructionsByBlock.TryGetValue(block, out var instructionList)
                    ? instructionList.ToImmutableArray()
                    : ImmutableArray<SsaInstruction>.Empty;

                blocks.Add(new SsaBlock(block, phis, instructions));
            }

            return new SsaFunction(
                _controlFlowFunction,
                _memoryVariable,
                _variables.ToImmutableArray(),
                blocks.ToImmutable(),
                _definitions.ToImmutableArray(),
                _uses.ToImmutableArray(),
                _problems.ToImmutableArray(),
                _undefinedNames,
                _options.ValueNumbering);
        }

        private void ScanCandidatesAndAddressTaken()
        {
            var functionType = _controlFlowFunction.Symbol?.FunctionType;
            if (functionType is not null)
            {
                foreach (var parameter in functionType.Parameters)
                    AddCandidate(SsaVariableKey.FromSymbol(parameter), parameter.Type, parameter.Name, StorageClass.Auto);
            }

            foreach (var temporary in _controlFlowFunction.Function.Temporaries)
                AddCandidate(SsaVariableKey.FromTemporary(temporary), temporary.Type, temporary.Name, StorageClass.Auto);

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
                        AddCandidate(SsaVariableKey.FromSymbol(declaration.Symbol), typed.Type, declaration.Symbol.Name, declaration.StorageClass);
                    break;

                case GimpleAssignmentStatement assignment:
                    ScanPlace(assignment.Target, addressContext: false);
                    ScanValue(assignment.Value);
                    break;

                case GimpleZeroInitializeStatement zeroInitialize:
                    ScanPlace(zeroInitialize.Target, addressContext: false);
                    break;

                case GimpleExpressionStatement expressionStatement:
                    ScanValue(expressionStatement.Expression);
                    break;

                case GimpleConditionalGotoStatement conditional:
                    ScanValue(conditional.Condition);
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
                    AddCandidate(SsaVariableKey.FromTemporary(temporary), temporary.Type, temporary.Name, StorageClass.Auto);
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

                case GimpleCallExpression call:
                    ScanValue(call.Callee);
                    foreach (var argument in call.Arguments)
                        ScanValue(argument);
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
                    _addressTaken.Add(SsaVariableKey.FromTemporary(temporary));
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
                    _addressTaken.Add(SsaVariableKey.FromTemporary(temporary));
                    break;

                default:
                    ScanValue(value);
                    break;
            }
        }

        private void AddCandidate(SsaVariableKey key, QualifiedType type, string name, StorageClass storageClass)
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
                _memoryVariable = new SsaVariable(
                    ordinal++,
                    SsaVariableKind.Memory,
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

                var variable = new SsaVariable(
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
            if (candidate.Key.Kind == SsaVariableKind.Temporary && !_options.PromoteTemporaries)
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
            switch (statement)
            {
                case GimpleAssignmentStatement assignment:
                    {
                        if (TryGetVariable(assignment.Target, out var targetVariable))
                            _definitionBlocks[targetVariable].Add(block);
                        else
                            AddMemoryDefinitionBlock(block);

                        if (ExpressionWritesMemory(assignment.Value))
                            AddMemoryDefinitionBlock(block);
                    }
                    break;

                case GimpleZeroInitializeStatement zeroInitialize:
                    {
                        if (TryGetVariable(zeroInitialize.Target, out var targetVariable))
                            _definitionBlocks[targetVariable].Add(block);
                        else
                            AddMemoryDefinitionBlock(block);
                    }
                    break;

                case GimpleExpressionStatement expressionStatement:
                    if (ExpressionWritesMemory(expressionStatement.Expression))
                        AddMemoryDefinitionBlock(block);
                    break;

                case GimpleConditionalGotoStatement conditional:
                    if (ExpressionWritesMemory(conditional.Condition))
                        AddMemoryDefinitionBlock(block);
                    break;

                case GimpleSwitchStatement switchStatement:
                    if (ExpressionWritesMemory(switchStatement.Expression))
                        AddMemoryDefinitionBlock(block);
                    break;

                case GimpleReturnStatement returnStatement when returnStatement.Expression is not null:
                    if (ExpressionWritesMemory(returnStatement.Expression))
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
            var blockUses = new Dictionary<ControlFlowBlock, HashSet<SsaVariable>>();
            var blockDefs = new Dictionary<ControlFlowBlock, HashSet<SsaVariable>>();
            var liveOut = new Dictionary<ControlFlowBlock, HashSet<SsaVariable>>();

            foreach (var variable in _variables)
                _liveInBlocks[variable] = new HashSet<ControlFlowBlock>();

            foreach (var block in _controlFlowFunction.RealBlocks)
            {
                var uses = new HashSet<SsaVariable>();
                var defs = new HashSet<SsaVariable>();

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
                liveOut[block] = new HashSet<SsaVariable>();
            }

            var changed = true;
            while (changed)
            {
                changed = false;

                foreach (var block in _controlFlowFunction.ReversePostOrder.Reverse())
                {
                    if (!blockUses.TryGetValue(block, out var uses))
                        continue;

                    var newOut = new HashSet<SsaVariable>();
                    foreach (var successor in block.UniqueSuccessors)
                    {
                        if (!blockUses.TryGetValue(successor, out _))
                            continue;

                        foreach (var variable in _liveInBlocks.Where(pair => pair.Value.Contains(successor)).Select(static pair => pair.Key))
                            newOut.Add(variable);
                    }

                    var newIn = new HashSet<SsaVariable>(uses);
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
            HashSet<SsaVariable> uses,
            HashSet<SsaVariable> defs)
        {
            switch (statement)
            {
                case GimpleAssignmentStatement assignment:
                    if (TryGetVariable(assignment.Target, out var targetVariable))
                    {
                        CollectValueLiveUses(assignment.Value, uses, defs);
                        defs.Add(targetVariable);
                    }
                    else
                    {
                        CollectPlaceAddressLiveUses(assignment.Target, uses, defs);
                        CollectValueLiveUses(assignment.Value, uses, defs);
                        if (_memoryVariable is not null)
                            defs.Add(_memoryVariable);
                    }

                    if (_memoryVariable is not null && ExpressionWritesMemory(assignment.Value))
                        defs.Add(_memoryVariable);
                    break;

                case GimpleZeroInitializeStatement zeroInitialize:
                    if (TryGetVariable(zeroInitialize.Target, out var zeroTargetVariable))
                    {
                        defs.Add(zeroTargetVariable);
                    }
                    else
                    {
                        CollectPlaceAddressLiveUses(zeroInitialize.Target, uses, defs);
                        if (_memoryVariable is not null)
                            defs.Add(_memoryVariable);
                    }
                    break;

                case GimpleExpressionStatement expressionStatement:
                    CollectValueLiveUses(expressionStatement.Expression, uses, defs);
                    if (_memoryVariable is not null && ExpressionWritesMemory(expressionStatement.Expression))
                        defs.Add(_memoryVariable);
                    break;

                case GimpleConditionalGotoStatement conditional:
                    CollectValueLiveUses(conditional.Condition, uses, defs);
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
            HashSet<SsaVariable> uses,
            HashSet<SsaVariable> defs)
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

                case GimpleCallExpression call:
                    CollectValueLiveUses(call.Callee, uses, defs);
                    foreach (var argument in call.Arguments)
                        CollectValueLiveUses(argument, uses, defs);
                    if (_memoryVariable is not null)
                        MarkLiveUse(_memoryVariable, uses, defs);
                    break;
            }
        }

        private void CollectPlaceAddressLiveUses(
            GimplePlace place,
            HashSet<SsaVariable> uses,
            HashSet<SsaVariable> defs)
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
            HashSet<SsaVariable> uses,
            HashSet<SsaVariable> defs)
        {
            if (value is GimplePlace place)
                CollectPlaceAddressLiveUses(place, uses, defs);
            else
                CollectValueLiveUses(value, uses, defs);
        }

        private static void MarkLiveUse(
            SsaVariable variable,
            HashSet<SsaVariable> uses,
            HashSet<SsaVariable> defs)
        {
            if (!defs.Contains(variable))
                uses.Add(variable);
        }

        private static bool SetEquals<T>(HashSet<T> left, HashSet<T> right)
            => left.Count == right.Count && left.SetEquals(right);

        private bool ShouldInsertPhi(SsaVariable variable, ControlFlowBlock block)
        {
            if (variable.Kind == SsaVariableKind.Memory)
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

        private void AddPhi(ControlFlowBlock block, SsaVariable variable)
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
                _stacks.Add(variable, new Stack<SsaName>());
                _nextVersions.Add(variable, 0);
                var undefined = CreateName(variable, isUndefined: true);
                _undefinedNames.Add(variable, undefined);
                _stacks[variable].Push(undefined);
                _definitions.Add(new SsaDefinition(undefined, SsaDefinitionKind.Undefined, block: null, statement: null, target: null, parameter: null));
            }
        }

        private void RenameBlock(ControlFlowBlock block)
        {
            var pushed = new List<SsaVariable>();

            if (ReferenceEquals(block, _controlFlowFunction.Entry))
            {
                foreach (var parameter in _controlFlowFunction.Symbol?.FunctionType?.Parameters ?? ImmutableArray<ParameterSymbol>.Empty)
                {
                    if (_parameterVariables.TryGetValue(parameter, out var variable))
                    {
                        _ = PushDefinition(variable, SsaDefinitionKind.Entry, block, statement: null, target: null, parameter: parameter);
                        pushed.Add(variable);
                    }
                }

                if (_memoryVariable is not null)
                {
                    _ = PushDefinition(_memoryVariable, SsaDefinitionKind.Entry, block, statement: null, target: null, parameter: null);
                    pushed.Add(_memoryVariable);
                }
            }

            if (_phisByBlock.TryGetValue(block, out var phis))
            {
                foreach (var phi in phis)
                {
                    phi.Result = PushDefinition(phi.Variable, SsaDefinitionKind.Phi, block, statement: null, target: null, parameter: null).Name;
                    pushed.Add(phi.Variable);
                }
            }

            foreach (var statement in block.Statements)
            {
                var instruction = RenameStatement(block, statement, pushed);
                if (!_instructionsByBlock.TryGetValue(block, out var instructions))
                {
                    instructions = new List<SsaInstruction>();
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

        private SsaInstruction RenameStatement(ControlFlowBlock block, GimpleStatement statement, List<SsaVariable> pushed)
        {
            _currentBlock = block;
            _currentStatement = statement;

            var uses = ImmutableArray.CreateBuilder<SsaUse>();
            var definitions = ImmutableArray.CreateBuilder<SsaDefinition>();
            var expressions = ImmutableArray.CreateBuilder<SsaExpression>();
            var flags = SsaInstructionFlags.None;
            bool explicitMemoryWrite = false;

            switch (statement)
            {
                case GimpleAssignmentStatement assignment:
                    if (!TryGetVariable(assignment.Target, out _))
                    {
                        expressions.Add(RewritePlaceAddress(assignment.Target, uses));
                        explicitMemoryWrite = true;
                    }

                    expressions.Add(RewriteValue(assignment.Value, uses));
                    break;

                case GimpleZeroInitializeStatement zeroInitialize:
                    if (!TryGetVariable(zeroInitialize.Target, out _))
                    {
                        expressions.Add(RewritePlaceAddress(zeroInitialize.Target, uses));
                        explicitMemoryWrite = true;
                    }
                    break;

                case GimpleExpressionStatement expressionStatement:
                    expressions.Add(RewriteValue(expressionStatement.Expression, uses));
                    break;

                case GimpleConditionalGotoStatement conditional:
                    expressions.Add(RewriteValue(conditional.Condition, uses));
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
                    flags |= SsaInstructionFlags.ReadsMemory;
                if (expression.WritesMemory)
                    flags |= SsaInstructionFlags.WritesMemory;
                if (expression.ContainsCall)
                    flags |= SsaInstructionFlags.ContainsCall;
            }

            if (explicitMemoryWrite)
                flags |= SsaInstructionFlags.WritesMemory;

            SsaName? memoryInput = null;
            if (_memoryVariable is not null && (flags & (SsaInstructionFlags.ReadsMemory | SsaInstructionFlags.WritesMemory)) != 0)
            {
                memoryInput = Peek(_memoryVariable);
                var memoryUse = new SsaUse(memoryInput, SsaUseKind.Memory, block, statement, value: null);
                uses.Add(memoryUse);
                _uses.Add(memoryUse);
            }

            switch (statement)
            {
                case GimpleAssignmentStatement assignment when TryGetVariable(assignment.Target, out var targetVariable):
                    {
                        var definition = PushDefinition(targetVariable, SsaDefinitionKind.Statement, block, statement, assignment.Target, parameter: null);
                        definitions.Add(definition);
                        pushed.Add(targetVariable);
                    }
                    break;

                case GimpleZeroInitializeStatement zeroInitialize when TryGetVariable(zeroInitialize.Target, out var targetVariable):
                    {
                        var definition = PushDefinition(targetVariable, SsaDefinitionKind.Statement, block, statement, zeroInitialize.Target, parameter: null);
                        definitions.Add(definition);
                        pushed.Add(targetVariable);
                    }
                    break;
            }

            SsaName? memoryOutput = null;
            if (_memoryVariable is not null && (flags & SsaInstructionFlags.WritesMemory) != 0)
            {
                var memoryDefinition = PushDefinition(_memoryVariable, SsaDefinitionKind.MemoryStatement, block, statement, target: null, parameter: null);
                memoryOutput = memoryDefinition.Name;
                definitions.Add(memoryDefinition);
                pushed.Add(_memoryVariable);
            }

            var instruction = new SsaInstruction(
                _instructionOrdinal++,
                block,
                statement,
                expressions.ToImmutable(),
                uses.ToImmutable(),
                definitions.ToImmutable(),
                memoryInput,
                memoryOutput,
                flags);

            _currentBlock = null;
            _currentStatement = null;
            return instruction;
        }

        private SsaExpression RewriteValue(GimpleValue value, ImmutableArray<SsaUse>.Builder uses)
        {
            switch (value)
            {
                case GimpleSymbolValue symbolValue when TryGetVariable(symbolValue, out var variable):
                    return CreateNameExpression(symbolValue, variable, uses, SsaUseKind.Value);

                case GimpleTemporaryValue temporary when TryGetVariable(temporary, out var variable):
                    return CreateNameExpression(temporary, variable, uses, SsaUseKind.Value);

                case GimpleSymbolValue symbolValue:
                    return new SsaExpression(symbolValue, name: null, ImmutableArray<SsaExpression>.Empty, IsMemoryBackedDirectRead(symbolValue), writesMemory: false, containsCall: false);

                case GimpleTemporaryValue temporary:
                    return new SsaExpression(temporary, name: null, ImmutableArray<SsaExpression>.Empty, readsMemory: true, writesMemory: false, containsCall: false);

                case GimpleConstantValue constant:
                    return new SsaExpression(constant, name: null, ImmutableArray<SsaExpression>.Empty, readsMemory: false, writesMemory: false, containsCall: false);

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

                case GimpleCallExpression call:
                    {
                        var children = ImmutableArray.CreateBuilder<SsaExpression>();
                        children.Add(RewriteValue(call.Callee, uses));
                        foreach (var argument in call.Arguments)
                            children.Add(RewriteValue(argument, uses));

                        return CreateCompositeExpression(call, children.ToImmutable(), readsMemoryOverride: true, writesMemoryOverride: true, containsCallOverride: true, role: SsaExpressionRole.Value);
                    }

                default:
                    return new SsaExpression(value, name: null, ImmutableArray<SsaExpression>.Empty, readsMemory: false, writesMemory: false, containsCall: false);
            }
        }

        private SsaExpression RewritePlaceAddress(GimplePlace place, ImmutableArray<SsaUse>.Builder uses)
        {
            switch (place)
            {
                case GimpleSymbolValue symbolValue when TryGetVariable(symbolValue, out var variable):
                    return CreateNameExpression(symbolValue, variable, uses, SsaUseKind.Address);

                case GimpleTemporaryValue temporary when TryGetVariable(temporary, out var variable):
                    return CreateNameExpression(temporary, variable, uses, SsaUseKind.Address);

                case GimpleSymbolValue symbolValue:
                    return new SsaExpression(symbolValue, name: null, ImmutableArray<SsaExpression>.Empty, readsMemory: false, writesMemory: false, containsCall: false, role: SsaExpressionRole.Address);

                case GimpleTemporaryValue temporary:
                    return new SsaExpression(temporary, name: null, ImmutableArray<SsaExpression>.Empty, readsMemory: false, writesMemory: false, containsCall: false, role: SsaExpressionRole.Address);

                case GimpleIndirectExpression indirect:
                    return CreateCompositeExpression(indirect, RewriteValue(indirect.Address, uses), role: SsaExpressionRole.Address);

                case GimpleElementAccessExpression elementAccess:
                    return elementAccess.Index is null
                        ? CreateCompositeExpression(elementAccess, RewriteAddressBase(elementAccess.Expression, uses), role: SsaExpressionRole.Address)
                        : CreateCompositeExpression(elementAccess, RewriteAddressBase(elementAccess.Expression, uses), RewriteValue(elementAccess.Index, uses), role: SsaExpressionRole.Address);

                case GimpleMemberAccessExpression memberAccess:
                    if (memberAccess.OperatorToken.Kind == SyntaxKind.ArrowToken)
                        return CreateCompositeExpression(memberAccess, RewriteValue(memberAccess.Expression, uses), role: SsaExpressionRole.Address);

                    return CreateCompositeExpression(memberAccess, RewriteAddressBase(memberAccess.Expression, uses), role: SsaExpressionRole.Address);

                default:
                    return RewriteValue(place, uses);
            }
        }

        private SsaExpression RewriteAddressBase(GimpleValue value, ImmutableArray<SsaUse>.Builder uses)
        {
            return value is GimplePlace place
                ? RewritePlaceAddress(place, uses)
                : RewriteValue(value, uses);
        }

        private SsaExpression CreateNameExpression(GimpleValue original, SsaVariable variable, ImmutableArray<SsaUse>.Builder uses, SsaUseKind kind)
        {
            var name = Peek(variable);
            var use = new SsaUse(name, kind, _currentBlock!, _currentStatement, original);
            uses.Add(use);
            _uses.Add(use);
            var role = kind == SsaUseKind.Address ? SsaExpressionRole.Address : SsaExpressionRole.Value;
            return new SsaExpression(original, name, ImmutableArray<SsaExpression>.Empty, readsMemory: false, writesMemory: false, containsCall: false, role);
        }

        private static SsaExpression CreateCompositeExpression(GimpleValue original, params SsaExpression[] children)
            => CreateCompositeExpression(original, children.ToImmutableArray(), readsMemoryOverride: null, writesMemoryOverride: null, containsCallOverride: null, role: SsaExpressionRole.Value);

        private static SsaExpression CreateCompositeExpression(GimpleValue original, SsaExpression child, bool? readsMemoryOverride = null, SsaExpressionRole role = SsaExpressionRole.Value)
            => CreateCompositeExpression(original, ImmutableArray.Create(child), readsMemoryOverride, writesMemoryOverride: null, containsCallOverride: null, role);

        private static SsaExpression CreateCompositeExpression(GimpleValue original, SsaExpression left, SsaExpression right, bool? readsMemoryOverride = null, SsaExpressionRole role = SsaExpressionRole.Value)
            => CreateCompositeExpression(original, ImmutableArray.Create(left, right), readsMemoryOverride, writesMemoryOverride: null, containsCallOverride: null, role);

        private static SsaExpression CreateCompositeExpression(
            GimpleValue original,
            ImmutableArray<SsaExpression> children,
            bool? readsMemoryOverride,
            bool? writesMemoryOverride,
            bool? containsCallOverride,
            SsaExpressionRole role)
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

            return new SsaExpression(original, name: null, children, readsMemory, writesMemory, containsCall, role);
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

        private SsaDefinition PushDefinition(
            SsaVariable variable,
            SsaDefinitionKind kind,
            ControlFlowBlock? block,
            GimpleStatement? statement,
            GimplePlace? target,
            ParameterSymbol? parameter)
        {
            var name = CreateName(variable, isUndefined: false);
            _stacks[variable].Push(name);
            var definition = new SsaDefinition(name, kind, block, statement, target, parameter);
            _definitions.Add(definition);
            return definition;
        }

        private SsaName CreateName(SsaVariable variable, bool isUndefined)
        {
            var version = _nextVersions[variable];
            _nextVersions[variable] = version + 1;
            return new SsaName(variable, version, isUndefined);
        }

        private SsaName Peek(SsaVariable variable)
            => _stacks[variable].Peek();

        private bool TryGetVariable(GimpleValue value, out SsaVariable variable)
        {
            if (TryCreateKey(value, out var key) && _variablesByKey.TryGetValue(key, out variable!))
                return true;

            variable = null!;
            return false;
        }

        private static bool TryCreateKey(GimpleValue value, out SsaVariableKey key)
        {
            switch (value)
            {
                case GimpleSymbolValue { Symbol: VariableSymbol or ParameterSymbol } symbolValue:
                    key = SsaVariableKey.FromSymbol(symbolValue.Symbol);
                    return true;

                case GimpleTemporaryValue temporary:
                    key = SsaVariableKey.FromTemporary(temporary);
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

        private static bool ExpressionWritesMemory(GimpleValue value)
        {
            switch (value)
            {
                case GimpleCallExpression:
                    return true;

                case GimpleUnaryExpression unary:
                    return ExpressionWritesMemory(unary.Operand);

                case GimpleBinaryExpression binary:
                    return ExpressionWritesMemory(binary.Left) || ExpressionWritesMemory(binary.Right);

                case GimpleConversionExpression conversion:
                    return ExpressionWritesMemory(conversion.Operand);

                case GimpleCastExpression cast:
                    return ExpressionWritesMemory(cast.Operand);

                case GimpleAddressOfExpression addressOf:
                    return PlaceAddressWritesMemory(addressOf.Target);

                case GimpleIndirectExpression indirect:
                    return ExpressionWritesMemory(indirect.Address);

                case GimpleElementAccessExpression elementAccess:
                    return ExpressionWritesMemory(elementAccess.Expression) ||
                           (elementAccess.Index is not null && ExpressionWritesMemory(elementAccess.Index));

                case GimpleMemberAccessExpression memberAccess:
                    return ExpressionWritesMemory(memberAccess.Expression);

                default:
                    return false;
            }
        }

        private static bool PlaceAddressWritesMemory(GimplePlace place)
        {
            switch (place)
            {
                case GimpleIndirectExpression indirect:
                    return ExpressionWritesMemory(indirect.Address);

                case GimpleElementAccessExpression elementAccess:
                    return ExpressionWritesMemory(elementAccess.Expression) ||
                           (elementAccess.Index is not null && ExpressionWritesMemory(elementAccess.Index));

                case GimpleMemberAccessExpression memberAccess:
                    return ExpressionWritesMemory(memberAccess.Expression);

                default:
                    return false;
            }
        }

        private sealed class CandidateInfo
        {
            public SsaVariableKey Key { get; }
            public QualifiedType Type { get; set; }
            public string Name { get; }
            public StorageClass StorageClass { get; set; }

            public CandidateInfo(SsaVariableKey key, QualifiedType type, string name, StorageClass storageClass)
            {
                Key = key;
                Type = type;
                Name = name ?? string.Empty;
                StorageClass = storageClass;
            }
        }

        private sealed class PhiBuilder
        {
            private readonly Dictionary<ControlFlowBlock, SsaName> _inputs = new();

            public int Ordinal { get; }
            public ControlFlowBlock Block { get; }
            public SsaVariable Variable { get; }
            public SsaName? Result { get; set; }

            public PhiBuilder(int ordinal, ControlFlowBlock block, SsaVariable variable)
            {
                if (ordinal < 0)
                    throw new ArgumentOutOfRangeException(nameof(ordinal));

                Ordinal = ordinal;
                Block = block ?? throw new ArgumentNullException(nameof(block));
                Variable = variable ?? throw new ArgumentNullException(nameof(variable));
            }

            public void SetInput(ControlFlowBlock predecessor, SsaName name)
                => _inputs[predecessor] = name;

            public SsaPhi Build()
            {
                var operands = ImmutableArray.CreateBuilder<SsaPhiOperand>();
                foreach (var predecessor in Block.UniquePredecessors)
                {
                    if (_inputs.TryGetValue(predecessor, out var value))
                        operands.Add(new SsaPhiOperand(predecessor, value));
                }

                return new SsaPhi(Ordinal, Block, Variable, Result!, operands.ToImmutable());
            }
        }
    }

    internal readonly struct SsaVariableKey : IEquatable<SsaVariableKey>
    {
        public SsaVariableKind Kind { get; }
        public Symbol? Symbol { get; }
        public GimpleTemporaryValue? Temporary { get; }

        private SsaVariableKey(SsaVariableKind kind, Symbol? symbol, GimpleTemporaryValue? temporary)
        {
            Kind = kind;
            Symbol = symbol;
            Temporary = temporary;
        }

        public static SsaVariableKey FromSymbol(Symbol symbol)
            => new SsaVariableKey(SsaVariableKind.Symbol, symbol ?? throw new ArgumentNullException(nameof(symbol)), temporary: null);

        public static SsaVariableKey FromTemporary(GimpleTemporaryValue temporary)
            => new SsaVariableKey(SsaVariableKind.Temporary, symbol: null, temporary ?? throw new ArgumentNullException(nameof(temporary)));

        public bool Equals(SsaVariableKey other)
            => Kind == other.Kind && ReferenceEquals(Symbol, other.Symbol) && ReferenceEquals(Temporary, other.Temporary);

        public override bool Equals(object? obj)
            => obj is SsaVariableKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)Kind;
                hash = (hash * 397) ^ (Symbol is null ? 0 : RuntimeHelpersShim.GetHashCode(Symbol));
                hash = (hash * 397) ^ (Temporary is null ? 0 : RuntimeHelpersShim.GetHashCode(Temporary));
                return hash;
            }
        }
    }

    internal static class RuntimeHelpersShim
    {
        public static int GetHashCode(object value)
            => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
    }

}
