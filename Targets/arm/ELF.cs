using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Cnidaria.Arm;

public static class ArmElfWriter
{
    public const string DefaultInterpreterPath = "/lib/ld-linux-aarch64.so.1";
    public const string DefaultStandardLibrarySoName = "libc.so.6";

    private const ushort EmArm = 40;
    private const ushort EmAArch64 = 183;
    private const uint EfArmEabiVersion5 = 0x05000000;
    private const uint EfArmAbiFloatSoft = 0x00000200;
    private const uint EfArmAbiFloatHard = 0x00000400;
    private const int PltEntrySize = 16;
    private const int ReservedGotEntries = 3;

    private const uint AArch64Absolute64 = 257;
    private const uint AArch64GlobalData = 1025;
    private const uint AArch64JumpSlot = 1026;
    private const uint AArch64Relative = 1027;

    public static ulong DefaultImageBase(ArmTarget target)
        => 0x10000UL;

    /// <summary>Where a Linux toolchain places a position-dependent AArch64 executable.</summary>
    public const ulong DefaultDynamicImageBase = 0x400000UL;

    public static byte[] WriteExecutable(
        ArmProgram program,
        ulong imageBase = 0,
        IReadOnlyDictionary<string, ulong>? externalSymbols = null)
        => Write(program, new ElfImageOptions
        {
            Kind = ElfImageKind.Executable,
            ImageBase = imageBase == 0 ? DefaultImageBase(program.Target) : imageBase,
            ExternalSymbols = externalSymbols,
        });

    public static byte[] WriteDynamicExecutable(
        ArmProgram program,
        ulong imageBase = 0,
        string? interpreter = null,
        IEnumerable<string>? needed = null)
        => Write(program, new ElfImageOptions
        {
            Kind = ElfImageKind.DynamicExecutable,
            ImageBase = imageBase == 0 ? DefaultDynamicImageBase : imageBase,
            Interpreter = string.IsNullOrEmpty(interpreter) ? DefaultInterpreterPath : interpreter,
            Needed = needed?.ToImmutableArray() ?? ImmutableArray.Create(DefaultStandardLibrarySoName),
        });

    public static byte[] WriteSharedObject(
        ArmProgram program,
        string soName,
        IEnumerable<string>? needed = null,
        string? initSymbol = null)
        => Write(program, new ElfImageOptions
        {
            Kind = ElfImageKind.SharedObject,
            SoName = soName ?? string.Empty,
            Needed = needed?.ToImmutableArray() ?? ImmutableArray<string>.Empty,
            InitSymbol = initSymbol ?? string.Empty,
        });

    public static byte[] Write(ArmProgram program, ElfImageOptions options)
    {
        if (program is null)
            throw new ArgumentNullException(nameof(program));
        if (program.Target.OperatingSystem != OperatingSystemKind.Linux)
            throw new ArgumentException("The ELF writer requires a Linux ARM target.", nameof(program));
        if (options.Dynamic && !program.Target.Is64Bit)
            throw new NotSupportedException("A 32-bit ARM dynamic image needs REL-format relocations, which are not implemented.");
        ElfImageBuilder.Validate(options);
        if (options.Shared)
            RequirePositionIndependentText(program);

        var target = program.Target;
        var imports = options.Dynamic ? CollectImports(program) : ElfImportSet.Empty;
        var rewritten = RedirectDataImports(program, imports.Data);

        var descriptor = new ElfTarget
        {
            Machine = target.Is64Bit ? EmAArch64 : EmArm,
            Is64Bit = target.Is64Bit,
            Endianness = target.Endianness,
            Flags = ElfFlags(target),
            PltEntrySize = PltEntrySize,
            ReservedGlobalOffsetTableEntries = ReservedGotEntries,
            AbsoluteRelocation = AArch64Absolute64,
            RelativeRelocation = AArch64Relative,
            JumpSlotRelocation = AArch64JumpSlot,
            GlobalDataRelocation = AArch64GlobalData,
            EmitPltEntry = (data, offset, entry, slot) => EmitPltEntry(data, offset, entry, slot, target),
            EncodeText = (symbols, image, fileOffset, address) => EncodeText(rewritten, fileOffset, address, symbols, image),
        };

        return new ElfImageBuilder(
            descriptor,
            options,
            CreateSections(program),
            CreateSymbols(program),
            CreateRelocations(program),
            imports.Functions,
            imports.Data,
            program.EntrySymbol).Build();
    }

    private static uint ElfFlags(ArmTarget target)
        => target.Is64Bit ? 0u : EfArmEabiVersion5 | (target.UsesHardFloat ? EfArmAbiFloatHard : EfArmAbiFloatSoft);

    private static void EncodeText(ArmProgram obj, int textFileOffset, ulong textAddress, IReadOnlyDictionary<string, ulong> symbols, byte[] image)
    {
        var relocations = obj.Text.Relocations.ToDictionary(static relocation => relocation.Offset);
        for (var index = 0; index < obj.Text.Instructions.Length; index++)
        {
            var offset = checked(index * 4);
            var instruction = obj.Text.Instructions[index];
            if (relocations.TryGetValue(offset, out var relocation))
                instruction = ApplyRelocation(instruction, relocation, checked(textAddress + (ulong)offset), symbols);
            var word = ArmCodeEncoder.EncodeWord(instruction, obj.Target, checked(textAddress + (ulong)offset), symbols);
            ArmCodeEncoder.WriteWord(image, checked(textFileOffset + offset), word, obj.Target.Endianness);
        }
    }

    private static ArmInstruction ApplyRelocation(ArmInstruction instruction, ArmObjectRelocation relocation, ulong pc, IReadOnlyDictionary<string, ulong> symbols)
    {
        var value = checked((long)ResolveSymbol(symbols, relocation.SymbolName) + relocation.Addend);
        return relocation.Kind switch
        {
            ArmObjectRelocationKind.ArmMovw16 => instruction.WithOperand1(ArmOperand.ImmediateOperand(value & 0xFFFF)),
            ArmObjectRelocationKind.ArmMovt16 => instruction.WithOperand1(ArmOperand.ImmediateOperand((value >> 16) & 0xFFFF)),
            ArmObjectRelocationKind.ArmBranch24 or
            ArmObjectRelocationKind.ArmCall24 or
            ArmObjectRelocationKind.AArch64Branch26 or
            ArmObjectRelocationKind.AArch64Call26 or
            ArmObjectRelocationKind.AArch64ConditionalBranch19 => instruction.WithOperand0(ArmOperand.ImmediateOperand(checked(value - (long)pc))),
            ArmObjectRelocationKind.AArch64CompareBranch19 => instruction.WithOperand1(ArmOperand.ImmediateOperand(checked(value - (long)pc))),
            ArmObjectRelocationKind.AArch64TestBranch14 => instruction.WithOperand2(ArmOperand.ImmediateOperand(checked(value - (long)pc))),
            ArmObjectRelocationKind.AArch64Adr21 => instruction.WithOperand1(ArmOperand.ImmediateOperand(checked(value - (long)pc))),
            ArmObjectRelocationKind.AArch64Adrp21 => instruction.WithOperand1(ArmOperand.ImmediateOperand(checked(((value & ~0xFFFL) - ((long)pc & ~0xFFFL))))),
            ArmObjectRelocationKind.AArch64AddLow12 => instruction.WithOperand2(ArmOperand.ImmediateOperand(value & 0xFFF)),
            ArmObjectRelocationKind.AArch64LoadLow12 => instruction.WithOperand1(ArmOperand.Memory(instruction.Operand1.BaseRegister, value & 0xFFF, instruction.Operand1.Size)),
            ArmObjectRelocationKind.AArch64LoadLiteral19 => instruction.WithOperand1(ArmOperand.Literal(checked(value - (long)pc), instruction.Operand0.Size)),
            _ => throw new NotSupportedException("Unsupported ARM ELF text relocation: " + relocation.Kind),
        };
    }

    private static void RequirePositionIndependentText(ArmProgram program)
    {
        foreach (var relocation in program.Text.Relocations)
        {
            if (relocation.Kind is ArmObjectRelocationKind.Absolute32 or
                ArmObjectRelocationKind.Absolute64 or
                ArmObjectRelocationKind.AbsolutePointer)
            {
                throw new NotSupportedException(
                    $"A shared object cannot hold the absolute address of {relocation.SymbolName}.");
            }
        }
    }

    private static IEnumerable<ElfSectionInput> CreateSections(ArmProgram program)
    {
        var referenced = new HashSet<string>(
            program.Symbols
                .Where(static s => s.Binding != ArmObjectSymbolBinding.External && s.Kind != ArmObjectSymbolKind.Section && !string.IsNullOrEmpty(s.SectionName))
                .Select(static s => s.SectionName),
            StringComparer.Ordinal);

        yield return new ElfSectionInput
        {
            Name = ".text",
            Kind = ElfSectionKind.Text,
            Alignment = 4,
            Data = new byte[program.Text.SizeInBytes],
        };

        foreach (var section in program.DataSections)
        {
            var memorySize = section.Kind == ArmObjectSectionKind.Bss ? section.BssSize : section.Data.Length;
            if (memorySize == 0 && section.Relocations.Length == 0 && !referenced.Contains(section.Name))
                continue;
            yield return new ElfSectionInput
            {
                Name = section.Name,
                Kind = section.Kind switch
                {
                    ArmObjectSectionKind.Rodata => ElfSectionKind.Rodata,
                    ArmObjectSectionKind.Bss => ElfSectionKind.Bss,
                    _ => ElfSectionKind.Data,
                },
                Alignment = Math.Max(1, section.Alignment),
                Data = section.Kind == ArmObjectSectionKind.Bss ? Array.Empty<byte>() : section.Data.ToArray(),
                MemorySize = memorySize,
            };
        }
    }

    private static IEnumerable<ElfSymbolInput> CreateSymbols(ArmProgram program)
    {
        foreach (var label in program.Text.Labels)
            yield return new ElfSymbolInput { Name = label.Key, SectionName = ".text", Offset = label.Value };

        foreach (var symbol in program.Symbols)
        {
            if (symbol.Binding == ArmObjectSymbolBinding.External ||
                string.IsNullOrEmpty(symbol.SectionName) ||
                (symbol.Kind == ArmObjectSymbolKind.Section && symbol.Size == 0))
            {
                continue;
            }
            yield return new ElfSymbolInput
            {
                Name = symbol.Name,
                SectionName = symbol.SectionName,
                Offset = symbol.Offset,
                Size = symbol.Size,
                IsFunction = symbol.Kind == ArmObjectSymbolKind.Function,
                IsGlobal = symbol.Binding == ArmObjectSymbolBinding.Global && symbol.Kind != ArmObjectSymbolKind.Section,
            };
        }
    }

    private static IEnumerable<ElfRelocationInput> CreateRelocations(ArmProgram program)
    {
        foreach (var section in program.DataSections)
        {
            foreach (var relocation in section.Relocations)
            {
                yield return new ElfRelocationInput
                {
                    SectionName = section.Name,
                    Offset = relocation.Offset,
                    Size = relocation.Kind switch
                    {
                        ArmObjectRelocationKind.AbsolutePointer => 8,
                        ArmObjectRelocationKind.Absolute32 => 4,
                        ArmObjectRelocationKind.Absolute64 => 8,
                        _ => throw new NotSupportedException($"Unsupported data relocation: {relocation.Kind}"),
                    },
                    SymbolName = relocation.SymbolName,
                    Addend = relocation.Addend,
                };
            }
        }
    }

    private static ElfImportSet CollectImports(ArmProgram program)
    {
        var defined = new HashSet<string>(program.Text.Labels.Keys, StringComparer.Ordinal);
        foreach (var symbol in program.Symbols)
        {
            if (symbol.Binding != ArmObjectSymbolBinding.External)
                defined.Add(symbol.Name);
        }

        var objects = new HashSet<string>(
            program.Symbols
                .Where(static s => s.Binding == ArmObjectSymbolBinding.External && s.Kind == ArmObjectSymbolKind.Object)
                .Select(static s => s.Name),
            StringComparer.Ordinal);

        return ElfImportSet.Collect(defined, EnumerateReferences(program), objects);
    }

    private static IEnumerable<string> EnumerateReferences(ArmProgram program)
    {
        foreach (var instruction in program.Text.Instructions)
        {
            foreach (var operand in new[] { instruction.Operand0, instruction.Operand1, instruction.Operand2 })
            {
                if (operand.Symbol is { Length: > 0 } symbol)
                    yield return symbol;
            }
        }
        foreach (var relocation in program.Text.Relocations)
            yield return relocation.SymbolName;
        foreach (var section in program.DataSections)
        {
            foreach (var relocation in section.Relocations)
                yield return relocation.SymbolName;
        }
    }

    private static void EmitPltEntry(byte[] data, int offset, ulong entry, ulong slot, ArmTarget target)
    {
        var page = checked((long)(slot & ~0xfffUL) - (long)(entry & ~0xfffUL));
        var low = (long)(slot & 0xfffUL);
        WriteWord(data, offset + 0, EncodeAdrp(ArmRegister.X16, page), target);
        WriteWord(data, offset + 4, EncodeLoadUnsigned(ArmRegister.X17, ArmRegister.X16, low), target);
        WriteWord(data, offset + 8, EncodeAddImmediate(ArmRegister.X16, ArmRegister.X16, low), target);
        WriteWord(data, offset + 12, EncodeBranchRegister(ArmRegister.X17), target);
    }

    private static uint EncodeAdrp(ArmRegister destination, long page)
    {
        var immediate = page >> 12;
        if (immediate < -(1L << 20) || immediate >= (1L << 20))
            throw new OverflowException("A procedure linkage table entry is out of reach of its table slot.");
        var value = (uint)(immediate & 0x1fffff);
        return 0x90000000u | ((value & 3u) << 29) | (((value >> 2) & 0x7ffffu) << 5) | RegisterIndex(destination);
    }

    private static uint EncodeLoadUnsigned(ArmRegister destination, ArmRegister baseRegister, long offset)
        => 0xf9400000u | ((uint)(offset >> 3) << 10) | (RegisterIndex(baseRegister) << 5) | RegisterIndex(destination);

    private static uint EncodeAddImmediate(ArmRegister destination, ArmRegister source, long immediate)
        => 0x91000000u | ((uint)immediate << 10) | (RegisterIndex(source) << 5) | RegisterIndex(destination);

    private static uint EncodeBranchRegister(ArmRegister target)
        => 0xd61f0000u | (RegisterIndex(target) << 5);

    private static uint RegisterIndex(ArmRegister register)
        => (uint)(register - ArmRegister.X0);

    private static void WriteWord(byte[] data, int offset, uint word, ArmTarget target)
        => ArmCodeEncoder.WriteWord(data, offset, word, target.Endianness);

    /// <summary>Turns the add of an address pair into a load, so an imported object is read from its table slot.</summary>
    private static ArmProgram RedirectDataImports(ArmProgram program, ImmutableArray<string> dataImports)
    {
        if (dataImports.Length == 0)
            return program;

        var imports = dataImports.ToImmutableHashSet(StringComparer.Ordinal);
        var instructions = program.Text.Instructions.ToBuilder();
        var relocations = ImmutableArray.CreateBuilder<ArmObjectRelocation>(program.Text.Relocations.Length);
        foreach (var relocation in program.Text.Relocations)
        {
            if (relocation.Kind != ArmObjectRelocationKind.AArch64AddLow12 || !imports.Contains(relocation.SymbolName))
            {
                relocations.Add(relocation);
                continue;
            }

            var index = relocation.Offset / 4;
            var instruction = instructions[index];
            if (instruction.Opcode != ArmInstrKind.Add)
                throw new NotSupportedException($"An imported data object is not addressed through an address pair: {relocation.SymbolName}");
            instructions[index] = ArmInstruction.Binary(
                ArmInstrKind.Ldr,
                instruction.Operand0,
                ArmOperand.Memory(instruction.Operand1.Register, 0, 8));
            relocations.Add(new ArmObjectRelocation(
                relocation.SectionName,
                relocation.Offset,
                relocation.SymbolName,
                relocation.Addend,
                ArmObjectRelocationKind.AArch64LoadLow12));
        }

        return new ArmProgram(
            program.Target,
            new ArmTextSection(instructions.ToImmutable(), program.Text.Labels, relocations.ToImmutable()),
            program.DataSections,
            program.Symbols,
            program.EntrySymbol);
    }

    private static ulong ResolveSymbol(IReadOnlyDictionary<string, ulong> symbols, string symbol)
    {
        if (symbols.TryGetValue(symbol, out var address))
            return address;
        throw new InvalidOperationException($"Unresolved symbol: {symbol}");
    }
}
