using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Cnidaria.RiscV;

public readonly record struct RVJitStatistics(ulong CompiledBlocks, ulong ExecutedBlocks, ulong NativeInstructions, ulong InterpretedInstructions, TimeSpan CompilationTime);

public sealed partial class RiscVEmulator
{
    private JitState? _jit;
    private bool _disposed;
    private int _running;

    public RVJitStatistics JitStatistics => _jit is JitState jit
        ? new(jit.CompiledBlocks, jit.ExecutedBlocks, jit.NativeInstructions, jit.InterpretedInstructions, TimeSpan.FromSeconds((double)jit.CompilationTicks / Stopwatch.Frequency))
        : default;

    public void Dispose()
    {
        if (_disposed)
            return;
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            throw new InvalidOperationException("The machine is running.");
        try
        {
            _disposed = true;
            _jit?.Dispose();
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
        GC.SuppressFinalize(this);
    }

    private unsafe RVRunResult RunJit(ulong instructionLimit)
    {
        if ((!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()) || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("The RISC-V JIT requires a Windows or Linux x64 host.");

        JitState jit = _jit ??= new JitState(_ram.Length);
        jit.FlushTlb();
        ulong steps = 0;
        ulong retired = 0;
        fixed (ulong* registers = &_x[0], floats = &_f[0], vectors = &_v[0])
        fixed (byte* ram = _ram, framebuffer = _framebufferPixels)
        fixed (JitTlbEntry* reads = jit.ReadTlb.Entries, writes = jit.WriteTlb.Entries)
        fixed (JitDispatchEntry* dispatch = jit.Dispatch)
        {
            JitContext context = new()
            {
                Registers = registers,
                Floats = floats,
                Vectors = (byte*)vectors,
                NativeMxcsr = 0x1F80,
                RamOffset = (ulong)ram - _ramBase,
                FramebufferStart = (ulong)framebuffer,
                FramebufferEnd = (ulong)framebuffer + (ulong)_framebufferPixels.Length,
                FramebufferOffset = (ulong)framebuffer - _framebufferPixelBase,
                ReadTlb = reads,
                WriteTlb = writes,
                Dispatch = dispatch,
            };

            while (steps < instructionLimit && !_stopped)
            {
                UpdatePendingInterrupts();
                ulong available = Math.Min(instructionLimit - steps, DevicePollInterval);
                available = Math.Min(available, DevicePollInterval - (_cycle & (DevicePollInterval - 1)) - 1);
                if (_clint.MTimeCmp > _clint.MTime)
                    available = Math.Min(available, _clint.MTimeCmp - _clint.MTime - 1);
                if (SupervisorTimerEnabled && _stimecmp > _clint.MTime)
                    available = Math.Min(available, _stimecmp - _clint.MTime - 1);

                JitBlock? block = null;
                if (available != 0 && !_waiting && SelectInterrupt() == ulong.MaxValue && (_pc & 1) == 0
                    && TryGetJitBlock(jit, out block) && block is not null && block.Count <= (int)available)
                {
                    jit.Dispatch[(int)((_pc >> 1) & JitState.DispatchMask)] = new()
                    {
                        Pc = _pc,
                        Entry = block.Entry + block.ChainOffset,
                        Generation = jit.Generation,
                    };
                    context.Generation = jit.Generation;
                    context.Pc = _pc;
                    context.Remaining = (int)available;
                    context.Executed = 0;
                    context.Blocks = 0;
                    context.MemorySize = 0;
                    context.Fallback = 0;
                    context.Rounding = (int)_frm;
                    context.VectorLength = _vl;
                    context.VectorType = _vtype;
                    context.VectorStart = _vstart;
                    context.VectorDirty = 0;
                    context.ReservationAddress = _reservationAddress;
                    context.HasReservation = _hasReservation ? 1 : 0;
                    // Bounded by DevicePollInterval; no blocking or managed callbacks
                    int executed = ((delegate* unmanaged[SuppressGCTransition]<JitContext*, int>)block.Entry)(&context);
                    jit.ExecutedBlocks += (ulong)context.Blocks;
                    jit.NativeInstructions += (ulong)executed;
                    _pc = context.Pc;
                    _vstart = context.VectorStart;
                    _vl = context.VectorLength;
                    if (_vtype != context.VectorType)
                    {
                        _vtype = context.VectorType;
                        TryDecodeVectorType(_vtype, out _vsewBytes, out _vgroupBytes, out _vgroupRegisters, out _vvlmax);
                        jit.ClearDispatch();
                    }
                    _mstatus |= context.VectorDirty;
                    _reservationAddress = context.ReservationAddress;
                    _hasReservation = context.HasReservation != 0;
                    _cycle += (ulong)executed;
                    _instret += (ulong)executed;
                    _clint.Advance((ulong)executed);
                    steps += (ulong)executed;
                    retired += (ulong)executed;
                    if (context.MemorySize == 0 && context.Fallback == 0)
                        continue;
                    if (context.MemorySize != 0 && TryFillJitTlb(jit, context.MemoryAddress, context.MemorySize, context.MemoryAccess, ram, framebuffer))
                        continue;
                }

                ulong fallbackBudget = available != 0 && block is not null && (ulong)block.Count > available ? available : 1;
                RVRunResult result = RunInterpreter(fallbackBudget);
                jit.InterpretedInstructions += result.InstructionsRetired;
                steps += result.Steps;
                retired += result.InstructionsRetired;
                jit.FlushTlb();
                if (result.StopReason is RVStopReason.WaitingForInterrupt or RVStopReason.Stopped or RVStopReason.MachineFatalTrap)
                    break;
            }
        }

        RVStopReason reason = _machineFatalTrap ? RVStopReason.MachineFatalTrap
            : _stopped ? RVStopReason.Stopped
            : _waiting ? RVStopReason.WaitingForInterrupt
            : steps == instructionLimit ? RVStopReason.InstructionLimit : RVStopReason.None;
        return new RVRunResult(retired, steps, reason, _lastTrap, _hasTrap);
    }

    private bool TryJitTranslate(JitState jit, ulong address, RVMemoryAccess access, out ulong physical)
    {
        jit.TrackingPageTables = true;
        try
        {
            return TryTranslate(address, access, out physical, out _, out _);
        }
        finally
        {
            jit.TrackingPageTables = false;
        }
    }

    private bool TryGetJitBlock(JitState jit, out JitBlock? block)
    {
        block = null;
        ulong page = _pc & ~PageMask;
        ref JitTlbEntry entry = ref jit.ExecuteTlb.Entries[(int)((page >> 12) & JitState.TlbMask)];
        ulong physical;
        if (entry.Tag == page)
        {
            physical = _pc + entry.Addend;
        }
        else
        {
            if (!TryJitTranslate(jit, _pc, RVMemoryAccess.Execute, out physical))
                return false;
            jit.ExecuteTlb.Set(page, physical - _pc);
        }
        ulong offset = physical - _ramBase;
        if (physical < _ramBase || offset >= (ulong)_ram.Length)
            return false;
        int start = (int)offset;
        var key = (_pc, start, _vtype);
        var scalarKey = (_pc, start, JitState.ScalarType);
        if (jit.Blocks.TryGetValue(scalarKey, out JitBlock? cached))
            key = scalarKey;
        else
            jit.Blocks.TryGetValue(key, out cached);
        if (cached is not null
            && _ram.AsSpan(start, cached.Source.Length).SequenceEqual(cached.Source))
        {
            if (cached.Count < 0 && ++cached.Hits < JitState.CompilationThreshold)
                return false;
            if (cached.Count >= 0)
            {
                block = cached;
                return cached.Count != 0;
            }
        }
        else
        {
            jit.MakeRoomForBlock();
            jit.Blocks[key] = new JitBlock { Count = -1, Hits = 1, Source = _ram.AsSpan(start, Math.Min(4, _ram.Length - start)).ToArray(), Bytes = Array.Empty<byte>() };
            return false;
        }

        int length = Math.Min(_ram.Length - start, (int)(PageSize - (_pc & PageMask)));
        length = Math.Min(length, (int)(PageSize - (physical & PageMask)));
        long compilationStart = Stopwatch.GetTimestamp();
        block = JitCompiler.Compile(this, _pc, _ram.AsSpan(start, length));
        if (block.Bytes.Length > jit.Code.Remaining)
        {
            jit.ResetCache();
            if (!TryJitTranslate(jit, _pc, RVMemoryAccess.Execute, out physical) || physical - _ramBase != (ulong)start)
                return false;
        }
        try
        {
            block.Entry = jit.Code.Add(block.Bytes);
        }
        catch
        {
            _jit = null;
            jit.Dispose();
            throw;
        }
        jit.Blocks.Remove(key);
        key = block.VectorSpecialized ? (_pc, start, _vtype) : scalarKey;
        jit.Blocks[key] = block;
        jit.ProtectPage(start >> 12);
        jit.ProtectPage((start + block.Source.Length - 1) >> 12);
        jit.CompiledBlocks++;
        jit.CompilationTicks += Stopwatch.GetTimestamp() - compilationStart;
        return block.Count != 0;
    }

    private unsafe bool TryFillJitTlb(JitState jit, ulong address, int size, RVMemoryAccess access, byte* ram, byte* framebuffer)
    {
        ref JitTlbEntry read = ref jit.ReadTlb.Entries[(int)((address >> 12) & JitState.TlbMask)];
        ulong readOffset = address + read.Addend - (ulong)ram;
        if (access == RVMemoryAccess.Store && read.Tag == (address & ~PageMask)
            && readOffset < (ulong)_ram.Length && jit.ProtectedPages[(int)(readOffset >> 12)])
            return false;
        if ((_ramBase & PageMask) != 0 || (address & (ulong)(size - 1)) != 0
            || !TryJitTranslate(jit, address, access, out ulong physical))
            return false;
        ulong pageOffset = address & PageMask;
        ulong offset = physical - _ramBase;
        byte* backing = ram;
        if (offset >= (ulong)_ram.Length)
        {
            offset = physical - _framebufferPixelBase;
            if (offset < pageOffset || offset - pageOffset >= (ulong)_framebufferPixels.Length
                || (ulong)_framebufferPixels.Length - (offset - pageOffset) < PageSize)
                return false;
            backing = framebuffer;
        }
        else
        {
            if (offset < pageOffset || (ulong)_ram.Length - (offset - pageOffset) < PageSize)
                return false;
            if (access == RVMemoryAccess.Store && jit.ProtectedPages[(int)(offset >> 12)])
                return false;
        }

        JitTlb entries = access == RVMemoryAccess.Store ? jit.WriteTlb : jit.ReadTlb;
        entries.Set(address & ~PageMask, (ulong)backing + offset - address);
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct JitContext
    {
        public ulong* Registers;
        public ulong* Floats;
        public byte* Vectors;
        public ulong VectorLength;
        public ulong VectorType;
        public ulong VectorStart;
        public ulong VectorDirty;
        public int VectorIndex;
        public ulong RamOffset;
        public ulong FramebufferStart;
        public ulong FramebufferEnd;
        public ulong FramebufferOffset;
        public ulong ReservationAddress;
        public int HasReservation;
        public JitTlbEntry* ReadTlb;
        public JitTlbEntry* WriteTlb;
        public JitDispatchEntry* Dispatch;
        public ulong Generation;
        public ulong Pc;
        public ulong MemoryAddress;
        public RVMemoryAccess MemoryAccess;
        public int MemorySize;
        public int Remaining;
        public int Executed;
        public int Blocks;
        public int Fallback;
        public int Rounding;
        public int NativeMxcsr;
        public int SavedMxcsr;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JitTlbEntry
    {
        public ulong Tag;
        public ulong Addend;
    }

    private sealed class JitTlb
    {
        public readonly JitTlbEntry[] Entries = new JitTlbEntry[JitState.TlbMask + 1];
        private readonly int[] _used = new int[JitState.TlbMask + 1];
        private int _count;

        public JitTlb()
        {
            for (int i = 0; i < Entries.Length; i++)
                Entries[i].Tag = ulong.MaxValue;
        }

        public void Set(ulong page, ulong addend)
        {
            int index = (int)((page >> 12) & JitState.TlbMask);
            if (Entries[index].Tag == ulong.MaxValue)
                _used[_count++] = index;
            Entries[index] = new() { Tag = page, Addend = addend };
        }

        public void Clear()
        {
            for (int i = 0; i < _count; i++)
                Entries[_used[i]].Tag = ulong.MaxValue;
            _count = 0;
        }
    }

    [StructLayout(LayoutKind.Sequential, Size = 32)]
    private struct JitDispatchEntry
    {
        public ulong Pc;
        public nint Entry;
        public ulong Generation;
    }

    private sealed class JitBlock
    {
        public required byte[] Source;
        public required byte[] Bytes;
        public required int Count;
        public int Hits;
        public nint Entry;
        public int ChainOffset;
        public bool VectorSpecialized;
    }

    private sealed class JitState : IDisposable
    {
        internal const ulong ScalarType = ulong.MaxValue;
        internal const int MaxBlockInstructions = 64;
        internal const int CompilationThreshold = 32;
        internal const int TlbMask = 255;
        internal const int DispatchMask = 4095;
        public readonly JitDispatchEntry[] Dispatch = new JitDispatchEntry[DispatchMask + 1];
        public ulong Generation;
        public readonly JitTlb ReadTlb = new();
        public readonly JitTlb WriteTlb = new();
        public readonly JitTlb ExecuteTlb = new();
        public readonly Dictionary<(ulong, int, ulong), JitBlock> Blocks = new();
        public readonly bool[] ProtectedPages;
        public readonly JitCodeMemory Code = new();
        public bool TrackingPageTables;
        public long CompilationTicks;
        public ulong CompiledBlocks;
        public ulong ExecutedBlocks;
        public ulong NativeInstructions;
        public ulong InterpretedInstructions;

        public JitState(int ramSize)
        {
            ProtectedPages = new bool[((long)ramSize + 4095) >> 12];
            FlushTlb();
        }

        public void ProtectPage(int page)
        {
            // Native stores must not alter executable bytes or cached page-table mappings
            if (ProtectedPages[page])
                return;
            ProtectedPages[page] = true;
            WriteTlb.Clear();
        }

        public void FlushTlb()
        {
            ClearDispatch();
            ReadTlb.Clear();
            WriteTlb.Clear();
            ExecuteTlb.Clear();
        }

        public void ClearDispatch()
        {
            if (++Generation == 0)
            {
                Array.Clear(Dispatch);
                Generation = 1;
            }
        }

        public void MakeRoomForBlock()
        {
            if (Blocks.Count < 16384)
                return;
            foreach (var pair in Blocks)
                if (pair.Value.Count < 0)
                    Blocks.Remove(pair.Key);
            if (Blocks.Count >= 16384)
                ResetCache();
        }

        public void ResetCache()
        {
            Blocks.Clear();
            Array.Clear(ProtectedPages);
            FlushTlb();
            Code.Reset();
        }

        public void Dispose() => Code.Dispose();
    }

    private sealed partial class JitCodeMemory : SafeHandleZeroOrMinusOneIsInvalid
    {
        private const int Capacity = 16 * 1024 * 1024;
        private int _used;
        public int Remaining => Capacity - _used;

        public JitCodeMemory() : base(true)
        {
            SetHandle(OperatingSystem.IsWindows() ? VirtualAlloc(0, Capacity, 0x3000, 0x04) : Mmap(0, Capacity, 3, 0x22, -1, 0));
            if (IsInvalid)
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        public nint Add(byte[] bytes)
        {
            if (bytes.Length == 0)
                return 0;
            int size = checked((bytes.Length + 15) & ~15);
            int pageOffset = _used & ~4095;
            nuint pageSize = (nuint)((_used + size - pageOffset + 4095) & ~4095);
            nint entry = handle + _used;
            Protect(handle + pageOffset, pageSize, executable: false);
            Marshal.Copy(bytes, 0, entry, bytes.Length);
            Protect(handle + pageOffset, pageSize, executable: true);
            if (OperatingSystem.IsWindows() && !FlushInstructionCache(new nint(-1), entry, (nuint)bytes.Length))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            _used += size;
            return entry;
        }

        public void Reset()
        {
            Protect(handle, Capacity, executable: false);
            _used = 0;
        }

        private static void Protect(nint address, nuint size, bool executable)
        {
            bool success = OperatingSystem.IsWindows()
                ? VirtualProtect(address, size, executable ? 0x20U : 0x04U, out _)
                : Mprotect(address, size, executable ? 5 : 3) == 0;
            if (!success)
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        protected override bool ReleaseHandle()
            => OperatingSystem.IsWindows() ? VirtualFree(handle, 0, 0x8000) : Munmap(handle, Capacity) == 0;

        [LibraryImport("kernel32", SetLastError = true)]
        private static partial nint VirtualAlloc(nint address, nuint size, uint allocationType, uint protection);
        [LibraryImport("kernel32", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool VirtualProtect(nint address, nuint size, uint protection, out uint oldProtection);
        [LibraryImport("kernel32", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool VirtualFree(nint address, nuint size, uint freeType);
        [LibraryImport("kernel32", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool FlushInstructionCache(nint process, nint address, nuint size);
        [LibraryImport("libc", EntryPoint = "mmap", SetLastError = true)]
        private static partial nint Mmap(nint address, nuint size, int protection, int flags, int descriptor, long offset);
        [LibraryImport("libc", EntryPoint = "mprotect", SetLastError = true)]
        private static partial int Mprotect(nint address, nuint size, int protection);
        [LibraryImport("libc", EntryPoint = "munmap", SetLastError = true)]
        private static partial int Munmap(nint address, nuint size);
    }
}
