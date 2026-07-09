using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Cnidaria.X86;

namespace Cnidaria.C
{
    internal sealed class X86CodeGenerator
    {
        private const string TextSectionName = ".text";
        private const string RodataSectionName = ".rodata";
        private const string DataSectionName = ".data";
        private const string BssSectionName = ".bss";

        private readonly LirModule _module;
        private readonly TargetInfo _target;
        private readonly X86Target _machineTarget;
        private readonly LSRAOptions _allocationOptions;
        private readonly Dictionary<FunctionSymbol, string> _functionLabels = new Dictionary<FunctionSymbol, string>();
        private readonly Dictionary<string, string> _functionLabelsByName = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<Symbol, string> _dataLabels = new Dictionary<Symbol, string>();
        private readonly Dictionary<string, string> _stringLabels = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _floatingLiteralLabels = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> _usedLabels = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _externalLabels = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<X86ObjectSymbol> _symbols = new List<X86ObjectSymbol>();
        private readonly DataSectionBuilder _rodata = new DataSectionBuilder(RodataSectionName, X86ObjectSectionKind.Rodata);
        private readonly DataSectionBuilder _data = new DataSectionBuilder(DataSectionName, X86ObjectSectionKind.Data);
        private readonly BssSectionBuilder _bss = new BssSectionBuilder(BssSectionName);
        private TextSectionBuilder _text = null!;
        private int _nextLocalId;

        private X86CodeGenerator(LirModule module, LSRAOptions? allocationOptions)
        {
            _module = module ?? throw new ArgumentNullException(nameof(module));
            _target = module.SemanticModel.Compilation.Options.Target;
            if (_target.Architecture is not TargetArchitectureKind.X86 and not TargetArchitectureKind.X64)
                throw new NotSupportedException("x86 C backend requires X86 or X64 target.");
            _machineTarget = X86Target.FromTargetInfo(_target);
            _allocationOptions = ReserveCodeGenScratchRegisters(_target, allocationOptions ?? LSRAOptions.ForTarget(_target));
        }

        private static LSRAOptions ReserveCodeGenScratchRegisters(TargetInfo target, LSRAOptions options)
        {
            var reservedGeneral = target.Architecture switch
            {
                TargetArchitectureKind.X86 => ImmutableHashSet.Create(MachineRegister.X0, MachineRegister.X1, MachineRegister.X2, MachineRegister.X4, MachineRegister.X5),
                TargetArchitectureKind.X64 when TargetRegisterInfo.IsWindowsX64(target) => ImmutableHashSet.Create(MachineRegister.X1, MachineRegister.X5, MachineRegister.X6),
                TargetArchitectureKind.X64 => ImmutableHashSet.Create(MachineRegister.X4, MachineRegister.X7, MachineRegister.X8),
                _ => ImmutableHashSet<MachineRegister>.Empty,
            };

            var reservedVector = ImmutableHashSet.Create(MachineRegister.V0, MachineRegister.V1, MachineRegister.V2);

            return new LSRAOptions(
                generalRegisters: options.GeneralRegisters.Where(r => !reservedGeneral.Contains(r)).ToImmutableArray(),
                floatingRegisters: options.FloatingRegisters,
                vectorRegisters: options.VectorRegisters.Where(r => !reservedVector.Contains(r)).ToImmutableArray(),
                stackAlignment: options.StackAlignment,
                spillSlotSize: options.SpillSlotSize,
                spillSlotAlignment: options.SpillSlotAlignment,
                stackArgumentSlotSize: options.StackArgumentSlotSize);
        }

        public static X86Program Generate(LirModule module, LSRAOptions? allocationOptions = null)
            => new X86CodeGenerator(module, allocationOptions).Generate();

        private X86Program Generate()
        {
            _text = new TextSectionBuilder(_machineTarget, TextSectionName);
            IndexFunctions();
            EmitGlobalStorage();
            foreach (var function in _module.Functions)
                EmitFunction(function);

            var selectedEntry = _functionLabelsByName.TryGetValue("main", out var mainLabel)
                ? mainLabel
                : (_functionLabels.Values.FirstOrDefault() ?? string.Empty);
            var entry = IsWindowsExecutableTarget
                ? EmitWindowsRuntime(selectedEntry)
                : IsLinuxExecutableTarget
                    ? EmitLinuxRuntime(selectedEntry)
                    : selectedEntry;

            AddSectionSymbols();
            var dataSections = ImmutableArray.CreateBuilder<X86DataSection>();
            dataSections.Add(_rodata.ToSection());
            dataSections.Add(_data.ToSection());
            dataSections.Add(_bss.ToSection());

            return new X86Program(
                _machineTarget,
                _text.ToSection(),
                dataSections.ToImmutable(),
                _symbols.ToImmutableArray(),
                entry);
        }



        private bool IsWindowsExecutableTarget
            => _target.OperatingSystem == OperatingSystemKind.Windows && _target.IsX86;

        private bool IsLinuxExecutableTarget
            => _target.OperatingSystem == OperatingSystemKind.Linux && _target.IsX86;

        private string EmitWindowsRuntime(string userEntryLabel)
        {
            AddWindowsImportSymbols();
            return EmitWindowsStart(userEntryLabel);
        }

        private string EmitLinuxRuntime(string userEntryLabel)
        {
            return EmitLinuxStart(userEntryLabel);
        }

        private string EmitLinuxStart(string userEntryLabel)
        {
            var label = CreateUniqueGlobalLabel("_start");
            var startOffset = _text.ByteLength;
            _text.DefineLabel(label);
            if (_machineTarget.Is64Bit)
            {
                Emit(X86Instruction.Unary(X86InstrKind.Pop, Reg(X86Register.Rdi, 8)));
                Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.Rsi, 8), Reg(X86Register.Rsp, 8)));
                Emit(X86Instruction.Binary(X86InstrKind.And, Reg(X86Register.Rsp, 8), Imm(-16)));
                if (!string.IsNullOrEmpty(userEntryLabel))
                    Emit(X86Instruction.Branch(X86InstrKind.Call, X86Operand.SymbolOperand(userEntryLabel, 4, X86ObjectRelocationKind.Relative32)));
                else
                    Emit(X86Instruction.Binary(X86InstrKind.Xor, Reg(X86Register.Rax, 4), Reg(X86Register.Rax, 4)));
                Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.Rdi, 4), Reg(X86Register.Rax, 4)));
                Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.Rax, 4), Imm(60)));
                Emit(new X86Instruction(X86InstrKind.Syscall));
                Emit(new X86Instruction(X86InstrKind.Ud2));
            }
            else
            {
                if (!string.IsNullOrEmpty(userEntryLabel))
                {
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.Rax, 4), Reg(X86Register.Rsp, 4)));
                    Emit(X86Instruction.Binary(X86InstrKind.Add, Reg(X86Register.Rax, 4), Imm(4)));
                    Emit(X86Instruction.Unary(X86InstrKind.Push, Reg(X86Register.Rax, 4)));
                    Emit(X86Instruction.Unary(X86InstrKind.Push, Mem(X86Register.Rsp, 4, 4)));
                    Emit(X86Instruction.Branch(X86InstrKind.Call, X86Operand.SymbolOperand(userEntryLabel, 4, X86ObjectRelocationKind.Relative32)));
                    Emit(X86Instruction.Binary(X86InstrKind.Add, Reg(X86Register.Rsp, 4), Imm(8)));
                }
                else
                    Emit(X86Instruction.Binary(X86InstrKind.Xor, Reg(X86Register.Rax, 4), Reg(X86Register.Rax, 4)));
                Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.Rbx, 4), Reg(X86Register.Rax, 4)));
                Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.Rax, 4), Imm(1)));
                Emit(X86Instruction.Raw(new byte[] { 0xcd, 0x80 }));
                Emit(new X86Instruction(X86InstrKind.Ud2));
            }
            _symbols.Add(new X86ObjectSymbol(label, TextSectionName, startOffset, _text.ByteLength - startOffset, X86ObjectSymbolBinding.Global, X86ObjectSymbolKind.Function));
            return label;
        }

        private void AddWindowsImportSymbols()
        {
            AddExternalSymbol("__imp_GetStdHandle", X86ObjectSymbolKind.Object);
            AddExternalSymbol("__imp_WriteFile", X86ObjectSymbolKind.Object);
            AddExternalSymbol("__imp_ExitProcess", X86ObjectSymbolKind.Object);
        }

        private string EmitWindowsStart(string userEntryLabel)
        {
            var label = CreateUniqueGlobalLabel("_start");
            var startOffset = _text.ByteLength;
            _text.DefineLabel(label);
            if (_machineTarget.Is64Bit)
            {
                Emit(X86Instruction.Binary(X86InstrKind.And, Reg(X86Register.Rsp, 8), Imm(-16)));
                Emit(X86Instruction.Binary(X86InstrKind.Sub, Reg(X86Register.Rsp, 8), Imm(32)));
                if (!string.IsNullOrEmpty(userEntryLabel))
                {
                    Emit(X86Instruction.Binary(X86InstrKind.Xor, Reg(X86Register.Rcx, 4), Reg(X86Register.Rcx, 4)));
                    Emit(X86Instruction.Binary(X86InstrKind.Xor, Reg(X86Register.Rdx, 4), Reg(X86Register.Rdx, 4)));
                    Emit(X86Instruction.Branch(X86InstrKind.Call, X86Operand.SymbolOperand(userEntryLabel, 4, X86ObjectRelocationKind.Relative32)));
                }
                else
                    Emit(X86Instruction.Binary(X86InstrKind.Xor, Reg(X86Register.Rax, 4), Reg(X86Register.Rax, 4)));
                Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.Rcx, 4), Reg(X86Register.Rax, 4)));
                Emit(X86Instruction.Branch(X86InstrKind.Call, ImportPointerOperand("ExitProcess")));
                Emit(new X86Instruction(X86InstrKind.Ud2));
            }
            else
            {
                if (!string.IsNullOrEmpty(userEntryLabel))
                {
                    Emit(X86Instruction.Unary(X86InstrKind.Push, Imm(0)));
                    Emit(X86Instruction.Unary(X86InstrKind.Push, Imm(0)));
                    Emit(X86Instruction.Branch(X86InstrKind.Call, X86Operand.SymbolOperand(userEntryLabel, 4, X86ObjectRelocationKind.Relative32)));
                    Emit(X86Instruction.Binary(X86InstrKind.Add, Reg(X86Register.Rsp, 4), Imm(8)));
                }
                else
                    Emit(X86Instruction.Binary(X86InstrKind.Xor, Reg(X86Register.Rax, 4), Reg(X86Register.Rax, 4)));
                Emit(X86Instruction.Unary(X86InstrKind.Push, Reg(X86Register.Rax, 4)));
                Emit(X86Instruction.Branch(X86InstrKind.Call, ImportPointerOperand("ExitProcess")));
                Emit(new X86Instruction(X86InstrKind.Ud2));
            }
            _symbols.Add(new X86ObjectSymbol(label, TextSectionName, startOffset, _text.ByteLength - startOffset, X86ObjectSymbolBinding.Global, X86ObjectSymbolKind.Function));
            return label;
        }

        private X86Operand ImportPointerOperand(string name)
        {
            var symbol = "__imp_" + name;
            return _machineTarget.Is64Bit
                ? X86Operand.RipRelative(symbol, 0, 8)
                : X86Operand.Memory(X86Register.Invalid, 0, 4, X86Register.Invalid, 1, symbol, X86ObjectRelocationKind.Absolute32);
        }

        private void Emit(X86Instruction instruction)
            => _text.Emit(instruction);

        private static X86Operand Reg(X86Register register, int size)
            => X86Operand.RegisterOperand(register, Math.Max(1, size));

        private static X86Operand Mem(X86Register register, long displacement, int size)
            => X86Operand.Memory(register, displacement, Math.Max(1, size));

        private static X86Operand Imm(long value)
            => X86Operand.ImmediateOperand(value);


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
            _symbols.Add(new X86ObjectSymbol(TextSectionName, TextSectionName, 0, _text.ByteLength, X86ObjectSymbolBinding.Local, X86ObjectSymbolKind.Section));
            _symbols.Add(new X86ObjectSymbol(RodataSectionName, RodataSectionName, 0, _rodata.ByteLength, X86ObjectSymbolBinding.Local, X86ObjectSymbolKind.Section));
            _symbols.Add(new X86ObjectSymbol(DataSectionName, DataSectionName, 0, _data.ByteLength, X86ObjectSymbolBinding.Local, X86ObjectSymbolKind.Section));
            _symbols.Add(new X86ObjectSymbol(BssSectionName, BssSectionName, 0, _bss.ByteLength, X86ObjectSymbolBinding.Local, X86ObjectSymbolKind.Section));
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
                var binding = global.StorageClass == StorageClass.Static ? X86ObjectSymbolBinding.Local : X86ObjectSymbolBinding.Global;

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
            => AddExternalSymbol(symbol.Name, X86ObjectSymbolKind.Object);

        private void AddExternalFunctionSymbol(FunctionSymbol symbol)
            => AddExternalSymbol(symbol.Name, X86ObjectSymbolKind.Function);

        private void AddExternalSymbol(string name, X86ObjectSymbolKind kind)
        {
            var label = CreateExternalLabel(name);
            if (_externalLabels.Add(label))
                _symbols.Add(new X86ObjectSymbol(label, string.Empty, 0, 0, X86ObjectSymbolBinding.External, kind));
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
            section.EmitInteger(ConvertIntegerConstant(value), size);
            return size;
        }

        private void EmitPointerRelocation(DataSectionBuilder section, string symbol)
        {
            var offset = section.ByteLength;
            section.EmitZero(_target.PointerSize);
            section.AddRelocation(offset, symbol, 0, _target.PointerSize == 8 ? X86ObjectRelocationKind.Absolute64 : X86ObjectRelocationKind.Absolute32);
        }

        private string GetSymbolLabel(Symbol symbol)
        {
            if (symbol is FunctionSymbol function)
            {
                if (_functionLabels.TryGetValue(function, out var functionLabel))
                    return functionLabel;
                if (_functionLabelsByName.TryGetValue(function.Name, out functionLabel))
                    return functionLabel;
                AddExternalFunctionSymbol(function);
                return CreateExternalLabel(function.Name);
            }

            if (_dataLabels.TryGetValue(symbol, out var dataLabel))
                return dataLabel;

            AddExternalObjectSymbol(symbol);
            return CreateExternalLabel(symbol.Name);
        }

        private void EmitFunction(LirFunction function)
        {
            if (function.Symbol is null || !_functionLabels.TryGetValue(function.Symbol, out var label))
                throw new NotSupportedException("Cannot emit anonymous functions to x86 object code.");

            var allocation = LinearScanRegisterAllocator.Allocate(function, _target, _allocationOptions);
            var blockLabels = new Dictionary<LirBlock, string>();
            foreach (var block in function.Blocks)
                blockLabels.Add(block, CreateLocalLabel(label + "_" + block.Name));

            var context = new FunctionEmissionContext(this, function, allocation, label, blockLabels);
            var startOffset = _text.ByteLength;
            _text.DefineLabel(label);
            context.EmitPrologue();
            context.EmitBlocks();
            context.EmitEpilogue();
            var size = _text.ByteLength - startOffset;
            var binding = function.Symbol.StorageClass == StorageClass.Static ? X86ObjectSymbolBinding.Local : X86ObjectSymbolBinding.Global;
            _symbols.Add(new X86ObjectSymbol(label, TextSectionName, startOffset, size, binding, X86ObjectSymbolKind.Function));
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
            _rodata.DefineSymbol(label, offset, bytes.Length + 1, X86ObjectSymbolBinding.Local, _symbols);
            _rodata.EmitBytes(bytes, bytes.Length);
            _rodata.EmitByte(0);
            _stringLabels.Add(text, label);
            return label;
        }

        private string CreateFloatingLiteral(QualifiedType type, object? value)
        {
            if (IsFloat32(type))
            {
                var bytes = BitConverter.GetBytes(Convert.ToSingle(value, CultureInfo.InvariantCulture));
                var bits = BitConverter.ToUInt32(bytes, 0);
                return CreateReadOnlyBytesLiteral("fp32:" + bits.ToString("X8", CultureInfo.InvariantCulture), bytes, 4, "fp32");
            }

            if (IsFloat64(type))
            {
                var bytes = BitConverter.GetBytes(Convert.ToDouble(value, CultureInfo.InvariantCulture));
                var bits = BitConverter.ToUInt64(bytes, 0);
                return CreateReadOnlyBytesLiteral("fp64:" + bits.ToString("X16", CultureInfo.InvariantCulture), bytes, 8, "fp64");
            }

            throw new NotSupportedException("x86 backend does not support long double literals.");
        }

        private string CreateFloatingBitsLiteral(QualifiedType type, ulong bits)
        {
            if (IsFloat32(type))
            {
                var narrowed = unchecked((uint)bits);
                return CreateReadOnlyBytesLiteral("fp32bits:" + narrowed.ToString("X8", CultureInfo.InvariantCulture), BitConverter.GetBytes(narrowed), 4, "fp32bits");
            }

            if (IsFloat64(type))
                return CreateReadOnlyBytesLiteral("fp64bits:" + bits.ToString("X16", CultureInfo.InvariantCulture), BitConverter.GetBytes(bits), 8, "fp64bits");

            throw new NotSupportedException("x86 backend does not support long double literals.");
        }

        private string CreateReadOnlyBytesLiteral(string key, byte[] bytes, int alignment, string prefix)
        {
            if (_floatingLiteralLabels.TryGetValue(key, out var existing))
                return existing;

            var label = CreateLocalLabel(prefix);
            var offset = _rodata.Align(Math.Max(1, alignment));
            _rodata.DefineSymbol(label, offset, bytes.Length, X86ObjectSymbolBinding.Local, _symbols);
            _rodata.EmitBytes(bytes, bytes.Length);
            _floatingLiteralLabels.Add(key, label);
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
            return type.Type is BuiltinType builtin && builtin.BuiltinKind is BuiltinTypeKind.SignedChar or BuiltinTypeKind.Short or BuiltinTypeKind.Int
                or BuiltinTypeKind.Long or BuiltinTypeKind.LongLong;
        }

        private static bool IsUnsignedIntegerType(QualifiedType type)
            => type.Type is BuiltinType builtin && builtin.BuiltinKind is BuiltinTypeKind.Bool or BuiltinTypeKind.Char or BuiltinTypeKind.UnsignedChar
            or BuiltinTypeKind.UnsignedShort or BuiltinTypeKind.UnsignedInt or BuiltinTypeKind.UnsignedLong or BuiltinTypeKind.UnsignedLongLong;

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
            private readonly X86CodeGenerator _owner;
            private readonly LirFunction _function;
            private readonly AllocationResult _allocation;
            private readonly string _functionLabel;
            private readonly IReadOnlyDictionary<LirBlock, string> _blockLabels;
            private readonly string _epilogueLabel;
            private readonly List<MachineRegister> _savedGeneralRegisters;
            private readonly List<MachineRegister> _savedVectorRegisters;
            private readonly int _wordSize;
            private readonly int _frameSize;
            private readonly int _stackArgumentSlotSize;
            private readonly int _sysVX64RegisterSaveAreaOffset;

            public FunctionEmissionContext(
                X86CodeGenerator owner,
                LirFunction function,
                AllocationResult allocation,
                string functionLabel,
                IReadOnlyDictionary<LirBlock, string> blockLabels)
            {
                _owner = owner;
                _function = function;
                _allocation = allocation;
                _functionLabel = functionLabel;
                _blockLabels = blockLabels;
                _epilogueLabel = owner.CreateLocalLabel(functionLabel + "_epilogue");
                var savedRegisters = allocation.UsedPhysicalRegisters
                    .Where(r => TargetRegisterInfo.IsCalleeSaved(owner._target, r) && ToX86Register(r, owner._machineTarget) != X86Register.Rbp)
                    .Distinct()
                    .OrderBy(r => (int)r)
                    .ToList();
                _savedGeneralRegisters = savedRegisters
                    .Where(r => MachineRegisters.GetClass(r) == RegisterClass.General)
                    .ToList();
                _savedVectorRegisters = savedRegisters
                    .Where(r => MachineRegisters.GetClass(r) == RegisterClass.Vector)
                    .ToList();
                _wordSize = owner._target.PointerSize;
                _stackArgumentSlotSize = owner._allocationOptions.StackArgumentSlotSize;
                _sysVX64RegisterSaveAreaOffset = NeedsSysVX64RegisterSaveArea()
                    ? AlignUp(allocation.Frame.FrameSize, 16)
                    : -1;
                _frameSize = ComputeFrameSize();
            }

            public void EmitPrologue()
            {
                foreach (var register in _savedGeneralRegisters)
                    Emit(X86Instruction.Unary(X86InstrKind.Push, Reg(ToX86Register(register, _owner._machineTarget), _wordSize)));

                if (_frameSize != 0)
                    Emit(X86Instruction.Binary(X86InstrKind.Sub, StackPointer(), Imm(_frameSize)));

                SaveCalleeSavedVectorRegisters();
                SaveIncomingSysVX64VarArgsRegisterArea();
                HomeIncomingWindowsX64VarArgsRegisters();

                StoreIncomingVarArgsPointer();
                if (_allocation.Frame.HasHiddenReturnBuffer)
                    StoreIncomingHiddenReturnBuffer();
            }

            public void EmitBlocks()
            {
                foreach (var block in _function.Blocks)
                {
                    DefineBlockLabel(block);
                    foreach (var instruction in block.Instructions)
                        EmitInstruction(instruction);
                }
            }

            public void EmitEpilogue()
            {
                _owner._text.DefineLabel(_epilogueLabel);
                RestoreCalleeSavedVectorRegisters();
                if (_frameSize != 0)
                    Emit(X86Instruction.Binary(X86InstrKind.Add, StackPointer(), Imm(_frameSize)));
                for (var i = _savedGeneralRegisters.Count - 1; i >= 0; i--)
                    Emit(X86Instruction.Unary(X86InstrKind.Pop, Reg(ToX86Register(_savedGeneralRegisters[i], _owner._machineTarget), _wordSize)));
                Emit(X86Instruction.Ret());
            }

            private int ComputeFrameSize()
            {
                var baseFrameSize = _allocation.Frame.FrameSize;
                if (_sysVX64RegisterSaveAreaOffset >= 0)
                    baseFrameSize = checked(_sysVX64RegisterSaveAreaOffset + SysVX64RegisterSaveAreaSize);

                var frameSize = AlignUp(baseFrameSize, Math.Max(1, _owner._allocationOptions.StackAlignment));
                if (_owner._machineTarget.Is64Bit)
                {
                    while (PositiveModulo(8 - _savedGeneralRegisters.Count * _wordSize - frameSize, 16) != 0)
                        frameSize += 8;
                }
                return frameSize;
            }

            private void SaveCalleeSavedVectorRegisters()
            {
                foreach (var register in _savedVectorRegisters)
                {
                    var stackSlot = SavedRegisterStackSlot(register);
                    Emit(X86Instruction.Binary(VectorSaveMove(stackSlot.Displacement), stackSlot, Reg(ToX86Register(register, _owner._machineTarget), 16)));
                }
            }

            private void RestoreCalleeSavedVectorRegisters()
            {
                for (var i = _savedVectorRegisters.Count - 1; i >= 0; i--)
                {
                    var register = _savedVectorRegisters[i];
                    var stackSlot = SavedRegisterStackSlot(register);
                    Emit(X86Instruction.Binary(VectorSaveMove(stackSlot.Displacement), Reg(ToX86Register(register, _owner._machineTarget), 16), stackSlot));
                }
            }

            private X86Operand SavedRegisterStackSlot(MachineRegister register)
            {
                if (!_allocation.Frame.SavedRegisterOffsets.TryGetValue(register, out var offset))
                    throw new InvalidOperationException("Missing saved-register stack slot for " + register + ".");

                return Mem(X86Register.Rsp, offset, 16);
            }

            private static X86InstrKind VectorSaveMove(long stackOffset)
                => PositiveModulo((int)stackOffset, 16) == 0 ? X86InstrKind.Movaps : X86InstrKind.Movups;

            private const int SysVX64RegisterSaveAreaSize = 176;
            private const int SysVX64GpSaveAreaSize = 48;
            private const int SysVX64FpSaveAreaOffset = 48;

            private bool NeedsSysVX64RegisterSaveArea()
                => _owner._machineTarget.Is64Bit && !TargetRegisterInfo.IsWindowsX64(_owner._target) && _function.Symbol?.FunctionType?.IsVariadic == true;

            private void SaveIncomingSysVX64VarArgsRegisterArea()
            {
                if (_sysVX64RegisterSaveAreaOffset < 0)
                    return;

                var gpRegisters = TargetRegisterInfo.IntegerArgumentRegisters(_owner._target);
                for (var i = 0; i < gpRegisters.Length; i++)
                    Emit(X86Instruction.Binary(X86InstrKind.Mov,
                        Mem(X86Register.Rsp, _sysVX64RegisterSaveAreaOffset + i * 8, 8),
                        Reg(ToX86Register(gpRegisters[i], _owner._machineTarget), 8)));

                var vectorRegisters = TargetRegisterInfo.VectorArgumentRegisters(_owner._target);
                for (var i = 0; i < vectorRegisters.Length; i++)
                    Emit(X86Instruction.Binary(X86InstrKind.Movups,
                        Mem(X86Register.Rsp, _sysVX64RegisterSaveAreaOffset + SysVX64FpSaveAreaOffset + i * 16, 16),
                        Reg(ToX86Register(vectorRegisters[i], _owner._machineTarget), 16)));
            }

            private void HomeIncomingWindowsX64VarArgsRegisters()
            {
                if (!_owner._machineTarget.Is64Bit || !TargetRegisterInfo.IsWindowsX64(_owner._target) || _function.Symbol?.FunctionType?.IsVariadic != true)
                    return;

                var registers = TargetRegisterInfo.IntegerArgumentRegisters(_owner._target);
                for (var i = 0; i < registers.Length; i++)
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, Mem(X86Register.Rsp, IncomingStackOffset(i * _stackArgumentSlotSize), _wordSize),
                        Reg(ToX86Register(registers[i], _owner._machineTarget), _wordSize)));
            }

            private void StoreIncomingVarArgsPointer()
            {
                if (!_allocation.Frame.HasVarArgsPointer)
                    return;

                if (_owner._machineTarget.Is64Bit && !TargetRegisterInfo.IsWindowsX64(_owner._target))
                    return;

                var functionType = _function.Symbol?.FunctionType;
                if (functionType is null)
                    return;

                var cursor = new AbiCursor();
                if (_allocation.Frame.HasHiddenReturnBuffer)
                    _ = CAbi.AssignHiddenReturnBufferLocation(_owner._target, ref cursor, _stackArgumentSlotSize);
                foreach (var parameter in functionType.Parameters)
                {
                    var value = CAbi.ClassifyValue(_owner._target, parameter.Type, isReturn: false, isVariadicUnnamedArgument: false);
                    _ = CAbi.AssignArgumentLocation(value, ref cursor, _stackArgumentSlotSize);
                }

                if (_owner._machineTarget.Is64Bit && TargetRegisterInfo.IsWindowsX64(_owner._target))
                    Emit(X86Instruction.Binary(X86InstrKind.Lea, Reg(Scratch0, _wordSize),
                        Mem(X86Register.Rsp, IncomingStackOffset(cursor.Unified * _stackArgumentSlotSize), _wordSize)));
                else
                    Emit(X86Instruction.Binary(X86InstrKind.Lea, Reg(Scratch0, _wordSize),
                        Mem(X86Register.Rsp, IncomingStackOffset(cursor.Stack * _stackArgumentSlotSize), _wordSize)));
                EmitStoreToStack(Scratch0, _allocation.Frame.VarArgsPointerOffset, _wordSize);
            }

            private void StoreIncomingHiddenReturnBuffer()
            {
                var cursor = new AbiCursor();
                var location = CAbi.AssignHiddenReturnBufferLocation(_owner._target, ref cursor, _stackArgumentSlotSize);
                if (location.Kind == AbiLocationKind.Register)
                    EmitStoreToStack(ToX86Register(location.Register, _owner._machineTarget), _allocation.Frame.HiddenReturnBufferOffset, _wordSize);
                else if (location.Kind == AbiLocationKind.Stack)
                    EmitMemoryCopy(Mem(X86Register.Rsp, IncomingStackOffset(location.StackByteOffset(_stackArgumentSlotSize)), _wordSize),
                        Mem(X86Register.Rsp, _allocation.Frame.HiddenReturnBufferOffset, _wordSize), _wordSize);
            }

            private void EmitInstruction(LirInstruction instruction)
            {
                switch (instruction.Kind)
                {
                    case LirInstructionKind.Nop:
                        Emit(X86Instruction.Nop());
                        break;
                    case LirInstructionKind.Parameter:
                        EmitParameter(instruction);
                        break;
                    case LirInstructionKind.Copy:
                    case LirInstructionKind.Constant:
                        EmitCopy(instruction);
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
                    case LirInstructionKind.Convert:
                    case LirInstructionKind.Cast:
                        EmitConvert(instruction);
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
                        EmitJump(instruction.Target);
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
                        Emit(new X86Instruction(X86InstrKind.Ud2));
                        break;
                    default:
                        throw Unsupported(instruction, $"Unsupported LIR instruction kind for x86 codegen: {instruction.Kind}.");
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
                        formatted = new InlineAsmFormattedOperand(output.Name, modifier => FormatX86AsmMemoryOperand(operand, instruction));
                    }
                    else
                    {
                        if (copyIndex >= instruction.ParallelCopies.Length)
                            throw Unsupported(instruction, "Inline assembly output register is missing from LIR.");
                        var destination = instruction.ParallelCopies[copyIndex++].Destination;
                        formatted = CreateX86AsmOutputOperand(output, destination, instruction, outputFinalizers);
                    }

                    AddAsmOperand(operands, namedOperands, formatted);
                }

                foreach (var input in asmStatement.Inputs)
                {
                    if (operandIndex >= instruction.Operands.Length)
                        throw Unsupported(instruction, "Inline assembly input operand is missing from LIR.");

                    var operand = instruction.Operands[operandIndex++];
                    var formatted = CreateX86AsmInputOperand(input, operand, instruction);
                    AddAsmOperand(operands, namedOperands, formatted);
                }

                foreach (var label in asmStatement.GotoLabels)
                {
                    if (operandIndex >= instruction.Operands.Length || instruction.Operands[operandIndex].Kind != LirOperandKind.Label || instruction.Operands[operandIndex].Label is null)
                        throw Unsupported(instruction, "Inline assembly goto label is missing from LIR.");

                    var text = LabelName(instruction.Operands[operandIndex++].Label!);
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

                if (asmStatement.IsGoto && instruction.Target is not null)
                    EmitJump(instruction.Target);
            }

            private void AddAsmOperand(List<InlineAsmFormattedOperand> operands, Dictionary<string, int> namedOperands, InlineAsmFormattedOperand operand)
            {
                if (operand.Name is not null && !namedOperands.ContainsKey(operand.Name))
                    namedOperands.Add(operand.Name, operands.Count);
                operands.Add(operand);
            }

            private InlineAsmFormattedOperand CreateX86AsmOutputOperand(GimpleAsmOperand operand, LirVirtualRegister destination, LirInstruction instruction, List<Action> finalizers)
            {
                var size = RegisterSize(destination.Type);
                var fixedRegister = TryGetX86ConstraintRegister(operand.Constraint, destination.Type);
                if (fixedRegister.HasValue)
                {
                    if (operand.IsReadWrite)
                        LoadX86AsmOperandInto(LirOperand.ForRegister(destination), fixedRegister.Value, instruction);
                    finalizers.Add(() => StoreX86AsmOutput(destination, fixedRegister.Value));
                    return new InlineAsmFormattedOperand(operand.Name, modifier => X86Registers.Format(fixedRegister.Value, X86AsmRegisterSize(modifier, size)));
                }

                if (InlineAsmConstraints.HasExplicitRegister(operand.Constraint))
                    throw Unsupported(instruction, $"Invalid or unsupported x86 explicit register constraint '{operand.Constraint}'.");

                var register = GetWritableRegister(destination, PreferredScratch(destination.Type));
                finalizers.Add(() => StoreWritableRegisterIfSpilled(destination, register));
                return new InlineAsmFormattedOperand(operand.Name, modifier => X86Registers.Format(register, X86AsmRegisterSize(modifier, size)));
            }

            private InlineAsmFormattedOperand CreateX86AsmInputOperand(GimpleAsmOperand operand, LirOperand value, LirInstruction instruction)
            {
                var storage = InlineAsmConstraints.PreferredStorage(operand.Constraint, value.Type);
                if (storage == InlineAsmOperandStorage.Memory)
                    return new InlineAsmFormattedOperand(operand.Name, modifier => FormatX86AsmMemoryOperand(value, instruction));
                if (storage == InlineAsmOperandStorage.Immediate)
                    return new InlineAsmFormattedOperand(operand.Name, modifier => FormatX86AsmImmediate(value, instruction));

                var size = RegisterSize(value.Type);
                var fixedRegister = TryGetX86ConstraintRegister(operand.Constraint, value.Type);
                if (fixedRegister.HasValue)
                {
                    LoadX86AsmOperandInto(value, fixedRegister.Value, instruction);
                    return new InlineAsmFormattedOperand(operand.Name, modifier => X86Registers.Format(fixedRegister.Value, X86AsmRegisterSize(modifier, size)));
                }

                if (InlineAsmConstraints.HasExplicitRegister(operand.Constraint))
                    throw Unsupported(instruction, $"Invalid or unsupported x86 explicit register constraint '{operand.Constraint}'.");

                if (IsFloatType(value.Type))
                    return new InlineAsmFormattedOperand(
                        operand.Name, modifier => X86Registers.Format(LoadFloatingOperand(value, FpScratch1, instruction), X86AsmRegisterSize(modifier, size)));

                return new InlineAsmFormattedOperand(operand.Name, modifier => FormatX86AsmRegisterOperand(value, instruction, X86AsmRegisterSize(modifier, size)));
            }

            private void LoadX86AsmOperandInto(LirOperand operand, X86Register destination, LirInstruction instruction)
            {
                if (IsFloatType(operand.Type))
                {
                    var source = LoadFloatingOperand(operand, destination, instruction);
                    EmitFloatingMove(destination, source, operand.Type);
                    return;
                }

                LoadOperandInto(operand, destination, instruction, RegisterSize(operand.Type));
            }

            private void StoreX86AsmOutput(LirVirtualRegister destination, X86Register source)
            {
                var writable = GetWritableRegister(destination, source);
                if (writable != source)
                {
                    if (IsFloatType(destination.Type))
                        EmitFloatingMove(writable, source, destination.Type);
                    else
                        MoveRegister(writable, source, RegisterSize(destination.Type));
                }
                StoreWritableRegisterIfSpilled(destination, writable);
            }

            private string FormatX86AsmRegisterOperand(LirOperand operand, LirInstruction instruction, int size)
            {
                if (operand.Kind == LirOperandKind.Register && operand.Register is not null)
                {
                    var allocation = _allocation[operand.Register];
                    if (!allocation.IsSpilled)
                        return X86Registers.Format(ToX86Register(allocation.PhysicalRegister, _owner._machineTarget), size);
                }

                var register = LoadOperand(operand, Scratch1, instruction, size);
                return X86Registers.Format(register, size);
            }

            private string FormatX86AsmMemoryOperand(LirOperand operand, LirInstruction instruction)
            {
                if (operand.Kind == LirOperandKind.Address && operand.Address is not null)
                    return FormatX86AsmAddress(operand.Address, instruction);
                if (operand.Kind == LirOperandKind.StackSlot && operand.StackSlot is not null)
                    return FormatX86AsmStackMemory(_allocation.Frame.StackSlotOffsets[operand.StackSlot]);
                return "[" + FormatX86AsmRegisterOperand(operand, instruction, _wordSize) + "]";
            }

            private string FormatX86AsmAddress(LirAddress address, LirInstruction instruction)
            {
                switch (address.Kind)
                {
                    case LirAddressKind.StackSlot:
                        if (address.StackSlot is null)
                            throw Unsupported(instruction, "Inline assembly stack address is missing its stack slot.");
                        return FormatX86AsmStackMemory(_allocation.Frame.StackSlotOffsets[address.StackSlot]);
                    case LirAddressKind.Symbol:
                        if (address.Symbol is null)
                            throw Unsupported(instruction, "Inline assembly symbol address is missing its symbol.");
                        return "[" + _owner.GetSymbolLabel(address.Symbol) + "]";
                    case LirAddressKind.Indirect:
                        if (address.BaseOperand is null)
                            throw Unsupported(instruction, "Inline assembly indirect address is missing its base.");
                        return "[" + FormatX86AsmRegisterOperand(address.BaseOperand, instruction, _wordSize) + "]";
                    default:
                        var register = MaterializeAddress(address, Scratch0, instruction);
                        return "[" + X86Registers.Format(register, _wordSize) + "]";
                }
            }

            private string FormatX86AsmStackMemory(int offset)
            {
                var stackPointer = X86Registers.Format(X86Register.Rsp, _wordSize);
                if (offset == 0)
                    return "[" + stackPointer + "]";
                return offset < 0
                    ? "[" + stackPointer + " - " + (-offset).ToString(CultureInfo.InvariantCulture) + "]"
                    : "[" + stackPointer + " + " + offset.ToString(CultureInfo.InvariantCulture) + "]";
            }

            private string FormatX86AsmImmediate(LirOperand operand, LirInstruction instruction)
            {
                switch (operand.Kind)
                {
                    case LirOperandKind.Immediate:
                        return ConvertIntegerConstant(operand.Immediate).ToString(CultureInfo.InvariantCulture);
                    case LirOperandKind.Symbol when operand.Symbol is not null:
                        return _owner.GetSymbolLabel(operand.Symbol);
                    default:
                        return FormatX86AsmRegisterOperand(operand, instruction, RegisterSize(operand.Type));
                }
            }

            private X86Register? TryGetX86ConstraintRegister(string constraint, QualifiedType type)
            {
                var explicitRegister = InlineAsmConstraints.ExplicitRegisterName(constraint);
                if (explicitRegister is not null)
                {
                    if (TryParseX86ExplicitRegisterConstraint(explicitRegister, type, out var register))
                        return register;
                    return null;
                }

                if (IsFloatType(type))
                    return null;

                var text = InlineAsmConstraints.OperandConstraint(constraint);
                foreach (var ch in text)
                {
                    switch (ch)
                    {
                        case 'a': return X86Register.Rax;
                        case 'b': return X86Register.Rbx;
                        case 'c': return X86Register.Rcx;
                        case 'd': return X86Register.Rdx;
                        case 'S': return X86Register.Rsi;
                        case 'D': return X86Register.Rdi;
                    }
                }
                return null;
            }

            private bool TryParseX86ExplicitRegisterConstraint(string text, QualifiedType type, out X86Register register)
            {
                register = X86Register.Invalid;
                if (string.IsNullOrWhiteSpace(text))
                    return false;

                var registerClass = CAbi.PreferredLirRegisterClass(_owner._target, type);
                if (TargetRegisterInfo.TryParseExplicitRegister(_owner._target, text, registerClass, out var machineRegister))
                {
                    register = ToX86Register(machineRegister, _owner._machineTarget);
                    return true;
                }

                if (!TryParseX86ExplicitRegisterName(text, out register))
                    return false;

                if (_owner._machineTarget.Is32Bit)
                {
                    if (!X86Registers.IsGeneral(register) && !X86Registers.IsVector(register))
                        return false;
                    if (X86Registers.Index(register) >= 8)
                        return false;
                }

                if (!IsX86ExplicitRegisterAllowedForType(register, type))
                    return false;

                return IsUsableX86ExplicitRegister(register);
            }

            private bool IsX86ExplicitRegisterAllowedForType(X86Register register, QualifiedType type)
            {
                if (X86Registers.IsVector(register))
                    return IsFloatType(type);
                if (X86Registers.IsGeneral(register))
                    return !IsFloatType(type) && !RequiresBlockCopyStorage(type);
                return false;
            }

            private bool IsUsableX86ExplicitRegister(X86Register register)
            {
                if (register is X86Register.Rsp or X86Register.Rbp or X86Register.Rip)
                    return false;
                return X86Registers.IsGeneral(register) || X86Registers.IsVector(register);
            }

            private bool TryParseX86ExplicitRegisterName(string text, out X86Register register)
            {
                register = X86Register.Invalid;
                if (!X86Registers.TryParse(NormalizeX86ExplicitRegisterName(text), out var parsed, out _))
                    return false;

                register = X86Registers.IsYmm(parsed)
                    ? (X86Register)((int)X86Register.Xmm0 + X86Registers.Index(parsed))
                    : parsed;
                return true;
            }

            private static string NormalizeX86ExplicitRegisterName(string text)
            {
                text = text.Trim();
                if (text.Length >= 2 && text[0] == '{' && text[text.Length - 1] == '}')
                    text = text.Substring(1, text.Length - 2).Trim();
                while (text.StartsWith("%", StringComparison.Ordinal))
                    text = text.Substring(1);
                return text.ToLowerInvariant();
            }

            private int X86AsmRegisterSize(char? modifier, int defaultSize)
            {
                return modifier switch
                {
                    'b' => 1,
                    'w' => 2,
                    'k' => 4,
                    'q' => _wordSize,
                    _ => defaultSize,
                };
            }

            private string LabelName(LirBlock block)
                => _blockLabels.TryGetValue(block, out var label) ? label : _epilogueLabel;

            private void EmitInlineAssemblyText(LirInstruction instruction, string text)
            {
                try
                {
                    _owner._text.EmitAssembly(text, _owner.CreateLocalLabel(_functionLabel + "_asm"));
                }
                catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
                {
                    throw Unsupported(instruction, $"Invalid x86 inline assembly: {ex.Message}");
                }
            }

            private void EmitParameter(LirInstruction instruction)
            {
                if (instruction.Result is null)
                    return;

                var parameterIndex = FindParameterIndex(instruction.Operator);
                if (parameterIndex < 0)
                    throw Unsupported(instruction, "Cannot map parameter to function signature.");

                var functionType = _function.Symbol?.FunctionType;
                if (functionType is null)
                    throw Unsupported(instruction, "Parameter instruction requires function type.");

                var cursor = new AbiCursor();
                if (_allocation.Frame.HasHiddenReturnBuffer)
                    _ = CAbi.AssignHiddenReturnBufferLocation(_owner._target, ref cursor, _stackArgumentSlotSize);

                for (var i = 0; i <= parameterIndex; i++)
                {
                    var parameter = functionType.Parameters[i];
                    var value = CAbi.ClassifyValue(_owner._target, parameter.Type, isReturn: false, isVariadicUnnamedArgument: false);
                    if (i == parameterIndex)
                    {
                        if (value.PassingKind == AbiPassingKind.MultiRegister)
                            LoadParameterMultiRegisterValue(instruction.Result, value, ref cursor, instruction);
                        else
                        {
                            var location = CAbi.AssignArgumentLocation(value, ref cursor, _stackArgumentSlotSize);
                            if (value.PassingKind == AbiPassingKind.Indirect)
                                LoadParameterIndirectValue(instruction.Result, location, instruction);
                            else
                                LoadParameterValue(instruction.Result, location, instruction);
                        }
                        return;
                    }
                    _ = CAbi.AssignArgumentLocation(value, ref cursor, _stackArgumentSlotSize);
                }
            }

            private int FindParameterIndex(string name)
            {
                var functionType = _function.Symbol?.FunctionType;
                if (functionType is null)
                    return -1;
                for (var i = 0; i < functionType.Parameters.Length; i++)
                    if (string.Equals(functionType.Parameters[i].Name, name, StringComparison.Ordinal))
                        return i;
                return -1;
            }

            private void LoadParameterValue(LirVirtualRegister destination, AbiLocation location, LirInstruction instruction)
            {
                if (IsFloatType(destination.Type))
                {
                    var writable = GetWritableRegister(destination, FpScratch0);
                    if (location.Kind == AbiLocationKind.Register)
                        EmitFloatingMove(writable, ToX86Register(location.Register, _owner._machineTarget), destination.Type);
                    else if (location.Kind == AbiLocationKind.Stack)
                        EmitFloatingLoad(writable, Mem(X86Register.Rsp, IncomingStackOffset(location.StackByteOffset(_stackArgumentSlotSize)),
                            FloatingStorageSize(destination.Type)), destination.Type);
                    else
                        throw Unsupported(instruction, "Unsupported floating-point parameter ABI location.");
                    StoreWritableRegisterIfSpilled(destination, writable);
                    return;
                }
                var size = SizeOfStorage(destination.Type);
                if (RequiresBlockCopyStorage(destination.Type))
                {
                    var destinationAddress = MaterializeVirtualRegisterStorageAddress(destination, Scratch0);
                    if (location.Kind == AbiLocationKind.Stack)
                    {
                        EmitMemoryCopy(
                            Mem(X86Register.Rsp, IncomingStackOffset(location.StackByteOffset(_stackArgumentSlotSize)), size), RegMem(destinationAddress, size), size);
                        return;
                    }
                    if (location.Kind == AbiLocationKind.Register)
                    {
                        EmitStoreToMemory(ToX86Register(location.Register, _owner._machineTarget), destinationAddress, 0, Math.Min(size, _wordSize));
                        return;
                    }
                    throw Unsupported(instruction, "Unsupported parameter location for aggregate parameter.");
                }
                {
                    var writable = GetWritableRegister(destination, PreferredScratch(destination.Type));
                    if (location.Kind == AbiLocationKind.Register)
                    {
                        var source = ToX86Register(location.Register, _owner._machineTarget);
                        MoveRegister(writable, source, RegisterSize(destination.Type));
                    }
                    else if (location.Kind == AbiLocationKind.Stack)
                    {
                        EmitLoadFromMemory(writable, Mem(X86Register.Rsp, IncomingStackOffset(location.StackByteOffset(_stackArgumentSlotSize)),
                            RegisterSize(destination.Type)), IsSignedIntegerType(destination.Type));
                    }
                    else
                    {
                        throw Unsupported(instruction, "Unsupported parameter ABI location.");
                    }
                    StoreWritableRegisterIfSpilled(destination, writable);
                }
            }

            private void LoadParameterMultiRegisterValue(LirVirtualRegister destination, AbiValue value, ref AbiCursor cursor, LirInstruction instruction)
            {
                var destinationAddress = RequiresBlockCopyStorage(destination.Type)
                    ? MaterializeVirtualRegisterStorageAddress(destination, Scratch0)
                    : X86Register.Invalid;

                foreach (var segment in value.Segments)
                {
                    var location = CAbi.AssignSegmentArgumentLocation(segment, ref cursor, _stackArgumentSlotSize);
                    if (RequiresBlockCopyStorage(destination.Type))
                    {
                        if (location.Kind == AbiLocationKind.Register)
                            EmitStoreToMemory(ToX86Register(location.Register, _owner._machineTarget), destinationAddress, segment.Offset, Math.Min(segment.Size, _wordSize));
                        else if (location.Kind == AbiLocationKind.Stack)
                            EmitMemoryCopy(Mem(X86Register.Rsp, IncomingStackOffset(location.StackByteOffset(_stackArgumentSlotSize)), segment.Size),
                                Mem(destinationAddress, segment.Offset, segment.Size), segment.Size);
                        else
                            throw Unsupported(instruction, "Unsupported multi-register parameter segment location.");
                    }
                    else
                    {
                        if (segment.Offset != 0)
                            throw Unsupported(instruction, "Scalar parameter cannot receive multiple ABI segments.");
                        var scalarLocation = location;
                        LoadParameterValue(destination, scalarLocation, instruction);
                    }
                }
            }

            private void LoadParameterIndirectValue(LirVirtualRegister destination, AbiLocation location, LirInstruction instruction)
            {
                if (!RequiresBlockCopyStorage(destination.Type))
                {
                    LoadParameterValue(destination, location, instruction);
                    return;
                }

                var destinationAddress = MaterializeVirtualRegisterStorageAddress(destination, Scratch0);
                if (location.Kind == AbiLocationKind.Register)
                {
                    var pointer = ToX86Register(location.Register, _owner._machineTarget);
                    EmitMemoryCopy(RegMem(pointer, 1), RegMem(destinationAddress, 1), SizeOfStorage(destination.Type));
                    return;
                }
                if (location.Kind == AbiLocationKind.Stack)
                {
                    EmitLoadFromMemory(Scratch1, Mem(X86Register.Rsp, IncomingStackOffset(location.StackByteOffset(_stackArgumentSlotSize)), _wordSize), false);
                    EmitMemoryCopy(RegMem(Scratch1, 1), RegMem(destinationAddress, 1), SizeOfStorage(destination.Type));
                    return;
                }
                throw Unsupported(instruction, "Unsupported indirect parameter location.");
            }

            private void EmitCopy(LirInstruction instruction)
            {
                if (instruction.Result is null || instruction.Operands.Length == 0)
                    return;
                EmitValueCopy(instruction.Result, instruction.Operands[0], instruction);
            }

            private void EmitParallelCopy(LirInstruction instruction)
            {
                if (instruction.ParallelCopies.Length == 0)
                    return;

                var tempOffset = _allocation.Frame.ParallelCopyTempOffset;
                var cursor = 0;
                foreach (var copy in instruction.ParallelCopies)
                {
                    var size = Math.Max(SizeOfStorage(copy.Destination.Type), SizeOfStorage(copy.Source.Type));
                    var temp = Mem(X86Register.Rsp, tempOffset + cursor, size);
                    EmitOperandToMemory(copy.Source, temp, size, instruction);
                    cursor += AlignUp(Math.Max(size, _owner._allocationOptions.SpillSlotSize), _owner._allocationOptions.SpillSlotAlignment);
                }

                cursor = 0;
                foreach (var copy in instruction.ParallelCopies)
                {
                    var size = Math.Max(SizeOfStorage(copy.Destination.Type), SizeOfStorage(copy.Source.Type));
                    EmitMemoryToDestination(Mem(X86Register.Rsp, tempOffset + cursor, size), copy.Destination, size);
                    cursor += AlignUp(Math.Max(size, _owner._allocationOptions.SpillSlotSize), _owner._allocationOptions.SpillSlotAlignment);
                }
            }

            private void EmitZero(LirInstruction instruction)
            {
                if (instruction.Result is null)
                    return;
                if (IsFloatType(instruction.Result.Type))
                {
                    var writable = GetWritableRegister(instruction.Result, FpScratch0);
                    EmitFloatingZero(writable, instruction.Result.Type);
                    StoreWritableRegisterIfSpilled(instruction.Result, writable);
                    return;
                }

                if (RequiresBlockCopyStorage(instruction.Result.Type))
                {
                    var destination = MaterializeVirtualRegisterStorageAddress(instruction.Result, Scratch0);
                    ZeroMemory(RegMem(destination, 1), SizeOfStorage(instruction.Result.Type));
                    return;
                }

                {
                    var writable = GetWritableRegister(instruction.Result, PreferredScratch(instruction.Result.Type));
                    Emit(X86Instruction.Binary(X86InstrKind.Xor, Reg(writable, RegisterSize(instruction.Result.Type)), Reg(writable, RegisterSize(instruction.Result.Type))));
                    StoreWritableRegisterIfSpilled(instruction.Result, writable);
                }
            }

            private void EmitUnary(LirInstruction instruction)
            {
                if (instruction.Result is null || instruction.Operands.Length != 1)
                    throw Unsupported(instruction, "Unary instruction expects one operand and result.");
                if (IsFloatType(instruction.Result.Type) || IsFloatType(instruction.Operands[0].Type))
                {
                    EmitFloatingUnary(instruction);
                    return;
                }

                var size = RegisterSize(instruction.Result.Type);
                var dst = GetWritableRegister(instruction.Result, Scratch0);
                LoadOperandInto(instruction.Operands[0], dst, instruction, size);
                switch (instruction.Operator)
                {
                    case "+":
                        break;
                    case "-":
                        Emit(X86Instruction.Unary(X86InstrKind.Neg, Reg(dst, size)));
                        break;
                    case "~":
                        Emit(X86Instruction.Unary(X86InstrKind.Not, Reg(dst, size)));
                        break;
                    case "!":
                        Emit(X86Instruction.Binary(X86InstrKind.Test, Reg(dst, size), Reg(dst, size)));
                        Emit(X86Instruction.Setcc(X86Condition.E, Reg(dst, 1)));
                        Emit(X86Instruction.Binary(X86InstrKind.Movzx, Reg(dst, 4), Reg(dst, 1)));
                        break;
                    default:
                        throw Unsupported(instruction, $"Unsupported unary operator '{instruction.Operator}'.");
                }
                NormalizeIntegerRegister(dst, instruction.Result.Type);
                StoreWritableRegisterIfSpilled(instruction.Result, dst);
            }

            private void EmitBinary(LirInstruction instruction)
            {
                if (instruction.Result is null || instruction.Operands.Length != 2)
                    throw Unsupported(instruction, "Binary instruction expects two operands and result.");
                if (IsFloatType(instruction.Result.Type) || IsFloatType(instruction.Operands[0].Type) || IsFloatType(instruction.Operands[1].Type))
                {
                    EmitFloatingBinary(instruction);
                    return;
                }
                if (RequiresBlockCopyStorage(instruction.Result.Type))
                    throw Unsupported(instruction, "Wide scalar or aggregate binary emission is not supported by X86CodeGenerator.");

                var left = instruction.Operands[0];
                var right = instruction.Operands[1];
                var size = RegisterSize(instruction.Result.Type);
                var dst = GetWritableRegister(instruction.Result, Scratch0);

                switch (instruction.Operator)
                {
                    case "+":
                        LoadOperandInto(left, dst, instruction, size);
                        if (IsPointerLike(left.Type) && IsIntegerLike(right.Type))
                            EmitScaledAdd(dst, right, PointerScale(left.Type), instruction, size);
                        else if (IsIntegerLike(left.Type) && IsPointerLike(right.Type))
                        {
                            EmitScaledAdd(dst, right, 1, instruction, size);
                            if (PointerScale(right.Type) != 1)
                                throw Unsupported(instruction, "Integer plus pointer with scaled integer lhs is not represented by this LIR shape.");
                        }
                        else
                            Emit(X86Instruction.Binary(X86InstrKind.Add, Reg(dst, size), LoadOperandForIntegerOperation(right, Scratch1, instruction, size)));
                        break;
                    case "-":
                        LoadOperandInto(left, dst, instruction, size);
                        EmitScaledSub(dst, right, IsPointerLike(left.Type) && IsIntegerLike(right.Type) ? PointerScale(left.Type) : 1, instruction, size);
                        break;
                    case "&":
                        EmitCommutativeBinary(X86InstrKind.And, left, right, dst, instruction, size);
                        break;
                    case "|":
                        EmitCommutativeBinary(X86InstrKind.Or, left, right, dst, instruction, size);
                        break;
                    case "^":
                        EmitCommutativeBinary(X86InstrKind.Xor, left, right, dst, instruction, size);
                        break;
                    case "*":
                        LoadOperandInto(left, dst, instruction, size);
                        var multiplier = LoadOperandForRead(right, Scratch1, instruction, size);
                        if (multiplier.Kind == X86OperandKind.Immediate && FitsSignedInt32(multiplier.Immediate))
                            Emit(X86Instruction.Ternary(X86InstrKind.Imul, Reg(dst, size), Reg(dst, size), multiplier));
                        else
                        {
                            if (multiplier.Kind == X86OperandKind.Immediate || multiplier.Kind == X86OperandKind.Symbol)
                            {
                                MoveIntoRegister(Scratch1, multiplier, size);
                                multiplier = Reg(Scratch1, size);
                            }
                            Emit(X86Instruction.Binary(X86InstrKind.Imul, Reg(dst, size), multiplier));
                        }
                        break;
                    case "/":
                    case "%":
                        EmitDivide(instruction, signed: IsSignedIntegerType(left.Type), wantRemainder: instruction.Operator == "%");
                        return;
                    case "<<":
                        EmitShift(instruction, X86InstrKind.Shl);
                        return;
                    case ">>":
                        EmitShift(instruction, IsSignedIntegerType(left.Type) ? X86InstrKind.Sar : X86InstrKind.Shr);
                        return;
                    case "==":
                    case "!=":
                    case "<":
                    case "<=":
                    case ">":
                    case ">=":
                        EmitComparison(instruction);
                        return;
                    default:
                        throw Unsupported(instruction, $"Unsupported binary operator '{instruction.Operator}'.");
                }

                NormalizeIntegerRegister(dst, instruction.Result.Type);
                StoreWritableRegisterIfSpilled(instruction.Result, dst);
            }

            private void EmitCommutativeBinary(X86InstrKind opcode, LirOperand left, LirOperand right, X86Register dst, LirInstruction instruction, int size)
            {
                LoadOperandInto(left, dst, instruction, size);
                Emit(X86Instruction.Binary(opcode, Reg(dst, size), LoadOperandForIntegerOperation(right, Scratch1, instruction, size)));
            }

            private void EmitScaledAdd(X86Register dst, LirOperand right, int scale, LirInstruction instruction, int size)
            {
                if (scale == 1)
                {
                    Emit(X86Instruction.Binary(X86InstrKind.Add, Reg(dst, size), LoadOperandForIntegerOperation(right, Scratch1, instruction, size)));
                    return;
                }

                LoadOperandInto(right, Scratch1, instruction, size);
                Emit(X86Instruction.Ternary(X86InstrKind.Imul, Reg(Scratch1, size), Reg(Scratch1, size), Imm(scale)));
                Emit(X86Instruction.Binary(X86InstrKind.Add, Reg(dst, size), Reg(Scratch1, size)));
            }

            private void EmitScaledSub(X86Register dst, LirOperand right, int scale, LirInstruction instruction, int size)
            {
                if (scale == 1)
                {
                    Emit(X86Instruction.Binary(X86InstrKind.Sub, Reg(dst, size), LoadOperandForIntegerOperation(right, Scratch1, instruction, size)));
                    return;
                }

                LoadOperandInto(right, Scratch1, instruction, size);
                Emit(X86Instruction.Ternary(X86InstrKind.Imul, Reg(Scratch1, size), Reg(Scratch1, size), Imm(scale)));
                Emit(X86Instruction.Binary(X86InstrKind.Sub, Reg(dst, size), Reg(Scratch1, size)));
            }

            private void EmitDivide(LirInstruction instruction, bool signed, bool wantRemainder)
            {
                var size = RegisterSize(instruction.Operands[0].Type);
                LoadOperandInto(instruction.Operands[0], X86Register.Rax, instruction, size);
                var divisor = LoadOperandForRead(instruction.Operands[1], X86Register.Rcx, instruction, size);
                if (divisor.Kind == X86OperandKind.Immediate || divisor.Kind == X86OperandKind.Symbol)
                {
                    MoveIntoRegister(X86Register.Rcx, divisor, size);
                    divisor = Reg(X86Register.Rcx, size);
                }

                if (signed)
                {
                    if (size == 8)
                        Emit(new X86Instruction(X86InstrKind.Cqo));
                    else
                        Emit(new X86Instruction(X86InstrKind.Cdq));
                    Emit(X86Instruction.Unary(X86InstrKind.Idiv, divisor));
                }
                else
                {
                    Emit(X86Instruction.Binary(X86InstrKind.Xor, Reg(X86Register.Rdx, size), Reg(X86Register.Rdx, size)));
                    Emit(X86Instruction.Unary(X86InstrKind.Div, divisor));
                }

                var dst = GetWritableRegister(instruction.Result!, wantRemainder ? X86Register.Rdx : X86Register.Rax);
                MoveRegister(dst, wantRemainder ? X86Register.Rdx : X86Register.Rax, size);
                NormalizeIntegerRegister(dst, instruction.Result!.Type);
                StoreWritableRegisterIfSpilled(instruction.Result!, dst);
            }

            private void EmitShift(LirInstruction instruction, X86InstrKind opcode)
            {
                var size = RegisterSize(instruction.Operands[0].Type);
                var dst = GetWritableRegister(instruction.Result!, Scratch0);
                LoadOperandInto(instruction.Operands[0], dst, instruction, size);
                if (instruction.Operands[1].Kind == LirOperandKind.Immediate)
                    Emit(X86Instruction.Binary(opcode, Reg(dst, size), Imm(ConvertIntegerConstant(instruction.Operands[1].Immediate) & 0x3f)));
                else
                {
                    LoadOperandInto(instruction.Operands[1], X86Register.Rcx, instruction, 1);
                    Emit(X86Instruction.Binary(opcode, Reg(dst, size), Reg(X86Register.Rcx, 1)));
                }
                NormalizeIntegerRegister(dst, instruction.Result!.Type);
                StoreWritableRegisterIfSpilled(instruction.Result!, dst);
            }

            private void EmitComparison(LirInstruction instruction)
            {
                var left = instruction.Operands[0];
                var right = instruction.Operands[1];
                var size = Math.Max(RegisterSize(left.Type), RegisterSize(right.Type));
                LoadOperandInto(left, Scratch0, instruction, size);
                Emit(X86Instruction.Binary(X86InstrKind.Cmp, Reg(Scratch0, size), LoadOperandForIntegerOperation(right, Scratch1, instruction, size)));
                var condition = instruction.Operator switch
                {
                    "==" => X86Condition.E,
                    "!=" => X86Condition.Ne,
                    "<" => IsSignedIntegerType(left.Type) ? X86Condition.L : X86Condition.B,
                    "<=" => IsSignedIntegerType(left.Type) ? X86Condition.Le : X86Condition.Be,
                    ">" => IsSignedIntegerType(left.Type) ? X86Condition.G : X86Condition.A,
                    ">=" => IsSignedIntegerType(left.Type) ? X86Condition.Ge : X86Condition.Ae,
                    _ => X86Condition.E,
                };
                var dst = GetWritableRegister(instruction.Result!, Scratch0);
                Emit(X86Instruction.Setcc(condition, Reg(dst, 1)));
                Emit(X86Instruction.Binary(X86InstrKind.Movzx, Reg(dst, 4), Reg(dst, 1)));
                StoreWritableRegisterIfSpilled(instruction.Result!, dst);
            }

            private void EmitFloatingUnary(LirInstruction instruction)
            {
                var operand = instruction.Operands[0];
                var result = instruction.Result!;
                if (IsLongDouble(operand.Type) || IsLongDouble(result.Type))
                    throw Unsupported(instruction, "x86 backend does not support long double code generation.");

                if (instruction.Operator == "!")
                {
                    var dst = GetWritableRegister(result, Scratch0);
                    var source = LoadFloatingOperand(operand, FpScratch0, instruction);
                    EmitFloatingRelation("==", operand.Type, source, LoadFloatingZero(FpScratch1, operand.Type), dst);
                    StoreWritableRegisterIfSpilled(result, dst);
                    return;
                }

                if (!IsFloatType(result.Type) || !IsFloatType(operand.Type))
                    throw Unsupported(instruction, "Unsupported mixed floating-point unary operator.");

                var writable = GetWritableRegister(result, FpScratch0);
                var sourceReg = LoadFloatingOperand(operand, writable, instruction);
                if (instruction.Operator == "+")
                {
                    EmitFloatingPrecisionMove(writable, sourceReg, operand.Type, result.Type);
                    StoreWritableRegisterIfSpilled(result, writable);
                    return;
                }

                if (instruction.Operator == "-")
                {
                    EmitFloatingPrecisionMove(writable, sourceReg, operand.Type, result.Type);
                    EmitFloatingNegate(writable, result.Type);
                    StoreWritableRegisterIfSpilled(result, writable);
                    return;
                }

                throw Unsupported(instruction, $"Unsupported unary operator '{instruction.Operator}'.");
            }

            private void EmitFloatingBinary(LirInstruction instruction)
            {
                var left = instruction.Operands[0];
                var right = instruction.Operands[1];
                var result = instruction.Result!;
                if (IsLongDouble(left.Type) || IsLongDouble(right.Type) || IsLongDouble(result.Type))
                    throw Unsupported(instruction, "x86 backend does not support long double code generation.");

                switch (instruction.Operator)
                {
                    case "+":
                    case "-":
                    case "*":
                    case "/":
                        if (!IsFloatType(result.Type))
                            throw Unsupported(instruction, "Floating-point arithmetic result must have a floating-point type.");
                        var operationType = SelectFloatingOperationType(left.Type, right.Type, result.Type);
                        var writable = GetWritableRegister(result, FpScratch0);
                        var leftReg = LoadOperandAsFloating(left, operationType, writable, instruction);
                        if (writable != leftReg)
                            EmitFloatingMove(writable, leftReg, operationType);
                        var rightReg = LoadOperandAsFloating(right, operationType, FpScratch1, instruction);
                        Emit(X86Instruction.Binary(FloatingArithmeticOpcode(instruction.Operator, operationType),
                            Reg(writable, FloatingStorageSize(operationType)), Reg(rightReg, FloatingStorageSize(operationType))));
                        if (!SameFloatingType(operationType, result.Type))
                            EmitFloatingPrecisionConversion(writable, writable, operationType, result.Type);
                        StoreWritableRegisterIfSpilled(result, writable);
                        return;
                    case "==":
                    case "!=":
                    case "<":
                    case "<=":
                    case ">":
                    case ">=":
                        var comparisonType = SelectFloatingOperationType(left.Type, right.Type, IsFloatType(result.Type) ? left.Type : result.Type);
                        var leftCmp = LoadOperandAsFloating(left, comparisonType, FpScratch0, instruction);
                        var rightCmp = LoadOperandAsFloating(right, comparisonType, FpScratch1, instruction);
                        var dst = GetWritableRegister(result, Scratch0);
                        EmitFloatingRelation(instruction.Operator, comparisonType, leftCmp, rightCmp, dst);
                        StoreWritableRegisterIfSpilled(result, dst);
                        return;
                    default:
                        throw Unsupported(instruction, $"Unsupported binary operator '{instruction.Operator}'.");
                }
            }

            private void EmitFloatingOrMixedConvert(LirInstruction instruction)
            {
                var result = instruction.Result!;
                var source = instruction.Operands[0];
                if (IsLongDouble(result.Type) || IsLongDouble(source.Type))
                    throw Unsupported(instruction, "x86 backend does not support long double code generation.");

                if (IsFloatType(result.Type))
                {
                    var writable = GetWritableRegister(result, FpScratch0);
                    if (IsFloatType(source.Type))
                    {
                        var sourceReg = LoadFloatingOperand(source, writable, instruction);
                        EmitFloatingPrecisionMove(writable, sourceReg, source.Type, result.Type);
                    }
                    else if (IsIntegerLike(source.Type) || IsPointerLike(source.Type))
                    {
                        EmitIntegerToFloating(source, writable, result.Type, instruction);
                    }
                    else
                    {
                        throw Unsupported(instruction, "Unsupported conversion to floating-point type.");
                    }
                    StoreWritableRegisterIfSpilled(result, writable);
                    return;
                }

                if (IsFloatType(source.Type))
                {
                    var sourceReg = LoadFloatingOperand(source, FpScratch0, instruction);
                    var dst = GetWritableRegister(result, Scratch0);
                    if (IsIntegerLike(result.Type) || IsPointerLike(result.Type))
                        EmitFloatingToInteger(sourceReg, source.Type, dst, result.Type, instruction);
                    else
                        throw Unsupported(instruction, "Unsupported conversion from floating-point type.");
                    StoreWritableRegisterIfSpilled(result, dst);
                    return;
                }

                EmitValueCopy(result, source, instruction);
            }

            private X86Register LoadFloatingOperand(LirOperand operand, X86Register scratch, LirInstruction instruction)
            {
                var type = operand.Type;
                if (IsLongDouble(type))
                    throw Unsupported(instruction, "x86 backend does not support long double code generation.");
                if (operand.Kind is LirOperandKind.Undefined or LirOperandKind.Void or LirOperandKind.None)
                    return LoadFloatingZero(scratch, type);
                if (operand.Kind == LirOperandKind.Immediate)
                {
                    LoadFloatingImmediate(scratch, operand.Immediate, type);
                    return scratch;
                }

                var size = FloatingStorageSize(type);
                var source = LoadOperandForRead(operand, scratch, instruction, size);
                if (source.Kind == X86OperandKind.Register)
                    return source.Register;
                if (source.Kind == X86OperandKind.Memory)
                {
                    EmitFloatingLoad(scratch, source, type);
                    return scratch;
                }
                throw Unsupported(instruction, $"Unsupported floating-point operand kind: {operand.Kind}.");
            }

            private X86Register LoadOperandAsFloating(LirOperand operand, QualifiedType destinationType, X86Register destination, LirInstruction instruction)
            {
                if (IsFloatType(operand.Type))
                {
                    var source = LoadFloatingOperand(operand, destination, instruction);
                    EmitFloatingPrecisionMove(destination, source, operand.Type, destinationType);
                    return destination;
                }

                if (IsIntegerLike(operand.Type) || IsPointerLike(operand.Type))
                {
                    EmitIntegerToFloating(operand, destination, destinationType, instruction);
                    return destination;
                }

                throw Unsupported(instruction, "Operand cannot be converted to floating-point.");
            }

            private void EmitIntegerToFloating(LirOperand operand, X86Register destination, QualifiedType destinationType, LirInstruction instruction)
            {
                var sourceSize = RegisterSize(operand.Type);
                var convertSize = sourceSize < 4 ? 4 : sourceSize;
                var isUnsigned = IsUnsignedIntegerType(operand.Type) || IsPointerLike(operand.Type);
                if (isUnsigned && sourceSize == 4)
                {
                    if (_wordSize < 8)
                        throw Unsupported(instruction, "x86 unsigned 32-bit integer to floating-point conversion requires 64-bit integer registers.");
                    convertSize = 8;
                }
                else if (isUnsigned && sourceSize >= 8)
                {
                    throw Unsupported(instruction, "Unsigned 64-bit integer to floating-point conversion is not implemented.");
                }

                LoadOperandInto(operand, Scratch0, instruction, convertSize);
                Emit(X86Instruction.Binary(IsFloat32(destinationType)
                    ? X86InstrKind.Cvtsi2ss
                    : X86InstrKind.Cvtsi2sd, Reg(destination, FloatingStorageSize(destinationType)), Reg(Scratch0, convertSize)));
            }

            private void EmitFloatingToInteger(X86Register source, QualifiedType sourceType, X86Register destination, QualifiedType destinationType, LirInstruction instruction)
            {
                if (destinationType.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Bool })
                {
                    EmitFloatingTruthValue(source, sourceType, destination);
                    return;
                }

                var destinationSize = RegisterSize(destinationType);
                var convertSize = destinationSize < 4 ? 4 : destinationSize;
                if (convertSize > _wordSize)
                    throw Unsupported(instruction, "Floating-point conversion target does not fit a native integer register.");
                if ((IsUnsignedIntegerType(destinationType) || IsPointerLike(destinationType)) && SizeOfStorage(destinationType) >= 4)
                    throw Unsupported(instruction, "Floating-point to unsigned 32/64-bit conversion is not implemented by X86CodeGenerator.");

                Emit(X86Instruction.Binary(IsFloat32(sourceType)
                    ? X86InstrKind.Cvttss2si
                    : X86InstrKind.Cvttsd2si, Reg(destination, convertSize), Reg(source, FloatingStorageSize(sourceType))));
                NormalizeIntegerRegister(destination, destinationType);
            }

            private void EmitFloatingTruthValue(X86Register source, QualifiedType sourceType, X86Register destination)
            {
                Emit(X86Instruction.Binary(FloatingCompareOpcode(sourceType), Reg(source, FloatingStorageSize(sourceType)),
                    Reg(LoadFloatingZero(FpScratch2, sourceType), FloatingStorageSize(sourceType))));
                Emit(X86Instruction.Setcc(X86Condition.Ne, Reg(destination, 1)));
                Emit(X86Instruction.Setcc(X86Condition.P, Reg(Scratch1, 1)));
                Emit(X86Instruction.Binary(X86InstrKind.Or, Reg(destination, 1), Reg(Scratch1, 1)));
                Emit(X86Instruction.Binary(X86InstrKind.Movzx, Reg(destination, 4), Reg(destination, 1)));
            }

            private void EmitFloatingRelation(string op, QualifiedType type, X86Register left, X86Register right, X86Register destination)
            {
                Emit(X86Instruction.Binary(FloatingCompareOpcode(type), Reg(left, FloatingStorageSize(type)), Reg(right, FloatingStorageSize(type))));
                switch (op)
                {
                    case "==":
                        EmitOrderedSet(X86Condition.E, destination, requireOrdered: true);
                        return;
                    case "!=":
                        Emit(X86Instruction.Setcc(X86Condition.Ne, Reg(destination, 1)));
                        Emit(X86Instruction.Setcc(X86Condition.P, Reg(Scratch1, 1)));
                        Emit(X86Instruction.Binary(X86InstrKind.Or, Reg(destination, 1), Reg(Scratch1, 1)));
                        Emit(X86Instruction.Binary(X86InstrKind.Movzx, Reg(destination, 4), Reg(destination, 1)));
                        return;
                    case "<":
                        EmitOrderedSet(X86Condition.B, destination, requireOrdered: true);
                        return;
                    case "<=":
                        EmitOrderedSet(X86Condition.Be, destination, requireOrdered: true);
                        return;
                    case ">":
                        EmitOrderedSet(X86Condition.A, destination, requireOrdered: false);
                        return;
                    case ">=":
                        EmitOrderedSet(X86Condition.Ae, destination, requireOrdered: false);
                        return;
                    default:
                        throw new NotSupportedException($"Unsupported floating-point comparison operator: {op}.");
                }
            }

            private void EmitOrderedSet(X86Condition condition, X86Register destination, bool requireOrdered)
            {
                Emit(X86Instruction.Setcc(condition, Reg(destination, 1)));
                if (requireOrdered)
                {
                    Emit(X86Instruction.Setcc(X86Condition.Np, Reg(Scratch1, 1)));
                    Emit(X86Instruction.Binary(X86InstrKind.And, Reg(destination, 1), Reg(Scratch1, 1)));
                }
                Emit(X86Instruction.Binary(X86InstrKind.Movzx, Reg(destination, 4), Reg(destination, 1)));
            }

            private void EmitFloatingPrecisionMove(X86Register destination, X86Register source, QualifiedType sourceType, QualifiedType destinationType)
            {
                if (SameFloatingType(sourceType, destinationType))
                    EmitFloatingMove(destination, source, destinationType);
                else
                    EmitFloatingPrecisionConversion(destination, source, sourceType, destinationType);
            }

            private void EmitFloatingPrecisionConversion(X86Register destination, X86Register source, QualifiedType sourceType, QualifiedType destinationType)
            {
                if (SameFloatingType(sourceType, destinationType))
                {
                    EmitFloatingMove(destination, source, destinationType);
                    return;
                }
                if (IsFloat32(sourceType) && IsFloat64(destinationType))
                {
                    Emit(X86Instruction.Binary(X86InstrKind.Cvtss2sd, Reg(destination, 8), Reg(source, 4)));
                    return;
                }
                if (IsFloat64(sourceType) && IsFloat32(destinationType))
                {
                    Emit(X86Instruction.Binary(X86InstrKind.Cvtsd2ss, Reg(destination, 4), Reg(source, 8)));
                    return;
                }
                throw new NotSupportedException("Unsupported floating-point precision conversion.");
            }

            private X86Register LoadFloatingZero(X86Register destination, QualifiedType type)
            {
                EmitFloatingZero(destination, type);
                return destination;
            }

            private void EmitFloatingZero(X86Register destination, QualifiedType type)
                => Emit(X86Instruction.Binary(FloatingXorOpcode(type), Reg(destination, 16), Reg(destination, 16)));

            private void LoadFloatingImmediate(X86Register destination, object? value, QualifiedType type)
            {
                var label = _owner.CreateFloatingLiteral(type, value);
                EmitSymbolAddress(Scratch1, label);
                EmitFloatingLoad(destination, Mem(Scratch1, 0, FloatingStorageSize(type)), type);
            }

            private void EmitFloatingNegate(X86Register destination, QualifiedType type)
            {
                var bits = IsFloat32(type) ? 0x80000000UL : 0x8000000000000000UL;
                var label = _owner.CreateFloatingBitsLiteral(type, bits);
                EmitSymbolAddress(Scratch1, label);
                EmitFloatingLoad(FpScratch2, Mem(Scratch1, 0, FloatingStorageSize(type)), type);
                Emit(X86Instruction.Binary(FloatingXorOpcode(type), Reg(destination, 16), Reg(FpScratch2, 16)));
            }

            private void EmitFloatingMove(X86Register destination, X86Register source, QualifiedType type)
            {
                if (destination == source)
                    return;
                Emit(X86Instruction.Binary(FloatingMoveOpcode(type), Reg(destination, FloatingStorageSize(type)), Reg(source, FloatingStorageSize(type))));
            }

            private void EmitFloatingLoad(X86Register destination, X86Operand source, QualifiedType type)
                => Emit(X86Instruction.Binary(FloatingMoveOpcode(type), Reg(destination, FloatingStorageSize(type)), source.WithSize(FloatingStorageSize(type))));

            private void EmitFloatingStore(X86Operand destination, X86Register source, QualifiedType type)
                => Emit(X86Instruction.Binary(FloatingMoveOpcode(type), destination.WithSize(FloatingStorageSize(type)), Reg(source, FloatingStorageSize(type))));

            private QualifiedType SelectFloatingOperationType(QualifiedType left, QualifiedType right, QualifiedType fallback)
            {
                if (IsFloat64(left) || IsFloat64(right) || IsFloat64(fallback))
                    return IsFloat64(left) ? left : IsFloat64(right) ? right : fallback;
                if (IsFloat32(left))
                    return left;
                if (IsFloat32(right))
                    return right;
                if (IsFloat32(fallback) || IsFloat64(fallback))
                    return fallback;
                throw new NotSupportedException("Floating-point operation has no floating-point operand.");
            }

            private static bool SameFloatingType(QualifiedType left, QualifiedType right)
                => (IsFloat32(left) && IsFloat32(right)) || (IsFloat64(left) && IsFloat64(right));

            private int FloatingStorageSize(QualifiedType type)
            {
                if (IsFloat32(type))
                    return 4;
                if (IsFloat64(type))
                    return 8;
                throw new NotSupportedException("x86 backend does not support long double code generation.");
            }

            private static X86InstrKind FloatingMoveOpcode(QualifiedType type)
                => IsFloat32(type) ? X86InstrKind.Movss : X86InstrKind.Movsd;

            private static X86InstrKind FloatingCompareOpcode(QualifiedType type)
                => IsFloat32(type) ? X86InstrKind.Ucomiss : X86InstrKind.Ucomisd;

            private static X86InstrKind FloatingXorOpcode(QualifiedType type)
                => IsFloat32(type) ? X86InstrKind.Xorps : X86InstrKind.Xorpd;

            private static X86InstrKind FloatingArithmeticOpcode(string op, QualifiedType type)
            {
                var isFloat32 = IsFloat32(type);
                return op switch
                {
                    "+" => isFloat32 ? X86InstrKind.Addss : X86InstrKind.Addsd,
                    "-" => isFloat32 ? X86InstrKind.Subss : X86InstrKind.Subsd,
                    "*" => isFloat32 ? X86InstrKind.Mulss : X86InstrKind.Mulsd,
                    "/" => isFloat32 ? X86InstrKind.Divss : X86InstrKind.Divsd,
                    _ => throw new ArgumentOutOfRangeException(nameof(op)),
                };
            }

            private void EmitConvert(LirInstruction instruction)
            {
                if (instruction.Result is null || instruction.Operands.Length == 0)
                    return;
                if (IsFloatType(instruction.Result.Type) || IsFloatType(instruction.Operands[0].Type))
                {
                    EmitFloatingOrMixedConvert(instruction);
                    return;
                }
                EmitValueCopy(instruction.Result, instruction.Operands[0], instruction);
            }

            private void EmitAddressOf(LirInstruction instruction)
            {
                if (instruction.Result is null || instruction.Address is null)
                    throw Unsupported(instruction, "AddressOf instruction expects result and address.");
                var dst = GetWritableRegister(instruction.Result, Scratch0);
                MaterializeAddress(instruction.Address, dst, instruction);
                StoreWritableRegisterIfSpilled(instruction.Result, dst);
            }

            private void EmitLoad(LirInstruction instruction)
            {
                if (instruction.Result is null || instruction.Address is null)
                    throw Unsupported(instruction, "Load instruction expects result and address.");
                if (IsFloatType(instruction.Result.Type))
                {
                    var fpDst = GetWritableRegister(instruction.Result, FpScratch0);
                    var fpAddress = MaterializeAddress(instruction.Address, Scratch1, instruction);
                    EmitFloatingLoad(fpDst, RegMem(fpAddress, FloatingStorageSize(instruction.Result.Type)), instruction.Result.Type);
                    StoreWritableRegisterIfSpilled(instruction.Result, fpDst);
                    return;
                }

                var size = SizeOfStorage(instruction.Result.Type);
                if (RequiresBlockCopyStorage(instruction.Result.Type))
                {
                    var destination = MaterializeVirtualRegisterStorageAddress(instruction.Result, Scratch0);
                    var source = MaterializeAddress(instruction.Address, Scratch1, instruction);
                    EmitMemoryCopy(RegMem(source, 1), RegMem(destination, 1), size);
                    return;
                }

                var dst = GetWritableRegister(instruction.Result, PreferredScratch(instruction.Result.Type));
                var address = MaterializeAddress(instruction.Address, Scratch1, instruction);
                EmitLoadFromMemory(dst, RegMem(address, RegisterSize(instruction.Result.Type)), IsSignedIntegerType(instruction.Result.Type));
                StoreWritableRegisterIfSpilled(instruction.Result, dst);
            }

            private void EmitStore(LirInstruction instruction)
            {
                if (instruction.Address is null || instruction.Operands.Length == 0)
                    throw Unsupported(instruction, "Store instruction expects address and value operand.");

                var value = instruction.Operands[0];
                if (IsFloatType(value.Type))
                {
                    var fpDestination = MaterializeAddress(instruction.Address, Scratch0, instruction);
                    var fpSource = LoadFloatingOperand(value, FpScratch0, instruction);
                    EmitFloatingStore(RegMem(fpDestination, FloatingStorageSize(value.Type)), fpSource, value.Type);
                    return;
                }
                var size = SizeOfStorage(value.Type);
                var destination = MaterializeAddress(instruction.Address, Scratch0, instruction);
                if (RequiresBlockCopyStorage(value.Type))
                {
                    EmitOperandToMemory(value, RegMem(destination, size), size, instruction);
                    return;
                }

                var scalarSize = RegisterSize(value.Type);
                var source = LoadOperandForRead(value, Scratch1, instruction, scalarSize);
                if (source.Kind == X86OperandKind.Memory || (scalarSize == 8 && (source.Kind == X86OperandKind.Immediate || source.Kind == X86OperandKind.Symbol)))
                {
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(Scratch1, scalarSize), source));
                    source = Reg(Scratch1, scalarSize);
                }
                Emit(X86Instruction.Binary(X86InstrKind.Mov, RegMem(destination, scalarSize), source));
            }

            private void EmitZeroMemory(LirInstruction instruction)
            {
                if (instruction.Address is null)
                    throw Unsupported(instruction, "ZeroMemory instruction expects address.");
                var size = instruction.Operands.Length == 0 ? SizeOfStorage(instruction.Address.ElementType) : ImmediateToInt32(instruction.Operands[0]);
                var address = MaterializeAddress(instruction.Address, Scratch0, instruction);
                ZeroMemory(RegMem(address, 1), size);
            }

            private void EmitCall(LirInstruction instruction)
            {
                if (instruction.Operands.Length == 0)
                    throw Unsupported(instruction, "Call instruction expects callee operand.");

                StoreVariadicHomeArea(instruction);

                var cursor = new AbiCursor();
                AbiLocation hiddenReturnBufferRegister = AbiLocation.None;
                if (instruction.Result is not null && CAbi.RequiresHiddenReturnBuffer(_owner._target, instruction.Result.Type))
                {
                    var loc = CAbi.AssignHiddenReturnBufferLocation(_owner._target, ref cursor, _stackArgumentSlotSize);
                    if (loc.Kind == AbiLocationKind.Stack)
                        StoreHiddenReturnBufferArgument(loc, instruction.Result, instruction);
                    else
                        hiddenReturnBufferRegister = loc;
                }

                var stackArguments = new List<PendingCallArgumentSegment>();
                var registerArguments = new List<PendingCallArgumentSegment>();
                var signature = instruction.CallSignature;
                for (var i = 1; i < instruction.Operands.Length; i++)
                {
                    var operand = instruction.Operands[i];
                    var isVariadicUnnamed = signature is not null && signature.IsVariadic && i - 1 >= signature.Parameters.Length;
                    var value = CAbi.ClassifyValue(_owner._target, operand.Type, isReturn: false, isVariadicUnnamed);
                    CollectArgumentSegments(stackArguments, registerArguments, operand, value, ref cursor, instruction, isVariadicUnnamed);
                }

                EmitPendingCallArgumentSegments(stackArguments, instruction);
                if (instruction.Result is not null && hiddenReturnBufferRegister.Kind != AbiLocationKind.None)
                    StoreHiddenReturnBufferArgument(hiddenReturnBufferRegister, instruction.Result, instruction);
                EmitPendingCallArgumentSegments(registerArguments, instruction);

                EmitSysVX64VariadicVectorRegisterCount(instruction);

                LoadVariadicHomePointer(instruction);

                var callee = instruction.Operands[0];
                if (callee.Kind == LirOperandKind.Symbol && callee.Symbol is not null)
                    Emit(X86Instruction.Branch(X86InstrKind.Call, X86Operand.SymbolOperand(_owner.GetSymbolLabel(callee.Symbol), 4, X86ObjectRelocationKind.Relative32)));
                else
                {
                    var target = LoadOperandForRead(callee, Scratch0, instruction, _wordSize);
                    if (target.Kind == X86OperandKind.Memory)
                    {
                        Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(Scratch0, _wordSize), target));
                        target = Reg(Scratch0, _wordSize);
                    }
                    Emit(X86Instruction.Branch(X86InstrKind.Call, target));
                }

                if (instruction.Result is not null)
                    LoadReturnValue(instruction.Result, instruction);
            }

            private void StoreVariadicHomeArea(LirInstruction instruction)
            {
                if (!TryGetVariadicHome(instruction, out var firstVariadicOperand, out var variadicCount, out var baseOffset, out var homeSlotSize))
                    return;

                for (var i = 0; i < variadicCount; i++)
                    StoreVariadicHomeValue(instruction, instruction.Operands[firstVariadicOperand + i], checked(baseOffset + i * homeSlotSize), homeSlotSize);
            }

            private void LoadVariadicHomePointer(LirInstruction instruction)
            {
                if (!TryGetVariadicHome(instruction, out _, out var variadicCount, out var baseOffset, out _))
                    return;

                if (variadicCount <= 0)
                {
                    Emit(X86Instruction.Binary(X86InstrKind.Xor, Reg(VarArgsRegister, _wordSize), Reg(VarArgsRegister, _wordSize)));
                    return;
                }

                Emit(X86Instruction.Binary(X86InstrKind.Lea, Reg(VarArgsRegister, _wordSize), Mem(X86Register.Rsp, baseOffset, _wordSize)));
            }
            private void EmitSysVX64VariadicVectorRegisterCount(LirInstruction instruction)
            {
                var signature = instruction.CallSignature;
                if (!_owner._machineTarget.Is64Bit || TargetRegisterInfo.IsWindowsX64(_owner._target) || signature is null || !signature.IsVariadic)
                    return;

                var cursor = new AbiCursor();
                if (instruction.Result is not null && CAbi.RequiresHiddenReturnBuffer(_owner._target, instruction.Result.Type))
                    _ = CAbi.AssignHiddenReturnBufferLocation(_owner._target, ref cursor, _stackArgumentSlotSize);

                for (var i = 1; i < instruction.Operands.Length; i++)
                {
                    var isVariadicUnnamed = signature.IsVariadic && i - 1 >= signature.Parameters.Length;
                    var value = CAbi.ClassifyValue(_owner._target, instruction.Operands[i].Type, isReturn: false, isVariadicUnnamed);
                    if (value.PassingKind == AbiPassingKind.MultiRegister)
                    {
                        foreach (var segment in value.Segments)
                            _ = CAbi.AssignSegmentArgumentLocation(segment, ref cursor, _stackArgumentSlotSize);
                    }
                    else
                    {
                        _ = CAbi.AssignArgumentLocation(value, ref cursor, _stackArgumentSlotSize);
                    }
                }

                Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(X86Register.Rax, 1), Imm(Math.Min(8, cursor.Vector))));
            }


            private bool TryGetVariadicHome(LirInstruction instruction, out int firstVariadicOperand, out int variadicCount, out int baseOffset, out int homeSlotSize)
            {
                firstVariadicOperand = 0;
                variadicCount = 0;
                baseOffset = 0;
                homeSlotSize = 0;

                var signature = instruction.CallSignature;
                if (signature is null || !signature.IsVariadic || !_owner._machineTarget.Is64Bit || !_owner._target.IsRegisterBytecode)
                    return false;

                var fixedCount = signature.Parameters.Length;
                firstVariadicOperand = 1 + fixedCount;
                variadicCount = instruction.Operands.Length - firstVariadicOperand;
                if (variadicCount < 0)
                    variadicCount = 0;

                var normalArgumentBytes = CAbi.ComputeOutgoingArgumentAreaSize(
                    instruction,
                    startOperand: 1,
                    _owner._target,
                    _stackArgumentSlotSize,
                    includeVariadicHomeArea: false);
                homeSlotSize = CAbi.VariadicHomeSlotSize(_owner._target, _stackArgumentSlotSize);
                baseOffset = checked(_allocation.Frame.OutgoingArgumentAreaOffset + AlignUp(normalArgumentBytes, homeSlotSize));
                return true;
            }

            private void StoreVariadicHomeValue(LirInstruction instruction, LirOperand operand, int offset, int homeSlotSize)
            {
                var size = SizeOfStorage(operand.Type);
                if (size > homeSlotSize)
                    throw Unsupported(instruction, "Variadic argument does not fit the configured va_list home slot.");

                var destination = Mem(X86Register.Rsp, offset, homeSlotSize);
                ZeroMemory(destination, homeSlotSize);

                if (RequiresBlockCopyStorage(operand.Type))
                {
                    var source = MaterializeScalarStorageAddress(operand, Scratch0, instruction);
                    EmitMemoryCopy(RegMem(source, 1), destination.WithSize(size), size);
                    return;
                }

                if (IsFloatType(operand.Type))
                {
                    var fpSource = LoadFloatingOperand(operand, FpScratch0, instruction);
                    EmitFloatingStore(destination.WithSize(FloatingStorageSize(operand.Type)), fpSource, operand.Type);
                    return;
                }

                var storeSize = Math.Min(RegisterSize(operand.Type), size);
                var sourceOperand = LoadOperandForRead(operand, Scratch0, instruction, storeSize);
                if (sourceOperand.Kind == X86OperandKind.Memory || (storeSize == 8 && (sourceOperand.Kind == X86OperandKind.Immediate || sourceOperand.Kind == X86OperandKind.Symbol)))
                {
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(Scratch0, storeSize), sourceOperand));
                    sourceOperand = Reg(Scratch0, storeSize);
                }
                Emit(X86Instruction.Binary(X86InstrKind.Mov, destination.WithSize(storeSize), sourceOperand));
            }

            private void StoreHiddenReturnBufferArgument(AbiLocation loc, LirVirtualRegister result, LirInstruction instruction)
            {
                var address = MaterializeVirtualRegisterStorageAddress(result, Scratch0);
                if (loc.Kind == AbiLocationKind.Register)
                {
                    MoveRegister(ToX86Register(loc.Register, _owner._machineTarget), address, _wordSize);
                    return;
                }
                if (loc.Kind == AbiLocationKind.Stack)
                {
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, Mem(X86Register.Rsp, loc.StackByteOffset(_stackArgumentSlotSize), _wordSize), Reg(address, _wordSize)));
                    return;
                }
                throw Unsupported(instruction, "Unsupported hidden return buffer ABI location.");
            }

            private readonly struct PendingCallArgumentSegment
            {
                public LirOperand Operand { get; }
                public AbiLocation Location { get; }
                public int SourceOffset { get; }
                public int Size { get; }
                public bool IsVariadicUnnamed { get; }

                public PendingCallArgumentSegment(LirOperand operand, AbiLocation location, int sourceOffset, int size, bool isVariadicUnnamed)
                {
                    Operand = operand;
                    Location = location;
                    SourceOffset = sourceOffset;
                    Size = size;
                    IsVariadicUnnamed = isVariadicUnnamed;
                }
            }

            private void CollectArgumentSegments(
                List<PendingCallArgumentSegment> stackArguments,
                List<PendingCallArgumentSegment> registerArguments,
                LirOperand operand,
                AbiValue value,
                ref AbiCursor cursor,
                LirInstruction instruction,
                bool isVariadicUnnamed)
            {
                if (IsLongDouble(operand.Type))
                    throw Unsupported(instruction, "x86 backend does not support long double code generation.");
                if (value.PassingKind == AbiPassingKind.Void)
                    return;
                if (value.PassingKind == AbiPassingKind.Unsupported)
                    throw Unsupported(instruction, $"Unsupported ABI argument: {operand.Type.ToDisplayString()}.");

                if (value.PassingKind == AbiPassingKind.MultiRegister)
                {
                    foreach (var segment in value.Segments)
                    {
                        var loc = CAbi.AssignSegmentArgumentLocation(segment, ref cursor, _stackArgumentSlotSize);
                        AddPendingCallArgumentSegment(stackArguments, registerArguments, operand, loc, segment.Offset, segment.Size, isVariadicUnnamed);
                    }
                    return;
                }

                var location = CAbi.AssignArgumentLocation(value, ref cursor, _stackArgumentSlotSize);
                AddPendingCallArgumentSegment(stackArguments, registerArguments, operand, location, 0, Math.Min(SizeOfStorage(operand.Type), Math.Max(1, value.Size)), isVariadicUnnamed);
            }

            private static void AddPendingCallArgumentSegment(
                List<PendingCallArgumentSegment> stackArguments,
                List<PendingCallArgumentSegment> registerArguments,
                LirOperand operand,
                AbiLocation location,
                int sourceOffset,
                int size,
                bool isVariadicUnnamed)
            {
                var segment = new PendingCallArgumentSegment(operand, location, sourceOffset, size, isVariadicUnnamed);
                if (location.Kind == AbiLocationKind.Stack)
                    stackArguments.Add(segment);
                else
                    registerArguments.Add(segment);
            }

            private void EmitPendingCallArgumentSegments(List<PendingCallArgumentSegment> segments, LirInstruction instruction)
            {
                foreach (var segment in segments)
                    StoreArgumentSegment(segment.Operand, segment.Location, segment.SourceOffset, segment.Size, instruction, segment.IsVariadicUnnamed);
            }

            private void StoreArgument(LirOperand operand, AbiValue value, ref AbiCursor cursor, LirInstruction instruction, bool isVariadicUnnamed)
            {
                if (IsLongDouble(operand.Type))
                    throw Unsupported(instruction, "x86 backend does not support long double code generation.");
                if (value.PassingKind == AbiPassingKind.Void)
                    return;
                if (value.PassingKind == AbiPassingKind.Unsupported)
                    throw Unsupported(instruction, $"Unsupported ABI argument: {operand.Type.ToDisplayString()}.");
                if (value.PassingKind == AbiPassingKind.MultiRegister)
                {
                    foreach (var segment in value.Segments)
                    {
                        var loc = CAbi.AssignSegmentArgumentLocation(segment, ref cursor, _stackArgumentSlotSize);
                        StoreArgumentSegment(operand, loc, segment.Offset, segment.Size, instruction, isVariadicUnnamed);
                    }
                    return;
                }

                var location = CAbi.AssignArgumentLocation(value, ref cursor, _stackArgumentSlotSize);
                StoreArgumentSegment(operand, location, 0, Math.Min(SizeOfStorage(operand.Type), Math.Max(1, value.Size)), instruction, isVariadicUnnamed);
            }

            private void StoreArgumentSegment(LirOperand operand, AbiLocation location, int sourceOffset, int size, LirInstruction instruction, bool isVariadicUnnamed)
            {
                if (IsFloatType(operand.Type) && !RequiresBlockCopyStorage(operand.Type))
                {
                    var fpArgumentSource = LoadFloatingOperand(operand, FpScratch0, instruction);
                    if (location.Kind == AbiLocationKind.Register)
                    {
                        var fpDestination = ToX86Register(location.Register, _owner._machineTarget);
                        EmitFloatingMove(fpDestination, fpArgumentSource, operand.Type);
                        DuplicateWindowsX64VariadicFloatingArgument(operand, fpDestination, location, isVariadicUnnamed, instruction);
                        return;
                    }
                    if (location.Kind == AbiLocationKind.Stack)
                    {
                        EmitFloatingStore(Mem(X86Register.Rsp, location.StackByteOffset(_stackArgumentSlotSize), FloatingStorageSize(operand.Type)), fpArgumentSource, operand.Type);
                        return;
                    }
                    throw Unsupported(instruction, "Unsupported floating-point ABI argument location.");
                }

                if (location.Kind == AbiLocationKind.Register)
                {
                    if (RequiresBlockCopyStorage(operand.Type))
                    {
                        var source = MaterializeScalarStorageAddress(operand, Scratch0, instruction);
                        EmitLoadFromMemory(ToX86Register(location.Register, _owner._machineTarget),
                            RegMem(source, Math.Min(RegisterSize(operand.Type), size)).WithDisplacement(sourceOffset), false);
                    }
                    else
                    {
                        var source = LoadOperandForRead(operand, Scratch0, instruction, Math.Min(RegisterSize(operand.Type), size));
                        MoveIntoRegister(ToX86Register(location.Register, _owner._machineTarget), source, Math.Min(_wordSize, Math.Max(1, size)));
                    }
                    return;
                }

                if (location.Kind == AbiLocationKind.Stack)
                {
                    var destination = Mem(X86Register.Rsp, location.StackByteOffset(_stackArgumentSlotSize), Math.Max(1, size));
                    if (RequiresBlockCopyStorage(operand.Type))
                    {
                        var source = MaterializeScalarStorageAddress(operand, Scratch0, instruction);
                        EmitMemoryCopy(RegMem(source, 1).WithDisplacement(sourceOffset), destination, size);
                    }
                    else
                    {
                        var storeSize = Math.Min(RegisterSize(operand.Type), size);
                        var source = LoadOperandForRead(operand, Scratch0, instruction, storeSize);
                        if (source.Kind == X86OperandKind.Memory || (storeSize == 8 && (source.Kind == X86OperandKind.Immediate || source.Kind == X86OperandKind.Symbol)))
                        {
                            Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(Scratch0, storeSize), source));
                            source = Reg(Scratch0, storeSize);
                        }
                        Emit(X86Instruction.Binary(X86InstrKind.Mov, destination.WithSize(storeSize), source));
                    }
                    return;
                }

                throw Unsupported(instruction, "Unsupported ABI argument location.");
            }

            private void DuplicateWindowsX64VariadicFloatingArgument(LirOperand operand, X86Register source, AbiLocation location, bool isVariadicUnnamed, LirInstruction instruction)
            {
                if (!isVariadicUnnamed || !_owner._machineTarget.Is64Bit || !TargetRegisterInfo.IsWindowsX64(_owner._target) || location.Kind != AbiLocationKind.Register)
                    return;

                var vectorRegisters = TargetRegisterInfo.VectorArgumentRegisters(_owner._target);
                var integerRegisters = TargetRegisterInfo.IntegerArgumentRegisters(_owner._target);
                var machineRegister = ToMachineRegister(source);
                var slot = vectorRegisters.IndexOf(machineRegister);
                if (slot < 0 || slot >= integerRegisters.Length)
                    return;

                var size = FloatingStorageSize(operand.Type);
                var temp = _allocation.Frame.FloatingImmediateTempOffset;
                EmitFloatingStore(Mem(X86Register.Rsp, temp, size), source, operand.Type);
                EmitLoadFromMemory(ToX86Register(integerRegisters[slot], _owner._machineTarget), Mem(X86Register.Rsp, temp, Math.Min(_wordSize, size)), false);
            }

            private static MachineRegister ToMachineRegister(X86Register register)
            {
                return register switch
                {
                    X86Register.Xmm0 => MachineRegister.V0,
                    X86Register.Xmm1 => MachineRegister.V1,
                    X86Register.Xmm2 => MachineRegister.V2,
                    X86Register.Xmm3 => MachineRegister.V3,
                    X86Register.Xmm4 => MachineRegister.V4,
                    X86Register.Xmm5 => MachineRegister.V5,
                    X86Register.Xmm6 => MachineRegister.V6,
                    X86Register.Xmm7 => MachineRegister.V7,
                    X86Register.Xmm8 => MachineRegister.V8,
                    X86Register.Xmm9 => MachineRegister.V9,
                    X86Register.Xmm10 => MachineRegister.V10,
                    X86Register.Xmm11 => MachineRegister.V11,
                    X86Register.Xmm12 => MachineRegister.V12,
                    X86Register.Xmm13 => MachineRegister.V13,
                    X86Register.Xmm14 => MachineRegister.V14,
                    X86Register.Xmm15 => MachineRegister.V15,
                    _ => MachineRegister.Invalid,
                };
            }

            private void LoadReturnValue(LirVirtualRegister destination, LirInstruction instruction)
            {
                var value = CAbi.ClassifyValue(_owner._target, destination.Type, isReturn: true, isVariadicUnnamedArgument: false);
                if (IsFloatType(destination.Type))
                {
                    if (value.PassingKind != AbiPassingKind.Scalar)
                        throw Unsupported(instruction, "Unsupported floating-point return ABI value.");
                    var fpSegment = value.Segments.Length != 0 ? value.Segments[0] : new AbiSegment(0, value.Size, AbiRegisterClass.Vector);
                    var fpReg = fpSegment.ReturnRegisters.Length == 0 ? MachineRegister.V0 : fpSegment.ReturnRegisters[0];
                    var fpWritable = GetWritableRegister(destination, ToX86Register(fpReg, _owner._machineTarget));
                    EmitFloatingMove(fpWritable, ToX86Register(fpReg, _owner._machineTarget), destination.Type);
                    StoreWritableRegisterIfSpilled(destination, fpWritable);
                    return;
                }
                if (value.PassingKind == AbiPassingKind.Void || value.PassingKind == AbiPassingKind.Indirect)
                    return;

                if (value.PassingKind == AbiPassingKind.Scalar)
                {
                    var segment = value.Segments.Length != 0 ? value.Segments[0] : new AbiSegment(0, value.Size, AbiRegisterClass.General);
                    var reg = segment.ReturnRegisters.Length == 0 ? MachineRegister.X0 : segment.ReturnRegisters[0];
                    var writable = GetWritableRegister(destination, ToX86Register(reg, _owner._machineTarget));
                    MoveRegister(writable, ToX86Register(reg, _owner._machineTarget), RegisterSize(destination.Type));
                    StoreWritableRegisterIfSpilled(destination, writable);
                    return;
                }

                if (value.PassingKind == AbiPassingKind.MultiRegister)
                {
                    var destinationAddress = MaterializeVirtualRegisterStorageAddress(destination, Scratch0);
                    foreach (var segment in value.Segments)
                    {
                        var reg = segment.ReturnRegisters.Length > 0
                            ? segment.ReturnRegisters[Math.Min(segment.ReturnRegisters.Length - 1, segment.Offset / Math.Max(1, _wordSize))]
                            : MachineRegister.X0;
                        EmitStoreToMemory(ToX86Register(reg, _owner._machineTarget), destinationAddress, segment.Offset, Math.Min(segment.Size, _wordSize));
                    }
                    return;
                }

                throw Unsupported(instruction, "Unsupported return ABI value.");
            }

            private void EmitVaStart(LirInstruction instruction)
            {
                if (instruction.Operands.Length == 1)
                {
                    EmitVaStartStore(instruction);
                    return;
                }

                if (instruction.Result is null)
                    return;
                var dst = GetWritableRegister(instruction.Result, Scratch0);
                if (_allocation.Frame.HasVarArgsPointer)
                    EmitLoadFromStack(dst, _allocation.Frame.VarArgsPointerOffset, _wordSize, false);
                else
                    Emit(X86Instruction.Binary(X86InstrKind.Xor, Reg(dst, _wordSize), Reg(dst, _wordSize)));
                StoreWritableRegisterIfSpilled(instruction.Result, dst);
            }

            private void EmitVaStartStore(LirInstruction instruction)
            {
                var ap = LoadOperand(instruction.Operands[0], Scratch0, instruction, _wordSize);
                if (_sysVX64RegisterSaveAreaOffset >= 0)
                {
                    var cursor = ComputeNamedArgumentCursor();
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, Mem(ap, 0, 4), Imm(cursor.Integer * 8)));
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, Mem(ap, 4, 4), Imm(SysVX64FpSaveAreaOffset + cursor.Vector * 16)));
                    Emit(X86Instruction.Binary(X86InstrKind.Lea, Reg(Scratch1, _wordSize), Mem(X86Register.Rsp, IncomingStackOffset(cursor.Stack * _stackArgumentSlotSize), _wordSize)));
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, Mem(ap, 8, _wordSize), Reg(Scratch1, _wordSize)));
                    Emit(X86Instruction.Binary(X86InstrKind.Lea, Reg(Scratch1, _wordSize), Mem(X86Register.Rsp, _sysVX64RegisterSaveAreaOffset, _wordSize)));
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, Mem(ap, 16, _wordSize), Reg(Scratch1, _wordSize)));
                    return;
                }

                if (_allocation.Frame.HasVarArgsPointer)
                    EmitLoadFromStack(Scratch1, _allocation.Frame.VarArgsPointerOffset, _wordSize, false);
                else
                    Emit(X86Instruction.Binary(X86InstrKind.Xor, Reg(Scratch1, _wordSize), Reg(Scratch1, _wordSize)));
                Emit(X86Instruction.Binary(X86InstrKind.Mov, RegMem(ap, _wordSize), Reg(Scratch1, _wordSize)));
            }

            private AbiCursor ComputeNamedArgumentCursor()
            {
                var cursor = new AbiCursor();
                if (_allocation.Frame.HasHiddenReturnBuffer)
                    _ = CAbi.AssignHiddenReturnBufferLocation(_owner._target, ref cursor, _stackArgumentSlotSize);
                var functionType = _function.Symbol?.FunctionType;
                if (functionType is not null)
                {
                    foreach (var parameter in functionType.Parameters)
                    {
                        var value = CAbi.ClassifyValue(_owner._target, parameter.Type, isReturn: false, isVariadicUnnamedArgument: false);
                        _ = CAbi.AssignArgumentLocation(value, ref cursor, _stackArgumentSlotSize);
                    }
                }
                return cursor;
            }

            private void EmitVaArg(LirInstruction instruction)
            {
                if (instruction.Operands.Length != 4)
                    throw Unsupported(instruction, "VaArg expects a va_list pointer, kind, size, and alignment.");
                if (instruction.Result is null)
                    return;

                var kind = ImmediateToInt32(instruction.Operands[1]);
                var size = Math.Max(1, ImmediateToInt32(instruction.Operands[2]));
                var align = Math.Max(1, ImmediateToInt32(instruction.Operands[3]));

                if (_owner._machineTarget.Is64Bit && !TargetRegisterInfo.IsWindowsX64(_owner._target))
                    EmitSysVX64VaArg(instruction, kind, size, align);
                else
                    EmitPointerVaArg(instruction, size, Math.Min(align, _wordSize));
            }

            private void EmitPointerVaArg(LirInstruction instruction, int size, int align)
            {
                var apAddress = LoadOperand(instruction.Operands[0], Scratch0, instruction, _wordSize);
                EmitLoadFromMemory(Scratch1, RegMem(apAddress, _wordSize), false);
                AlignPointerRegister(Scratch1, align);
                var slotSize = AlignUp(size, _wordSize);
                var dst = GetWritableRegister(instruction.Result!, Scratch0);
                MoveRegister(dst, Scratch1, _wordSize);
                Emit(X86Instruction.Binary(X86InstrKind.Add, Reg(Scratch1, _wordSize), Imm(slotSize)));
                Emit(X86Instruction.Binary(X86InstrKind.Mov, RegMem(apAddress, _wordSize), Reg(Scratch1, _wordSize)));
                StoreWritableRegisterIfSpilled(instruction.Result!, dst);
            }

            private void EmitSysVX64VaArg(LirInstruction instruction, int kind, int size, int align)
            {
                var ap = LoadOperand(instruction.Operands[0], Scratch0, instruction, _wordSize);
                var overflowLabel = _owner.CreateLocalLabel(_functionLabel + "_va_overflow");
                var doneLabel = _owner.CreateLocalLabel(_functionLabel + "_va_done");

                if (kind == 1 && size <= 8)
                {
                    EmitSysVX64RegisterVaArg(ap, offsetField: 4, registerSaveAreaLimit: SysVX64RegisterSaveAreaSize, needed: 16, overflowLabel);
                }
                else if (kind == 0 && size <= 16)
                {
                    EmitSysVX64RegisterVaArg(ap, offsetField: 0, registerSaveAreaLimit: SysVX64GpSaveAreaSize, needed: AlignUp(size, 8), overflowLabel);
                }
                else
                {
                    Emit(X86Instruction.Branch(X86InstrKind.Jmp, X86Operand.SymbolOperand(overflowLabel, 4, X86ObjectRelocationKind.Relative32)));
                }

                Emit(X86Instruction.Branch(X86InstrKind.Jmp, X86Operand.SymbolOperand(doneLabel, 4, X86ObjectRelocationKind.Relative32)));
                _owner._text.DefineLabel(overflowLabel);
                EmitSysVX64OverflowVaArg(ap, size, align);
                _owner._text.DefineLabel(doneLabel);

                var dst = GetWritableRegister(instruction.Result!, Scratch0);
                MoveRegister(dst, Scratch1, _wordSize);
                StoreWritableRegisterIfSpilled(instruction.Result!, dst);
            }

            private void EmitSysVX64RegisterVaArg(X86Register ap, int offsetField, int registerSaveAreaLimit, int needed, string overflowLabel)
            {
                EmitLoadFromMemory(Scratch1, Mem(ap, offsetField, 4), false);
                Emit(X86Instruction.Binary(X86InstrKind.Cmp, Reg(Scratch1, 4), Imm(registerSaveAreaLimit - needed)));
                Emit(X86Instruction.ConditionalBranch(X86Condition.A, X86Operand.SymbolOperand(overflowLabel, 4, X86ObjectRelocationKind.Relative32)));
                EmitLoadFromMemory(Scratch0, Mem(ap, 16, _wordSize), false);
                Emit(X86Instruction.Binary(X86InstrKind.Add, Reg(Scratch0, _wordSize), Reg(Scratch1, _wordSize)));
                Emit(X86Instruction.Binary(X86InstrKind.Add, Reg(Scratch1, 4), Imm(needed)));
                Emit(X86Instruction.Binary(X86InstrKind.Mov, Mem(ap, offsetField, 4), Reg(Scratch1, 4)));
                MoveRegister(Scratch1, Scratch0, _wordSize);
            }

            private void EmitSysVX64OverflowVaArg(X86Register ap, int size, int align)
            {
                EmitLoadFromMemory(Scratch1, Mem(ap, 8, _wordSize), false);
                AlignPointerRegister(Scratch1, align);
                MoveRegister(Scratch0, Scratch1, _wordSize);
                Emit(X86Instruction.Binary(X86InstrKind.Add, Reg(Scratch0, _wordSize), Imm(AlignUp(size, _wordSize))));
                Emit(X86Instruction.Binary(X86InstrKind.Mov, Mem(ap, 8, _wordSize), Reg(Scratch0, _wordSize)));
            }

            private void AlignPointerRegister(X86Register register, int alignment)
            {
                if (alignment <= 1)
                    return;
                Emit(X86Instruction.Binary(X86InstrKind.Add, Reg(register, _wordSize), Imm(alignment - 1)));
                Emit(X86Instruction.Binary(X86InstrKind.And, Reg(register, _wordSize), Imm(-alignment)));
            }

            private void EmitBranch(LirInstruction instruction)
            {
                if (instruction.Operands.Length == 0)
                    throw Unsupported(instruction, "Branch expects condition operand.");
                var condition = instruction.Operands[0];
                if (IsFloatType(condition.Type))
                {
                    var fpReg = LoadFloatingOperand(condition, FpScratch0, instruction);
                    Emit(X86Instruction.Binary(FloatingCompareOpcode(condition.Type),
                        Reg(fpReg, FloatingStorageSize(condition.Type)), Reg(LoadFloatingZero(FpScratch1, condition.Type), FloatingStorageSize(condition.Type))));
                    Emit(X86Instruction.ConditionalBranch(X86Condition.P, Label(instruction.TrueTarget)));
                    Emit(X86Instruction.ConditionalBranch(X86Condition.Ne, Label(instruction.TrueTarget)));
                    EmitJump(instruction.FalseTarget);
                    return;
                }
                var size = RegisterSize(condition.Type);
                var reg = LoadOperand(condition, Scratch0, instruction, size);
                Emit(X86Instruction.Binary(X86InstrKind.Test, Reg(reg, size), Reg(reg, size)));
                Emit(X86Instruction.ConditionalBranch(X86Condition.Ne, Label(instruction.TrueTarget)));
                EmitJump(instruction.FalseTarget);
            }

            private void EmitSwitch(LirInstruction instruction)
            {
                if (instruction.Operands.Length == 0)
                    throw Unsupported(instruction, "Switch expects selector operand.");
                var size = RegisterSize(instruction.Operands[0].Type);
                var reg = LoadOperand(instruction.Operands[0], Scratch0, instruction, size);
                foreach (var switchCase in instruction.SwitchCases)
                {
                    var caseValue = ImmediateToInt64(switchCase.Value);
                    if (size == 8 && !FitsSignedInt32(caseValue))
                    {
                        MoveIntoRegister(Scratch1, Imm(caseValue), size);
                        Emit(X86Instruction.Binary(X86InstrKind.Cmp, Reg(reg, size), Reg(Scratch1, size)));
                    }
                    else
                    {
                        Emit(X86Instruction.Binary(X86InstrKind.Cmp, Reg(reg, size), Imm(caseValue)));
                    }
                    Emit(X86Instruction.ConditionalBranch(X86Condition.E, Label(switchCase.Target)));
                }
                EmitJump(instruction.Target);
            }

            private void EmitReturn(LirInstruction instruction)
            {
                if (instruction.Operands.Length != 0)
                    StoreReturnValue(instruction.Operands[0], instruction);
                Emit(X86Instruction.Branch(X86InstrKind.Jmp, X86Operand.SymbolOperand(_epilogueLabel, 4, X86ObjectRelocationKind.Relative32)));
            }

            private void StoreReturnValue(LirOperand operand, LirInstruction instruction)
            {
                var value = CAbi.ClassifyValue(_owner._target, operand.Type, isReturn: true, isVariadicUnnamedArgument: false);
                if (IsFloatType(operand.Type))
                {
                    if (value.PassingKind == AbiPassingKind.Void)
                        return;
                    if (value.PassingKind != AbiPassingKind.Scalar)
                        throw Unsupported(instruction, "Unsupported floating-point return ABI value.");
                    var fpSegment = value.Segments.Length != 0 ? value.Segments[0] : new AbiSegment(0, value.Size, AbiRegisterClass.Vector);
                    var fpReg = fpSegment.ReturnRegisters.Length == 0 ? MachineRegister.V0 : fpSegment.ReturnRegisters[0];
                    var fpSource = LoadFloatingOperand(operand, FpScratch0, instruction);
                    EmitFloatingMove(ToX86Register(fpReg, _owner._machineTarget), fpSource, operand.Type);
                    return;
                }
                if (value.PassingKind == AbiPassingKind.Void)
                    return;

                if (value.PassingKind == AbiPassingKind.Indirect)
                {
                    if (!_allocation.Frame.HasHiddenReturnBuffer)
                        throw Unsupported(instruction, "Hidden return buffer is required but not available.");
                    EmitLoadFromStack(Scratch0, _allocation.Frame.HiddenReturnBufferOffset, _wordSize, false);
                    var source = MaterializeScalarStorageAddress(operand, Scratch1, instruction);
                    EmitMemoryCopy(RegMem(source, 1), RegMem(Scratch0, 1), SizeOfStorage(operand.Type));
                    MoveRegister(X86Register.Rax, Scratch0, _wordSize);
                    return;
                }

                if (value.PassingKind == AbiPassingKind.Scalar)
                {
                    var segment = value.Segments.Length != 0 ? value.Segments[0] : new AbiSegment(0, value.Size, AbiRegisterClass.General);
                    var reg = segment.ReturnRegisters.Length == 0 ? MachineRegister.X0 : segment.ReturnRegisters[0];
                    MoveIntoRegister(ToX86Register(reg, _owner._machineTarget), LoadOperandForRead(operand, Scratch0, instruction, RegisterSize(operand.Type)), RegisterSize(operand.Type));
                    return;
                }

                if (value.PassingKind == AbiPassingKind.MultiRegister)
                {
                    var source = MaterializeScalarStorageAddress(operand, Scratch0, instruction);
                    for (var i = 0; i < value.Segments.Length; i++)
                    {
                        var segment = value.Segments[i];
                        var reg = segment.ReturnRegisters.Length > i ? segment.ReturnRegisters[i] : MachineRegister.X0;
                        EmitLoadFromMemory(ToX86Register(reg, _owner._machineTarget), RegMem(source, Math.Min(segment.Size, _wordSize)).WithDisplacement(segment.Offset), false);
                    }
                    return;
                }

                throw Unsupported(instruction, "Unsupported return ABI value.");
            }

            private void EmitJump(LirBlock? target)
            {
                Emit(X86Instruction.Branch(X86InstrKind.Jmp, Label(target)));
            }

            private void EmitValueCopy(LirVirtualRegister destination, LirOperand source, LirInstruction instruction)
            {
                if (IsFloatType(destination.Type) || IsFloatType(source.Type))
                {
                    if (!IsFloatType(destination.Type) || !IsFloatType(source.Type))
                        throw Unsupported(instruction, "Floating-point copy requires matching floating-point operands.");
                    var writable = GetWritableRegister(destination, FpScratch0);
                    var sourceReg = LoadFloatingOperand(source, writable, instruction);
                    EmitFloatingPrecisionMove(writable, sourceReg, source.Type, destination.Type);
                    StoreWritableRegisterIfSpilled(destination, writable);
                    return;
                }
                if (RequiresBlockCopyStorage(destination.Type))
                {
                    var destAddress = MaterializeVirtualRegisterStorageAddress(destination, Scratch0);
                    EmitOperandToMemory(source, RegMem(destAddress, SizeOfStorage(destination.Type)), SizeOfStorage(destination.Type), instruction);
                    return;
                }

                var size = RegisterSize(destination.Type);
                var dst = GetWritableRegister(destination, PreferredScratch(destination.Type));
                LoadOperandInto(source, dst, instruction, size);
                NormalizeIntegerRegister(dst, destination.Type);
                StoreWritableRegisterIfSpilled(destination, dst);
            }

            private void EmitOperandToMemory(LirOperand source, X86Operand destination, int size, LirInstruction instruction)
            {
                if (source.Kind is LirOperandKind.Undefined or LirOperandKind.Void or LirOperandKind.None)
                {
                    ZeroMemory(destination, size);
                    return;
                }

                if (source.Kind == LirOperandKind.Immediate && source.Immediate is string text)
                {
                    var label = _owner.CreateStringLiteral(text);
                    if (size >= _wordSize)
                    {
                        EmitSymbolAddress(Scratch1, label);
                        Emit(X86Instruction.Binary(X86InstrKind.Mov, destination.WithSize(_wordSize), Reg(Scratch1, _wordSize)));
                    }
                    else
                        ZeroMemory(destination, size);
                    return;
                }

                if (RequiresBlockCopyStorage(source.Type))
                {
                    var address = MaterializeScalarStorageAddress(source, Scratch1, instruction);
                    EmitMemoryCopy(RegMem(address, 1), destination, size);
                    return;
                }

                if (IsFloatType(source.Type))
                {
                    var fpValue = LoadFloatingOperand(source, FpScratch0, instruction);
                    EmitFloatingStore(destination, fpValue, source.Type);
                    return;
                }

                var storeSize = Math.Min(RegisterSize(source.Type), size);
                var integerValue = LoadOperandForRead(source, Scratch1, instruction, storeSize);
                if (integerValue.Kind == X86OperandKind.Memory || (storeSize == 8 && (integerValue.Kind == X86OperandKind.Immediate || integerValue.Kind == X86OperandKind.Symbol)))
                {
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(Scratch1, storeSize), integerValue));
                    integerValue = Reg(Scratch1, storeSize);
                }
                Emit(X86Instruction.Binary(X86InstrKind.Mov, destination.WithSize(storeSize), integerValue));
            }

            private void EmitMemoryToDestination(X86Operand source, LirVirtualRegister destination, int size)
            {
                if (RequiresBlockCopyStorage(destination.Type))
                {
                    var address = MaterializeVirtualRegisterStorageAddress(destination, Scratch0);
                    EmitMemoryCopy(source, RegMem(address, 1), size);
                    return;
                }

                if (IsFloatType(destination.Type))
                {
                    var dst = GetWritableRegister(destination, FpScratch0);
                    EmitFloatingLoad(dst, source, destination.Type);
                    StoreWritableRegisterIfSpilled(destination, dst);
                    return;
                }

                {
                    var dst = GetWritableRegister(destination, PreferredScratch(destination.Type));
                    EmitLoadFromMemory(dst, source.WithSize(RegisterSize(destination.Type)), IsSignedIntegerType(destination.Type));
                    StoreWritableRegisterIfSpilled(destination, dst);
                }
            }

            private X86Register MaterializeScalarStorageAddress(LirOperand operand, X86Register scratch, LirInstruction instruction)
            {
                if (operand.Kind == LirOperandKind.Register && operand.Register is not null)
                    return MaterializeVirtualRegisterStorageAddress(operand.Register, scratch);
                if (operand.Kind == LirOperandKind.StackSlot && operand.StackSlot is not null)
                {
                    Emit(X86Instruction.Binary(X86InstrKind.Lea, Reg(scratch, _wordSize), Mem(X86Register.Rsp, _allocation.Frame.StackSlotOffsets[operand.StackSlot], _wordSize)));
                    return scratch;
                }
                if (operand.Kind == LirOperandKind.Symbol && operand.Symbol is not null)
                {
                    EmitSymbolAddress(scratch, _owner.GetSymbolLabel(operand.Symbol));
                    return scratch;
                }
                if (operand.Kind == LirOperandKind.Address && operand.Address is not null)
                    return MaterializeAddress(operand.Address, scratch, instruction);
                throw Unsupported(instruction, "Operand has no addressable storage.");
            }

            private X86Register MaterializeVirtualRegisterStorageAddress(LirVirtualRegister register, X86Register scratch)
            {
                var allocation = _allocation[register];
                if (allocation.IsSpilled)
                    Emit(X86Instruction.Binary(X86InstrKind.Lea, Reg(scratch, _wordSize), Mem(X86Register.Rsp, allocation.StackOffset, _wordSize)));
                else
                    throw new NotSupportedException("Register-allocated aggregate storage is not addressable.");
                return scratch;
            }

            private X86Register MaterializeAddress(LirAddress address, X86Register scratch, LirInstruction instruction)
            {
                switch (address.Kind)
                {
                    case LirAddressKind.StackSlot:
                        if (address.StackSlot is null)
                            throw Unsupported(instruction, "Stack-slot address without stack slot.");
                        Emit(X86Instruction.Binary(X86InstrKind.Lea, Reg(scratch, _wordSize), Mem(X86Register.Rsp, _allocation.Frame.StackSlotOffsets[address.StackSlot], _wordSize)));
                        return scratch;
                    case LirAddressKind.Symbol:
                        if (address.Symbol is null)
                            throw Unsupported(instruction, "Symbol address without symbol.");
                        EmitSymbolAddress(scratch, _owner.GetSymbolLabel(address.Symbol));
                        return scratch;
                    case LirAddressKind.Indirect:
                        if (address.BaseOperand is null)
                            throw Unsupported(instruction, "Indirect address without base operand.");
                        LoadOperandInto(address.BaseOperand, scratch, instruction, _wordSize);
                        return scratch;
                    case LirAddressKind.Element:
                        if (address.BaseAddress is null)
                            throw Unsupported(instruction, "Element address without base address.");
                        MaterializeAddress(address.BaseAddress, scratch, instruction);
                        if (address.Index is not null)
                        {
                            var indexScratch = scratch == Scratch1 ? Scratch0 : Scratch1;
                            LoadOperandInto(address.Index, indexScratch, instruction, _wordSize);
                            if (address.Scale != 1)
                                Emit(X86Instruction.Ternary(X86InstrKind.Imul, Reg(indexScratch, _wordSize), Reg(indexScratch, _wordSize), Imm(address.Scale)));
                            Emit(X86Instruction.Binary(X86InstrKind.Add, Reg(scratch, _wordSize), Reg(indexScratch, _wordSize)));
                        }
                        return scratch;
                    case LirAddressKind.Field:
                        if (address.BaseAddress is null)
                            throw Unsupported(instruction, "Field address without base address.");
                        MaterializeAddress(address.BaseAddress, scratch, instruction);
                        if (address.Displacement != 0)
                            Emit(X86Instruction.Binary(X86InstrKind.Add, Reg(scratch, _wordSize), Imm(address.Displacement)));
                        return scratch;
                    default:
                        throw Unsupported(instruction, "Unsupported LIR address kind: " + address.Kind + ".");
                }
            }

            private void EmitSymbolAddress(X86Register destination, string symbol)
            {
                if (_owner._machineTarget.Is64Bit)
                    Emit(X86Instruction.Binary(X86InstrKind.Lea, Reg(destination, _wordSize), X86Operand.RipRelative(symbol, 0, _wordSize)));
                else
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(destination, _wordSize), X86Operand.SymbolOperand(symbol, _wordSize, X86ObjectRelocationKind.Absolute32)));
            }

            private X86Operand LoadOperandForIntegerOperation(LirOperand operand, X86Register scratch, LirInstruction instruction, int size)
            {
                var value = LoadOperandForRead(operand, scratch, instruction, size);
                if (size == 8 && (value.Kind == X86OperandKind.Symbol || (value.Kind == X86OperandKind.Immediate && !FitsSignedInt32(value.Immediate))))
                {
                    MoveIntoRegister(scratch, value, size);
                    return Reg(scratch, size);
                }
                return value;
            }

            private X86Operand LoadOperandForRead(LirOperand operand, X86Register scratch, LirInstruction instruction, int size)
            {
                switch (operand.Kind)
                {
                    case LirOperandKind.Immediate:
                        if (operand.Immediate is string text)
                            return SymbolAddressOperand(_owner.CreateStringLiteral(text));
                        return Imm(ConvertIntegerConstant(operand.Immediate));
                    case LirOperandKind.Symbol:
                        if (operand.Symbol is null)
                            throw Unsupported(instruction, "Symbol operand without symbol.");
                        return SymbolAddressOperand(_owner.GetSymbolLabel(operand.Symbol));
                    case LirOperandKind.Register:
                        if (operand.Register is null)
                            throw Unsupported(instruction, "Register operand without register.");
                        return GetRegisterReadOperand(operand.Register, scratch, size);
                    case LirOperandKind.StackSlot:
                        if (operand.StackSlot is null)
                            throw Unsupported(instruction, "Stack slot operand without slot.");
                        return Mem(X86Register.Rsp, _allocation.Frame.StackSlotOffsets[operand.StackSlot], size);
                    case LirOperandKind.Address:
                        if (operand.Address is null)
                            throw Unsupported(instruction, "Address operand without address.");
                        MaterializeAddress(operand.Address, scratch, instruction);
                        return Reg(scratch, _wordSize);
                    case LirOperandKind.Undefined:
                    case LirOperandKind.Void:
                    case LirOperandKind.None:
                        return Imm(0);
                    default:
                        throw Unsupported(instruction, "Unsupported operand kind: " + operand.Kind + ".");
                }
            }

            private X86Register LoadOperand(LirOperand operand, X86Register scratch, LirInstruction instruction, int size)
            {
                var source = LoadOperandForRead(operand, scratch, instruction, size);
                if (source.Kind == X86OperandKind.Register)
                    return source.Register;
                MoveIntoRegister(scratch, source, size);
                return scratch;
            }

            private void LoadOperandInto(LirOperand operand, X86Register destination, LirInstruction instruction, int size)
            {
                var sourceSize = operand.Kind == LirOperandKind.Register && operand.Register is not null
                    ? RegisterSize(operand.Register.Type)
                    : RegisterSize(operand.Type);
                var readSize = Math.Min(Math.Max(1, size), Math.Max(1, sourceSize));
                var source = LoadOperandForRead(operand, destination, instruction, readSize);
                if ((source.Kind == X86OperandKind.Register || source.Kind == X86OperandKind.Memory) && source.Size < size)
                {
                    if (source.Size == 4 && size == 8)
                    {
                        if (IsSignedIntegerType(operand.Type))
                            Emit(X86Instruction.Binary(X86InstrKind.Movsxd, Reg(destination, size), source.WithSize(4)));
                        else
                            Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(destination, 4), source.WithSize(4)));
                    }
                    else
                    {
                        Emit(X86Instruction.Binary(IsSignedIntegerType(operand.Type) ? X86InstrKind.Movsx : X86InstrKind.Movzx, Reg(destination, size), source.WithSize(source.Size)));
                    }
                    return;
                }
                MoveIntoRegister(destination, source, size);
            }

            private X86Operand GetRegisterReadOperand(LirVirtualRegister register, X86Register scratch, int size)
            {
                var allocation = _allocation[register];
                if (!allocation.IsSpilled)
                    return Reg(ToX86Register(allocation.PhysicalRegister, _owner._machineTarget), size);
                return Mem(X86Register.Rsp, allocation.StackOffset, size);
            }

            private X86Register GetWritableRegister(LirVirtualRegister register, X86Register fallback)
            {
                var allocation = _allocation[register];
                return allocation.IsSpilled ? fallback : ToX86Register(allocation.PhysicalRegister, _owner._machineTarget);
            }

            private X86Register PreferredScratch(QualifiedType type)
                => IsFloatType(type) ? X86Register.Xmm0 : Scratch0;

            private void StoreWritableRegisterIfSpilled(LirVirtualRegister register, X86Register source)
            {
                var allocation = _allocation[register];
                if (!allocation.IsSpilled)
                    return;
                if (IsFloatType(register.Type))
                {
                    EmitFloatingStore(Mem(X86Register.Rsp, allocation.StackOffset, FloatingStorageSize(register.Type)), source, register.Type);
                    return;
                }
                EmitStoreToStack(source, allocation.StackOffset, Math.Min(RegisterSize(register.Type), SizeOfStorage(register.Type)));
            }

            private void MoveIntoRegister(X86Register destination, X86Operand source, int size)
            {
                if (source.Kind == X86OperandKind.Register && source.Register == destination && source.Size == size)
                    return;
                Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(destination, size), source.WithSize(size)));
            }

            private void MoveRegister(X86Register destination, X86Register source, int size)
            {
                if (destination == source)
                    return;
                Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(destination, size), Reg(source, size)));
            }

            private void NormalizeIntegerRegister(X86Register register, QualifiedType type)
            {
                if (!IsIntegerLike(type) || IsSignedIntegerType(type))
                    return;
                var size = SizeOfStorage(type);
                if (size >= RegisterSize(type))
                    return;
                Emit(X86Instruction.Binary(X86InstrKind.Movzx, Reg(register, RegisterSize(type)), Reg(register, size)));
            }

            private void EmitLoadFromStack(X86Register destination, int offset, int size, bool signed)
                => EmitLoadFromMemory(destination, Mem(X86Register.Rsp, offset, size), signed);

            private void EmitLoadFromMemory(X86Register destination, X86Operand source, bool signed)
            {
                var size = Math.Max(1, source.Size);
                var registerSize = Math.Max(size, size < 4 ? 4 : size);
                if (size == registerSize)
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(destination, registerSize), source.WithSize(size)));
                else if (signed)
                    Emit(X86Instruction.Binary(X86InstrKind.Movsx, Reg(destination, registerSize), source.WithSize(size)));
                else
                    Emit(X86Instruction.Binary(X86InstrKind.Movzx, Reg(destination, registerSize), source.WithSize(size)));
            }

            private void EmitStoreToStack(X86Register source, int offset, int size)
                => Emit(X86Instruction.Binary(X86InstrKind.Mov, Mem(X86Register.Rsp, offset, size), Reg(source, size)));

            private void EmitStoreToMemory(X86Register source, X86Register baseRegister, int offset, int size)
                => Emit(X86Instruction.Binary(X86InstrKind.Mov, Mem(baseRegister, offset, size), Reg(source, size)));

            private void EmitMemoryCopy(X86Operand source, X86Operand destination, int size)
            {
                for (var offset = 0; offset < size;)
                {
                    var chunk = Math.Min(_wordSize, size - offset);
                    if (chunk > 4 && _wordSize == 8)
                        chunk = 8;
                    else if (chunk > 2)
                        chunk = 4;
                    else if (chunk > 1)
                        chunk = 2;
                    else
                        chunk = 1;
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, Reg(Scratch1, chunk), source.WithSize(chunk).WithDisplacement(source.Displacement + offset)));
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, destination.WithSize(chunk).WithDisplacement(destination.Displacement + offset), Reg(Scratch1, chunk)));
                    offset += chunk;
                }
            }

            private void ZeroMemory(X86Operand destination, int size)
            {
                Emit(X86Instruction.Binary(X86InstrKind.Xor, Reg(Scratch1, 4), Reg(Scratch1, 4)));
                for (var offset = 0; offset < size;)
                {
                    var chunk = Math.Min(_wordSize, size - offset);
                    if (chunk > 4 && _wordSize == 8)
                        chunk = 8;
                    else if (chunk > 2)
                        chunk = 4;
                    else if (chunk > 1)
                        chunk = 2;
                    else
                        chunk = 1;
                    Emit(X86Instruction.Binary(X86InstrKind.Mov, destination.WithSize(chunk).WithDisplacement(destination.Displacement + offset), Reg(Scratch1, chunk)));
                    offset += chunk;
                }
            }

            private X86Operand SymbolAddressOperand(string symbol)
                => X86Operand.SymbolOperand(symbol, _wordSize, _wordSize == 8 ? X86ObjectRelocationKind.Absolute64 : X86ObjectRelocationKind.Absolute32);

            private X86Operand Label(LirBlock? target)
                => X86Operand.SymbolOperand(target is not null && _blockLabels.TryGetValue(target, out var label) ? label : _epilogueLabel, 4, X86ObjectRelocationKind.Relative32);

            private void DefineBlockLabel(LirBlock block)
            {
                if (_blockLabels.TryGetValue(block, out var label))
                    _owner._text.DefineLabel(label);
            }

            private void Emit(X86Instruction instruction)
                => _owner._text.Emit(instruction);

            private int IncomingStackOffset(int abiStackOffset)
                => checked(_frameSize + _savedGeneralRegisters.Count * _wordSize + abiStackOffset + _wordSize);

            private int SizeOfStorage(QualifiedType type)
                => Math.Max(1, _owner._target.SizeOf(type));

            private int RegisterSize(QualifiedType type)
            {
                if (IsPointerLike(type))
                    return _wordSize;
                if (IsFloatType(type))
                    return Math.Max(1, SizeOfStorage(type));
                return Math.Min(Math.Max(1, SizeOfStorage(type)), _wordSize);
            }

            private int PointerScale(QualifiedType pointerType)
            {
                if (pointerType.Type is PointerType pointer)
                    return Math.Max(1, _owner._target.SizeOf(pointer.PointeeType));
                if (pointerType.Type is ArrayType array)
                    return Math.Max(1, _owner._target.SizeOf(array.ElementType));
                return 1;
            }

            private bool RequiresBlockCopyStorage(QualifiedType type)
                => IsAggregateType(type) || (!IsPointerLike(type) && SizeOfStorage(type) > _wordSize);

            private static X86Operand Reg(X86Register register, int size)
                => X86Operand.RegisterOperand(register, Math.Max(1, size));

            private static X86Operand Mem(X86Register baseRegister, long displacement, int size)
                => X86Operand.Memory(baseRegister, displacement, Math.Max(1, size));

            private static X86Operand RegMem(X86Register baseRegister, int size)
                => X86Operand.Memory(baseRegister, 0, Math.Max(1, size));

            private X86Operand StackPointer()
                => Reg(X86Register.Rsp, _wordSize);

            private X86Register Scratch0
                => _owner._machineTarget.Is64Bit ? X86Register.R10 : X86Register.Rax;

            private X86Register Scratch1
                => _owner._machineTarget.Is64Bit ? X86Register.R11 : X86Register.Rdx;

            private X86Register VarArgsRegister
                => _owner._machineTarget.Is64Bit ? X86Register.R11 : X86Register.Rax;

            private static X86Register FpScratch0
                => X86Register.Xmm0;

            private static X86Register FpScratch1
                => X86Register.Xmm1;

            private static X86Register FpScratch2
                => X86Register.Xmm2;

            private static X86Operand Imm(long value)
                => X86Operand.ImmediateOperand(value);

            private static int AlignUp(int value, int alignment)
            {
                if (alignment <= 1)
                    return value;
                var remainder = value % alignment;
                return remainder == 0 ? value : checked(value + alignment - remainder);
            }

            private static int PositiveModulo(int value, int modulus)
            {
                if (modulus <= 1)
                    return 0;
                var remainder = value % modulus;
                return remainder < 0 ? remainder + modulus : remainder;
            }

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

            private static bool FitsSignedInt32(long value)
                => value >= int.MinValue && value <= int.MaxValue;

            private NotSupportedException Unsupported(LirInstruction instruction, string message)
                => new NotSupportedException($"{message} Function '{_function.Symbol?.Name ?? _functionLabel}', LIR instruction #{instruction.Ordinal}.");
        }

        private static X86Register ToX86Register(MachineRegister register, X86Target target)
        {
            if (register >= MachineRegister.V0 && register <= MachineRegister.V31)
            {
                var index = (int)register - (int)MachineRegister.V0;
                return (X86Register)((int)X86Register.Xmm0 + index);
            }

            if (target.Is32Bit)
            {
                return register switch
                {
                    MachineRegister.X0 => X86Register.Rax,
                    MachineRegister.X1 => X86Register.Rcx,
                    MachineRegister.X2 => X86Register.Rdx,
                    MachineRegister.X3 => X86Register.Rbx,
                    MachineRegister.X4 => X86Register.Rsi,
                    MachineRegister.X5 => X86Register.Rdi,
                    _ => throw new NotSupportedException("Unsupported i386 machine register: " + register + "."),
                };
            }

            if (target.Abi == X86AbiKind.WindowsX64)
            {
                return register switch
                {
                    MachineRegister.X0 => X86Register.Rax,
                    MachineRegister.X1 => X86Register.Rcx,
                    MachineRegister.X2 => X86Register.Rdx,
                    MachineRegister.X3 => X86Register.R8,
                    MachineRegister.X4 => X86Register.R9,
                    MachineRegister.X5 => X86Register.R10,
                    MachineRegister.X6 => X86Register.R11,
                    MachineRegister.X7 => X86Register.Rbx,
                    MachineRegister.X8 => X86Register.Rsi,
                    MachineRegister.X9 => X86Register.Rdi,
                    MachineRegister.X11 => X86Register.R12,
                    MachineRegister.X12 => X86Register.R13,
                    MachineRegister.X13 => X86Register.R14,
                    MachineRegister.X14 => X86Register.R15,
                    _ => throw new NotSupportedException("Unsupported Windows x64 machine register: " + register + "."),
                };
            }

            return register switch
            {
                MachineRegister.X0 => X86Register.Rax,
                MachineRegister.X1 => X86Register.Rdi,
                MachineRegister.X2 => X86Register.Rsi,
                MachineRegister.X3 => X86Register.Rdx,
                MachineRegister.X4 => X86Register.Rcx,
                MachineRegister.X5 => X86Register.R8,
                MachineRegister.X6 => X86Register.R9,
                MachineRegister.X7 => X86Register.R10,
                MachineRegister.X8 => X86Register.R11,
                MachineRegister.X9 => X86Register.Rbx,
                MachineRegister.X11 => X86Register.R12,
                MachineRegister.X12 => X86Register.R13,
                MachineRegister.X13 => X86Register.R14,
                MachineRegister.X14 => X86Register.R15,
                _ => throw new NotSupportedException("Unsupported SysV x64 machine register: " + register + "."),
            };
        }

        private sealed class TextSectionBuilder
        {
            private readonly X86Target _target;
            private readonly X86InstructionBuilder _builder;
            private readonly List<X86ObjectRelocation> _relocations = new List<X86ObjectRelocation>();

            public string Name { get; }
            public int ByteLength => _builder.Position;

            public TextSectionBuilder(X86Target target, string name)
            {
                _target = target ?? throw new ArgumentNullException(nameof(target));
                _builder = new X86InstructionBuilder(target);
                Name = name;
            }

            public bool HasLabel(string label)
                => _builder.HasLabel(label);

            public void DefineLabel(string label)
                => _builder.DefineLabel(label);

            public void Emit(X86Instruction instruction)
                => _builder.Emit(instruction);

            public void EmitAssembly(string text, string labelPrefix)
            {
                var program = X86Assembler.Assemble(text ?? string.Empty, _target);
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
                    var rewritten = RewriteInlineLabelReferences(instruction, renamedLabels);
                    _builder.Emit(rewritten);
                    offset = checked(offset + X86CodeEncoder.GetEncodedLength(instruction, _target));
                }

                DefineInlineLabels(labelsByOffset, offset);
            }

            private void DefineInlineLabels(Dictionary<int, List<string>> labelsByOffset, int offset)
            {
                if (!labelsByOffset.TryGetValue(offset, out var labels))
                    return;

                foreach (var label in labels)
                    _builder.DefineLabel(label);
            }

            private static X86Instruction RewriteInlineLabelReferences(
                X86Instruction instruction,
                IReadOnlyDictionary<string, string> renamedLabels)
            {
                return instruction
                    .WithOperand0(RewriteInlineLabelReference(instruction.Operand0, renamedLabels))
                    .WithOperand1(RewriteInlineLabelReference(instruction.Operand1, renamedLabels))
                    .WithOperand2(RewriteInlineLabelReference(instruction.Operand2, renamedLabels));
            }

            private static X86Operand RewriteInlineLabelReference(
                X86Operand operand,
                IReadOnlyDictionary<string, string> renamedLabels)
            {
                return operand.Symbol is not null && renamedLabels.TryGetValue(operand.Symbol, out var replacement)
                    ? operand.WithSymbol(replacement, operand.RelocationKind, operand.Addend)
                    : operand;
            }

            public void AddRelocation(int offset, string symbol, long addend, X86ObjectRelocationKind kind)
                => _relocations.Add(new X86ObjectRelocation(Name, offset, symbol, addend, kind));

            public X86TextSection ToSection()
            {
                var section = _builder.ToTextSection();
                return new X86TextSection(section.Instructions, section.Labels, _relocations.ToImmutableArray());
            }
        }

        private sealed class DataSectionBuilder
        {
            private readonly List<byte> _data = new List<byte>();
            private readonly List<X86ObjectRelocation> _relocations = new List<X86ObjectRelocation>();

            public string Name { get; }
            public X86ObjectSectionKind Kind { get; }
            public int ByteLength => _data.Count;
            public int Alignment { get; private set; } = 1;

            public DataSectionBuilder(string name, X86ObjectSectionKind kind)
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

            public void DefineSymbol(string name, int offset, int size, X86ObjectSymbolBinding binding, List<X86ObjectSymbol> symbols)
                => symbols.Add(new X86ObjectSymbol(name, Name, offset, size, binding, X86ObjectSymbolKind.Object));

            public void AddRelocation(int offset, string symbol, long addend, X86ObjectRelocationKind kind)
                => _relocations.Add(new X86ObjectRelocation(Name, offset, symbol, addend, kind));

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

            public void EmitInteger(long value, int size)
            {
                var bytes = BitConverter.GetBytes(value);
                for (var i = 0; i < size; i++)
                    _data.Add(i < bytes.Length ? bytes[i] : (byte)0);
            }

            public X86DataSection ToSection()
                => new X86DataSection(Name, Kind, Alignment, _data.ToImmutableArray(), 0, _relocations.ToImmutableArray());
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

            public void DefineSymbol(string name, int offset, int size, X86ObjectSymbolBinding binding, List<X86ObjectSymbol> symbols)
                => symbols.Add(new X86ObjectSymbol(name, Name, offset, size, binding, X86ObjectSymbolKind.Object));

            public X86DataSection ToSection()
                => new X86DataSection(Name, X86ObjectSectionKind.Bss, Alignment, ImmutableArray<byte>.Empty, ByteLength, ImmutableArray<X86ObjectRelocation>.Empty);
        }

        private static int AlignUp(int value, int alignment)
        {
            if (alignment <= 1)
                return value;
            var remainder = value % alignment;
            return remainder == 0 ? value : checked(value + alignment - remainder);
        }
    }
}
