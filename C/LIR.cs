using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Cnidaria.C
{
    public sealed class LirOptions
    {
        public static LirOptions Default { get; } = new LirOptions();

        public bool KeepNops { get; }
        public bool EmitValueNumberComments { get; }
        public bool PreserveUnreachableBlocks { get; }

        public LirOptions(
            bool keepNops = false,
            bool emitValueNumberComments = true,
            bool preserveUnreachableBlocks = false)
        {
            KeepNops = keepNops;
            EmitValueNumberComments = emitValueNumberComments;
            PreserveUnreachableBlocks = preserveUnreachableBlocks;
        }
    }

    public enum LirRegisterClass : byte
    {
        Void,
        General,
        Floating,
        Address,
        Aggregate,
        Memory,
        Unknown,
    }

    public enum LirOperandKind : byte
    {
        None,
        Register,
        Immediate,
        Symbol,
        StackSlot,
        Address,
        Label,
        Undefined,
        Void,
    }

    public enum LirAddressKind : byte
    {
        StackSlot,
        Symbol,
        Indirect,
        Element,
        Field,
    }

    public enum LirInstructionKind : ushort
    {
        Nop,
        Parameter,
        Copy,
        ParallelCopy,
        Constant,
        Zero,
        Unary,
        Binary,
        Convert,
        Cast,
        AddressOf,
        Load,
        Store,
        ZeroMemory,
        Call,
        VaStart,
        Jump,
        Branch,
        Switch,
        Return,
        Unreachable,
    }

    public enum LirProblemKind : byte
    {
        MissingTarget,
        UnsupportedNode,
        InvalidAddress,
        PromotedAddressTakenValue,
    }

    public sealed class LirModule
    {
        public SemanticModel SemanticModel { get; }
        public GimpleTree GimpleTree { get; }
        public SsaGraph SsaGraph { get; }
        public ImmutableArray<LirGlobal> Globals { get; }
        public ImmutableArray<LirFunction> Functions { get; }
        public ImmutableArray<LirProblem> Problems { get; }

        private LirModule(
            SsaGraph ssaGraph,
            ImmutableArray<LirGlobal> globals,
            ImmutableArray<LirFunction> functions,
            ImmutableArray<LirProblem> problems)
        {
            SsaGraph = ssaGraph ?? throw new ArgumentNullException(nameof(ssaGraph));
            GimpleTree = ssaGraph.GimpleTree;
            SemanticModel = ssaGraph.SemanticModel;
            Globals = globals.IsDefault ? ImmutableArray<LirGlobal>.Empty : globals;
            Functions = functions.IsDefault ? ImmutableArray<LirFunction>.Empty : functions;
            Problems = problems.IsDefault ? ImmutableArray<LirProblem>.Empty : problems;
        }

        public static LirModule Lower(SemanticModel semanticModel, SsaOptions? ssaOptions = null, LirOptions? options = null)
        {
            if (semanticModel is null)
                throw new ArgumentNullException(nameof(semanticModel));

            return Lower(SsaGraph.Build(semanticModel, ssaOptions), options);
        }

        public static LirModule Lower(GimpleTree gimpleTree, SsaOptions? ssaOptions = null, LirOptions? options = null)
        {
            if (gimpleTree is null)
                throw new ArgumentNullException(nameof(gimpleTree));

            return Lower(SsaGraph.Build(gimpleTree, ssaOptions), options);
        }

        public static LirModule Lower(ControlFlowGraph controlFlowGraph, SsaOptions? ssaOptions = null, LirOptions? options = null)
        {
            if (controlFlowGraph is null)
                throw new ArgumentNullException(nameof(controlFlowGraph));

            return Lower(SsaGraph.Build(controlFlowGraph, ssaOptions), options);
        }

        public static LirModule Lower(SsaGraph ssaGraph, LirOptions? options = null)
        {
            if (ssaGraph is null)
                throw new ArgumentNullException(nameof(ssaGraph));

            options ??= LirOptions.Default;
            var globals = ImmutableArray.CreateBuilder<LirGlobal>();
            var functions = ImmutableArray.CreateBuilder<LirFunction>();
            var problems = ImmutableArray.CreateBuilder<LirProblem>();

            foreach (var member in ssaGraph.GimpleTree.Members)
            {
                if (member is not GimpleGlobalDeclaration global)
                    continue;

                foreach (var declaration in global.Declarators)
                    globals.Add(new LirGlobal(declaration.Symbol, declaration.Type, declaration.StorageClass, declaration.Initializer));
            }

            foreach (var function in ssaGraph.Functions)
            {
                var lowered = LirFunctionBuilder.Lower(function, options, ssaGraph.SemanticModel.Compilation.Options.Target);
                functions.Add(lowered.Function);
                problems.AddRange(lowered.Problems);
            }

            foreach (var ssaProblem in ssaGraph.Problems)
                problems.Add(new LirProblem(LirProblemKind.UnsupportedNode, ssaProblem.Block, null, ssaProblem.Message));

            return new LirModule(ssaGraph, globals.ToImmutable(), functions.ToImmutable(), problems.ToImmutable());
        }

        public override string ToString()
            => LirPrinter.Print(this);
    }

    public sealed class LirGlobal
    {
        public Symbol? Symbol { get; }
        public QualifiedType Type { get; }
        public StorageClass StorageClass { get; }
        public GimpleInitializer? Initializer { get; }

        public LirGlobal(Symbol? symbol, QualifiedType type, StorageClass storageClass, GimpleInitializer? initializer)
        {
            Symbol = symbol;
            Type = GimpleTypeHelpers.Normalize(type);
            StorageClass = storageClass;
            Initializer = initializer;
        }
    }

    public sealed class LirFunction
    {
        public SsaFunction SsaFunction { get; }
        public FunctionSymbol? Symbol => SsaFunction.Symbol;
        public LirBlock Entry { get; }
        public ImmutableArray<LirVirtualRegister> VirtualRegisters { get; }
        public ImmutableArray<LirStackSlot> StackSlots { get; }
        public ImmutableArray<LirBlock> Blocks { get; }
        public ImmutableArray<LirProblem> Problems { get; }

        internal LirFunction(
            SsaFunction ssaFunction,
            LirBlock entry,
            ImmutableArray<LirVirtualRegister> virtualRegisters,
            ImmutableArray<LirStackSlot> stackSlots,
            ImmutableArray<LirBlock> blocks,
            ImmutableArray<LirProblem> problems)
        {
            SsaFunction = ssaFunction ?? throw new ArgumentNullException(nameof(ssaFunction));
            Entry = entry ?? throw new ArgumentNullException(nameof(entry));
            VirtualRegisters = virtualRegisters.IsDefault ? ImmutableArray<LirVirtualRegister>.Empty : virtualRegisters;
            StackSlots = stackSlots.IsDefault ? ImmutableArray<LirStackSlot>.Empty : stackSlots;
            Blocks = blocks.IsDefault ? ImmutableArray<LirBlock>.Empty : blocks;
            Problems = problems.IsDefault ? ImmutableArray<LirProblem>.Empty : problems;
        }

        public override string ToString()
            => LirPrinter.Print(this);
    }

    public sealed class LirBlock
    {
        private ImmutableArray<LirInstruction> _instructions;

        public int Ordinal { get; }
        public string Name { get; }
        public ControlFlowBlock? SourceBlock { get; }
        public bool IsEdgeSplit { get; }
        public ImmutableArray<LirInstruction> Instructions => _instructions;

        internal LirBlock(int ordinal, string name, ControlFlowBlock? sourceBlock, bool isEdgeSplit)
        {
            if (ordinal < 0)
                throw new ArgumentOutOfRangeException(nameof(ordinal));

            Ordinal = ordinal;
            Name = string.IsNullOrWhiteSpace(name)
                ? "bb" + ordinal.ToString(CultureInfo.InvariantCulture)
                : name;
            SourceBlock = sourceBlock;
            IsEdgeSplit = isEdgeSplit;
            _instructions = ImmutableArray<LirInstruction>.Empty;
        }

        internal void SetInstructions(ImmutableArray<LirInstruction> instructions)
            => _instructions = instructions.IsDefault ? ImmutableArray<LirInstruction>.Empty : instructions;

        public override string ToString() => Name;
    }

    public sealed class LirVirtualRegister
    {
        public int Ordinal { get; }
        public string Name { get; }
        public QualifiedType Type { get; }
        public LirRegisterClass RegisterClass { get; }
        public SsaName? SourceName { get; }
        public ValueNumber? ValueNumber { get; }
        public bool IsCompilerTemporary => SourceName is null;

        internal LirVirtualRegister(int ordinal, QualifiedType type, LirRegisterClass registerClass, SsaName? sourceName, ValueNumber? valueNumber)
        {
            if (ordinal < 0)
                throw new ArgumentOutOfRangeException(nameof(ordinal));

            Ordinal = ordinal;
            Type = GimpleTypeHelpers.Normalize(type);
            RegisterClass = registerClass;
            SourceName = sourceName;
            ValueNumber = valueNumber;
            Name = "%v" + ordinal.ToString(CultureInfo.InvariantCulture);
        }

        public override string ToString() => Name;
    }

    public sealed class LirStackSlot
    {
        public int Ordinal { get; }
        public string Name { get; }
        public QualifiedType Type { get; }
        public int Size { get; }
        public int Alignment { get; }
        public Symbol? Symbol { get; }
        public GimpleTemporaryValue? Temporary { get; }
        public bool IsParameter { get; }
        public StorageClass StorageClass { get; }

        internal LirStackSlot(
            int ordinal,
            string name,
            QualifiedType type,
            int size,
            int alignment,
            Symbol? symbol,
            GimpleTemporaryValue? temporary,
            bool isParameter,
            StorageClass storageClass)
        {
            if (ordinal < 0)
                throw new ArgumentOutOfRangeException(nameof(ordinal));

            Ordinal = ordinal;
            Name = string.IsNullOrWhiteSpace(name)
                ? "slot" + ordinal.ToString(CultureInfo.InvariantCulture)
                : name;
            Type = GimpleTypeHelpers.Normalize(type);
            Size = size < 0 ? 0 : size;
            Alignment = alignment <= 0 ? 1 : alignment;
            Symbol = symbol;
            Temporary = temporary;
            IsParameter = isParameter;
            StorageClass = storageClass;
        }

        public override string ToString()
            => "slot" + Ordinal.ToString(CultureInfo.InvariantCulture);
    }

    public sealed class LirAddress
    {
        public LirAddressKind Kind { get; }
        public QualifiedType ElementType { get; }
        public LirStackSlot? StackSlot { get; }
        public Symbol? Symbol { get; }
        public LirOperand? BaseOperand { get; }
        public LirAddress? BaseAddress { get; }
        public LirOperand? Index { get; }
        public int Scale { get; }
        public int Displacement { get; }
        public FieldSymbol? Field { get; }

        private LirAddress(
            LirAddressKind kind,
            QualifiedType elementType,
            LirStackSlot? stackSlot,
            Symbol? symbol,
            LirOperand? baseOperand,
            LirAddress? baseAddress,
            LirOperand? index,
            int scale,
            int displacement,
            FieldSymbol? field)
        {
            Kind = kind;
            ElementType = GimpleTypeHelpers.Normalize(elementType);
            StackSlot = stackSlot;
            Symbol = symbol;
            BaseOperand = baseOperand;
            BaseAddress = baseAddress;
            Index = index;
            Scale = scale;
            Displacement = displacement;
            Field = field;
        }

        public static LirAddress ForStackSlot(LirStackSlot slot)
        {
            if (slot is null)
                throw new ArgumentNullException(nameof(slot));

            return new LirAddress(LirAddressKind.StackSlot, slot.Type, slot, symbol: null, baseOperand: null, baseAddress: null, index: null, scale: 1, displacement: 0, field: null);
        }

        public static LirAddress ForSymbol(Symbol symbol, QualifiedType type)
        {
            if (symbol is null)
                throw new ArgumentNullException(nameof(symbol));

            return new LirAddress(LirAddressKind.Symbol, type, stackSlot: null, symbol, baseOperand: null, baseAddress: null, index: null, scale: 1, displacement: 0, field: null);
        }

        public static LirAddress Indirect(LirOperand baseOperand, QualifiedType elementType)
        {
            if (baseOperand is null)
                throw new ArgumentNullException(nameof(baseOperand));

            return new LirAddress(LirAddressKind.Indirect, elementType, stackSlot: null, symbol: null, baseOperand, baseAddress: null, index: null, scale: 1, displacement: 0, field: null);
        }

        public static LirAddress Element(LirAddress baseAddress, LirOperand? index, QualifiedType elementType, int scale)
        {
            if (baseAddress is null)
                throw new ArgumentNullException(nameof(baseAddress));

            return new LirAddress(LirAddressKind.Element, elementType, stackSlot: null, symbol: null, baseOperand: null, baseAddress, index, scale <= 0 ? 1 : scale, displacement: 0, field: null);
        }

        public static LirAddress ForField(LirAddress baseAddress, FieldSymbol? field, QualifiedType fieldType, int displacement)
        {
            if (baseAddress is null)
                throw new ArgumentNullException(nameof(baseAddress));

            return new LirAddress(LirAddressKind.Field, fieldType, stackSlot: null, symbol: null, baseOperand: null, baseAddress, index: null, scale: 1, displacement, field);
        }
    }

    public sealed class LirOperand
    {
        public static LirOperand None { get; } = new LirOperand(LirOperandKind.None, new QualifiedType(TypeCatalog.Instance.Void), null, null, null, null, null, null, null);
        public static LirOperand Void { get; } = new LirOperand(LirOperandKind.Void, new QualifiedType(TypeCatalog.Instance.Void), null, null, null, null, null, null, null);

        public LirOperandKind Kind { get; }
        public QualifiedType Type { get; }
        public LirVirtualRegister? Register { get; }
        public object? Immediate { get; }
        public Symbol? Symbol { get; }
        public LirStackSlot? StackSlot { get; }
        public LirAddress? Address { get; }
        public LirBlock? Label { get; }
        public SsaName? UndefinedName { get; }

        private LirOperand(
            LirOperandKind kind,
            QualifiedType type,
            LirVirtualRegister? register,
            object? immediate,
            Symbol? symbol,
            LirStackSlot? stackSlot,
            LirAddress? address,
            LirBlock? label,
            SsaName? undefinedName)
        {
            Kind = kind;
            Type = GimpleTypeHelpers.Normalize(type);
            Register = register;
            Immediate = immediate;
            Symbol = symbol;
            StackSlot = stackSlot;
            Address = address;
            Label = label;
            UndefinedName = undefinedName;
        }

        public static LirOperand ForRegister(LirVirtualRegister register)
        {
            if (register is null)
                throw new ArgumentNullException(nameof(register));

            return new LirOperand(LirOperandKind.Register, register.Type, register, null, null, null, null, null, null);
        }

        public static LirOperand ImmediateValue(object? value, QualifiedType type)
            => new LirOperand(LirOperandKind.Immediate, type, null, value, null, null, null, null, null);

        public static LirOperand ForSymbol(Symbol symbol, QualifiedType type)
        {
            if (symbol is null)
                throw new ArgumentNullException(nameof(symbol));

            return new LirOperand(LirOperandKind.Symbol, type, null, null, symbol, null, null, null, null);
        }

        public static LirOperand ForStackSlot(LirStackSlot slot)
        {
            if (slot is null)
                throw new ArgumentNullException(nameof(slot));

            return new LirOperand(LirOperandKind.StackSlot, slot.Type, null, null, null, slot, null, null, null);
        }

        public static LirOperand ForAddress(LirAddress address)
        {
            if (address is null)
                throw new ArgumentNullException(nameof(address));

            return new LirOperand(LirOperandKind.Address, new QualifiedType(TypeCatalog.Instance.PointerTo(address.ElementType)), null, null, null, null, address, null, null);
        }

        public static LirOperand ForLabel(LirBlock block)
        {
            if (block is null)
                throw new ArgumentNullException(nameof(block));

            return new LirOperand(LirOperandKind.Label, new QualifiedType(TypeCatalog.Instance.Void), null, null, null, null, null, block, null);
        }

        public static LirOperand Undefined(SsaName? name, QualifiedType type)
            => new LirOperand(LirOperandKind.Undefined, type, null, null, null, null, null, null, name);

        public bool ReferencesSameRegister(LirVirtualRegister register)
            => Kind == LirOperandKind.Register && ReferenceEquals(Register, register);
    }

    public readonly struct LirParallelCopy
    {
        public LirVirtualRegister Destination { get; }
        public LirOperand Source { get; }

        public LirParallelCopy(LirVirtualRegister destination, LirOperand source)
        {
            Destination = destination ?? throw new ArgumentNullException(nameof(destination));
            Source = source ?? throw new ArgumentNullException(nameof(source));
        }
    }

    public readonly struct LirSwitchCase
    {
        public LirOperand Value { get; }
        public LirBlock Target { get; }

        public LirSwitchCase(LirOperand value, LirBlock target)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
            Target = target ?? throw new ArgumentNullException(nameof(target));
        }
    }

    public sealed class LirInstruction
    {
        public int Ordinal { get; }
        public LirInstructionKind Kind { get; }
        public LirVirtualRegister? Result { get; }
        public ImmutableArray<LirOperand> Operands { get; }
        public LirAddress? Address { get; }
        public string Operator { get; }
        public GimpleConversionKind? ConversionKind { get; }
        public FunctionType? CallSignature { get; }
        public ImmutableArray<LirParallelCopy> ParallelCopies { get; }
        public ImmutableArray<LirSwitchCase> SwitchCases { get; }
        public LirBlock? Target { get; }
        public LirBlock? TrueTarget { get; }
        public LirBlock? FalseTarget { get; }
        public GimpleStatement? SourceStatement { get; }
        public GimpleValue? SourceValue { get; }
        public SsaInstruction? SourceInstruction { get; }
        public ValueNumber? ValueNumber { get; }
        public bool IsTerminator => Kind is LirInstructionKind.Jump or LirInstructionKind.Branch or LirInstructionKind.Switch or LirInstructionKind.Return or LirInstructionKind.Unreachable;

        internal LirInstruction(
            int ordinal,
            LirInstructionKind kind,
            LirVirtualRegister? result,
            ImmutableArray<LirOperand> operands,
            LirAddress? address,
            string? op,
            GimpleConversionKind? conversionKind,
            FunctionType? callSignature,
            ImmutableArray<LirParallelCopy> parallelCopies,
            ImmutableArray<LirSwitchCase> switchCases,
            LirBlock? target,
            LirBlock? trueTarget,
            LirBlock? falseTarget,
            GimpleStatement? sourceStatement,
            GimpleValue? sourceValue,
            SsaInstruction? sourceInstruction,
            ValueNumber? valueNumber)
        {
            if (ordinal < 0)
                throw new ArgumentOutOfRangeException(nameof(ordinal));

            Ordinal = ordinal;
            Kind = kind;
            Result = result;
            Operands = operands.IsDefault ? ImmutableArray<LirOperand>.Empty : operands;
            Address = address;
            Operator = op ?? string.Empty;
            ConversionKind = conversionKind;
            CallSignature = callSignature;
            ParallelCopies = parallelCopies.IsDefault ? ImmutableArray<LirParallelCopy>.Empty : parallelCopies;
            SwitchCases = switchCases.IsDefault ? ImmutableArray<LirSwitchCase>.Empty : switchCases;
            Target = target;
            TrueTarget = trueTarget;
            FalseTarget = falseTarget;
            SourceStatement = sourceStatement;
            SourceValue = sourceValue;
            SourceInstruction = sourceInstruction;
            ValueNumber = valueNumber;
        }
    }

    public sealed class LirProblem
    {
        public LirProblemKind Kind { get; }
        public ControlFlowBlock? Block { get; }
        public GimpleNode? Node { get; }
        public string Message { get; }

        public LirProblem(LirProblemKind kind, ControlFlowBlock? block, GimpleNode? node, string? message)
        {
            Kind = kind;
            Block = block;
            Node = node;
            Message = message ?? string.Empty;
        }

        public override string ToString() => Message;
    }

    internal sealed class LirFunctionBuilder
    {
        private readonly SsaFunction _function;
        private readonly ControlFlowFunction _controlFlowFunction;
        private readonly LirOptions _options;
        private readonly TargetInfo _target;
        private readonly List<LirVirtualRegister> _registers = new();
        private readonly List<LirStackSlot> _stackSlots = new();
        private readonly List<LirBlock> _blocks = new();
        private readonly List<LirProblem> _problems = new();
        private readonly Dictionary<SsaName, LirVirtualRegister> _registersByName = new();
        private readonly Dictionary<ControlFlowBlock, LirBlock> _blocksByControlFlowBlock = new();
        private readonly Dictionary<Symbol, LirStackSlot> _stackSlotsBySymbol = new();
        private readonly Dictionary<GimpleTemporaryValue, LirStackSlot> _stackSlotsByTemporary = new();
        private readonly Dictionary<Symbol, GimpleVariableDeclaration> _localDeclarationsBySymbol = new();
        private readonly Dictionary<Symbol, SsaVariable> _promotedSymbols = new();
        private readonly Dictionary<GimpleTemporaryValue, SsaVariable> _promotedTemporaries = new();
        private readonly Dictionary<(ControlFlowBlock Source, ControlFlowBlock Target), LirBlock> _edgeSplitBlocks = new();
        private readonly Dictionary<(ControlFlowBlock Source, ControlFlowBlock Target), List<LirParallelCopy>> _edgeCopies = new();
        private readonly Dictionary<LirBlock, List<LirInstruction>> _instructions = new();
        private readonly HashSet<SsaName> _usedSsaNames = new();
        private readonly HashSet<ControlFlowBlock> _reachableControlFlowBlocks = new();
        private readonly Dictionary<ControlFlowBlock, SsaBlock> _ssaBlocksByControlFlowBlock = new();

        private int _nextInstructionOrdinal;
        private SsaInstruction? _currentInstruction;
        private LirBlock? _currentBlock;

        private LirFunctionBuilder(SsaFunction function, LirOptions options, TargetInfo target)
        {
            _function = function ?? throw new ArgumentNullException(nameof(function));
            _controlFlowFunction = function.ControlFlowFunction;
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _target = target;
        }

        public static LoweredLirFunction Lower(SsaFunction function, LirOptions options, TargetInfo? target)
        {
            var builder = new LirFunctionBuilder(function, options, target ?? TargetInfo.Default);
            return builder.Lower();
        }

        private LoweredLirFunction Lower()
        {
            IndexPromotedVariables();
            ScanLocalDeclarations();
            IndexReachableControlFlowBlocks();
            IndexUsedSsaNames();
            CreateVirtualRegistersForSsaNames();
            CreateBaseBlocks();
            CreateParameterStackSlots();
            CreateEdgeSplitBlocksForPhis();
            TranslateBaseBlocks();
            TranslateEdgeSplitBlocks();
            SealBlocks();

            var entry = _blocksByControlFlowBlock.TryGetValue(_controlFlowFunction.Entry, out var entryBlock)
                ? entryBlock
                : _blocks.First();

            var function = new LirFunction(
                _function,
                entry,
                _registers.ToImmutableArray(),
                _stackSlots.ToImmutableArray(),
                _blocks.ToImmutableArray(),
                _problems.ToImmutableArray());

            return new LoweredLirFunction(function, _problems.ToImmutableArray());
        }

        private void ScanLocalDeclarations()
        {
            foreach (var block in _controlFlowFunction.Function.Blocks)
            {
                foreach (var statement in block.Statements)
                {
                    if (statement is not GimpleDeclarationStatement declarationStatement)
                        continue;

                    if (declarationStatement.Symbol is null)
                        continue;

                    _localDeclarationsBySymbol[declarationStatement.Symbol] = declarationStatement.Declaration;
                    if (!_promotedSymbols.ContainsKey(declarationStatement.Symbol) && IsStackAllocatedLocal(declarationStatement.Declaration))
                        _ = GetOrCreateStackSlot(declarationStatement.Declaration);
                }
            }
        }

        private void IndexPromotedVariables()
        {
            foreach (var variable in _function.Variables)
            {
                if (variable.Kind == SsaVariableKind.Symbol && variable.Symbol is not null && !_promotedSymbols.ContainsKey(variable.Symbol))
                    _promotedSymbols.Add(variable.Symbol, variable);
                else if (variable.Kind == SsaVariableKind.Temporary && variable.Temporary is not null && !_promotedTemporaries.ContainsKey(variable.Temporary))
                    _promotedTemporaries.Add(variable.Temporary, variable);
            }
        }

        private void IndexReachableControlFlowBlocks()
        {
            _reachableControlFlowBlocks.Clear();
            _ssaBlocksByControlFlowBlock.Clear();
            if (_function.Blocks.Length == 0)
                return;

            foreach (var block in _function.Blocks)
            {
                if (!_ssaBlocksByControlFlowBlock.ContainsKey(block.ControlFlowBlock))
                    _ssaBlocksByControlFlowBlock.Add(block.ControlFlowBlock, block);
            }

            if (!_ssaBlocksByControlFlowBlock.TryGetValue(_controlFlowFunction.Entry, out var entry))
                entry = _function.Blocks[0];

            var stack = new Stack<SsaBlock>();
            _reachableControlFlowBlocks.Add(entry.ControlFlowBlock);
            stack.Push(entry);

            while (stack.Count != 0)
            {
                var block = stack.Pop();
                foreach (var successor in EnumerateOptimizedSuccessors(block))
                {
                    if (successor.IsExit)
                        continue;

                    if (!_reachableControlFlowBlocks.Add(successor))
                        continue;

                    if (_ssaBlocksByControlFlowBlock.TryGetValue(successor, out var successorBlock))
                        stack.Push(successorBlock);
                }
            }
        }

        private IEnumerable<ControlFlowBlock> EnumerateOptimizedSuccessors(SsaBlock block)
        {
            var terminator = block.Instructions.Length == 0 ? null : block.Instructions[^1].Statement;
            switch (terminator)
            {
                case GimpleGotoStatement gotoStatement:
                    if (_controlFlowFunction.TryGetBlock(gotoStatement.Target, out var gotoTarget) && gotoTarget is not null)
                        yield return gotoTarget;
                    yield break;

                case GimpleConditionalGotoStatement conditional:
                    if (_controlFlowFunction.TryGetBlock(conditional.WhenTrue, out var trueTarget) && trueTarget is not null)
                        yield return trueTarget;
                    if (_controlFlowFunction.TryGetBlock(conditional.WhenFalse, out var falseTarget) && falseTarget is not null && !ReferenceEquals(falseTarget, trueTarget))
                        yield return falseTarget;
                    yield break;

                case GimpleSwitchStatement switchStatement:
                    var seen = new HashSet<ControlFlowBlock>();
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        if (_controlFlowFunction.TryGetBlock(switchCase.Target, out var caseTarget) && caseTarget is not null && seen.Add(caseTarget))
                            yield return caseTarget;
                    }

                    if (_controlFlowFunction.TryGetBlock(switchStatement.DefaultLabel, out var defaultTarget) && defaultTarget is not null && seen.Add(defaultTarget))
                        yield return defaultTarget;
                    yield break;

                case GimpleReturnStatement:
                    yield break;
            }

            foreach (var edge in block.ControlFlowBlock.Successors)
            {
                if (!edge.Target.IsExit)
                    yield return edge.Target;
            }
        }

        private void CreateVirtualRegistersForSsaNames()
        {
            foreach (var definition in _function.Definitions)
            {
                if (definition.Name.Variable.Kind == SsaVariableKind.Memory || definition.Name.IsUndefined)
                    continue;

                if (_registersByName.ContainsKey(definition.Name))
                    continue;

                if (!IsSsaNameUsed(definition.Name))
                    continue;

                _function.ValueNumbering.TryGetValueNumber(definition.Name, out var valueNumber);
                var register = NewVirtualRegister(definition.Name.Type, definition.Name, valueNumber);
                _registersByName.Add(definition.Name, register);
            }
        }

        private void IndexUsedSsaNames()
        {
            foreach (var block in _function.Blocks)
            {
                if (_reachableControlFlowBlocks.Count != 0 && !_reachableControlFlowBlocks.Contains(block.ControlFlowBlock))
                    continue;

                foreach (var phi in block.Phis)
                {
                    foreach (var operand in phi.Operands)
                    {
                        if (!operand.Value.IsUndefined && operand.Value.Variable.Kind != SsaVariableKind.Memory)
                            _usedSsaNames.Add(operand.Value);
                    }
                }

                foreach (var instruction in block.Instructions)
                {
                    foreach (var use in instruction.Uses)
                    {
                        if (use.Kind != SsaUseKind.Memory && !use.Name.IsUndefined && use.Name.Variable.Kind != SsaVariableKind.Memory)
                            _usedSsaNames.Add(use.Name);
                    }
                }
            }
        }

        private bool IsSsaNameUsed(SsaName name)
            => _usedSsaNames.Contains(name);

        private void CreateBaseBlocks()
        {
            foreach (var controlFlowBlock in _controlFlowFunction.RealBlocks)
            {
                if (!_options.PreserveUnreachableBlocks)
                {
                    if (!controlFlowBlock.IsReachable)
                        continue;

                    if (_reachableControlFlowBlocks.Count != 0 && !_reachableControlFlowBlocks.Contains(controlFlowBlock))
                        continue;
                }

                var block = NewBlock(GetBlockName(controlFlowBlock), controlFlowBlock, isEdgeSplit: false);
                _blocksByControlFlowBlock.Add(controlFlowBlock, block);
            }
        }

        private void CreateParameterStackSlots()
        {
            var functionType = _function.Symbol?.FunctionType;
            if (functionType is null)
                return;

            foreach (var parameter in functionType.Parameters)
            {
                if (_promotedSymbols.ContainsKey(parameter))
                    continue;

                _ = GetOrCreateStackSlot(parameter, parameter.Type, isParameter: true, storageClass: StorageClass.Auto);
            }
        }

        private void CreateEdgeSplitBlocksForPhis()
        {
            foreach (var block in _function.Blocks)
            {
                if (block.Phis.Length == 0)
                    continue;

                foreach (var phi in block.Phis)
                {
                    if (phi.Result.Variable.Kind == SsaVariableKind.Memory || !IsSsaNameUsed(phi.Result))
                        continue;

                    if (!_blocksByControlFlowBlock.ContainsKey(phi.Block))
                        continue;

                    var destination = GetRegister(phi.Result);
                    foreach (var operand in phi.Operands)
                    {
                        if (!_blocksByControlFlowBlock.ContainsKey(operand.Predecessor))
                            continue;

                        var source = GetOperand(operand.Value);
                        if (source.ReferencesSameRegister(destination))
                            continue;

                        var key = (operand.Predecessor, phi.Block);
                        if (!_edgeCopies.TryGetValue(key, out var copies))
                        {
                            copies = new List<LirParallelCopy>();
                            _edgeCopies.Add(key, copies);
                        }

                        copies.Add(new LirParallelCopy(destination, source));
                    }
                }
            }

            foreach (var pair in _edgeCopies)
            {
                var source = pair.Key.Source;
                var target = pair.Key.Target;
                if (!RequiresEdgeSplitForCopies(source, target))
                    continue;

                var name = "edge_" + source.Ordinal.ToString(CultureInfo.InvariantCulture) + "_to_" + target.Ordinal.ToString(CultureInfo.InvariantCulture);
                var split = NewBlock(name, sourceBlock: source, isEdgeSplit: true);
                _edgeSplitBlocks.Add(pair.Key, split);
            }
        }

        private bool RequiresEdgeSplitForCopies(ControlFlowBlock source, ControlFlowBlock target)
        {
            if (!_edgeCopies.ContainsKey((source, target)))
                return false;

            return !CanInlineEdgeCopies(source, target);
        }

        private bool CanInlineEdgeCopies(ControlFlowBlock source, ControlFlowBlock target)
        {
            if (!_ssaBlocksByControlFlowBlock.TryGetValue(source, out var sourceBlock))
                return source.UniqueSuccessors.Length == 1 && ReferenceEquals(source.UniqueSuccessors[0], target);

            var successorCount = 0;
            foreach (var successor in EnumerateOptimizedSuccessors(sourceBlock))
            {
                if (successor.IsExit)
                    continue;

                if (!ReferenceEquals(successor, target))
                    return false;

                successorCount++;
            }

            if (successorCount != 1)
                return false;

            var terminator = sourceBlock.Instructions.Length == 0 ? null : sourceBlock.Instructions[^1].Statement;
            return terminator is null or GimpleGotoStatement or GimpleConditionalGotoStatement;
        }

        private void TranslateBaseBlocks()
        {
            foreach (var ssaBlock in _function.Blocks)
            {
                if (!_blocksByControlFlowBlock.TryGetValue(ssaBlock.ControlFlowBlock, out var block))
                    continue;

                _currentBlock = block;

                if (ReferenceEquals(ssaBlock.ControlFlowBlock, _controlFlowFunction.Entry))
                    EmitEntryParameters(block);

                foreach (var instruction in ssaBlock.Instructions)
                    TranslateInstruction(block, instruction);

                EnsureTerminator(block, ssaBlock.ControlFlowBlock);
                _currentBlock = null;
            }
        }

        private void EmitEntryParameters(LirBlock block)
        {
            var functionType = _function.Symbol?.FunctionType;
            if (functionType is null)
                return;

            for (var i = 0; i < functionType.Parameters.Length; i++)
            {
                var parameter = functionType.Parameters[i];
                var definition = _function.Definitions.FirstOrDefault(d =>
                    d.Kind == SsaDefinitionKind.Entry &&
                    ReferenceEquals(d.Parameter, parameter) &&
                    d.Name.Variable.Kind != SsaVariableKind.Memory);

                if (definition is not null && _registersByName.TryGetValue(definition.Name, out var register))
                {
                    _function.ValueNumbering.TryGetValueNumber(definition, out var valueNumber);
                    Emit(block, LirInstructionKind.Parameter, register, ImmutableArray<LirOperand>.Empty, address: null, op: parameter.Name, conversionKind: null, callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: null, sourceValue: null, sourceInstruction: null, valueNumber: valueNumber);
                    continue;
                }

                if (_stackSlotsBySymbol.TryGetValue(parameter, out var slot))
                {
                    var temporary = NewVirtualRegister(parameter.Type, sourceName: null, valueNumber: null);
                    Emit(block, LirInstructionKind.Parameter, temporary, ImmutableArray<LirOperand>.Empty, address: null, op: parameter.Name, conversionKind: null, callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: null, sourceValue: null, sourceInstruction: null, valueNumber: null);
                    Emit(block, LirInstructionKind.Store, null, ImmutableArray.Create(LirOperand.ForRegister(temporary)), LirAddress.ForStackSlot(slot), op: string.Empty, conversionKind: null, callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: null, sourceValue: null, sourceInstruction: null, valueNumber: null);
                }
            }
        }

        private void TranslateEdgeSplitBlocks()
        {
            foreach (var pair in _edgeSplitBlocks)
            {
                var key = pair.Key;
                var block = pair.Value;
                var copies = _edgeCopies.TryGetValue(key, out var copyList)
                    ? copyList.ToImmutableArray()
                    : ImmutableArray<LirParallelCopy>.Empty;

                if (copies.Length != 0)
                    Emit(block, LirInstructionKind.ParallelCopy, null, ImmutableArray<LirOperand>.Empty, address: null, op: string.Empty, conversionKind: null, callSignature: null, parallelCopies: copies, switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: null, sourceValue: null, sourceInstruction: null, valueNumber: null);

                Emit(block, LirInstructionKind.Jump, null, ImmutableArray<LirOperand>.Empty, address: null, op: string.Empty, conversionKind: null, callSignature: null, parallelCopies: default, switchCases: default, target: GetBaseBlock(key.Target), trueTarget: null, falseTarget: null, sourceStatement: null, sourceValue: null, sourceInstruction: null, valueNumber: null);
            }
        }

        private void TranslateInstruction(LirBlock block, SsaInstruction instruction)
        {
            _currentInstruction = instruction;

            switch (instruction.Statement)
            {
                case GimpleDeclarationStatement declaration:
                    if (_options.KeepNops)
                        EmitNop(block, declaration);
                    break;

                case GimpleAssignmentStatement assignment:
                    TranslateAssignment(block, instruction, assignment);
                    break;

                case GimpleZeroInitializeStatement zeroInitialize:
                    TranslateZeroInitialize(block, instruction, zeroInitialize);
                    break;

                case GimpleExpressionStatement expressionStatement:
                    if (TryGetExpression(instruction, 0, out var expression))
                        _ = EmitValue(block, expression);
                    else
                        _ = EmitValue(block, expressionStatement.Expression);
                    break;

                case GimpleGotoStatement gotoStatement:
                    EmitJump(block, instruction.Block, gotoStatement.Target, gotoStatement);
                    break;

                case GimpleConditionalGotoStatement conditional:
                    TranslateConditionalGoto(block, instruction, conditional);
                    break;

                case GimpleSwitchStatement switchStatement:
                    TranslateSwitch(block, instruction, switchStatement);
                    break;

                case GimpleReturnStatement returnStatement:
                    TranslateReturn(block, instruction, returnStatement);
                    break;

                case GimpleNopStatement nop:
                    if (_options.KeepNops)
                        EmitNop(block, nop);
                    break;

                default:
                    _problems.Add(new LirProblem(LirProblemKind.UnsupportedNode, instruction.Block, instruction.Statement, "Unsupported GIMPLE statement in LIR lowering: " + instruction.Statement.Kind));
                    if (_options.KeepNops)
                        EmitNop(block, instruction.Statement);
                    break;
            }

            _currentInstruction = null;
        }

        private void TranslateAssignment(LirBlock block, SsaInstruction instruction, GimpleAssignmentStatement assignment)
        {
            var valueExpression = TryGetAssignmentValueExpression(instruction, assignment, out var expression)
                ? expression
                : null;
            var value = valueExpression is null ? EmitValue(block, assignment.Value) : EmitValue(block, valueExpression);
            if (IsVoid(assignment.Target.Type) || value.Kind == LirOperandKind.Void || IsVoid(value.Type))
                return;
            var definition = GetPrimaryDefinition(instruction);

            if (definition is not null)
            {
                if (!IsSsaNameUsed(definition.Name))
                    return;

                var destination = GetRegister(definition.Name);
                if (value.ReferencesSameRegister(destination))
                    return;

                _function.ValueNumbering.TryGetValueNumber(definition, out var valueNumber);
                Emit(block, LirInstructionKind.Copy, destination, ImmutableArray.Create(value), address: null, op: string.Empty, conversionKind: null, callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: assignment, sourceValue: assignment.Value, sourceInstruction: instruction, valueNumber: valueNumber);
                return;
            }

            var address = TryGetAssignmentTargetAddressExpression(instruction, out var addressExpression)
                ? EmitAddress(block, addressExpression)
                : EmitAddress(block, assignment.Target);

            Emit(block, LirInstructionKind.Store, null, ImmutableArray.Create(value), address, op: string.Empty, conversionKind: null, callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: assignment, sourceValue: assignment.Value, sourceInstruction: instruction, valueNumber: null);
        }

        private void TranslateZeroInitialize(LirBlock block, SsaInstruction instruction, GimpleZeroInitializeStatement zeroInitialize)
        {
            var definition = GetPrimaryDefinition(instruction);
            if (definition is not null)
            {
                var destination = GetRegister(definition.Name);
                _function.ValueNumbering.TryGetValueNumber(definition, out var valueNumber);
                Emit(block, LirInstructionKind.Zero, destination, ImmutableArray<LirOperand>.Empty, address: null, op: string.Empty, conversionKind: null, callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: zeroInitialize, sourceValue: null, sourceInstruction: instruction, valueNumber: valueNumber);
                return;
            }

            var address = TryGetExpression(instruction, 0, out var addressExpression)
                ? EmitAddress(block, addressExpression)
                : EmitAddress(block, zeroInitialize.Target);
            var size = _target.SizeOf(zeroInitialize.Target.Type);
            Emit(block, LirInstructionKind.ZeroMemory, null, ImmutableArray.Create(LirOperand.ImmediateValue(size, TypeCatalog.Instance.Builtin(BuiltinTypeKind.Int))), address, op: string.Empty, conversionKind: null, callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: zeroInitialize, sourceValue: null, sourceInstruction: instruction, valueNumber: null);
        }

        private void TranslateConditionalGoto(LirBlock block, SsaInstruction instruction, GimpleConditionalGotoStatement conditional)
        {
            var condition = TryGetExpression(instruction, 0, out var expression)
                ? EmitValue(block, expression)
                : EmitValue(block, conditional.Condition);
            var trueTarget = ResolveTarget(instruction.Block, conditional.WhenTrue, conditional);
            var falseTarget = ResolveTarget(instruction.Block, conditional.WhenFalse, conditional);

            if (ReferenceEquals(trueTarget, falseTarget))
            {
                EmitJump(block, instruction.Block, conditional.WhenTrue, conditional);
                return;
            }

            if (TryGetImmediateTruth(condition, out var truth))
            {
                EmitJump(block, instruction.Block, truth ? conditional.WhenTrue : conditional.WhenFalse, conditional);
                return;
            }

            Emit(block, LirInstructionKind.Branch, null, ImmutableArray.Create(condition), address: null, op: string.Empty, conversionKind: null, callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: trueTarget, falseTarget: falseTarget, sourceStatement: conditional, sourceValue: conditional.Condition, sourceInstruction: instruction, valueNumber: null);
        }

        private void TranslateSwitch(LirBlock block, SsaInstruction instruction, GimpleSwitchStatement switchStatement)
        {
            var value = TryGetExpression(instruction, 0, out var expression)
                ? EmitValue(block, expression)
                : EmitValue(block, switchStatement.Expression);
            var cases = ImmutableArray.CreateBuilder<LirSwitchCase>();

            foreach (var switchCase in switchStatement.Cases)
            {
                var target = ResolveTarget(instruction.Block, switchCase.Target, switchStatement);
                cases.Add(new LirSwitchCase(LirOperand.ImmediateValue(switchCase.Value.Value, switchCase.Value.Type), target));
            }

            var defaultTarget = ResolveTarget(instruction.Block, switchStatement.DefaultLabel, switchStatement);
            Emit(block, LirInstructionKind.Switch, null, ImmutableArray.Create(value), address: null, op: string.Empty, conversionKind: null, callSignature: null, parallelCopies: default, switchCases: cases.ToImmutable(), target: defaultTarget, trueTarget: null, falseTarget: null, sourceStatement: switchStatement, sourceValue: switchStatement.Expression, sourceInstruction: instruction, valueNumber: null);
        }

        private void TranslateReturn(LirBlock block, SsaInstruction instruction, GimpleReturnStatement returnStatement)
        {
            var operands = ImmutableArray<LirOperand>.Empty;
            if (returnStatement.Expression is not null)
            {
                var value = TryGetExpression(instruction, 0, out var expression)
                    ? EmitValue(block, expression)
                    : EmitValue(block, returnStatement.Expression);
                operands = ImmutableArray.Create(value);
            }

            Emit(block, LirInstructionKind.Return, null, operands, address: null, op: string.Empty, conversionKind: null, callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: returnStatement, sourceValue: returnStatement.Expression, sourceInstruction: instruction, valueNumber: null);
        }

        private LirOperand EmitValue(LirBlock block, SsaExpression expression)
        {
            if (expression.Name is not null && !expression.IsAddress)
                return GetOperand(expression.Name);

            if (expression.IsAddress)
            {
                var address = EmitAddress(block, expression);
                return EmitAddressValue(block, address, expression.Original, expression);
            }

            return EmitValue(block, expression.Original, expression);
        }

        private LirOperand EmitValue(LirBlock block, GimpleValue value)
            => EmitValue(block, value, expression: null);

        private LirOperand EmitValue(LirBlock block, GimpleValue value, SsaExpression? expression)
        {
            switch (value)
            {
                case GimpleSymbolValue symbolValue:
                    if (expression?.Name is not null)
                        return GetOperand(expression.Name);
                    if (symbolValue.Symbol is FunctionSymbol)
                        return LirOperand.ForSymbol(symbolValue.Symbol, new QualifiedType(TypeCatalog.Instance.PointerTo(symbolValue.Type)));
                    return EmitLoad(block, EmitAddress(block, symbolValue), symbolValue.Type, value, expression);

                case GimpleTemporaryValue temporary:
                    if (expression?.Name is not null)
                        return GetOperand(expression.Name);
                    return EmitLoad(block, EmitAddress(block, temporary), temporary.Type, value, expression);

                case GimpleConstantValue constant:
                    return LirOperand.ImmediateValue(constant.Value, constant.Type);

                case GimpleUnaryExpression unary:
                    return EmitUnary(block, unary, expression);

                case GimpleBinaryExpression binary:
                    return EmitBinary(block, binary, expression);

                case GimpleConversionExpression conversion:
                    return EmitConversion(block, conversion, expression);

                case GimpleCastExpression cast:
                    return EmitCast(block, cast, expression);

                case GimpleAddressOfExpression addressOf:
                    return EmitAddressValue(block, EmitAddress(block, addressOf.Target, GetChild(expression, 0)), addressOf, expression);

                case GimpleIndirectExpression indirect:
                    return EmitLoad(block, EmitAddress(block, indirect, expression), indirect.Type, indirect, expression);

                case GimpleElementAccessExpression elementAccess:
                    return EmitLoad(block, EmitAddress(block, elementAccess, expression), elementAccess.Type, elementAccess, expression);

                case GimpleMemberAccessExpression memberAccess:
                    return EmitLoad(block, EmitAddress(block, memberAccess, expression), memberAccess.Type, memberAccess, expression);

                case GimpleCallExpression call:
                    return EmitCall(block, call, expression);

                case GimpleErrorValue:
                    return LirOperand.Undefined(null, value.Type);

                default:
                    _problems.Add(new LirProblem(LirProblemKind.UnsupportedNode, _currentInstruction?.Block, value, "Unsupported GIMPLE value in LIR lowering: " + value.Kind));
                    return LirOperand.Undefined(null, value.Type);
            }
        }

        private LirOperand EmitUnary(LirBlock block, GimpleUnaryExpression unary, SsaExpression? expression)
        {
            var operand = GetChild(expression, 0) is { } child
                ? EmitValue(block, child)
                : EmitValue(block, unary.Operand);
            var result = NewVirtualRegister(unary.Type, sourceName: null, GetValueNumber(expression));
            Emit(block, LirInstructionKind.Unary, result, ImmutableArray.Create(operand), address: null, op: TokenText(unary.OperatorToken), 
                conversionKind: null, callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null,
                sourceStatement: _currentInstruction?.Statement, sourceValue: unary, sourceInstruction: _currentInstruction, valueNumber: GetValueNumber(expression));
            return LirOperand.ForRegister(result);
        }

        private LirOperand EmitBinary(LirBlock block, GimpleBinaryExpression binary, SsaExpression? expression)
        {
            var left = GetChild(expression, 0) is { } leftExpression
                ? EmitValue(block, leftExpression)
                : EmitValue(block, binary.Left);
            var right = GetChild(expression, 1) is { } rightExpression
                ? EmitValue(block, rightExpression)
                : EmitValue(block, binary.Right);
            var result = NewVirtualRegister(binary.Type, sourceName: null, GetValueNumber(expression));
            Emit(block, LirInstructionKind.Binary, result, ImmutableArray.Create(left, right), address: null, op: TokenText(binary.OperatorToken), 
                conversionKind: null, callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, 
                sourceStatement: _currentInstruction?.Statement, sourceValue: binary, sourceInstruction: _currentInstruction, valueNumber: GetValueNumber(expression));
            return LirOperand.ForRegister(result);
        }

        private LirOperand EmitConversion(LirBlock block, GimpleConversionExpression conversion, SsaExpression? expression)
        {
            if (conversion.ConversionKind == GimpleConversionKind.ArrayToPointer)
                return EmitArrayToPointer(block, conversion, expression);

            var operand = GetChild(expression, 0) is { } child
                ? EmitValue(block, child)
                : EmitValue(block, conversion.Operand);

            if (conversion.ConversionKind == GimpleConversionKind.Identity && SameType(operand.Type, conversion.Type))
                return operand;

            if (IsVoid(conversion.Type))
                return LirOperand.Void;

            if (conversion.ConversionKind == GimpleConversionKind.FunctionToPointer &&
                operand.Kind == LirOperandKind.Symbol &&
                operand.Symbol is FunctionSymbol function)
            {
                if (function.IntrinsicKind == RuntimeIntrinsicKind.BuiltinVaStart)
                {
                    _problems.Add(new LirProblem(
                        LirProblemKind.UnsupportedNode,
                        _currentInstruction?.Block,
                        conversion,
                        "Cannot take address of compiler intrinsic '__builtin_va_start'."));
                    return LirOperand.Undefined(null, conversion.Type);
                }
                return LirOperand.ForSymbol(operand.Symbol, conversion.Type);
            }

            var result = NewVirtualRegister(conversion.Type, sourceName: null, GetValueNumber(expression));
            Emit(block, LirInstructionKind.Convert, result, ImmutableArray.Create(operand), address: null, op: conversion.ConversionKind.ToString(), 
                conversionKind: conversion.ConversionKind, callSignature: null, parallelCopies: default, switchCases: default, target: null, 
                trueTarget: null, falseTarget: null, sourceStatement: _currentInstruction?.Statement, sourceValue: conversion, 
                sourceInstruction: _currentInstruction, valueNumber: GetValueNumber(expression));
            return LirOperand.ForRegister(result);
        }
        private LirOperand EmitArrayToPointer(LirBlock block, GimpleConversionExpression conversion, SsaExpression? expression)
        {
            var child = GetChild(expression, 0);

            if (conversion.Operand is GimpleConstantValue { Value: string text })
                return LirOperand.ImmediateValue(text, conversion.Type);

            if (child?.Original is GimpleConstantValue { Value: string childText })
                return LirOperand.ImmediateValue(childText, conversion.Type);

            if (child is not null && child.IsAddress)
                return EmitAddressValue(block, EmitAddress(block, child), conversion.Type, conversion, expression);

            if (conversion.Operand is GimplePlace place)
                return EmitAddressValue(block, EmitAddress(block, place, child), conversion.Type, conversion, expression);

            if (child?.Original is GimplePlace childPlace)
                return EmitAddressValue(block, EmitAddress(block, childPlace, child), conversion.Type, conversion, expression);

            _problems.Add(new LirProblem(
                LirProblemKind.InvalidAddress,
                _currentInstruction?.Block,
                conversion,
                "Cannot decay a non-addressable array expression to pointer."));

            return LirOperand.Undefined(null, conversion.Type);
        }
        private LirOperand EmitCast(LirBlock block, GimpleCastExpression cast, SsaExpression? expression)
        {
            var operand = GetChild(expression, 0) is { } child
                ? EmitValue(block, child)
                : EmitValue(block, cast.Operand);
            if (IsVoid(cast.Type))
                return LirOperand.Void;
            var result = NewVirtualRegister(cast.Type, sourceName: null, GetValueNumber(expression));
            Emit(block, LirInstructionKind.Cast, result, ImmutableArray.Create(operand), address: null, op: "cast", conversionKind: null, 
                callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, 
                sourceStatement: _currentInstruction?.Statement, sourceValue: cast, sourceInstruction: _currentInstruction, valueNumber: GetValueNumber(expression));
            return LirOperand.ForRegister(result);
        }

        private LirOperand EmitCall(LirBlock block, GimpleCallExpression call, SsaExpression? expression)
        {
            if (TryGetRuntimeIntrinsic(call.Callee, out var intrinsic))
            {
                switch (intrinsic)
                {
                    case RuntimeIntrinsicKind.BuiltinVaStart:
                        return EmitVaStart(block, call, expression);
                }
            }
            var operands = ImmutableArray.CreateBuilder<LirOperand>();
            operands.Add(GetChild(expression, 0) is { } calleeExpression
                ? EmitValue(block, calleeExpression)
                : EmitValue(block, call.Callee));

            for (var i = 0; i < call.Arguments.Length; i++)
            {
                var child = GetChild(expression, i + 1);
                operands.Add(child is null ? EmitValue(block, call.Arguments[i]) : EmitValue(block, child));
            }

            LirVirtualRegister? result = IsVoid(call.Type) ? null : NewVirtualRegister(call.Type, sourceName: null, GetValueNumber(expression));
            Emit(block, LirInstructionKind.Call, result, operands.ToImmutable(), address: null, op: string.Empty, conversionKind: null, 
                callSignature: call.FunctionType, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, 
                sourceStatement: _currentInstruction?.Statement, sourceValue: call, sourceInstruction: _currentInstruction, valueNumber: GetValueNumber(expression));
            return result is null ? LirOperand.Void : LirOperand.ForRegister(result);
        }

        private LirOperand EmitVaStart(LirBlock block, GimpleCallExpression call, SsaExpression? expression)
        {
            if (call.Arguments.Length != 0)
            {
                _problems.Add(new LirProblem(
                    LirProblemKind.UnsupportedNode,
                    _currentInstruction?.Block,
                    call,
                    "__builtin_va_start expects no explicit arguments after macro expansion."));
            }

            LirVirtualRegister? result = IsVoid(call.Type) ? null : NewVirtualRegister(call.Type, sourceName: null, GetValueNumber(expression));
            Emit(block, LirInstructionKind.VaStart, result, ImmutableArray<LirOperand>.Empty, address: null, op: string.Empty, conversionKind: null, 
                callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, 
                sourceStatement: _currentInstruction?.Statement, sourceValue: call, sourceInstruction: _currentInstruction, valueNumber: GetValueNumber(expression));
            return result is null ? LirOperand.Void : LirOperand.ForRegister(result);
        }
        private static bool TryGetRuntimeIntrinsic(GimpleValue value, out RuntimeIntrinsicKind intrinsic)
        {
            while (true)
            {
                switch (value)
                {
                    case GimpleConversionExpression conversion 
                    when conversion.ConversionKind is GimpleConversionKind.FunctionToPointer or GimpleConversionKind.Identity:
                        value = conversion.Operand;
                        continue;
                    case GimpleCastExpression cast:
                        value = cast.Operand;
                        continue;
                    case GimpleSymbolValue { Symbol: FunctionSymbol function } when function.IntrinsicKind != RuntimeIntrinsicKind.None:
                        intrinsic = function.IntrinsicKind;
                        return true;
                    default:
                        intrinsic = RuntimeIntrinsicKind.None;
                        return false;
                }
            }
        }

        private LirOperand EmitLoad(LirBlock block, LirAddress address, QualifiedType type, GimpleValue sourceValue, SsaExpression? expression)
        {
            var result = NewVirtualRegister(type, sourceName: null, GetValueNumber(expression));
            Emit(block, LirInstructionKind.Load, result, ImmutableArray<LirOperand>.Empty, address, op: string.Empty, conversionKind: null, 
                callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, 
                sourceStatement: _currentInstruction?.Statement, sourceValue: sourceValue, sourceInstruction: _currentInstruction, valueNumber: GetValueNumber(expression));
            return LirOperand.ForRegister(result);
        }

        private LirOperand EmitAddressValue(LirBlock block, LirAddress address, GimpleValue sourceValue, SsaExpression? expression)
            => EmitAddressValue(block, address, new QualifiedType(TypeCatalog.Instance.PointerTo(address.ElementType)), sourceValue, expression);
        private LirOperand EmitAddressValue(LirBlock block, LirAddress address, QualifiedType resultType, GimpleValue sourceValue, SsaExpression? expression)
        {
            var result = NewVirtualRegister(resultType, sourceName: null, GetValueNumber(expression));
            Emit(block, LirInstructionKind.AddressOf, result, ImmutableArray<LirOperand>.Empty, address, op: string.Empty, conversionKind: null, 
                callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, 
                sourceStatement: _currentInstruction?.Statement, sourceValue: sourceValue, sourceInstruction: _currentInstruction, valueNumber: GetValueNumber(expression));
            return LirOperand.ForRegister(result);
        }

        private LirAddress EmitAddress(LirBlock block, SsaExpression expression)
        {
            if (expression.Name is not null && expression.IsAddress)
                return EmitPromotedAddress(block, expression);

            return EmitAddress(block, expression.Original, expression);
        }

        private LirAddress EmitAddress(LirBlock block, GimplePlace place)
            => EmitAddress(block, place, expression: null);

        private LirAddress EmitAddress(LirBlock block, GimplePlace place, SsaExpression? expression)
            => EmitAddress(block, (GimpleValue)place, expression);

        private LirAddress EmitAddress(LirBlock block, GimpleValue value, SsaExpression? expression)
        {
            if (expression?.Name is not null && expression.IsAddress)
                return EmitPromotedAddress(block, expression);

            switch (value)
            {
                case GimpleSymbolValue symbolValue:
                    if (_stackSlotsBySymbol.TryGetValue(symbolValue.Symbol, out var slot))
                        return LirAddress.ForStackSlot(slot);

                    if (_localDeclarationsBySymbol.TryGetValue(symbolValue.Symbol, out var declaration) && IsStackAllocatedLocal(declaration))
                        return LirAddress.ForStackSlot(GetOrCreateStackSlot(declaration));

                    return LirAddress.ForSymbol(symbolValue.Symbol, symbolValue.Type);

                case GimpleTemporaryValue temporary:
                    return LirAddress.ForStackSlot(GetOrCreateStackSlot(temporary));

                case GimpleIndirectExpression indirect:
                    {
                        var pointer = GetChild(expression, 0) is { } child
                            ? EmitValue(block, child)
                            : EmitValue(block, indirect.Address);
                        return LirAddress.Indirect(pointer, indirect.Type);
                    }

                case GimpleElementAccessExpression elementAccess:
                    return EmitElementAddress(block, elementAccess, expression);

                case GimpleMemberAccessExpression memberAccess:
                    return EmitMemberAddress(block, memberAccess, expression);

                default:
                    _problems.Add(new LirProblem(LirProblemKind.InvalidAddress, _currentInstruction?.Block, value, $"Cannot form an LIR address for GIMPLE value: {value.Kind}"));
                    return LirAddress.Indirect(LirOperand.Undefined(null, value.Type), value.Type);
            }
        }

        private LirAddress EmitElementAddress(LirBlock block, GimpleElementAccessExpression elementAccess, SsaExpression? expression)
        {
            var baseExpression = GetChild(expression, 0);
            LirAddress baseAddress;
            if (elementAccess.Expression.Type.Type is PointerType)
            {
                var pointer = EmitPointerElementBaseValue(block, elementAccess.Expression, baseExpression);
                baseAddress = LirAddress.Indirect(pointer, elementAccess.Type);
            }
            else if (baseExpression is not null && baseExpression.IsAddress)
            {
                baseAddress = EmitAddress(block, baseExpression);
            }
            else if (elementAccess.Expression is GimplePlace basePlace)
            {
                baseAddress = EmitAddress(block, basePlace, baseExpression);
            }
            else
            {
                baseAddress = LirAddress.Indirect(baseExpression is null ? EmitValue(block, elementAccess.Expression) : EmitValue(block, baseExpression), elementAccess.Type);
            }

            LirOperand? index = null;
            if (elementAccess.Index is not null)
            {
                var indexExpression = GetChild(expression, 1);
                index = indexExpression is null ? EmitValue(block, elementAccess.Index) : EmitValue(block, indexExpression);
            }

            var scale = _target.SizeOf(elementAccess.Type);
            return LirAddress.Element(baseAddress, index, elementAccess.Type, scale);
        }
        private LirOperand EmitPointerElementBaseValue(LirBlock block, GimpleValue originalBase, SsaExpression? rewrittenBase)
        {
            if (rewrittenBase?.Name is not null)
                return GetOperand(rewrittenBase.Name);

            if (rewrittenBase is not null && !rewrittenBase.IsAddress)
                return EmitValue(block, rewrittenBase);

            return EmitValue(block, originalBase);
        }
        private LirAddress EmitMemberAddress(LirBlock block, GimpleMemberAccessExpression memberAccess, SsaExpression? expression)
        {
            var baseExpression = GetChild(expression, 0);
            LirAddress baseAddress;
            if (memberAccess.OperatorToken.Kind.ToString().Contains("Arrow", StringComparison.Ordinal))
            {
                var pointer = baseExpression is null ? EmitValue(block, memberAccess.Expression) : EmitValue(block, baseExpression);
                baseAddress = LirAddress.Indirect(pointer, memberAccess.Expression.Type);
            }
            else if (baseExpression is not null && baseExpression.IsAddress)
            {
                baseAddress = EmitAddress(block, baseExpression);
            }
            else if (memberAccess.Expression is GimplePlace basePlace)
            {
                baseAddress = EmitAddress(block, basePlace, baseExpression);
            }
            else
            {
                baseAddress = LirAddress.Indirect(baseExpression is null ? EmitValue(block, memberAccess.Expression) : EmitValue(block, baseExpression), memberAccess.Expression.Type);
            }

            return LirAddress.ForField(baseAddress, memberAccess.Field, memberAccess.Type, GetFieldOffset(memberAccess.Field));
        }

        private LirAddress EmitPromotedAddress(LirBlock block, SsaExpression expression)
        {
            var name = expression.Name!;
            if (name.Variable.Kind != SsaVariableKind.Temporary)
            {
                _problems.Add(new LirProblem(
                LirProblemKind.PromotedAddressTakenValue,
                _currentInstruction?.Block,
                expression.Original,
                $"Address was requested for promoted SSA value '{name}'."));
            }

            LirStackSlot slot;
            if (name.Variable.Symbol is not null)
                slot = GetOrCreateStackSlot(name.Variable.Symbol, name.Type, isParameter: false, StorageClass.Auto);
            else if (name.Variable.Temporary is not null)
                slot = GetOrCreateStackSlot(name.Variable.Temporary);
            else
                slot = GetOrCreateAnonymousStackSlot(name.Type, name.Variable.Name);

            Emit(block, LirInstructionKind.Store, null, ImmutableArray.Create(GetOperand(name)), LirAddress.ForStackSlot(slot), op: string.Empty, conversionKind: null, 
                callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: _currentInstruction?.Statement, 
                sourceValue: expression.Original, sourceInstruction: _currentInstruction, valueNumber: null);
            return LirAddress.ForStackSlot(slot);
        }

        private void EnsureTerminator(LirBlock block, ControlFlowBlock source)
        {
            if (HasTerminator(block))
                return;

            if (source.Successors.Length == 0)
            {
                Emit(block, LirInstructionKind.Return, null, ImmutableArray<LirOperand>.Empty, address: null, op: string.Empty, conversionKind: null, callSignature: null, 
                    parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: null, sourceValue: null, 
                    sourceInstruction: null, valueNumber: null);
                return;
            }

            var nonExitSuccessors = source.Successors.Where(static edge => !edge.Target.IsExit).ToArray();
            if (nonExitSuccessors.Length == 0)
            {
                Emit(block, LirInstructionKind.Return, null, ImmutableArray<LirOperand>.Empty, address: null, op: string.Empty, conversionKind: null, callSignature: null, 
                    parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: null, sourceValue: null, 
                    sourceInstruction: null, valueNumber: null);
                return;
            }

            EmitJumpToControlFlowTarget(block, source, nonExitSuccessors[0].Target, statement: null, sourceInstruction: null);
        }

        private void EmitJump(LirBlock block, ControlFlowBlock source, GimpleLabel targetLabel, GimpleStatement statement)
        {
            if (_controlFlowFunction.TryGetBlock(targetLabel, out var target) && target is not null)
            {
                EmitJumpToControlFlowTarget(block, source, target, statement, _currentInstruction);
                return;
            }

            _problems.Add(new LirProblem(LirProblemKind.MissingTarget, source, statement, $"Missing LIR target for label '{targetLabel.Name}'."));
            EmitJumpToBlock(block, _blocksByControlFlowBlock.TryGetValue(source, out var self) ? self : _blocks[0], statement, _currentInstruction);
        }

        private void EmitJumpToControlFlowTarget(
            LirBlock block,
            ControlFlowBlock source,
            ControlFlowBlock target,
            GimpleStatement? statement,
            SsaInstruction? sourceInstruction)
        {
            if (_edgeSplitBlocks.TryGetValue((source, target), out var split))
            {
                EmitJumpToBlock(block, split, statement, sourceInstruction);
                return;
            }

            EmitEdgeCopies(block, source, target);
            EmitJumpToBlock(block, GetBaseBlock(target), statement, sourceInstruction);
        }

        private void EmitJumpToBlock(
            LirBlock block,
            LirBlock target,
            GimpleStatement? statement,
            SsaInstruction? sourceInstruction)
        {
            Emit(block, LirInstructionKind.Jump, null, ImmutableArray<LirOperand>.Empty, address: null, op: string.Empty, conversionKind: null, callSignature: null,
                parallelCopies: default, switchCases: default, target: target, trueTarget: null, falseTarget: null, sourceStatement: statement, sourceValue: null,
                sourceInstruction: sourceInstruction, valueNumber: null);
        }

        private void EmitEdgeCopies(LirBlock block, ControlFlowBlock source, ControlFlowBlock target)
        {
            if (!_edgeCopies.TryGetValue((source, target), out var copies) || copies.Count == 0)
                return;

            Emit(block, LirInstructionKind.ParallelCopy, null, ImmutableArray<LirOperand>.Empty, address: null, op: string.Empty, conversionKind: null, callSignature: null,
                parallelCopies: copies.ToImmutableArray(), switchCases: default, target: null, trueTarget: null, falseTarget: null, sourceStatement: null, sourceValue: null,
                sourceInstruction: null, valueNumber: null);
        }

        private LirBlock ResolveTarget(ControlFlowBlock source, GimpleLabel label, GimpleStatement statement)
        {
            if (_controlFlowFunction.TryGetBlock(label, out var target) && target is not null)
                return Redirect(source, target);

            _problems.Add(new LirProblem(LirProblemKind.MissingTarget, source, statement, $"Missing LIR target for label '{label.Name}'."));
            return _blocksByControlFlowBlock.TryGetValue(source, out var self) ? self : _blocks[0];
        }

        private LirBlock Redirect(ControlFlowBlock source, ControlFlowBlock target)
        {
            if (_edgeSplitBlocks.TryGetValue((source, target), out var split))
                return split;

            return GetBaseBlock(target);
        }

        private LirBlock GetBaseBlock(ControlFlowBlock target)
        {
            if (_blocksByControlFlowBlock.TryGetValue(target, out var block))
                return block;

            _problems.Add(new LirProblem(LirProblemKind.MissingTarget, target, target.GimpleBlock, $"Missing LIR block for CFG block '{target}'."));
            return _blocks[0];
        }

        private void SealBlocks()
        {
            foreach (var block in _blocks)
            {
                if (_instructions.TryGetValue(block, out var instructions))
                    block.SetInstructions(instructions.ToImmutableArray());
                else
                    block.SetInstructions(ImmutableArray<LirInstruction>.Empty);
            }
        }

        private LirInstruction Emit(
            LirBlock block,
            LirInstructionKind kind,
            LirVirtualRegister? result,
            ImmutableArray<LirOperand> operands,
            LirAddress? address,
            string? op,
            GimpleConversionKind? conversionKind,
            FunctionType? callSignature,
            ImmutableArray<LirParallelCopy> parallelCopies,
            ImmutableArray<LirSwitchCase> switchCases,
            LirBlock? target,
            LirBlock? trueTarget,
            LirBlock? falseTarget,
            GimpleStatement? sourceStatement,
            GimpleValue? sourceValue,
            SsaInstruction? sourceInstruction,
            ValueNumber? valueNumber)
        {
            var instruction = new LirInstruction(
                _nextInstructionOrdinal++,
                kind,
                result,
                operands,
                address,
                op,
                conversionKind,
                callSignature,
                parallelCopies,
                switchCases,
                target,
                trueTarget,
                falseTarget,
                sourceStatement,
                sourceValue,
                sourceInstruction,
                valueNumber);

            if (!_instructions.TryGetValue(block, out var list))
            {
                list = new List<LirInstruction>();
                _instructions.Add(block, list);
            }

            list.Add(instruction);
            return instruction;
        }

        private void EmitNop(LirBlock block, GimpleStatement statement)
            => Emit(block, LirInstructionKind.Nop, null, ImmutableArray<LirOperand>.Empty, address: null, op: string.Empty, conversionKind: null, 
                callSignature: null, parallelCopies: default, switchCases: default, target: null, trueTarget: null, falseTarget: null, 
                sourceStatement: statement, sourceValue: null, sourceInstruction: _currentInstruction, valueNumber: null);

        private bool HasTerminator(LirBlock block)
        {
            return _instructions.TryGetValue(block, out var list) &&
                   list.Count != 0 &&
                   list[^1].IsTerminator;
        }

        private LirBlock NewBlock(string name, ControlFlowBlock? sourceBlock, bool isEdgeSplit)
        {
            var block = new LirBlock(_blocks.Count, name, sourceBlock, isEdgeSplit);
            _blocks.Add(block);
            return block;
        }

        private LirVirtualRegister NewVirtualRegister(QualifiedType type, SsaName? sourceName, ValueNumber? valueNumber)
        {
            var register = new LirVirtualRegister(_registers.Count, type, GetRegisterClass(type), sourceName, valueNumber);
            _registers.Add(register);
            return register;
        }

        private LirVirtualRegister GetRegister(SsaName name)
        {
            if (!_registersByName.TryGetValue(name, out var register))
            {
                _function.ValueNumbering.TryGetValueNumber(name, out var valueNumber);
                register = NewVirtualRegister(name.Type, name, valueNumber);
                _registersByName.Add(name, register);
            }

            return register;
        }

        private LirOperand GetOperand(SsaName name)
        {
            if (name.IsUndefined)
                return LirOperand.Undefined(name, name.Type);

            return LirOperand.ForRegister(GetRegister(name));
        }

        private LirStackSlot GetOrCreateStackSlot(GimpleVariableDeclaration declaration)
        {
            if (declaration.Symbol is not null && _stackSlotsBySymbol.TryGetValue(declaration.Symbol, out var existing))
                return existing;

            var name = declaration.Symbol?.Name ?? "local";
            var slot = NewStackSlot(name, declaration.Type, declaration.Symbol, temporary: null, isParameter: declaration.Symbol is ParameterSymbol, declaration.StorageClass);
            if (declaration.Symbol is not null)
                _stackSlotsBySymbol[declaration.Symbol] = slot;
            return slot;
        }

        private LirStackSlot GetOrCreateStackSlot(Symbol symbol, QualifiedType type, bool isParameter, StorageClass storageClass)
        {
            if (_stackSlotsBySymbol.TryGetValue(symbol, out var existing))
                return existing;

            var slot = NewStackSlot(symbol.Name, type, symbol, temporary: null, isParameter, storageClass);
            _stackSlotsBySymbol.Add(symbol, slot);
            return slot;
        }

        private LirStackSlot GetOrCreateStackSlot(GimpleTemporaryValue temporary)
        {
            if (_stackSlotsByTemporary.TryGetValue(temporary, out var existing))
                return existing;

            var slot = NewStackSlot(temporary.Name, temporary.Type, symbol: null, temporary, isParameter: false, StorageClass.Auto);
            _stackSlotsByTemporary.Add(temporary, slot);
            return slot;
        }

        private LirStackSlot GetOrCreateAnonymousStackSlot(QualifiedType type, string name)
            => NewStackSlot(name, type, symbol: null, temporary: null, isParameter: false, StorageClass.Auto);

        private LirStackSlot NewStackSlot(string name, QualifiedType type, Symbol? symbol, GimpleTemporaryValue? temporary, bool isParameter, StorageClass storageClass)
        {
            var size = _target.SizeOf(type);
            var alignment = _target.AlignOf(type);
            var slot = new LirStackSlot(_stackSlots.Count, name, type, size, alignment, symbol, temporary, isParameter, storageClass);
            _stackSlots.Add(slot);
            return slot;
        }

        private SsaDefinition? GetPrimaryDefinition(SsaInstruction instruction)
        {
            foreach (var definition in instruction.Definitions)
            {
                if (definition.Name.Variable.Kind != SsaVariableKind.Memory)
                    return definition;
            }

            return null;
        }

        private bool TryGetExpression(SsaInstruction instruction, int index, out SsaExpression expression)
        {
            if (index >= 0 && index < instruction.Expressions.Length)
            {
                expression = instruction.Expressions[index];
                return true;
            }

            expression = null!;
            return false;
        }

        private bool TryGetAssignmentTargetAddressExpression(SsaInstruction instruction, out SsaExpression expression)
        {
            if (instruction.Statement is GimpleAssignmentStatement assignment &&
                GetPrimaryDefinition(instruction) is null &&
                instruction.Expressions.Length >= 2)
            {
                expression = instruction.Expressions[0];
                return true;
            }

            expression = null!;
            return false;
        }

        private bool TryGetAssignmentValueExpression(SsaInstruction instruction, GimpleAssignmentStatement assignment, out SsaExpression expression)
        {
            if (GetPrimaryDefinition(instruction) is not null && instruction.Expressions.Length >= 1)
            {
                expression = instruction.Expressions[0];
                return true;
            }

            if (GetPrimaryDefinition(instruction) is null && instruction.Expressions.Length >= 2)
            {
                expression = instruction.Expressions[1];
                return true;
            }

            expression = null!;
            return false;
        }

        private SsaExpression? GetChild(SsaExpression? expression, int index)
        {
            if (expression is null || index < 0 || index >= expression.Children.Length)
                return null;

            return expression.Children[index];
        }

        private ValueNumber? GetValueNumber(SsaExpression? expression)
        {
            if (expression is null)
                return null;

            _function.ValueNumbering.TryGetValueNumber(expression, out var valueNumber);
            return valueNumber;
        }

        private bool IsStackAllocatedLocal(GimpleVariableDeclaration declaration)
        {
            if (declaration.Symbol is not VariableSymbol variable)
                return declaration.Symbol is ParameterSymbol;

            return variable.StorageClass is StorageClass.None or StorageClass.Auto or StorageClass.Register;
        }

        private int GetFieldOffset(FieldSymbol? field)
        {
            if (field is null)
                return 0;

            var tag = field.ContainingTag;
            if (tag.TagKind == TagKind.Union)
                return 0;

            var offset = 0;
            foreach (var candidate in tag.Fields)
            {
                var fieldAlignment = _target.AlignOf(candidate.Type);
                offset = AlignTo(offset, fieldAlignment);
                if (ReferenceEquals(candidate, field))
                    return offset;
                offset += _target.SizeOf(candidate.Type);
            }

            return 0;
        }

        private static int AlignTo(int value, int alignment)
        {
            if (alignment <= 1)
                return value;

            var remainder = value % alignment;
            return remainder == 0 ? value : checked(value + alignment - remainder);
        }

        private static string GetBlockName(ControlFlowBlock block)
            => block.Label?.Name ?? "bb" + block.Ordinal.ToString(CultureInfo.InvariantCulture);

        private static string TokenText(SyntaxToken token)
            => string.IsNullOrEmpty(token.Text) ? token.Kind.ToString() : token.Text;

        private static bool TryGetImmediateTruth(LirOperand operand, out bool truth)
        {
            if (operand.Kind != LirOperandKind.Immediate)
            {
                truth = false;
                return false;
            }

            switch (operand.Immediate)
            {
                case null:
                    truth = false;
                    return true;
                case bool value:
                    truth = value;
                    return true;
                case char value:
                    truth = value != 0;
                    return true;
                case byte value:
                    truth = value != 0;
                    return true;
                case sbyte value:
                    truth = value != 0;
                    return true;
                case short value:
                    truth = value != 0;
                    return true;
                case ushort value:
                    truth = value != 0;
                    return true;
                case int value:
                    truth = value != 0;
                    return true;
                case uint value:
                    truth = value != 0;
                    return true;
                case long value:
                    truth = value != 0;
                    return true;
                case ulong value:
                    truth = value != 0;
                    return true;
                case float value:
                    truth = value != 0.0f;
                    return true;
                case double value:
                    truth = value != 0.0;
                    return true;
                case decimal value:
                    truth = value != 0m;
                    return true;
                default:
                    truth = false;
                    return false;
            }
        }

        private static bool SameType(QualifiedType left, QualifiedType right)
            => string.Equals(left.ToDisplayString(), right.ToDisplayString(), StringComparison.Ordinal);

        private static bool IsVoid(QualifiedType type)
            => type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Void };

        private static LirRegisterClass GetRegisterClass(QualifiedType type)
        {
            if (type.Type is BuiltinType builtin)
            {
                return builtin.BuiltinKind switch
                {
                    BuiltinTypeKind.Void => LirRegisterClass.Void,
                    BuiltinTypeKind.Float or BuiltinTypeKind.Double or BuiltinTypeKind.LongDouble => LirRegisterClass.Floating,
                    _ => LirRegisterClass.General,
                };
            }

            return type.Type.Kind switch
            {
                TypeKind.Pointer or TypeKind.Function => LirRegisterClass.Address,
                TypeKind.Array or TypeKind.Struct or TypeKind.Union => LirRegisterClass.Aggregate,
                TypeKind.Enum => LirRegisterClass.General,
                TypeKind.Error => LirRegisterClass.Unknown,
                _ => LirRegisterClass.Unknown,
            };
        }

        internal readonly struct LoweredLirFunction
        {
            public LirFunction Function { get; }
            public ImmutableArray<LirProblem> Problems { get; }

            public LoweredLirFunction(LirFunction function, ImmutableArray<LirProblem> problems)
            {
                Function = function ?? throw new ArgumentNullException(nameof(function));
                Problems = problems.IsDefault ? ImmutableArray<LirProblem>.Empty : problems;
            }
        }
    }

    public sealed class LirPrinter
    {
        private readonly StringBuilder _builder = new();
        private readonly LirOptions _options;
        private int _indent;

        private LirPrinter(LirOptions? options)
        {
            _options = options ?? LirOptions.Default;
        }

        public static string Print(LirModule module, LirOptions? options = null)
        {
            if (module is null)
                throw new ArgumentNullException(nameof(module));

            var printer = new LirPrinter(options);
            printer.WriteModule(module);
            return printer._builder.ToString();
        }

        public static string Print(LirFunction function, LirOptions? options = null)
        {
            if (function is null)
                throw new ArgumentNullException(nameof(function));

            var printer = new LirPrinter(options);
            printer.WriteFunction(function);
            return printer._builder.ToString();
        }

        public static void WriteTo(TextWriter writer, LirModule module, LirOptions? options = null)
        {
            if (writer is null)
                throw new ArgumentNullException(nameof(writer));

            writer.Write(Print(module, options));
        }

        public static void WriteTo(TextWriter writer, LirFunction function, LirOptions? options = null)
        {
            if (writer is null)
                throw new ArgumentNullException(nameof(writer));

            writer.Write(Print(function, options));
        }

        private void WriteModule(LirModule module)
        {
            WriteLine("lir module");
            _indent++;

            foreach (var global in module.Globals)
                WriteLine("global " + FormatSymbol(global.Symbol) + " : " + global.Type.ToDisplayString() + " storage=" + global.StorageClass);

            if (module.Globals.Length != 0 && module.Functions.Length != 0)
                WriteLine(string.Empty);

            for (var i = 0; i < module.Functions.Length; i++)
            {
                if (i != 0)
                    WriteLine(string.Empty);
                WriteFunction(module.Functions[i]);
            }

            if (module.Problems.Length != 0)
            {
                WriteLine(string.Empty);
                WriteLine("problems");
                _indent++;
                foreach (var problem in module.Problems)
                    WriteLine(problem.Kind + ": " + problem.Message);
                _indent--;
            }

            _indent--;
        }

        private void WriteFunction(LirFunction function)
        {
            var name = function.Symbol?.Name ?? "<anonymous-function>";
            WriteLine("function @" + name);
            _indent++;

            if (function.VirtualRegisters.Length != 0)
            {
                WriteLine("vregs");
                _indent++;
                foreach (var register in function.VirtualRegisters)
                {
                    var line = register.Name + " : " + register.Type.ToDisplayString() + " class=" + register.RegisterClass;
                    if (register.SourceName is not null)
                        line += " source=" + register.SourceName;
                    if (_options.EmitValueNumberComments && register.ValueNumber is not null)
                        line += " ; " + register.ValueNumber;
                    WriteLine(line);
                }
                _indent--;
            }

            if (function.StackSlots.Length != 0)
            {
                WriteLine("stack");
                _indent++;
                foreach (var slot in function.StackSlots)
                {
                    var line = slot + " " + slot.Name + " : " + slot.Type.ToDisplayString() +
                               " size=" + slot.Size.ToString(CultureInfo.InvariantCulture) +
                               " align=" + slot.Alignment.ToString(CultureInfo.InvariantCulture);
                    if (slot.IsParameter)
                        line += " parameter";
                    WriteLine(line);
                }
                _indent--;
            }

            foreach (var block in function.Blocks)
            {
                WriteLine(block.Name + ":");
                _indent++;
                foreach (var instruction in block.Instructions)
                    WriteInstruction(instruction);
                _indent--;
            }

            if (function.Problems.Length != 0)
            {
                WriteLine("problems");
                _indent++;
                foreach (var problem in function.Problems)
                    WriteLine(problem.Kind + ": " + problem.Message);
                _indent--;
            }

            _indent--;
        }

        private void WriteInstruction(LirInstruction instruction)
        {
            var line = instruction.Ordinal.ToString(CultureInfo.InvariantCulture).PadLeft(4, ' ') + ": ";
            switch (instruction.Kind)
            {
                case LirInstructionKind.Nop:
                    line += "nop";
                    break;

                case LirInstructionKind.Parameter:
                    line += FormatResult(instruction) + " = param";
                    if (!string.IsNullOrEmpty(instruction.Operator))
                        line += " " + instruction.Operator;
                    break;

                case LirInstructionKind.Copy:
                    line += FormatResult(instruction) + " = copy " + FormatOperand(instruction.Operands[0]);
                    break;

                case LirInstructionKind.ParallelCopy:
                    line += "parallelcopy ";
                    line += string.Join(", ", instruction.ParallelCopies.Select(move => move.Destination + " <- " + FormatOperand(move.Source)));
                    break;

                case LirInstructionKind.Constant:
                    line += FormatResult(instruction) + " = const " + FormatOperand(instruction.Operands[0]);
                    break;

                case LirInstructionKind.Zero:
                    line += FormatResult(instruction) + " = zero";
                    break;

                case LirInstructionKind.Unary:
                    line += FormatResult(instruction) + " = " + instruction.Operator + " " + FormatOperand(instruction.Operands[0]);
                    break;

                case LirInstructionKind.Binary:
                    line += FormatResult(instruction) + " = " + FormatOperand(instruction.Operands[0]) + " " + instruction.Operator + " " + FormatOperand(instruction.Operands[1]);
                    break;

                case LirInstructionKind.Convert:
                    line += FormatResult(instruction) + " = convert." + instruction.Operator + " " + FormatOperand(instruction.Operands[0]);
                    break;

                case LirInstructionKind.Cast:
                    line += FormatResult(instruction) + " = cast " + FormatOperand(instruction.Operands[0]);
                    break;

                case LirInstructionKind.AddressOf:
                    line += FormatResult(instruction) + " = addressof " + FormatAddress(instruction.Address);
                    break;

                case LirInstructionKind.Load:
                    line += FormatResult(instruction) + " = load " + FormatAddress(instruction.Address);
                    break;

                case LirInstructionKind.Store:
                    line += "store " + FormatAddress(instruction.Address) + " <- " + FormatOperand(instruction.Operands[0]);
                    break;

                case LirInstructionKind.ZeroMemory:
                    line += "zeromem " + FormatAddress(instruction.Address);
                    if (instruction.Operands.Length != 0)
                        line += " bytes=" + FormatOperand(instruction.Operands[0]);
                    break;

                case LirInstructionKind.Call:
                    if (instruction.Result is not null)
                        line += FormatResult(instruction) + " = ";
                    line += "call " + FormatOperand(instruction.Operands[0]) + "(" + string.Join(", ", instruction.Operands.Skip(1).Select(FormatOperand)) + ")";
                    break;

                case LirInstructionKind.VaStart:
                    if (instruction.Result is not null)
                        line += FormatResult(instruction) + " = ";
                    line += "vastart";
                    break;

                case LirInstructionKind.Jump:
                    line += "jump " + FormatBlock(instruction.Target);
                    break;

                case LirInstructionKind.Branch:
                    line += "branch " + FormatOperand(instruction.Operands[0]) + " ? " + FormatBlock(instruction.TrueTarget) + " : " + FormatBlock(instruction.FalseTarget);
                    break;

                case LirInstructionKind.Switch:
                    line += "switch " + FormatOperand(instruction.Operands[0]) + " default " + FormatBlock(instruction.Target);
                    if (instruction.SwitchCases.Length != 0)
                        line += " { " + string.Join(", ", instruction.SwitchCases.Select(c => FormatOperand(c.Value) + " -> " + FormatBlock(c.Target))) + " }";
                    break;

                case LirInstructionKind.Return:
                    line += instruction.Operands.Length == 0 ? "return" : "return " + FormatOperand(instruction.Operands[0]);
                    break;

                case LirInstructionKind.Unreachable:
                    line += "unreachable";
                    break;

                default:
                    line += instruction.Kind.ToString().ToLowerInvariant();
                    break;
            }

            if (_options.EmitValueNumberComments && instruction.ValueNumber is not null)
                line += " ; " + instruction.ValueNumber;

            WriteLine(line);
        }

        private static string FormatResult(LirInstruction instruction)
            => instruction.Result?.ToString() ?? "%void";

        private static string FormatOperand(LirOperand operand)
        {
            switch (operand.Kind)
            {
                case LirOperandKind.Register:
                    return operand.Register!.ToString();
                case LirOperandKind.Immediate:
                    return FormatImmediate(operand.Immediate);
                case LirOperandKind.Symbol:
                    return FormatSymbol(operand.Symbol);
                case LirOperandKind.StackSlot:
                    return operand.StackSlot!.ToString();
                case LirOperandKind.Address:
                    return FormatAddress(operand.Address);
                case LirOperandKind.Label:
                    return FormatBlock(operand.Label);
                case LirOperandKind.Undefined:
                    return operand.UndefinedName is null ? "undef" : "undef(" + operand.UndefinedName + ")";
                case LirOperandKind.Void:
                    return "void";
                default:
                    return "<none>";
            }
        }

        private static string FormatAddress(LirAddress? address)
        {
            if (address is null)
                return "<addr>";

            switch (address.Kind)
            {
                case LirAddressKind.StackSlot:
                    return "&" + address.StackSlot;
                case LirAddressKind.Symbol:
                    return "&" + FormatSymbol(address.Symbol);
                case LirAddressKind.Indirect:
                    return "*" + FormatOperand(address.BaseOperand ?? LirOperand.None);
                case LirAddressKind.Element:
                    return "element(" + FormatAddress(address.BaseAddress) +
                           (address.Index is null ? string.Empty : ", index=" + FormatOperand(address.Index)) +
                           ", scale=" + address.Scale.ToString(CultureInfo.InvariantCulture) + ")";
                case LirAddressKind.Field:
                    return "field(" + FormatAddress(address.BaseAddress) + ", ." +
                           (address.Field?.Name ?? "<field>") + ", +" +
                           address.Displacement.ToString(CultureInfo.InvariantCulture) + ")";
                default:
                    return "<addr>";
            }
        }

        private static string FormatSymbol(Symbol? symbol)
            => symbol is null ? "@<anonymous>" : "@" + symbol.Name;

        private static string FormatBlock(LirBlock? block)
            => block?.Name ?? "<missing>";

        private static string FormatImmediate(object? value)
        {
            if (value is null)
                return "null";

            if (value is string text)
                return "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

            if (value is char ch)
                return "'" + ch.ToString().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal) + "'";

            if (value is IFormattable formattable)
                return formattable.ToString(null, CultureInfo.InvariantCulture);

            return value.ToString() ?? string.Empty;
        }

        private void WriteLine(string text)
        {
            if (text.Length != 0)
                _builder.Append(' ', _indent * 2);
            _builder.AppendLine(text);
        }
    }

}
