using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;

namespace Cnidaria.Cs
{
    public enum VmValueKind : byte
    {
        Null,
        I4,
        I8,
        R8,
        Ref,
        Ptr,
        ByRef,
        Value,
    }

    public readonly struct VmValue
    {
        public VmValueKind Kind { get; }
        public long Payload { get; }
        public int Aux { get; }

        public VmValue(VmValueKind kind, long payload, int aux = 0)
        {
            Kind = kind;
            Payload = payload;
            Aux = aux;
        }

        public int AsInt32()
        {
            return Kind switch
            {
                VmValueKind.I4 => unchecked((int)Payload),
                VmValueKind.I8 => checked((int)Payload),
                VmValueKind.R8 => checked((int)BitConverter.Int64BitsToDouble(Payload)),
                VmValueKind.Null => 0,
                _ => throw new InvalidOperationException($"Expected I4/I8/R8, got {Kind}")
            };
        }

        public long AsInt64()
        {
            return Kind switch
            {
                VmValueKind.I8 => Payload,
                VmValueKind.I4 => unchecked((int)Payload),
                VmValueKind.R8 => checked((long)BitConverter.Int64BitsToDouble(Payload)),
                VmValueKind.Null => 0,
                _ => throw new InvalidOperationException($"Expected I4/I8/R8, got {Kind}")
            };
        }

        public double AsDouble()
        {
            return Kind switch
            {
                VmValueKind.R8 => BitConverter.Int64BitsToDouble(Payload),
                VmValueKind.I4 => unchecked((int)Payload),
                VmValueKind.I8 => Payload,
                _ => throw new InvalidOperationException($"Expected I4/I8/R8, got {Kind}")
            };
        }

        public bool AsBool() => AsInt32() != 0;
        public char AsChar() => (char)AsInt32();

        internal VmValue(Cnidaria.Cs.Slot s)
        {
            switch (s.Kind)
            {
                case Cnidaria.Cs.SlotKind.Null: Kind = VmValueKind.Null; Payload = 0; Aux = 0; break;
                case Cnidaria.Cs.SlotKind.I4: Kind = VmValueKind.I4; Payload = s.Payload; Aux = 0; break;
                case Cnidaria.Cs.SlotKind.I8: Kind = VmValueKind.I8; Payload = s.Payload; Aux = 0; break;
                case Cnidaria.Cs.SlotKind.R8: Kind = VmValueKind.R8; Payload = s.Payload; Aux = 0; break;
                case Cnidaria.Cs.SlotKind.Ref: Kind = VmValueKind.Ref; Payload = s.Payload; Aux = 0; break;
                case Cnidaria.Cs.SlotKind.Ptr: Kind = VmValueKind.Ptr; Payload = s.Payload; Aux = s.Aux; break;
                case Cnidaria.Cs.SlotKind.ByRef: Kind = VmValueKind.ByRef; Payload = s.Payload; Aux = s.Aux; break;
                case Cnidaria.Cs.SlotKind.Value: Kind = VmValueKind.Value; Payload = s.Payload; Aux = s.Aux; break;
                default: throw new InvalidOperationException($"Unknown Cnidaria.Cs.SlotKind: {s.Kind}");
            }
        }

        internal Cnidaria.Cs.Slot ToSlot()
        {
            return Kind switch
            {
                VmValueKind.Null => new Cnidaria.Cs.Slot(Cnidaria.Cs.SlotKind.Null, 0),
                VmValueKind.I4 => new Cnidaria.Cs.Slot(Cnidaria.Cs.SlotKind.I4, Payload),
                VmValueKind.I8 => new Cnidaria.Cs.Slot(Cnidaria.Cs.SlotKind.I8, Payload),
                VmValueKind.R8 => new Cnidaria.Cs.Slot(Cnidaria.Cs.SlotKind.R8, Payload),
                VmValueKind.Ref => new Cnidaria.Cs.Slot(Cnidaria.Cs.SlotKind.Ref, Payload),
                VmValueKind.Ptr => new Cnidaria.Cs.Slot(Cnidaria.Cs.SlotKind.Ptr, Payload, Aux),
                VmValueKind.ByRef => new Cnidaria.Cs.Slot(Cnidaria.Cs.SlotKind.ByRef, Payload, Aux),
                VmValueKind.Value => new Cnidaria.Cs.Slot(Cnidaria.Cs.SlotKind.Value, Payload, Aux),
                _ => throw new InvalidOperationException($"Unknown VmValueKind: {Kind}")
            };
        }

        public static VmValue FromInt32(int v) => new VmValue(VmValueKind.I4, v);
        public static VmValue FromInt64(long v) => new VmValue(VmValueKind.I8, v);
        public static VmValue FromDouble(double v) => new VmValue(VmValueKind.R8, BitConverter.DoubleToInt64Bits(v));
        public static VmValue Null => new VmValue(VmValueKind.Null, 0);
    }

    public sealed class VmCallContext
    {
        private readonly Cnidaria.Cs.StackBasedVm? _stackVm;
        private readonly Cnidaria.Cs.RegisterBasedVm? _registerVm;
        private CancellationToken _ct;

        internal VmCallContext(Cnidaria.Cs.StackBasedVm vm) => _stackVm = vm ?? throw new ArgumentNullException(nameof(vm));
        internal VmCallContext(Cnidaria.Cs.RegisterBasedVm vm) => _registerVm = vm ?? throw new ArgumentNullException(nameof(vm));

        internal void SetToken(CancellationToken ct) => _ct = ct;

        public CancellationToken CancellationToken => _ct;
        public string? ReadString(VmValue v)
            => _stackVm != null ? _stackVm.HostReadString(v, _ct) : _registerVm!.HostReadString(v, _ct);
        public VmValue NewString(string? s)
            => _stackVm != null ? _stackVm.HostAllocString(s) : _registerVm!.HostAllocString(s);
        public int GetAddress(VmValue v)
            => _stackVm != null ? _stackVm.HostGetAddress(v) : _registerVm!.HostGetAddress(v);
        public ReadOnlySpan<byte> ReadOnlyMemory(int address, int size)
        {
            if (_stackVm != null) return _stackVm.HostGetSpan(address, size, writable: false);
            return _registerVm!.HostGetSpan(address, size, writable: false);
        }
        public Span<byte> Memory(int address, int size)
        {
            if (_stackVm != null) return _stackVm.HostGetSpan(address, size, writable: true);
            return _registerVm!.HostGetSpan(address, size, writable: true);
        }
        public int GetArrayLength(VmValue array)
            => _stackVm != null ? _stackVm.HostGetArrayLength(array) : _registerVm!.HostGetArrayLength(array);
        public VmValue GetArrayElement(VmValue array, int index)
            => _stackVm != null ? _stackVm.HostGetArrayElement(array, index) : _registerVm!.HostGetArrayElement(array, index);

        public int ReadInt32(int address) => BinaryPrimitives.ReadInt32LittleEndian(ReadOnlyMemory(address, 4));
        public long ReadInt64(int address) => BinaryPrimitives.ReadInt64LittleEndian(ReadOnlyMemory(address, 8));
        public ushort ReadUInt16(int address) => BinaryPrimitives.ReadUInt16LittleEndian(ReadOnlyMemory(address, 2));
        public void WriteInt32(int address, int value) => BinaryPrimitives.WriteInt32LittleEndian(Memory(address, 4), value);
        public void WriteInt64(int address, long value) => BinaryPrimitives.WriteInt64LittleEndian(Memory(address, 8), value);
        public void WriteUInt16(int address, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(Memory(address, 2), value);

    }

    public delegate VmValue HostMethod(VmCallContext ctx, ReadOnlySpan<VmValue> args);

    internal sealed class HostOverride
    {
        public readonly int MethodId;
        public readonly HostMethod Handler;

        public HostOverride(Cnidaria.Cs.RuntimeMethod method, HostMethod handler)
        {
            if (method is null) throw new ArgumentNullException(nameof(method));
            if (!method.HasInternalCall)
                throw new InvalidOperationException($"Host override target must be marked InternalCall: {method.DeclaringType.Namespace}.{method.DeclaringType.Name}.{method.Name}");
            MethodId = method.MethodId;
            Handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

    }

    public sealed class HostInterface
    {
        private readonly Cnidaria.Cs.StackBasedVm? _stackVm;
        private readonly Cnidaria.Cs.RegisterBasedVm? _registerVm;
        private readonly Cnidaria.Cs.RuntimeTypeSystem _rts;
        private readonly IReadOnlyDictionary<string, Cnidaria.Cs.RuntimeModule> _modules;

        internal HostInterface(Cnidaria.Cs.StackBasedVm vm, Cnidaria.Cs.RuntimeTypeSystem rts, IReadOnlyDictionary<string, Cnidaria.Cs.RuntimeModule> modules)
        {
            _stackVm = vm ?? throw new ArgumentNullException(nameof(vm));
            _rts = rts ?? throw new ArgumentNullException(nameof(rts));
            _modules = modules ?? throw new ArgumentNullException(nameof(modules));
        }

        internal HostInterface(Cnidaria.Cs.RegisterBasedVm vm, Cnidaria.Cs.RuntimeTypeSystem rts, IReadOnlyDictionary<string, Cnidaria.Cs.RuntimeModule> modules)
        {
            _registerVm = vm ?? throw new ArgumentNullException(nameof(vm));
            _rts = rts ?? throw new ArgumentNullException(nameof(rts));
            _modules = modules ?? throw new ArgumentNullException(nameof(modules));
        }

        public void OverrideStatic(string assemblyName, string typeFullName, string methodName, Delegate handler)
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            var stackSig = ExtractSignatureStack(handler);
            var stackMethod = ResolveStaticMethodStack(assemblyName, typeFullName, methodName, stackSig.ParamTypes, stackSig.ReturnType);
            RegisterHostOverride(new HostOverride(stackMethod, BuildWrapperStack(handler, stackSig, stackMethod)));
        }

        public void OverrideStaticRaw(string assemblyName, string typeFullName, string methodName, Type returnType, Type[] paramTypes, HostMethod handler)
        {
            if (returnType is null) throw new ArgumentNullException(nameof(returnType));
            if (paramTypes is null) throw new ArgumentNullException(nameof(paramTypes));
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            var stackParams = new Cnidaria.Cs.RuntimeType[paramTypes.Length];
            for (int i = 0; i < paramTypes.Length; i++)
                stackParams[i] = MapClrTypeToVmStack(paramTypes[i]);
            var stackRet = MapClrTypeToVmStack(returnType);
            var stackMethod = ResolveStaticMethodStack(assemblyName, typeFullName, methodName, stackParams, stackRet);
            RegisterHostOverride(new HostOverride(stackMethod, handler));
        }
        private void RegisterHostOverride(HostOverride ov)
        {
            if (_stackVm != null)
            {
                _stackVm.RegisterHostOverride(ov);
                return;
            }

            _registerVm!.RegisterHostOverride(ov);
        }

        private (bool HasContext, Type ReturnClr, Cnidaria.Cs.RuntimeType ReturnType, Type[] ParamClr, Cnidaria.Cs.RuntimeType[] ParamTypes) ExtractSignatureStack(Delegate d)
        {
            var mi = d.Method;
            var ps = mi.GetParameters();
            int offset = ps.Length > 0 && ps[0].ParameterType == typeof(VmCallContext) ? 1 : 0;
            var paramClr = new Type[ps.Length - offset];
            var paramVm = new Cnidaria.Cs.RuntimeType[paramClr.Length];
            for (int i = 0; i < paramClr.Length; i++)
            {
                paramClr[i] = ps[i + offset].ParameterType;
                paramVm[i] = MapClrTypeToVmStack(paramClr[i]);
            }
            Type retClr = mi.ReturnType;
            return (offset != 0, retClr, MapClrTypeToVmStack(retClr), paramClr, paramVm);
        }


        private HostMethod BuildWrapperStack(Delegate handler, (bool HasContext, Type ReturnClr, Cnidaria.Cs.RuntimeType ReturnType, Type[] ParamClr, Cnidaria.Cs.RuntimeType[] ParamTypes) sig, Cnidaria.Cs.RuntimeMethod targetMethod)
        {
            ValidateHostSignature(sig.ParamClr, sig.ReturnClr);
            var invokeArgs = new object?[sig.ParamClr.Length + (sig.HasContext ? 1 : 0)];
            return (ctx, args) =>
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();
                if (args.Length != sig.ParamTypes.Length) throw new InvalidOperationException("Arity mismatch for host override.");
                int dst = 0;
                if (sig.HasContext) invokeArgs[dst++] = ctx;
                for (int i = 0; i < sig.ParamClr.Length; i++) invokeArgs[dst + i] = ConvertArg(ctx, args[i], sig.ParamClr[i]);
                object? ret = handler.DynamicInvoke(invokeArgs);
                return targetMethod.ReturnType.Namespace == "System" && targetMethod.ReturnType.Name == "Void" ? VmValue.Null : ConvertRetStack(ctx, ret, sig.ReturnClr, targetMethod.ReturnType);
            };
        }

        private static void ValidateHostSignature(Type[] paramClr, Type returnClr)
        {
            for (int i = 0; i < paramClr.Length; i++)
                if (paramClr[i].IsByRefLike)
                    throw new NotSupportedException($"Delegate parameter '{paramClr[i]}' is byref-like. Use OverrideStaticRaw + HostMethod instead.");
            if (returnClr.IsByRefLike)
                throw new NotSupportedException($"Delegate return '{returnClr}' is byref-like. Use OverrideStaticRaw + HostMethod instead.");
        }

        private object? ConvertArg(VmCallContext ctx, VmValue v, Type clr)
        {
            if (clr.IsEnum)
            {
                var underlying = Enum.GetUnderlyingType(clr);
                var raw = ConvertArg(ctx, v, underlying) ?? Activator.CreateInstance(underlying)!;
                return Enum.ToObject(clr, raw);
            }
            if (clr.IsArray)
            {
                if (clr.GetArrayRank() != 1) throw new NotSupportedException("Only SZARRAY supported in host marshaling.");
                if (v.Kind == VmValueKind.Null) return null;
                var elementClr = clr.GetElementType() ?? throw new InvalidOperationException("Array without element type.");
                int length = ctx.GetArrayLength(v);
                var result = Array.CreateInstance(elementClr, length);
                for (int i = 0; i < length; i++) result.SetValue(ConvertArg(ctx, ctx.GetArrayElement(v, i), elementClr), i);
                return result;
            }
            if (clr == typeof(string)) return ctx.ReadString(v);
            if (clr == typeof(int)) return v.AsInt32();
            if (clr == typeof(uint)) return unchecked((uint)v.AsInt32());
            if (clr == typeof(short)) return unchecked((short)v.AsInt32());
            if (clr == typeof(ushort)) return unchecked((ushort)v.AsInt32());
            if (clr == typeof(byte)) return unchecked((byte)v.AsInt32());
            if (clr == typeof(sbyte)) return unchecked((sbyte)v.AsInt32());
            if (clr == typeof(bool)) return v.AsBool();
            if (clr == typeof(char)) return v.AsChar();
            if (clr == typeof(long)) return v.AsInt64();
            if (clr == typeof(ulong)) return unchecked((ulong)v.AsInt64());
            if (clr == typeof(double)) return v.AsDouble();
            if (clr == typeof(float)) return (float)v.AsDouble();
            if (clr == typeof(IntPtr)) return TargetArchitecture.PointerSize == 8 ? new IntPtr(v.AsInt64()) : new IntPtr(v.AsInt32());
            if (clr == typeof(UIntPtr)) return TargetArchitecture.PointerSize == 8 ? new UIntPtr(unchecked((ulong)v.AsInt64())) : new UIntPtr(unchecked((uint)v.AsInt32()));
            throw new NotSupportedException($"Host arg type not supported: {clr.FullName}");
        }

        private VmValue ConvertRetStack(VmCallContext ctx, object? retObj, Type clr, Cnidaria.Cs.RuntimeType actualVmType)
        {
            if (clr.IsEnum)
            {
                var underlying = Enum.GetUnderlyingType(clr);
                object raw = retObj ?? Activator.CreateInstance(underlying)!;
                if (raw.GetType() != underlying) raw = Convert.ChangeType(raw, underlying, CultureInfo.InvariantCulture)!;
                return ConvertRetStack(ctx, raw, underlying, actualVmType);
            }
            if (clr.IsArray)
            {
                if (clr.GetArrayRank() != 1) throw new NotSupportedException("Only SZARRAY supported in host marshaling.");
                if (retObj is null) return VmValue.Null;
                if (retObj is not Array arr) throw new InvalidOperationException($"Expected array return value for '{clr.FullName}'.");
                if (actualVmType.Kind != Cnidaria.Cs.RuntimeTypeKind.Array || actualVmType.ElementType is null) throw new InvalidOperationException($"VM return type '{actualVmType.Namespace}.{actualVmType.Name}' is not an array.");
                var elementClr = clr.GetElementType() ?? throw new InvalidOperationException("Array without element type.");
                var vmArr = _stackVm != null ? _stackVm.HostAllocArray(actualVmType, arr.Length) : _registerVm!.HostAllocArray(actualVmType, arr.Length);
                for (int i = 0; i < arr.Length; i++)
                {
                    VmValue elem = ConvertRetStack(ctx, arr.GetValue(i), elementClr, actualVmType.ElementType);
                    if (_stackVm != null) _stackVm.HostSetArrayElement(vmArr, i, elem);
                    else _registerVm!.HostSetArrayElement(vmArr, i, elem);
                }
                return vmArr;
            }
            return ConvertScalarRet(ctx, retObj, clr);
        }

        private static VmValue ConvertScalarRet(VmCallContext ctx, object? retObj, Type clr)
        {
            if (clr == typeof(void)) return VmValue.Null;
            if (clr == typeof(string)) return ctx.NewString((string?)retObj);
            if (clr == typeof(int)) return VmValue.FromInt32((int)(retObj ?? 0));
            if (clr == typeof(uint)) return VmValue.FromInt32(unchecked((int)(uint)(retObj ?? 0u)));
            if (clr == typeof(short)) return VmValue.FromInt32(unchecked((short)(retObj ?? (short)0)));
            if (clr == typeof(ushort)) return VmValue.FromInt32(unchecked((ushort)(retObj ?? (ushort)0)));
            if (clr == typeof(byte)) return VmValue.FromInt32(unchecked((byte)(retObj ?? (byte)0)));
            if (clr == typeof(sbyte)) return VmValue.FromInt32(unchecked((sbyte)(retObj ?? (sbyte)0)));
            if (clr == typeof(bool)) return VmValue.FromInt32(((bool)(retObj ?? false)) ? 1 : 0);
            if (clr == typeof(char)) return VmValue.FromInt32((char)(retObj ?? '\0'));
            if (clr == typeof(long)) return VmValue.FromInt64((long)(retObj ?? 0L));
            if (clr == typeof(ulong)) return VmValue.FromInt64(unchecked((long)(ulong)(retObj ?? 0UL)));
            if (clr == typeof(double)) return VmValue.FromDouble((double)(retObj ?? 0.0));
            if (clr == typeof(float)) return VmValue.FromDouble((float)(retObj ?? 0.0f));
            if (clr == typeof(IntPtr))
            {
                var ip = (IntPtr)(retObj ?? IntPtr.Zero);
                long raw = TargetArchitecture.PointerSize == 8 ? ip.ToInt64() : ip.ToInt32();
                return TargetArchitecture.PointerSize == 8 ? VmValue.FromInt64(raw) : VmValue.FromInt32((int)raw);
            }
            if (clr == typeof(UIntPtr))
            {
                var up = (UIntPtr)(retObj ?? UIntPtr.Zero);
                ulong raw = up.ToUInt64();
                return TargetArchitecture.PointerSize == 8 ? VmValue.FromInt64(unchecked((long)raw)) : VmValue.FromInt32(unchecked((int)(uint)raw));
            }
            throw new NotSupportedException($"Host return type not supported: {clr.FullName}");
        }


        private Cnidaria.Cs.RuntimeMethod ResolveStaticMethodStack(string assemblyName, string typeFullName, string methodName, Cnidaria.Cs.RuntimeType[] ps, Cnidaria.Cs.RuntimeType ret)
        {
            if (!_modules.TryGetValue(assemblyName, out var mod)) throw new TypeLoadException($"Module '{assemblyName}' not loaded.");
            SplitTypeFullName(typeFullName, out var ns, out var name);
            if (!mod.TypeDefByFullName.TryGetValue((ns, name), out var typeDefTok)) throw new TypeLoadException($"Type '{ns}.{name}' not found in '{assemblyName}'.");
            var owner = _rts.ResolveType(mod, typeDefTok);
            Cnidaria.Cs.RuntimeMethod? match = null;
            int bestScore = int.MaxValue;
            for (int i = 0; i < owner.Methods.Length; i++)
            {
                var m = owner.Methods[i];
                if (!m.IsStatic || m.HasThis || !m.HasInternalCall || !StringComparer.Ordinal.Equals(m.Name, methodName) || m.ParameterTypes.Length != ps.Length) continue;
                if (!TryGetHostTypeMatchCostStack(m.ReturnType, ret, out int score)) continue;
                bool ok = true;
                for (int p = 0; p < ps.Length; p++)
                {
                    if (!TryGetHostTypeMatchCostStack(m.ParameterTypes[p], ps[p], out int c)) { ok = false; break; }
                    score += c;
                }
                if (!ok) continue;
                if (score < bestScore) { bestScore = score; match = m; }
                else if (score == bestScore) match = null;
            }
            return match ?? throw new MissingMethodException($"Static method '{typeFullName}.{methodName}' not found or ambiguous in '{assemblyName}'.");
        }

        private bool TryGetHostTypeMatchCostStack(Cnidaria.Cs.RuntimeType actual, Cnidaria.Cs.RuntimeType requested, out int cost)
        {
            if (actual.TypeId == requested.TypeId) { cost = 0; return true; }
            if (actual.Kind == Cnidaria.Cs.RuntimeTypeKind.Enum && actual.ElementType != null && actual.ElementType.TypeId == requested.TypeId) { cost = 1; return true; }
            if (requested.Kind == Cnidaria.Cs.RuntimeTypeKind.Enum && requested.ElementType != null && requested.ElementType.TypeId == actual.TypeId) { cost = 1; return true; }
            cost = 0;
            return false;
        }


        private Cnidaria.Cs.RuntimeType MapClrTypeToVmStack(Type t)
        {
            if (t == typeof(void)) return ResolveStdStack("System", "Void");
            if (t.IsEnum) return MapClrTypeToVmStack(Enum.GetUnderlyingType(t));
            if (t.IsArray)
            {
                if (t.GetArrayRank() != 1) throw new NotSupportedException("Only SZARRAY supported in host marshaling.");
                return _rts.GetArrayType(MapClrTypeToVmStack(t.GetElementType()!));
            }
            if (t == typeof(string)) return _rts.SystemString;
            if (t == typeof(bool)) return ResolveStdStack("System", "Boolean");
            if (t == typeof(char)) return ResolveStdStack("System", "Char");
            if (t == typeof(byte)) return ResolveStdStack("System", "Byte");
            if (t == typeof(sbyte)) return ResolveStdStack("System", "SByte");
            if (t == typeof(short)) return ResolveStdStack("System", "Int16");
            if (t == typeof(ushort)) return ResolveStdStack("System", "UInt16");
            if (t == typeof(int)) return ResolveStdStack("System", "Int32");
            if (t == typeof(uint)) return ResolveStdStack("System", "UInt32");
            if (t == typeof(long)) return ResolveStdStack("System", "Int64");
            if (t == typeof(ulong)) return ResolveStdStack("System", "UInt64");
            if (t == typeof(float)) return ResolveStdStack("System", "Single");
            if (t == typeof(double)) return ResolveStdStack("System", "Double");
            if (t == typeof(IntPtr)) return ResolveStdStack("System", "IntPtr");
            if (t == typeof(UIntPtr)) return ResolveStdStack("System", "UIntPtr");
            throw new NotSupportedException($"CLR type cannot be mapped to VM type: {t.FullName}");
        }


        private Cnidaria.Cs.RuntimeType ResolveStdStack(string ns, string name)
        {
            if (!_modules.TryGetValue("std", out var std)) throw new InvalidOperationException("Std module not loaded.");
            if (!std.TypeDefByFullName.TryGetValue((ns, name), out var tok)) throw new TypeLoadException($"Std type not found: std:{ns}.{name}");
            return _rts.ResolveType(std, tok);
        }

        private static void SplitTypeFullName(string full, out string ns, out string name)
        {
            int lastDot = full.LastIndexOf('.');
            if (lastDot < 0)
            {
                ns = "";
                name = full;
                return;
            }
            ns = full.Substring(0, lastDot);
            name = full.Substring(lastDot + 1);
        }
    }
}
