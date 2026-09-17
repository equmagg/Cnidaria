using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Cnidaria.RiscV;

public static class RiscVElfWriter
{
    public const string DefaultInterpreterPath = "/ld.so";
    public const string DefaultStandardLibrarySoName = "libc.so";

    private const ushort EmRiscV = 243;
    private const uint EfRiscVRvc = 0x0001;
    private const uint EfRiscVFloatAbiSingle = 0x0002;
    private const uint EfRiscVFloatAbiDouble = 0x0004;
    private const int PltEntrySize = 12;
    private const int ReservedGotEntries = 2;

    private const uint RiscVAbsolute32 = 1;
    private const uint RiscVAbsolute64 = 2;
    private const uint RiscVRelative = 3;
    private const uint RiscVJumpSlot = 5;

    public static ulong DefaultImageBase(RVTarget target)
        => 0x10000UL;

    public static byte[] WriteExecutable(
        RiscVProgram program,
        ulong imageBase = 0,
        IReadOnlyDictionary<string, ulong>? externalSymbols = null)
        => Write(program, new ElfImageOptions
        {
            Kind = ElfImageKind.Executable,
            ImageBase = imageBase == 0 ? DefaultImageBase(program.Target) : imageBase,
            ExternalSymbols = externalSymbols,
        });

    public static byte[] WriteDynamicExecutable(
        RiscVProgram program,
        ulong imageBase = 0,
        string? interpreter = null,
        IEnumerable<string>? needed = null)
        => Write(program, new ElfImageOptions
        {
            Kind = ElfImageKind.DynamicExecutable,
            ImageBase = imageBase == 0 ? DefaultImageBase(program.Target) : imageBase,
            Interpreter = string.IsNullOrEmpty(interpreter) ? DefaultInterpreterPath : interpreter,
            Needed = needed?.ToImmutableArray() ?? ImmutableArray.Create(DefaultStandardLibrarySoName),
        });

    public static byte[] WriteSharedObject(
        RiscVProgram program,
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

    public static byte[] Write(RiscVProgram program, ElfImageOptions options)
    {
        if (program is null)
            throw new ArgumentNullException(nameof(program));
        if (program.Target.OperatingSystem != OperatingSystemKind.Linux)
            throw new ArgumentException("The ELF writer requires a Linux RISC-V target.", nameof(program));
        ElfImageBuilder.Validate(options);

        var target = program.Target;
        var is64 = target.Is64Bit;
        var imports = options.Dynamic ? CollectImports(program) : ElfImportSet.Empty;
        var rewritten = RedirectDataImports(program, imports.Data);

        var descriptor = new ElfTarget
        {
            Machine = EmRiscV,
            Is64Bit = is64,
            Endianness = target.Endianness,
            Flags = ElfFlags(target),
            PltEntrySize = PltEntrySize,
            ReservedGlobalOffsetTableEntries = ReservedGotEntries,
            AbsoluteRelocation = is64 ? RiscVAbsolute64 : RiscVAbsolute32,
            RelativeRelocation = RiscVRelative,
            JumpSlotRelocation = RiscVJumpSlot,
            GlobalDataRelocation = is64 ? RiscVAbsolute64 : RiscVAbsolute32,
            EmitPltEntry = (data, offset, entry, slot) => EmitPltEntry(data, offset, entry, slot, target),
            EncodeText = (symbols, image, fileOffset, address) => EncodeText(rewritten, fileOffset, address, symbols, image),
        };

        return new ElfImageBuilder(
            descriptor,
            options,
            CreateSections(program),
            CreateSymbols(program),
            CreateRelocations(program, is64 ? 8 : 4),
            imports.Functions,
            imports.Data,
            program.EntrySymbol).Build();
    }

    private static void EncodeText(RiscVProgram obj, int textFileOffset, ulong textAddress, IReadOnlyDictionary<string, ulong> symbols, byte[] image)
    {
        var relocations = obj.Text.Relocations.ToDictionary(static r => r.Offset, static r => r);
        var sectionOffset = 0;
        for (var i = 0; i < obj.Text.Instructions.Length; i++)
        {
            var instruction = obj.Text.Instructions[i];
            if (relocations.TryGetValue(sectionOffset, out var relocation))
                instruction = ResolveRelocatedInstruction(instruction, relocation, sectionOffset, relocations, textAddress, symbols);
            else if (instruction.HasSymbol)
                instruction = ResolveSymbolicInstruction(instruction, checked(textAddress + (ulong)sectionOffset), symbols);

            var encoded = RiscVCodeEncoder.Encode(instruction, obj.Target);
            var size = RVInstructionTable.GetEncodedSize(instruction.Opcode);
            RiscVCodeEncoder.WriteInstruction(image, checked(textFileOffset + sectionOffset), encoded, size, obj.Target.Endianness);
            sectionOffset = checked(sectionOffset + size);
        }
    }

    private static RVInstruction ResolveRelocatedInstruction(
        RVInstruction instruction,
        RVObjectRelocation relocation,
        int instructionOffset,
        IReadOnlyDictionary<int, RVObjectRelocation> textRelocations,
        ulong textAddress,
        IReadOnlyDictionary<string, ulong> symbols)
    {
        var symbolAddress = ResolveSymbol(symbols, relocation.SymbolName);
        var pc = checked(textAddress + (ulong)instructionOffset);
        var value = checked((long)symbolAddress + relocation.Addend);
        switch (relocation.Kind)
        {
            case RVObjectRelocationKind.Branch12:
                return instruction.WithImmediate(checked((int)(value - (long)pc)));
            case RVObjectRelocationKind.Jal20:
                return instruction.WithImmediate(checked((int)(value - (long)pc)));
            case RVObjectRelocationKind.PcrelHi20:
                return instruction.WithImmediate(PcrelHi20(value, (long)pc));
            case RVObjectRelocationKind.PcrelLo12I:
            case RVObjectRelocationKind.PcrelLo12S:
                return instruction.WithImmediate(PcrelLo12(value, FindPcrelHiPc(relocation, instructionOffset, textRelocations, textAddress)));
            default:
                throw new NotSupportedException($"Unsupported text relocation: {relocation.Kind}");
        }
    }

    private static RVInstruction ResolveSymbolicInstruction(RVInstruction instruction, ulong pc, IReadOnlyDictionary<string, ulong> symbols)
    {
        if (!instruction.HasSymbol)
            return instruction;
        var value = ResolveSymbol(symbols, instruction.Symbol!);
        switch (instruction.RelocationKind)
        {
            case RVRelocationKind.RelativeBranch:
            case RVRelocationKind.RelativeJal:
                return instruction.WithImmediate(checked((int)((long)value - (long)pc)));
            case RVRelocationKind.AbsoluteUpper20:
                return instruction.WithImmediate(PcrelHi20((long)value, (long)pc));
            case RVRelocationKind.AbsoluteLow12:
                return instruction.WithImmediate(PcrelLo12((long)value, checked((long)pc - 4)));
            default:
                throw new NotSupportedException($"Unsupported symbolic instruction relocation: {instruction.RelocationKind}");
        }
    }

    private static long FindPcrelHiPc(
        RVObjectRelocation relocation,
        int instructionOffset,
        IReadOnlyDictionary<int, RVObjectRelocation> textRelocations,
        ulong textAddress)
    {
        var previousOffset = instructionOffset - 4;
        if (textRelocations.TryGetValue(previousOffset, out var previous) &&
            previous.Kind == RVObjectRelocationKind.PcrelHi20 &&
            previous.SymbolName == relocation.SymbolName)
            return checked((long)textAddress + previousOffset);
        return checked((long)textAddress + instructionOffset);
    }

    private static uint ElfFlags(RVTarget target)
    {
        var flags = target.HasC ? EfRiscVRvc : 0u;
        flags |= target.Abi switch
        {
            RVAbiKind.Ilp32F or RVAbiKind.Lp64F => EfRiscVFloatAbiSingle,
            RVAbiKind.Ilp32D or RVAbiKind.Lp64D => EfRiscVFloatAbiDouble,
            _ => 0u,
        };
        return flags;
    }

    private static IEnumerable<ElfSectionInput> CreateSections(RiscVProgram program)
    {
        var referenced = new HashSet<string>(
            program.Symbols
                .Where(static s => s.Binding != RVObjectSymbolBinding.External && s.Kind != RVObjectSymbolKind.Section && !string.IsNullOrEmpty(s.SectionName))
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
            var memorySize = section.Kind == RVObjectSectionKind.Bss ? section.BssSize : section.Data.Length;
            if (memorySize == 0 && section.Relocations.Length == 0 && !referenced.Contains(section.Name))
                continue;
            yield return new ElfSectionInput
            {
                Name = section.Name,
                Kind = section.Kind switch
                {
                    RVObjectSectionKind.Rodata => ElfSectionKind.Rodata,
                    RVObjectSectionKind.Bss => ElfSectionKind.Bss,
                    _ => ElfSectionKind.Data,
                },
                Alignment = Math.Max(1, section.Alignment),
                Data = section.Kind == RVObjectSectionKind.Bss ? Array.Empty<byte>() : section.Data.ToArray(),
                MemorySize = memorySize,
            };
        }
    }

    private static IEnumerable<ElfSymbolInput> CreateSymbols(RiscVProgram program)
    {
        foreach (var label in program.Text.Labels)
            yield return new ElfSymbolInput { Name = label.Key, SectionName = ".text", Offset = label.Value };

        foreach (var symbol in program.Symbols)
        {
            if (symbol.Binding == RVObjectSymbolBinding.External ||
                string.IsNullOrEmpty(symbol.SectionName) ||
                (symbol.Kind == RVObjectSymbolKind.Section && symbol.Size == 0))
            {
                continue;
            }
            yield return new ElfSymbolInput
            {
                Name = symbol.Name,
                SectionName = symbol.SectionName,
                Offset = symbol.Offset,
                Size = symbol.Size,
                IsFunction = symbol.Kind == RVObjectSymbolKind.Function,
                IsGlobal = symbol.Binding == RVObjectSymbolBinding.Global && symbol.Kind != RVObjectSymbolKind.Section,
            };
        }
    }

    private static IEnumerable<ElfRelocationInput> CreateRelocations(RiscVProgram program, int pointerSize)
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
                        RVObjectRelocationKind.AbsolutePointer => pointerSize,
                        RVObjectRelocationKind.Absolute32 => 4,
                        RVObjectRelocationKind.Absolute64 => 8,
                        _ => throw new NotSupportedException($"Unsupported data relocation: {relocation.Kind}"),
                    },
                    SymbolName = relocation.SymbolName,
                    Addend = relocation.Addend,
                };
            }
        }
    }

    private static ElfImportSet CollectImports(RiscVProgram program)
    {
        var defined = new HashSet<string>(program.Text.Labels.Keys, StringComparer.Ordinal);
        foreach (var symbol in program.Symbols)
        {
            if (symbol.Binding != RVObjectSymbolBinding.External)
                defined.Add(symbol.Name);
        }

        var objects = new HashSet<string>(
            program.Symbols
                .Where(static s => s.Binding == RVObjectSymbolBinding.External && s.Kind == RVObjectSymbolKind.Object)
                .Select(static s => s.Name),
            StringComparer.Ordinal);

        return ElfImportSet.Collect(defined, EnumerateReferences(program), objects);
    }

    private static IEnumerable<string> EnumerateReferences(RiscVProgram program)
    {
        foreach (var instruction in program.Text.Instructions)
        {
            if (instruction.Symbol is { Length: > 0 } symbol)
                yield return symbol;
        }
        foreach (var relocation in program.Text.Relocations)
            yield return relocation.SymbolName;
        foreach (var section in program.DataSections)
        {
            foreach (var relocation in section.Relocations)
                yield return relocation.SymbolName;
        }
    }

    private static void EmitPltEntry(byte[] data, int offset, ulong entry, ulong slot, RVTarget target)
    {
        var high = PcrelHi20((long)slot, (long)entry);
        var low = PcrelLo12((long)slot, (long)entry);
        var load = target.Is64Bit ? RVInstrKind.Ld : RVInstrKind.Lw;
        WriteInstruction(data, offset + 0, RVInstruction.U(RVInstrKind.Auipc, RVRegister.X28, high), target);
        WriteInstruction(data, offset + 4, RVInstruction.I(load, RVRegister.X28, RVRegister.X28, low), target);
        WriteInstruction(data, offset + 8, RVInstruction.I(RVInstrKind.Jalr, RVRegister.X0, RVRegister.X28, 0), target);
    }

    private static void WriteInstruction(byte[] data, int offset, RVInstruction instruction, RVTarget target)
    {
        var encoded = RiscVCodeEncoder.Encode(instruction, target);
        for (var i = 0; i < 4; i++)
            data[offset + i] = (byte)(encoded >> (i * 8));
    }

    /// <summary>Turns the addi of an address pair into a load, so an imported object is read from its table slot.</summary>
    private static RiscVProgram RedirectDataImports(RiscVProgram program, ImmutableArray<string> dataImports)
    {
        if (dataImports.IsDefaultOrEmpty)
            return program;

        var imports = dataImports.ToImmutableHashSet(StringComparer.Ordinal);
        var offsets = new Dictionary<int, int>();
        var offset = 0;
        for (var i = 0; i < program.Text.Instructions.Length; i++)
        {
            offsets.Add(offset, i);
            offset = checked(offset + RVInstructionTable.GetEncodedSize(program.Text.Instructions[i].Opcode));
        }

        var instructions = program.Text.Instructions.ToBuilder();
        foreach (var relocation in program.Text.Relocations)
        {
            if (relocation.Kind != RVObjectRelocationKind.PcrelLo12I || !imports.Contains(relocation.SymbolName))
                continue;
            if (!offsets.TryGetValue(relocation.Offset, out var index))
                throw new InvalidOperationException("A relocation does not fall on an instruction boundary.");

            var instruction = instructions[index];
            if (instruction.Opcode != RVInstrKind.Addi)
                throw new NotSupportedException($"An imported data object is not addressed through an address pair: {relocation.SymbolName}");
            instructions[index] = new RVInstruction(
                program.Target.Is64Bit ? RVInstrKind.Ld : RVInstrKind.Lw,
                instruction.Rd,
                instruction.Rs1,
                RVRegister.Invalid,
                0,
                instruction.Symbol,
                instruction.RelocationKind);
        }

        return new RiscVProgram(
            program.Target,
            new RVTextSection(instructions.ToImmutable(), program.Text.Labels, program.Text.Relocations),
            program.DataSections,
            program.Symbols,
            program.EntrySymbol);
    }

    private static int PcrelHi20(long value, long pc)
    {
        var delta = checked(value - pc);
        return checked((int)((delta + 0x800L) >> 12));
    }

    private static int PcrelLo12(long value, long pc)
    {
        var hi = PcrelHi20(value, pc);
        return checked((int)(value - pc - ((long)hi << 12)));
    }

    private static ulong ResolveSymbol(IReadOnlyDictionary<string, ulong> symbols, string symbol)
    {
        if (symbols.TryGetValue(symbol, out var address))
            return address;
        throw new InvalidOperationException($"Unresolved symbol: {symbol}");
    }
}
