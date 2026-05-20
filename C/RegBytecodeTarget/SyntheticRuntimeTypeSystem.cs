using Cnidaria.Cs;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Cnidaria.C
{
    internal sealed class RegisterBytecodeSyntheticRuntime
    {
        public RuntimeTypeSystem RuntimeTypes { get; }
        public Dictionary<string, RuntimeModule> Modules { get; }
        public RuntimeMethod EntryMethod { get; }

        public RegisterBytecodeSyntheticRuntime(
            RuntimeTypeSystem runtimeTypes,
            Dictionary<string, RuntimeModule> modules,
            RuntimeMethod entryMethod)
        {
            RuntimeTypes = runtimeTypes ?? throw new ArgumentNullException(nameof(runtimeTypes));
            Modules = modules ?? throw new ArgumentNullException(nameof(modules));
            EntryMethod = entryMethod ?? throw new ArgumentNullException(nameof(entryMethod));
        }
    }
    internal sealed class MinimalCRuntimeMetadataView : IMetadataView
    {
        private static readonly string[] Strings =
        {
            string.Empty,
            "System",
            "Object",
            "ValueType",
            "Enum",
            "String",
            "Array",
            "Void",
            "Boolean",
            "Char",
            "SByte",
            "Byte",
            "Int16",
            "UInt16",
            "Int32",
            "UInt32",
            "Single",
            "Int64",
            "UInt64",
            "Double",
            "Decimal",
            "IntPtr",
            "UIntPtr",
        };

        private static readonly TypeDefRow[] TypeDefs = BuildTypeDefs();

        public string ModuleName => "std";
        public string DefaultExternalAssemblyName => "std";

        public int GetRowCount(MetadataTableKind table)
            => table == MetadataTableKind.TypeDef ? TypeDefs.Length : 0;

        public string GetString(int index)
        {
            if ((uint)index >= (uint)Strings.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return Strings[index];
        }

        public string GetUserString(int index) => string.Empty;
        public ReadOnlySpan<byte> GetBlob(int index) => ReadOnlySpan<byte>.Empty;
        public int GetBlobLength(int index) => 0;

        public bool TryCopyBlob(int index, Span<byte> destination, out int bytesWritten)
        {
            bytesWritten = 0;
            return true;
        }

        public TypeDefRow GetTypeDef(int rid)
        {
            if (rid <= 0 || rid > TypeDefs.Length)
                throw new ArgumentOutOfRangeException(nameof(rid));
            return TypeDefs[rid - 1];
        }

        public AssemblyRefRow GetAssemblyRef(int rid) => throw EmptyTable(nameof(GetAssemblyRef), rid);
        public TypeRefRow GetTypeRef(int rid) => throw EmptyTable(nameof(GetTypeRef), rid);
        public NestedClassRow GetNestedClass(int rid) => throw EmptyTable(nameof(GetNestedClass), rid);
        public InterfaceImplRow GetInterfaceImpl(int rid) => throw EmptyTable(nameof(GetInterfaceImpl), rid);
        public MethodImplRow GetMethodImpl(int rid) => throw EmptyTable(nameof(GetMethodImpl), rid);
        public FieldRow GetField(int rid) => throw EmptyTable(nameof(GetField), rid);
        public MethodDefRow GetMethodDef(int rid) => throw EmptyTable(nameof(GetMethodDef), rid);
        public ParamRow GetParam(int rid) => throw EmptyTable(nameof(GetParam), rid);
        public MemberRefRow GetMemberRef(int rid) => throw EmptyTable(nameof(GetMemberRef), rid);
        public TypeSpecRow GetTypeSpec(int rid) => throw EmptyTable(nameof(GetTypeSpec), rid);
        public MethodSpecRow GetMethodSpec(int rid) => throw EmptyTable(nameof(GetMethodSpec), rid);
        public ConstantRow GetConstant(int rid) => throw EmptyTable(nameof(GetConstant), rid);
        public PropertyRow GetProperty(int rid) => throw EmptyTable(nameof(GetProperty), rid);
        public CustomAttributeRow GetCustomAttribute(int rid) => throw EmptyTable(nameof(GetCustomAttribute), rid);


        private static TypeDefRow[] BuildTypeDefs()
        {
            const int nsSystem = 1;
            const int objectName = 2;
            const int valueTypeName = 3;
            const int enumName = 4;
            const int stringName = 5;
            const int arrayName = 6;

            var classFlags = (int)(TypeAttributes.Public | TypeAttributes.Class);
            var sealedFlags = classFlags | (int)TypeAttributes.Sealed;

            var rows = new List<TypeDefRow>
            {
                TypeDef(classFlags, objectName, nsSystem, extendsRid: 0),
                TypeDef(classFlags, valueTypeName, nsSystem, extendsRid: 1),
                TypeDef(classFlags, enumName, nsSystem, extendsRid: 2),
                TypeDef(sealedFlags, stringName, nsSystem, extendsRid: 1),
                TypeDef(classFlags, arrayName, nsSystem, extendsRid: 1),
            };

            for (var nameIndex = 7; nameIndex < Strings.Length; nameIndex++)
                rows.Add(TypeDef(sealedFlags, nameIndex, nsSystem, extendsRid: 2));

            return rows.ToArray();
        }

        private static TypeDefRow TypeDef(int flags, int name, int ns, int extendsRid)
            => new TypeDefRow(
                flags: flags,
                name: name,
                @namespace: ns,
                extendsEncoded: extendsRid == 0 ? 0 : EncodeTypeDefOrRefTypeDef(extendsRid),
                fieldList: 1,
                methodList: 1);

        private static int EncodeTypeDefOrRefTypeDef(int rid) => rid << 2;

        private static Exception EmptyTable(string member, int rid)
            => new ArgumentOutOfRangeException(nameof(rid), rid, member + " is empty in synthetic C runtime metadata.");
    }
}
