using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Cnidaria.C
{
    public sealed class ValueNumberingOptions
    {
        public static ValueNumberingOptions Default { get; } = new ValueNumberingOptions();

        public bool CanonicalizeCommutativeComparisons { get; }
        public bool CanonicalizeRelationalComparisons { get; }
        public bool CanonicalizeCommutativeBitwiseOperators { get; }
        public bool CanonicalizeCommutativeIntegerArithmetic { get; }
        public bool TreatCallsAsUniqueValues { get; }
        public bool TreatMemoryDefinitionsAsUniqueStates { get; }
        public int MemorySearchBudget { get; }

        public ValueNumberingOptions(
            bool canonicalizeCommutativeComparisons = true,
            bool canonicalizeRelationalComparisons = true,
            bool canonicalizeCommutativeBitwiseOperators = true,
            bool canonicalizeCommutativeIntegerArithmetic = true,
            bool treatCallsAsUniqueValues = true,
            bool treatMemoryDefinitionsAsUniqueStates = true,
            int memorySearchBudget = 64)
        {
            CanonicalizeCommutativeComparisons = canonicalizeCommutativeComparisons;
            CanonicalizeRelationalComparisons = canonicalizeRelationalComparisons;
            CanonicalizeCommutativeBitwiseOperators = canonicalizeCommutativeBitwiseOperators;
            CanonicalizeCommutativeIntegerArithmetic = canonicalizeCommutativeIntegerArithmetic;
            TreatCallsAsUniqueValues = treatCallsAsUniqueValues;
            TreatMemoryDefinitionsAsUniqueStates = treatMemoryDefinitionsAsUniqueStates;
            MemorySearchBudget = Math.Clamp(memorySearchBudget, 0, 1024);
        }
    }

    public enum ValueNumberKind : byte
    {
        Unknown,
        Undefined,
        Entry,
        Constant,
        Expression,
        Phi,
        Memory,
        Unique,
        Error,
    }

    public enum ValueNumberOperation : ushort
    {
        Unknown,
        Undefined,
        Entry,
        Constant,
        Copy,
        Unary,
        Binary,
        Conversion,
        Cast,
        AddressOf,
        SymbolAddress,
        TemporaryAddress,
        DirectMemoryRead,
        IndirectLoad,
        ElementAccess,
        MemberAccess,
        Call,
        Phi,
        ZeroInitialize,
        StatementResult,
        MemoryEntry,
        MemoryDef,
        Error,
    }

    [Flags]
    public enum ValueNumberFlags : byte
    {
        None = 0,
        ReadsMemory = 1,
        WritesMemory = 2,
        ContainsCall = 4,
        Unique = 8,
    }

    public sealed class GimpleValueNumbering
    {
        private readonly Dictionary<GimpleName, ValueNumber> _nameNumbers;
        private readonly Dictionary<GimpleOperandInfo, ValueNumber> _expressionNumbers;
        private readonly Dictionary<GimpleDefinition, ValueNumber> _definitionNumbers;
        private readonly Dictionary<GimplePhi, ValueNumber> _phiNumbers;

        public GimpleFunctionAnnotations Function { get; }
        public ImmutableArray<ValueNumber> ValueNumbers { get; }

        internal GimpleValueNumbering(
            GimpleFunctionAnnotations function,
            ImmutableArray<ValueNumber> valueNumbers,
            Dictionary<GimpleName, ValueNumber> nameNumbers,
            Dictionary<GimpleOperandInfo, ValueNumber> expressionNumbers,
            Dictionary<GimpleDefinition, ValueNumber> definitionNumbers,
            Dictionary<GimplePhi, ValueNumber> phiNumbers)
        {
            Function = function ?? throw new ArgumentNullException(nameof(function));
            ValueNumbers = valueNumbers.IsDefault ? ImmutableArray<ValueNumber>.Empty : valueNumbers;
            _nameNumbers = nameNumbers is null
                ? new Dictionary<GimpleName, ValueNumber>()
                : new Dictionary<GimpleName, ValueNumber>(nameNumbers);
            _expressionNumbers = expressionNumbers is null
                ? new Dictionary<GimpleOperandInfo, ValueNumber>()
                : new Dictionary<GimpleOperandInfo, ValueNumber>(expressionNumbers);
            _definitionNumbers = definitionNumbers is null
                ? new Dictionary<GimpleDefinition, ValueNumber>()
                : new Dictionary<GimpleDefinition, ValueNumber>(definitionNumbers);
            _phiNumbers = phiNumbers is null
                ? new Dictionary<GimplePhi, ValueNumber>()
                : new Dictionary<GimplePhi, ValueNumber>(phiNumbers);
        }

        internal static GimpleValueNumbering Build(GimpleFunctionAnnotations function, ValueNumberingOptions? options = null)
            => new ValueNumberingBuilder(function, options ?? ValueNumberingOptions.Default).Build();

        public bool TryGetValueNumber(GimpleName name, out ValueNumber? valueNumber)
        {
            if (name is null)
                throw new ArgumentNullException(nameof(name));

            return _nameNumbers.TryGetValue(name, out valueNumber);
        }

        public bool TryGetValueNumber(GimpleOperandInfo expression, out ValueNumber? valueNumber)
        {
            if (expression is null)
                throw new ArgumentNullException(nameof(expression));

            return _expressionNumbers.TryGetValue(expression, out valueNumber);
        }

        public bool TryGetValueNumber(GimpleDefinition definition, out ValueNumber? valueNumber)
        {
            if (definition is null)
                throw new ArgumentNullException(nameof(definition));

            return _definitionNumbers.TryGetValue(definition, out valueNumber);
        }

        public bool TryGetValueNumber(GimplePhi phi, out ValueNumber? valueNumber)
        {
            if (phi is null)
                throw new ArgumentNullException(nameof(phi));

            return _phiNumbers.TryGetValue(phi, out valueNumber);
        }

        public bool AreEquivalent(GimpleName left, GimpleName right)
        {
            if (left is null)
                throw new ArgumentNullException(nameof(left));
            if (right is null)
                throw new ArgumentNullException(nameof(right));

            return TryGetValueNumber(left, out var leftNumber) &&
                   TryGetValueNumber(right, out var rightNumber) &&
                   ReferenceEquals(leftNumber, rightNumber);
        }

        public bool TryGetConstantValue(GimpleName name, out object? value)
        {
            if (name is null)
                throw new ArgumentNullException(nameof(name));

            if (_nameNumbers.TryGetValue(name, out var number) && number.HasConstantValue)
            {
                value = number.ConstantValue;
                return true;
            }

            value = null;
            return false;
        }

        public bool TryGetConstantValue(GimpleOperandInfo expression, out object? value)
        {
            if (expression is null)
                throw new ArgumentNullException(nameof(expression));

            if (_expressionNumbers.TryGetValue(expression, out var number) && number.HasConstantValue)
            {
                value = number.ConstantValue;
                return true;
            }

            value = null;
            return false;
        }
    }

    public sealed class ValueNumber
    {
        public int Id { get; }
        public ValueNumberKind Kind { get; }
        public ValueNumberOperation Operation => Key.Operation;
        public QualifiedType Type { get; }
        public ValueNumberKey Key { get; }
        public ImmutableArray<ValueNumber> Operands { get; private set; }
        public ValueNumberFlags Flags { get; }
        public bool IsMemoryDependent => (Flags & ValueNumberFlags.ReadsMemory) != 0;
        public bool IsUnique => (Flags & ValueNumberFlags.Unique) != 0;
        public bool HasConstantValue { get; }
        public object? ConstantValue { get; }
        public string Display { get; }

        internal ValueNumber(
            int id,
            ValueNumberKind kind,
            QualifiedType type,
            ValueNumberKey key,
            ImmutableArray<ValueNumber> operands,
            ValueNumberFlags flags,
            string display,
            bool hasConstantValue = false,
            object? constantValue = null)
        {
            if (id < 0)
                throw new ArgumentOutOfRangeException(nameof(id));

            Id = id;
            Kind = kind;
            Type = GimpleTypeHelpers.Normalize(type);
            Key = key;
            Operands = operands.IsDefault ? ImmutableArray<ValueNumber>.Empty : operands;
            Flags = flags;
            HasConstantValue = hasConstantValue;
            ConstantValue = constantValue;
            Display = string.IsNullOrWhiteSpace(display)
                ? $"vn{id.ToString(CultureInfo.InvariantCulture)}"
                : display;
        }

        internal void SetOperands(ImmutableArray<ValueNumber> operands)
            => Operands = operands.IsDefault ? ImmutableArray<ValueNumber>.Empty : operands;

        public override string ToString()
            => $"vn{Id.ToString(CultureInfo.InvariantCulture)}";
    }

    public readonly struct ValueNumberKey : IEquatable<ValueNumberKey>
    {
        public ValueNumberOperation Operation { get; }
        public string TypeKey { get; }
        public string OperatorKey { get; }
        public int MemoryInputId { get; }
        public int BlockOrdinal { get; }
        public int Discriminator { get; }
        public ImmutableArray<int> OperandIds { get; }

        internal ValueNumberKey(
            ValueNumberOperation operation,
            string typeKey,
            string operatorKey,
            ImmutableArray<int> operandIds,
            int memoryInputId = -1,
            int blockOrdinal = -1,
            int discriminator = -1)
        {
            Operation = operation;
            TypeKey = typeKey ?? string.Empty;
            OperatorKey = operatorKey ?? string.Empty;
            OperandIds = operandIds.IsDefault ? ImmutableArray<int>.Empty : operandIds;
            MemoryInputId = memoryInputId;
            BlockOrdinal = blockOrdinal;
            Discriminator = discriminator;
        }

        public bool Equals(ValueNumberKey other)
        {
            if (Operation != other.Operation ||
                MemoryInputId != other.MemoryInputId ||
                BlockOrdinal != other.BlockOrdinal ||
                Discriminator != other.Discriminator ||
                !StringComparer.Ordinal.Equals(TypeKey, other.TypeKey) ||
                !StringComparer.Ordinal.Equals(OperatorKey, other.OperatorKey) ||
                OperandIds.Length != other.OperandIds.Length)
            {
                return false;
            }

            for (var i = 0; i < OperandIds.Length; i++)
            {
                if (OperandIds[i] != other.OperandIds[i])
                    return false;
            }

            return true;
        }

        public override bool Equals(object? obj)
            => obj is ValueNumberKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)Operation;
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(TypeKey);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(OperatorKey);
                hash = (hash * 397) ^ MemoryInputId;
                hash = (hash * 397) ^ BlockOrdinal;
                hash = (hash * 397) ^ Discriminator;
                for (var i = 0; i < OperandIds.Length; i++)
                    hash = (hash * 397) ^ OperandIds[i];
                return hash;
            }
        }

        public override string ToString()
        {
            var operands = OperandIds.Length == 0
                ? string.Empty
                : ":" + string.Join(",", OperandIds.Select(static id => id.ToString(CultureInfo.InvariantCulture)));
            var memory = MemoryInputId < 0 ? string.Empty : ":mem" + MemoryInputId.ToString(CultureInfo.InvariantCulture);
            var block = BlockOrdinal < 0 ? string.Empty : ":b" + BlockOrdinal.ToString(CultureInfo.InvariantCulture);
            var discriminator = Discriminator < 0 ? string.Empty : ":#" + Discriminator.ToString(CultureInfo.InvariantCulture);
            return Operation + ":" + TypeKey + ":" + OperatorKey + operands + memory + block + discriminator;
        }
    }

    internal sealed class ValueNumberingBuilder
    {
        private readonly GimpleFunctionAnnotations _function;
        private readonly ValueNumberingOptions _options;
        private readonly Dictionary<ValueNumberKey, ValueNumber> _canonicalNumbers = new();
        private readonly Dictionary<GimpleName, ValueNumber> _nameNumbers = new();
        private readonly Dictionary<GimpleOperandInfo, ValueNumber> _expressionNumbers = new();
        private readonly Dictionary<GimpleDefinition, ValueNumber> _definitionNumbers = new();
        private readonly Dictionary<GimplePhi, ValueNumber> _phiNumbers = new();
        private readonly HashSet<GimplePhi> _stablePhis = new();
        private readonly List<ValueNumber> _numbers = new();
        private readonly Dictionary<QualifiedType, string> _typeKeys = new();
        private int _uniqueOrdinal;

        private readonly record struct MemoryAddress(int Root, long Offset);
        private readonly record struct MemoryLocation(MemoryAddress Address, int Size, string Type);
        private readonly record struct MemoryStore(ValueNumber Input, MemoryLocation Location, ValueNumber? Value);

        private readonly Dictionary<object, int> _objectRoots = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<int, MemoryAddress> _pointerAddresses = new();
        private readonly Dictionary<int, MemoryStore> _memoryStores = new();
        private readonly Dictionary<int, GimplePhi> _memoryPhis = new();
        private readonly Dictionary<(int Memory, MemoryLocation Location), ValueNumber> _memoryReads = new();
        private readonly Dictionary<FieldSymbol, long> _fieldOffsets = new();

        public ValueNumberingBuilder(GimpleFunctionAnnotations function, ValueNumberingOptions options)
        {
            _function = function ?? throw new ArgumentNullException(nameof(function));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public GimpleValueNumbering Build()
        {
            NumberInitialDefinitions();

            foreach (var controlFlowBlock in _function.ControlFlowFunction.ReversePostOrder)
            {
                if (controlFlowBlock.IsExit || !controlFlowBlock.IsReachable)
                    continue;

                if (!_function.TryGetBlock(controlFlowBlock, out var block) || block is null)
                    continue;

                foreach (var phi in block.Phis)
                    NumberPhi(phi);

                foreach (var instruction in block.Statements)
                    NumberInstruction(instruction);
            }

            RefreshStablePhiOperands();

            return new GimpleValueNumbering(
                _function,
                _numbers.ToImmutableArray(),
                _nameNumbers,
                _expressionNumbers,
                _definitionNumbers,
                _phiNumbers);
        }

        private void NumberInitialDefinitions()
        {
            foreach (var definition in _function.Definitions)
            {
                switch (definition.Kind)
                {
                    case GimpleDefinitionKind.Undefined:
                        AssignDefinition(
                            definition,
                            GetCanonicalNumber(
                                ValueNumberKind.Undefined,
                                definition.Name.Type,
                                ValueNumberOperation.Undefined,
                                "v" + definition.Name.Variable.Ordinal.ToString(CultureInfo.InvariantCulture),
                                ImmutableArray<ValueNumber>.Empty));
                        break;

                    case GimpleDefinitionKind.Entry when definition.Name.Variable.Kind == GimpleVariableKind.Memory:
                        AssignDefinition(
                            definition,
                            GetCanonicalNumber(
                                ValueNumberKind.Memory,
                                definition.Name.Type,
                                ValueNumberOperation.MemoryEntry,
                                "entry-memory",
                                ImmutableArray<ValueNumber>.Empty));
                        break;

                    case GimpleDefinitionKind.Entry:
                        AssignDefinition(
                            definition,
                            GetCanonicalNumber(
                                ValueNumberKind.Entry,
                                definition.Name.Type,
                                ValueNumberOperation.Entry,
                                "v" + definition.Name.Variable.Ordinal.ToString(CultureInfo.InvariantCulture),
                                ImmutableArray<ValueNumber>.Empty));
                        break;
                }
            }
        }

        private void NumberPhi(GimplePhi phi)
        {
            if (phi.Operands.Length == 0)
            {
                var emptyPhi = GetUniqueNumber(
                    ValueNumberKind.Phi,
                    phi.Result.Type,
                    ValueNumberOperation.Phi,
                    "empty:v" + phi.Variable.Ordinal.ToString(CultureInfo.InvariantCulture),
                    ImmutableArray<ValueNumber>.Empty,
                    ValueNumberFlags.Unique,
                    blockOrdinal: phi.Block.Ordinal);
                AssignPhi(phi, emptyPhi);
                return;
            }

            var operandNumbers = phi.Operands.Select(operand => GetNameNumber(operand.Value)).ToImmutableArray();
            var first = operandNumbers[0];
            if (operandNumbers.All(number => ReferenceEquals(number, first)))
            {
                AssignPhi(phi, first);
                return;
            }

            var key = new ValueNumberKey(
                ValueNumberOperation.Phi,
                TypeKey(phi.Result.Type),
                "v" + phi.Variable.Ordinal.ToString(CultureInfo.InvariantCulture) + ":" + phi.Result.Version.ToString(CultureInfo.InvariantCulture),
                ImmutableArray<int>.Empty,
                blockOrdinal: phi.Block.Ordinal);

            var number = GetOrCreateNumber(
                key,
                ValueNumberKind.Phi,
                phi.Result.Type,
                operandNumbers,
                ValueNumberFlags.None,
                "phi " + phi.Result.ToString(),
                hasConstantValue: false,
                constantValue: null);

            _stablePhis.Add(phi);
            if (phi.Variable.Kind == GimpleVariableKind.Memory)
                _memoryPhis[number.Id] = phi;
            AssignPhi(phi, number);
        }

        private void RefreshStablePhiOperands()
        {
            foreach (var phi in _stablePhis)
            {
                if (!_phiNumbers.TryGetValue(phi, out var number))
                    continue;

                var operands = phi.Operands
                    .Select(operand => GetNameNumber(operand.Value))
                    .ToImmutableArray();
                number.SetOperands(operands);
            }
        }

        private void NumberInstruction(GimpleStatementAnnotations instruction)
        {
            var memoryInput = instruction.MemoryInput is null ? null : GetNameNumber(instruction.MemoryInput);
            var expressionNumbers = instruction.Operands
                .Select(expression => NumberExpression(expression, memoryInput))
                .ToImmutableArray();

            foreach (var definition in instruction.Definitions)
            {
                if (definition.Name.Variable.Kind == GimpleVariableKind.Memory)
                {
                    AssignDefinition(definition, NumberMemoryDefinition(instruction, definition, memoryInput, expressionNumbers));
                    continue;
                }

                AssignDefinition(definition, NumberStatementDefinition(instruction, definition, expressionNumbers));
            }

            RecordMemoryDefinition(instruction, memoryInput, expressionNumbers);
        }

        private ValueNumber NumberStatementDefinition(
            GimpleStatementAnnotations instruction,
            GimpleDefinition definition,
            ImmutableArray<ValueNumber> expressionNumbers)
        {
            if (instruction.Statement is GimpleAssignStatement assign &&
                (instruction.Flags & GimpleStatementFlags.WritesMemory) == 0)
            {
                var start = expressionNumbers.Length - assign.Operands.Length;
                if (start >= 0)
                    return NumberAssignDefinition(assign, definition.Name.Type, expressionNumbers, start);
            }

            var flags = TranslateFlags(instruction.Flags) | ValueNumberFlags.Unique;
            return GetUniqueNumber(
                ValueNumberKind.Unique,
                definition.Name.Type,
                ValueNumberOperation.StatementResult,
                "i" + instruction.Ordinal.ToString(CultureInfo.InvariantCulture),
                expressionNumbers,
                flags,
                blockOrdinal: instruction.Block.Ordinal);
        }

        /// <summary>Numbers an assignment from its subcode and the numbers of its right-hand side operands</summary>
        private ValueNumber NumberAssignDefinition(
            GimpleAssignStatement assign,
            QualifiedType resultType,
            ImmutableArray<ValueNumber> expressionNumbers,
            int start)
        {
            if (assign.IsConstructor)
            {
                if (IsIntegerLike(resultType))
                    return NumberConstantValue(0, resultType);

                return GetCanonicalNumber(
                    ValueNumberKind.Constant,
                    resultType,
                    ValueNumberOperation.ZeroInitialize,
                    "zero",
                    ImmutableArray<ValueNumber>.Empty);
            }

            var operands = ImmutableArray.CreateBuilder<ValueNumber>(assign.Operands.Length);
            for (var i = 0; i < assign.Operands.Length; i++)
                operands.Add(expressionNumbers[start + i]);

            var operandArray = operands.ToImmutable();

            if (assign.RhsClass == GimpleRhsClass.Single)
                return operandArray[0];

            if (TryFoldAssignNumber(assign, operandArray, out var folded))
                return folded;

            var operation = OperationOf(assign.Subcode);
            var operatorKey = GimpleOperators.Name(assign.Subcode);
            CanonicalizeAssignOperands(assign, ref operatorKey, ref operandArray);

            return GetCanonicalNumber(
                ValueNumberKind.Expression,
                assign.Lhs.Type,
                operation,
                operatorKey,
                operandArray);
        }

        private static ValueNumberOperation OperationOf(GimpleTreeCode code)
        {
            if (GimpleOperators.IsConversion(code))
                return code == GimpleTreeCode.ViewConvertExpr ? ValueNumberOperation.Cast : ValueNumberOperation.Conversion;

            return GimpleOperators.ClassOf(code) switch
            {
                GimpleTreeCodeClass.Unary => ValueNumberOperation.Unary,
                GimpleTreeCodeClass.Binary => ValueNumberOperation.Binary,
                GimpleTreeCodeClass.Comparison => ValueNumberOperation.Binary,
                _ => ValueNumberOperation.Unknown,
            };
        }

        // Relational and commutative forms collapse onto one canonical operand order
        private void CanonicalizeAssignOperands(
            GimpleAssignStatement assign,
            ref string operatorKey,
            ref ImmutableArray<ValueNumber> operands)
        {
            if (operands.Length != 2)
                return;

            if (_options.CanonicalizeRelationalComparisons)
            {
                switch (assign.Subcode)
                {
                    case GimpleTreeCode.LtExpr:
                        operatorKey = "rel:lt";
                        return;

                    case GimpleTreeCode.GtExpr:
                        operatorKey = "rel:lt";
                        operands = ImmutableArray.Create(operands[1], operands[0]);
                        return;

                    case GimpleTreeCode.LeExpr:
                        operatorKey = "rel:le";
                        return;

                    case GimpleTreeCode.GeExpr:
                        operatorKey = "rel:le";
                        operands = ImmutableArray.Create(operands[1], operands[0]);
                        return;
                }
            }

            if (!IsCanonicalizedCommutative(assign.Subcode, assign.Operands[0].Type, assign.Operands[1].Type) ||
                operands[0].Id <= operands[1].Id)
            {
                return;
            }

            operands = ImmutableArray.Create(operands[1], operands[0]);
        }

        private ValueNumber NumberMemoryDefinition(
            GimpleStatementAnnotations instruction,
            GimpleDefinition definition,
            ValueNumber? memoryInput,
            ImmutableArray<ValueNumber> expressionNumbers)
        {
            var operands = ImmutableArray.CreateBuilder<ValueNumber>();
            if (memoryInput is not null)
                operands.Add(memoryInput);
            operands.AddRange(expressionNumbers);

            var flags = TranslateFlags(instruction.Flags) | ValueNumberFlags.WritesMemory;
            if (_options.TreatMemoryDefinitionsAsUniqueStates)
            {
                flags |= ValueNumberFlags.Unique;
                return GetUniqueNumber(
                    ValueNumberKind.Memory,
                    definition.Name.Type,
                    ValueNumberOperation.MemoryDef,
                    "i" + instruction.Ordinal.ToString(CultureInfo.InvariantCulture),
                    operands.ToImmutable(),
                    flags,
                    blockOrdinal: instruction.Block.Ordinal);
            }

            return GetCanonicalNumber(
                ValueNumberKind.Memory,
                definition.Name.Type,
                ValueNumberOperation.MemoryDef,
                "store",
                operands.ToImmutable(),
                flags,
                blockOrdinal: instruction.Block.Ordinal);
        }

        private ValueNumber NumberExpression(GimpleOperandInfo expression, ValueNumber? memoryInput)
        {
            if (_expressionNumbers.TryGetValue(expression, out var existing))
                return existing;

            ValueNumber number;
            if (expression.Name is not null)
            {
                number = expression.IsAddress
                    ? NumberGimpleAddress(expression.Name)
                    : GetNameNumber(expression.Name);
            }
            else if (expression.Original is GimpleConstantValue constant)
            {
                number = NumberConstant(constant);
            }
            else if (expression.Original is GimpleErrorValue)
            {
                number = GetUniqueNumber(
                    ValueNumberKind.Error,
                    expression.Original.Type,
                    ValueNumberOperation.Error,
                    "error",
                    ImmutableArray<ValueNumber>.Empty,
                    ValueNumberFlags.Unique);
            }
            else
            {
                var childNumbers = expression.Children
                    .Select(child => NumberExpression(child, memoryInput))
                    .ToImmutableArray();

                number = NumberCompositeExpression(expression, childNumbers, memoryInput);
            }

            _expressionNumbers.Add(expression, number);
            return number;
        }

        private ValueNumber NumberGimpleAddress(GimpleName name)
        {
            var operation = name.Variable.Kind == GimpleVariableKind.Temporary
                ? ValueNumberOperation.TemporaryAddress
                : ValueNumberOperation.SymbolAddress;

            return GetCanonicalNumber(
                ValueNumberKind.Expression,
                name.Type,
                operation,
                "v" + name.Variable.Ordinal.ToString(CultureInfo.InvariantCulture),
                ImmutableArray<ValueNumber>.Empty);
        }

        private ValueNumber NumberConstant(GimpleConstantValue constant)
            => NumberConstantValue(constant.Value, constant.Type);

        private ValueNumber NumberConstantValue(object? value, QualifiedType type)
            => GetCanonicalNumber(
                ValueNumberKind.Constant,
                type,
                ValueNumberOperation.Constant,
                NormalizeConstantKey(value),
                ImmutableArray<ValueNumber>.Empty,
                hasConstantValue: true,
                constantValue: value);

        private ValueNumber NumberCompositeExpression(
            GimpleOperandInfo expression,
            ImmutableArray<ValueNumber> childNumbers,
            ValueNumber? memoryInput)
        {
            var flags = TranslateFlags(expression);
            if (TryNumberMemoryExpression(expression, memoryInput, out var memoryNumber))
                return memoryNumber;
            if (expression.ReadsMemory && IsVolatileOrAtomic(expression.Original.Type))
            {
                return GetUniqueNumber(
                    ValueNumberKind.Unique,
                    expression.Original.Type,
                    GetOperation(expression),
                    GetOperatorKey(expression.Original),
                    childNumbers,
                    flags | ValueNumberFlags.Unique);
            }

            if (expression.ReadsMemory && memoryInput is null)
            {
                return GetUniqueNumber(
                    ValueNumberKind.Unique,
                    expression.Original.Type,
                    GetOperation(expression),
                    GetOperatorKey(expression.Original),
                    childNumbers,
                    flags | ValueNumberFlags.Unique);
            }

            if (expression.ContainsCall && _options.TreatCallsAsUniqueValues)
            {
                return GetUniqueNumber(
                    ValueNumberKind.Unique,
                    expression.Original.Type,
                    ValueNumberOperation.Call,
                    GetOperatorKey(expression.Original),
                    childNumbers,
                    flags | ValueNumberFlags.Unique);
            }

            if (expression.WritesMemory)
            {
                return GetUniqueNumber(
                    ValueNumberKind.Unique,
                    expression.Original.Type,
                    GetOperation(expression),
                    GetOperatorKey(expression.Original),
                    childNumbers,
                    flags | ValueNumberFlags.Unique);
            }

            if (expression.IsAddress && expression.Original is GimpleIndirectExpression && childNumbers.Length == 1)
                return childNumbers[0];

            if (expression.Original is GimpleConversionExpression { ConversionKind: GimpleConversionKind.Identity } conversion &&
                childNumbers.Length == 1 &&
                SameType(conversion.Operand.Type, conversion.Type))
            {
                return childNumbers[0];
            }

            if (expression.Original is GimpleCastExpression cast &&
                childNumbers.Length == 1 &&
                SameType(cast.Operand.Type, cast.Type))
            {
                return childNumbers[0];
            }

            var operation = GetOperation(expression);
            var operatorKey = GetOperatorKey(expression.Original);
            var operands = childNumbers;
            var memoryInputId = expression.ReadsMemory ? memoryInput?.Id ?? -1 : -1;

            return GetCanonicalNumber(
                ValueNumberKind.Expression,
                expression.Original.Type,
                operation,
                operatorKey,
                operands,
                flags,
                memoryInputId: memoryInputId);
        }

        private bool TryNumberMemoryExpression(GimpleOperandInfo expression, ValueNumber? memoryInput, out ValueNumber number)
        {
            number = null!;
            if (expression.IsAddress || expression.Name is not null || !expression.ReadsMemory ||
                expression.Original is not GimplePlace || !IsMemoryScalar(expression.Original.Type) ||
                IsVolatileOrAtomic(expression.Original.Type) || memoryInput is null ||
                !TryGetLocation(expression, memoryInput, out var location))
                return false;

            var budget = _options.MemorySearchBudget;
            number = SelectMemory(memoryInput, location, expression.Original.Type, ref budget);
            return true;
        }

        private ValueNumber SelectMemory(ValueNumber memory, MemoryLocation location, QualifiedType type, ref int budget)
        {
            var key = (memory.Id, location);
            if (_memoryReads.TryGetValue(key, out var cached))
                return cached;

            var fallback = GetCanonicalNumber(ValueNumberKind.Expression, type, ValueNumberOperation.IndirectLoad,
                $"load:{location.Address.Root}:{location.Address.Offset}:{location.Size}",
                ImmutableArray<ValueNumber>.Empty, ValueNumberFlags.ReadsMemory, memoryInputId: memory.Id);
            if (budget-- <= 0)
                return fallback;

            // Seed the query before following phis, including loop backedges.
            _memoryReads[key] = fallback;
            var result = fallback;
            if (_memoryStores.TryGetValue(memory.Id, out var store))
            {
                if (store.Location.Equals(location) && store.Value is not null && SameType(store.Value.Type, type))
                    result = store.Value;
                else if (Disjoint(store.Location, location))
                    result = SelectMemory(store.Input, location, type, ref budget);
            }
            else if (_memoryPhis.TryGetValue(memory.Id, out var phi))
            {
                ValueNumber? common = null;
                foreach (var operand in phi.Operands)
                {
                    if (budget-- <= 0 || !_nameNumbers.TryGetValue(operand.Value, out var input) || input.Kind == ValueNumberKind.Unknown)
                    {
                        common = null;
                        break;
                    }
                    var value = SelectMemory(input, location, type, ref budget);
                    if (common is not null && !ReferenceEquals(common, value))
                    {
                        common = null;
                        break;
                    }
                    common = value;
                }
                result = common ?? fallback;
            }

            _memoryReads[key] = result;
            return result;
        }

        private static bool Disjoint(MemoryLocation left, MemoryLocation right)
        {
            if (left.Address.Root != right.Address.Root)
                return left.Address.Root < 0 && right.Address.Root < 0;
            return left.Address.Offset <= right.Address.Offset
                ? (ulong)right.Address.Offset - (ulong)left.Address.Offset >= (ulong)left.Size
                : (ulong)left.Address.Offset - (ulong)right.Address.Offset >= (ulong)right.Size;
        }

        private void RecordMemoryDefinition(GimpleStatementAnnotations instruction, ValueNumber? memoryInput,
            ImmutableArray<ValueNumber> expressionNumbers)
        {
            if (instruction.Statement is not GimpleAssignStatement assign)
                return;

            foreach (var definition in instruction.Definitions)
            {
                if (definition.Name.Type.Type is PointerType && definition.Name.Variable.Kind != GimpleVariableKind.Memory &&
                    _nameNumbers.TryGetValue(definition.Name, out var number) &&
                    TryGetAssignedAddress(assign, instruction.Operands, expressionNumbers, memoryInput, out var address))
                    _pointerAddresses[number.Id] = address;
            }

            if (instruction.MemoryOutput is null || memoryInput is null || instruction.Operands.Length == 0 ||
                !instruction.Operands[0].IsAddress || IsVolatileOrAtomic(assign.Lhs.Type) ||
                GimpleMemoryEffects.HasOrderedAccess(instruction.Statement) ||
                !TryGetLocation(instruction.Operands[0], memoryInput, out var location))
                return;

            ValueNumber? value = null;
            if (IsMemoryScalar(assign.Lhs.Type))
            {
                var start = expressionNumbers.Length - assign.Operands.Length;
                if (start >= 0)
                {
                    value = NumberAssignDefinition(assign, assign.Lhs.Type, expressionNumbers, start);
                    if (!SameType(value.Type, assign.Lhs.Type))
                        value = null;
                }
            }
            _memoryStores[GetNameNumber(instruction.MemoryOutput).Id] = new MemoryStore(memoryInput, location, value);
        }

        private bool TryGetLocation(GimpleOperandInfo expression, ValueNumber? memory, out MemoryLocation location)
        {
            location = default;
            try
            {
                var size = _function.Target.SizeOf(expression.Original.Type);
                if (size <= 0 || !TryGetPlaceAddress(expression, memory, out var address))
                    return false;
                location = new MemoryLocation(address, size, TypeKey(expression.Original.Type));
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private bool TryGetPlaceAddress(GimpleOperandInfo expression, ValueNumber? memory, out MemoryAddress address)
        {
            address = default;
            switch (expression.Original)
            {
                case GimpleSymbolValue symbol:
                    address = ObjectAddress(symbol.Symbol);
                    return true;
                case GimpleTemporaryValue temporary:
                    address = ObjectAddress(temporary);
                    return true;
                case GimpleName name:
                    address = ObjectAddress((object?)name.Variable.Symbol ?? name.Variable.Temporary ?? (object)name.Variable);
                    return true;
                case GimpleIndirectExpression when expression.Children.Length == 1:
                    return TryGetPointerAddress(expression.Children[0], memory, out address);
                case GimpleMemberAccessExpression { Field: not null } member when expression.Children.Length == 1:
                    if (!(member.ThroughPointer
                        ? TryGetPointerAddress(expression.Children[0], memory, out address)
                        : TryGetPlaceAddress(expression.Children[0], memory, out address)))
                        return false;
                    address = address with { Offset = checked(address.Offset + FieldOffset(member.Field)) };
                    return true;
                case GimpleElementAccessExpression element when expression.Children.Length == 2:
                    if (!(element.Expression.Type.Type is PointerType
                        ? TryGetPointerAddress(expression.Children[0], memory, out address)
                        : TryGetPlaceAddress(expression.Children[0], memory, out address)))
                        return false;
                    var index = NumberExpression(expression.Children[1], memory);
                    var scale = _function.Target.SizeOf(element.Type);
                    if (scale <= 0)
                        return false;
                    address = IndexAddress(address, index, scale);
                    return true;
                default:
                    return false;
            }
        }

        private bool TryGetPointerAddress(GimpleOperandInfo expression, ValueNumber? memory, out MemoryAddress address)
        {
            if (expression.Original is GimpleAddressOfExpression && expression.Children.Length == 1)
                return TryGetPlaceAddress(expression.Children[0], memory, out address);
            if (expression.Original.Type.Type is ArrayType)
                return TryGetPlaceAddress(expression, memory, out address);
            var number = NumberExpression(expression, memory);
            address = _pointerAddresses.TryGetValue(number.Id, out var known) ? known : new MemoryAddress(number.Id, 0);
            return expression.Original.Type.Type is PointerType;
        }

        private bool TryGetAssignedAddress(GimpleAssignStatement assign, ImmutableArray<GimpleOperandInfo> expressions,
            ImmutableArray<ValueNumber> numbers, ValueNumber? memory, out MemoryAddress address)
        {
            address = default;
            var start = expressions.Length - assign.Operands.Length;
            if (start < 0 || assign.Operands.Length == 0)
                return false;
            try
            {
                if (assign.RhsClass == GimpleRhsClass.Single)
                    return TryGetPointerAddress(expressions[start], memory, out address);
                if (assign.Subcode == GimpleTreeCode.AddrExpr)
                    return TryGetPlaceAddress(expressions[start], memory, out address);
                if (assign.Operands.Length == 1 && GimpleOperators.IsConversion(assign.Subcode) &&
                    assign.Operands[0].Type.Type is PointerType)
                    return TryGetPointerAddress(expressions[start], memory, out address);
                if (assign.Subcode == GimpleTreeCode.PointerPlusExpr && numbers.Length > start + 1)
                {
                    var pointerIndex = assign.Operands[0].Type.Type is PointerType ? start : start + 1;
                    var index = pointerIndex == start ? start + 1 : start;
                    if (expressions[pointerIndex].Original.Type.Type is not PointerType pointer ||
                        !TryGetPointerAddress(expressions[pointerIndex], memory, out address))
                        return false;
                    var scale = Math.Max(1, _function.Target.SizeOf(pointer.PointeeType));
                    address = IndexAddress(address, numbers[index], scale);
                    return true;
                }
            }
            catch (OverflowException) { }
            return false;
        }

        private MemoryAddress IndexAddress(MemoryAddress address, ValueNumber index, int scale)
        {
            if (TryGetIntegerConstant(index, out var offset))
                return address with { Offset = checked(address.Offset + checked(offset * scale)) };
            var indexed = GetCanonicalNumber(ValueNumberKind.Expression, new QualifiedType(TypeCatalog.Instance.Void),
                ValueNumberOperation.ElementAccess, $"address:{address.Root}:{address.Offset}:{scale}", ImmutableArray.Create(index));
            return new MemoryAddress(indexed.Id, 0);
        }

        private MemoryAddress ObjectAddress(object value)
        {
            if (!_objectRoots.TryGetValue(value, out var root))
            {
                root = -1 - _objectRoots.Count;
                _objectRoots.Add(value, root);
            }
            return new MemoryAddress(root, 0);
        }

        private long FieldOffset(FieldSymbol field)
        {
            if (_fieldOffsets.TryGetValue(field, out var offset))
                return offset;
            offset = 0;
            foreach (var member in field.ContainingTag.Fields)
            {
                var alignment = Math.Max(1, _function.Target.AlignOf(member.Type));
                offset = checked((offset + alignment - 1) / alignment * alignment);
                _fieldOffsets[member] = offset;
                if (field.ContainingTag.TagKind != TagKind.Union)
                    offset = checked(offset + _function.Target.SizeOf(member.Type));
            }
            return _fieldOffsets.TryGetValue(field, out offset) ? offset : 0;
        }

        private static bool IsMemoryScalar(QualifiedType type)
            => type.Type.Kind is TypeKind.Builtin or TypeKind.Enum or TypeKind.Pointer;

        /// <summary>Folds an assignment into an existing number when its subcode admits an identity</summary>
        private bool TryFoldAssignNumber(
            GimpleAssignStatement assign,
            ImmutableArray<ValueNumber> operands,
            out ValueNumber folded)
        {
            switch (assign.RhsClass)
            {
                case GimpleRhsClass.Unary when operands.Length == 1:
                    return TryFoldUnaryNumber(assign, operands[0], out folded);

                case GimpleRhsClass.Binary when operands.Length == 2:
                    return TryFoldBinaryNumber(assign, operands, out folded);
            }

            folded = null!;
            return false;
        }

        private bool TryFoldUnaryNumber(GimpleAssignStatement assign, ValueNumber operandNumber, out ValueNumber folded)
        {
            switch (assign.Subcode)
            {
                case GimpleTreeCode.NopExpr when SameType(assign.Operands[0].Type, assign.Lhs.Type):
                    folded = operandNumber;
                    return true;

                case GimpleTreeCode.TruthNotExpr when TryGetIntegerConstant(operandNumber, out var value):
                    folded = NumberConstantValue(value == 0 ? 1 : 0, assign.Lhs.Type);
                    return true;
            }

            folded = null!;
            return false;
        }

        private bool TryFoldBinaryNumber(
            GimpleAssignStatement assign,
            ImmutableArray<ValueNumber> childNumbers,
            out ValueNumber folded)
        {
            var code = assign.Subcode;
            var resultType = assign.Lhs.Type;
            var leftType = assign.Operands[0].Type;
            var left = childNumbers[0];
            var right = childNumbers[1];
            var canUseIntegerIdentities = IsIntegerLike(resultType) || IsPointerLike(resultType);
            var hasLeftInteger = TryGetIntegerConstant(left, out var leftValue);
            var hasRightInteger = TryGetIntegerConstant(right, out var rightValue);

            if (canUseIntegerIdentities)
            {
                if (code is GimpleTreeCode.PlusExpr or GimpleTreeCode.PointerPlusExpr)
                {
                    if (hasRightInteger && rightValue == 0)
                    {
                        folded = left;
                        return true;
                    }

                    if (hasLeftInteger && leftValue == 0 && code == GimpleTreeCode.PlusExpr)
                    {
                        folded = right;
                        return true;
                    }
                }

                if (code is GimpleTreeCode.MinusExpr or GimpleTreeCode.PointerDiffExpr && hasRightInteger && rightValue == 0)
                {
                    folded = left;
                    return true;
                }
            }

            if (IsIntegerLike(resultType))
            {
                if (code == GimpleTreeCode.MultExpr)
                {
                    if (hasRightInteger && rightValue == 1)
                    {
                        folded = left;
                        return true;
                    }

                    if (hasLeftInteger && leftValue == 1)
                    {
                        folded = right;
                        return true;
                    }

                    if ((hasRightInteger && rightValue == 0) || (hasLeftInteger && leftValue == 0))
                    {
                        folded = NumberConstantValue(0, resultType);
                        return true;
                    }
                }

                if (code is GimpleTreeCode.TruncDivExpr or GimpleTreeCode.ExactDivExpr or GimpleTreeCode.TruncModExpr &&
                    hasRightInteger && rightValue == 1)
                {
                    folded = code == GimpleTreeCode.TruncModExpr ? NumberConstantValue(0, resultType) : left;
                    return true;
                }

                if (code is GimpleTreeCode.LshiftExpr or GimpleTreeCode.RshiftExpr && hasRightInteger && rightValue == 0)
                {
                    folded = left;
                    return true;
                }

                if (code == GimpleTreeCode.BitAndExpr && ((hasRightInteger && rightValue == 0) || (hasLeftInteger && leftValue == 0)))
                {
                    folded = NumberConstantValue(0, resultType);
                    return true;
                }

                if (code is GimpleTreeCode.BitIorExpr or GimpleTreeCode.BitXorExpr)
                {
                    if (hasRightInteger && rightValue == 0)
                    {
                        folded = left;
                        return true;
                    }

                    if (hasLeftInteger && leftValue == 0)
                    {
                        folded = right;
                        return true;
                    }
                }

                if (ReferenceEquals(left, right))
                {
                    switch (code)
                    {
                        case GimpleTreeCode.BitAndExpr:
                        case GimpleTreeCode.BitIorExpr:
                            folded = left;
                            return true;

                        case GimpleTreeCode.BitXorExpr:
                        case GimpleTreeCode.MinusExpr:
                            folded = NumberConstantValue(0, resultType);
                            return true;
                    }
                }
            }

            if (ReferenceEquals(left, right) && (IsIntegerLike(leftType) || IsPointerLike(leftType)))
            {
                switch (code)
                {
                    case GimpleTreeCode.EqExpr:
                    case GimpleTreeCode.LeExpr:
                    case GimpleTreeCode.GeExpr:
                        folded = NumberConstantValue(1, resultType);
                        return true;

                    case GimpleTreeCode.NeExpr:
                    case GimpleTreeCode.LtExpr:
                    case GimpleTreeCode.GtExpr:
                        folded = NumberConstantValue(0, resultType);
                        return true;
                }
            }

            folded = null!;
            return false;
        }

        private static bool TryGetIntegerConstant(ValueNumber number, out long value)
        {
            if (number.HasConstantValue && TryConvertIntegerConstant(number.ConstantValue, out value))
                return true;

            value = 0;
            return false;
        }

        private static bool TryConvertIntegerConstant(object? constant, out long value)
        {
            switch (constant)
            {
                case null:
                    value = 0;
                    return true;
                case bool b:
                    value = b ? 1 : 0;
                    return true;
                case byte b:
                    value = b;
                    return true;
                case sbyte s:
                    value = s;
                    return true;
                case short s:
                    value = s;
                    return true;
                case ushort u:
                    value = u;
                    return true;
                case int i:
                    value = i;
                    return true;
                case uint u:
                    value = u;
                    return true;
                case long l:
                    value = l;
                    return true;
                case ulong u when u <= long.MaxValue:
                    value = (long)u;
                    return true;
                case char c:
                    value = c;
                    return true;
                default:
                    value = 0;
                    return false;
            }
        }

        private bool SameType(QualifiedType left, QualifiedType right)
            => StringComparer.Ordinal.Equals(TypeKey(left), TypeKey(right));

        private static bool IsVolatileOrAtomic(QualifiedType type)
            => (GimpleTypeHelpers.Normalize(type).Qualifiers & (TypeQualifiers.Volatile | TypeQualifiers.Atomic)) != 0;

        private static bool IsPointerLike(QualifiedType type)
            => type.Type.Kind is TypeKind.Pointer or TypeKind.Array or TypeKind.Function;


        private bool IsCanonicalizedCommutative(GimpleTreeCode code, QualifiedType leftType, QualifiedType rightType)
        {
            if (!GimpleOperators.IsCommutative(code))
                return false;

            if (_options.CanonicalizeCommutativeComparisons && code is GimpleTreeCode.EqExpr or GimpleTreeCode.NeExpr)
                return true;

            if (_options.CanonicalizeCommutativeBitwiseOperators &&
                code is GimpleTreeCode.BitAndExpr or GimpleTreeCode.BitIorExpr or GimpleTreeCode.BitXorExpr)
            {
                return true;
            }

            if (_options.CanonicalizeCommutativeIntegerArithmetic &&
                code is GimpleTreeCode.PlusExpr or GimpleTreeCode.MultExpr &&
                IsIntegerLike(leftType) &&
                IsIntegerLike(rightType))
            {
                return true;
            }

            return false;
        }

        private static bool IsIntegerLike(QualifiedType type)
        {
            if (type.Type.Kind == TypeKind.Enum)
                return true;

            if (type.Type is not BuiltinType builtin)
                return false;

            return builtin.BuiltinKind is
                BuiltinTypeKind.Bool or
                BuiltinTypeKind.Char or
                BuiltinTypeKind.SignedChar or
                BuiltinTypeKind.UnsignedChar or
                BuiltinTypeKind.Short or
                BuiltinTypeKind.UnsignedShort or
                BuiltinTypeKind.Int or
                BuiltinTypeKind.UnsignedInt or
                BuiltinTypeKind.Long or
                BuiltinTypeKind.UnsignedLong or
                BuiltinTypeKind.LongLong or
                BuiltinTypeKind.UnsignedLongLong;
        }

        private ValueNumber GetNameNumber(GimpleName name)
        {
            if (_nameNumbers.TryGetValue(name, out var number))
                return number;

            number = GetUniqueNumber(
                ValueNumberKind.Unknown,
                name.Type,
                ValueNumberOperation.Unknown,
                name.ToString(),
                ImmutableArray<ValueNumber>.Empty,
                ValueNumberFlags.Unique);
            _nameNumbers.Add(name, number);
            return number;
        }

        private void AssignPhi(GimplePhi phi, ValueNumber number)
        {
            _phiNumbers[phi] = number;
            AssignName(phi.Result, number);

            if (_function.TryGetDefinition(phi.Result, out var definition) && definition is not null)
                _definitionNumbers[definition] = number;
        }

        private void AssignDefinition(GimpleDefinition definition, ValueNumber number)
        {
            _definitionNumbers[definition] = number;
            AssignName(definition.Name, number);
        }

        private void AssignName(GimpleName name, ValueNumber number)
            => _nameNumbers[name] = number;

        private ValueNumber GetCanonicalNumber(
            ValueNumberKind kind,
            QualifiedType type,
            ValueNumberOperation operation,
            string operatorKey,
            ImmutableArray<ValueNumber> operands,
            ValueNumberFlags flags = ValueNumberFlags.None,
            int memoryInputId = -1,
            int blockOrdinal = -1,
            bool hasConstantValue = false,
            object? constantValue = null)
        {
            var key = CreateKey(operation, type, operatorKey, operands, memoryInputId, blockOrdinal, discriminator: -1);
            return GetOrCreateNumber(key, kind, type, operands, flags, operatorKey, hasConstantValue, constantValue);
        }

        private ValueNumber GetUniqueNumber(
            ValueNumberKind kind,
            QualifiedType type,
            ValueNumberOperation operation,
            string operatorKey,
            ImmutableArray<ValueNumber> operands,
            ValueNumberFlags flags,
            int blockOrdinal = -1)
        {
            var key = CreateKey(operation, type, operatorKey, operands, memoryInputId: -1, blockOrdinal, discriminator: _uniqueOrdinal++);
            return GetOrCreateNumber(key, kind, type, operands, flags | ValueNumberFlags.Unique, operatorKey, hasConstantValue: false, constantValue: null);
        }

        private ValueNumber GetOrCreateNumber(
            ValueNumberKey key,
            ValueNumberKind kind,
            QualifiedType type,
            ImmutableArray<ValueNumber> operands,
            ValueNumberFlags flags,
            string display,
            bool hasConstantValue,
            object? constantValue)
        {
            if ((flags & ValueNumberFlags.Unique) == 0 && _canonicalNumbers.TryGetValue(key, out var existing))
                return existing;

            var number = new ValueNumber(_numbers.Count, kind, type, key, operands, flags, display, hasConstantValue, constantValue);
            _numbers.Add(number);

            if ((flags & ValueNumberFlags.Unique) == 0)
                _canonicalNumbers.Add(key, number);

            return number;
        }

        private ValueNumberKey CreateKey(
            ValueNumberOperation operation,
            QualifiedType type,
            string operatorKey,
            ImmutableArray<ValueNumber> operands,
            int memoryInputId,
            int blockOrdinal,
            int discriminator)
        {
            var operandIds = operands.IsDefault || operands.Length == 0
                ? ImmutableArray<int>.Empty
                : operands.Select(static operand => operand.Id).ToImmutableArray();

            return new ValueNumberKey(
                operation,
                TypeKey(type),
                operatorKey,
                operandIds,
                memoryInputId,
                blockOrdinal,
                discriminator);
        }

        private static ValueNumberOperation GetOperation(GimpleOperandInfo expression)
        {
            var value = expression.Original;
            if (expression.IsAddress)
            {
                switch (value)
                {
                    case GimpleSymbolValue:
                        return ValueNumberOperation.SymbolAddress;
                    case GimpleTemporaryValue:
                        return ValueNumberOperation.TemporaryAddress;
                    case GimpleIndirectExpression:
                        return ValueNumberOperation.Copy;
                    case GimpleElementAccessExpression:
                        return ValueNumberOperation.ElementAccess;
                    case GimpleMemberAccessExpression:
                        return ValueNumberOperation.MemberAccess;
                }
            }

            switch (value)
            {
                case GimpleSymbolValue:
                    return ValueNumberOperation.DirectMemoryRead;
                case GimpleTemporaryValue:
                    return ValueNumberOperation.DirectMemoryRead;
                case GimpleUnaryExpression:
                    return ValueNumberOperation.Unary;
                case GimpleBinaryExpression:
                    return ValueNumberOperation.Binary;
                case GimpleConversionExpression:
                    return ValueNumberOperation.Conversion;
                case GimpleCastExpression:
                    return ValueNumberOperation.Cast;
                case GimpleAddressOfExpression:
                    return ValueNumberOperation.AddressOf;
                case GimpleIndirectExpression:
                    return ValueNumberOperation.IndirectLoad;
                case GimpleElementAccessExpression:
                    return ValueNumberOperation.ElementAccess;
                case GimpleMemberAccessExpression:
                    return ValueNumberOperation.MemberAccess;
                case GimpleErrorValue:
                    return ValueNumberOperation.Error;
                default:
                    return ValueNumberOperation.Unknown;
            }
        }

        private string GetOperatorKey(GimpleValue value)
        {
            switch (value)
            {
                case GimpleSymbolValue symbolValue:
                    return "symbol:" + ObjectAddress(symbolValue.Symbol).Root.ToString(CultureInfo.InvariantCulture) + ":" + symbolValue.Symbol.Name;

                case GimpleTemporaryValue temporary:
                    return "temporary:" + ObjectAddress(temporary).Root.ToString(CultureInfo.InvariantCulture) + ":" + temporary.Ordinal.ToString(CultureInfo.InvariantCulture);

                case GimpleUnaryExpression unary:
                    return GimpleOperators.Name(unary.Code);

                case GimpleBinaryExpression binary:
                    return GimpleOperators.Name(binary.Code);

                case GimpleConversionExpression conversion:
                    return conversion.ConversionKind.ToString();

                case GimpleCastExpression:
                    return "cast";

                case GimpleAddressOfExpression:
                    return "address-of";

                case GimpleIndirectExpression:
                    return "indirect";

                case GimpleElementAccessExpression:
                    return "element";

                case GimpleMemberAccessExpression member:
                    return (member.ThroughPointer ? "arrow:" : "dot:") + FieldKey(member);

                case GimpleErrorValue:
                    return "error";

                default:
                    return value.Kind.ToString();
            }
        }

        private string FieldKey(GimpleMemberAccessExpression member)
        {
            if (member.Field is not null)
                return "field:" + ObjectAddress(member.Field).Root.ToString(CultureInfo.InvariantCulture) + ":" + member.Field.Name;

            return "name:" + member.NameToken.Text;
        }

        private string TypeKey(QualifiedType type)
        {
            type = GimpleTypeHelpers.Normalize(type);
            if (_typeKeys.TryGetValue(type, out var key))
                return key;
            var identity = type.Type switch
            {
                TagType tag => "tag:" + ObjectAddress(tag.Symbol).Root.ToString(CultureInfo.InvariantCulture),
                EnumType enumeration => "enum:" + ObjectAddress(enumeration.Symbol).Root.ToString(CultureInfo.InvariantCulture),
                PointerType pointer => "ptr(" + TypeKey(pointer.PointeeType) + ")",
                ArrayType array => "array(" + TypeKey(array.ElementType) + "," + array.Length?.ToString(CultureInfo.InvariantCulture) + ")",
                FunctionType function => "func(" + TypeKey(function.ReturnType) + "," + function.HasPrototype + "," + function.IsVariadic +
                    "," + string.Join(",", function.Parameters.Select(parameter => TypeKey(parameter.Type))) + ")",
                _ => type.Type.ToDisplayString(),
            };
            key = type.Qualifiers == TypeQualifiers.None ? identity : type.Qualifiers + ":" + identity;
            _typeKeys.Add(type, key);
            return key;
        }

        private static ValueNumberFlags TranslateFlags(GimpleOperandInfo expression)
        {
            var flags = ValueNumberFlags.None;
            if (expression.ReadsMemory)
                flags |= ValueNumberFlags.ReadsMemory;
            if (expression.WritesMemory)
                flags |= ValueNumberFlags.WritesMemory;
            if (expression.ContainsCall)
                flags |= ValueNumberFlags.ContainsCall;
            return flags;
        }

        private static ValueNumberFlags TranslateFlags(GimpleStatementFlags instructionFlags)
        {
            var flags = ValueNumberFlags.None;
            if ((instructionFlags & GimpleStatementFlags.ReadsMemory) != 0)
                flags |= ValueNumberFlags.ReadsMemory;
            if ((instructionFlags & GimpleStatementFlags.WritesMemory) != 0)
                flags |= ValueNumberFlags.WritesMemory;
            if ((instructionFlags & GimpleStatementFlags.ContainsCall) != 0)
                flags |= ValueNumberFlags.ContainsCall;
            return flags;
        }

        private static string NormalizeConstantKey(object? value)
        {
            if (value is null)
                return "null";

            if (TryNormalizeIntegerConstant(value, out var integer))
                return "i:" + integer.ToString("X16", CultureInfo.InvariantCulture);

            if (value is float single)
                return "f32:" + unchecked((uint)BitConverter.SingleToInt32Bits(single)).ToString("X8", CultureInfo.InvariantCulture);

            if (value is double @double)
                return "f64:" + unchecked((ulong)BitConverter.DoubleToInt64Bits(@double)).ToString("X16", CultureInfo.InvariantCulture);

            var typeName = value.GetType().FullName ?? value.GetType().Name;
            var text = value switch
            {
                string textValue => textValue,
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty,
            };

            return typeName + ":" + text;
        }

        private static bool TryNormalizeIntegerConstant(object value, out ulong result)
        {
            switch (value)
            {
                case bool b:
                    result = b ? 1UL : 0UL;
                    return true;
                case char c:
                    result = c;
                    return true;
                case byte b:
                    result = b;
                    return true;
                case sbyte sb:
                    result = unchecked((ulong)sb);
                    return true;
                case short s:
                    result = unchecked((ulong)s);
                    return true;
                case ushort us:
                    result = us;
                    return true;
                case int i:
                    result = unchecked((ulong)i);
                    return true;
                case uint ui:
                    result = ui;
                    return true;
                case long l:
                    result = unchecked((ulong)l);
                    return true;
                case ulong ul:
                    result = ul;
                    return true;
                default:
                    result = 0;
                    return false;
            }
        }

    }

    internal static class GimpleMemoryEffects
    {
        public static bool HasOrderedAccess(GimpleStatement statement)
            => statement switch
            {
                GimpleAssignStatement assign => Ordered(assign.Lhs.Type) || AnyOrdered(assign.Operands),
                GimpleCondStatement cond => HasOrderedRead(cond.Lhs) || HasOrderedRead(cond.Rhs),
                GimpleReturnStatement { Expression: not null } ret => HasOrderedRead(ret.Expression),
                GimpleSwitchStatement sw => HasOrderedRead(sw.Expression),
                _ => false,
            };

        private static bool AnyOrdered(ImmutableArray<GimpleValue> values)
        {
            foreach (var value in values)
            {
                if (HasOrderedRead(value))
                    return true;
            }
            return false;
        }

        private static bool HasOrderedRead(GimpleValue value, bool address = false)
        {
            if (!address && value is GimplePlace && Ordered(value.Type))
                return true;
            return value switch
            {
                GimpleAddressOfExpression addr => HasOrderedRead(addr.Target, true),
                GimpleIndirectExpression ind => HasOrderedRead(ind.Address),
                GimpleMemberAccessExpression member => HasOrderedRead(member.Expression, !member.ThroughPointer),
                GimpleElementAccessExpression element => HasOrderedRead(element.Expression, element.Expression.Type.Type is not PointerType) ||
                    element.Index is not null && HasOrderedRead(element.Index),
                GimpleUnaryExpression unary => HasOrderedRead(unary.Operand),
                GimpleBinaryExpression binary => HasOrderedRead(binary.Left) || HasOrderedRead(binary.Right),
                GimpleConversionExpression conversion => HasOrderedRead(conversion.Operand),
                GimpleCastExpression cast => HasOrderedRead(cast.Operand),
                _ => false,
            };
        }

        private static bool Ordered(QualifiedType type)
            => (type.Qualifiers & (TypeQualifiers.Volatile | TypeQualifiers.Atomic)) != 0;
    }
}
