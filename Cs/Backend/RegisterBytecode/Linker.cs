using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;

namespace Cnidaria.Cs;

internal readonly record struct RegisterBytecodeImport(int MethodId, string Module, string Name, string Signature);
internal readonly record struct RegisterBytecodeExport(string Name, int EntryPc, string Signature);

public sealed class RegisterBytecodeLibrary
{
    private const uint Magic = 0x314C4252;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public const string Extension = ".rbl";
    public string Name { get; }
    public CodeImage Image { get; }
    public IEnumerable<string> ExportNames => Exports.Select(e => e.Name);
    internal ImmutableArray<RegisterBytecodeExport> Exports { get; }
    internal ImmutableArray<RegisterBytecodeImport> Imports { get; }
    internal ImmutableArray<int> AddressRelocations { get; }

    internal RegisterBytecodeLibrary(string name, CodeImage image, ImmutableArray<RegisterBytecodeExport> exports,
        ImmutableArray<RegisterBytecodeImport> imports, ImmutableArray<int> addressRelocations)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['/', '\\', '\0']) >= 0) throw new ArgumentException("Invalid RBC library name.", nameof(name));
        Name = name; Image = image; Exports = exports; Imports = imports; AddressRelocations = addressRelocations;
        if (!image.Imports.IsEmpty || !image.TypeLayouts.IsEmpty || !image.VTables.IsEmpty || !image.EhRegions.IsEmpty ||
            !image.GcRoots.IsEmpty || !image.GcSafePoints.IsEmpty)
            throw new NotSupportedException("An unmanaged RBC library cannot contain managed runtime tables.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var export in exports)
            if (string.IsNullOrEmpty(export.Name) || !names.Add(export.Name) || !image.MethodIndexByEntryPc.ContainsKey(export.EntryPc))
                throw new InvalidDataException("Invalid or duplicate RBC export.");
        var ids = new HashSet<int>();
        foreach (var import in imports)
            if (import.MethodId < 0 || string.IsNullOrEmpty(import.Name) || !ids.Add(import.MethodId) || image.MethodIndexByRuntimeMethodId.ContainsKey(import.MethodId))
                throw new InvalidDataException("Invalid or duplicate RBC import.");
        var relocations = new HashSet<int>();
        foreach (int pc in addressRelocations)
            if ((uint)pc >= (uint)image.Code.Length || !relocations.Add(pc) || image.Code[pc].Op is not (Op.LiI32 or Op.LiI64) ||
                !image.MethodIndexByEntryPc.ContainsKey(checked((int)image.Code[pc].Imm)))
                throw new InvalidDataException("Invalid RBC code-address relocation.");
        foreach (InstrDesc instruction in image.Code)
            if (instruction.Op == Op.CallInternal && instruction.Imm != RegisterBytecodeLinker.CStringWriteMethodId && !ids.Contains(checked((int)instruction.Imm)))
                throw new InvalidDataException("An RBC external call has no import record.");
    }

    public byte[] ToBytes()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Utf8, leaveOpen: true);
        writer.Write(Magic);
        WriteString(writer, Name);
        byte[] image = Image.ToExecutableBytes();
        writer.Write(image.Length); writer.Write(image);
        writer.Write(Exports.Length);
        foreach (var export in Exports) { WriteString(writer, export.Name); writer.Write(export.EntryPc); WriteString(writer, export.Signature); }
        writer.Write(Imports.Length);
        foreach (var import in Imports) { writer.Write(import.MethodId); WriteString(writer, import.Module); WriteString(writer, import.Name); WriteString(writer, import.Signature); }
        writer.Write(AddressRelocations.Length);
        foreach (int pc in AddressRelocations) writer.Write(pc);
        writer.Flush();
        return stream.ToArray();
    }

    public static RegisterBytecodeLibrary FromBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream, Utf8);
        if (reader.ReadUInt32() != Magic) throw new InvalidDataException("Not an RBC library or unsupported library version.");
        string name = ReadString(reader);
        int length = Count(reader, 1);
        CodeImage image = ImageSerializer.FromBytes(reader.ReadBytes(length));
        int count = Count(reader, 12);
        var exports = ImmutableArray.CreateBuilder<RegisterBytecodeExport>(count);
        for (int i = 0; i < count; i++) exports.Add(new(ReadString(reader), reader.ReadInt32(), ReadString(reader)));
        count = Count(reader, 16);
        var imports = ImmutableArray.CreateBuilder<RegisterBytecodeImport>(count);
        for (int i = 0; i < count; i++) imports.Add(new(reader.ReadInt32(), ReadString(reader), ReadString(reader), ReadString(reader)));
        count = Count(reader, 4);
        var relocations = ImmutableArray.CreateBuilder<int>(count);
        for (int i = 0; i < count; i++) relocations.Add(reader.ReadInt32());
        if (stream.Position != stream.Length) throw new InvalidDataException("Trailing data in RBC library.");
        return new RegisterBytecodeLibrary(name, image, exports.MoveToImmutable(), imports.MoveToImmutable(), relocations.MoveToImmutable());
    }
    private static int Count(BinaryReader reader, int minimumSize)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > (reader.BaseStream.Length - reader.BaseStream.Position) / minimumSize)
            throw new InvalidDataException("RBC library section is outside the file.");
        return count;
    }
    private static string ReadString(BinaryReader reader) => Utf8.GetString(reader.ReadBytes(Count(reader, 1)));
    private static void WriteString(BinaryWriter writer, string value)
    {
        byte[] bytes = Utf8.GetBytes(value);
        writer.Write(bytes.Length); writer.Write(bytes);
    }
}

internal static class RegisterBytecodeLinker
{
    internal const int CStringWriteMethodId = 10_999_999;

    internal static string AbiValue(int kind, int size, int alignment, IEnumerable<(int Class, int Offset, int Size)> segments)
        => FormattableString.Invariant($"{kind}:{size}:{alignment}[") + string.Join(",", segments.Select(s => FormattableString.Invariant($"{s.Class}:{s.Offset}:{s.Size}"))) + "]";

    internal static RegisterBytecodeImport Import(RuntimeMethod method, TargetInfo target)
    {
        DllImportData import = method.DllImportData ?? throw new NotSupportedException($"Extern method '{method.Name}' has no DllImport metadata.");
        if (!method.IsStatic || method.HasThis || !import.PreserveSig || import.SetLastError ||
            import.CallingConvention is not (System.Runtime.InteropServices.CallingConvention.Cdecl or System.Runtime.InteropServices.CallingConvention.Winapi))
            throw new NotSupportedException($"Unsupported RBC P/Invoke convention on '{method.Name}'.");
        string Value(RuntimeType type, bool result)
        {
            AbiValueInfo abi = MachineAbi.ClassifyValue(type, MachineAbi.StackKindForType(type), result, target);
            if (abi.PassingKind == AbiValuePassingKind.Void) return AbiValue(0, 0, 1, []);
            if (type.ContainsGcPointers || type.IsReferenceType || type.Kind == RuntimeTypeKind.ByRef || type.PrimitiveKind is RuntimePrimitiveKind.Boolean or RuntimePrimitiveKind.Char)
                throw new NotSupportedException($"RBC P/Invoke '{method.Name}' requires blittable values or unmanaged pointers; '{type.Name}' requires marshalling.");
            IEnumerable<(int, int, int)> segments = abi.PassingKind == AbiValuePassingKind.ScalarRegister
                ? [(abi.RegisterClass == RegisterClass.Float ? 1 : 0, 0, abi.Size)]
                : abi.PassingKind == AbiValuePassingKind.MultiRegister ? abi.RegisterSegments.Select(s => (s.RegisterClass == RegisterClass.Float ? 1 : 0, s.Offset, s.Size)) : [];
            return AbiValue((int)abi.PassingKind, abi.Size, abi.Alignment, segments);
        }
        string signature = Value(method.ReturnType, true) + "(" + string.Join(";", method.ParameterTypes.Select(t => Value(t, false))) + ")";
        return new(method.MethodId, import.ModuleName, string.IsNullOrEmpty(import.EntryPointName) ? method.Name : import.EntryPointName, signature);
    }

    internal static CodeImage Link(CodeImage application, IReadOnlyList<RegisterBytecodeLibrary> libraries)
    {
        var offsets = new Dictionary<RegisterBytecodeLibrary, int>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        int codeSize = application.Code.Length;
        foreach (var library in libraries)
        {
            if (!names.Add(ModuleName(library.Name))) throw new InvalidOperationException($"Duplicate RBC library '{library.Name}'.");
            if ((application.Flags & ImageFlags.Target32) != (library.Image.Flags & ImageFlags.Target32))
                throw new InvalidOperationException($"RBC library '{library.Name}' has a different pointer width.");
            offsets.Add(library, codeSize);
            codeSize = checked(codeSize + library.Image.Code.Length);
        }
        int Resolve(RegisterBytecodeImport import)
        {
            string module = ModuleName(import.Module);
            IEnumerable<RegisterBytecodeLibrary> candidates = import.Module.Length == 0 ? libraries : libraries.Where(l => ModuleName(l.Name) == module);
            if (import.Module.Length != 0 && !candidates.Any() && module.StartsWith("lib", StringComparison.Ordinal) &&
                (import.Module.EndsWith(".so", StringComparison.Ordinal) || import.Module.EndsWith(".dylib", StringComparison.Ordinal)))
                candidates = libraries.Where(l => ModuleName(l.Name) == module[3..]);
            var matches = candidates
                .SelectMany(l => l.Exports.Where(e => e.Name == import.Name).Select(e => (Library: l, Export: e))).ToArray();
            if (matches.Length != 1) throw new MissingMethodException($"RBC import '{import.Module}!{import.Name}' has {matches.Length} matching exports.");
            var match = matches[0];
            if (import.Signature != match.Export.Signature) throw new InvalidOperationException($"RBC ABI mismatch for '{import.Module}!{import.Name}'.");
            return checked(offsets[match.Library] + match.Export.EntryPc);
        }
        var imports = application.Imports.ToDictionary(i => application.GetMethod(i.MethodId).EntryPc, Resolve);
        var code = application.Code.ToBuilder();
        var methods = application.Methods.ToBuilder();
        var unwind = application.Unwind.ToBuilder();
        var switches = application.SwitchTable.ToBuilder();
        var blob = application.Blob.ToBuilder();
        var addresses = application.CodeAddressRelocations.ToHashSet();
        for (int pc = 0; pc < code.Count; pc++)
            if ((code[pc].Op == Op.Call || addresses.Contains(pc)) && imports.TryGetValue(checked((int)code[pc].Imm), out int target))
                code[pc] = code[pc].WithTargetPc(target);
        foreach (var library in libraries)
        {
            CodeImage image = library.Image;
            int codeBase = code.Count, blobBase = blob.Count, switchBase = switches.Count, unwindBase = unwind.Count;
            var external = library.Imports.ToDictionary(i => i.MethodId, Resolve);
            var relocations = library.AddressRelocations.ToHashSet();
            for (int pc = 0; pc < image.Code.Length; pc++)
            {
                InstrDesc instruction = image.Code[pc];
                if (instruction.Op == Op.CallInternal && external.TryGetValue(checked((int)instruction.Imm), out int target))
                    instruction = InstrDesc.Call(Op.Call, target, (CallFlags)instruction.Aux & ~CallFlags.InternalCall);
                else if (CodeImage.IsPcTargetInstruction(instruction.Op) || instruction.Op == Op.Call || relocations.Contains(pc))
                    instruction = instruction.WithTargetPc(checked((int)instruction.Imm + codeBase));
                else if (instruction.Op is Op.SwitchI32 or Op.SwitchI64)
                    instruction = instruction.WithTargetPc(checked((int)instruction.Imm + switchBase));
                else if (instruction.Op is Op.StaticData or Op.GlobalData)
                {
                    int offset = checked((int)(instruction.Imm >> 32) + blobBase);
                    instruction = new(instruction.Op, instruction.Rd, instruction.Rs1, instruction.Rs2, instruction.Rs3, instruction.Aux,
                        ((long)offset << 32) | (uint)instruction.Imm);
                }
                code.Add(instruction);
            }
            // Unmanaged frames have no metadata method id; their unwind records still describe the shared VM stack
            foreach (var method in image.Methods)
                methods.Add(new(-1, -1, checked(method.EntryPc + codeBase), method.FrameSize, method.OutgoingArgumentAreaOffset,
                    0, 0, 0, 0, checked(method.UnwindStartIndex + unwindBase), method.UnwindCount, method.Flags));
            foreach (var record in image.Unwind)
                unwind.Add(new(checked(record.Pc + codeBase), (UnwindCodeKind)record.Kind, RegisterVmIsa.DecodeRegister(record.Register), record.StackOffset, record.Size));
            foreach (var record in image.SwitchTable) switches.Add(new(record.Key, checked(record.TargetPc + codeBase)));
            blob.AddRange(image.Blob);
        }
        return new CodeImage(application.Flags | (unwind.Count == 0 ? ImageFlags.None : ImageFlags.HasUnwindInfo), code.ToImmutable(), methods.ToImmutable(),
            application.EhRegions, application.GcSafePoints, application.GcRoots, unwind.ToImmutable(), switches.ToImmutable(),
            application.TypeLayouts, application.VTables, blob.ToImmutable());
    }

    private static string ModuleName(string name)
    {
        foreach (string extension in new[] { ".dll", ".so", ".dylib", RegisterBytecodeLibrary.Extension })
            if (name.EndsWith(extension, StringComparison.Ordinal)) return name[..^extension.Length];
        return name;
    }

    internal static void RegisterRuntimeImports(CodeImage image, RuntimeTypeSystem types)
    {
        if (!image.Code.Any(i => i.Op == Op.CallInternal && i.Imm == CStringWriteMethodId)) return;
        try
        {
            RuntimeMethod existing = types.GetMethodById(CStringWriteMethodId);
            if (existing.DeclaringType.Namespace != "System" || existing.DeclaringType.Name != "Console" || existing.Name != "_Write")
                throw new InvalidOperationException("The RBC runtime import id is already in use.");
            return;
        }
        catch (MissingMethodException) { }
        RuntimeType console = types.GetRequiredNamedType("std", "System", "Console");
        RuntimeType result = types.GetRequiredNamedType("std", "System", "Void");
        RuntimeType bytes = types.GetPointerType(types.GetRequiredNamedType("std", "System", "Byte"));
        types.RegisterSyntheticStaticMethod(console, "_Write", result, [bytes], 0x1000, CStringWriteMethodId);
    }
}
