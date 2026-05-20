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
        public bool CanonicalizeCommutativeBitwiseOperators { get; }
        public bool CanonicalizeCommutativeIntegerArithmetic { get; }
        public bool TreatCallsAsUniqueValues { get; }
        public bool TreatMemoryDefinitionsAsUniqueStates { get; }

        public ValueNumberingOptions(
            bool canonicalizeCommutativeComparisons = true,
            bool canonicalizeCommutativeBitwiseOperators = true,
            bool canonicalizeCommutativeIntegerArithmetic = false,
            bool treatCallsAsUniqueValues = true,
            bool treatMemoryDefinitionsAsUniqueStates = true)
        {
            CanonicalizeCommutativeComparisons = canonicalizeCommutativeComparisons;
            CanonicalizeCommutativeBitwiseOperators = canonicalizeCommutativeBitwiseOperators;
            CanonicalizeCommutativeIntegerArithmetic = canonicalizeCommutativeIntegerArithmetic;
            TreatCallsAsUniqueValues = treatCallsAsUniqueValues;
            TreatMemoryDefinitionsAsUniqueStates = treatMemoryDefinitionsAsUniqueStates;
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

    public sealed class SsaValueNumbering
    {
        private readonly Dictionary<SsaName, ValueNumber> _nameNumbers;
        private readonly Dictionary<SsaExpression, ValueNumber> _expressionNumbers;
        private readonly Dictionary<SsaDefinition, ValueNumber> _definitionNumbers;
        private readonly Dictionary<SsaPhi, ValueNumber> _phiNumbers;

        public SsaFunction Function { get; }
        public ImmutableArray<ValueNumber> ValueNumbers { get; }

        internal SsaValueNumbering(
            SsaFunction function,
            ImmutableArray<ValueNumber> valueNumbers,
            Dictionary<SsaName, ValueNumber> nameNumbers,
            Dictionary<SsaExpression, ValueNumber> expressionNumbers,
            Dictionary<SsaDefinition, ValueNumber> definitionNumbers,
            Dictionary<SsaPhi, ValueNumber> phiNumbers)
        {
            Function = function ?? throw new ArgumentNullException(nameof(function));
            ValueNumbers = valueNumbers.IsDefault ? ImmutableArray<ValueNumber>.Empty : valueNumbers;
            _nameNumbers = nameNumbers is null
                ? new Dictionary<SsaName, ValueNumber>()
                : new Dictionary<SsaName, ValueNumber>(nameNumbers);
            _expressionNumbers = expressionNumbers is null
                ? new Dictionary<SsaExpression, ValueNumber>()
                : new Dictionary<SsaExpression, ValueNumber>(expressionNumbers);
            _definitionNumbers = definitionNumbers is null
                ? new Dictionary<SsaDefinition, ValueNumber>()
                : new Dictionary<SsaDefinition, ValueNumber>(definitionNumbers);
            _phiNumbers = phiNumbers is null
                ? new Dictionary<SsaPhi, ValueNumber>()
                : new Dictionary<SsaPhi, ValueNumber>(phiNumbers);
        }

        internal static SsaValueNumbering Build(SsaFunction function, ValueNumberingOptions? options = null)
            => new ValueNumberingBuilder(function, options ?? ValueNumberingOptions.Default).Build();

        public bool TryGetValueNumber(SsaName name, out ValueNumber? valueNumber)
        {
            if (name is null)
                throw new ArgumentNullException(nameof(name));

            return _nameNumbers.TryGetValue(name, out valueNumber);
        }

        public bool TryGetValueNumber(SsaExpression expression, out ValueNumber? valueNumber)
        {
            if (expression is null)
                throw new ArgumentNullException(nameof(expression));

            return _expressionNumbers.TryGetValue(expression, out valueNumber);
        }

        public bool TryGetValueNumber(SsaDefinition definition, out ValueNumber? valueNumber)
        {
            if (definition is null)
                throw new ArgumentNullException(nameof(definition));

            return _definitionNumbers.TryGetValue(definition, out valueNumber);
        }

        public bool TryGetValueNumber(SsaPhi phi, out ValueNumber? valueNumber)
        {
            if (phi is null)
                throw new ArgumentNullException(nameof(phi));

            return _phiNumbers.TryGetValue(phi, out valueNumber);
        }

        public bool AreEquivalent(SsaName left, SsaName right)
        {
            if (left is null)
                throw new ArgumentNullException(nameof(left));
            if (right is null)
                throw new ArgumentNullException(nameof(right));

            return TryGetValueNumber(left, out var leftNumber) &&
                   TryGetValueNumber(right, out var rightNumber) &&
                   ReferenceEquals(leftNumber, rightNumber);
        }
    }

    public sealed class ValueNumber
    {
        public int Id { get; }
        public ValueNumberKind Kind { get; }
        public ValueNumberOperation Operation => Key.Operation;
        public QualifiedType Type { get; }
        public ValueNumberKey Key { get; }
        public ImmutableArray<ValueNumber> Operands { get; }
        public ValueNumberFlags Flags { get; }
        public bool IsMemoryDependent => (Flags & ValueNumberFlags.ReadsMemory) != 0;
        public bool IsUnique => (Flags & ValueNumberFlags.Unique) != 0;
        public string Display { get; }

        internal ValueNumber(
            int id,
            ValueNumberKind kind,
            QualifiedType type,
            ValueNumberKey key,
            ImmutableArray<ValueNumber> operands,
            ValueNumberFlags flags,
            string display)
        {
            if (id < 0)
                throw new ArgumentOutOfRangeException(nameof(id));

            Id = id;
            Kind = kind;
            Type = GimpleTypeHelpers.Normalize(type);
            Key = key;
            Operands = operands.IsDefault ? ImmutableArray<ValueNumber>.Empty : operands;
            Flags = flags;
            Display = string.IsNullOrWhiteSpace(display)
                ? $"vn{id.ToString(CultureInfo.InvariantCulture)}"
                : display;
        }

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
        private readonly SsaFunction _function;
        private readonly ValueNumberingOptions _options;
        private readonly Dictionary<ValueNumberKey, ValueNumber> _canonicalNumbers = new();
        private readonly Dictionary<SsaName, ValueNumber> _nameNumbers = new();
        private readonly Dictionary<SsaExpression, ValueNumber> _expressionNumbers = new();
        private readonly Dictionary<SsaDefinition, ValueNumber> _definitionNumbers = new();
        private readonly Dictionary<SsaPhi, ValueNumber> _phiNumbers = new();
        private readonly List<ValueNumber> _numbers = new();
        private int _uniqueOrdinal;

        public ValueNumberingBuilder(SsaFunction function, ValueNumberingOptions options)
        {
            _function = function ?? throw new ArgumentNullException(nameof(function));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public SsaValueNumbering Build()
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

                foreach (var instruction in block.Instructions)
                    NumberInstruction(instruction);
            }

            return new SsaValueNumbering(
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
                    case SsaDefinitionKind.Undefined:
                        AssignDefinition(
                            definition,
                            GetCanonicalNumber(
                                ValueNumberKind.Undefined,
                                definition.Name.Type,
                                ValueNumberOperation.Undefined,
                                "v" + definition.Name.Variable.Ordinal.ToString(CultureInfo.InvariantCulture),
                                ImmutableArray<ValueNumber>.Empty));
                        break;

                    case SsaDefinitionKind.Entry when definition.Name.Variable.Kind == SsaVariableKind.Memory:
                        AssignDefinition(
                            definition,
                            GetCanonicalNumber(
                                ValueNumberKind.Memory,
                                definition.Name.Type,
                                ValueNumberOperation.MemoryEntry,
                                "entry-memory",
                                ImmutableArray<ValueNumber>.Empty));
                        break;

                    case SsaDefinitionKind.Entry:
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

        private void NumberPhi(SsaPhi phi)
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

            var keyOperands = ImmutableArray.CreateBuilder<int>(phi.Operands.Length * 2);
            for (var i = 0; i < phi.Operands.Length; i++)
            {
                keyOperands.Add(phi.Operands[i].Predecessor.Ordinal);
                keyOperands.Add(operandNumbers[i].Id);
            }

            var key = new ValueNumberKey(
                ValueNumberOperation.Phi,
                TypeKey(phi.Result.Type),
                "v" + phi.Variable.Ordinal.ToString(CultureInfo.InvariantCulture),
                keyOperands.ToImmutable(),
                blockOrdinal: phi.Block.Ordinal);

            var number = GetOrCreateNumber(
                key,
                ValueNumberKind.Phi,
                phi.Result.Type,
                operandNumbers,
                ValueNumberFlags.None,
                "phi " + phi.Result.ToString());

            AssignPhi(phi, number);
        }

        private void NumberInstruction(SsaInstruction instruction)
        {
            var memoryInput = instruction.MemoryInput is null ? null : GetNameNumber(instruction.MemoryInput);
            var expressionNumbers = instruction.Expressions
                .Select(expression => NumberExpression(expression, memoryInput))
                .ToImmutableArray();

            foreach (var definition in instruction.Definitions)
            {
                if (definition.Name.Variable.Kind == SsaVariableKind.Memory)
                {
                    AssignDefinition(definition, NumberMemoryDefinition(instruction, definition, memoryInput, expressionNumbers));
                    continue;
                }

                AssignDefinition(definition, NumberStatementDefinition(instruction, definition, expressionNumbers));
            }
        }

        private ValueNumber NumberStatementDefinition(
            SsaInstruction instruction,
            SsaDefinition definition,
            ImmutableArray<ValueNumber> expressionNumbers)
        {
            if (instruction.Statement is GimpleZeroInitializeStatement && expressionNumbers.Length == 0)
            {
                return GetCanonicalNumber(
                    ValueNumberKind.Constant,
                    definition.Name.Type,
                    ValueNumberOperation.ZeroInitialize,
                    "zero",
                    ImmutableArray<ValueNumber>.Empty);
            }

            if (expressionNumbers.Length == 1 && (instruction.Flags & SsaInstructionFlags.WritesMemory) == 0)
                return expressionNumbers[0];

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

        private ValueNumber NumberMemoryDefinition(
            SsaInstruction instruction,
            SsaDefinition definition,
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

        private ValueNumber NumberExpression(SsaExpression expression, ValueNumber? memoryInput)
        {
            if (_expressionNumbers.TryGetValue(expression, out var existing))
                return existing;

            ValueNumber number;
            if (expression.Name is not null)
            {
                number = GetNameNumber(expression.Name);
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

        private ValueNumber NumberConstant(GimpleConstantValue constant)
            => GetCanonicalNumber(
                ValueNumberKind.Constant,
                constant.Type,
                ValueNumberOperation.Constant,
                NormalizeConstantKey(constant.Value),
                ImmutableArray<ValueNumber>.Empty);

        private ValueNumber NumberCompositeExpression(
            SsaExpression expression,
            ImmutableArray<ValueNumber> childNumbers,
            ValueNumber? memoryInput)
        {
            var flags = TranslateFlags(expression);
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

            var operation = GetOperation(expression);
            var operatorKey = GetOperatorKey(expression.Original);
            var operands = CanonicalizeOperands(expression.Original, childNumbers);
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

        private ImmutableArray<ValueNumber> CanonicalizeOperands(GimpleValue value, ImmutableArray<ValueNumber> operands)
        {
            if (operands.Length != 2 || value is not GimpleBinaryExpression binary || !IsCommutative(binary))
                return operands;

            return operands[0].Id <= operands[1].Id
                ? operands
                : ImmutableArray.Create(operands[1], operands[0]);
        }

        private bool IsCommutative(GimpleBinaryExpression binary)
        {
            var kind = binary.OperatorToken.Kind;

            if (_options.CanonicalizeCommutativeComparisons &&
                kind is SyntaxKind.EqualsEqualsToken or SyntaxKind.BangEqualsToken)
            {
                return true;
            }

            if (_options.CanonicalizeCommutativeBitwiseOperators &&
                kind is SyntaxKind.AmpersandToken or SyntaxKind.PipeToken or SyntaxKind.HatToken)
            {
                return true;
            }

            if (_options.CanonicalizeCommutativeIntegerArithmetic &&
                kind is SyntaxKind.PlusToken or SyntaxKind.StarToken &&
                IsIntegerLike(binary.Left.Type) &&
                IsIntegerLike(binary.Right.Type))
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

        private ValueNumber GetNameNumber(SsaName name)
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

        private void AssignPhi(SsaPhi phi, ValueNumber number)
        {
            _phiNumbers[phi] = number;
            AssignName(phi.Result, number);
        }

        private void AssignDefinition(SsaDefinition definition, ValueNumber number)
        {
            _definitionNumbers[definition] = number;
            AssignName(definition.Name, number);
        }

        private void AssignName(SsaName name, ValueNumber number)
        {
            if (!_nameNumbers.ContainsKey(name))
                _nameNumbers.Add(name, number);
        }

        private ValueNumber GetCanonicalNumber(
            ValueNumberKind kind,
            QualifiedType type,
            ValueNumberOperation operation,
            string operatorKey,
            ImmutableArray<ValueNumber> operands,
            ValueNumberFlags flags = ValueNumberFlags.None,
            int memoryInputId = -1,
            int blockOrdinal = -1)
        {
            var key = CreateKey(operation, type, operatorKey, operands, memoryInputId, blockOrdinal, discriminator: -1);
            return GetOrCreateNumber(key, kind, type, operands, flags, operatorKey);
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
            return GetOrCreateNumber(key, kind, type, operands, flags | ValueNumberFlags.Unique, operatorKey);
        }

        private ValueNumber GetOrCreateNumber(
            ValueNumberKey key,
            ValueNumberKind kind,
            QualifiedType type,
            ImmutableArray<ValueNumber> operands,
            ValueNumberFlags flags,
            string display)
        {
            if ((flags & ValueNumberFlags.Unique) == 0 && _canonicalNumbers.TryGetValue(key, out var existing))
                return existing;

            var number = new ValueNumber(_numbers.Count, kind, type, key, operands, flags, display);
            _numbers.Add(number);

            if ((flags & ValueNumberFlags.Unique) == 0)
                _canonicalNumbers.Add(key, number);

            return number;
        }

        private static ValueNumberKey CreateKey(
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

        private static ValueNumberOperation GetOperation(SsaExpression expression)
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
                case GimpleUnaryExpression unary when unary.OperatorToken.Kind == SyntaxKind.AmpersandToken:
                    return ValueNumberOperation.AddressOf;
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
                case GimpleCallExpression:
                    return ValueNumberOperation.Call;
                case GimpleErrorValue:
                    return ValueNumberOperation.Error;
                default:
                    return ValueNumberOperation.Unknown;
            }
        }

        private static string GetOperatorKey(GimpleValue value)
        {
            switch (value)
            {
                case GimpleSymbolValue symbolValue:
                    return "symbol:" + RuntimeHelpersShim.GetHashCode(symbolValue.Symbol).ToString(CultureInfo.InvariantCulture) + ":" + symbolValue.Symbol.Name;

                case GimpleTemporaryValue temporary:
                    return "temporary:" + RuntimeHelpersShim.GetHashCode(temporary).ToString(CultureInfo.InvariantCulture) + ":" + temporary.Ordinal.ToString(CultureInfo.InvariantCulture);

                case GimpleUnaryExpression unary:
                    return TokenKey(unary.OperatorToken);

                case GimpleBinaryExpression binary:
                    return TokenKey(binary.OperatorToken);

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
                    return TokenKey(member.OperatorToken) + ":" + FieldKey(member);

                case GimpleCallExpression call:
                    return "call:" + (call.FunctionType?.ToDisplayString() ?? "<unknown>");

                case GimpleErrorValue:
                    return "error";

                default:
                    return value.Kind.ToString();
            }
        }

        private static string FieldKey(GimpleMemberAccessExpression member)
        {
            if (member.Field is not null)
                return "field:" + RuntimeHelpersShim.GetHashCode(member.Field).ToString(CultureInfo.InvariantCulture) + ":" + member.Field.Name;

            return "name:" + member.NameToken.Text;
        }

        private static string TokenKey(SyntaxToken token)
            => token.Kind + ":" + token.Text;

        private static string TypeKey(QualifiedType type)
            => GimpleTypeHelpers.Normalize(type).ToDisplayString();

        private static ValueNumberFlags TranslateFlags(SsaExpression expression)
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

        private static ValueNumberFlags TranslateFlags(SsaInstructionFlags instructionFlags)
        {
            var flags = ValueNumberFlags.None;
            if ((instructionFlags & SsaInstructionFlags.ReadsMemory) != 0)
                flags |= ValueNumberFlags.ReadsMemory;
            if ((instructionFlags & SsaInstructionFlags.WritesMemory) != 0)
                flags |= ValueNumberFlags.WritesMemory;
            if ((instructionFlags & SsaInstructionFlags.ContainsCall) != 0)
                flags |= ValueNumberFlags.ContainsCall;
            return flags;
        }

        private static string NormalizeConstantKey(object? value)
        {
            if (value is null)
                return "null";

            var typeName = value.GetType().FullName ?? value.GetType().Name;
            var text = value switch
            {
                string s => s,
                char c => ((int)c).ToString(CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty,
            };

            return typeName + ":" + text;
        }
    }
}
