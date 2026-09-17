using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Cnidaria.X86;

public static class X86ElfWriter
{
    public const string DefaultInterpreterPath = "/lib64/ld-linux-x86-64.so.2";
    public const string DefaultStandardLibrarySoName = "libc.so.6";

    private const ushort Em386 = 3;
    private const ushort EmX86_64 = 62;
    private const int PltEntrySize = 16;
    private const int ReservedGotEntries = 3;

    private const uint X86Absolute64 = 1;
    private const uint X86GlobalData = 6;
    private const uint X86JumpSlot = 7;
    private const uint X86Relative = 8;

    public static ulong DefaultImageBase(X86Target target)
        => target.Is64Bit ? 0x400000UL : 0x08048000UL;

    public static byte[] WriteExecutable(
        X86Program program,
        ulong imageBase = 0,
        IReadOnlyDictionary<string, ulong>? externalSymbols = null)
        => Write(program, new ElfImageOptions
        {
            Kind = ElfImageKind.Executable,
            ImageBase = imageBase == 0 ? DefaultImageBase(program.Target) : imageBase,
            ExternalSymbols = externalSymbols,
        });

    public static byte[] WriteDynamicExecutable(
        X86Program program,
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
        X86Program program,
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

    public static byte[] Write(X86Program program, ElfImageOptions options)
    {
        if (program is null)
            throw new ArgumentNullException(nameof(program));
        if (program.Target.OperatingSystem != OperatingSystemKind.Linux)
            throw new ArgumentException("The ELF writer requires a Linux x86 target.", nameof(program));
        if (options.Dynamic && !program.Target.Is64Bit)
            throw new NotSupportedException("A 32-bit x86 dynamic image needs REL-format relocations, which are not implemented.");
        ElfImageBuilder.Validate(options);
        if (options.Shared)
            RequirePositionIndependentText(program);

        var imports = options.Dynamic ? CollectImports(program) : ElfImportSet.Empty;
        var rewritten = RedirectDataImports(program, imports.Data);

        var descriptor = new ElfTarget
        {
            Machine = program.Target.Is64Bit ? EmX86_64 : Em386,
            Is64Bit = program.Target.Is64Bit,
            Endianness = TargetEndianness.Little,
            PltEntrySize = PltEntrySize,
            ReservedGlobalOffsetTableEntries = ReservedGotEntries,
            AbsoluteRelocation = X86Absolute64,
            RelativeRelocation = X86Relative,
            JumpSlotRelocation = X86JumpSlot,
            GlobalDataRelocation = X86GlobalData,
            EmitPltEntry = EmitPltEntry,
            EncodeText = (symbols, image, fileOffset, address) =>
            {
                EncodeText(rewritten, fileOffset, address, symbols, image);
                ApplyTextRelocations(rewritten, fileOffset, address, symbols, image);
            },
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

    private static int ComputeTextSize(X86TextSection text, X86Target target)
    {
        var size = 0;
        foreach (var instruction in text.Instructions)
            size = checked(size + X86CodeEncoder.GetEncodedLength(instruction, target));
        return size;
    }

    private static void EncodeText(X86Program obj, int textFileOffset, ulong textAddress, IReadOnlyDictionary<string, ulong> symbols, byte[] image)
    {
        var offset = 0;
        foreach (var instruction in obj.Text.Instructions)
        {
            var encoded = X86CodeEncoder.Encode(instruction, obj.Target, checked(textAddress + (ulong)offset), symbols);
            Array.Copy(encoded, 0, image, textFileOffset + offset, encoded.Length);
            offset = checked(offset + encoded.Length);
        }
    }

    private static void ApplyTextRelocations(X86Program obj, int textFileOffset, ulong textAddress, IReadOnlyDictionary<string, ulong> symbols, byte[] image)
    {
        foreach (var relocation in obj.Text.Relocations)
            ApplyRelocation(image, textFileOffset + relocation.Offset, textAddress, relocation.Offset, relocation, symbols, obj.Target);
    }

    private static void ApplyRelocation(
        byte[] image,
        int imageOffset,
        ulong sectionAddress,
        int sectionOffset,
        X86ObjectRelocation relocation,
        IReadOnlyDictionary<string, ulong> symbols,
        X86Target target)
    {
        var symbolAddress = ResolveSymbol(symbols, relocation.SymbolName);
        var value = checked((long)symbolAddress + relocation.Addend);
        switch (relocation.Kind)
        {
            case X86ObjectRelocationKind.Relative8:
                WriteSigned(image, imageOffset, checked(value - (long)(sectionAddress + (ulong)sectionOffset + 1)), 1);
                break;
            case X86ObjectRelocationKind.Relative32:
            case X86ObjectRelocationKind.RipRelative32:
                WriteSigned(image, imageOffset, checked(value - (long)(sectionAddress + (ulong)sectionOffset + 4)), 4);
                break;
            case X86ObjectRelocationKind.AbsolutePointer:
                WriteUnsigned(image, imageOffset, checked((ulong)value), target.Is32Bit ? 4 : 8);
                break;
            case X86ObjectRelocationKind.Absolute32:
                WriteUnsigned(image, imageOffset, checked((ulong)value), 4);
                break;
            case X86ObjectRelocationKind.Absolute64:
                WriteUnsigned(image, imageOffset, checked((ulong)value), 8);
                break;
            default:
                throw new NotSupportedException("Unsupported x86 relocation: " + relocation.Kind);
        }
    }

    private static void RequirePositionIndependentText(X86Program program)
    {
        foreach (var instruction in program.Text.Instructions)
        {
            foreach (var operand in Operands(instruction))
            {
                if (operand.HasSymbol &&
                    operand.RelocationKind is X86ObjectRelocationKind.Absolute32 or X86ObjectRelocationKind.Absolute64)
                {
                    throw new NotSupportedException(
                        $"A shared object cannot hold the absolute address of {operand.Symbol}; compile it as position independent code.");
                }
            }
        }
    }

    private static IEnumerable<ElfSectionInput> CreateSections(X86Program program)
    {
        var referenced = new HashSet<string>(
            program.Symbols
                .Where(static s => s.Binding != X86ObjectSymbolBinding.External && s.Kind != X86ObjectSymbolKind.Section && !string.IsNullOrEmpty(s.SectionName))
                .Select(static s => s.SectionName),
            StringComparer.Ordinal);

        yield return new ElfSectionInput
        {
            Name = ".text",
            Kind = ElfSectionKind.Text,
            Alignment = 16,
            Data = new byte[ComputeTextSize(program.Text, program.Target)],
        };

        foreach (var section in program.DataSections)
        {
            var memorySize = section.Kind == X86ObjectSectionKind.Bss ? section.BssSize : section.Data.Length;
            if (memorySize == 0 && section.Relocations.Length == 0 && !referenced.Contains(section.Name))
                continue;
            yield return new ElfSectionInput
            {
                Name = section.Name,
                Kind = section.Kind switch
                {
                    X86ObjectSectionKind.Rodata => ElfSectionKind.Rodata,
                    X86ObjectSectionKind.Bss => ElfSectionKind.Bss,
                    _ => ElfSectionKind.Data,
                },
                Alignment = Math.Max(1, section.Alignment),
                Data = section.Kind == X86ObjectSectionKind.Bss ? Array.Empty<byte>() : section.Data.ToArray(),
                MemorySize = memorySize,
            };
        }
    }

    private static IEnumerable<ElfSymbolInput> CreateSymbols(X86Program program)
    {
        foreach (var label in program.Text.Labels)
            yield return new ElfSymbolInput { Name = label.Key, SectionName = ".text", Offset = label.Value };

        foreach (var symbol in program.Symbols)
        {
            if (symbol.Binding == X86ObjectSymbolBinding.External ||
                string.IsNullOrEmpty(symbol.SectionName) ||
                (symbol.Kind == X86ObjectSymbolKind.Section && symbol.Size == 0))
            {
                continue;
            }
            yield return new ElfSymbolInput
            {
                Name = symbol.Name,
                SectionName = symbol.SectionName,
                Offset = symbol.Offset,
                Size = symbol.Size,
                IsFunction = symbol.Kind == X86ObjectSymbolKind.Function,
                IsGlobal = symbol.Binding == X86ObjectSymbolBinding.Global && symbol.Kind != X86ObjectSymbolKind.Section,
            };
        }
    }

    private static IEnumerable<ElfRelocationInput> CreateRelocations(X86Program program)
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
                        X86ObjectRelocationKind.AbsolutePointer => 8,
                        X86ObjectRelocationKind.Absolute32 => 4,
                        X86ObjectRelocationKind.Absolute64 => 8,
                        _ => throw new NotSupportedException($"Unsupported data relocation: {relocation.Kind}"),
                    },
                    SymbolName = relocation.SymbolName,
                    Addend = relocation.Addend,
                };
            }
        }
    }

    private static ElfImportSet CollectImports(X86Program program)
    {
        var defined = new HashSet<string>(program.Text.Labels.Keys, StringComparer.Ordinal);
        foreach (var symbol in program.Symbols)
        {
            if (symbol.Binding != X86ObjectSymbolBinding.External)
                defined.Add(symbol.Name);
        }

        var objects = new HashSet<string>(
            program.Symbols
                .Where(static s => s.Binding == X86ObjectSymbolBinding.External && s.Kind == X86ObjectSymbolKind.Object)
                .Select(static s => s.Name),
            StringComparer.Ordinal);

        return ElfImportSet.Collect(defined, EnumerateReferences(program), objects);
    }

    private static IEnumerable<string> EnumerateReferences(X86Program program)
    {
        foreach (var instruction in program.Text.Instructions)
        {
            foreach (var operand in Operands(instruction))
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

    private static IEnumerable<X86Operand> Operands(X86Instruction instruction)
    {
        yield return instruction.Operand0;
        yield return instruction.Operand1;
        yield return instruction.Operand2;
    }

    private static void EmitPltEntry(byte[] data, int offset, ulong entry, ulong slot)
    {
        var displacement = checked((long)slot - (long)(entry + 6));
        if (displacement < int.MinValue || displacement > int.MaxValue)
            throw new OverflowException("A procedure linkage table entry is out of reach of its table slot.");
        data[offset + 0] = 0xff;
        data[offset + 1] = 0x25;
        for (var i = 0; i < 4; i++)
            data[offset + 2 + i] = (byte)((uint)displacement >> (i * 8));
        for (var i = 6; i < PltEntrySize; i++)
            data[offset + i] = 0x90;
    }

    /// <summary>Turns the lea of an address into a load, so an imported object is read from its table slot.</summary>
    private static X86Program RedirectDataImports(X86Program program, ImmutableArray<string> dataImports)
    {
        if (dataImports.Length == 0)
            return program;

        var imports = dataImports.ToImmutableHashSet(StringComparer.Ordinal);
        var instructions = program.Text.Instructions.ToBuilder();
        for (var i = 0; i < instructions.Count; i++)
        {
            var instruction = instructions[i];
            if (!Operands(instruction).Any(operand => operand.IsRipRelative && operand.Symbol is { } name && imports.Contains(name)))
                continue;

            var source = instruction.Operand1;
            if (instruction.Opcode != X86InstrKind.Lea ||
                !source.IsRipRelative ||
                source.Symbol is null ||
                !imports.Contains(source.Symbol) ||
                source.Addend != 0)
            {
                throw new NotSupportedException(
                    $"An imported data object is addressed in place rather than through its address: {source.Symbol ?? "?"}");
            }

            instructions[i] = X86Instruction.Binary(X86InstrKind.Mov, instruction.Operand0, source);
        }

        return new X86Program(
            program.Target,
            new X86TextSection(instructions.ToImmutable(), program.Text.Labels, program.Text.Relocations),
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

    private static void WriteSigned(byte[] image, int offset, long value, int size)
    {
        if (size == 1 && (value < sbyte.MinValue || value > sbyte.MaxValue))
            throw new OverflowException("8-bit relocation overflow.");
        if (size == 4 && (value < int.MinValue || value > int.MaxValue))
            throw new OverflowException("32-bit relocation overflow.");
        WriteUnsigned(image, offset, unchecked((ulong)value), size);
    }

    private static void WriteUnsigned(byte[] image, int offset, ulong value, int size)
    {
        for (var i = 0; i < size; i++)
            image[offset + i] = (byte)(value >> (i * 8));
    }
}
