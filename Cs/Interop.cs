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

        internal VmValue(Cnidaria.Cs.Stack.Slot s)
        {
            switch (s.Kind)
            {
                case Cnidaria.Cs.Stack.SlotKind.Null: Kind = VmValueKind.Null; Payload = 0; Aux = 0; break;
                case Cnidaria.Cs.Stack.SlotKind.I4: Kind = VmValueKind.I4; Payload = s.Payload; Aux = 0; break;
                case Cnidaria.Cs.Stack.SlotKind.I8: Kind = VmValueKind.I8; Payload = s.Payload; Aux = 0; break;
                case Cnidaria.Cs.Stack.SlotKind.R8: Kind = VmValueKind.R8; Payload = s.Payload; Aux = 0; break;
                case Cnidaria.Cs.Stack.SlotKind.Ref: Kind = VmValueKind.Ref; Payload = s.Payload; Aux = 0; break;
                case Cnidaria.Cs.Stack.SlotKind.Ptr: Kind = VmValueKind.Ptr; Payload = s.Payload; Aux = s.Aux; break;
                case Cnidaria.Cs.Stack.SlotKind.ByRef: Kind = VmValueKind.ByRef; Payload = s.Payload; Aux = s.Aux; break;
                case Cnidaria.Cs.Stack.SlotKind.Value: Kind = VmValueKind.Value; Payload = s.Payload; Aux = s.Aux; break;
                default: throw new InvalidOperationException($"Unknown Cnidaria.Cs.Stack.SlotKind: {s.Kind}");
            }
        }

        internal VmValue(Cell c)
        {
            switch (c.Kind)
            {
                case CellKind.Null: Kind = VmValueKind.Null; Payload = 0; Aux = 0; break;
                case CellKind.I4: Kind = VmValueKind.I4; Payload = c.Payload; Aux = 0; break;
                case CellKind.I8: Kind = VmValueKind.I8; Payload = c.Payload; Aux = 0; break;
                case CellKind.R8: Kind = VmValueKind.R8; Payload = c.Payload; Aux = 0; break;
                case CellKind.Ref: Kind = VmValueKind.Ref; Payload = c.Payload; Aux = 0; break;
                case CellKind.Ptr: Kind = VmValueKind.Ptr; Payload = c.Payload; Aux = c.Aux; break;
                case CellKind.ByRef: Kind = VmValueKind.ByRef; Payload = c.Payload; Aux = c.Aux; break;
                case CellKind.Value: Kind = VmValueKind.Value; Payload = c.Payload; Aux = c.Aux; break;
                default: throw new InvalidOperationException($"Unknown CellKind: {c.Kind}");
            }
        }

        internal Cnidaria.Cs.Stack.Slot ToSlot()
        {
            return Kind switch
            {
                VmValueKind.Null => new Cnidaria.Cs.Stack.Slot(Cnidaria.Cs.Stack.SlotKind.Null, 0),
                VmValueKind.I4 => new Cnidaria.Cs.Stack.Slot(Cnidaria.Cs.Stack.SlotKind.I4, Payload),
                VmValueKind.I8 => new Cnidaria.Cs.Stack.Slot(Cnidaria.Cs.Stack.SlotKind.I8, Payload),
                VmValueKind.R8 => new Cnidaria.Cs.Stack.Slot(Cnidaria.Cs.Stack.SlotKind.R8, Payload),
                VmValueKind.Ref => new Cnidaria.Cs.Stack.Slot(Cnidaria.Cs.Stack.SlotKind.Ref, Payload),
                VmValueKind.Ptr => new Cnidaria.Cs.Stack.Slot(Cnidaria.Cs.Stack.SlotKind.Ptr, Payload, Aux),
                VmValueKind.ByRef => new Cnidaria.Cs.Stack.Slot(Cnidaria.Cs.Stack.SlotKind.ByRef, Payload, Aux),
                VmValueKind.Value => new Cnidaria.Cs.Stack.Slot(Cnidaria.Cs.Stack.SlotKind.Value, Payload, Aux),
                _ => throw new InvalidOperationException($"Unknown VmValueKind: {Kind}")
            };
        }

        internal Cell ToCell()
        {
            return Kind switch
            {
                VmValueKind.Null => Cell.Null,
                VmValueKind.I4 => Cell.I4(unchecked((int)Payload)),
                VmValueKind.I8 => Cell.I8(Payload),
                VmValueKind.R8 => new Cell(CellKind.R8, Payload),
                VmValueKind.Ref => Cell.Ref(checked((int)Payload)),
                VmValueKind.Ptr => Cell.Ptr(checked((int)Payload), Aux),
                VmValueKind.ByRef => Cell.ByRef(checked((int)Payload), Aux),
                VmValueKind.Value => new Cell(CellKind.Value, Payload, Aux),
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
        private readonly Cnidaria.Cs.Stack.Vm? _stackVm;
        private readonly Cnidaria.Cs.Vm? _flatVm;
        private CancellationToken _ct;

        internal VmCallContext(Cnidaria.Cs.Stack.Vm vm) => _stackVm = vm ?? throw new ArgumentNullException(nameof(vm));
        internal VmCallContext(Cnidaria.Cs.Vm vm) => _flatVm = vm ?? throw new ArgumentNullException(nameof(vm));

        internal void SetToken(CancellationToken ct) => _ct = ct;

        public CancellationToken CancellationToken => _ct;
        public int PointerSize => Cnidaria.Cs.RuntimeTypeSystem.PointerSize;
        public string? ReadString(VmValue v) => _flatVm != null ? _flatVm.HostReadString(v, _ct) : _stackVm!.HostReadString(v, _ct);
        public VmValue NewString(string? s) => _flatVm != null ? _flatVm.HostAllocString(s) : _stackVm!.HostAllocString(s);
        public int GetAddress(VmValue v) => _flatVm != null ? _flatVm.HostGetAddress(v) : _stackVm!.HostGetAddress(v);
        public ReadOnlySpan<byte> ReadOnlyMemory(int address, int size) => _flatVm != null ? _flatVm.HostGetSpan(address, size, writable: false) : _stackVm!.HostGetSpan(address, size, writable: false);
        public Span<byte> Memory(int address, int size) => _flatVm != null ? _flatVm.HostGetSpan(address, size, writable: true) : _stackVm!.HostGetSpan(address, size, writable: true);
        public int GetArrayLength(VmValue array) => _flatVm != null ? _flatVm.HostGetArrayLength(array) : _stackVm!.HostGetArrayLength(array);
        public VmValue GetArrayElement(VmValue array, int index) => _flatVm != null ? _flatVm.HostGetArrayElement(array, index) : _stackVm!.HostGetArrayElement(array, index);

        public int ReadInt32(int address) => BinaryPrimitives.ReadInt32LittleEndian(ReadOnlyMemory(address, 4));
        public long ReadInt64(int address) => BinaryPrimitives.ReadInt64LittleEndian(ReadOnlyMemory(address, 8));
        public ushort ReadUInt16(int address) => BinaryPrimitives.ReadUInt16LittleEndian(ReadOnlyMemory(address, 2));
        public void WriteInt32(int address, int value) => BinaryPrimitives.WriteInt32LittleEndian(Memory(address, 4), value);
        public void WriteInt64(int address, long value) => BinaryPrimitives.WriteInt64LittleEndian(Memory(address, 8), value);
        public void WriteUInt16(int address, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(Memory(address, 2), value);

        public string ReadUtf16Z(int address, int maxChars = 8 * 1024)
        {
            if (maxChars < 0) throw new ArgumentOutOfRangeException(nameof(maxChars));
            var chars = new char[Math.Min(maxChars, 1024)];
            int n = 0;
            for (int i = 0; i < maxChars; i++)
            {
                if ((i & 0xFF) == 0) _ct.ThrowIfCancellationRequested();
                ushort ch = ReadUInt16(address + i * 2);
                if (ch == 0) break;
                if (n == chars.Length)
                    Array.Resize(ref chars, Math.Min(chars.Length * 2, maxChars));
                chars[n++] = (char)ch;
            }
            return new string(chars, 0, n);
        }
    }

    public delegate VmValue HostMethod(VmCallContext ctx, ReadOnlySpan<VmValue> args);

    internal sealed class HostOverride
    {
        public readonly int MethodId;
        public readonly HostMethod Handler;

        public HostOverride(Cnidaria.Cs.Stack.RuntimeMethod method, HostMethod handler)
        {
            if (method is null) throw new ArgumentNullException(nameof(method));
            MethodId = method.MethodId;
            Handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public HostOverride(Cnidaria.Cs.RuntimeMethod method, HostMethod handler)
        {
            if (method is null) throw new ArgumentNullException(nameof(method));
            MethodId = method.MethodId;
            Handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }
    }

    public sealed class HostInterface
    {
        private readonly Cnidaria.Cs.Stack.Vm? _stackVm;
        private readonly Cnidaria.Cs.Stack.RuntimeTypeSystem? _stackRts;
        private readonly IReadOnlyDictionary<string, Cnidaria.Cs.Stack.RuntimeModule>? _stackModules;
        private readonly Cnidaria.Cs.Vm? _flatVm;
        private readonly Cnidaria.Cs.RuntimeTypeSystem? _flatRts;
        private readonly IReadOnlyDictionary<string, Cnidaria.Cs.RuntimeModule>? _flatModules;

        internal HostInterface(Cnidaria.Cs.Stack.Vm vm, Cnidaria.Cs.Stack.RuntimeTypeSystem rts, IReadOnlyDictionary<string, Cnidaria.Cs.Stack.RuntimeModule> modules)
        {
            _stackVm = vm ?? throw new ArgumentNullException(nameof(vm));
            _stackRts = rts ?? throw new ArgumentNullException(nameof(rts));
            _stackModules = modules ?? throw new ArgumentNullException(nameof(modules));
        }

        internal HostInterface(Cnidaria.Cs.Vm vm, Cnidaria.Cs.RuntimeTypeSystem rts, IReadOnlyDictionary<string, Cnidaria.Cs.RuntimeModule> modules)
        {
            _flatVm = vm ?? throw new ArgumentNullException(nameof(vm));
            _flatRts = rts ?? throw new ArgumentNullException(nameof(rts));
            _flatModules = modules ?? throw new ArgumentNullException(nameof(modules));
        }

        public void OverrideStatic(string assemblyName, string typeFullName, string methodName, Delegate handler)
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            if (_flatVm != null)
            {
                var sig = ExtractSignatureFlat(handler);
                var method = ResolveStaticMethodFlat(assemblyName, typeFullName, methodName, sig.ParamTypes, sig.ReturnType);
                _flatVm.RegisterHostOverride(new HostOverride(method, BuildWrapperFlat(handler, sig, method)));
                return;
            }
            var stackSig = ExtractSignatureStack(handler);
            var stackMethod = ResolveStaticMethodStack(assemblyName, typeFullName, methodName, stackSig.ParamTypes, stackSig.ReturnType);
            _stackVm!.RegisterHostOverride(new HostOverride(stackMethod, BuildWrapperStack(handler, stackSig, stackMethod)));
        }

        public void OverrideStaticRaw(string assemblyName, string typeFullName, string methodName, Type returnType, Type[] paramTypes, HostMethod handler)
        {
            if (returnType is null) throw new ArgumentNullException(nameof(returnType));
            if (paramTypes is null) throw new ArgumentNullException(nameof(paramTypes));
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            if (_flatVm != null)
            {
                var vmParams = new Cnidaria.Cs.RuntimeType[paramTypes.Length];
                for (int i = 0; i < paramTypes.Length; i++)
                    vmParams[i] = MapClrTypeToVmFlat(paramTypes[i]);
                var vmRet = MapClrTypeToVmFlat(returnType);
                var method = ResolveStaticMethodFlat(assemblyName, typeFullName, methodName, vmParams, vmRet);
                _flatVm.RegisterHostOverride(new HostOverride(method, handler));
                return;
            }
            var stackParams = new Cnidaria.Cs.Stack.RuntimeType[paramTypes.Length];
            for (int i = 0; i < paramTypes.Length; i++)
                stackParams[i] = MapClrTypeToVmStack(paramTypes[i]);
            var stackRet = MapClrTypeToVmStack(returnType);
            var stackMethod = ResolveStaticMethodStack(assemblyName, typeFullName, methodName, stackParams, stackRet);
            _stackVm!.RegisterHostOverride(new HostOverride(stackMethod, handler));
        }

        private (bool HasContext, Type ReturnClr, Cnidaria.Cs.RuntimeType ReturnType, Type[] ParamClr, Cnidaria.Cs.RuntimeType[] ParamTypes) ExtractSignatureFlat(Delegate d)
        {
            var mi = d.Method;
            var ps = mi.GetParameters();
            int offset = ps.Length > 0 && ps[0].ParameterType == typeof(VmCallContext) ? 1 : 0;
            var paramClr = new Type[ps.Length - offset];
            var paramVm = new Cnidaria.Cs.RuntimeType[paramClr.Length];
            for (int i = 0; i < paramClr.Length; i++)
            {
                paramClr[i] = ps[i + offset].ParameterType;
                paramVm[i] = MapClrTypeToVmFlat(paramClr[i]);
            }
            Type retClr = mi.ReturnType;
            return (offset != 0, retClr, MapClrTypeToVmFlat(retClr), paramClr, paramVm);
        }

        private (bool HasContext, Type ReturnClr, Cnidaria.Cs.Stack.RuntimeType ReturnType, Type[] ParamClr, Cnidaria.Cs.Stack.RuntimeType[] ParamTypes) ExtractSignatureStack(Delegate d)
        {
            var mi = d.Method;
            var ps = mi.GetParameters();
            int offset = ps.Length > 0 && ps[0].ParameterType == typeof(VmCallContext) ? 1 : 0;
            var paramClr = new Type[ps.Length - offset];
            var paramVm = new Cnidaria.Cs.Stack.RuntimeType[paramClr.Length];
            for (int i = 0; i < paramClr.Length; i++)
            {
                paramClr[i] = ps[i + offset].ParameterType;
                paramVm[i] = MapClrTypeToVmStack(paramClr[i]);
            }
            Type retClr = mi.ReturnType;
            return (offset != 0, retClr, MapClrTypeToVmStack(retClr), paramClr, paramVm);
        }

        private HostMethod BuildWrapperFlat(Delegate handler, (bool HasContext, Type ReturnClr, Cnidaria.Cs.RuntimeType ReturnType, Type[] ParamClr, Cnidaria.Cs.RuntimeType[] ParamTypes) sig, Cnidaria.Cs.RuntimeMethod targetMethod)
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
                return targetMethod.ReturnType.Namespace == "System" && targetMethod.ReturnType.Name == "Void" ? VmValue.Null : ConvertRetFlat(ctx, ret, sig.ReturnClr, targetMethod.ReturnType);
            };
        }

        private HostMethod BuildWrapperStack(Delegate handler, (bool HasContext, Type ReturnClr, Cnidaria.Cs.Stack.RuntimeType ReturnType, Type[] ParamClr, Cnidaria.Cs.Stack.RuntimeType[] ParamTypes) sig, Cnidaria.Cs.Stack.RuntimeMethod targetMethod)
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
            if (clr == typeof(IntPtr)) return Cnidaria.Cs.RuntimeTypeSystem.PointerSize == 8 ? new IntPtr(v.AsInt64()) : new IntPtr(v.AsInt32());
            if (clr == typeof(UIntPtr)) return Cnidaria.Cs.RuntimeTypeSystem.PointerSize == 8 ? new UIntPtr(unchecked((ulong)v.AsInt64())) : new UIntPtr(unchecked((uint)v.AsInt32()));
            throw new NotSupportedException($"Host arg type not supported: {clr.FullName}");
        }

        private VmValue ConvertRetFlat(VmCallContext ctx, object? retObj, Type clr, Cnidaria.Cs.RuntimeType actualVmType)
        {
            if (clr.IsEnum)
            {
                var underlying = Enum.GetUnderlyingType(clr);
                object raw = retObj ?? Activator.CreateInstance(underlying)!;
                if (raw.GetType() != underlying) raw = Convert.ChangeType(raw, underlying, CultureInfo.InvariantCulture)!;
                return ConvertRetFlat(ctx, raw, underlying, actualVmType);
            }
            if (clr.IsArray)
            {
                if (clr.GetArrayRank() != 1) throw new NotSupportedException("Only SZARRAY supported in host marshaling.");
                if (retObj is null) return VmValue.Null;
                if (retObj is not Array arr) throw new InvalidOperationException($"Expected array return value for '{clr.FullName}'.");
                if (actualVmType.Kind != Cnidaria.Cs.RuntimeTypeKind.Array || actualVmType.ElementType is null) throw new InvalidOperationException($"VM return type '{actualVmType.Namespace}.{actualVmType.Name}' is not an array.");
                var elementClr = clr.GetElementType() ?? throw new InvalidOperationException("Array without element type.");
                var vmArr = _flatVm!.HostAllocArray(actualVmType, arr.Length);
                for (int i = 0; i < arr.Length; i++) _flatVm.HostSetArrayElement(vmArr, i, ConvertRetFlat(ctx, arr.GetValue(i), elementClr, actualVmType.ElementType));
                return vmArr;
            }
            return ConvertScalarRet(ctx, retObj, clr);
        }

        private VmValue ConvertRetStack(VmCallContext ctx, object? retObj, Type clr, Cnidaria.Cs.Stack.RuntimeType actualVmType)
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
                if (actualVmType.Kind != Cnidaria.Cs.Stack.RuntimeTypeKind.Array || actualVmType.ElementType is null) throw new InvalidOperationException($"VM return type '{actualVmType.Namespace}.{actualVmType.Name}' is not an array.");
                var elementClr = clr.GetElementType() ?? throw new InvalidOperationException("Array without element type.");
                var vmArr = _stackVm!.HostAllocArray(actualVmType, arr.Length);
                for (int i = 0; i < arr.Length; i++) _stackVm.HostSetArrayElement(vmArr, i, ConvertRetStack(ctx, arr.GetValue(i), elementClr, actualVmType.ElementType));
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
                long raw = Cnidaria.Cs.RuntimeTypeSystem.PointerSize == 8 ? ip.ToInt64() : ip.ToInt32();
                return Cnidaria.Cs.RuntimeTypeSystem.PointerSize == 8 ? VmValue.FromInt64(raw) : VmValue.FromInt32((int)raw);
            }
            if (clr == typeof(UIntPtr))
            {
                var up = (UIntPtr)(retObj ?? UIntPtr.Zero);
                ulong raw = up.ToUInt64();
                return Cnidaria.Cs.RuntimeTypeSystem.PointerSize == 8 ? VmValue.FromInt64(unchecked((long)raw)) : VmValue.FromInt32(unchecked((int)(uint)raw));
            }
            throw new NotSupportedException($"Host return type not supported: {clr.FullName}");
        }

        private Cnidaria.Cs.RuntimeMethod ResolveStaticMethodFlat(string assemblyName, string typeFullName, string methodName, Cnidaria.Cs.RuntimeType[] ps, Cnidaria.Cs.RuntimeType ret)
        {
            if (!_flatModules!.TryGetValue(assemblyName, out var mod)) throw new TypeLoadException($"Module '{assemblyName}' not loaded.");
            SplitTypeFullName(typeFullName, out var ns, out var name);
            if (!mod.TypeDefByFullName.TryGetValue((ns, name), out var typeDefTok)) throw new TypeLoadException($"Type '{ns}.{name}' not found in '{assemblyName}'.");
            var owner = _flatRts!.ResolveType(mod, typeDefTok);
            Cnidaria.Cs.RuntimeMethod? match = null;
            int bestScore = int.MaxValue;
            for (int i = 0; i < owner.Methods.Length; i++)
            {
                var m = owner.Methods[i];
                if (!m.IsStatic || m.HasThis || !StringComparer.Ordinal.Equals(m.Name, methodName) || m.ParameterTypes.Length != ps.Length) continue;
                if (!TryGetHostTypeMatchCostFlat(m.ReturnType, ret, out int score)) continue;
                bool ok = true;
                for (int p = 0; p < ps.Length; p++)
                {
                    if (!TryGetHostTypeMatchCostFlat(m.ParameterTypes[p], ps[p], out int c)) { ok = false; break; }
                    score += c;
                }
                if (!ok) continue;
                if (score < bestScore) { bestScore = score; match = m; }
                else if (score == bestScore) match = null;
            }
            return match ?? throw new MissingMethodException($"Static method '{typeFullName}.{methodName}' not found or ambiguous in '{assemblyName}'.");
        }

        private Cnidaria.Cs.Stack.RuntimeMethod ResolveStaticMethodStack(string assemblyName, string typeFullName, string methodName, Cnidaria.Cs.Stack.RuntimeType[] ps, Cnidaria.Cs.Stack.RuntimeType ret)
        {
            if (!_stackModules!.TryGetValue(assemblyName, out var mod)) throw new TypeLoadException($"Module '{assemblyName}' not loaded.");
            SplitTypeFullName(typeFullName, out var ns, out var name);
            if (!mod.TypeDefByFullName.TryGetValue((ns, name), out var typeDefTok)) throw new TypeLoadException($"Type '{ns}.{name}' not found in '{assemblyName}'.");
            var owner = _stackRts!.ResolveType(mod, typeDefTok);
            Cnidaria.Cs.Stack.RuntimeMethod? match = null;
            int bestScore = int.MaxValue;
            for (int i = 0; i < owner.Methods.Length; i++)
            {
                var m = owner.Methods[i];
                if (!m.IsStatic || m.HasThis || !StringComparer.Ordinal.Equals(m.Name, methodName) || m.ParameterTypes.Length != ps.Length) continue;
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

        private bool TryGetHostTypeMatchCostFlat(Cnidaria.Cs.RuntimeType actual, Cnidaria.Cs.RuntimeType requested, out int cost)
        {
            if (actual.TypeId == requested.TypeId) { cost = 0; return true; }
            if (actual.Kind == Cnidaria.Cs.RuntimeTypeKind.Enum && actual.ElementType != null && actual.ElementType.TypeId == requested.TypeId) { cost = 1; return true; }
            if (requested.Kind == Cnidaria.Cs.RuntimeTypeKind.Enum && requested.ElementType != null && requested.ElementType.TypeId == actual.TypeId) { cost = 1; return true; }
            cost = 0;
            return false;
        }

        private bool TryGetHostTypeMatchCostStack(Cnidaria.Cs.Stack.RuntimeType actual, Cnidaria.Cs.Stack.RuntimeType requested, out int cost)
        {
            if (actual.TypeId == requested.TypeId) { cost = 0; return true; }
            if (actual.Kind == Cnidaria.Cs.Stack.RuntimeTypeKind.Enum && actual.ElementType != null && actual.ElementType.TypeId == requested.TypeId) { cost = 1; return true; }
            if (requested.Kind == Cnidaria.Cs.Stack.RuntimeTypeKind.Enum && requested.ElementType != null && requested.ElementType.TypeId == actual.TypeId) { cost = 1; return true; }
            cost = 0;
            return false;
        }

        private Cnidaria.Cs.RuntimeType MapClrTypeToVmFlat(Type t)
        {
            if (t == typeof(void)) return ResolveStdFlat("System", "Void");
            if (t.IsEnum) return MapClrTypeToVmFlat(Enum.GetUnderlyingType(t));
            if (t.IsArray)
            {
                if (t.GetArrayRank() != 1) throw new NotSupportedException("Only SZARRAY supported in host marshaling.");
                return _flatRts!.GetArrayType(MapClrTypeToVmFlat(t.GetElementType()!));
            }
            if (t == typeof(string)) return _flatRts!.SystemString;
            if (t == typeof(bool)) return ResolveStdFlat("System", "Boolean");
            if (t == typeof(char)) return ResolveStdFlat("System", "Char");
            if (t == typeof(byte)) return ResolveStdFlat("System", "Byte");
            if (t == typeof(sbyte)) return ResolveStdFlat("System", "SByte");
            if (t == typeof(short)) return ResolveStdFlat("System", "Int16");
            if (t == typeof(ushort)) return ResolveStdFlat("System", "UInt16");
            if (t == typeof(int)) return ResolveStdFlat("System", "Int32");
            if (t == typeof(uint)) return ResolveStdFlat("System", "UInt32");
            if (t == typeof(long)) return ResolveStdFlat("System", "Int64");
            if (t == typeof(ulong)) return ResolveStdFlat("System", "UInt64");
            if (t == typeof(float)) return ResolveStdFlat("System", "Single");
            if (t == typeof(double)) return ResolveStdFlat("System", "Double");
            if (t == typeof(IntPtr)) return ResolveStdFlat("System", "IntPtr");
            if (t == typeof(UIntPtr)) return ResolveStdFlat("System", "UIntPtr");
            throw new NotSupportedException($"CLR type cannot be mapped to VM type: {t.FullName}");
        }

        private Cnidaria.Cs.Stack.RuntimeType MapClrTypeToVmStack(Type t)
        {
            if (t == typeof(void)) return ResolveStdStack("System", "Void");
            if (t.IsEnum) return MapClrTypeToVmStack(Enum.GetUnderlyingType(t));
            if (t.IsArray)
            {
                if (t.GetArrayRank() != 1) throw new NotSupportedException("Only SZARRAY supported in host marshaling.");
                return _stackRts!.GetArrayType(MapClrTypeToVmStack(t.GetElementType()!));
            }
            if (t == typeof(string)) return _stackRts!.SystemString;
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

        private Cnidaria.Cs.RuntimeType ResolveStdFlat(string ns, string name)
        {
            if (!_flatModules!.TryGetValue("std", out var std)) throw new InvalidOperationException("Std module not loaded.");
            if (!std.TypeDefByFullName.TryGetValue((ns, name), out var tok)) throw new TypeLoadException($"Std type not found: std:{ns}.{name}");
            return _flatRts!.ResolveType(std, tok);
        }

        private Cnidaria.Cs.Stack.RuntimeType ResolveStdStack(string ns, string name)
        {
            if (!_stackModules!.TryGetValue("std", out var std)) throw new InvalidOperationException("Std module not loaded.");
            if (!std.TypeDefByFullName.TryGetValue((ns, name), out var tok)) throw new TypeLoadException($"Std type not found: std:{ns}.{name}");
            return _stackRts!.ResolveType(std, tok);
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
