using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
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

        internal VmValue(Slot s)
        {
            switch (s.Kind)
            {
                case SlotKind.Null: Kind = VmValueKind.Null; Payload = 0; Aux = 0; break;
                case SlotKind.I4: Kind = VmValueKind.I4; Payload = s.Payload; Aux = 0; break;
                case SlotKind.I8: Kind = VmValueKind.I8; Payload = s.Payload; Aux = 0; break;
                case SlotKind.R8: Kind = VmValueKind.R8; Payload = s.Payload; Aux = 0; break;
                case SlotKind.Ref: Kind = VmValueKind.Ref; Payload = s.Payload; Aux = 0; break;
                case SlotKind.Ptr: Kind = VmValueKind.Ptr; Payload = s.Payload; Aux = s.Aux; break;
                case SlotKind.ByRef: Kind = VmValueKind.ByRef; Payload = s.Payload; Aux = s.Aux; break;
                case SlotKind.Value: Kind = VmValueKind.Value; Payload = s.Payload; Aux = s.Aux; break;
                default:
                    throw new InvalidOperationException($"Unknown SlotKind: {s.Kind}");
            }
        }

        internal Slot ToSlot()
        {
            return Kind switch
            {
                VmValueKind.Null => new Slot(SlotKind.Null, 0),
                VmValueKind.I4 => new Slot(SlotKind.I4, Payload),
                VmValueKind.I8 => new Slot(SlotKind.I8, Payload),
                VmValueKind.R8 => new Slot(SlotKind.R8, Payload),
                VmValueKind.Ref => new Slot(SlotKind.Ref, Payload),
                VmValueKind.Ptr => new Slot(SlotKind.Ptr, Payload, Aux),
                VmValueKind.ByRef => new Slot(SlotKind.ByRef, Payload, Aux),
                VmValueKind.Value => new Slot(SlotKind.Value, Payload, Aux),
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
        private readonly Vm _vm;
        private CancellationToken _ct;

        internal VmCallContext(Vm vm) => _vm = vm;

        internal void SetToken(CancellationToken ct) => _ct = ct;

        public CancellationToken CancellationToken => _ct;
        public int PointerSize => RuntimeTypeSystem.PointerSize;
        public string? ReadString(VmValue v) => _vm.HostReadString(v, _ct);
        public VmValue NewString(string? s) => _vm.HostAllocString(s);

        public int GetAddress(VmValue v) => _vm.HostGetAddress(v);

        public ReadOnlySpan<byte> ReadOnlyMemory(int address, int size) => _vm.HostGetSpan(address, size, writable: false);
        public Span<byte> Memory(int address, int size) => _vm.HostGetSpan(address, size, writable: true);

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
        public readonly RuntimeMethod Method;
        public readonly HostMethod Handler;

        public HostOverride(RuntimeMethod method, HostMethod handler)
        {
            Method = method;
            Handler = handler;
        }
    }

    public sealed class HostInterface
    {
        private readonly Vm _vm;
        private readonly RuntimeTypeSystem _rts;
        private readonly IReadOnlyDictionary<string, RuntimeModule> _modules;

        internal HostInterface(Vm vm, RuntimeTypeSystem rts, IReadOnlyDictionary<string, RuntimeModule> modules)
        {
            _vm = vm;
            _rts = rts;
            _modules = modules;
        }
        public void OverrideStatic(string assemblyName, string typeFullName, string methodName, Delegate handler)
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            var sig = ExtractSignature(handler);

            var m = ResolveStaticMethod(assemblyName, typeFullName, methodName, sig.ParamTypes, sig.ReturnType);
            var wrapper = BuildWrapper(handler, sig);

            _vm.RegisterHostOverride(new HostOverride(m, wrapper));
        }
        public void OverrideStaticRaw(string assemblyName, string typeFullName, string methodName, Type returnType, Type[] paramTypes, HostMethod handler)
        {
            if (paramTypes is null) throw new ArgumentNullException(nameof(paramTypes));
            if (handler is null) throw new ArgumentNullException(nameof(handler));

            var vmParams = new RuntimeType[paramTypes.Length];
            for (int i = 0; i < paramTypes.Length; i++)
                vmParams[i] = MapClrTypeToVm(paramTypes[i]);

            var vmRet = MapClrTypeToVm(returnType);
            var m = ResolveStaticMethod(assemblyName, typeFullName, methodName, vmParams, vmRet);

            _vm.RegisterHostOverride(new HostOverride(m, handler));
        }
        private (bool HasContext, Type ReturnClr, RuntimeType ReturnType, Type[] ParamClr, RuntimeType[] ParamTypes) ExtractSignature(Delegate d)
        {
            var mi = d.Method;
            var ps = mi.GetParameters();

            int offset = 0;
            bool hasCtx = false;
            if (ps.Length > 0 && ps[0].ParameterType == typeof(VmCallContext))
            {
                hasCtx = true;
                offset = 1;
            }

            var paramClr = new Type[ps.Length - offset];
            var paramVm = new RuntimeType[paramClr.Length];
            for (int i = 0; i < paramClr.Length; i++)
            {
                paramClr[i] = ps[i + offset].ParameterType;
                paramVm[i] = MapClrTypeToVm(paramClr[i]);
            }

            Type retClr = mi.ReturnType;
            var retVm = MapClrTypeToVm(retClr);

            return (hasCtx, retClr, retVm, paramClr, paramVm);
        }
        private HostMethod BuildWrapper
            (Delegate handler, (bool HasContext, Type ReturnClr, RuntimeType ReturnType, Type[] ParamClr, RuntimeType[] ParamTypes) sig)
        {
            for (int i = 0; i < sig.ParamClr.Length; i++)
            {
                if (sig.ParamClr[i].IsByRefLike)
                    throw new NotSupportedException($"Delegate parameter '{sig.ParamClr[i]}' is byref-like. Use OverrideStaticRaw + VmHostMethod instead.");
            }
            if (sig.ReturnClr.IsByRefLike)
                throw new NotSupportedException($"Delegate return '{sig.ReturnClr}' is byref-like. Use OverrideStaticRaw + VmHostMethod instead.");

            var invokeArgs = new object?[sig.ParamClr.Length + (sig.HasContext ? 1 : 0)];

            return (ctx, args) =>
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();

                int dst = 0;
                if (sig.HasContext)
                    invokeArgs[dst++] = ctx;

                if (args.Length != sig.ParamTypes.Length)
                    throw new InvalidOperationException("Arity mismatch for host override.");

                for (int i = 0; i < sig.ParamClr.Length; i++)
                    invokeArgs[dst + i] = ConvertArg(ctx, args[i], sig.ParamClr[i]);

                object? retObj = handler.DynamicInvoke(invokeArgs);

                if (sig.ReturnClr == typeof(void))
                    return VmValue.Null;

                return ConvertRet(ctx, retObj, sig.ReturnClr);
            };
        }
        private object? ConvertArg(VmCallContext ctx, VmValue v, Type clr)
        {
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
            if (clr == typeof(IntPtr))
            {
                long raw = (RuntimeTypeSystem.PointerSize == 8) ? v.AsInt64() : v.AsInt32();
                return new IntPtr((nint)raw);
            }
            if (clr == typeof(UIntPtr))
            {
                ulong raw = (RuntimeTypeSystem.PointerSize == 8) ? (ulong)v.AsInt64() : (uint)v.AsInt32();
                return new UIntPtr((nuint)raw);
            }

            throw new NotSupportedException($"Host arg type not supported: {clr.FullName}");
        }

        private VmValue ConvertRet(VmCallContext ctx, object? retObj, Type clr)
        {
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
                long raw = (RuntimeTypeSystem.PointerSize == 8) ? ip.ToInt64() : ip.ToInt32();
                return (RuntimeTypeSystem.PointerSize == 8) ? VmValue.FromInt64(raw) : VmValue.FromInt32((int)raw);
            }
            if (clr == typeof(UIntPtr))
            {
                var up = (UIntPtr)(retObj ?? UIntPtr.Zero);
                ulong raw = up.ToUInt64();
                return (RuntimeTypeSystem.PointerSize == 8) ? VmValue.FromInt64(unchecked((long)raw)) : VmValue.FromInt32(unchecked((int)(uint)raw));
            }

            throw new NotSupportedException($"Host return type not supported: {clr.FullName}");
        }
        private RuntimeMethod ResolveStaticMethod(string assemblyName, string typeFullName, string methodName, RuntimeType[] ps, RuntimeType ret)
        {
            if (!_modules.TryGetValue(assemblyName, out var mod))
                throw new TypeLoadException($"Module '{assemblyName}' not loaded.");

            SplitTypeFullName(typeFullName, out var ns, out var name);

            if (!mod.TypeDefByFullName.TryGetValue((ns, name), out var typeDefTok))
                throw new TypeLoadException($"Type '{ns}.{name}' not found in '{assemblyName}'.");

            var owner = _rts.ResolveType(mod, typeDefTok);

            RuntimeMethod? match = null;
            for (int i = 0; i < owner.Methods.Length; i++)
            {
                var m = owner.Methods[i];
                if (!m.IsStatic || m.HasThis) continue;
                if (!StringComparer.Ordinal.Equals(m.Name, methodName)) continue;
                if (m.ParameterTypes.Length != ps.Length) continue;
                if (m.ReturnType.TypeId != ret.TypeId) continue;

                bool ok = true;
                for (int p = 0; p < ps.Length; p++)
                {
                    if (m.ParameterTypes[p].TypeId != ps[p].TypeId)
                    {
                        ok = false;
                        break;
                    }
                }

                if (!ok) continue;

                if (match != null)
                    throw new AmbiguousMatchException($"Multiple overloads match: {typeFullName}.{methodName} in '{assemblyName}'.");

                match = m;
            }

            return match ?? throw new MissingMethodException($"{assemblyName}:{typeFullName}.{methodName} not found.");
        }

        private RuntimeType MapClrTypeToVm(Type t)
        {
            if (t == typeof(void)) return ResolveStd("System", "Void");
            if (t == typeof(string)) return ResolveStd("System", "String");
            if (t == typeof(object)) return ResolveStd("System", "Object");

            if (t.IsByRef)
            {
                var elem = t.GetElementType() ?? throw new InvalidOperationException("ByRef without element type.");
                return _rts.GetByRefType(MapClrTypeToVm(elem));
            }

            if (t.IsArray)
            {
                if (t.GetArrayRank() != 1)
                    throw new NotSupportedException("Only SZARRAY supported in host mapping.");
                var elem = t.GetElementType() ?? throw new InvalidOperationException("Array without element type.");
                return _rts.GetArrayType(MapClrTypeToVm(elem));
            }

            // primitives
            if (t == typeof(bool)) return ResolveStd("System", "Boolean");
            if (t == typeof(char)) return ResolveStd("System", "Char");
            if (t == typeof(byte)) return ResolveStd("System", "Byte");
            if (t == typeof(sbyte)) return ResolveStd("System", "SByte");
            if (t == typeof(short)) return ResolveStd("System", "Int16");
            if (t == typeof(ushort)) return ResolveStd("System", "UInt16");
            if (t == typeof(int)) return ResolveStd("System", "Int32");
            if (t == typeof(uint)) return ResolveStd("System", "UInt32");
            if (t == typeof(long)) return ResolveStd("System", "Int64");
            if (t == typeof(ulong)) return ResolveStd("System", "UInt64");
            if (t == typeof(float)) return ResolveStd("System", "Single");
            if (t == typeof(double)) return ResolveStd("System", "Double");
            if (t == typeof(IntPtr)) return ResolveStd("System", "IntPtr");
            if (t == typeof(UIntPtr)) return ResolveStd("System", "UIntPtr");

            throw new NotSupportedException($"CLR type cannot be mapped to VM type: {t.FullName}");
        }

        private RuntimeType ResolveStd(string ns, string name)
        {
            if (!_modules.TryGetValue("std", out var std))
                throw new InvalidOperationException("Std module not loaded.");

            if (!std.TypeDefByFullName.TryGetValue((ns, name), out var tok))
                throw new TypeLoadException($"Std type not found: std:{ns}.{name}");

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
