using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Cnidaria.Cs;
using Cnidaria.RiscV;

namespace Cnidaria.C
{
    internal sealed class RiscVCodeGenerator
    {
        private const string TextSectionName = ".text";
        private const string RodataSectionName = ".rodata";
        private const string DataSectionName = ".data";
        private const string BssSectionName = ".bss";

        private static readonly MachineRegister Sp = MachineRegister.X2;
        private static readonly MachineRegister Ra = MachineRegister.X1;
        private static readonly MachineRegister GpScratch0 = MachineRegister.X5;
        private static readonly MachineRegister GpScratch1 = MachineRegister.X6;
        private static readonly MachineRegister GpScratch2 = MachineRegister.X7;
        private static readonly MachineRegister GpScratch3 = MachineRegister.X28;
        private static readonly MachineRegister FpScratch0 = MachineRegister.F0;
        private static readonly MachineRegister FpScratch1 = MachineRegister.F1;
        private static readonly MachineRegister FpScratch2 = MachineRegister.F2;

        private readonly LirModule _module;
        private readonly TargetInfo _target;
        private readonly RVTarget _machineTarget;
        private readonly LSRAOptions _allocationOptions;
        private readonly Dictionary<FunctionSymbol, string> _functionLabels = new Dictionary<FunctionSymbol, string>();
        private readonly Dictionary<string, string> _functionLabelsByName = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<Symbol, string> _dataLabels = new Dictionary<Symbol, string>();
        private readonly Dictionary<string, string> _stringLabels = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> _usedLabels = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<RVObjectSymbol> _symbols = new List<RVObjectSymbol>();
        private readonly DataSectionBuilder _rodata = new DataSectionBuilder(RodataSectionName, RVObjectSectionKind.Rodata);
        private readonly DataSectionBuilder _data = new DataSectionBuilder(DataSectionName, RVObjectSectionKind.Data);
        private readonly BssSectionBuilder _bss = new BssSectionBuilder(BssSectionName);
        private TextSectionBuilder _text = null!;
        private int _nextLocalId;

        private RiscVCodeGenerator(LirModule module, LSRAOptions? allocationOptions)
        {
            _module = module ?? throw new ArgumentNullException(nameof(module));
            _target = module.SemanticModel.Compilation.Options.Target;
            if (_target.Architecture is not TargetArchitectureKind.RiscV32 and not TargetArchitectureKind.RiscV64)
                throw new NotSupportedException("RISC-V C backend requires RiscV32 or RiscV64 target.");
            _machineTarget = RVTarget.FromTargetInfo(_target);
            _allocationOptions = allocationOptions ?? LSRAOptions.ForTarget(_target);
        }

        public static RiscVProgram Generate(LirModule module, LSRAOptions? allocationOptions = null)
            => new RiscVCodeGenerator(module, allocationOptions).Generate();

        private RiscVProgram Generate()
        {
            _text = new TextSectionBuilder(TextSectionName);
            IndexFunctions();
            EmitGlobalStorage();
            foreach (var function in _module.Functions)
                EmitFunction(function);

            var entry = _functionLabelsByName.TryGetValue("main", out var mainLabel)
                ? mainLabel
                : (_functionLabels.Values.FirstOrDefault() ?? string.Empty);

            AddSectionSymbols();
            var dataSections = ImmutableArray.CreateBuilder<RVDataSection>();
            dataSections.Add(_rodata.ToSection());
            dataSections.Add(_data.ToSection());
            dataSections.Add(_bss.ToSection());

            return new RiscVProgram(
                _machineTarget,
                _text.ToSection(),
                dataSections.ToImmutable(),
                _symbols.ToImmutableArray(),
                entry);
        }

        private void IndexFunctions()
        {
            foreach (var function in _module.Functions)
            {
                var symbol = function.Symbol;
                if (symbol is null || _functionLabels.ContainsKey(symbol))
                    continue;

                var label = CreateUniqueGlobalLabel(symbol.Name);
                _functionLabels.Add(symbol, label);
                if (!_functionLabelsByName.ContainsKey(symbol.Name))
                    _functionLabelsByName.Add(symbol.Name, label);
            }
        }

        private void AddSectionSymbols()
        {
            _symbols.Add(new RVObjectSymbol(TextSectionName, TextSectionName, 0, _text.ByteLength, RVObjectSymbolBinding.Local, RVObjectSymbolKind.Section));
            _symbols.Add(new RVObjectSymbol(RodataSectionName, RodataSectionName, 0, _rodata.ByteLength, RVObjectSymbolBinding.Local, RVObjectSymbolKind.Section));
            _symbols.Add(new RVObjectSymbol(DataSectionName, DataSectionName, 0, _data.ByteLength, RVObjectSymbolBinding.Local, RVObjectSymbolKind.Section));
            _symbols.Add(new RVObjectSymbol(BssSectionName, BssSectionName, 0, _bss.ByteLength, RVObjectSymbolBinding.Local, RVObjectSymbolKind.Section));
        }

        private void EmitGlobalStorage()
        {
            foreach (var global in _module.Globals)
            {
                if (global.Symbol is null || global.StorageClass == StorageClass.Extern)
                {
                    if (global.Symbol is not null)
                        AddExternalObjectSymbol(global.Symbol);
                    continue;
                }

                var size = Math.Max(1, _target.SizeOf(global.Type));
                var alignment = Math.Max(1, _target.AlignOf(global.Type));
                var label = CreateUniqueGlobalLabel(global.Symbol.Name);
                _dataLabels[global.Symbol] = label;
                var binding = global.StorageClass == StorageClass.Static ? RVObjectSymbolBinding.Local : RVObjectSymbolBinding.Global;

                if (global.Initializer is null)
                {
                    var offset = _bss.Allocate(size, alignment);
                    _bss.DefineSymbol(label, offset, size, binding, _symbols);
                    continue;
                }

                var section = IsReadOnlyGlobal(global) ? _rodata : _data;
                var symbolOffset = section.Align(alignment);
                section.DefineSymbol(label, symbolOffset, size, binding, _symbols);
                var bytes = EmitInitializer(section, global.Type, global.Initializer, size);
                if (bytes < size)
                    section.EmitZero(size - bytes);
            }
        }

        private static bool IsReadOnlyGlobal(LirGlobal global)
            => (global.Type.Qualifiers & TypeQualifiers.Const) != 0 && global.StorageClass != StorageClass.Extern;

        private void AddExternalObjectSymbol(Symbol symbol)
        {
            var name = CreateExternalLabel(symbol.Name);
            _symbols.Add(new RVObjectSymbol(name, string.Empty, 0, 0, RVObjectSymbolBinding.External, RVObjectSymbolKind.Object));
        }

        private int EmitInitializer(DataSectionBuilder section, QualifiedType type, GimpleInitializer initializer, int availableSize)
        {
            if (initializer is GimpleExpressionInitializer expressionInitializer)
                return EmitExpressionInitializer(section, type, expressionInitializer.Expression, availableSize);

            if (initializer is GimpleInitializerList list)
                return EmitInitializerList(section, type, list, availableSize);

            section.EmitZero(availableSize);
            return availableSize;
        }

        private int EmitInitializerList(DataSectionBuilder section, QualifiedType type, GimpleInitializerList list, int availableSize)
        {
            var start = section.ByteLength;
            if (type.Type is ArrayType array)
            {
                var elementType = array.ElementType;
                var elementSize = Math.Max(1, _target.SizeOf(elementType));
                foreach (var item in list.Items)
                {
                    if (section.ByteLength - start >= availableSize)
                        break;
                    var used = EmitInitializer(section, elementType, item.Initializer, Math.Min(elementSize, availableSize - (section.ByteLength - start)));
                    if (used < elementSize && section.ByteLength - start < availableSize)
                        section.EmitZero(Math.Min(elementSize - used, availableSize - (section.ByteLength - start)));
                }
            }
            else
            {
                foreach (var item in list.Items)
                {
                    if (section.ByteLength - start >= availableSize)
                        break;
                    var itemType = item.Initializer.TargetType;
                    var itemSize = Math.Max(1, _target.SizeOf(itemType));
                    EmitInitializer(section, itemType, item.Initializer, Math.Min(itemSize, availableSize - (section.ByteLength - start)));
                }
            }

            return section.ByteLength - start;
        }

        private int EmitExpressionInitializer(DataSectionBuilder section, QualifiedType type, GimpleValue expression, int availableSize)
        {
            if (expression is GimpleConstantValue constant)
                return EmitConstantInitializer(section, type, constant.Value, availableSize);

            if (expression is GimpleSymbolValue symbolValue)
            {
                EmitPointerRelocation(section, GetSymbolLabel(symbolValue.Symbol));
                return Math.Min(availableSize, _target.PointerSize);
            }

            if (expression is GimpleAddressOfExpression addressOf && addressOf.Target is GimpleSymbolValue addressSymbol)
            {
                EmitPointerRelocation(section, GetSymbolLabel(addressSymbol.Symbol));
                return Math.Min(availableSize, _target.PointerSize);
            }

            section.EmitZero(availableSize);
            return availableSize;
        }

        private int EmitConstantInitializer(DataSectionBuilder section, QualifiedType type, object? value, int availableSize)
        {
            if (value is string text && type.Type is ArrayType)
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                var count = Math.Min(availableSize, checked(bytes.Length + 1));
                for (var i = 0; i < count; i++)
                    section.EmitByte(i < bytes.Length ? bytes[i] : (byte)0);
                return count;
            }

            if (IsFloatType(type))
            {
                if (IsFloat32(type))
                {
                    var raw = BitConverter.GetBytes(Convert.ToSingle(value, CultureInfo.InvariantCulture));
                    section.EmitBytes(raw, Math.Min(availableSize, 4));
                    return Math.Min(availableSize, 4);
                }

                var raw64 = BitConverter.GetBytes(Convert.ToDouble(value, CultureInfo.InvariantCulture));
                section.EmitBytes(raw64, Math.Min(availableSize, 8));
                return Math.Min(availableSize, 8);
            }

            var size = Math.Min(Math.Max(1, _target.SizeOf(type)), availableSize);
            var integer = ConvertIntegerConstant(value);
            section.EmitInteger(integer, size, _target.Endianness);
            return size;
        }

        private void EmitPointerRelocation(DataSectionBuilder section, string symbol)
        {
            var offset = section.ByteLength;
            section.EmitZero(_target.PointerSize);
            section.AddRelocation(offset, symbol, 0, _target.PointerSize == 8 ? RVObjectRelocationKind.Absolute64 : RVObjectRelocationKind.Absolute32);
        }

        private string GetSymbolLabel(Symbol symbol)
        {
            if (symbol is FunctionSymbol function)
            {
                if (_functionLabels.TryGetValue(function, out var functionLabel))
                    return functionLabel;
                if (_functionLabelsByName.TryGetValue(function.Name, out functionLabel))
                    return functionLabel;
                return CreateExternalLabel(function.Name);
            }

            if (_dataLabels.TryGetValue(symbol, out var dataLabel))
                return dataLabel;

            return CreateExternalLabel(symbol.Name);
        }

        private void EmitFunction(LirFunction function)
        {
            if (function.Symbol is null || !_functionLabels.TryGetValue(function.Symbol, out var label))
                throw new NotSupportedException("Cannot emit anonymous functions to object code.");

            var allocation = LinearScanRegisterAllocator.Allocate(function, _target, _allocationOptions);
            var blockLabels = new Dictionary<LirBlock, string>();
            foreach (var block in function.Blocks)
                blockLabels.Add(block, CreateLocalLabel(label + "_" + block.Name));

            var context = new FunctionEmissionContext(this, function, allocation, label, blockLabels);
            var startOffset = _text.ByteLength;
            _text.DefineLabel(label);
            context.EmitPrologue();
            context.EmitBlocks();
            context.EmitTrap();
            var size = _text.ByteLength - startOffset;
            var binding = function.Symbol.StorageClass == StorageClass.Static ? RVObjectSymbolBinding.Local : RVObjectSymbolBinding.Global;
            _symbols.Add(new RVObjectSymbol(label, TextSectionName, startOffset, size, binding, RVObjectSymbolKind.Function));
        }

        private string CreateUniqueGlobalLabel(string name)
        {
            var baseName = SanitizeSymbolName(name);
            if (baseName.Length == 0)
                baseName = "sym";
            var candidate = baseName;
            var suffix = 0;
            while (!_usedLabels.Add(candidate))
                candidate = baseName + "_" + (++suffix).ToString(CultureInfo.InvariantCulture);
            return candidate;
        }

        private string CreateExternalLabel(string name)
        {
            var label = SanitizeSymbolName(name);
            return label.Length == 0 ? "extern" : label;
        }

        private string CreateLocalLabel(string prefix)
        {
            var baseName = ".L" + SanitizeSymbolName(prefix);
            var candidate = baseName;
            while (!_usedLabels.Add(candidate))
                candidate = baseName + "_" + (++_nextLocalId).ToString(CultureInfo.InvariantCulture);
            return candidate;
        }

        private string CreateStringLiteral(string text)
        {
            if (_stringLabels.TryGetValue(text, out var existing))
                return existing;

            var label = CreateLocalLabel("str");
            var bytes = Encoding.UTF8.GetBytes(text);
            var offset = _rodata.Align(1);
            _rodata.DefineSymbol(label, offset, bytes.Length + 1, RVObjectSymbolBinding.Local, _symbols);
            _rodata.EmitBytes(bytes, bytes.Length);
            _rodata.EmitByte(0);
            _stringLabels.Add(text, label);
            return label;
        }

        private static string SanitizeSymbolName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            var sb = new StringBuilder(name.Length);
            for (var i = 0; i < name.Length; i++)
            {
                var ch = name[i];
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '$' || ch == '.')
                    sb.Append(ch);
                else
                    sb.Append('_');
            }

            if (sb.Length != 0 && char.IsDigit(sb[0]))
                sb.Insert(0, '_');
            return sb.ToString();
        }

        private static long ConvertIntegerConstant(object? value)
        {
            return value switch
            {
                null => 0,
                bool b => b ? 1 : 0,
                byte b => b,
                sbyte s => s,
                short s => s,
                ushort u => u,
                int i => i,
                uint u => u,
                long l => l,
                ulong u when u <= long.MaxValue => (long)u,
                ulong u => unchecked((long)u),
                char c => c,
                _ => Convert.ToInt64(value, CultureInfo.InvariantCulture),
            };
        }

        private static bool IsSignedIntegerType(QualifiedType type)
        {
            if (type.Type is EnumType)
                return true;
            return type.Type is BuiltinType builtin && builtin.BuiltinKind is BuiltinTypeKind.SignedChar or BuiltinTypeKind.Short or BuiltinTypeKind.Int or BuiltinTypeKind.Long or BuiltinTypeKind.LongLong;
        }

        private static bool IsUnsignedIntegerType(QualifiedType type)
        {
            return type.Type is BuiltinType builtin && builtin.BuiltinKind is BuiltinTypeKind.Bool or BuiltinTypeKind.Char or BuiltinTypeKind.UnsignedChar or BuiltinTypeKind.UnsignedShort or BuiltinTypeKind.UnsignedInt or BuiltinTypeKind.UnsignedLong or BuiltinTypeKind.UnsignedLongLong;
        }

        private static bool IsIntegerLike(QualifiedType type)
            => (type.Type.Kind is TypeKind.Builtin or TypeKind.Enum) && !IsFloatType(type) && !IsVoid(type);

        private static bool IsVoid(QualifiedType type)
            => type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Void };

        private static bool IsPointerLike(QualifiedType type)
            => type.Type.Kind is TypeKind.Pointer or TypeKind.Array or TypeKind.Function;

        private static bool IsAggregateType(QualifiedType type)
            => type.Type.Kind is TypeKind.Struct or TypeKind.Union or TypeKind.Array;

        private static bool IsFloatType(QualifiedType type)
            => IsFloat32(type) || IsFloat64(type) || IsLongDouble(type);

        private static bool IsFloat32(QualifiedType type)
            => type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Float };

        private static bool IsFloat64(QualifiedType type)
            => type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Double };

        private static bool IsLongDouble(QualifiedType type)
            => type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.LongDouble };

        private sealed class FunctionEmissionContext
        {
            private readonly RiscVCodeGenerator _owner;
            private readonly LirFunction _function;
            private readonly AllocationResult _allocation;
            private readonly string _functionLabel;
            private readonly IReadOnlyDictionary<LirBlock, string> _labels;
            private readonly bool _hasCalls;
            private readonly int _raSaveOffset;
            private readonly int _riscVVarArgsSaveAreaOffset;
            private readonly int _riscVVarArgsSaveAreaSize;
            private readonly int _totalFrameSize;
            private AbiCursor _parameters;

            public FunctionEmissionContext(
                RiscVCodeGenerator owner,
                LirFunction function,
                AllocationResult allocation,
                string functionLabel,
                IReadOnlyDictionary<LirBlock, string> labels)
            {
                _owner = owner ?? throw new ArgumentNullException(nameof(owner));
                _function = function ?? throw new ArgumentNullException(nameof(function));
                _allocation = allocation ?? throw new ArgumentNullException(nameof(allocation));
                _functionLabel = functionLabel ?? string.Empty;
                _labels = labels ?? throw new ArgumentNullException(nameof(labels));
                _hasCalls = function.Blocks.SelectMany(static b => b.Instructions).Any(static i => i.Kind is LirInstructionKind.Call or LirInstructionKind.InlineAssembly);
                _raSaveOffset = _hasCalls ? AlignUp(_allocation.Frame.FrameSize, _owner._target.PointerAlignment) : -1;
                _riscVVarArgsSaveAreaSize = ComputeRiscVVarArgsSaveAreaSize();
                var baseFrameSize = _allocation.Frame.FrameSize;
                if (_hasCalls)
                    baseFrameSize = Math.Max(baseFrameSize, checked(_raSaveOffset + _owner._target.PointerSize));
                _totalFrameSize = AlignUp(checked(baseFrameSize + _riscVVarArgsSaveAreaSize), _allocation.Frame.FrameAlignment);
                _riscVVarArgsSaveAreaOffset = _riscVVarArgsSaveAreaSize == 0 ? -1 : checked(_totalFrameSize - _riscVVarArgsSaveAreaSize);
            }

            public void EmitPrologue()
            {
                AdjustStack(-_totalFrameSize);
                SaveIncomingVarArgsPointer();
                foreach (var pair in _allocation.Frame.SavedRegisterOffsets.OrderBy(static p => p.Value))
                    StoreRegister(pair.Key, Sp, pair.Value, RegisterSaveSize(pair.Key));
                if (_hasCalls)
                    StoreRegister(Ra, Sp, _raSaveOffset, _owner._target.PointerSize);
                SaveIncomingHiddenReturnBuffer();
            }

            public void EmitBlocks()
            {
                foreach (var block in _function.Blocks)
                {
                    _owner._text.DefineLabel(LabelOf(block));
                    foreach (var instruction in block.Instructions)
                        EmitInstruction(instruction);
                }
            }

            public void EmitTrap()
                => Emit(new RVInstruction(RVInstrKind.Ebreak));

            private int ComputeRiscVVarArgsSaveAreaSize()
            {
                if (!_allocation.Frame.HasVarArgsPointer || !_owner._target.IsRiscV || _function.Symbol?.FunctionType?.IsVariadic != true)
                    return 0;

                var cursor = ComputeNamedArgumentCursor();
                var integerRegisters = TargetRegisterInfo.IntegerArgumentRegisters(_owner._target);
                var remainingRegisters = Math.Max(0, integerRegisters.Length - cursor.Integer);
                return checked(remainingRegisters * _owner._allocationOptions.StackArgumentSlotSize);
            }

            private AbiCursor ComputeNamedArgumentCursor()
            {
                var cursor = new AbiCursor();
                if (_allocation.Frame.HasHiddenReturnBuffer)
                    _ = CAbi.AssignHiddenReturnBufferLocation(_owner._target, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                var functionType = _function.Symbol?.FunctionType;
                if (functionType is not null)
                {
                    foreach (var parameter in functionType.Parameters)
                    {
                        var value = CAbi.ClassifyValue(_owner._target, parameter.Type, isReturn: false, isVariadicUnnamedArgument: false);
                        _ = CAbi.AssignArgumentLocation(value, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                    }
                }
                return cursor;
            }

            private void SaveIncomingVarArgsPointer()
            {
                if (!_allocation.Frame.HasVarArgsPointer)
                    return;

                var cursor = ComputeNamedArgumentCursor();
                if (_riscVVarArgsSaveAreaOffset >= 0)
                {
                    var integerRegisters = TargetRegisterInfo.IntegerArgumentRegisters(_owner._target);
                    for (var register = cursor.Integer; register < integerRegisters.Length; register++)
                        StoreRegister(integerRegisters[register], Sp, checked(_riscVVarArgsSaveAreaOffset + (register - cursor.Integer) * _owner._allocationOptions.StackArgumentSlotSize), _owner._target.PointerSize);
                    AddImmediate(GpScratch0, Sp, _riscVVarArgsSaveAreaOffset);
                }
                else
                {
                    AddImmediate(GpScratch0, Sp, IncomingStackOffset(cursor.Stack * _owner._allocationOptions.StackArgumentSlotSize));
                }
                StoreRegister(GpScratch0, Sp, _allocation.Frame.VarArgsPointerOffset, _owner._target.PointerSize);
            }

            private void SaveIncomingHiddenReturnBuffer()
            {
                var returnType = _function.Symbol?.FunctionType?.ReturnType;
                if (!returnType.HasValue || !CAbi.RequiresHiddenReturnBuffer(_owner._target, returnType.Value))
                    return;

                var cursor = new AbiCursor();
                var location = CAbi.AssignHiddenReturnBufferLocation(_owner._target, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                if (!_allocation.Frame.HasHiddenReturnBuffer)
                    return;

                if (location.Kind == AbiLocationKind.Register)
                {
                    StoreRegister(location.Register, Sp, _allocation.Frame.HiddenReturnBufferOffset, _owner._target.PointerSize);
                    _parameters = cursor;
                    return;
                }

                if (location.Kind == AbiLocationKind.Stack)
                {
                    LoadFromMemory(GpScratch0, Sp, IncomingStackOffset(location.StackByteOffset(_owner._allocationOptions.StackArgumentSlotSize)), _owner._target.PointerSize, signed: false);
                    StoreRegister(GpScratch0, Sp, _allocation.Frame.HiddenReturnBufferOffset, _owner._target.PointerSize);
                    _parameters = cursor;
                }
            }

            private void EmitInstruction(LirInstruction instruction)
            {
                switch (instruction.Kind)
                {
                    case LirInstructionKind.Nop:
                        EmitNop();
                        break;
                    case LirInstructionKind.Parameter:
                        EmitParameter(instruction);
                        break;
                    case LirInstructionKind.Copy:
                    case LirInstructionKind.Constant:
                        EmitCopyLike(instruction);
                        break;
                    case LirInstructionKind.Cast:
                    case LirInstructionKind.Convert:
                        EmitConvert(instruction);
                        break;
                    case LirInstructionKind.ParallelCopy:
                        EmitParallelCopy(instruction);
                        break;
                    case LirInstructionKind.Zero:
                        EmitZero(instruction);
                        break;
                    case LirInstructionKind.Unary:
                        EmitUnary(instruction);
                        break;
                    case LirInstructionKind.Binary:
                        EmitBinary(instruction);
                        break;
                    case LirInstructionKind.AddressOf:
                        EmitAddressOf(instruction);
                        break;
                    case LirInstructionKind.Load:
                        EmitLoad(instruction);
                        break;
                    case LirInstructionKind.Store:
                        EmitStore(instruction);
                        break;
                    case LirInstructionKind.ZeroMemory:
                        EmitZeroMemory(instruction);
                        break;
                    case LirInstructionKind.Call:
                        EmitCall(instruction);
                        break;
                    case LirInstructionKind.VaStart:
                        EmitVaStart(instruction);
                        break;
                    case LirInstructionKind.VaArg:
                        EmitVaArg(instruction);
                        break;
                    case LirInstructionKind.InlineAssembly:
                        EmitInlineAssembly(instruction);
                        break;
                    case LirInstructionKind.Jump:
                        if (!IsFallthroughTarget(instruction.Target))
                            EmitJump(LabelOf(instruction.Target));
                        break;
                    case LirInstructionKind.Branch:
                        EmitBranch(instruction);
                        break;
                    case LirInstructionKind.Switch:
                        EmitSwitch(instruction);
                        break;
                    case LirInstructionKind.Return:
                        EmitReturn(instruction);
                        break;
                    case LirInstructionKind.Unreachable:
                        EmitTrap();
                        break;
                    default:
                        throw Unsupported(instruction, $"Unsupported LIR instruction kind: {instruction.Kind}.");
                }
            }

            private void EmitInlineAssembly(LirInstruction instruction)
            {
                if (instruction.SourceStatement is not GimpleAsmStatement asmStatement)
                {
                    EmitInlineAssemblyText(instruction, instruction.Operator);
                    return;
                }

                var operands = new List<InlineAsmFormattedOperand>();
                var namedOperands = new Dictionary<string, int>(StringComparer.Ordinal);
                var labels = new List<string>();
                var namedLabels = new Dictionary<string, int>(StringComparer.Ordinal);
                var outputFinalizers = new List<Action>();
                var operandIndex = 0;
                var copyIndex = 0;

                foreach (var output in asmStatement.Outputs)
                {
                    InlineAsmFormattedOperand formatted;
                    var storage = output.Target is null
                        ? InlineAsmOperandStorage.Register
                        : InlineAsmConstraints.PreferredStorage(output.Constraint, output.Target.Type);
                    if (storage == InlineAsmOperandStorage.Memory)
                    {
                        if (operandIndex >= instruction.Operands.Length)
                            throw Unsupported(instruction, "Inline assembly output operand is missing from LIR.");
                        var operand = instruction.Operands[operandIndex++];
                        formatted = new InlineAsmFormattedOperand(output.Name, modifier => FormatRiscVAsmMemoryOperand(operand));
                    }
                    else
                    {
                        if (copyIndex >= instruction.ParallelCopies.Length)
                            throw Unsupported(instruction, "Inline assembly output register is missing from LIR.");
                        var destination = instruction.ParallelCopies[copyIndex++].Destination;
                        formatted = CreateRiscVAsmOutputOperand(output, destination, instruction, outputFinalizers);
                    }

                    AddAsmOperand(operands, namedOperands, formatted);
                }

                foreach (var input in asmStatement.Inputs)
                {
                    if (operandIndex >= instruction.Operands.Length)
                        throw Unsupported(instruction, "Inline assembly input operand is missing from LIR.");

                    var operand = instruction.Operands[operandIndex++];
                    var formatted = CreateRiscVAsmInputOperand(input, operand, instruction);
                    AddAsmOperand(operands, namedOperands, formatted);
                }

                foreach (var label in asmStatement.GotoLabels)
                {
                    if (operandIndex >= instruction.Operands.Length 
                        || instruction.Operands[operandIndex].Kind != LirOperandKind.Label 
                        || instruction.Operands[operandIndex].Label is null)
                        throw Unsupported(instruction, "Inline assembly goto label is missing from LIR.");

                    var text = LabelOf(instruction.Operands[operandIndex++].Label!);
                    if (label.Symbol is not null && !namedLabels.ContainsKey(label.Symbol.Name))
                        namedLabels.Add(label.Symbol.Name, labels.Count);
                    if (!namedLabels.ContainsKey(label.Name))
                        namedLabels.Add(label.Name, labels.Count);
                    labels.Add(text);
                }

                var expanded = InlineAsmTemplateExpander.Expand(
                    asmStatement.Text,
                    operands,
                    namedOperands,
                    labels,
                    namedLabels,
                    _owner.CreateLocalLabel(_functionLabel + "_asm_id"));
                EmitInlineAssemblyText(instruction, expanded);

                foreach (var finalize in outputFinalizers)
                    finalize();

                if (asmStatement.IsGoto && instruction.Target is not null && !IsFallthroughTarget(instruction.Target))
                    EmitJump(LabelOf(instruction.Target));
            }

            private void AddAsmOperand(List<InlineAsmFormattedOperand> operands, Dictionary<string, int> namedOperands, InlineAsmFormattedOperand operand)
            {
                if (operand.Name is not null && !namedOperands.ContainsKey(operand.Name))
                    namedOperands.Add(operand.Name, operands.Count);
                operands.Add(operand);
            }

            private InlineAsmFormattedOperand CreateRiscVAsmOutputOperand(
                GimpleAsmOperand operand, LirVirtualRegister destination, LirInstruction instruction, List<Action> finalizers)
            {
                var fixedRegister = TryGetRiscVConstraintRegister(operand.Constraint, destination.Type);
                if (fixedRegister.HasValue)
                {
                    if (operand.IsReadWrite)
                        LoadOperandIntoAs(LirOperand.ForRegister(destination), fixedRegister.Value, destination.Type, instruction);
                    finalizers.Add(() => StoreRiscVAsmOutput(destination, fixedRegister.Value));
                    return new InlineAsmFormattedOperand(operand.Name, modifier => RVRegisters.Format(ToAnyRegister(fixedRegister.Value)));
                }

                if (InlineAsmConstraints.HasExplicitRegister(operand.Constraint))
                    throw Unsupported(instruction, $"Invalid or unsupported explicit register constraint '{operand.Constraint}'.");

                var register = GetWritableRegister(destination, UsesHardwareFloating(destination.Type) ? FpScratch0 : GpScratch0);
                finalizers.Add(() => StoreWritableRegisterIfSpilled(destination, register));
                return new InlineAsmFormattedOperand(operand.Name, modifier => RVRegisters.Format(ToAnyRegister(register)));
            }

            private InlineAsmFormattedOperand CreateRiscVAsmInputOperand(GimpleAsmOperand operand, LirOperand value, LirInstruction instruction)
            {
                var storage = InlineAsmConstraints.PreferredStorage(operand.Constraint, value.Type);
                if (storage == InlineAsmOperandStorage.Memory)
                    return new InlineAsmFormattedOperand(operand.Name, modifier => FormatRiscVAsmMemoryOperand(value));
                if (storage == InlineAsmOperandStorage.Immediate)
                    return new InlineAsmFormattedOperand(operand.Name, modifier => FormatRiscVAsmImmediate(value));

                var fixedRegister = TryGetRiscVConstraintRegister(operand.Constraint, value.Type);
                if (fixedRegister.HasValue)
                {
                    LoadOperandIntoAs(value, fixedRegister.Value, value.Type, instruction);
                    return new InlineAsmFormattedOperand(operand.Name, modifier => RVRegisters.Format(ToAnyRegister(fixedRegister.Value)));
                }

                if (InlineAsmConstraints.HasExplicitRegister(operand.Constraint))
                    throw Unsupported(instruction, $"Invalid or unsupported explicit register constraint '{operand.Constraint}'.");

                var preferred = UsesHardwareFloating(value.Type) ? FpScratch1 : GpScratch1;
                return new InlineAsmFormattedOperand(operand.Name, modifier => RVRegisters.Format(ToAnyRegister(LoadOperand(value, preferred))));
            }

            private void StoreRiscVAsmOutput(LirVirtualRegister destination, MachineRegister source)
            {
                var writable = GetWritableRegister(destination, source);
                if (writable != source)
                    MoveRegister(writable, source);
                StoreWritableRegisterIfSpilled(destination, writable);
            }

            private MachineRegister? TryGetRiscVConstraintRegister(string constraint, QualifiedType type)
            {
                var explicitRegister = InlineAsmConstraints.ExplicitRegisterName(constraint);
                if (explicitRegister is null)
                    return null;

                var registerClass = CAbi.PreferredLirRegisterClass(_owner._target, type);
                if (TargetRegisterInfo.TryParseExplicitRegister(_owner._target, explicitRegister, registerClass, out var register))
                    return register;
                return null;
            }

            private string FormatRiscVAsmMemoryOperand(LirOperand operand)
            {
                if (operand.Kind == LirOperandKind.Address && operand.Address is not null)
                {
                    var address = BuildAddress(operand.Address, GpScratch0, GpScratch1);
                    return $"{address.Offset}({RVRegisters.Format(ToRegister(address.BaseRegister))})";
                }

                var register = LoadOperand(operand, GpScratch0);
                return "0(" + RVRegisters.Format(ToRegister(register)) + ")";
            }

            private string FormatRiscVAsmImmediate(LirOperand operand)
            {
                switch (operand.Kind)
                {
                    case LirOperandKind.Immediate:
                        return ConvertIntegerConstant(operand.Immediate).ToString(CultureInfo.InvariantCulture);
                    case LirOperandKind.Symbol when operand.Symbol is not null:
                        return _owner.GetSymbolLabel(operand.Symbol);
                    default:
                        return RVRegisters.Format(ToAnyRegister(LoadOperand(operand, GpScratch1)));
                }
            }


            private void EmitInlineAssemblyText(LirInstruction instruction, string text)
            {
                try
                {
                    _owner._text.EmitAssembly(text, _owner.CreateLocalLabel(_functionLabel + "_asm"), _owner._machineTarget);
                }
                catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
                {
                    throw Unsupported(instruction, $"Invalid RISC-V inline assembly: {ex.Message}");
                }
            }

            private void EmitParameter(LirInstruction instruction)
            {
                if (instruction.Result is null)
                    return;

                var type = instruction.Result.Type;
                var value = CAbi.ClassifyValue(_owner._target, type, isReturn: false, isVariadicUnnamedArgument: false);

                if (value.PassingKind == AbiPassingKind.Indirect)
                {
                    var loc = CAbi.AssignArgumentLocation(value, ref _parameters, _owner._allocationOptions.StackArgumentSlotSize);
                    var destinationAddress = MaterializeVirtualRegisterStorageAddress(instruction.Result, GpScratch0);
                    if (loc.Kind == AbiLocationKind.Register)
                    {
                        CopyMemory(destinationAddress, loc.Register, value.Size);
                    }
                    else if (loc.Kind == AbiLocationKind.Stack)
                    {
                        LoadFromMemory(
                            GpScratch1, 
                            Sp, 
                            IncomingStackOffset(loc.StackByteOffset(_owner._allocationOptions.StackArgumentSlotSize)), 
                            _owner._target.PointerSize, 
                            signed: false);
                        CopyMemory(destinationAddress, GpScratch1, value.Size);
                    }
                    else
                    {
                        throw Unsupported(instruction, "Invalid indirect parameter ABI location.");
                    }
                    return;
                }

                if (value.PassingKind == AbiPassingKind.MultiRegister)
                {
                    var destinationAddress = MaterializeVirtualRegisterStorageAddress(instruction.Result, GpScratch0);
                    for (var i = 0; i < value.Segments.Length; i++)
                    {
                        var segment = value.Segments[i];
                        var loc = CAbi.AssignSegmentArgumentLocation(segment, ref _parameters, _owner._allocationOptions.StackArgumentSlotSize);
                        if (loc.Kind == AbiLocationKind.Register)
                        {
                            StoreRawBitsToAddress(loc.Register, destinationAddress, segment.Offset, segment.Size);
                        }
                        else if (loc.Kind == AbiLocationKind.Stack)
                        {
                            LoadRawBitsFromMemory(GpScratch1, Sp, IncomingStackOffset(loc.StackByteOffset(_owner._allocationOptions.StackArgumentSlotSize)), segment.Size);
                            StoreRawBitsToAddress(GpScratch1, destinationAddress, segment.Offset, segment.Size);
                        }
                        else
                        {
                            throw Unsupported(instruction, "Invalid multi-register parameter ABI location.");
                        }
                    }
                    return;
                }

                if (value.PassingKind == AbiPassingKind.Stack && IsAggregateType(type))
                {
                    var loc = CAbi.AssignArgumentLocation(value, ref _parameters, _owner._allocationOptions.StackArgumentSlotSize);
                    var destinationAddress = MaterializeVirtualRegisterStorageAddress(instruction.Result, GpScratch0);
                    AddImmediate(GpScratch1, Sp, IncomingStackOffset(loc.StackByteOffset(_owner._allocationOptions.StackArgumentSlotSize)));
                    CopyMemory(destinationAddress, GpScratch1, value.Size);
                    return;
                }

                var scalarLocation = CAbi.AssignArgumentLocation(value, ref _parameters, _owner._allocationOptions.StackArgumentSlotSize);
                var destination = GetWritableRegister(instruction.Result, IsFloatType(type) && scalarLocation.RegisterClass == AbiRegisterClass.Floating ? FpScratch0 : GpScratch0);
                if (scalarLocation.Kind == AbiLocationKind.Register)
                {
                    MoveRegister(destination, scalarLocation.Register);
                }
                else if (scalarLocation.Kind == AbiLocationKind.Stack)
                {
                    LoadFromMemory(destination, Sp, IncomingStackOffset(
                        scalarLocation.StackByteOffset(_owner._allocationOptions.StackArgumentSlotSize)), SizeOfRegisterType(type), IsSignedIntegerType(type));
                    NormalizeScalarRegister(destination, type);
                }
                else
                {
                    throw Unsupported(instruction, "Invalid scalar parameter ABI location.");
                }

                StoreWritableRegisterIfSpilled(instruction.Result, destination);
            }

            private void EmitCopyLike(LirInstruction instruction)
            {
                if (instruction.Result is null)
                    return;
                if (instruction.Operands.Length == 0)
                    throw Unsupported(instruction, "Copy-like instruction has no source operand.");

                if (IsAggregateType(instruction.Result.Type))
                {
                    EmitAggregateCopyToRegisterStorage(instruction.Result, instruction.Operands[0], instruction);
                    return;
                }

                if (RequiresStackBackedScalar(instruction.Result.Type) || RequiresStackBackedScalar(instruction.Operands[0].Type))
                {
                    EmitStackBackedScalarCopy(instruction.Result, instruction.Operands[0], instruction);
                    return;
                }

                var destination = GetWritableRegister(instruction.Result, UsesHardwareFloating(instruction.Result.Type) ? FpScratch0 : GpScratch0);
                LoadOperandIntoAs(instruction.Operands[0], destination, instruction.Result.Type, instruction);
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
            }

            private void EmitZero(LirInstruction instruction)
            {
                if (instruction.Result is null)
                    return;

                if (IsAggregateType(instruction.Result.Type))
                {
                    var destinationAddress = MaterializeVirtualRegisterStorageAddress(instruction.Result, GpScratch0);
                    ZeroMemory(destinationAddress, SizeOf(instruction.Result.Type));
                    return;
                }

                if (RequiresStackBackedScalar(instruction.Result.Type))
                {
                    var destinationAddress = MaterializeVirtualRegisterStorageAddress(instruction.Result, GpScratch0);
                    ZeroMemory(destinationAddress, SizeOf(instruction.Result.Type));
                    return;
                }

                var destination = GetWritableRegister(instruction.Result, UsesHardwareFloating(instruction.Result.Type) ? FpScratch0 : GpScratch0);
                if (IsFloatType(instruction.Result.Type) && UsesHardwareFloating(instruction.Result.Type))
                    LoadFloatingImmediate(destination, 0.0, instruction.Result.Type);
                else
                    MoveRegister(destination, MachineRegister.X0);
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
            }

            private void EmitUnary(LirInstruction instruction)
            {
                if (instruction.Result is null)
                    throw Unsupported(instruction, "Unary instruction has no result.");
                if (instruction.Operands.Length == 0)
                    throw Unsupported(instruction, "Unary instruction has no operand.");
                if (IsFloatType(instruction.Result.Type) || IsFloatType(instruction.Operands[0].Type))
                {
                    EmitFloatingUnary(instruction);
                    return;
                }

                if (RequiresStackBackedScalar(instruction.Operands[0].Type) && instruction.Operator == "!")
                {
                    EmitStackBackedScalarIsZero(instruction.Result, instruction.Operands[0], instruction);
                    return;
                }

                if (TryEmitSoftwareIntegerUnary(instruction))
                    return;

                if (RequiresSoftwareScalar(instruction.Result.Type) || RequiresSoftwareScalar(instruction.Operands[0].Type))
                    throw HelperRequired(instruction, SelectScalarMoveHelper(instruction.Result.Type), 
                        "Unary operation for scalar wider than one machine register is not implemented yet.");

                var dst = GetWritableRegister(instruction.Result, GpScratch0);
                var src = LoadOperand(instruction.Operands[0], GpScratch1);
                switch (instruction.Operator)
                {
                    case "+":
                        MoveRegister(dst, src);
                        break;
                    case "-":
                        Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(dst), RVRegister.X0, ToRegister(src)));
                        break;
                    case "~":
                        EmitImm(RVInstrKind.Xori, dst, src, -1);
                        break;
                    case "!":
                        EmitImm(RVInstrKind.Sltiu, dst, src, 1);
                        break;
                    default:
                        throw Unsupported(instruction, "Unsupported unary operator '" + instruction.Operator + "'.");
                }

                NormalizeIntegerRegister(dst, instruction.Result.Type);
                StoreWritableRegisterIfSpilled(instruction.Result, dst);
            }

            private bool TryEmitSoftwareIntegerUnary(LirInstruction instruction)
            {
                if (instruction.Result is null || instruction.Operands.Length == 0)
                    return false;
                if (!IsRv32WideInteger(instruction.Result.Type) && !IsRv32WideInteger(instruction.Operands[0].Type))
                    return false;

                if (!IsRv32WideInteger(instruction.Result.Type) || !IsRv32WideInteger(instruction.Operands[0].Type))
                    throw HelperRequired(instruction, SelectConversionHelper(instruction.Operands[0].Type, instruction.Result.Type), 
                        "Unary operation requires an explicit wide integer conversion.");

                LoadWideIntegerOperand(instruction.Operands[0], GpScratch0, GpScratch1, instruction);
                switch (instruction.Operator)
                {
                    case "+":
                        break;
                    case "-":
                        Emit(RVInstruction.R(RVInstrKind.Sltu, ToRegister(GpScratch2), RVRegister.X0, ToRegister(GpScratch0)));
                        Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(GpScratch0), RVRegister.X0, ToRegister(GpScratch0)));
                        Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(GpScratch1), RVRegister.X0, ToRegister(GpScratch1)));
                        Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(GpScratch1), ToRegister(GpScratch1), ToRegister(GpScratch2)));
                        break;
                    case "~":
                        EmitImm(RVInstrKind.Xori, GpScratch0, GpScratch0, -1);
                        EmitImm(RVInstrKind.Xori, GpScratch1, GpScratch1, -1);
                        break;
                    default:
                        return false;
                }

                StoreWideIntegerResult(instruction.Result, GpScratch0, GpScratch1);
                return true;
            }

            private bool TryEmitSoftwareIntegerBinary(LirInstruction instruction)
            {
                if (instruction.Result is null || instruction.Operands.Length != 2)
                    return false;
                if (!IsRv32WideInteger(instruction.Result.Type) && !IsRv32WideInteger(instruction.Operands[0].Type) && !IsRv32WideInteger(instruction.Operands[1].Type))
                    return false;

                var op = instruction.Operator;
                if (op is "==" or "!=" or "<" or ">" or "<=" or ">=")
                {
                    EmitWideIntegerRelation(instruction);
                    return true;
                }

                if (!IsRv32WideInteger(instruction.Result.Type))
                    throw HelperRequired(instruction, SelectScalarMoveHelper(instruction.Result.Type), 
                        "Wide integer binary operation requires a wide integer result.");

                switch (op)
                {
                    case "+":
                        LoadWideIntegerOperand(instruction.Operands[0], GpScratch0, GpScratch1, instruction);
                        LoadWideIntegerOperand(instruction.Operands[1], GpScratch2, GpScratch3, instruction);
                        Emit(RVInstruction.R(RVInstrKind.Add, ToRegister(GpScratch0), ToRegister(GpScratch0), ToRegister(GpScratch2)));
                        Emit(RVInstruction.R(RVInstrKind.Sltu, ToRegister(GpScratch2), ToRegister(GpScratch0), ToRegister(GpScratch2)));
                        Emit(RVInstruction.R(RVInstrKind.Add, ToRegister(GpScratch1), ToRegister(GpScratch1), ToRegister(GpScratch3)));
                        Emit(RVInstruction.R(RVInstrKind.Add, ToRegister(GpScratch1), ToRegister(GpScratch1), ToRegister(GpScratch2)));
                        StoreWideIntegerResult(instruction.Result, GpScratch0, GpScratch1);
                        return true;
                    case "-":
                        LoadWideIntegerOperand(instruction.Operands[0], GpScratch0, GpScratch1, instruction);
                        LoadWideIntegerOperand(instruction.Operands[1], GpScratch2, GpScratch3, instruction);
                        StoreToMemory(GpScratch0, Sp, _allocation.Frame.FloatingImmediateTempOffset, 4);
                        Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(GpScratch0), ToRegister(GpScratch0), ToRegister(GpScratch2)));
                        LoadFromMemory(GpScratch2, Sp, _allocation.Frame.FloatingImmediateTempOffset, 4, signed: false);
                        Emit(RVInstruction.R(RVInstrKind.Sltu, ToRegister(GpScratch2), ToRegister(GpScratch2), ToRegister(GpScratch0)));
                        Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(GpScratch1), ToRegister(GpScratch1), ToRegister(GpScratch3)));
                        Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(GpScratch1), ToRegister(GpScratch1), ToRegister(GpScratch2)));
                        StoreWideIntegerResult(instruction.Result, GpScratch0, GpScratch1);
                        return true;
                    case "&":
                    case "|":
                    case "^":
                        LoadWideIntegerOperand(instruction.Operands[0], GpScratch0, GpScratch1, instruction);
                        LoadWideIntegerOperand(instruction.Operands[1], GpScratch2, GpScratch3, instruction);
                        var opcode = op == "&" ? RVInstrKind.And : op == "|" ? RVInstrKind.Or : RVInstrKind.Xor;
                        Emit(RVInstruction.R(opcode, ToRegister(GpScratch0), ToRegister(GpScratch0), ToRegister(GpScratch2)));
                        Emit(RVInstruction.R(opcode, ToRegister(GpScratch1), ToRegister(GpScratch1), ToRegister(GpScratch3)));
                        StoreWideIntegerResult(instruction.Result, GpScratch0, GpScratch1);
                        return true;
                    default:
                        return false;
                }
            }

            private void EmitWideIntegerRelation(LirInstruction instruction)
            {
                var left = instruction.Operands[0];
                var right = instruction.Operands[1];
                var op = instruction.Operator;
                var signed = IsSignedIntegerType(left.Type) || IsSignedIntegerType(right.Type);
                var destination = GetWritableRegister(instruction.Result!, GpScratch0);

                if (op == "==" || op == "!=")
                {
                    LoadWideIntegerOperand(left, GpScratch0, GpScratch1, instruction);
                    LoadWideIntegerOperand(right, GpScratch2, GpScratch3, instruction);
                    Emit(RVInstruction.R(RVInstrKind.Xor, ToRegister(GpScratch0), ToRegister(GpScratch0), ToRegister(GpScratch2)));
                    Emit(RVInstruction.R(RVInstrKind.Xor, ToRegister(GpScratch1), ToRegister(GpScratch1), ToRegister(GpScratch3)));
                    Emit(RVInstruction.R(RVInstrKind.Or, ToRegister(GpScratch0), ToRegister(GpScratch0), ToRegister(GpScratch1)));
                    if (op == "==")
                        EmitImm(RVInstrKind.Sltiu, destination, GpScratch0, 1);
                    else
                        Emit(RVInstruction.R(RVInstrKind.Sltu, ToRegister(destination), RVRegister.X0, ToRegister(GpScratch0)));
                }
                else
                {
                    if (op == ">" || op == "<=")
                        EmitWideIntegerLessThan(right, left, destination, signed, instruction);
                    else
                        EmitWideIntegerLessThan(left, right, destination, signed, instruction);

                    if (op == "<=" || op == ">=")
                        EmitImm(RVInstrKind.Xori, destination, destination, 1);
                }

                StoreWritableRegisterIfSpilled(instruction.Result!, destination);
            }

            private void EmitWideIntegerLessThan(LirOperand left, LirOperand right, MachineRegister destination, bool signed, LirInstruction instruction)
            {
                LoadWideIntegerOperand(left, GpScratch0, GpScratch1, instruction);
                LoadWideIntegerOperand(right, GpScratch2, GpScratch3, instruction);
                Emit(RVInstruction.R(RVInstrKind.Sltu, ToRegister(GpScratch0), ToRegister(GpScratch0), ToRegister(GpScratch2)));
                Emit(RVInstruction.R(RVInstrKind.Xor, ToRegister(GpScratch2), ToRegister(GpScratch1), ToRegister(GpScratch3)));
                EmitImm(RVInstrKind.Sltiu, GpScratch2, GpScratch2, 1);
                Emit(RVInstruction.R(RVInstrKind.And, ToRegister(GpScratch0), ToRegister(GpScratch0), ToRegister(GpScratch2)));
                Emit(RVInstruction.R(signed ? RVInstrKind.Slt : RVInstrKind.Sltu, ToRegister(GpScratch1), ToRegister(GpScratch1), ToRegister(GpScratch3)));
                Emit(RVInstruction.R(RVInstrKind.Or, ToRegister(destination), ToRegister(GpScratch1), ToRegister(GpScratch0)));
            }

            private bool TryEmitSoftwareIntegerConvert(LirInstruction instruction)
            {
                if (instruction.Result is null || instruction.Operands.Length == 0)
                    return false;

                var source = instruction.Operands[0];
                var sourceWide = IsRv32WideInteger(source.Type);
                var destinationWide = IsRv32WideInteger(instruction.Result.Type);
                if (!sourceWide && !destinationWide)
                    return false;

                if ((!IsIntegerLike(source.Type) && !IsPointerLike(source.Type)) || (!IsIntegerLike(instruction.Result.Type) && !IsPointerLike(instruction.Result.Type)))
                    return false;

                if (destinationWide)
                {
                    if (source.Kind is LirOperandKind.Undefined or LirOperandKind.Void or LirOperandKind.None)
                    {
                        var address = MaterializeVirtualRegisterStorageAddress(instruction.Result, GpScratch0);
                        ZeroMemory(address, SizeOf(instruction.Result.Type));
                        return true;
                    }

                    if (sourceWide)
                    {
                        EmitStackBackedScalarCopy(instruction.Result, source, instruction);
                        return true;
                    }

                    var low = LoadOperand(source, GpScratch0);
                    if (low != GpScratch0)
                        MoveRegister(GpScratch0, low);
                    NormalizeIntegerRegister(GpScratch0, source.Type);
                    if (IsSignedIntegerType(source.Type))
                        EmitShiftImmediate(RVInstrKind.Srai, GpScratch1, GpScratch0, 31);
                    else
                        MoveRegister(GpScratch1, MachineRegister.X0);
                    StoreWideIntegerResult(instruction.Result, GpScratch0, GpScratch1);
                    return true;
                }

                if (sourceWide)
                {
                    if (instruction.Result.Type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Bool })
                    {
                        var destination = GetWritableRegister(instruction.Result, GpScratch0);
                        var nonZero = EmitStackBackedScalarNonZero(source, instruction);
                        MoveRegister(destination, nonZero);
                        StoreWritableRegisterIfSpilled(instruction.Result, destination);
                        return true;
                    }
                    {
                        var destination = GetWritableRegister(instruction.Result, GpScratch0);
                        LoadWideIntegerLowWord(source, destination, instruction);
                        NormalizeIntegerRegister(destination, instruction.Result.Type);
                        StoreWritableRegisterIfSpilled(instruction.Result, destination);
                        return true;
                    }
                }

                return false;
            }

            private void LoadWideIntegerOperand(LirOperand operand, MachineRegister low, MachineRegister high, LirInstruction instruction)
            {
                if (SizeOf(operand.Type) <= _owner._target.RegisterSize && !IsRv32WideInteger(operand.Type))
                {
                    var scalar = LoadOperand(operand, low);
                    if (scalar != low)
                        MoveRegister(low, scalar);
                    NormalizeIntegerRegister(low, operand.Type);
                    if (IsSignedIntegerType(operand.Type))
                        EmitShiftImmediate(RVInstrKind.Srai, high, low, 31);
                    else
                        MoveRegister(high, MachineRegister.X0);
                    return;
                }

                var address = MaterializeScalarStorageAddress(operand, GpScratch3, instruction);
                LoadFromMemory(low, address, WideIntegerLowOffset(), 4, signed: false);
                LoadFromMemory(high, address, WideIntegerHighOffset(), 4, signed: false);
            }

            private void LoadWideIntegerLowWord(LirOperand operand, MachineRegister destination, LirInstruction instruction)
            {
                if (SizeOf(operand.Type) <= _owner._target.RegisterSize && !IsRv32WideInteger(operand.Type))
                {
                    var scalar = LoadOperand(operand, destination);
                    if (scalar != destination)
                        MoveRegister(destination, scalar);
                    return;
                }

                var address = MaterializeScalarStorageAddress(operand, GpScratch3, instruction);
                LoadFromMemory(destination, address, WideIntegerLowOffset(), 4, signed: false);
            }

            private void StoreWideIntegerResult(LirVirtualRegister destination, MachineRegister low, MachineRegister high)
            {
                var address = MaterializeVirtualRegisterStorageAddress(destination, GpScratch3);
                StoreToMemory(low, address, WideIntegerLowOffset(), 4);
                StoreToMemory(high, address, WideIntegerHighOffset(), 4);
            }

            private int WideIntegerLowOffset()
                => _owner._target.Endianness == TargetEndianness.Little ? 0 : 4;

            private int WideIntegerHighOffset()
                => _owner._target.Endianness == TargetEndianness.Little ? 4 : 0;

            private bool IsRv32WideInteger(QualifiedType type)
                => _owner._target.Is32Bit && IsIntegerLike(type) && SizeOf(type) == 8;

            private void EmitFloatingUnary(LirInstruction instruction)
            {
                var operandType = instruction.Operands[0].Type;
                if (IsLongDouble(operandType) || IsLongDouble(instruction.Result!.Type))
                    throw HelperRequired(instruction, SelectFloatingHelper(instruction.Operator, operandType), "long double operation requires a runtime helper.");
                RequireFloatingHardware(operandType, instruction);
                if (instruction.Operator == "!")
                {
                    var dst = GetWritableRegister(instruction.Result!, GpScratch0);
                    var src = LoadOperand(instruction.Operands[0], FpScratch1);
                    LoadFloatingImmediate(FpScratch2, 0.0, operandType);
                    EmitFloatingCompare(IsFloat32(operandType) ? RVInstrKind.FeqS : RVInstrKind.FeqD, dst, src, FpScratch2);
                    StoreWritableRegisterIfSpilled(instruction.Result!, dst);
                    return;
                }

                if (!IsFloatType(instruction.Result!.Type) || !IsFloatType(operandType))
                    throw Unsupported(instruction, "Unsupported floating-point unary conversion shape.");

                var destination = GetWritableRegister(instruction.Result, FpScratch0);
                var source = LoadOperand(instruction.Operands[0], FpScratch1);
                switch (instruction.Operator)
                {
                    case "+":
                        MoveRegister(destination, source);
                        break;
                    case "-":
                        EmitFloatingR(IsFloat32(operandType) ? RVInstrKind.FsgnjnS : RVInstrKind.FsgnjnD, destination, source, source);
                        break;
                    default:
                        throw Unsupported(instruction, "Unsupported floating-point unary operator '" + instruction.Operator + "'.");
                }

                StoreWritableRegisterIfSpilled(instruction.Result, destination);
            }

            private void EmitBinary(LirInstruction instruction)
            {
                if (instruction.Result is null)
                    throw Unsupported(instruction, "Binary instruction has no result.");
                if (instruction.Operands.Length != 2)
                    throw Unsupported(instruction, "Binary instruction expects two operands.");
                if (IsFloatType(instruction.Result.Type) || IsFloatType(instruction.Operands[0].Type) || IsFloatType(instruction.Operands[1].Type))
                {
                    EmitFloatingBinary(instruction);
                    return;
                }

                if (TryEmitSoftwareIntegerBinary(instruction))
                    return;

                if (RequiresSoftwareScalar(instruction.Result.Type) || RequiresSoftwareScalar(instruction.Operands[0].Type) || RequiresSoftwareScalar(instruction.Operands[1].Type))
                    throw HelperRequired(instruction, SelectScalarMoveHelper(instruction.Result.Type), "Binary operation for scalar wider than one machine register is not implemented yet.");

                if (TryEmitPointerBinary(instruction))
                    return;

                var dst = GetWritableRegister(instruction.Result, GpScratch0);
                var left = LoadOperand(instruction.Operands[0], GpScratch1);
                var right = LoadOperand(instruction.Operands[1], GpScratch2);
                EmitIntegerBinary(instruction, dst, left, right);
                NormalizeIntegerRegister(dst, instruction.Result.Type);
                StoreWritableRegisterIfSpilled(instruction.Result, dst);
            }

            private void EmitFloatingBinary(LirInstruction instruction)
            {
                var lhsType = instruction.Operands[0].Type;
                var rhsType = instruction.Operands[1].Type;
                var floatType = SelectFloatingOperationType(lhsType, rhsType, instruction.Result!.Type);
                if (IsLongDouble(floatType))
                    throw HelperRequired(instruction, SelectFloatingHelper(instruction.Operator, floatType), "long double operation requires a runtime helper.");
                RequireFloatingHardware(floatType, instruction);

                var left = LoadOperandAsFloating(instruction.Operands[0], floatType, FpScratch1, instruction);
                var right = LoadOperandAsFloating(instruction.Operands[1], floatType, FpScratch2, instruction);
                var op = instruction.Operator;

                if (op is "+" or "-" or "*" or "/")
                {
                    if (!IsFloatType(instruction.Result.Type))
                        throw Unsupported(instruction, "Floating arithmetic result must be floating-point.");
                    var dst = GetWritableRegister(instruction.Result, FpScratch0);
                    EmitFloatingR(SelectFloatingArithmeticOpcode(op, floatType), dst, left, right);
                    StoreWritableRegisterIfSpilled(instruction.Result, dst);
                    return;
                }

                if (op is "==" or "!=" or "<" or ">" or "<=" or ">=")
                {
                    var dst = GetWritableRegister(instruction.Result, GpScratch0);
                    EmitFloatingRelation(op, floatType, dst, left, right);
                    StoreWritableRegisterIfSpilled(instruction.Result, dst);
                    return;
                }

                throw Unsupported(instruction, "Unsupported floating-point binary operator '" + op + "'.");
            }

            private QualifiedType SelectFloatingOperationType(QualifiedType left, QualifiedType right, QualifiedType result)
            {
                if (IsLongDouble(left) || IsLongDouble(right) || IsLongDouble(result))
                    return IsLongDouble(left) ? left : IsLongDouble(right) ? right : result;
                if (IsFloat64(left) || IsFloat64(right) || IsFloat64(result))
                    return IsFloat64(left) ? left : IsFloat64(right) ? right : result;
                if (IsFloat32(left) || IsFloat32(right) || IsFloat32(result))
                    return IsFloat32(left) ? left : IsFloat32(right) ? right : result;
                throw new InvalidOperationException("Expected at least one floating-point operand.");
            }

            private MachineRegister LoadOperandAsFloating(LirOperand operand, QualifiedType floatType, MachineRegister destination, LirInstruction instruction)
            {
                if (IsFloatType(operand.Type))
                {
                    var source = LoadOperand(operand, destination);
                    if (SameBuiltinFloatingType(operand.Type, floatType))
                        return source;
                    if (source != destination)
                        MoveRegister(destination, source);
                    EmitFloatingPrecisionConversion(destination, destination, operand.Type, floatType, instruction);
                    return destination;
                }

                if (IsIntegerLike(operand.Type))
                {
                    var source = LoadOperand(operand, GpScratch0);
                    EmitIntegerToFloat(destination, source, operand.Type, floatType, instruction);
                    return destination;
                }

                throw Unsupported(instruction, "Cannot convert operand to floating-point for binary operation.");
            }

            private void EmitFloatingRelation(string op, QualifiedType type, MachineRegister destination, MachineRegister left, MachineRegister right)
            {
                switch (op)
                {
                    case "==":
                        EmitFloatingCompare(IsFloat32(type) ? RVInstrKind.FeqS : RVInstrKind.FeqD, destination, left, right);
                        return;
                    case "!=":
                        EmitFloatingCompare(IsFloat32(type) ? RVInstrKind.FeqS : RVInstrKind.FeqD, destination, left, right);
                        EmitImm(RVInstrKind.Xori, destination, destination, 1);
                        return;
                    case "<":
                        EmitFloatingCompare(IsFloat32(type) ? RVInstrKind.FltS : RVInstrKind.FltD, destination, left, right);
                        return;
                    case ">":
                        EmitFloatingCompare(IsFloat32(type) ? RVInstrKind.FltS : RVInstrKind.FltD, destination, right, left);
                        return;
                    case "<=":
                        EmitFloatingCompare(IsFloat32(type) ? RVInstrKind.FleS : RVInstrKind.FleD, destination, left, right);
                        return;
                    case ">=":
                        EmitFloatingCompare(IsFloat32(type) ? RVInstrKind.FleS : RVInstrKind.FleD, destination, right, left);
                        return;
                    default:
                        throw new InvalidOperationException("Invalid floating-point relation operator.");
                }
            }

            private static RVInstrKind SelectFloatingArithmeticOpcode(string op, QualifiedType type)
            {
                var single = IsFloat32(type);
                return op switch
                {
                    "+" => single ? RVInstrKind.FaddS : RVInstrKind.FaddD,
                    "-" => single ? RVInstrKind.FsubS : RVInstrKind.FsubD,
                    "*" => single ? RVInstrKind.FmulS : RVInstrKind.FmulD,
                    "/" => single ? RVInstrKind.FdivS : RVInstrKind.FdivD,
                    _ => throw new InvalidOperationException("Invalid floating-point arithmetic operator."),
                };
            }

            private bool TryEmitPointerBinary(LirInstruction instruction)
            {
                var lhsType = instruction.Operands[0].Type;
                var rhsType = instruction.Operands[1].Type;
                var resultType = instruction.Result!.Type;
                var op = instruction.Operator;

                if (op == "+" && IsPointerLike(lhsType) && IsIntegerLike(rhsType))
                {
                    var dst = GetWritableRegister(instruction.Result, GpScratch0);
                    var ptr = LoadOperand(instruction.Operands[0], GpScratch1);
                    var index = LoadOperand(instruction.Operands[1], GpScratch2);
                    MoveRegister(GpScratch2, index);
                    ScaleIndex(GpScratch2, PointerScale(lhsType), GpScratch3);
                    Emit(RVInstruction.R(RVInstrKind.Add, ToRegister(dst), ToRegister(ptr), ToRegister(GpScratch2)));
                    StoreWritableRegisterIfSpilled(instruction.Result, dst);
                    return true;
                }

                if (op == "+" && IsIntegerLike(lhsType) && IsPointerLike(rhsType))
                {
                    var dst = GetWritableRegister(instruction.Result, GpScratch0);
                    var index = LoadOperand(instruction.Operands[0], GpScratch2);
                    var ptr = LoadOperand(instruction.Operands[1], GpScratch1);
                    MoveRegister(GpScratch2, index);
                    ScaleIndex(GpScratch2, PointerScale(rhsType), GpScratch3);
                    Emit(RVInstruction.R(RVInstrKind.Add, ToRegister(dst), ToRegister(ptr), ToRegister(GpScratch2)));
                    StoreWritableRegisterIfSpilled(instruction.Result, dst);
                    return true;
                }

                if (op == "-" && IsPointerLike(lhsType) && IsIntegerLike(rhsType))
                {
                    var dst = GetWritableRegister(instruction.Result, GpScratch0);
                    var ptr = LoadOperand(instruction.Operands[0], GpScratch1);
                    var index = LoadOperand(instruction.Operands[1], GpScratch2);
                    MoveRegister(GpScratch2, index);
                    ScaleIndex(GpScratch2, PointerScale(lhsType), GpScratch3);
                    Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(dst), ToRegister(ptr), ToRegister(GpScratch2)));
                    StoreWritableRegisterIfSpilled(instruction.Result, dst);
                    return true;
                }

                if (op == "-" && IsPointerLike(lhsType) && IsPointerLike(rhsType))
                {
                    var dst = GetWritableRegister(instruction.Result, GpScratch0);
                    var left = LoadOperand(instruction.Operands[0], GpScratch1);
                    var right = LoadOperand(instruction.Operands[1], GpScratch2);
                    Emit(RVInstruction.R(RVInstrKind.Sub, ToRegister(dst), ToRegister(left), ToRegister(right)));
                    var scale = PointerScale(lhsType);
                    if (scale > 1)
                        DivideRegisterByScale(dst, scale, GpScratch3);
                    StoreWritableRegisterIfSpilled(instruction.Result, dst);
                    return true;
                }

                if ((op == "==" || op == "!=") && IsPointerLike(lhsType) && IsPointerLike(rhsType))
                {
                    var dst = GetWritableRegister(instruction.Result, GpScratch0);
                    var left = LoadOperand(instruction.Operands[0], GpScratch1);
                    var right = LoadOperand(instruction.Operands[1], GpScratch2);
                    EmitEquality(dst, left, right, op == "==");
                    StoreWritableRegisterIfSpilled(instruction.Result, dst);
                    return true;
                }

                return false;
            }

            private void EmitIntegerBinary(LirInstruction instruction, MachineRegister dst, MachineRegister left, MachineRegister right)
            {
                var signed = IsSignedIntegerType(instruction.Operands[0].Type) || IsSignedIntegerType(instruction.Operands[1].Type);
                var wordOp = _owner._target.Is64Bit && Math.Max(SizeOf(instruction.Operands[0].Type), SizeOf(instruction.Operands[1].Type)) <= 4;
                switch (instruction.Operator)
                {
                    case "+": Emit(RVInstruction.R(wordOp ? RVInstrKind.Addw : RVInstrKind.Add, ToRegister(dst), ToRegister(left), ToRegister(right))); return;
                    case "-": Emit(RVInstruction.R(wordOp ? RVInstrKind.Subw : RVInstrKind.Sub, ToRegister(dst), ToRegister(left), ToRegister(right))); return;
                    case "*": RequireM(instruction); Emit(RVInstruction.R(wordOp ? RVInstrKind.Mulw : RVInstrKind.Mul, ToRegister(dst), ToRegister(left), ToRegister(right))); return;
                    case "/": RequireM(instruction); Emit(RVInstruction.R(signed ? (wordOp ? RVInstrKind.Divw : RVInstrKind.Div) : (wordOp ? RVInstrKind.Divuw : RVInstrKind.Divu), ToRegister(dst), ToRegister(left), ToRegister(right))); return;
                    case "%": RequireM(instruction); Emit(RVInstruction.R(signed ? (wordOp ? RVInstrKind.Remw : RVInstrKind.Rem) : (wordOp ? RVInstrKind.Remuw : RVInstrKind.Remu), ToRegister(dst), ToRegister(left), ToRegister(right))); return;
                    case "&": Emit(RVInstruction.R(RVInstrKind.And, ToRegister(dst), ToRegister(left), ToRegister(right))); return;
                    case "|": Emit(RVInstruction.R(RVInstrKind.Or, ToRegister(dst), ToRegister(left), ToRegister(right))); return;
                    case "^": Emit(RVInstruction.R(RVInstrKind.Xor, ToRegister(dst), ToRegister(left), ToRegister(right))); return;
                    case "<<": Emit(RVInstruction.R(wordOp ? RVInstrKind.Sllw : RVInstrKind.Sll, ToRegister(dst), ToRegister(left), ToRegister(right))); return;
                    case ">>": Emit(RVInstruction.R(signed ? (wordOp ? RVInstrKind.Sraw : RVInstrKind.Sra) : (wordOp ? RVInstrKind.Srlw : RVInstrKind.Srl), ToRegister(dst), ToRegister(left), ToRegister(right))); return;
                    case "==": EmitEquality(dst, left, right, equal: true); return;
                    case "!=": EmitEquality(dst, left, right, equal: false); return;
                    case "<": EmitLessThan(dst, left, right, signed); return;
                    case ">": EmitLessThan(dst, right, left, signed); return;
                    case "<=": EmitLessThan(dst, right, left, signed); EmitImm(RVInstrKind.Xori, dst, dst, 1); return;
                    case ">=": EmitLessThan(dst, left, right, signed); EmitImm(RVInstrKind.Xori, dst, dst, 1); return;
                    default: throw Unsupported(instruction, "Unsupported binary operator '" + instruction.Operator + "'.");
                }
            }

            private void EmitEquality(MachineRegister dst, MachineRegister left, MachineRegister right, bool equal)
            {
                Emit(RVInstruction.R(RVInstrKind.Xor, ToRegister(dst), ToRegister(left), ToRegister(right)));
                if (equal)
                    EmitImm(RVInstrKind.Sltiu, dst, dst, 1);
                else
                    Emit(RVInstruction.R(RVInstrKind.Sltu, ToRegister(dst), RVRegister.X0, ToRegister(dst)));
            }

            private void EmitLessThan(MachineRegister dst, MachineRegister left, MachineRegister right, bool signed)
                => Emit(RVInstruction.R(signed ? RVInstrKind.Slt : RVInstrKind.Sltu, ToRegister(dst), ToRegister(left), ToRegister(right)));

            private void RequireM(LirInstruction instruction)
            {
                if (!_owner._machineTarget.HasM)
                    throw Unsupported(instruction, "Integer multiply/divide/remainder requires M extension.");
            }

            private void EmitConvert(LirInstruction instruction)
            {
                if (instruction.Result is null)
                    return;
                if (instruction.Operands.Length == 0)
                    throw Unsupported(instruction, "Conversion instruction has no source operand.");
                if (IsFloatType(instruction.Result.Type) || IsFloatType(instruction.Operands[0].Type))
                {
                    EmitFloatingOrMixedConvert(instruction);
                    return;
                }

                if (TryEmitSoftwareIntegerConvert(instruction))
                    return;

                if (RequiresSoftwareScalar(instruction.Result.Type) || RequiresSoftwareScalar(instruction.Operands[0].Type))
                    throw HelperRequired(instruction, SelectConversionHelper(instruction.Operands[0].Type, instruction.Result.Type), "Scalar conversion wider than one machine register is not implemented yet.");

                var dst = GetWritableRegister(instruction.Result, GpScratch0);
                var src = LoadOperand(instruction.Operands[0], GpScratch1);
                MoveRegister(dst, src);
                NormalizeIntegerRegister(dst, instruction.Result.Type);
                StoreWritableRegisterIfSpilled(instruction.Result, dst);
            }

            private void EmitFloatingOrMixedConvert(LirInstruction instruction)
            {
                var srcType = instruction.Operands[0].Type;
                var dstType = instruction.Result!.Type;

                if (IsLongDouble(srcType) || IsLongDouble(dstType))
                    throw HelperRequired(instruction, SelectConversionHelper(srcType, dstType), "long double conversion requires a runtime helper.");

                if (IsFloatType(srcType) && IsFloatType(dstType))
                {
                    var dst = GetWritableRegister(instruction.Result, FpScratch0);
                    var src = LoadOperand(instruction.Operands[0], FpScratch1);
                    if (SameBuiltinFloatingType(srcType, dstType))
                        MoveRegister(dst, src);
                    else
                        EmitFloatingPrecisionConversion(dst, src, srcType, dstType, instruction);
                    StoreWritableRegisterIfSpilled(instruction.Result, dst);
                    return;
                }

                if ((IsIntegerLike(srcType) || IsPointerLike(srcType)) && IsFloatType(dstType))
                {
                    if (RequiresSoftwareScalar(srcType))
                        throw HelperRequired(instruction, SelectConversionHelper(srcType, dstType), "Integer-to-floating conversion from scalar wider than one machine register is not implemented yet.");
                    var dst = GetWritableRegister(instruction.Result, FpScratch0);
                    var src = LoadOperand(instruction.Operands[0], GpScratch1);
                    EmitIntegerToFloat(dst, src, srcType, dstType, instruction);
                    StoreWritableRegisterIfSpilled(instruction.Result, dst);
                    return;
                }

                if (IsFloatType(srcType) && (IsIntegerLike(dstType) || IsPointerLike(dstType)))
                {
                    if (RequiresSoftwareScalar(dstType))
                        throw HelperRequired(instruction, SelectConversionHelper(srcType, dstType), "Floating-to-integer conversion to scalar wider than one machine register is not implemented yet.");
                    var dst = GetWritableRegister(instruction.Result, GpScratch0);
                    var src = LoadOperand(instruction.Operands[0], FpScratch1);
                    EmitFloatToInteger(dst, src, srcType, dstType, instruction);
                    NormalizeIntegerRegister(dst, dstType);
                    StoreWritableRegisterIfSpilled(instruction.Result, dst);
                    return;
                }

                throw Unsupported(instruction, "Unsupported mixed floating-point conversion.");
            }

            private void EmitFloatingPrecisionConversion(MachineRegister destination, MachineRegister source, QualifiedType sourceType, QualifiedType destinationType, LirInstruction instruction)
            {
                RequireFloatingHardware(destinationType, instruction);
                RequireFloatingHardware(sourceType, instruction);
                if (IsFloat32(sourceType) && IsFloat64(destinationType))
                {
                    EmitFloatingConvert(RVInstrKind.FcvtDS, destination, source);
                    return;
                }
                if (IsFloat64(sourceType) && IsFloat32(destinationType))
                {
                    EmitFloatingConvert(RVInstrKind.FcvtSD, destination, source);
                    return;
                }
                throw Unsupported(instruction, "Unsupported floating-point precision conversion.");
            }

            private void EmitIntegerToFloat(MachineRegister destination, MachineRegister source, QualifiedType sourceType, QualifiedType destinationType, LirInstruction instruction)
            {
                RequireFloatingHardware(destinationType, instruction);
                var unsigned = IsUnsignedIntegerType(sourceType) || IsPointerLike(sourceType);
                var sourceSize = IsPointerLike(sourceType) ? _owner._target.PointerSize : SizeOf(sourceType);
                RVInstrKind opcode;
                if (IsFloat32(destinationType))
                    opcode = sourceSize <= 4 ? (unsigned ? RVInstrKind.FcvtSWu : RVInstrKind.FcvtSW) : (unsigned ? RVInstrKind.FcvtSLu : RVInstrKind.FcvtSL);
                else if (IsFloat64(destinationType))
                    opcode = sourceSize <= 4 ? (unsigned ? RVInstrKind.FcvtDWu : RVInstrKind.FcvtDW) : (unsigned ? RVInstrKind.FcvtDLu : RVInstrKind.FcvtDL);
                else
                    throw HelperRequired(instruction, SelectConversionHelper(sourceType, destinationType), "long double conversion requires a runtime helper.");

                if (sourceSize > _owner._target.RegisterSize)
                    throw HelperRequired(instruction, SelectConversionHelper(sourceType, destinationType), "Integer-to-floating conversion from scalar wider than one machine register is not implemented yet.");
                EmitFloatingConvertFromInteger(opcode, destination, source);
            }

            private void EmitFloatToInteger(MachineRegister destination, MachineRegister source, QualifiedType sourceType, QualifiedType destinationType, LirInstruction instruction)
            {
                RequireFloatingHardware(sourceType, instruction);
                var unsigned = IsUnsignedIntegerType(destinationType) || IsPointerLike(destinationType);
                var destinationSize = IsPointerLike(destinationType) ? _owner._target.PointerSize : SizeOf(destinationType);
                RVInstrKind opcode;
                if (IsFloat32(sourceType))
                    opcode = destinationSize <= 4 ? (unsigned ? RVInstrKind.FcvtWuS : RVInstrKind.FcvtWS) : (unsigned ? RVInstrKind.FcvtLuS : RVInstrKind.FcvtLS);
                else if (IsFloat64(sourceType))
                    opcode = destinationSize <= 4 ? (unsigned ? RVInstrKind.FcvtWuD : RVInstrKind.FcvtWD) : (unsigned ? RVInstrKind.FcvtLuD : RVInstrKind.FcvtLD);
                else
                    throw HelperRequired(instruction, SelectConversionHelper(sourceType, destinationType), "long double conversion requires a runtime helper.");

                if (destinationSize > _owner._target.RegisterSize)
                    throw HelperRequired(instruction, SelectConversionHelper(sourceType, destinationType), "Floating-to-integer conversion to scalar wider than one machine register is not implemented yet.");
                EmitFloatingConvertToInteger(opcode, destination, source);
            }

            private void EmitAddressOf(LirInstruction instruction)
            {
                if (instruction.Result is null || instruction.Address is null)
                    throw Unsupported(instruction, "Invalid addressof instruction.");

                var dst = GetWritableRegister(instruction.Result, GpScratch0);
                MaterializeAddress(instruction.Address, dst);
                StoreWritableRegisterIfSpilled(instruction.Result, dst);
            }

            private void EmitLoad(LirInstruction instruction)
            {
                if (instruction.Result is null || instruction.Address is null)
                    throw Unsupported(instruction, "Invalid load instruction.");

                if (IsAggregateType(instruction.Result.Type))
                {
                    var destinationAddress = MaterializeVirtualRegisterStorageAddress(instruction.Result, GpScratch0);
                    MaterializeAddress(instruction.Address, GpScratch1);
                    CopyMemory(destinationAddress, GpScratch1, SizeOf(instruction.Result.Type));
                    return;
                }

                if (RequiresStackBackedScalar(instruction.Result.Type))
                {
                    var destinationAddress = MaterializeVirtualRegisterStorageAddress(instruction.Result, GpScratch0);
                    var sourceAddress = BuildAddress(instruction.Address, GpScratch1, GpScratch2);
                    if (sourceAddress.Offset != 0)
                    {
                        AddImmediate(GpScratch1, sourceAddress.BaseRegister, sourceAddress.Offset);
                        CopyMemory(destinationAddress, GpScratch1, SizeOf(instruction.Result.Type));
                    }
                    else
                    {
                        CopyMemory(destinationAddress, sourceAddress.BaseRegister, SizeOf(instruction.Result.Type));
                    }
                    return;
                }

                var dst = GetWritableRegister(instruction.Result, UsesHardwareFloating(instruction.Result.Type) ? FpScratch0 : GpScratch0);
                var address = BuildAddress(instruction.Address, GpScratch1, GpScratch2);
                LoadFromMemory(dst, address.BaseRegister, address.Offset, SizeOfRegisterType(instruction.Result.Type), IsSignedIntegerType(instruction.Result.Type));
                NormalizeScalarRegister(dst, instruction.Result.Type);
                StoreWritableRegisterIfSpilled(instruction.Result, dst);
            }

            private void EmitStore(LirInstruction instruction)
            {
                if (instruction.Address is null)
                    throw Unsupported(instruction, "Invalid store instruction.");
                if (instruction.Operands.Length == 0)
                    throw Unsupported(instruction, "Store instruction has no source operand.");

                var storeType = instruction.Address.ElementType;
                if (IsAggregateType(storeType))
                {
                    MaterializeAddress(instruction.Address, GpScratch0);
                    MaterializeOperandStorageAddress(instruction.Operands[0], GpScratch1, instruction);
                    CopyMemory(GpScratch0, GpScratch1, SizeOf(storeType));
                    return;
                }

                if (RequiresStackBackedScalar(storeType))
                {
                    var destinationAddress = BuildAddress(instruction.Address, GpScratch0, GpScratch2);
                    var sourceAddress = MaterializeScalarStorageAddress(instruction.Operands[0], GpScratch1, instruction);
                    if (destinationAddress.Offset != 0)
                    {
                        AddImmediate(GpScratch0, destinationAddress.BaseRegister, destinationAddress.Offset);
                        CopyMemory(GpScratch0, sourceAddress, SizeOf(storeType));
                    }
                    else
                    {
                        CopyMemory(destinationAddress.BaseRegister, sourceAddress, SizeOf(storeType));
                    }
                    return;
                }

                var src = LoadOperandAs(instruction.Operands[0], storeType, UsesHardwareFloating(storeType) ? FpScratch0 : GpScratch0, instruction);
                var address = BuildAddress(instruction.Address, GpScratch1, GpScratch2);
                StoreToMemory(src, address.BaseRegister, address.Offset, Math.Min(SizeOfRegisterType(storeType), SizeOf(storeType)));
            }

            private void EmitZeroMemory(LirInstruction instruction)
            {
                if (instruction.Address is null)
                    throw Unsupported(instruction, "Invalid zeromem instruction.");
                var size = instruction.Operands.Length == 0 ? SizeOf(instruction.Address.ElementType) : ImmediateToInt32(instruction.Operands[0]);
                MaterializeAddress(instruction.Address, GpScratch0);
                ZeroMemory(GpScratch0, size);
            }

            private void EmitCall(LirInstruction instruction)
            {
                if (instruction.Operands.Length == 0)
                    throw Unsupported(instruction, "Call has no callee operand.");

                MarshalCallArguments(instruction, 1);
                PrepareVariadicCall(instruction);
                var callee = instruction.Operands[0];
                if (TryResolveDirectCallLabel(callee, out var label))
                {
                    EmitCall(label);
                }
                else
                {
                    var target = LoadOperand(callee, GpScratch0);
                    Emit(RVInstruction.I(RVInstrKind.Jalr, RVRegister.X1, ToRegister(target), 0));
                }

                EmitCallResult(instruction);
            }

            private void MarshalCallArguments(LirInstruction instruction, int startOperand)
            {
                var cursor = new AbiCursor();
                MarshalHiddenReturnBufferArgument(instruction, ref cursor);
                for (var i = startOperand; i < instruction.Operands.Length; i++)
                    MarshalCallArgument(instruction, instruction.Operands[i], ref cursor, i - startOperand);
            }

            private void PrepareVariadicCall(LirInstruction instruction)
            {
            }

            private void StoreVariadicHomeValue(LirInstruction instruction, LirOperand operand, int offset, int homeSlotSize)
            {
                if (IsAggregateType(operand.Type))
                {
                    var size = SizeOf(operand.Type);
                    if (size > homeSlotSize)
                        throw Unsupported(instruction, "Aggregate variadic argument does not fit the configured va_list home slot.");
                    MaterializeOperandStorageAddress(operand, GpScratch0, instruction);
                    AddImmediate(GpScratch1, Sp, offset);
                    CopyMemory(GpScratch1, GpScratch0, size);
                    return;
                }

                if (RequiresStackBackedScalar(operand.Type))
                {
                    var size = SizeOf(operand.Type);
                    if (size > homeSlotSize)
                        throw Unsupported(instruction, "Variadic scalar argument does not fit the configured va_list home slot.");
                    var sourceAddress = MaterializeScalarStorageAddress(operand, GpScratch0, instruction);
                    AddImmediate(GpScratch1, Sp, offset);
                    CopyMemory(GpScratch1, sourceAddress, size);
                    return;
                }

                {
                    var source = LoadOperand(operand, UsesHardwareFloating(operand.Type) ? FpScratch0 : GpScratch0);
                    var size = Math.Min(SizeOfRegisterType(operand.Type), SizeOf(operand.Type));
                    if (size > homeSlotSize)
                        throw Unsupported(instruction, "Variadic scalar argument does not fit the configured va_list home slot.");
                    StoreToMemory(source, Sp, offset, size);
                }
            }

            private void MarshalHiddenReturnBufferArgument(LirInstruction instruction, ref AbiCursor cursor)
            {
                if (instruction.Result is null || !CAbi.RequiresHiddenReturnBuffer(_owner._target, instruction.Result.Type))
                    return;

                var location = CAbi.AssignHiddenReturnBufferLocation(_owner._target, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                var address = MaterializeVirtualRegisterStorageAddress(instruction.Result, GpScratch0);
                StoreArgumentValue(address, location, _owner._target.PointerSize);
            }

            private void MarshalCallArgument(LirInstruction instruction, LirOperand operand, ref AbiCursor cursor, int sourceArgumentIndex)
            {
                var isVariadicUnnamed = instruction.CallSignature is not null && instruction.CallSignature.IsVariadic && sourceArgumentIndex >= instruction.CallSignature.Parameters.Length;
                var value = CAbi.ClassifyValue(_owner._target, operand.Type, isReturn: false, isVariadicUnnamed);
                if (value.PassingKind == AbiPassingKind.Indirect)
                {
                    MaterializeOperandStorageAddress(operand, GpScratch0, instruction);
                    var loc = CAbi.AssignArgumentLocation(value, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                    StoreArgumentValue(GpScratch0, loc, _owner._target.PointerSize);
                    return;
                }

                if (value.PassingKind == AbiPassingKind.MultiRegister)
                {
                    MachineRegister storageBase;
                    if (IsAggregateType(operand.Type))
                    {
                        MaterializeOperandStorageAddress(operand, GpScratch0, instruction);
                        storageBase = GpScratch0;
                    }
                    else
                    {
                        storageBase = MaterializeScalarBitsAddress(operand, GpScratch0, instruction);
                    }
                    for (var i = 0; i < value.Segments.Length; i++)
                    {
                        var segment = value.Segments[i];
                        var loc = CAbi.AssignSegmentArgumentLocation(segment, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                        LoadRawBitsFromMemory(GpScratch1, storageBase, segment.Offset, segment.Size);
                        StoreArgumentValue(GpScratch1, loc, segment.Size);
                    }
                    return;
                }

                if (value.PassingKind == AbiPassingKind.Stack && IsAggregateType(operand.Type))
                {
                    var loc = CAbi.AssignArgumentLocation(value, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                    MaterializeOperandStorageAddress(operand, GpScratch0, instruction);
                    AddImmediate(GpScratch1, Sp, _allocation.Frame.OutgoingArgumentAreaOffset + loc.StackByteOffset(_owner._allocationOptions.StackArgumentSlotSize));
                    CopyMemory(GpScratch1, GpScratch0, value.Size);
                    return;
                }

                var scalarLoc = CAbi.AssignArgumentLocation(value, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                var segmentClass = value.Segments.Length != 0 ? value.Segments[0].RegisterClass : AbiRegisterClass.General;
                var source = LoadOperandForArgument(operand, segmentClass, scalarLoc, instruction);
                StoreArgumentValue(source, scalarLoc, Math.Min(SizeOfRegisterType(operand.Type), Math.Max(1, value.Size)));
            }

            private MachineRegister MaterializeScalarBitsAddress(LirOperand operand, MachineRegister destination, LirInstruction instruction)
            {
                if (RequiresStackBackedScalar(operand.Type))
                    return MaterializeScalarStorageAddress(operand, destination, instruction);

                var offset = _allocation.Frame.FloatingImmediateTempOffset;
                var source = LoadOperand(operand, UsesHardwareFloating(operand.Type) ? FpScratch0 : GpScratch0);
                StoreToMemory(source, Sp, offset, Math.Min(SizeOfRegisterType(operand.Type), SizeOf(operand.Type)));
                AddImmediate(destination, Sp, offset);
                return destination;
            }

            private MachineRegister LoadOperandForArgument(LirOperand operand, AbiRegisterClass registerClass, AbiLocation location, LirInstruction instruction)
            {
                if (!IsFloatType(operand.Type))
                    return LoadOperand(operand, GpScratch0);

                if (registerClass == AbiRegisterClass.Floating || location.Kind == AbiLocationKind.Stack)
                    return LoadOperand(operand, UsesHardwareFloating(operand.Type) ? FpScratch0 : GpScratch0);

                return LoadFloatingOperandBitsToInteger(operand, instruction);
            }

            private MachineRegister LoadFloatingOperandBitsToInteger(LirOperand operand, LirInstruction instruction)
            {
                if (!UsesHardwareFloating(operand.Type))
                    return LoadOperand(operand, GpScratch0);

                var source = LoadOperand(operand, FpScratch0);
                if (IsFloat32(operand.Type))
                {
                    EmitFloatingMoveToInteger(RVInstrKind.FmvXW, GpScratch0, source);
                    return GpScratch0;
                }

                if (IsFloat64(operand.Type) && _owner._target.Is64Bit)
                {
                    EmitFloatingMoveToInteger(RVInstrKind.FmvXD, GpScratch0, source);
                    return GpScratch0;
                }

                throw HelperRequired(instruction, SelectScalarMoveHelper(operand.Type), 
                    "Floating-point argument bit move to an integer ABI register requires a runtime helper.");
            }

            private void StoreArgumentValue(MachineRegister source, AbiLocation location, int size)
            {
                if (location.Kind == AbiLocationKind.Register)
                {
                    MoveRegister(location.Register, source);
                    return;
                }

                if (location.Kind == AbiLocationKind.Stack)
                {
                    StoreToMemory(source, Sp, _allocation.Frame.OutgoingArgumentAreaOffset + location.StackByteOffset(_owner._allocationOptions.StackArgumentSlotSize), size);
                    return;
                }

                throw new InvalidOperationException("Invalid argument ABI location.");
            }

            private void EmitCallResult(LirInstruction instruction)
            {
                if (instruction.Result is null)
                    return;

                var value = CAbi.ClassifyValue(_owner._target, instruction.Result.Type, isReturn: true, isVariadicUnnamedArgument: false);
                if (value.PassingKind == AbiPassingKind.Indirect)
                    return;

                if (IsAggregateType(instruction.Result.Type))
                {
                    if (value.PassingKind == AbiPassingKind.Indirect)
                        return;

                    var destinationAddress = MaterializeVirtualRegisterStorageAddress(instruction.Result, GpScratch0);
                    for (var i = 0; i < value.Segments.Length; i++)
                    {
                        var segment = value.Segments[i];
                        StoreRawBitsToAddress(CAbi.ReturnRegister(segment, i), destinationAddress, segment.Offset, segment.Size);
                    }
                    return;
                }

                if (value.PassingKind == AbiPassingKind.MultiRegister)
                {
                    var destinationAddress = MaterializeVirtualRegisterStorageAddress(instruction.Result, GpScratch0);
                    for (var i = 0; i < value.Segments.Length; i++)
                    {
                        var segment = value.Segments[i];
                        StoreRawBitsToAddress(CAbi.ReturnRegister(segment, i), destinationAddress, segment.Offset, segment.Size);
                    }
                    return;
                }

                var dst = GetWritableRegister(instruction.Result, value.Segments.Length != 0 && value.Segments[0].RegisterClass == AbiRegisterClass.Floating ? FpScratch0 : GpScratch0);
                MoveRegister(dst, value.Segments.Length != 0 && value.Segments[0].RegisterClass == AbiRegisterClass.Floating ? MachineRegister.F10 : MachineRegister.X10);
                NormalizeScalarRegister(dst, instruction.Result.Type);
                StoreWritableRegisterIfSpilled(instruction.Result, dst);
            }

            private bool TryResolveDirectCallLabel(LirOperand callee, out string label)
            {
                label = string.Empty;
                if (callee.Kind != LirOperandKind.Symbol || callee.Symbol is not FunctionSymbol function)
                    return false;

                if (_owner._functionLabels.TryGetValue(function, out label!))
                    return true;
                if (_owner._functionLabelsByName.TryGetValue(function.Name, out label!))
                    return true;

                label = _owner.CreateExternalLabel(function.Name);
                _owner._symbols.Add(new RVObjectSymbol(label, string.Empty, 0, 0, RVObjectSymbolBinding.External, RVObjectSymbolKind.Function));
                return true;
            }

            private void EmitVaStart(LirInstruction instruction)
            {
                if (instruction.Operands.Length == 1)
                {
                    var ap = LoadOperand(instruction.Operands[0], GpScratch0);
                    if (_allocation.Frame.HasVarArgsPointer)
                        LoadFromMemory(GpScratch1, Sp, _allocation.Frame.VarArgsPointerOffset, _owner._target.PointerSize, signed: false);
                    else
                        MoveRegister(GpScratch1, MachineRegister.X0);
                    StoreRegister(GpScratch1, ap, 0, _owner._target.PointerSize);
                    return;
                }

                if (instruction.Result is null)
                    return;
                var destination = GetWritableRegister(instruction.Result, GpScratch0);
                if (_allocation.Frame.HasVarArgsPointer)
                    LoadFromMemory(destination, Sp, _allocation.Frame.VarArgsPointerOffset, _owner._target.PointerSize, signed: false);
                else
                    MoveRegister(destination, MachineRegister.X0);
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
            }

            private void EmitVaArg(LirInstruction instruction)
            {
                if (instruction.Operands.Length != 4)
                    throw Unsupported(instruction, "VaArg expects a va_list pointer, kind, size, and alignment.");
                if (instruction.Result is null)
                    return;

                var size = Math.Max(1, ImmediateToInt32(instruction.Operands[2]));
                var align = Math.Max(1, ImmediateToInt32(instruction.Operands[3]));
                var ap = LoadOperand(instruction.Operands[0], GpScratch0);
                LoadFromMemory(GpScratch1, ap, 0, _owner._target.PointerSize, signed: false);
                AlignPointerRegister(GpScratch1, align);
                var destination = GetWritableRegister(instruction.Result, GpScratch0);
                MoveRegister(destination, GpScratch1);
                AddImmediate(GpScratch1, GpScratch1, AlignUp(size, _owner._target.PointerSize));
                StoreRegister(GpScratch1, ap, 0, _owner._target.PointerSize);
                StoreWritableRegisterIfSpilled(instruction.Result, destination);
            }

            private void AlignPointerRegister(MachineRegister register, int alignment)
            {
                if (alignment <= 1)
                    return;
                AddImmediate(register, register, alignment - 1);
                LoadImmediate(GpScratch2, -alignment);
                Emit(RVInstruction.R(RVInstrKind.And, ToRegister(register), ToRegister(register), ToRegister(GpScratch2)));
            }

            private void EmitBranch(LirInstruction instruction)
            {
                if (instruction.Operands.Length != 1)
                    throw Unsupported(instruction, "Branch expects one condition operand.");
                var trueFallsThrough = IsFallthroughTarget(instruction.TrueTarget);
                var falseFallsThrough = IsFallthroughTarget(instruction.FalseTarget);
                MachineRegister cond;
                if (IsFloatType(instruction.Operands[0].Type))
                {
                    if (IsLongDouble(instruction.Operands[0].Type))
                        throw HelperRequired(instruction, SelectFloatingHelper("!", instruction.Operands[0].Type), "long double branch condition requires a runtime helper.");
                    RequireFloatingHardware(instruction.Operands[0].Type, instruction);
                    var fcond = LoadOperand(instruction.Operands[0], FpScratch0);
                    LoadFloatingImmediate(FpScratch1, 0.0, instruction.Operands[0].Type);
                    EmitFloatingCompare(IsFloat32(instruction.Operands[0].Type) ? RVInstrKind.FeqS : RVInstrKind.FeqD, GpScratch0, fcond, FpScratch1);
                    EmitImm(RVInstrKind.Xori, GpScratch0, GpScratch0, 1);
                    cond = GpScratch0;
                }
                else if (RequiresStackBackedScalar(instruction.Operands[0].Type))
                {
                    cond = EmitStackBackedScalarNonZero(instruction.Operands[0], instruction);
                }
                else
                {
                    cond = LoadOperand(instruction.Operands[0], GpScratch0);
                }

                if (trueFallsThrough && !falseFallsThrough)
                {
                    EmitBranch(RVInstrKind.Beq, cond, MachineRegister.X0, LabelOf(instruction.FalseTarget));
                    return;
                }

                EmitBranch(RVInstrKind.Bne, cond, MachineRegister.X0, LabelOf(instruction.TrueTarget));
                if (!falseFallsThrough)
                    EmitJump(LabelOf(instruction.FalseTarget));
            }

            private void EmitStackBackedScalarIsZero(LirVirtualRegister destinationRegister, LirOperand operand, LirInstruction instruction)
            {
                var destination = GetWritableRegister(destinationRegister, GpScratch0);
                var nonZero = EmitStackBackedScalarNonZero(operand, instruction);
                EmitImm(RVInstrKind.Sltiu, destination, nonZero, 1);
                StoreWritableRegisterIfSpilled(destinationRegister, destination);
            }

            private MachineRegister EmitStackBackedScalarNonZero(LirOperand operand, LirInstruction instruction)
            {
                var sourceAddress = MaterializeScalarStorageAddress(operand, GpScratch1, instruction);
                MoveRegister(GpScratch0, MachineRegister.X0);
                var size = SizeOf(operand.Type);
                var registerSize = Math.Max(1, _owner._target.RegisterSize);
                for (var offset = 0; offset < size; offset += registerSize)
                {
                    var segmentSize = Math.Min(registerSize, size - offset);
                    LoadRawBitsFromMemory(GpScratch2, sourceAddress, offset, segmentSize);
                    Emit(RVInstruction.R(RVInstrKind.Or, ToRegister(GpScratch0), ToRegister(GpScratch0), ToRegister(GpScratch2)));
                }
                return GpScratch0;
            }

            private void EmitSwitch(LirInstruction instruction)
            {
                if (instruction.Operands.Length != 1)
                    throw Unsupported(instruction, "Switch expects one key operand.");
                if (RequiresStackBackedScalar(instruction.Operands[0].Type))
                    throw HelperRequired(instruction, SelectScalarMoveHelper(instruction.Operands[0].Type), "Switch on scalar wider than one machine register is not implemented yet.");

                var key = LoadOperand(instruction.Operands[0], GpScratch0);
                foreach (var @case in instruction.SwitchCases)
                {
                    LoadImmediate(GpScratch1, ImmediateToInt64(@case.Value));
                    EmitBranch(RVInstrKind.Beq, key, GpScratch1, LabelOf(@case.Target));
                }

                if (!IsFallthroughTarget(instruction.Target))
                    EmitJump(LabelOf(instruction.Target));
            }

            private void EmitReturn(LirInstruction instruction)
            {
                if (instruction.Operands.Length == 0 || IsVoid(instruction.Operands[0].Type))
                {
                    EmitEpilogue();
                    EmitReturnInstruction();
                    return;
                }

                var operand = instruction.Operands[0];
                if (IsAggregateType(operand.Type))
                {
                    var value = CAbi.ClassifyValue(_owner._target, operand.Type, isReturn: true, isVariadicUnnamedArgument: false);
                    MaterializeOperandStorageAddress(operand, GpScratch0, instruction);
                    if (value.PassingKind == AbiPassingKind.Indirect)
                    {
                        MaterializeIncomingHiddenReturnBufferAddress(GpScratch1);
                        CopyMemory(GpScratch1, GpScratch0, value.Size);
                        EmitEpilogue();
                        EmitReturnInstruction();
                        return;
                    }

                    for (var i = 0; i < value.Segments.Length; i++)
                    {
                        var segment = value.Segments[i];
                        LoadRawBitsFromMemory(CAbi.ReturnRegister(segment, i), GpScratch0, segment.Offset, segment.Size);
                    }

                    EmitEpilogue();
                    EmitReturnInstruction();
                    return;
                }

                var returnType = _function.Symbol?.FunctionType?.ReturnType ?? operand.Type;
                var returnAbi = CAbi.ClassifyValue(_owner._target, returnType, isReturn: true, isVariadicUnnamedArgument: false);
                if (returnAbi.PassingKind == AbiPassingKind.Indirect)
                {
                    var sourceAddress = MaterializeScalarStorageAddress(operand, GpScratch0, instruction);
                    MaterializeIncomingHiddenReturnBufferAddress(GpScratch1);
                    CopyMemory(GpScratch1, sourceAddress, returnAbi.Size);
                    EmitEpilogue();
                    EmitReturnInstruction();
                    return;
                }

                if (returnAbi.PassingKind == AbiPassingKind.MultiRegister)
                {
                    var sourceAddress = MaterializeScalarStorageAddress(operand, GpScratch0, instruction);
                    for (var i = 0; i < returnAbi.Segments.Length; i++)
                    {
                        var segment = returnAbi.Segments[i];
                        LoadRawBitsFromMemory(CAbi.ReturnRegister(segment, i), sourceAddress, segment.Offset, segment.Size);
                    }
                    EmitEpilogue();
                    EmitReturnInstruction();
                    return;
                }
                LoadOperandIntoAs(operand, returnAbi.Segments.Length != 0 && returnAbi.Segments[0].RegisterClass == AbiRegisterClass.Floating ? MachineRegister.F10 : MachineRegister.X10, returnType, instruction);
                EmitEpilogue();
                EmitReturnInstruction();
            }

            private void EmitEpilogue()
            {
                if (_hasCalls)
                    LoadFromMemory(Ra, Sp, _raSaveOffset, _owner._target.PointerSize, signed: false);
                foreach (var pair in _allocation.Frame.SavedRegisterOffsets.OrderByDescending(static p => p.Value))
                    LoadFromMemory(pair.Key, Sp, pair.Value, RegisterSaveSize(pair.Key), signed: false);
                AdjustStack(_totalFrameSize);
            }

            private void EmitParallelCopy(LirInstruction instruction)
            {
                if (instruction.ParallelCopies.Length == 0)
                    return;

                var copies = instruction.ParallelCopies.Where(RequiresPhysicalParallelCopy).ToArray();
                if (copies.Length == 0)
                    return;

                if (copies.Length == 1)
                {
                    EmitDirectParallelCopy(copies[0], instruction);
                    return;
                }

                if (CanEmitDirectParallelCopies(copies))
                {
                    foreach (var copy in copies)
                        EmitDirectParallelCopy(copy, instruction);
                    return;
                }

                if (_allocation.Frame.ParallelCopyTempSize == 0)
                    throw Unsupported(instruction, "Parallel copy requires a temporary frame area.");

                var tempOffset = _allocation.Frame.ParallelCopyTempOffset;
                var tempCursor = 0;
                foreach (var copy in copies)
                {
                    if (RequiresBlockCopyStorage(copy.Destination.Type))
                    {
                        var sourceAddress = IsAggregateType(copy.Destination.Type)
                            ? MaterializeAnyStorageAddress(copy.Source, GpScratch0, instruction)
                            : MaterializeScalarStorageAddress(copy.Source, GpScratch0, instruction);
                        AddImmediate(GpScratch1, Sp, tempOffset + tempCursor);
                        CopyMemory(GpScratch1, sourceAddress, SizeOf(copy.Destination.Type));
                        tempCursor += AlignUp(SizeOf(copy.Destination.Type), _owner._allocationOptions.SpillSlotAlignment);
                    }
                    else
                    {
                        var source = LoadOperand(copy.Source, GpScratch0);
                        StoreToMemory(source, Sp, tempOffset + tempCursor, SizeOfRegisterType(copy.Destination.Type));
                        tempCursor += AlignUp(SizeOfRegisterType(copy.Destination.Type), _owner._allocationOptions.SpillSlotAlignment);
                    }
                }

                tempCursor = 0;
                foreach (var copy in copies)
                {
                    if (RequiresBlockCopyStorage(copy.Destination.Type))
                    {
                        var dest = MaterializeVirtualRegisterStorageAddress(copy.Destination, GpScratch0);
                        AddImmediate(GpScratch1, Sp, tempOffset + tempCursor);
                        CopyMemory(dest, GpScratch1, SizeOf(copy.Destination.Type));
                        tempCursor += AlignUp(SizeOf(copy.Destination.Type), _owner._allocationOptions.SpillSlotAlignment);
                    }
                    else
                    {
                        var destination = GetWritableRegister(copy.Destination, UsesHardwareFloating(copy.Destination.Type) ? FpScratch0 : GpScratch0);
                        LoadFromMemory(destination, Sp, tempOffset + tempCursor, SizeOfRegisterType(copy.Destination.Type), IsSignedIntegerType(copy.Destination.Type));
                        NormalizeScalarRegister(destination, copy.Destination.Type);
                        StoreWritableRegisterIfSpilled(copy.Destination, destination);
                        tempCursor += AlignUp(SizeOfRegisterType(copy.Destination.Type), _owner._allocationOptions.SpillSlotAlignment);
                    }
                }
            }

            private bool RequiresPhysicalParallelCopy(LirParallelCopy copy)
            {
                if (copy.Destination.RegisterClass is LirRegisterClass.Void or LirRegisterClass.Memory)
                    return false;
                if (copy.Source.Kind is LirOperandKind.Void or LirOperandKind.None)
                    return false;
                if (copy.Source.Kind == LirOperandKind.Register && copy.Source.Register is { RegisterClass: LirRegisterClass.Void or LirRegisterClass.Memory })
                    return false;
                return !ReferencesSamePhysicalStorage(copy.Source, copy.Destination);
            }

            private bool CanEmitDirectParallelCopies(IReadOnlyList<LirParallelCopy> copies)
            {
                foreach (var copy in copies)
                    if (RequiresBlockCopyStorage(copy.Destination.Type))
                        return false;
                return !HasPhysicalStorageClobber(copies);
            }

            private bool HasPhysicalStorageClobber(IReadOnlyList<LirParallelCopy> copies)
            {
                for (var i = 0; i < copies.Count; i++)
                {
                    var destination = copies[i].Destination;
                    var hasDestinationRegister = TryGetPhysicalRegister(destination, out var destinationRegister);
                    var hasDestinationStackOffset = TryGetStackOffset(destination, out var destinationStackOffset);
                    if (!hasDestinationRegister && !hasDestinationStackOffset)
                        continue;

                    for (var j = 0; j < copies.Count; j++)
                    {
                        if (i == j && ReferencesSamePhysicalStorage(copies[j].Source, destination))
                            continue;

                        if (hasDestinationRegister && TryGetPhysicalRegister(copies[j].Source, out var sourceRegister) && sourceRegister == destinationRegister)
                            return true;
                        if (hasDestinationStackOffset && TryGetStackOffset(copies[j].Source, out var sourceStackOffset) && sourceStackOffset == destinationStackOffset)
                            return true;
                    }
                }

                return false;
            }

            private void EmitDirectParallelCopy(LirParallelCopy copy, LirInstruction instruction)
            {
                if (!RequiresPhysicalParallelCopy(copy))
                    return;
                if (IsAggregateType(copy.Destination.Type))
                {
                    EmitAggregateCopyToRegisterStorage(copy.Destination, copy.Source, instruction);
                    return;
                }

                if (RequiresStackBackedScalar(copy.Destination.Type))
                {
                    EmitStackBackedScalarCopy(copy.Destination, copy.Source, instruction);
                    return;
                }

                var destination = GetWritableRegister(copy.Destination, UsesHardwareFloating(copy.Destination.Type) ? FpScratch0 : GpScratch0);
                LoadOperandIntoAs(copy.Source, destination, copy.Destination.Type, instruction);
                StoreWritableRegisterIfSpilled(copy.Destination, destination);
            }

            private bool TryGetPhysicalRegister(LirVirtualRegister register, out MachineRegister physicalRegister)
            {
                physicalRegister = default;
                if (!_allocation.TryGetAllocation(register, out var allocation) || allocation.IsSpilled)
                    return false;
                physicalRegister = allocation.PhysicalRegister;
                return true;
            }

            private bool TryGetPhysicalRegister(LirOperand operand, out MachineRegister physicalRegister)
            {
                physicalRegister = default;
                if (operand.Kind != LirOperandKind.Register || operand.Register is null)
                    return false;
                return TryGetPhysicalRegister(operand.Register, out physicalRegister);
            }

            private bool TryGetStackOffset(LirVirtualRegister register, out int stackOffset)
            {
                stackOffset = 0;
                if (!_allocation.TryGetAllocation(register, out var allocation) || !allocation.IsSpilled)
                    return false;
                stackOffset = allocation.StackOffset;
                return true;
            }

            private bool TryGetStackOffset(LirOperand operand, out int stackOffset)
            {
                stackOffset = 0;
                if (operand.Kind != LirOperandKind.Register || operand.Register is null)
                    return false;
                return TryGetStackOffset(operand.Register, out stackOffset);
            }

            private bool ReferencesSamePhysicalStorage(LirOperand source, LirVirtualRegister destination)
            {
                if (source.Kind != LirOperandKind.Register || source.Register is null)
                    return false;
                if (!_allocation.TryGetAllocation(source.Register, out var sourceAllocation) || !_allocation.TryGetAllocation(destination, out var destinationAllocation))
                    return false;
                if (!sourceAllocation.IsSpilled && !destinationAllocation.IsSpilled)
                    return sourceAllocation.PhysicalRegister == destinationAllocation.PhysicalRegister;
                if (sourceAllocation.IsSpilled && destinationAllocation.IsSpilled)
                    return sourceAllocation.StackOffset == destinationAllocation.StackOffset;
                return false;
            }

            private MachineRegister LoadOperandAs(LirOperand operand, QualifiedType targetType, MachineRegister scratch, LirInstruction instruction)
            {
                if (IsFloatType(targetType))
                {
                    if (!UsesHardwareFloating(targetType) && !IsFloatRegister(scratch))
                    {
                        if (IsFloatType(operand.Type) && SameBuiltinFloatingType(operand.Type, targetType))
                            return LoadOperand(operand, scratch);
                        throw HelperRequired(instruction, SelectConversionHelper(operand.Type, targetType), "Software floating-point conversion requires a runtime helper.");
                    }
                    var destination = IsFloatRegister(scratch) ? scratch : FpScratch0;
                    return LoadOperandAsFloating(operand, targetType, destination, instruction);
                }

                if (IsFloatType(operand.Type))
                    throw Unsupported(instruction, "Implicit floating-point to non-floating load is not supported.");

                var source = LoadOperand(operand, scratch);
                if (!TypesNeedIntegerConversion(operand.Type, targetType))
                    return source;
                if (source != scratch)
                    MoveRegister(scratch, source);
                NormalizeIntegerRegister(scratch, targetType);
                return scratch;
            }

            private void LoadOperandIntoAs(LirOperand operand, MachineRegister destination, QualifiedType targetType, LirInstruction instruction)
            {
                var scratch = IsFloatType(targetType) && (UsesHardwareFloating(targetType) || IsFloatRegister(destination))
                    ? (destination == FpScratch0 ? FpScratch1 : FpScratch0)
                    : (destination == GpScratch0 ? GpScratch1 : GpScratch0);
                var source = LoadOperandAs(operand, targetType, scratch, instruction);
                MoveRegister(destination, source);
                NormalizeScalarRegister(destination, targetType);
            }

            private MachineRegister LoadOperand(LirOperand operand, MachineRegister preferred)
            {
                switch (operand.Kind)
                {
                    case LirOperandKind.Register:
                        if (operand.Register is null)
                            throw new InvalidOperationException("Register operand has no register.");
                        return LoadVirtualRegister(operand.Register, preferred);
                    case LirOperandKind.Immediate:
                        if (operand.Immediate is string text)
                        {
                            MaterializeSymbolAddress(_owner.CreateStringLiteral(text), preferred);
                            return preferred;
                        }
                        if (IsFloatType(operand.Type))
                        {
                            if (!UsesHardwareFloating(operand.Type) && !IsFloatRegister(preferred))
                            {
                                LoadFloatingImmediateBits(preferred, operand.Immediate, operand.Type);
                                return preferred;
                            }
                            var destination = IsFloatRegister(preferred) ? preferred : FpScratch0;
                            LoadFloatingImmediate(destination, operand.Immediate, operand.Type);
                            return destination;
                        }
                        LoadImmediate(preferred, ConvertIntegerConstant(operand.Immediate));
                        NormalizeIntegerRegister(preferred, operand.Type);
                        return preferred;
                    case LirOperandKind.StackSlot:
                        if (operand.StackSlot is null)
                            throw new InvalidOperationException("Stack-slot operand has no stack slot.");
                        if (!_allocation.Frame.StackSlotOffsets.TryGetValue(operand.StackSlot, out var offset))
                            throw new InvalidOperationException("Missing stack slot offset for " + operand.StackSlot.Name + ".");
                        if (UsesHardwareFloating(operand.Type) && !IsFloatRegister(preferred))
                            preferred = FpScratch0;
                        LoadFromMemory(preferred, Sp, offset, SizeOfRegisterType(operand.Type), IsSignedIntegerType(operand.Type));
                        NormalizeScalarRegister(preferred, operand.Type);
                        return preferred;
                    case LirOperandKind.Address:
                        if (operand.Address is null)
                            throw new InvalidOperationException("Address operand has no address.");
                        MaterializeAddress(operand.Address, preferred);
                        return preferred;
                    case LirOperandKind.Symbol:
                        if (operand.Symbol is null)
                            throw new InvalidOperationException("Symbol operand has no symbol.");
                        MaterializeSymbolAddress(_owner.GetSymbolLabel(operand.Symbol), preferred);
                        return preferred;
                    case LirOperandKind.Undefined:
                    case LirOperandKind.Void:
                    case LirOperandKind.None:
                        if (IsFloatRegister(preferred) && IsFloatType(operand.Type))
                            LoadFloatingImmediate(preferred, 0.0, operand.Type);
                        else
                            MoveRegister(preferred, MachineRegister.X0);
                        return preferred;
                    default:
                        throw new NotSupportedException("Cannot load LIR operand kind " + operand.Kind + " into a register.");
                }
            }

            private MachineRegister LoadVirtualRegister(LirVirtualRegister register, MachineRegister preferred)
            {
                if (IsAggregateType(register.Type) || RequiresStackBackedScalar(register.Type))
                    throw new NotSupportedException("Virtual register " + register.Name + " cannot be loaded as a single scalar register.");
                var alloc = _allocation[register];
                if (!alloc.IsSpilled)
                    return alloc.PhysicalRegister;
                if (UsesHardwareFloating(register.Type) && !IsFloatRegister(preferred))
                    preferred = FpScratch0;
                LoadFromMemory(preferred, Sp, alloc.StackOffset, SizeOfRegisterType(register.Type), IsSignedIntegerType(register.Type));
                NormalizeScalarRegister(preferred, register.Type);
                return preferred;
            }

            private MachineRegister GetWritableRegister(LirVirtualRegister register, MachineRegister scratch)
            {
                if (IsAggregateType(register.Type) || RequiresStackBackedScalar(register.Type))
                    throw new NotSupportedException("Virtual register " + register.Name + " must be accessed through its storage address.");
                var alloc = _allocation[register];
                if (alloc.IsSpilled && UsesHardwareFloating(register.Type) && !IsFloatRegister(scratch))
                    scratch = FpScratch0;
                return alloc.IsSpilled ? scratch : alloc.PhysicalRegister;
            }

            private void StoreWritableRegisterIfSpilled(LirVirtualRegister register, MachineRegister source)
            {
                if (IsAggregateType(register.Type) || RequiresStackBackedScalar(register.Type))
                    throw new NotSupportedException("Virtual register " + register.Name + " must be stored with a block copy.");
                var alloc = _allocation[register];
                if (!alloc.IsSpilled)
                    return;
                StoreToMemory(source, Sp, alloc.StackOffset, Math.Min(SizeOfRegisterType(register.Type), SizeOf(register.Type)));
            }

            private MachineRegister MaterializeVirtualRegisterStorageAddress(LirVirtualRegister register, MachineRegister destination)
            {
                var alloc = _allocation[register];
                if (!alloc.IsSpilled)
                    throw new NotSupportedException("Virtual register " + register.Name + " must be stack-backed.");
                AddImmediate(destination, Sp, alloc.StackOffset);
                return destination;
            }

            private void MaterializeOperandStorageAddress(LirOperand operand, MachineRegister destination, LirInstruction instruction)
            {
                switch (operand.Kind)
                {
                    case LirOperandKind.Register:
                        if (operand.Register is null)
                            throw new InvalidOperationException("Register operand has no register.");
                        MaterializeVirtualRegisterStorageAddress(operand.Register, destination);
                        return;
                    case LirOperandKind.StackSlot:
                        if (operand.StackSlot is null)
                            throw new InvalidOperationException("Stack-slot operand has no stack slot.");
                        if (!_allocation.Frame.StackSlotOffsets.TryGetValue(operand.StackSlot, out var offset))
                            throw new InvalidOperationException("Missing stack slot offset for " + operand.StackSlot.Name + ".");
                        AddImmediate(destination, Sp, offset);
                        return;
                    case LirOperandKind.Address:
                        if (operand.Address is null)
                            throw new InvalidOperationException("Address operand has no address.");
                        MaterializeAddress(operand.Address, destination);
                        return;
                    case LirOperandKind.Immediate:
                        if (operand.Immediate is string text)
                        {
                            MaterializeSymbolAddress(_owner.CreateStringLiteral(text), destination);
                            return;
                        }
                        break;
                }
                throw Unsupported(instruction, "Cannot materialize storage address for operand kind " + operand.Kind + ".");
            }

            private MachineRegister MaterializeAnyStorageAddress(LirOperand operand, MachineRegister destination, LirInstruction instruction)
            {
                MaterializeOperandStorageAddress(operand, destination, instruction);
                return destination;
            }

            private MachineRegister MaterializeScalarStorageAddress(LirOperand operand, MachineRegister destination, LirInstruction instruction)
            {
                var size = SizeOf(operand.Type);
                switch (operand.Kind)
                {
                    case LirOperandKind.Register:
                        if (operand.Register is null)
                            throw new InvalidOperationException("Register operand has no register.");
                        if (RequiresStackBackedScalar(operand.Type))
                            return MaterializeVirtualRegisterStorageAddress(operand.Register, destination);
                        break;
                    case LirOperandKind.StackSlot:
                        if (operand.StackSlot is null)
                            throw new InvalidOperationException("Stack-slot operand has no stack slot.");
                        if (!_allocation.Frame.StackSlotOffsets.TryGetValue(operand.StackSlot, out var stackOffset))
                            throw new InvalidOperationException("Missing stack slot offset for " + operand.StackSlot.Name + ".");
                        AddImmediate(destination, Sp, stackOffset);
                        return destination;
                    case LirOperandKind.Immediate:
                        if (size > _allocation.Frame.FloatingImmediateTempSize)
                            throw HelperRequired(instruction, SelectScalarMoveHelper(operand.Type), "Immediate scalar storage materialization requires a runtime helper.");
                        AddImmediate(destination, Sp, _allocation.Frame.FloatingImmediateTempOffset);
                        StoreImmediateScalarToMemory(operand, destination, 0, size, instruction);
                        return destination;
                    case LirOperandKind.Undefined:
                    case LirOperandKind.Void:
                    case LirOperandKind.None:
                        if (size > _allocation.Frame.FloatingImmediateTempSize)
                            throw HelperRequired(instruction, SelectScalarMoveHelper(operand.Type), "Undefined scalar storage materialization requires a runtime helper.");
                        AddImmediate(destination, Sp, _allocation.Frame.FloatingImmediateTempOffset);
                        ZeroMemory(destination, size);
                        return destination;
                }

                if (size > _allocation.Frame.FloatingImmediateTempSize)
                    throw HelperRequired(instruction, SelectScalarMoveHelper(operand.Type), "Scalar storage materialization requires a runtime helper.");

                var source = LoadOperand(operand, UsesHardwareFloating(operand.Type) ? FpScratch0 : GpScratch2);
                AddImmediate(destination, Sp, _allocation.Frame.FloatingImmediateTempOffset);
                StoreToMemory(source, destination, 0, Math.Min(SizeOfRegisterType(operand.Type), size));
                return destination;
            }

            private void EmitStackBackedScalarCopy(LirVirtualRegister destination, LirOperand source, LirInstruction instruction)
            {
                var destinationAddress = MaterializeVirtualRegisterStorageAddress(destination, GpScratch0);
                var size = SizeOf(destination.Type);
                if (source.Kind is LirOperandKind.Undefined or LirOperandKind.Void or LirOperandKind.None)
                {
                    ZeroMemory(destinationAddress, size);
                    return;
                }

                if (source.Kind == LirOperandKind.Immediate)
                {
                    StoreImmediateScalarToMemory(source, destinationAddress, 0, size, instruction);
                    return;
                }

                if (SizeOf(source.Type) != size)
                    throw Unsupported(instruction, "Stack-backed scalar copy requires equal source and destination sizes.");

                var sourceAddress = MaterializeScalarStorageAddress(source, GpScratch1, instruction);
                CopyMemory(destinationAddress, sourceAddress, size);
            }

            private void StoreImmediateScalarToMemory(LirOperand operand, MachineRegister baseRegister, int offset, int size, LirInstruction instruction)
            {
                var bytes = GetImmediateScalarBytes(operand, size, instruction);
                for (var i = 0; i < size; i++)
                {
                    LoadImmediate(GpScratch2, i < bytes.Length ? bytes[i] : 0);
                    StoreToMemory(GpScratch2, baseRegister, checked(offset + i), 1);
                }
            }

            private byte[] GetImmediateScalarBytes(LirOperand operand, int size, LirInstruction instruction)
            {
                if (operand.Kind != LirOperandKind.Immediate)
                    throw new InvalidOperationException("Expected immediate operand.");

                if (IsFloatType(operand.Type))
                {
                    if (IsFloat32(operand.Type))
                        return AdjustConstantBytes(BitConverter.GetBytes(Convert.ToSingle(operand.Immediate, CultureInfo.InvariantCulture)), size);
                    if (IsFloat64(operand.Type))
                        return AdjustConstantBytes(BitConverter.GetBytes(Convert.ToDouble(operand.Immediate, CultureInfo.InvariantCulture)), size);
                    throw HelperRequired(instruction, SelectScalarMoveHelper(operand.Type), "long double immediate materialization requires a runtime helper.");
                }

                return AdjustConstantBytes(BitConverter.GetBytes(ConvertIntegerConstant(operand.Immediate)), size);
            }

            private byte[] AdjustConstantBytes(byte[] bytes, int size)
            {
                size = Math.Max(1, size);
                var result = new byte[size];
                if (_owner._target.Endianness == TargetEndianness.Little)
                {
                    for (var i = 0; i < size && i < bytes.Length; i++)
                        result[i] = bytes[i];
                    return result;
                }

                var ordered = (byte[])bytes.Clone();
                Array.Reverse(ordered);
                for (var i = 0; i < size; i++)
                {
                    var source = ordered.Length - size + i;
                    result[i] = source >= 0 && source < ordered.Length ? ordered[source] : (byte)0;
                }
                return result;
            }

            private void EmitAggregateCopyToRegisterStorage(LirVirtualRegister destination, LirOperand source, LirInstruction instruction)
            {
                var destinationAddress = MaterializeVirtualRegisterStorageAddress(destination, GpScratch0);
                var size = SizeOf(destination.Type);
                if (source.Kind is LirOperandKind.Undefined or LirOperandKind.Void or LirOperandKind.None)
                {
                    ZeroMemory(destinationAddress, size);
                    return;
                }
                MaterializeOperandStorageAddress(source, GpScratch1, instruction);
                CopyMemory(destinationAddress, GpScratch1, size);
            }

            private void MaterializeIncomingHiddenReturnBufferAddress(MachineRegister destination)
            {
                if (_allocation.Frame.HasHiddenReturnBuffer)
                {
                    LoadFromMemory(destination, Sp, _allocation.Frame.HiddenReturnBufferOffset, _owner._target.PointerSize, signed: false);
                    return;
                }

                var cursor = new AbiCursor();
                var location = CAbi.AssignHiddenReturnBufferLocation(_owner._target, ref cursor, _owner._allocationOptions.StackArgumentSlotSize);
                if (location.Kind == AbiLocationKind.Register)
                {
                    MoveRegister(destination, location.Register);
                    return;
                }

                if (location.Kind == AbiLocationKind.Stack)
                {
                    LoadFromMemory(destination, Sp, IncomingStackOffset(location.StackByteOffset(_owner._allocationOptions.StackArgumentSlotSize)), _owner._target.PointerSize, signed: false);
                    return;
                }

                throw new InvalidOperationException("Invalid hidden return buffer ABI location.");
            }

            private void MaterializeAddress(LirAddress address, MachineRegister destination)
            {
                var built = BuildAddress(address, destination, destination == GpScratch1 ? GpScratch2 : GpScratch1);
                if (built.Offset == 0 && built.BaseRegister == destination)
                    return;
                AddImmediate(destination, built.BaseRegister, built.Offset);
            }

            private AddressParts BuildAddress(LirAddress address, MachineRegister scratchBase, MachineRegister scratchIndex)
            {
                switch (address.Kind)
                {
                    case LirAddressKind.StackSlot:
                        if (address.StackSlot is null)
                            throw new InvalidOperationException("Stack slot address has no stack slot.");
                        if (!_allocation.Frame.StackSlotOffsets.TryGetValue(address.StackSlot, out var slotOffset))
                            throw new InvalidOperationException("Missing stack slot offset for " + address.StackSlot.Name + ".");
                        return new AddressParts(Sp, slotOffset);
                    case LirAddressKind.Symbol:
                        if (address.Symbol is null)
                            throw new InvalidOperationException("Symbol address has no symbol.");
                        MaterializeSymbolAddress(_owner.GetSymbolLabel(address.Symbol), scratchBase);
                        return new AddressParts(scratchBase, 0);
                    case LirAddressKind.Indirect:
                        if (address.BaseOperand is null)
                            throw new InvalidOperationException("Indirect address has no base operand.");
                        return new AddressParts(LoadOperand(address.BaseOperand, scratchBase), address.Displacement);
                    case LirAddressKind.Element:
                        if (address.BaseAddress is null)
                            throw new InvalidOperationException("Element address has no base address.");
                        var baseAddress = BuildAddress(address.BaseAddress, scratchBase, scratchIndex);
                        if (baseAddress.Offset != 0)
                            AddImmediate(scratchBase, baseAddress.BaseRegister, baseAddress.Offset);
                        else if (baseAddress.BaseRegister != scratchBase)
                            MoveRegister(scratchBase, baseAddress.BaseRegister);
                        if (address.Index is not null)
                        {
                            var index = LoadOperand(address.Index, scratchIndex);
                            MoveRegister(scratchIndex, index);
                            ScaleIndex(scratchIndex, address.Scale, scratchIndex == GpScratch3 ? GpScratch2 : GpScratch3);
                            Emit(RVInstruction.R(RVInstrKind.Add, ToRegister(scratchBase), ToRegister(scratchBase), ToRegister(scratchIndex)));
                        }
                        return new AddressParts(scratchBase, address.Displacement);
                    case LirAddressKind.Field:
                        if (address.BaseAddress is null)
                            throw new InvalidOperationException("Field address has no base address.");
                        var fieldBase = BuildAddress(address.BaseAddress, scratchBase, scratchIndex);
                        return new AddressParts(fieldBase.BaseRegister, checked(fieldBase.Offset + address.Displacement));
                    default:
                        throw new NotSupportedException("Unsupported LIR address kind " + address.Kind + ".");
                }
            }

            private void ScaleIndex(MachineRegister index, int scale, MachineRegister scratch)
            {
                if (scale <= 1)
                    return;
                if (IsPowerOfTwo(scale))
                {
                    EmitShiftImmediate(RVInstrKind.Slli, index, index, Log2(scale));
                    return;
                }
                if (!_owner._machineTarget.HasM)
                    throw new NotSupportedException("Non power-of-two pointer scale requires M extension.");
                LoadImmediate(scratch, scale);
                Emit(RVInstruction.R(RVInstrKind.Mul, ToRegister(index), ToRegister(index), ToRegister(scratch)));
            }

            private void DivideRegisterByScale(MachineRegister register, int scale, MachineRegister scratch)
            {
                if (scale <= 1)
                    return;
                if (IsPowerOfTwo(scale))
                {
                    EmitShiftImmediate(RVInstrKind.Srai, register, register, Log2(scale));
                    return;
                }
                if (!_owner._machineTarget.HasM)
                    throw new NotSupportedException("Non power-of-two pointer difference scale requires M extension.");
                LoadImmediate(scratch, scale);
                Emit(RVInstruction.R(RVInstrKind.Div, ToRegister(register), ToRegister(register), ToRegister(scratch)));
            }

            private void CopyMemory(MachineRegister destination, MachineRegister source, int size)
            {
                for (var i = 0; i < Math.Max(0, size); i++)
                {
                    LoadFromMemory(GpScratch2, source, i, 1, signed: false);
                    StoreToMemory(GpScratch2, destination, i, 1);
                }
            }

            private void ZeroMemory(MachineRegister destination, int size)
            {
                for (var i = 0; i < Math.Max(0, size); i++)
                    StoreToMemory(MachineRegister.X0, destination, i, 1);
            }

            private void LoadRawBitsFromMemory(MachineRegister destination, MachineRegister baseRegister, int offset, int size)
                => LoadFromMemory(destination, baseRegister, offset, RawStorageSize(size), signed: false);

            private void StoreRawBitsToAddress(MachineRegister source, MachineRegister baseRegister, int offset, int size)
                => StoreToMemory(source, baseRegister, offset, RawStorageSize(size));

            private static int RawStorageSize(int size)
            {
                if (size <= 1)
                    return 1;
                if (size <= 2)
                    return 2;
                if (size <= 4)
                    return 4;
                return 8;
            }


            private void StoreRegister(MachineRegister source, MachineRegister baseRegister, int offset, int size)
                => StoreToMemory(source, baseRegister, offset, size);

            private void LoadFromMemory(MachineRegister destination, MachineRegister baseRegister, int offset, int size, bool signed)
            {
                var opcode = IsFloatRegister(destination) ? FloatingLoadOpcode(size) : LoadOpcode(size, signed);
                EmitMemory(opcode, destination, baseRegister, offset, isStore: false);
            }

            private void StoreToMemory(MachineRegister source, MachineRegister baseRegister, int offset, int size)
            {
                var opcode = IsFloatRegister(source) ? FloatingStoreOpcode(size) : StoreOpcode(size);
                EmitMemory(opcode, source, baseRegister, offset, isStore: true);
            }

            private void EmitMemory(RVInstrKind opcode, MachineRegister valueRegister, MachineRegister baseRegister, int offset, bool isStore)
            {
                if (FitsSignedImmediate(offset, 12))
                {
                    if (isStore)
                        Emit(RVInstruction.S(opcode, ToAnyRegister(valueRegister), ToRegister(baseRegister), offset));
                    else
                        Emit(RVInstruction.I(opcode, ToAnyRegister(valueRegister), ToRegister(baseRegister), offset));
                    return;
                }

                AddImmediate(GpScratch3, baseRegister, offset);
                if (isStore)
                    Emit(RVInstruction.S(opcode, ToAnyRegister(valueRegister), ToRegister(GpScratch3), 0));
                else
                    Emit(RVInstruction.I(opcode, ToAnyRegister(valueRegister), ToRegister(GpScratch3), 0));
            }

            private RVInstrKind LoadOpcode(int size, bool signed)
            {
                return size switch
                {
                    1 => signed ? RVInstrKind.Lb : RVInstrKind.Lbu,
                    2 => signed ? RVInstrKind.Lh : RVInstrKind.Lhu,
                    4 => !signed && _owner._target.Is64Bit ? RVInstrKind.Lwu : RVInstrKind.Lw,
                    8 when _owner._target.Is64Bit => RVInstrKind.Ld,
                    _ => throw new NotSupportedException($"Unsupported store size {size}."),
                };
            }

            private RVInstrKind StoreOpcode(int size)
            {
                return size switch
                {
                    1 => RVInstrKind.Sb,
                    2 => RVInstrKind.Sh,
                    4 => RVInstrKind.Sw,
                    8 when _owner._target.Is64Bit => RVInstrKind.Sd,
                    _ => throw new NotSupportedException($"Unsupported store size {size}."),
                };
            }

            private RVInstrKind FloatingLoadOpcode(int size)
            {
                return size switch
                {
                    4 when _owner._machineTarget.HasF => RVInstrKind.Flw,
                    8 when _owner._machineTarget.HasD => RVInstrKind.Fld,
                    _ => throw new NotSupportedException($"Unsupported floating-pointstore size {size}."),
                };
            }

            private RVInstrKind FloatingStoreOpcode(int size)
            {
                return size switch
                {
                    4 when _owner._machineTarget.HasF => RVInstrKind.Fsw,
                    8 when _owner._machineTarget.HasD => RVInstrKind.Fsd,
                    _ => throw new NotSupportedException($"Unsupported floating-pointstore size {size}."),
                };
            }

            private void LoadFloatingImmediateBits(MachineRegister destination, object? value, QualifiedType type)
            {
                if (IsFloat32(type))
                {
                    var raw = BitConverter.GetBytes(Convert.ToSingle(value, CultureInfo.InvariantCulture));
                    LoadImmediate(destination, BitConverter.ToInt32(raw, 0));
                    return;
                }

                if (IsFloat64(type) && _owner._target.Is64Bit)
                {
                    var raw = BitConverter.GetBytes(Convert.ToDouble(value, CultureInfo.InvariantCulture));
                    LoadImmediate(destination, BitConverter.ToInt64(raw, 0));
                    return;
                }

                throw new NotSupportedException("Floating-point immediate requires a runtime helper.");
            }

            private void LoadFloatingImmediate(MachineRegister destination, object? value, QualifiedType type)
            {
                RequireFloatingHardware(type, null);
                var size = IsFloat32(type) ? 4 : IsFloat64(type) ? 8 : throw new NotSupportedException("long double immediate requires a runtime helper.");
                var label = _owner.CreateLocalLabel(size == 4 ? "f32" : "f64");
                var offset = _owner._rodata.Align(size);
                _owner._rodata.DefineSymbol(label, offset, size, RVObjectSymbolBinding.Local, _owner._symbols);
                if (size == 4)
                {
                    var raw = BitConverter.GetBytes(Convert.ToSingle(value, CultureInfo.InvariantCulture));
                    _owner._rodata.EmitBytes(raw, 4);
                }
                else
                {
                    var raw = BitConverter.GetBytes(Convert.ToDouble(value, CultureInfo.InvariantCulture));
                    _owner._rodata.EmitBytes(raw, 8);
                }
                MaterializeSymbolAddress(label, GpScratch3);
                LoadFromMemory(destination, GpScratch3, 0, size, signed: false);
            }

            private void EmitFloatingR(RVInstrKind opcode, MachineRegister destination, MachineRegister left, MachineRegister right)
                => Emit(RVInstruction.R(opcode, ToFloatRegister(destination), ToFloatRegister(left), ToFloatRegister(right)));

            private void EmitFloatingCompare(RVInstrKind opcode, MachineRegister destination, MachineRegister left, MachineRegister right)
                => Emit(RVInstruction.R(opcode, ToRegister(destination), ToFloatRegister(left), ToFloatRegister(right)));

            private void EmitFloatingConvertFromInteger(RVInstrKind opcode, MachineRegister destination, MachineRegister source)
                => Emit(RVInstruction.R(opcode, ToFloatRegister(destination), ToRegister(source), RVRegister.X0));

            private void EmitFloatingConvertToInteger(RVInstrKind opcode, MachineRegister destination, MachineRegister source)
                => Emit(RVInstruction.R(opcode, ToRegister(destination), ToFloatRegister(source), RVRegister.X0));

            private void EmitFloatingConvert(RVInstrKind opcode, MachineRegister destination, MachineRegister source)
                => Emit(RVInstruction.R(opcode, ToFloatRegister(destination), ToFloatRegister(source), RVRegister.X0));

            private void EmitFloatingMoveToInteger(RVInstrKind opcode, MachineRegister destination, MachineRegister source)
                => Emit(RVInstruction.R(opcode, ToRegister(destination), ToFloatRegister(source), RVRegister.X0));

            private void EmitFloatingMoveFromInteger(RVInstrKind opcode, MachineRegister destination, MachineRegister source)
                => Emit(RVInstruction.R(opcode, ToFloatRegister(destination), ToRegister(source), RVRegister.X0));

            private void RequireFloatingHardware(QualifiedType type, LirInstruction? instruction)
            {
                if (IsFloat32(type) && _owner._machineTarget.HasF)
                    return;
                if (IsFloat64(type) && _owner._machineTarget.HasD)
                    return;
                var message = "Floating-point operation requires a hardware floating-point extension or a runtime helper.";
                if (instruction is null)
                    throw new NotSupportedException(message);
                throw HelperRequired(instruction, SelectFloatingHelper(string.Empty, type), message);
            }

            private void LoadImmediate(MachineRegister destination, long value)
            {
                if (value == 0)
                {
                    MoveRegister(destination, MachineRegister.X0);
                    return;
                }

                if (FitsSignedImmediate(value, 12))
                {
                    EmitImm(RVInstrKind.Addi, destination, MachineRegister.X0, (int)value);
                    return;
                }

                if (value >= int.MinValue && value <= int.MaxValue)
                {
                    var hi = (int)((value + 0x800L) >> 12);
                    var lo = (int)(value - ((long)hi << 12));
                    Emit(RVInstruction.U(RVInstrKind.Lui, ToRegister(destination), hi));
                    if (lo != 0)
                        EmitImm(RVInstrKind.Addi, destination, destination, lo);
                    return;
                }

                if (!_owner._target.Is64Bit)
                    throw new OverflowException("Immediate does not fit RV32 register.");

                var label = _owner.CreateLocalLabel("i64");
                var offset = _owner._rodata.Align(8);
                _owner._rodata.DefineSymbol(label, offset, 8, RVObjectSymbolBinding.Local, _owner._symbols);
                _owner._rodata.EmitInteger(value, 8, _owner._target.Endianness);
                MaterializeSymbolAddress(label, destination);
                LoadFromMemory(destination, destination, 0, 8, signed: false);
            }

            private void MaterializeSymbolAddress(string symbol, MachineRegister destination)
            {
                var hiOffset = _owner._text.ByteLength;
                var auipc = new RVInstruction(RVInstrKind.Auipc, ToRegister(destination), symbol: symbol, relocationKind: RVRelocationKind.AbsoluteUpper20);
                Emit(auipc);
                _owner._text.AddRelocation(hiOffset, symbol, 0, RVObjectRelocationKind.PcrelHi20);
                var loOffset = _owner._text.ByteLength;
                var addi = new RVInstruction(RVInstrKind.Addi, ToRegister(destination), ToRegister(destination), RVRegister.Invalid, 0, symbol, RVRelocationKind.AbsoluteLow12);
                Emit(addi);
                _owner._text.AddRelocation(loOffset, symbol, 0, RVObjectRelocationKind.PcrelLo12I);
            }

            private void AddImmediate(MachineRegister destination, MachineRegister source, int immediate)
            {
                if (immediate == 0)
                {
                    MoveRegister(destination, source);
                    return;
                }

                if (FitsSignedImmediate(immediate, 12))
                {
                    EmitImm(RVInstrKind.Addi, destination, source, immediate);
                    return;
                }

                LoadImmediate(destination, immediate);
                Emit(RVInstruction.R(RVInstrKind.Add, ToRegister(destination), ToRegister(source), ToRegister(destination)));
            }

            private void AdjustStack(int delta)
            {
                if (delta == 0)
                    return;
                if (FitsSignedImmediate(delta, 12))
                {
                    EmitImm(RVInstrKind.Addi, Sp, Sp, delta);
                    return;
                }

                LoadImmediate(GpScratch0, Math.Abs((long)delta));
                Emit(RVInstruction.R(delta < 0 ? RVInstrKind.Sub : RVInstrKind.Add, ToRegister(Sp), ToRegister(Sp), ToRegister(GpScratch0)));
            }

            private void MoveRegister(MachineRegister destination, MachineRegister source)
            {
                if (destination == source)
                    return;
                if (IsFloatRegister(destination) || IsFloatRegister(source))
                {
                    if (!IsFloatRegister(destination) || !IsFloatRegister(source))
                        throw new NotSupportedException("Cross-class register move requires an explicit conversion or bitcast.");
                    EmitFloatingR(_owner._machineTarget.HasD ? RVInstrKind.FsgnjD : RVInstrKind.FsgnjS, destination, source, source);
                    return;
                }
                EmitImm(RVInstrKind.Addi, destination, source, 0);
            }

            private void EmitImm(RVInstrKind opcode, MachineRegister destination, MachineRegister source, int immediate)
            {
                if (!FitsSignedImmediate(immediate, 12))
                    throw new ArgumentOutOfRangeException(nameof(immediate));
                Emit(RVInstruction.I(opcode, ToRegister(destination), ToRegister(source), immediate));
            }

            private void EmitShiftImmediate(RVInstrKind opcode, MachineRegister destination, MachineRegister source, int amount)
                => Emit(new RVInstruction(opcode, ToRegister(destination), ToRegister(source), RVRegister.Invalid, amount));

            private void NormalizeScalarRegister(MachineRegister register, QualifiedType type)
            {
                if (IsFloatType(type))
                    return;
                NormalizeIntegerRegister(register, type);
            }

            private void NormalizeIntegerRegister(MachineRegister register, QualifiedType type)
            {
                if (!IsIntegerLike(type) && !IsPointerLike(type))
                    return;
                if (IsPointerLike(type))
                {
                    if (_owner._target.Is32Bit)
                        EmitShiftPair(register, 0, unsignedShift: true);
                    return;
                }

                var size = SizeOf(type);
                if (_owner._target.Is64Bit)
                {
                    if (size >= 8)
                        return;
                    if (size == 4)
                    {
                        if (IsUnsignedIntegerType(type))
                        {
                            EmitShiftImmediate(RVInstrKind.Slli, register, register, 32);
                            EmitShiftImmediate(RVInstrKind.Srli, register, register, 32);
                        }
                        else
                        {
                            EmitImm(RVInstrKind.Addiw, register, register, 0);
                        }
                        return;
                    }

                    var shift = 64 - size * 8;
                    EmitShiftImmediate(RVInstrKind.Slli, register, register, shift);
                    EmitShiftImmediate(IsUnsignedIntegerType(type) ? RVInstrKind.Srli : RVInstrKind.Srai, register, register, shift);
                    return;
                }

                if (size >= 4)
                    return;
                var shift32 = 32 - size * 8;
                EmitShiftImmediate(RVInstrKind.Slli, register, register, shift32);
                EmitShiftImmediate(IsUnsignedIntegerType(type) ? RVInstrKind.Srli : RVInstrKind.Srai, register, register, shift32);
            }

            private void EmitShiftPair(MachineRegister register, int shift, bool unsignedShift)
            {
                if (shift == 0)
                    return;
                EmitShiftImmediate(RVInstrKind.Slli, register, register, shift);
                EmitShiftImmediate(unsignedShift ? RVInstrKind.Srli : RVInstrKind.Srai, register, register, shift);
            }

            private void EmitCall(string label)
            {
                var offset = _owner._text.ByteLength;
                Emit(RVInstruction.J(RVInstrKind.Jal, RVRegister.X1, label));
                _owner._text.AddRelocation(offset, label, 0, RVObjectRelocationKind.Jal20);
            }

            private void EmitJump(string label)
            {
                Emit(RVInstruction.J(RVInstrKind.Jal, RVRegister.X0, label));
            }

            private void EmitBranch(RVInstrKind opcode, MachineRegister left, MachineRegister right, string label)
            {
                Emit(RVInstruction.B(opcode, ToRegister(left), ToRegister(right), label));
            }

            private void EmitReturnInstruction()
                => Emit(RVInstruction.I(RVInstrKind.Jalr, RVRegister.X0, RVRegister.X1, 0));

            private void EmitNop()
                => Emit(RVInstruction.I(RVInstrKind.Addi, RVRegister.X0, RVRegister.X0, 0));

            private void Emit(RVInstruction instruction)
                => _owner._text.Emit(instruction);

            private string LabelOf(LirBlock? block)
            {
                if (block is null || !_labels.TryGetValue(block, out var label))
                    throw new InvalidOperationException("Missing label for LIR block.");
                return label;
            }

            private bool IsFallthroughTarget(LirBlock? target)
                => false;

            private int IncomingStackOffset(int abiStackOffset)
                => checked(_totalFrameSize + abiStackOffset);

            private int SizeOf(QualifiedType type)
                => Math.Max(1, _owner._target.SizeOf(type));

            private int SizeOfRegisterType(QualifiedType type)
            {
                if (IsPointerLike(type))
                    return _owner._target.PointerSize;
                if (IsFloatType(type))
                    return Math.Max(1, SizeOf(type));
                return Math.Min(Math.Max(1, SizeOf(type)), _owner._target.RegisterSize);
            }

            private int PointerScale(QualifiedType pointerType)
            {
                if (pointerType.Type is PointerType pointer)
                    return Math.Max(1, _owner._target.SizeOf(pointer.PointeeType));
                if (pointerType.Type is ArrayType array)
                    return Math.Max(1, _owner._target.SizeOf(array.ElementType));
                return 1;
            }

            private int RegisterSaveSize(MachineRegister register)
            {
                if (IsFloatRegister(register))
                    return Math.Max(4, CAbi.RiscVAbiFloatingRegisterSize(_owner._target));
                return Math.Max(1, _owner._target.RegisterSize);
            }

            private static bool TypesNeedIntegerConversion(QualifiedType source, QualifiedType destination)
                => (IsIntegerLike(source) || IsPointerLike(source)) && (IsIntegerLike(destination) || IsPointerLike(destination));

            private bool RequiresSoftwareScalar(QualifiedType type)
            {
                if (IsAggregateType(type) || IsPointerLike(type))
                    return false;
                if (IsFloatType(type))
                    return !UsesHardwareFloating(type) && SizeOf(type) > _owner._target.RegisterSize;
                return SizeOf(type) > _owner._target.RegisterSize;
            }

            private bool RequiresStackBackedScalar(QualifiedType type)
                => !IsAggregateType(type) && !IsPointerLike(type) && !UsesHardwareFloating(type) && SizeOf(type) > _owner._target.RegisterSize;

            private bool RequiresBlockCopyStorage(QualifiedType type)
                => IsAggregateType(type) || RequiresStackBackedScalar(type);

            private bool UsesHardwareFloating(QualifiedType type)
                => CAbi.UsesHardwareFloatingRegister(_owner._target, type, isVariadicUnnamedArgument: false);

            private NotImplementedException HelperRequired(LirInstruction instruction, RiscVRuntimeHelperKind helper, string message)
                => new NotImplementedException($"{message} Required helper: {helper}. Function '{_function.Symbol?.Name ?? _functionLabel}', LIR instruction #{instruction.Ordinal}.");

            private RiscVRuntimeHelperKind SelectScalarMoveHelper(QualifiedType type)
            {
                if (_owner._target.Is32Bit && IsIntegerLike(type) && SizeOf(type) == 8)
                    return IsUnsignedIntegerType(type) ? RiscVRuntimeHelperKind.UInt64Move : RiscVRuntimeHelperKind.Int64Move;
                return RiscVRuntimeHelperKind.Unsupported;
            }

            private RiscVRuntimeHelperKind SelectConversionHelper(QualifiedType source, QualifiedType destination)
            {
                if (IsLongDouble(source) || IsLongDouble(destination))
                    return RiscVRuntimeHelperKind.LongDoubleConvert;
                if (_owner._target.Is32Bit && ((IsIntegerLike(source) && SizeOf(source) == 8) || (IsIntegerLike(destination) && SizeOf(destination) == 8)))
                    return RiscVRuntimeHelperKind.Int64Convert;
                if (IsFloatType(source) || IsFloatType(destination))
                    return RiscVRuntimeHelperKind.SoftFloatConvert;
                return RiscVRuntimeHelperKind.Unsupported;
            }

            private RiscVRuntimeHelperKind SelectFloatingHelper(string op, QualifiedType type)
            {
                if (IsLongDouble(type))
                    return RiscVRuntimeHelperKind.LongDoubleArithmetic;
                if (IsFloat32(type))
                    return op switch
                    {
                        "+" => RiscVRuntimeHelperKind.Float32Add,
                        "-" => RiscVRuntimeHelperKind.Float32Sub,
                        "*" => RiscVRuntimeHelperKind.Float32Mul,
                        "/" => RiscVRuntimeHelperKind.Float32Div,
                        _ => RiscVRuntimeHelperKind.SoftFloatOperation,
                    };
                if (IsFloat64(type))
                    return op switch
                    {
                        "+" => RiscVRuntimeHelperKind.Float64Add,
                        "-" => RiscVRuntimeHelperKind.Float64Sub,
                        "*" => RiscVRuntimeHelperKind.Float64Mul,
                        "/" => RiscVRuntimeHelperKind.Float64Div,
                        _ => RiscVRuntimeHelperKind.SoftFloatOperation,
                    };
                return RiscVRuntimeHelperKind.Unsupported;
            }

            private static bool SameBuiltinFloatingType(QualifiedType left, QualifiedType right)
                => (IsFloat32(left) && IsFloat32(right)) || (IsFloat64(left) && IsFloat64(right)) || (IsLongDouble(left) && IsLongDouble(right));

            private static bool FitsSignedImmediate(long value, int bits)
            {
                var min = -(1L << (bits - 1));
                var max = (1L << (bits - 1)) - 1;
                return value >= min && value <= max;
            }

            private static bool IsPowerOfTwo(int value)
                => value > 0 && (value & (value - 1)) == 0;

            private static int Log2(int value)
            {
                var result = 0;
                while (value > 1)
                {
                    result++;
                    value >>= 1;
                }
                return result;
            }

            private static int AlignUp(int value, int alignment)
            {
                if (alignment <= 1)
                    return value;
                var remainder = value % alignment;
                return remainder == 0 ? value : checked(value + alignment - remainder);
            }

            private static RVRegister ToAnyRegister(MachineRegister register)
            {
                if (register >= MachineRegister.X0 && register <= MachineRegister.X31)
                    return (RVRegister)(int)register;
                if (register >= MachineRegister.F0 && register <= MachineRegister.F31)
                    return (RVRegister)(int)register;
                if (register >= MachineRegister.V0 && register <= MachineRegister.V31)
                    return (RVRegister)(int)register;
                throw new NotSupportedException("Unsupported machine register for instruction emission.");
            }

            private static RVRegister ToRegister(MachineRegister register)
            {
                if (register >= MachineRegister.X0 && register <= MachineRegister.X31)
                    return (RVRegister)(int)register;
                throw new NotSupportedException("Expected an integer register.");
            }

            private static RVRegister ToFloatRegister(MachineRegister register)
            {
                if (register >= MachineRegister.F0 && register <= MachineRegister.F31)
                    return (RVRegister)(int)register;
                throw new NotSupportedException("Expected a floating-point register.");
            }

            private static bool IsFloatRegister(MachineRegister register)
                => register >= MachineRegister.F0 && register <= MachineRegister.F31;

            private static int ImmediateToInt32(LirOperand operand)
            {
                var value = ImmediateToInt64(operand);
                if (value < int.MinValue || value > int.MaxValue)
                    throw new OverflowException("Immediate does not fit Int32.");
                return (int)value;
            }

            private static long ImmediateToInt64(LirOperand operand)
            {
                if (operand.Kind != LirOperandKind.Immediate)
                    throw new InvalidOperationException("Expected immediate operand.");
                return ConvertIntegerConstant(operand.Immediate);
            }

            private NotSupportedException Unsupported(LirInstruction instruction, string message)
                => new NotSupportedException($"{message} Function '{_function.Symbol?.Name ?? _functionLabel}', LIR instruction #{instruction.Ordinal}.");

            private enum RiscVRuntimeHelperKind
            {
                Unsupported,
                Int64Move,
                UInt64Move,
                Int64Convert,
                SoftFloatOperation,
                SoftFloatConvert,
                Float32Add,
                Float32Sub,
                Float32Mul,
                Float32Div,
                Float64Add,
                Float64Sub,
                Float64Mul,
                Float64Div,
                LongDoubleArithmetic,
                LongDoubleConvert,
            }

            private readonly struct AddressParts
            {
                public MachineRegister BaseRegister { get; }
                public int Offset { get; }

                public AddressParts(MachineRegister baseRegister, int offset)
                {
                    BaseRegister = baseRegister;
                    Offset = offset;
                }
            }
        }

        private sealed class TextSectionBuilder
        {
            private readonly List<RVInstruction> _instructions = new List<RVInstruction>();
            private readonly Dictionary<string, int> _labels = new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly List<RVObjectRelocation> _relocations = new List<RVObjectRelocation>();

            public string Name { get; }
            public string CurrentLabel { get; private set; } = string.Empty;
            public int ByteLength => checked(_instructions.Count * 4);

            public TextSectionBuilder(string name)
            {
                Name = name;
            }

            public void DefineLabel(string label)
            {
                if (!_labels.ContainsKey(label))
                    _labels.Add(label, ByteLength);
                CurrentLabel = label;
            }

            public void Emit(RVInstruction instruction)
            {
                _instructions.Add(instruction);
                CurrentLabel = string.Empty;
            }

            public void EmitAssembly(string text, string labelPrefix, RVTarget target)
            {
                var program = RiscVAssembler.Assemble(text ?? string.Empty, target);
                var renamedLabels = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var label in program.Text.Labels.Keys)
                    renamedLabels[label] = labelPrefix + "_" + SanitizeSymbolName(label);

                var labelsByOffset = new Dictionary<int, List<string>>();
                foreach (var pair in program.Text.Labels)
                {
                    if (!labelsByOffset.TryGetValue(pair.Value, out var labels))
                    {
                        labels = new List<string>();
                        labelsByOffset.Add(pair.Value, labels);
                    }

                    labels.Add(renamedLabels[pair.Key]);
                }

                var offset = 0;
                foreach (var instruction in program.Text.Instructions)
                {
                    DefineInlineLabels(labelsByOffset, offset);
                    Emit(RewriteInlineLabelReference(instruction, renamedLabels));
                    offset = checked(offset + 4);
                }

                DefineInlineLabels(labelsByOffset, offset);
            }

            private void DefineInlineLabels(Dictionary<int, List<string>> labelsByOffset, int offset)
            {
                if (!labelsByOffset.TryGetValue(offset, out var labels))
                    return;

                foreach (var label in labels)
                    DefineLabel(label);
            }

            private static RVInstruction RewriteInlineLabelReference(
                RVInstruction instruction,
                IReadOnlyDictionary<string, string> renamedLabels)
            {
                return instruction.Symbol is not null && renamedLabels.TryGetValue(instruction.Symbol, out var replacement)
                    ? instruction.WithSymbol(replacement, instruction.RelocationKind)
                    : instruction;
            }

            public void AddRelocation(int offset, string symbol, int addend, RVObjectRelocationKind kind)
                => _relocations.Add(new RVObjectRelocation(Name, offset, symbol, addend, kind));

            public RVTextSection ToSection()
                => new RVTextSection(_instructions, _labels, _relocations.ToImmutableArray());
        }

        private sealed class DataSectionBuilder
        {
            private readonly List<byte> _data = new List<byte>();
            private readonly List<RVObjectRelocation> _relocations = new List<RVObjectRelocation>();

            public string Name { get; }
            public RVObjectSectionKind Kind { get; }
            public int ByteLength => _data.Count;
            public int Alignment { get; private set; } = 1;

            public DataSectionBuilder(string name, RVObjectSectionKind kind)
            {
                Name = name;
                Kind = kind;
            }

            public int Align(int alignment)
            {
                alignment = Math.Max(1, alignment);
                Alignment = Math.Max(Alignment, alignment);
                var aligned = AlignUp(ByteLength, alignment);
                EmitZero(aligned - ByteLength);
                return ByteLength;
            }

            public void DefineSymbol(string name, int offset, int size, RVObjectSymbolBinding binding, List<RVObjectSymbol> symbols)
                => symbols.Add(new RVObjectSymbol(name, Name, offset, size, binding, RVObjectSymbolKind.Object));

            public void AddRelocation(int offset, string symbol, int addend, RVObjectRelocationKind kind)
                => _relocations.Add(new RVObjectRelocation(Name, offset, symbol, addend, kind));

            public void EmitByte(byte value)
                => _data.Add(value);

            public void EmitBytes(byte[] bytes, int count)
            {
                for (var i = 0; i < count && i < bytes.Length; i++)
                    _data.Add(bytes[i]);
            }

            public void EmitZero(int count)
            {
                for (var i = 0; i < count; i++)
                    _data.Add(0);
            }

            public void EmitInteger(long value, int size, TargetEndianness endianness)
            {
                var bytes = BitConverter.GetBytes(value);
                if (endianness == TargetEndianness.Big)
                    Array.Reverse(bytes);
                if (endianness == TargetEndianness.Little)
                {
                    for (var i = 0; i < size; i++)
                        _data.Add(i < bytes.Length ? bytes[i] : (byte)0);
                }
                else
                {
                    for (var i = bytes.Length - size; i < bytes.Length; i++)
                        _data.Add(i >= 0 ? bytes[i] : (byte)0);
                }
            }

            public RVDataSection ToSection()
                => new RVDataSection(Name, Kind, Alignment, _data.ToImmutableArray(), 0, _relocations.ToImmutableArray());

            private static int AlignUp(int value, int alignment)
            {
                if (alignment <= 1)
                    return value;
                var remainder = value % alignment;
                return remainder == 0 ? value : checked(value + alignment - remainder);
            }
        }

        private sealed class BssSectionBuilder
        {
            public string Name { get; }
            public int ByteLength { get; private set; }
            public int Alignment { get; private set; } = 1;

            public BssSectionBuilder(string name)
            {
                Name = name;
            }

            public int Allocate(int size, int alignment)
            {
                alignment = Math.Max(1, alignment);
                Alignment = Math.Max(Alignment, alignment);
                ByteLength = AlignUp(ByteLength, alignment);
                var offset = ByteLength;
                ByteLength = checked(ByteLength + Math.Max(0, size));
                return offset;
            }

            public void DefineSymbol(string name, int offset, int size, RVObjectSymbolBinding binding, List<RVObjectSymbol> symbols)
                => symbols.Add(new RVObjectSymbol(name, Name, offset, size, binding, RVObjectSymbolKind.Object));

            public RVDataSection ToSection()
                => new RVDataSection(Name, RVObjectSectionKind.Bss, Alignment, ImmutableArray<byte>.Empty, ByteLength, ImmutableArray<RVObjectRelocation>.Empty);

            private static int AlignUp(int value, int alignment)
            {
                if (alignment <= 1)
                    return value;
                var remainder = value % alignment;
                return remainder == 0 ? value : checked(value + alignment - remainder);
            }
        }
    }
}
