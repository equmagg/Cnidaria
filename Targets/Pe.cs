using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Cnidaria;

public sealed record PeImport
{
    public string SymbolName { get; }
    public string LibraryName { get; }
    public string EntryPoint { get; }
    public bool IsData { get; }
    public string AddressSymbol => "__imp_" + SymbolName;

    public PeImport(string symbolName, string libraryName, string entryPoint, bool isData = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbolName);
        ValidateName(libraryName, nameof(libraryName));
        ValidateName(entryPoint, nameof(entryPoint));
        if (entryPoint[0] == '#' && (!ushort.TryParse(entryPoint.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out ushort ordinal) || ordinal == 0))
            throw new ArgumentException("PE import ordinal must be between 1 and 65535", nameof(entryPoint));
        SymbolName = symbolName;
        LibraryName = libraryName;
        EntryPoint = entryPoint;
        IsData = isData;
    }

    internal static void ValidateName(string value, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        if (value.Any(static c => c == 0 || c > 127))
            throw new ArgumentException("PE names must contain non-null ASCII characters", parameter);
    }

    internal static ImmutableArray<PeImport> Merge(IEnumerable<PeImport> imports)
    {
        var result = new Dictionary<string, PeImport>(StringComparer.Ordinal);
        foreach (var import in imports)
        {
            if (result.TryGetValue(import.SymbolName, out var previous) &&
                (!string.Equals(previous.LibraryName, import.LibraryName, StringComparison.OrdinalIgnoreCase) ||
                 previous.EntryPoint != import.EntryPoint || previous.IsData != import.IsData))
                throw new InvalidOperationException($"Conflicting PE import: {import.SymbolName}");
            result.TryAdd(import.SymbolName, import);
        }
        return result.Values.ToImmutableArray();
    }
}

internal sealed class PeImportTable
{
    internal static readonly ImmutableArray<PeImport> Kernel32Imports = ImmutableArray.Create(
            "GetStdHandle", "WriteFile", "ExitProcess", "GetCurrentThreadId", "GetCurrentProcessorNumber", "GetProcessHeap", 
            "HeapAlloc", "HeapReAlloc", "HeapFree", "VirtualAlloc", "VirtualFree",
            "CreateFileA", "ReadFile", "CloseHandle", "SetFilePointerEx", "SetEndOfFile", "FlushFileBuffers",
            "GetFileSizeEx", "GetFileAttributesA", "DeleteFileA", "MoveFileExA", "CreateDirectoryA", "RemoveDirectoryA",
            "GetFileType", "GetCurrentDirectoryA", "SetCurrentDirectoryA").Select(static name => new PeImport(name, "KERNEL32.dll", name)).ToImmutableArray();

    public byte[] Bytes { get; }
    public IReadOnlyDictionary<string, int> AddressOffsets { get; }
    public int DirectorySize { get; }

    private PeImportTable(byte[] bytes, IReadOnlyDictionary<string, int> offsets, int directorySize)
    {
        Bytes = bytes;
        AddressOffsets = offsets;
        DirectorySize = directorySize;
    }

    public static PeImportTable Build(int pointerSize, ImmutableArray<PeImport> imports, int rva)
    {
        var groups = imports.GroupBy(static import => import.LibraryName, StringComparer.OrdinalIgnoreCase).ToArray();
        int directorySize = checked((groups.Length + 1) * 20);
        int offset = Align(directorySize, pointerSize);
        var layouts = new List<(string Library, PeImport[][] Entries, int Lookup, int Addresses, int Name, int[] Names)>();
        foreach (var group in groups)
        {
            var entries = group.GroupBy(static import => import.EntryPoint, StringComparer.Ordinal).Select(static entry => entry.ToArray()).ToArray();
            int lookup = offset;
            offset = checked(offset + (entries.Length + 1) * pointerSize);
            int addresses = offset;
            offset = checked(offset + (entries.Length + 1) * pointerSize);
            layouts.Add((group.Key, entries, lookup, addresses, 0, new int[entries.Length]));
        }
        for (int i = 0; i < layouts.Count; i++)
        {
            var layout = layouts[i];
            layout.Name = offset;
            offset = checked(offset + layout.Library.Length + 1);
            for (int j = 0; j < layout.Entries.Length; j++)
            {
                string name = layout.Entries[j][0].EntryPoint;
                if (name[0] == '#')
                    continue;
                offset = Align(offset, 2);
                layout.Names[j] = offset;
                offset = checked(offset + 2 + name.Length + 1);
            }
            layouts[i] = layout;
        }
        var bytes = new byte[offset];
        var offsets = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < layouts.Count; i++)
        {
            var layout = layouts[i];
            Write32(bytes, i * 20, checked(rva + layout.Lookup));
            Write32(bytes, i * 20 + 12, checked(rva + layout.Name));
            Write32(bytes, i * 20 + 16, checked(rva + layout.Addresses));
            Encoding.ASCII.GetBytes(layout.Library, bytes.AsSpan(layout.Name));
            for (int j = 0; j < layout.Entries.Length; j++)
            {
                string name = layout.Entries[j][0].EntryPoint;
                ulong value;
                if (name[0] == '#')
                    value = (1UL << (pointerSize * 8 - 1)) | ushort.Parse(name.AsSpan(1), CultureInfo.InvariantCulture);
                else
                {
                    value = checked((ulong)(rva + layout.Names[j]));
                    Encoding.ASCII.GetBytes(name, bytes.AsSpan(layout.Names[j] + 2));
                }
                int address = layout.Addresses + j * pointerSize;
                WritePointer(bytes, layout.Lookup + j * pointerSize, value, pointerSize);
                WritePointer(bytes, address, value, pointerSize);
                foreach (var import in layout.Entries[j])
                {
                    offsets.Add(import.AddressSymbol, address);
                    if (import.IsData)
                        offsets.Add(import.SymbolName, address);
                }
            }
        }
        return new PeImportTable(bytes, offsets, directorySize);
    }

    private static void WritePointer(byte[] bytes, int offset, ulong value, int size)
    {
        if (size == 8)
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset), value);
        else
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), checked((uint)value));
    }

    private static int Align(int value, int alignment) => checked((value + alignment - 1) & -alignment);
    private static void Write32(byte[] bytes, int offset, int value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), checked((uint)value));
}

internal static class PeTables
{
    public static byte[] Exports(string libraryName, IReadOnlyDictionary<string, uint> exports, int rva)
    {
        PeImport.ValidateName(libraryName, nameof(libraryName));
        string[] names = exports.Keys.OrderBy(static name => name, StringComparer.Ordinal).ToArray();
        if (names.Length > ushort.MaxValue)
            throw new NotSupportedException("PE export ordinal table exceeds 65535 entries");
        int strings = checked(40 + names.Length * 10);
        int length = checked(strings + libraryName.Length + 1);
        foreach (string name in names)
        {
            PeImport.ValidateName(name, nameof(exports));
            length = checked(length + name.Length + 1);
        }
        var bytes = new byte[length];
        Write32(bytes, 12, checked((uint)(rva + strings)));
        Write32(bytes, 16, 1);
        Write32(bytes, 20, checked((uint)names.Length));
        Write32(bytes, 24, checked((uint)names.Length));
        Write32(bytes, 28, checked((uint)(rva + 40)));
        Write32(bytes, 32, checked((uint)(rva + 40 + names.Length * 4)));
        Write32(bytes, 36, checked((uint)(rva + 40 + names.Length * 8)));
        Encoding.ASCII.GetBytes(libraryName, bytes.AsSpan(strings));
        strings += libraryName.Length + 1;
        for (int i = 0; i < names.Length; i++)
        {
            string name = names[i];
            Write32(bytes, 40 + i * 4, exports[name]);
            Write32(bytes, 40 + names.Length * 4 + i * 4, checked((uint)(rva + strings)));
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(40 + names.Length * 8 + i * 2), checked((ushort)i));
            Encoding.ASCII.GetBytes(name, bytes.AsSpan(strings));
            strings += name.Length + 1;
        }
        return bytes;
    }

    public static byte[] BaseRelocations(IEnumerable<(int Rva, int Size)> relocations)
    {
        var entries = new Dictionary<int, int>();
        foreach (var relocation in relocations)
        {
            if (relocation.Size is not (4 or 8))
                throw new NotSupportedException("PE base relocation requires a 32-bit or 64-bit address");
            if (entries.TryGetValue(relocation.Rva, out int size) && size != relocation.Size)
                throw new InvalidOperationException("Overlapping PE base relocations");
            entries[relocation.Rva] = relocation.Size;
        }
        var pages = entries.OrderBy(static entry => entry.Key).GroupBy(static entry => entry.Key & ~4095).ToArray();
        int length = pages.Sum(static page => 8 + (page.Count() + 1) / 2 * 4);
        var bytes = new byte[Math.Max(8, length)];
        int offset = 0;
        foreach (var page in pages)
        {
            int blockSize = 8 + (page.Count() + 1) / 2 * 4;
            Write32(bytes, offset, checked((uint)page.Key));
            Write32(bytes, offset + 4, checked((uint)blockSize));
            int entryOffset = offset + 8;
            foreach (var entry in page)
            {
                ushort value = checked((ushort)(((entry.Value == 8 ? 10 : 3) << 12) | (entry.Key & 4095)));
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(entryOffset), value);
                entryOffset += 2;
            }
            offset += blockSize;
        }
        if (pages.Length == 0)
            Write32(bytes, 4, 8);
        return bytes;
    }

    private static void Write32(byte[] bytes, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
