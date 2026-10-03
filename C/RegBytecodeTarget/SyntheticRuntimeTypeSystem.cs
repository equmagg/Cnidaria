using Cnidaria.Cs;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Cnidaria.C;

public sealed class RegisterBytecodeSyntheticRuntime
{
    public RuntimeTypeSystem RuntimeTypes { get; }
    public Dictionary<string, RuntimeModule> Modules { get; }
    public int EntryPc { get; }

    /// <summary>Creates the host interface that installs delegates over this program's C functions</summary>
    public HostInterface CreateHostInterface(RegisterBasedVm machine)
    {
        if (machine is null)
            throw new ArgumentNullException(nameof(machine));
        return new HostInterface(
            machine,
            RuntimeTypes,
            Modules,
            RegisterBytecodeProgram.CProgramAssemblyName,
            RegisterBytecodeProgram.CProgramTypeFullName);
    }

    public RegisterBytecodeSyntheticRuntime(
        RuntimeTypeSystem runtimeTypes,
        Dictionary<string, RuntimeModule> modules,
        int entryPc)
    {
        RuntimeTypes = runtimeTypes ?? throw new ArgumentNullException(nameof(runtimeTypes));
        Modules = modules ?? throw new ArgumentNullException(nameof(modules));
        if (entryPc < 0)
            throw new ArgumentOutOfRangeException(nameof(entryPc));
        EntryPc = entryPc;
    }
}
internal static class MinimalCRuntimeMetadata
{
    private static readonly string[] PrimitiveNames =
    {
        "Void", "Boolean", "Char", "SByte", "Byte", "Int16", "UInt16", "Int32", "UInt32",
        "Single", "Int64", "UInt64", "Double", "Decimal", "IntPtr", "UIntPtr",
    };

    public static EcmaMetadata Instance { get; } = new EcmaMetadata(Build());

    private static byte[] Build()
    {
        var image = new MetadataImage("std");
        int system = image.Strings.Add("System");
        var classFlags = (int)(TypeAttributes.Public | TypeAttributes.Class);
        var sealedFlags = classFlags | (int)TypeAttributes.Sealed;

        void Add(int flags, string name, int ns, int extendsRid)
            => image.TypeDefs.Add(new TypeDefRow(flags, image.Strings.Add(name), ns, extendsRid << 2, fieldList: 1, methodList: 1));

        const int ObjectRid = 2;
        const int ValueTypeRid = 3;
        Add(0, "<Module>", 0, 0);
        Add(classFlags, "Object", system, 0);
        Add(classFlags, "ValueType", system, ObjectRid);
        Add(classFlags, "Enum", system, ValueTypeRid);
        Add(sealedFlags, "String", system, ObjectRid);
        Add(classFlags, "Array", system, ObjectRid);
        foreach (string name in PrimitiveNames)
            Add(sealedFlags, name, system, ValueTypeRid);

        return EcmaImageWriter.Write(image);
    }
}
