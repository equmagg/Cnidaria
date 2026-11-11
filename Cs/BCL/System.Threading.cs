using Microsoft.Win32.SafeHandles;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Tracing;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace System.Threading
{
    public class SynchronizationLockException : SystemException
    {
        public SynchronizationLockException()
            : base(string.Empty)
        { }

        public SynchronizationLockException(string? message)
            : base(message ?? string.Empty)
        { }

        public SynchronizationLockException(string? message, Exception? innerException)
            : base(message ?? string.Empty, innerException)
        { }
    }
    public sealed class Thread
    {
        /// <summary>Returns the operating system identifier for the current thread.</summary>
        internal static ulong CurrentOSThreadId => Interop.Sys.GetCurrentThreadId();

        // Extra bits used in _threadState
        private const ThreadState ThreadPoolThread = (ThreadState)0x1000;
#if TARGET_WINDOWS
        private const ThreadState Interrupted = (ThreadState)0x2000;
#endif

        // Bits of _threadState that are returned by the ThreadState property
        private const ThreadState PublicThreadStateMask = (ThreadState)0x1FF;

        internal ExecutionContext? _executionContext;
        internal SynchronizationContext? _synchronizationContext;

        private volatile int _threadState = (int)ThreadState.Unstarted;
        private ThreadPriority _priority;

        private ManagedThreadId _managedThreadId;
        private string? _name;

        // Protects starting the thread and setting its priority
        private Lock _lock = new Lock(useTrivialWaits: true);

        // This is used for a quick check on thread pool threads after running a work item to determine if the name, background
        // state, or priority were changed by the work item, and if so to reset it. Other threads may also change some of those,
        // but those types of changes may race with the reset anyway, so this field doesn't need to be synchronized.
        private bool _mayNeedResetForThreadPool;

        // so far the only place we initialize it is `WaitForForegroundThreads`
        // and only in the case when there are running foreground threads
        // by the moment of `StartupCodeHelpers.Shutdown()` invocation
        private static ManualResetEvent s_allDone;

        private static int s_foregroundRunningCount;

        [ThreadStatic]
        private static Thread? t_currentThread;

        // Event signaling that the thread has stopped
        private ManualResetEvent _stopped;

        private bool _isBackground;
        private bool _isThreadPoolThread;

        private readonly Delegate? _start;

        private Thread()
        {
            _managedThreadId = System.Threading.ManagedThreadId.GetCurrentThreadId();
        }

        private Thread(Delegate start)
        {
            ArgumentNullException.ThrowIfNull(start);
            _start = start;
            _managedThreadId = new ManagedThreadId();
        }

        public Thread(ThreadStart start) : this((Delegate)start)
        {
        }

        public Thread(ThreadStart start, int maxStackSize) : this((Delegate)start)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(maxStackSize);
        }

        public Thread(ParameterizedThreadStart start) : this((Delegate)start)
        {
        }

        public Thread(ParameterizedThreadStart start, int maxStackSize) : this((Delegate)start)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(maxStackSize);
        }

        // Platform-specific initialization of foreign threads, i.e. threads not created by Thread.Start
        private void PlatformSpecificInitializeExistingThread()
        {
            _stopped = new ManualResetEvent(false);
        }

#pragma warning disable CA1822, IDE0060
        private ThreadPriority GetPriorityLive()
        {
            return ThreadPriority.Normal;
        }

        private bool SetPriorityLive(ThreadPriority priority)
        {
            return true;
        }
#pragma warning restore CA1822, IDE0060

        public static Thread CurrentThread
        {
            [Intrinsic]
            get
            {
                return t_currentThread ?? InitializeCurrentThread();
            }
        }

        // Slow path executed once per thread
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Thread InitializeCurrentThread()
        {
            var currentThread = new Thread();

            ThreadState state = 0;

            // The main thread is foreground, other ones are background
            if (currentThread._managedThreadId.Id != System.Threading.ManagedThreadId.IdMainThread)
            {
                state |= ThreadState.Background;
            }
            else
            {
                Interlocked.Increment(ref s_foregroundRunningCount);
            }

            currentThread._threadState = (int)(state | ThreadState.Running);
            currentThread.PlatformSpecificInitializeExistingThread();
            currentThread._priority = currentThread.GetPriorityLive();
            t_currentThread = currentThread;

            return currentThread;
        }

        public int ManagedThreadId => _managedThreadId.Id;

        public string? Name
        {
            get => _name;
            set => _name = value;
        }

        public bool IsBackground
        {
            get => _isBackground;
            set => _isBackground = value;
        }

        public bool IsThreadPoolThread
        {
            get => _isThreadPoolThread;
            internal set => _isThreadPoolThread = value;
        }

        public void Start() => StartCore();

        public void Start(object? parameter) => StartCore();

        public void UnsafeStart() => StartCore();

        public void UnsafeStart(object? parameter) => StartCore();

        private static void StartCore()
        {
            RuntimeFeature.ThrowIfMultithreadingIsNotSupported();
            throw new PlatformNotSupportedException();
        }

        public static void Sleep(int millisecondsTimeout)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(millisecondsTimeout, Timeout.Infinite);
            SleepInternal(millisecondsTimeout);
        }

        public static void Sleep(TimeSpan timeout) => Sleep(WaitHandle.ToTimeoutMilliseconds(timeout));

        internal static void SleepInternal(int millisecondsTimeout)
        {
            RuntimeFeature.ThrowIfMultithreadingIsNotSupported();
            throw new PlatformNotSupportedException();
        }

        internal static void UninterruptibleSleep0()
        {
        }

        public static bool Yield() => false;

        public static void SpinWait(int iterations)
        {
        }

        internal static int OptimalMaxSpinWaitsPerSpinIteration => 1;

        internal void ResetThreadPoolThread()
        {
        }

        public static void MemoryBarrier() => Interlocked.MemoryBarrier();

        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void FastPollGC();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetCurrentProcessorId()
        {
            return ProcessorIdCache.GetCurrentProcessorId();
        }

        //internal static int GetCurrentProcessorNumber() => Interop.Sys.SchedGetCpu();
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern int GetCurrentProcessorNumber();
    }
    internal static class WaitSubsystem
    {
        private static readonly LowLevelLock s_lock = new LowLevelLock();

        // Exception handling may use the WaitSubsystem. It means that we need to release the WaitSubsystem
        // lock before throwing any exceptions to avoid deadlocks. LockHolder allows us to pass the lock state
        // around and keep track of whether the lock still needs to be released.
        public struct LockHolder
        {
            private LowLevelLock? _lock;

            public LockHolder(LowLevelLock l)
            {
                l.Acquire();
                _lock = l;
            }

            public void Dispose()
            {
                if (_lock != null)
                {
                    _lock.Release();
                    _lock = null;
                }
            }
        }

        public interface IWaitableObject
        {
            void OnDeleteHandle();
            int Wait_Locked(ThreadWaitInfo waitInfo, int timeoutMilliseconds, bool interruptible, ref LockHolder lockHolder);
            void Signal(int count, ref LockHolder lockHolder);
        }

        private static class HandleManager
        {
            public static IntPtr NewHandle(IWaitableObject waitableObject)
            {
                IntPtr handle = GCHandle.ToIntPtr(GCHandle.Alloc(waitableObject, GCHandleType.Normal));

                return handle;
            }

            public static IWaitableObject FromHandle(IntPtr handle)
            {
                if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                {
                    WaitHandle.ThrowInvalidHandleException();
                }

                // We don't know if any other handles are invalid, and this may crash or otherwise do bad things, that is by
                // design, IntPtr is unsafe by nature.
                return (IWaitableObject)GCHandle.FromIntPtr(handle).Target!;
            }

            /// <summary>
            /// Unlike on Windows, a handle may not be deleted more than once with this implementation
            /// </summary>
            public static void DeleteHandle(IntPtr handle)
            {
                if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                {
                    return;
                }

                // We don't know if any other handles are invalid, and this may crash or otherwise do bad things, that is by
                // design, IntPtr is unsafe by nature.
                FromHandle(handle).OnDeleteHandle();
                GCHandle.FromIntPtr(handle).Free();
            }
        }

        /// <summary>
        /// Contains thread-specific information for the wait subsystem. There is one instance per thread that is registered
        /// using <see cref="WaitedListNode"/>s with each <see cref="WaitableObject"/> that the thread is waiting upon.
        ///
        /// Used by the wait subsystem on Unix, so this class cannot have any dependencies on the wait subsystem.
        /// </summary>
        public sealed class ThreadWaitInfo
        {

        }
    }
    internal enum MutexTryAcquireLockResult : byte
    {
        AcquiredLock,
        AcquiredLockButMutexWasAbandoned,
        TimedOut,
        AcquiredLockRecursively,
    }
    public enum ThreadPriority
    {
        /*=========================================================================
        ** Constants for thread priorities.
        =========================================================================*/
        Lowest = 0,
        BelowNormal = 1,
        Normal = 2,
        AboveNormal = 3,
        Highest = 4
    }
    [Flags]
    public enum ThreadState
    {
        /*=========================================================================
        ** Constants for thread states.
        =========================================================================*/
        Running = 0,
        StopRequested = 1,
        SuspendRequested = 2,
        Background = 4,
        Unstarted = 8,
        Stopped = 16,
        WaitSleepJoin = 32,
        Suspended = 64,
        AbortRequested = 128,
        Aborted = 256
    }
    internal static class ProcessorIdCache
    {
        [ThreadStatic]
        private static int t_currentProcessorIdCache;

        private const int ProcessorIdCacheShift = 16;
        private const int ProcessorIdCacheCountDownMask = (1 << ProcessorIdCacheShift) - 1;
        // Refresh rate of the cache. Will be derived from a speed check of GetCurrentProcessorNumber API.
        private static int s_processorIdRefreshRate;
        // We will not adjust higher than this though.
        private const int MaxIdRefreshRate = 5000;

        private static int RefreshCurrentProcessorId()
        {
            int currentProcessorId = Thread.GetCurrentProcessorNumber();

            // Mask with int.MaxValue to ensure the execution Id is not negative
            t_currentProcessorIdCache = ((currentProcessorId << ProcessorIdCacheShift) & int.MaxValue) | s_processorIdRefreshRate;

            return currentProcessorId;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetCurrentProcessorId()
        {
            int currentProcessorIdCache = t_currentProcessorIdCache--;
            if ((currentProcessorIdCache & ProcessorIdCacheCountDownMask) == 0)
            {
                return RefreshCurrentProcessorId();
            }

            return currentProcessorIdCache >> ProcessorIdCacheShift;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static int UninlinedThreadStatic()
        {
            return t_currentProcessorIdCache;
        }
    }
    /// <summary>
    /// Indicates whether an <see cref="EventWaitHandle" /> is reset automatically or manually after receiving a signal.
    /// </summary>
    public enum EventResetMode
    {
        AutoReset = 0,
        ManualReset = 1
    }
    public abstract class WaitHandle : MarshalByRefObject, IDisposable
    {
        internal const int MaxWaitHandles = 64;

        protected static readonly IntPtr InvalidHandle = new IntPtr(-1);

        // IMPORTANT:
        // - Do not add or rearrange fields as the EE depends on this layout.

        private SafeWaitHandle? _waitHandle;
        private bool _disposed;

        [ThreadStatic]
        private static SafeWaitHandle?[]? t_safeWaitHandlesForRent;

        // The wait result values below match Win32 wait result codes (WAIT_OBJECT_0,
        // WAIT_ABANDONED, WAIT_TIMEOUT).

        // Successful wait on first object. When waiting for multiple objects the
        // return value is (WaitSuccess + waitIndex).
        internal const int WaitSuccess = 0;

        // The specified object is a mutex object that was not released by the
        // thread that owned the mutex object before the owning thread terminated.
        // When waiting for multiple objects the return value is (WaitAbandoned +
        // waitIndex).
        internal const int WaitAbandoned = 0x80;

        public const int WaitTimeout = 0x102;
        internal const int WaitFailed = unchecked((int)0xffffffff);

        protected WaitHandle()
        {
        }

        [AllowNull]
        public SafeWaitHandle SafeWaitHandle
        {
            get => _waitHandle ??= new SafeWaitHandle(InvalidHandle, false);
            set => _waitHandle = value;
        }

        internal static int ToTimeoutMilliseconds(TimeSpan timeout)
        {
            long timeoutMilliseconds = (long)timeout.TotalMilliseconds;
            ArgumentOutOfRangeException.ThrowIfLessThan(timeoutMilliseconds, -1, nameof(timeout));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(timeoutMilliseconds, int.MaxValue, nameof(timeout));
            return (int)timeoutMilliseconds;
        }

        public virtual bool WaitOne(int millisecondsTimeout)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(millisecondsTimeout, -1);
            return WaitOneNoCheck(millisecondsTimeout);
        }

        public virtual bool WaitOne(TimeSpan timeout) => WaitOneNoCheck(ToTimeoutMilliseconds(timeout));

        public virtual bool WaitOne() => WaitOneNoCheck(Timeout.Infinite);

        internal bool WaitOneNoCheck(int millisecondsTimeout, bool useTrivialWaits = false)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (TryConsumeSignal())
            {
                return true;
            }
            if (millisecondsTimeout == 0)
            {
                return false;
            }
            RuntimeFeature.ThrowIfMultithreadingIsNotSupported();
            throw new PlatformNotSupportedException();
        }

        private protected abstract bool TryConsumeSignal();

        internal static int WaitMultipleIgnoringSyncContext(nint[] waitHandles, bool waitAll, int millisecondsTimeout)
        {
            RuntimeFeature.ThrowIfBlockingIsNotSupported(millisecondsTimeout);
            throw new PlatformNotSupportedException();
        }

        public virtual void Close() => Dispose();

        protected virtual void Dispose(bool explicitDisposing)
        {
            _disposed = true;
            _waitHandle?.Close();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        internal static void ThrowInvalidHandleException()
        {
            var ex = new InvalidOperationException(SR.InvalidOperation_InvalidHandle);
            ex.HResult = HResults.E_HANDLE;
            throw ex;
        }
    }
    public partial class EventWaitHandle : WaitHandle
    {
        private readonly EventResetMode _mode;
        private int _signaled;

        public EventWaitHandle(bool initialState, EventResetMode mode)
        {
            ValidateMode(mode);
            _mode = mode;
            _signaled = initialState ? 1 : 0;
        }

        private static void ValidateMode(EventResetMode mode)
        {
            if (mode != EventResetMode.AutoReset && mode != EventResetMode.ManualReset)
            {
                throw new ArgumentException(SR.Argument_InvalidFlag, nameof(mode));
            }
        }

        public bool Reset()
        {
            Volatile.Write(ref _signaled, 0);
            return true;
        }

        public bool Set()
        {
            Volatile.Write(ref _signaled, 1);
            return true;
        }

        private protected override bool TryConsumeSignal() =>
            _mode == EventResetMode.AutoReset ? Interlocked.Exchange(ref _signaled, 0) != 0 : Volatile.Read(in _signaled) != 0;
    }
    public sealed class AutoResetEvent : EventWaitHandle
    {
        public AutoResetEvent(bool initialState) : base(initialState, EventResetMode.AutoReset) { }
    }
    public sealed class ManualResetEvent : EventWaitHandle
    {
        public ManualResetEvent(bool initialState) : base(initialState, EventResetMode.ManualReset) { }
    }
    internal sealed class Condition
    {
        internal sealed class Waiter
        {
            public Waiter? next;
            public Waiter? prev;
            public readonly AutoResetEvent ev = new AutoResetEvent(false);
        }

        [ThreadStatic]
        private static Waiter? t_waiterForCurrentThread;

        // Takes the cached Waiter for this thread (or allocates a new one) and removes the
        // current wait's cached Waiter from the thread-static so that any reentrant
        // Monitor.Wait (for example, from a SynchronizationContext message pump) gets its own Waiter with a distinct AutoResetEvent.
        private static Waiter GetWaiterForCurrentThread()
        {
            Waiter? waiter = t_waiterForCurrentThread;
            if (waiter is not null)
            {
                t_waiterForCurrentThread = null;
            }
            else
            {
                waiter = new Waiter();
            }

            return waiter;
        }

        private static void ReleaseWaiterForCurrentThread(Waiter waiter)
        {
            // Return the waiter to the thread-static cache for reuse.
            t_waiterForCurrentThread = waiter;
        }

        private readonly Lock _lock;

        // When condition is installed in a Lock it takes the same field as waitEvent would.
        // If waitEvent is also needed, it is available through here.
        internal AutoResetEvent? _waitEvent;

        private Waiter? _waitersHead;
        private Waiter? _waitersTail;

        internal Lock AssociatedLock => _lock;

        private void AssertIsInList(Waiter waiter)
        {
            Waiter waiter2 = _waitersHead;
            while (waiter2 is not null && waiter2 != waiter)
            {
                waiter2 = waiter2.next;
            }
        }

        private void AssertIsNotInList(Waiter waiter)
        {
            for (Waiter waiter2 = _waitersHead; waiter2 is not null; waiter2 = waiter2.next)
            {
            }
        }

        internal bool NotInList(Waiter waiter)
        {
            if (_waitersHead != waiter)
            {
                return waiter.prev is null;
            }
            return false;
        }

        private void AddWaiter(Waiter waiter)
        {
            Waiter waiter2 = (waiter.prev = _waitersTail);
            if (waiter2 is null)
            {
                _waitersHead = waiter;
            }
            else
            {
                waiter2.next = waiter;
            }
            _waitersTail = waiter;
        }

        private void RemoveWaiter(Waiter waiter)
        {
            if (waiter.next is not null)
            {
                waiter.next.prev = waiter.prev;
            }
            else
            {
                _waitersTail = waiter.prev;
            }
            if (waiter.prev is not null)
            {
                waiter.prev.next = waiter.next;
            }
            else
            {
                _waitersHead = waiter.next;
            }
            waiter.next = null;
            waiter.prev = null;
        }

        public Condition(Lock @lock)
        {
            ArgumentNullException.ThrowIfNull(@lock, "@lock");
            _lock = @lock;
        }

        public bool Wait(int millisecondsTimeout, object associatedObjectForMonitorWait)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(millisecondsTimeout, -1);
            if (!_lock.IsHeldByCurrentThread)
            {
                throw new SynchronizationLockException();
            }
            using (new ThreadBlockingInfo.Scope(this, millisecondsTimeout))
            {
                Waiter waiterForCurrentThread = GetWaiterForCurrentThread();
                AddWaiter(waiterForCurrentThread);
                uint previousRecursionCount = _lock.ExitAll();
                bool flag = false;
                bool flag2;
                try
                {
                    flag = waiterForCurrentThread.ev.WaitOneNoCheck(millisecondsTimeout, useTrivialWaits: false);
                }
                finally
                {
                    _lock.Reenter(previousRecursionCount);
                    flag2 = NotInList(waiterForCurrentThread);
                    if (!flag2)
                    {
                        RemoveWaiter(waiterForCurrentThread);
                    }
                    else if (!flag)
                    {
                        waiterForCurrentThread.ev.Reset();
                    }
                    ReleaseWaiterForCurrentThread(waiterForCurrentThread);
                }
                return flag2;
            }
        }

        public void SignalAll()
        {
            if (!_lock.IsHeldByCurrentThread)
            {
                throw new SynchronizationLockException();
            }
            Waiter waiter = _waitersHead;
            if (waiter is not null)
            {
                _waitersHead = null;
                _waitersTail = null;
                while (waiter is not null)
                {
                    Waiter next = waiter.next;
                    waiter.next = null;
                    waiter.prev = null;
                    waiter.ev.Set();
                    waiter = next;
                }
            }
        }

        public void SignalOne()
        {
            if (!_lock.IsHeldByCurrentThread)
            {
                throw new SynchronizationLockException();
            }
            Waiter waitersHead = _waitersHead;
            if (waitersHead is not null)
            {
                RemoveWaiter(waitersHead);
                waitersHead.ev.Set();
            }
        }
    }
    public sealed class Lock
    {
        private const short DefaultMaxSpinCount = 22;
        private const short DefaultAdaptiveSpinPeriod = 100;
        private const short SpinSleep0Threshold = 10;
        private const ushort MaxDurationMsForPreemptingWaiters = 100;

        private const short SpinCountNotInitialized = short.MinValue;

        internal const int UninitializedThreadId = 0;

        // NOTE: Lock must not have a static (class) constructor, as Lock itself is used to synchronize
        // class construction.  If Lock has its own class constructor, this can lead to infinite recursion.
        // All static data in Lock must be lazy-initialized.
        private static int s_staticsInitializationStage;
        private static bool s_isSingleProcessor;
        private static short s_maxSpinCount;
        private static short s_minSpinCountForAdaptiveSpin;

        private static long s_contentionCount;

        private int _owningThreadId; // cDAC depends on exact name of this field

        private uint _state; // see State for layout. cDAC depends on exact name of this field
        private uint _recursionCount; // cDAC depends on exact name of this field

        // This field serves a few purposes currently:
        // - When positive, it indicates the number of spin-wait iterations that most threads would do upon contention
        // - When zero, it indicates that spin-waiting is to be attempted by a thread to test if it is successful
        // - When negative, it serves as a rough counter for contentions that would increment it towards zero
        //
        // See references to this field and "AdaptiveSpin" in TryEnterSlow for more information.
        private short _spinCount;

        private ushort _waiterStartTimeMs;

        private object? _waitEventOrCondition;
        private AutoResetEvent? WaitEvent
        {
            get
            {
                object? weoc = _waitEventOrCondition;
                if (weoc is Condition c)
                    return c._waitEvent;

                return (AutoResetEvent?)weoc;
            }
        }

        internal int OwningManagedThreadId => (int)_owningThreadId;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Enter()
        {
            TryEnter_Inlined(-1);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private int EnterAndGetCurrentThreadId()
        {
            return TryEnter_Inlined(-1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int TryEnter_Inlined(int timeoutMs)
        {
            int currentManagedThreadIdUnchecked = ManagedThreadId.CurrentManagedThreadIdUnchecked;
            if (currentManagedThreadIdUnchecked != 0 && State.TryLock(this))
            {
                _owningThreadId = currentManagedThreadIdUnchecked;
                return currentManagedThreadIdUnchecked;
            }
            return TryEnterSlow(timeoutMs, currentManagedThreadIdUnchecked);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Scope EnterScope() => new Scope(this, EnterAndGetCurrentThreadId());

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Exit()
        {
            int owningThreadId = _owningThreadId;
            if (owningThreadId == 0 || owningThreadId != ManagedThreadId.CurrentManagedThreadIdUnchecked)
            {
                ThrowHelper.ThrowSynchronizationLockException_LockExit();
            }
            ExitImpl();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void Exit(int currentThreadId)
        {
            if (_owningThreadId != currentThreadId)
            {
                ThrowHelper.ThrowSynchronizationLockException_LockExit();
            }

            ExitImpl();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExitImpl()
        {
            if (_recursionCount == 0)
            {
                _owningThreadId = 0;

                State state = State.Unlock(this);
                if (state.HasAnyWaiters)
                {
                    SignalWaiterIfNecessary(state);
                }
            }
            else
            {
                _recursionCount--;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void SignalWaiterIfNecessary(State state)
        {
            if (State.TrySetIsWaiterSignaledToWake(this, state))
            {
                WaitEvent.Set();
            }
        }

        private bool ShouldStopPreemptingWaiters
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                ushort waiterStartTimeMs = _waiterStartTimeMs;
                return
                    waiterStartTimeMs != 0 &&
                    (ushort)(Environment.TickCount - waiterStartTimeMs) >= MaxDurationMsForPreemptingWaiters;
            }
        }

        /// <summary>
        /// A disposable structure that is returned by <see cref="EnterScope()"/>, which when disposed, exits the lock.
        /// </summary>
        public ref struct Scope
        {
            private Lock? _lockObj;
            private int _currentThreadId;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal Scope(Lock lockObj, int currentThreadId)
            {
                _lockObj = lockObj;
                _currentThreadId = currentThreadId;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Dispose()
            {
                Lock? lockObj = _lockObj;
                if (lockObj is not null)
                {
                    _lockObj = null;
                    lockObj.Exit(_currentThreadId);
                }
            }
        }
        private struct State : IEquatable<State>
        {
            // Layout constants for Lock._state
            private const uint IsLockedMask = (uint)1 << 0; // bit 0
            private const uint ShouldNotPreemptWaitersMask = (uint)1 << 1; // bit 1
            private const uint SpinnerCountIncrement = (uint)1 << 2; // bits 2-4
            private const uint SpinnerCountMask = (uint)0x7 << 2;
            private const uint IsWaiterSignaledToWakeMask = (uint)1 << 5; // bit 5
            private const uint UseTrivialWaitsMask = (uint)1 << 6; // bit 6
            private const uint WaiterCountIncrement = (uint)1 << 7; // bits 7-31

            private uint _state;

            public State(Lock lockObj) : this(lockObj._state) { }
            private State(uint state) => _state = state;

            public static uint InitialStateValue => 0;
            public static uint LockedStateValue => IsLockedMask;
            private static uint Neg(uint state) => (uint)-(int)state;
            public bool IsInitialState => this == default;
            public bool IsLocked => (_state & IsLockedMask) != 0;

            private void SetIsLocked()
            {
                _state += IsLockedMask;
            }

            private bool ShouldNotPreemptWaiters => (_state & ShouldNotPreemptWaitersMask) != 0;

            private void SetShouldNotPreemptWaiters()
            {
                _state += ShouldNotPreemptWaitersMask;
            }

            private void ClearShouldNotPreemptWaiters()
            {
                _state -= ShouldNotPreemptWaitersMask;
            }

            private bool ShouldNonWaiterAttemptToAcquireLock
            {
                get
                {
                    return (_state & (IsLockedMask | ShouldNotPreemptWaitersMask)) == 0;
                }
            }

            private bool HasAnySpinners => (_state & SpinnerCountMask) != 0;

            private bool TryIncrementSpinnerCount()
            {
                uint newState = _state + SpinnerCountIncrement;
                if (new State(newState).HasAnySpinners) // overflow check
                {
                    _state = newState;
                    return true;
                }
                return false;
            }

            private void DecrementSpinnerCount()
            {
                _state -= SpinnerCountIncrement;
            }

            private bool IsWaiterSignaledToWake => (_state & IsWaiterSignaledToWakeMask) != 0;

            private void SetIsWaiterSignaledToWake()
            {
                _state += IsWaiterSignaledToWakeMask;
            }

            private void ClearIsWaiterSignaledToWake()
            {
                _state -= IsWaiterSignaledToWakeMask;
            }

            // Trivial waits are:
            // - Not interruptible by Thread.Interrupt
            // - Don't allow reentrance through APCs or message pumping
            // - Not forwarded to SynchronizationContext wait overrides
            public bool UseTrivialWaits => (_state & UseTrivialWaitsMask) != 0;

            public static void InitializeUseTrivialWaits(Lock lockObj, bool useTrivialWaits)
            {
                if (useTrivialWaits)
                {
                    lockObj._state = UseTrivialWaitsMask;
                }
            }

            public bool HasAnyWaiters => _state >= WaiterCountIncrement;

            private bool TryIncrementWaiterCount()
            {
                uint newState = _state + WaiterCountIncrement;
                if (new State(newState).HasAnyWaiters) // overflow check
                {
                    _state = newState;
                    return true;
                }
                return false;
            }

            private void DecrementWaiterCount()
            {
                _state -= WaiterCountIncrement;
            }

            public bool NeedToSignalWaiter
            {
                get
                {
                    return (_state & (SpinnerCountMask | IsWaiterSignaledToWakeMask)) == 0;
                }
            }

            public static bool operator ==(State state1, State state2) => state1._state == state2._state;
            public static bool operator !=(State state1, State state2) => !(state1 == state2);

            bool IEquatable<State>.Equals(State other) => this == other;
            public override bool Equals(object? obj) => obj is State other && this == other;
            public override int GetHashCode() => (int)_state;

            private static State CompareExchange(Lock lockObj, State toState, State fromState) =>
                new State(Interlocked.CompareExchange(ref lockObj._state, toState._state, fromState._state));

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool TryLock(Lock lockObj)
            {
                var state = new State(lockObj);
                if (!state.ShouldNonWaiterAttemptToAcquireLock)
                {
                    return false;
                }

                State newState = state;
                newState.SetIsLocked();

                return CompareExchange(lockObj, newState, state) == state;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static State Unlock(Lock lockObj)
            {
                var state = new State(Interlocked.Decrement(ref lockObj._state));
                return state;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool TrySetIsWaiterSignaledToWake(Lock lockObj, State state)
            {
                while (true)
                {
                    if (!state.NeedToSignalWaiter)
                    {
                        return false;
                    }

                    State newState = state;
                    newState.SetIsWaiterSignaledToWake();
                    if (!newState.ShouldNotPreemptWaiters && lockObj.ShouldStopPreemptingWaiters)
                    {
                        newState.SetShouldNotPreemptWaiters();
                    }

                    State stateBeforeUpdate = CompareExchange(lockObj, newState, state);
                    if (stateBeforeUpdate == state)
                    {
                        return true;
                    }
                    if (!stateBeforeUpdate.HasAnyWaiters)
                    {
                        return false;
                    }

                    state = stateBeforeUpdate;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static TryLockResult TryLockBeforeSpinLoop(Lock lockObj, short spinCount, out bool isFirstSpinner)
            {
                isFirstSpinner = false;
                State state = new State(lockObj);
                TryLockResult tryLockResult;
                while (true)
                {
                    State toState = state;
                    tryLockResult = TryLockResult.Spin;
                    if (toState.HasAnyWaiters)
                    {
                        if (toState.ShouldNotPreemptWaiters)
                        {
                            return TryLockResult.Wait;
                        }
                        if (lockObj.ShouldStopPreemptingWaiters)
                        {
                            toState.SetShouldNotPreemptWaiters();
                            tryLockResult = TryLockResult.Wait;
                        }
                    }
                    if (tryLockResult == TryLockResult.Spin)
                    {
                        if (!toState.IsLocked)
                        {
                            toState.SetIsLocked();
                            tryLockResult = TryLockResult.Locked;
                        }
                        else if ((toState.HasAnySpinners && spinCount == 0) || !toState.TryIncrementSpinnerCount())
                        {
                            return TryLockResult.Wait;
                        }
                    }
                    State state2 = CompareExchange(lockObj, toState, state);
                    if (state2 == state)
                    {
                        break;
                    }
                    state = state2;
                }
                if (tryLockResult == TryLockResult.Spin && !state.HasAnySpinners)
                {
                    isFirstSpinner = true;
                }
                return tryLockResult;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static TryLockResult TryLockInsideSpinLoop(Lock lockObj)
            {
                State state = new State(lockObj);
                while (true)
                {
                    if (!state.ShouldNonWaiterAttemptToAcquireLock)
                    {
                        if (!state.ShouldNotPreemptWaiters)
                        {
                            return TryLockResult.Spin;
                        }
                        return TryLockResult.Wait;
                    }
                    State toState = state;
                    toState.SetIsLocked();
                    toState.DecrementSpinnerCount();
                    State state2 = CompareExchange(lockObj, toState, state);
                    if (state2 == state)
                    {
                        break;
                    }
                    state = state2;
                }
                return TryLockResult.Locked;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static TryLockResult TryLockAfterSpinLoop(Lock lockObj)
            {
                State state = new State(Interlocked.Add(ref lockObj._state, Neg(4u)));
                while (true)
                {
                    if (state.IsLocked)
                    {
                        return TryLockResult.Wait;
                    }
                    State toState = state;
                    toState.SetIsLocked();
                    State state2 = CompareExchange(lockObj, toState, state);
                    if (state2 == state)
                    {
                        break;
                    }
                    state = state2;
                }
                return TryLockResult.Locked;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool TryLockBeforeWait(Lock lockObj)
            {
                State state = new State(lockObj);
                bool flag = false;
                while (true)
                {
                    State toState = state;
                    if (toState.ShouldNonWaiterAttemptToAcquireLock)
                    {
                        toState.SetIsLocked();
                    }
                    else
                    {
                        if (!toState.TryIncrementWaiterCount())
                        {
                            ThrowHelper.ThrowOutOfMemoryException_LockEnter_WaiterCountOverflow();
                        }
                        if (!state.HasAnyWaiters && !flag)
                        {
                            flag = true;
                            lockObj.ResetWaiterStartTime();
                        }
                    }
                    State state2 = CompareExchange(lockObj, toState, state);
                    if (state2 == state)
                    {
                        break;
                    }
                    state = state2;
                }
                if (state.ShouldNonWaiterAttemptToAcquireLock)
                {
                    return true;
                }
                if (!state.HasAnyWaiters | flag)
                {
                    lockObj.RecordWaiterStartTime();
                }
                return false;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool TryLockInsideWaiterSpinLoop(Lock lockObj)
            {
                bool flag = false;
                State state = new State(lockObj);
                State toState;
                while (true)
                {
                    if (state.IsLocked)
                    {
                        return false;
                    }
                    toState = state;
                    toState.SetIsLocked();
                    toState.ClearIsWaiterSignaledToWake();
                    toState.DecrementWaiterCount();
                    if (toState.ShouldNotPreemptWaiters)
                    {
                        toState.ClearShouldNotPreemptWaiters();
                        if (toState.HasAnyWaiters && !flag)
                        {
                            flag = true;
                            lockObj.RecordWaiterStartTime();
                        }
                    }
                    State state2 = CompareExchange(lockObj, toState, state);
                    if (state2 == state)
                    {
                        break;
                    }
                    state = state2;
                }
                if (toState.HasAnyWaiters && !flag)
                {
                    lockObj.RecordWaiterStartTime();
                }
                return true;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool TryLockAfterWaiterSpinLoop(Lock lockObj)
            {
                State state = new State(Interlocked.Add(ref lockObj._state, Neg(32u)));
                bool flag = false;
                State toState;
                while (true)
                {
                    if (state.IsLocked)
                    {
                        return false;
                    }
                    toState = state;
                    toState.SetIsLocked();
                    toState.DecrementWaiterCount();
                    if (toState.ShouldNotPreemptWaiters)
                    {
                        toState.ClearShouldNotPreemptWaiters();
                        if (toState.HasAnyWaiters && !flag)
                        {
                            flag = true;
                            lockObj.RecordWaiterStartTime();
                        }
                    }
                    State state2 = CompareExchange(lockObj, toState, state);
                    if (state2 == state)
                    {
                        break;
                    }
                    state = state2;
                }
                if (toState.HasAnyWaiters && !flag)
                {
                    lockObj.RecordWaiterStartTime();
                }
                return true;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static void UnregisterWaiter(Lock lockObj)
            {
                State state = new State(lockObj);
                while (true)
                {
                    State toState = state;
                    toState.DecrementWaiterCount();
                    if (toState.ShouldNotPreemptWaiters && !toState.HasAnyWaiters)
                    {
                        toState.ClearShouldNotPreemptWaiters();
                    }
                    State state2 = CompareExchange(lockObj, toState, state);
                    if (state2 == state)
                    {
                        break;
                    }
                    state = state2;
                }
            }
        }

        private enum TryLockResult
        {
            Locked,
            Spin,
            Wait
        }

        private enum StaticsInitializationStage
        {
            NotStarted,
            Started,
            PartiallyComplete,
            Complete
        }

        public bool IsHeldByCurrentThread
        {
            get
            {
                int owningThreadId = _owningThreadId;
                if (owningThreadId != 0)
                {
                    return owningThreadId == ManagedThreadId.CurrentManagedThreadIdUnchecked;
                }
                return false;
            }
        }

        internal static long ContentionCount => s_contentionCount;

        internal int OwningThreadId => _owningThreadId;

        internal static bool IsSingleProcessor => s_isSingleProcessor;

        internal Condition GetOrCreateCondition()
        {
            object waitEventOrCondition;
            Condition condition;
            do
            {
                waitEventOrCondition = _waitEventOrCondition;
                if (waitEventOrCondition is Condition result)
                {
                    return result;
                }
                condition = new Condition(this);
                condition._waitEvent = WaitEvent;
            }
            while (Interlocked.CompareExchange(ref _waitEventOrCondition, condition, waitEventOrCondition) != waitEventOrCondition);
            return condition;
        }

        public Lock()
        {
            _spinCount = short.MinValue;
        }

        public Lock(bool useTrivialWaits)
            : this()
        {
            State.InitializeUseTrivialWaits(this, useTrivialWaits);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public bool TryEnter()
        {
            return TryEnter_Inlined(0) != 0;
        }

        public bool TryEnter(int millisecondsTimeout)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(millisecondsTimeout, -1);
            return TryEnter_Outlined(millisecondsTimeout);
        }

        public bool TryEnter(TimeSpan timeout)
        {
            return TryEnter_Outlined(WaitHandle.ToTimeoutMilliseconds(timeout));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal bool TryEnter_Outlined(int timeoutMs)
        {
            return TryEnter_Inlined(timeoutMs) != 0;
        }

        internal uint ExitAll()
        {
            uint recursionCount = _recursionCount;
            _owningThreadId = 0;
            _recursionCount = 0u;
            State state = State.Unlock(this);
            if (state.HasAnyWaiters)
            {
                SignalWaiterIfNecessary(state);
            }
            return recursionCount;
        }

        internal void Reenter(uint previousRecursionCount)
        {
            Enter();
            _recursionCount = previousRecursionCount;
        }

        private static bool IsAdaptiveSpinEnabled(short minSpinCountForAdaptiveSpin)
        {
            return minSpinCountForAdaptiveSpin <= 0;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal int TryEnterSlow(int timeoutMs, int currentThreadId)
        {
            if (currentThreadId == 0)
            {
                currentThreadId = ManagedThreadId.Current;
                if (State.TryLock(this))
                {
                    goto IL_012d;
                }
            }
            else if (_owningThreadId == currentThreadId)
            {
                uint num = _recursionCount + 1;
                if (num != 0)
                {
                    _recursionCount = num;
                    return currentThreadId;
                }
                throw new LockRecursionException(SR.Lock_Enter_LockRecursionException);
            }
            if (timeoutMs == 0)
            {
                return 0;
            }
            Debugger.NotifyOfCrossThreadDependency();
            if (LazyInitializeOrEnter() == TryLockResult.Locked)
            {
                goto IL_012d;
            }
            short num2 = s_maxSpinCount;
            short num3;
            bool isFirstSpinner;
            TryLockResult tryLockResult;
            if (num2 != 0)
            {
                num3 = s_minSpinCountForAdaptiveSpin;
                short num4 = _spinCount;
                if (num4 >= 0)
                {
                    tryLockResult = State.TryLockBeforeSpinLoop(this, num4, out isFirstSpinner);
                    if (tryLockResult == TryLockResult.Spin)
                    {
                        if (isFirstSpinner)
                        {
                            num4 = num2;
                        }
                        short num5 = 0;
                        while (true)
                        {
                            LowLevelSpinWaiter.Wait(num5, 10, isSingleProcessor: false);
                            if (++num5 >= num4)
                            {
                                break;
                            }
                            tryLockResult = State.TryLockInsideSpinLoop(this);
                            if (tryLockResult == TryLockResult.Spin)
                            {
                                continue;
                            }
                            goto IL_00be;
                        }
                        goto IL_00e4;
                    }
                    goto IL_0129;
                }
                _spinCount = (short)(num4 + 1);
            }
            goto IL_0136;
        IL_012d:
            _owningThreadId = currentThreadId;
            return currentThreadId;
        IL_00be:
            if (tryLockResult != TryLockResult.Locked)
            {
                goto IL_00e4;
            }
            if (isFirstSpinner && IsAdaptiveSpinEnabled(num3))
            {
                short num4 = _spinCount;
                if (num4 < num2)
                {
                    _spinCount = (short)(num4 + 1);
                }
            }
            goto IL_012d;
        IL_0129:
            if (tryLockResult != TryLockResult.Wait)
            {
                goto IL_012d;
            }
            goto IL_0136;
        IL_00e4:
            tryLockResult = State.TryLockAfterSpinLoop(this);
            if (isFirstSpinner && IsAdaptiveSpinEnabled(num3))
            {
                if (tryLockResult == TryLockResult.Locked)
                {
                    short num4 = _spinCount;
                    if (num4 < num2)
                    {
                        _spinCount = (short)(num4 + 1);
                    }
                }
                else
                {
                    short num4 = _spinCount;
                    _spinCount = ((num4 > 0) ? ((short)(num4 - 1)) : num3);
                }
            }
            goto IL_0129;
        IL_0136:
            AutoResetEvent autoResetEvent = WaitEvent ?? CreateWaitEvent();
            if (!State.TryLockBeforeWait(this))
            {
                try
                {
                    Interlocked.Increment(ref s_contentionCount);
                    using (new ThreadBlockingInfo.Scope(this, timeoutMs))
                    {
                        bool flag2 = false;
                        int num7 = ((timeoutMs >= 0) ? Environment.TickCount : 0);
                        int num8 = timeoutMs;
                        while (autoResetEvent.WaitOneNoCheck(num8, new State(this).UseTrivialWaits))
                        {
                            for (short num9 = 0; num9 < num2; num9++)
                            {
                                if (State.TryLockInsideWaiterSpinLoop(this))
                                {
                                    flag2 = true;
                                    break;
                                }
                                LowLevelSpinWaiter.Wait(num9, 10, isSingleProcessor: false);
                            }
                            if (flag2)
                            {
                                break;
                            }
                            if (State.TryLockAfterWaiterSpinLoop(this))
                            {
                                flag2 = true;
                                break;
                            }
                            if (num8 >= 0)
                            {
                                uint num10 = (uint)(Environment.TickCount - num7);
                                if (num10 >= (uint)timeoutMs)
                                {
                                    break;
                                }
                                num8 = timeoutMs - (int)num10;
                            }
                        }
                        if (flag2)
                        {
                            _owningThreadId = currentThreadId;
                            return currentThreadId;
                        }
                    }
                }
                catch
                {
                    State.UnregisterWaiter(this);
                    throw;
                }
                State.UnregisterWaiter(this);
                return 0;
            }
            goto IL_012d;
        }

        private void ResetWaiterStartTime()
        {
            _waiterStartTimeMs = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RecordWaiterStartTime()
        {
            ushort num = (ushort)Environment.TickCount;
            if (num == 0)
            {
                num--;
            }
            _waiterStartTimeMs = num;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private AutoResetEvent CreateWaitEvent()
        {
            AutoResetEvent autoResetEvent = new AutoResetEvent(initialState: false);
            object obj = Interlocked.CompareExchange(ref _waitEventOrCondition, autoResetEvent, null);
            if (obj is Condition condition)
            {
                obj = Interlocked.CompareExchange(ref condition._waitEvent, autoResetEvent, null);
            }
            if (obj is null)
            {
                return autoResetEvent;
            }
            autoResetEvent.Dispose();
            return (AutoResetEvent)obj;
        }

        internal void Dispose()
        {
            WaitEvent?.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryEnterOneShot(int currentManagedThreadId)
        {
            if (State.TryLock(this))
            {
                _owningThreadId = currentManagedThreadId;
                return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool GetIsHeldByCurrentThread(int currentManagedThreadId)
        {
            return _owningThreadId == currentManagedThreadId;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private TryLockResult LazyInitializeOrEnter()
        {
            StaticsInitializationStage staticsInitializationStage = (StaticsInitializationStage)Volatile.Read(in s_staticsInitializationStage);
            if (staticsInitializationStage == StaticsInitializationStage.Started)
            {
                goto IL_002d;
            }
            if (staticsInitializationStage == StaticsInitializationStage.Complete)
            {
                goto IL_0013;
            }
            goto IL_0062;
        IL_0013:
            if (_spinCount == short.MinValue)
            {
                _spinCount = s_maxSpinCount;
            }
            return TryLockResult.Spin;
        IL_0062:
            if (TryInitializeStatics())
            {
                goto IL_0013;
            }
            goto IL_002d;
        IL_002d:
            bool flag = true;
            while (true)
            {
                if (flag)
                {
                    Thread.UninterruptibleSleep0();
                }
                else
                {
                    Thread.SpinWait(1);
                }
                staticsInitializationStage = (StaticsInitializationStage)Volatile.Read(in s_staticsInitializationStage);
                if (staticsInitializationStage == StaticsInitializationStage.Complete)
                {
                    break;
                }
                if (staticsInitializationStage != StaticsInitializationStage.NotStarted)
                {
                    if (State.TryLock(this))
                    {
                        return TryLockResult.Locked;
                    }
                    flag = !flag;
                    continue;
                }
                goto IL_0062;
            }
            goto IL_0013;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TryInitializeStatics()
        {
            StaticsInitializationStage staticsInitializationStage = (StaticsInitializationStage)s_staticsInitializationStage;
            while (true)
            {
                if (staticsInitializationStage == StaticsInitializationStage.Complete)
                {
                    return true;
                }
                StaticsInitializationStage staticsInitializationStage2 = (StaticsInitializationStage)Interlocked.CompareExchange(ref s_staticsInitializationStage, 1, (int)staticsInitializationStage);
                if (staticsInitializationStage2 == StaticsInitializationStage.Started)
                {
                    return false;
                }
                if (staticsInitializationStage2 == staticsInitializationStage)
                {
                    break;
                }
                staticsInitializationStage = staticsInitializationStage2;
            }
            bool flag;
            try
            {
                if (staticsInitializationStage == StaticsInitializationStage.NotStarted)
                {
                    s_isSingleProcessor = Environment.GetProcessorCount() == 1;
                    s_maxSpinCount = DetermineMaxSpinCount();
                    s_minSpinCountForAdaptiveSpin = DetermineMinSpinCountForAdaptiveSpin();
                }
                flag = true;
            }
            catch
            {
                s_staticsInitializationStage = 0;
                throw;
            }
            Volatile.Write(ref s_staticsInitializationStage, flag ? 3 : 2);
            return flag;
        }

        internal void InitializeForMonitor(int managedThreadId, uint recursionCount)
        {
            _state = ((managedThreadId == 0) ? State.InitialStateValue : State.LockedStateValue);
            _owningThreadId = managedThreadId;
            _recursionCount = recursionCount;
        }

        private static short DetermineMaxSpinCount()
        {
            if (IsSingleProcessor)
            {
                return 0;
            }
            return AppContextConfigHelper.GetInt16Config("System.Threading.Lock.SpinCount", "DOTNET_Lock_SpinCount", 22, allowNegative: false);
        }

        private static short DetermineMinSpinCountForAdaptiveSpin()
        {
            short num = AppContextConfigHelper.GetInt16Config("System.Threading.Lock.AdaptiveSpinPeriod", "DOTNET_Lock_AdaptiveSpinPeriod", 100);
            if (num < -1)
            {
                num = 100;
            }
            return (short)(-num);
        }

        internal bool Wait(int millisecondsTimeout)
        {
            return Wait(millisecondsTimeout, this);
        }

        internal bool Wait(int millisecondsTimeout, object associatedObject)
        {
            return GetOrCreateCondition().Wait(millisecondsTimeout, associatedObject);
        }

        internal void Pulse()
        {
            GetOrCreateCondition().SignalOne();
        }

        internal void PulseAll()
        {
            GetOrCreateCondition().SignalAll();
        }
    }
    public static class Monitor
    {
        public static long LockContentionCount => Lock.ContentionCount;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Enter(object obj)
        {
            ObjectHeader.AcquireThinLock(obj);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Enter(object obj, ref bool lockTaken)
        {
            if (lockTaken)
            {
                ThrowLockTakenException();
            }
            Enter(obj);
            lockTaken = true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Exit(object obj)
        {
            ArgumentNullException.ThrowIfNull(obj);
            ObjectHeader.Release(obj);
        }

        public static bool Wait(object obj, int millisecondsTimeout)
        {
            ArgumentNullException.ThrowIfNull(obj);
            RuntimeFeature.ThrowIfBlockingIsNotSupported(millisecondsTimeout);
            throw new PlatformNotSupportedException();
        }

        public static bool Wait(object obj) => Wait(obj, Timeout.Infinite);

        public static void Pulse(object obj)
        {
            ArgumentNullException.ThrowIfNull(obj);
        }

        public static void PulseAll(object obj)
        {
            ArgumentNullException.ThrowIfNull(obj);
        }

        [DoesNotReturn]
        private static void ThrowLockTakenException()
        {
            throw new ArgumentException(SR.Argument_MustBeFalse, "lockTaken");
        }
    }
    internal static class ObjectHeader
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static unsafe void AcquireThinLock(object obj)
        {
            if (obj is null) throw new ArgumentNullException();

        }
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static unsafe void Release(object obj)
        {

        }
    }
    /// <summary>Provides atomic operations for variables that are shared by multiple threads.</summary>
    public static class Volatile
    {
        [Intrinsic]
        public static bool Read(ref readonly bool location) => location;

        [Intrinsic]
        public static int Read(ref readonly int location) => location;

        [Intrinsic]
        public static uint Read(ref readonly uint location) => location;

        [Intrinsic]
        public static long Read(ref readonly long location) => location;

        [Intrinsic]
        public static ulong Read(ref readonly ulong location) => location;

        [Intrinsic]
        public static nint Read(ref readonly nint location) => location;

        [Intrinsic]
        public static T Read<T>(ref readonly T location) where T : class? => location;

        [Intrinsic]
        public static void Write(ref bool location, bool value) => location = value;

        [Intrinsic]
        public static void Write(ref int location, int value) => location = value;

        [Intrinsic]
        public static void Write(ref uint location, uint value) => location = value;

        [Intrinsic]
        public static void Write(ref long location, long value) => location = value;

        [Intrinsic]
        public static void Write(ref ulong location, ulong value) => location = value;

        [Intrinsic]
        public static void Write(ref nint location, nint value) => location = value;

        [Intrinsic]
        public static void Write<T>(ref T location, T value) where T : class? => location = value;
    }
    public static class Interlocked
    {
        #region Increment
        /// <summary>Increments a specified variable and stores the result, as an atomic operation.</summary>
        /// <param name="location">The variable whose value is to be incremented.</param>
        /// <returns>The incremented value.</returns>
        /// <exception cref="NullReferenceException">The address of location is a null pointer.</exception>
        public static int Increment(ref int location) =>
            Add(ref location, 1);

        /// <summary>Increments a specified variable and stores the result, as an atomic operation.</summary>
        /// <param name="location">The variable whose value is to be incremented.</param>
        /// <returns>The incremented value.</returns>
        /// <exception cref="NullReferenceException">The address of location is a null pointer.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Increment(ref uint location) =>
            Add(ref location, 1);

        /// <summary>Increments a specified variable and stores the result, as an atomic operation.</summary>
        /// <param name="location">The variable whose value is to be incremented.</param>
        /// <returns>The incremented value.</returns>
        /// <exception cref="NullReferenceException">The address of location is a null pointer.</exception>
        public static long Increment(ref long location) =>
            Add(ref location, 1);

        /// <summary>Increments a specified variable and stores the result, as an atomic operation.</summary>
        /// <param name="location">The variable whose value is to be incremented.</param>
        /// <returns>The incremented value.</returns>
        /// <exception cref="NullReferenceException">The address of location is a null pointer.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Increment(ref ulong location) =>
            Add(ref location, 1);
        #endregion

        #region Decrement
        /// <summary>Decrements a specified variable and stores the result, as an atomic operation.</summary>
        /// <param name="location">The variable whose value is to be decremented.</param>
        /// <returns>The decremented value.</returns>
        /// <exception cref="NullReferenceException">The address of location is a null pointer.</exception>
        public static int Decrement(ref int location) =>
            Add(ref location, -1);

        /// <summary>Decrements a specified variable and stores the result, as an atomic operation.</summary>
        /// <param name="location">The variable whose value is to be decremented.</param>
        /// <returns>The decremented value.</returns>
        /// <exception cref="NullReferenceException">The address of location is a null pointer.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Decrement(ref uint location) =>
            (uint)Add(ref Unsafe.As<uint, int>(ref location), -1);


        /// <summary>Decrements a specified variable and stores the result, as an atomic operation.</summary>
        /// <param name="location">The variable whose value is to be decremented.</param>
        /// <returns>The decremented value.</returns>
        /// <exception cref="NullReferenceException">The address of location is a null pointer.</exception>
        public static long Decrement(ref long location) =>
            Add(ref location, -1);

        /// <summary>Decrements a specified variable and stores the result, as an atomic operation.</summary>
        /// <param name="location">The variable whose value is to be decremented.</param>
        /// <returns>The decremented value.</returns>
        /// <exception cref="NullReferenceException">The address of location is a null pointer.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Decrement(ref ulong location) =>
            (ulong)Add(ref Unsafe.As<ulong, long>(ref location), -1);
        #endregion

        #region Exchange
        /// <summary>Sets an 8-bit signed integer to a specified value and returns the original value, as an atomic operation.</summary>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static sbyte Exchange(ref sbyte location1, sbyte value) =>
            (sbyte)Exchange(ref Unsafe.As<sbyte, byte>(ref location1), (byte)value);

        /// <summary>Sets an 8-bit unsigned integer to a specified value and returns the original value, as an atomic operation.</summary>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte Exchange(ref byte location1, byte value)
        {
            return Exchange(ref location1, value); // Must expand intrinsic
        }

        /// <summary>Sets a 16-bit signed integer to a specified value and returns the original value, as an atomic operation.</summary>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static short Exchange(ref short location1, short value) =>
            (short)Exchange(ref Unsafe.As<short, ushort>(ref location1), (ushort)value);

        /// <summary>Sets a 16-bit unsigned integer to a specified value and returns the original value, as an atomic operation.</summary>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort Exchange(ref ushort location1, ushort value)
        {
            return Exchange(ref location1, value); // Must expand intrinsic
        }

        /// <summary>Sets a 32-bit signed integer to a specified value and returns the original value, as an atomic operation.</summary>
        /// <param name="location1">The variable to set to the specified value.</param>
        /// <param name="value">The value to which the <paramref name="location1"/> parameter is set.</param>
        /// <returns>The original value of <paramref name="location1"/>.</returns>
        /// <exception cref="NullReferenceException">The address of location1 is a null pointer.</exception>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Exchange(ref int location1, int value)
        {
            return Exchange(ref location1, value); // Must expand intrinsic
        }

        /// <summary>Sets a 32-bit unsigned integer to a specified value and returns the original value, as an atomic operation.</summary>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Exchange(ref uint location1, uint value) =>
            (uint)Exchange(ref Unsafe.As<uint, int>(ref location1), (int)value);

        /// <summary>Sets a 64-bit signed integer to a specified value and returns the original value, as an atomic operation.</summary>
        /// <param name="location1">The variable to set to the specified value.</param>
        /// <param name="value">The value to which the <paramref name="location1"/> parameter is set.</param>
        /// <returns>The original value of <paramref name="location1"/>.</returns>
        /// <exception cref="NullReferenceException">The address of location1 is a null pointer.</exception>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Exchange(ref long location1, long value)
        {
            return Exchange(ref location1, value); // Must expand intrinsic
        }

        /// <summary>Sets a 64-bit unsigned integer to a specified value and returns the original value, as an atomic operation.</summary>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Exchange(ref ulong location1, ulong value) =>
            (ulong)Exchange(ref Unsafe.As<ulong, long>(ref location1), (long)value);

        /// <summary>Sets a single-precision floating point number to a specified value and returns the original value, as an atomic operation.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Exchange(ref float location1, float value)
            => Unsafe.BitCast<int, float>(Exchange(ref Unsafe.As<float, int>(ref location1), Unsafe.BitCast<float, int>(value)));

        /// <summary>Sets a double-precision floating point number to a specified value and returns the original value, as an atomic operation.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Exchange(ref double location1, double value)
            => Unsafe.BitCast<long, double>(Exchange(ref Unsafe.As<double, long>(ref location1), Unsafe.BitCast<double, long>(value)));

        /// <summary>Sets a platform-specific handle or pointer to a specified value and returns the original value, as an atomic operation.</summary>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nint Exchange(ref nint location1, nint value)
        {
#if TARGET_64BIT
            return (nint)Exchange(ref Unsafe.As<nint, long>(ref location1), (long)value);
#else
            return (nint)Exchange(ref Unsafe.As<nint, int>(ref location1), (int)value);
#endif
        }

        /// <summary>Sets a platform-specific handle or pointer to a specified value and returns the original value, as an atomic operation.</summary>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nuint Exchange(ref nuint location1, nuint value)
        {
#if TARGET_64BIT
            return (nuint)Exchange(ref Unsafe.As<nuint, long>(ref location1), (long)value);
#else
            return (nuint)Exchange(ref Unsafe.As<nuint, int>(ref location1), (int)value);
#endif
        }

        /// <summary>Sets an object to a specified value and returns a reference to the original object, as an atomic operation.</summary>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static object? Exchange(ref object? location1, object? value)
        {
            return Exchange(ref location1, value); // Must expand intrinsic
        }

        /// <summary>Sets a variable of the specified type <typeparamref name="T"/> to a specified value and returns the original value, as an atomic operation.</summary>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe T Exchange<T>(ref T location1, T value)
        {
            // Handle all reference types with Exchange(ref object, ...).
            if (!typeof(T).IsValueType)
            {
                object? result = Exchange(ref Unsafe.As<T, object?>(ref location1), value);
                return Unsafe.As<object?, T>(ref result);
            }

            // Handle everything else with an Exchange overload for the integral type of the corresponding size.
            // Only primitive types and enum types (which are backed by primitive types) are supported.
            if (!typeof(T).IsPrimitive && !typeof(T).IsEnum)
            {
                throw new NotSupportedException();
            }

            if (sizeof(T) == 1)
            {
                return Unsafe.BitCast<byte, T>(
                    Exchange(
                        ref Unsafe.As<T, byte>(ref location1),
                        Unsafe.BitCast<T, byte>(value)));
            }

            if (sizeof(T) == 2)
            {
                return Unsafe.BitCast<ushort, T>(
                    Exchange(
                        ref Unsafe.As<T, ushort>(ref location1),
                        Unsafe.BitCast<T, ushort>(value)));
            }

            if (sizeof(T) == 4)
            {
                return Unsafe.BitCast<int, T>(
                    Exchange(
                        ref Unsafe.As<T, int>(ref location1),
                        Unsafe.BitCast<T, int>(value)));
            }

            return Unsafe.BitCast<long, T>(
                Exchange(
                    ref Unsafe.As<T, long>(ref location1),
                    Unsafe.BitCast<T, long>(value)));
        }
        #endregion

        #region CompareExchange
        /// <summary>Compares two 8-bit signed integers for equality and, if they are equal, replaces the first value.</summary>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static sbyte CompareExchange(ref sbyte location1, sbyte value, sbyte comparand) =>
           (sbyte)CompareExchange(ref Unsafe.As<sbyte, byte>(ref location1), (byte)value, (byte)comparand);

        /// <summary>Compares two 16-bit unsigned integers for equality and, if they are equal, replaces the first value.</summary>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static short CompareExchange(ref short location1, short value, short comparand) =>
            (short)CompareExchange(ref Unsafe.As<short, ushort>(ref location1), (ushort)value, (ushort)comparand);

        /// <summary>Compares two 8-bit unsigned integers for equality and, if they are equal, replaces the first value.</summary>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte CompareExchange(ref byte location1, byte value, byte comparand)
        {
            return CompareExchange(ref location1, value, comparand); // Must expand intrinsic
        }

        /// <summary>Compares two 16-bit signed integers for equality and, if they are equal, replaces the first value.</summary>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort CompareExchange(ref ushort location1, ushort value, ushort comparand)
        {
            return CompareExchange(ref location1, value, comparand); // Must expand intrinsic
        }

        /// <summary>Compares two 32-bit unsigned integers for equality and, if they are equal, replaces the first value.</summary>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint CompareExchange(ref uint location1, uint value, uint comparand) =>
            (uint)CompareExchange(ref Unsafe.As<uint, int>(ref location1), (int)value, (int)comparand);

        /// <summary>Compares two 64-bit unsigned integers for equality and, if they are equal, replaces the first value.</summary>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong CompareExchange(ref ulong location1, ulong value, ulong comparand) =>
            (ulong)CompareExchange(ref Unsafe.As<ulong, long>(ref location1), (long)value, (long)comparand);

        /// <summary>Compares two single-precision floating point numbers for equality and, if they are equal, replaces the first value.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CompareExchange(ref float location1, float value, float comparand)
            => Unsafe.BitCast<int, float>(CompareExchange(ref Unsafe.As<float, int>(ref location1), Unsafe.BitCast<float, int>(value), Unsafe.BitCast<float, int>(comparand)));

        /// <summary>Compares two double-precision floating point numbers for equality and, if they are equal, replaces the first value.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double CompareExchange(ref double location1, double value, double comparand)
            => Unsafe.BitCast<long, double>(CompareExchange(ref Unsafe.As<double, long>(ref location1), Unsafe.BitCast<double, long>(value), Unsafe.BitCast<double, long>(comparand)));

        /// <summary>Compares two native-sized signed integers for equality and, if they are equal, replaces the first one.</summary>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nint CompareExchange(ref nint location1, nint value, nint comparand)
        {
#if TARGET_64BIT
            return (nint)CompareExchange(ref Unsafe.As<nint, long>(ref location1), (long)value, (long)comparand);
#else
            return (nint)CompareExchange(ref Unsafe.As<nint, int>(ref location1), (int)value, (int)comparand);
#endif
        }

        /// <summary>Compares two native-sized unsigned integers for equality and, if they are equal, replaces the first one.</summary>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nuint CompareExchange(ref nuint location1, nuint value, nuint comparand)
        {
#if TARGET_64BIT
            return (nuint)CompareExchange(ref Unsafe.As<nuint, long>(ref location1), (long)value, (long)comparand);
#else
            return (nuint)CompareExchange(ref Unsafe.As<nuint, int>(ref location1), (int)value, (int)comparand);
#endif
        }

        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe T CompareExchange<T>(ref T location1, T value, T comparand)
        {
            // Handle all reference types with CompareExchange(ref object, ...).
            if (!typeof(T).IsValueType)
            {
                object? result = CompareExchange(ref Unsafe.As<T, object?>(ref location1), value, comparand);
                return Unsafe.As<object?, T>(ref result);
            }

            // Handle everything else with a CompareExchange overload for the unsigned integral type of the corresponding size.
            // Only primitive types and enum types (which are backed by primitive types) are supported.
            if (!typeof(T).IsPrimitive && !typeof(T).IsEnum)
            {
                throw new NotSupportedException();
            }

            if (sizeof(T) == 1)
            {
                return Unsafe.BitCast<byte, T>(
                    CompareExchange(
                        ref Unsafe.As<T, byte>(ref location1),
                        Unsafe.BitCast<T, byte>(value),
                        Unsafe.BitCast<T, byte>(comparand)));
            }

            if (sizeof(T) == 2)
            {
                return Unsafe.BitCast<ushort, T>(
                    CompareExchange(
                        ref Unsafe.As<T, ushort>(ref location1),
                        Unsafe.BitCast<T, ushort>(value),
                        Unsafe.BitCast<T, ushort>(comparand)));
            }

            if (sizeof(T) == 4)
            {
                return Unsafe.BitCast<int, T>(
                    CompareExchange(
                        ref Unsafe.As<T, int>(ref location1),
                        Unsafe.BitCast<T, int>(value),
                        Unsafe.BitCast<T, int>(comparand)));
            }

            return Unsafe.BitCast<long, T>(
                CompareExchange(
                    ref Unsafe.As<T, long>(ref location1),
                    Unsafe.BitCast<T, long>(value),
                    Unsafe.BitCast<T, long>(comparand)));
        }

        #endregion

        #region Add
        // <summary>Adds two 32-bit signed integers and replaces the first integer with the sum, as an atomic operation.</summary>
        /// <param name="location1">A variable containing the first value to be added. The sum of the two values is stored in <paramref name="location1"/>.</param>
        /// <param name="value">The value to be added to the integer at <paramref name="location1"/>.</param>
        /// <returns>The new value stored at <paramref name="location1"/>.</returns>
        /// <exception cref="NullReferenceException">The address of <paramref name="location1"/> is a null pointer.</exception>
        public static int Add(ref int location1, int value) =>
            ExchangeAdd(ref location1, value) + value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Add(ref uint location1, uint value) =>
            (uint)Add(ref Unsafe.As<uint, int>(ref location1), (int)value);

        /// <summary>Adds two 64-bit signed integers and replaces the first integer with the sum, as an atomic operation.</summary>
        /// <param name="location1">A variable containing the first value to be added. The sum of the two values is stored in <paramref name="location1"/>.</param>
        /// <param name="value">The value to be added to the integer at <paramref name="location1"/>.</param>
        /// <returns>The new value stored at <paramref name="location1"/>.</returns>
        /// <exception cref="NullReferenceException">The address of <paramref name="location1"/> is a null pointer.</exception>
        public static long Add(ref long location1, long value) =>
            ExchangeAdd(ref location1, value) + value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Add(ref ulong location1, ulong value) =>
            (ulong)Add(ref Unsafe.As<ulong, long>(ref location1), (long)value);

        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ExchangeAdd(ref int location1, int value)
        {
#if TARGET_X86 || TARGET_AMD64 || TARGET_ARM64 || TARGET_RISCV64
            return ExchangeAdd(ref location1, value); // Must expand intrinsic
#else
            if (Unsafe.IsNullRef(ref location1))
                throw new NullReferenceException();
            return ExchangeAdd32(ref location1, value);
#endif
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern int ExchangeAdd32(ref int location1, int value);

        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long ExchangeAdd(ref long location1, long value)
        {
#if TARGET_AMD64 || TARGET_ARM64 || TARGET_RISCV64
            return ExchangeAdd(ref location1, value); // Must expand intrinsic
#else
            if (Unsafe.IsNullRef(ref location1))
                throw new NullReferenceException();
            return ExchangeAdd64(ref location1, value);
#endif
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern long ExchangeAdd64(ref long location1, long value);
        #endregion

        #region Read
        /// <summary>Returns a 64-bit unsigned value, loaded as an atomic operation.</summary>
        /// <param name="location">The 64-bit value to be loaded.</param>
        /// <returns>The loaded value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static ulong Read(ref readonly ulong location) =>
            CompareExchange(ref Unsafe.AsRef(in location), 0, 0);
        #endregion

        #region And
        /// <summary>Bitwise "ands" two 32-bit signed integers and replaces the first integer with the result, as an atomic operation.</summary>
        /// <param name="location1">A variable containing the first value to be combined. The result is stored in <paramref name="location1"/>.</param>
        /// <param name="value">The value to be combined with the integer at <paramref name="location1"/>.</param>
        /// <returns>The original value in <paramref name="location1"/>.</returns>
        /// <exception cref="NullReferenceException">The address of <paramref name="location1"/> is a null pointer.</exception>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int And(ref int location1, int value)
        {
            int current = location1;
            while (true)
            {
                int newValue = current & value;
                int oldValue = CompareExchange(ref location1, newValue, current);
                if (oldValue == current)
                {
                    return oldValue;
                }
                current = oldValue;
            }
        }

        /// <summary>Bitwise "ands" two 32-bit unsigned integers and replaces the first integer with the result, as an atomic operation.</summary>
        /// <param name="location1">A variable containing the first value to be combined. The result is stored in <paramref name="location1"/>.</param>
        /// <param name="value">The value to be combined with the integer at <paramref name="location1"/>.</param>
        /// <returns>The original value in <paramref name="location1"/>.</returns>
        /// <exception cref="NullReferenceException">The address of <paramref name="location1"/> is a null pointer.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static uint And(ref uint location1, uint value) =>
            (uint)And(ref Unsafe.As<uint, int>(ref location1), (int)value);

        /// <summary>Bitwise "ands" two 64-bit signed integers and replaces the first integer with the result, as an atomic operation.</summary>
        /// <param name="location1">A variable containing the first value to be combined. The result is stored in <paramref name="location1"/>.</param>
        /// <param name="value">The value to be combined with the integer at <paramref name="location1"/>.</param>
        /// <returns>The original value in <paramref name="location1"/>.</returns>
        /// <exception cref="NullReferenceException">The address of <paramref name="location1"/> is a null pointer.</exception>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long And(ref long location1, long value)
        {
            long current = location1;
            while (true)
            {
                long newValue = current & value;
                long oldValue = CompareExchange(ref location1, newValue, current);
                if (oldValue == current)
                {
                    return oldValue;
                }
                current = oldValue;
            }
        }

        /// <summary>Bitwise "ands" two 64-bit unsigned integers and replaces the first integer with the result, as an atomic operation.</summary>
        /// <param name="location1">A variable containing the first value to be combined. The result is stored in <paramref name="location1"/>.</param>
        /// <param name="value">The value to be combined with the integer at <paramref name="location1"/>.</param>
        /// <returns>The original value in <paramref name="location1"/>.</returns>
        /// <exception cref="NullReferenceException">The address of <paramref name="location1"/> is a null pointer.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static ulong And(ref ulong location1, ulong value) =>
            (ulong)And(ref Unsafe.As<ulong, long>(ref location1), (long)value);

        /// <summary>Bitwise "ands" two values of type <typeparamref name="T"/> and replaces the first value with the result, as an atomic operation.</summary>
        /// <param name="location1">A variable containing the first value to be combined. The result is stored in <paramref name="location1"/>.</param>
        /// <param name="value">The value to be combined with the value at <paramref name="location1"/>.</param>
        /// <returns>The original value in <paramref name="location1"/>.</returns>
        /// <exception cref="NullReferenceException">The address of <paramref name="location1"/> is a null pointer.</exception>
        /// <exception cref="NotSupportedException">An unsupported <typeparamref name="T"/> is specified.</exception>
        /// <typeparam name="T">
        /// The type to be used for <paramref name="location1"/> and <paramref name="value"/>.
        /// This type must be an integer primitive type or an enum type backed by an integer type.
        /// Floating-point types (float, double) are not supported.
        /// </typeparam>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe T And<T>(ref T location1, T value) where T : struct
        {
            // Only integer primitive types and enum types backed by integer types are supported.
            // Floating-point types and floating-point backed enums are not supported.
            if ((!typeof(T).IsPrimitive && !typeof(T).IsEnum) ||
                typeof(T) == typeof(float) || typeof(T) == typeof(double) ||
                (typeof(T).IsEnum && (typeof(T).GetEnumUnderlyingType() == typeof(float) || typeof(T).GetEnumUnderlyingType() == typeof(double))))
            {
                throw new NotSupportedException(SR.NotSupported_IntegerEnumOrPrimitiveTypeRequired);
            }

            // For 1-byte and 2-byte types, we need to use CompareExchange-based implementations
            // because there are no direct atomic And operations for these sizes.
            if (sizeof(T) == 1)
            {
                byte current = Unsafe.BitCast<T, byte>(location1);
                while (true)
                {
                    byte newValue = (byte)(current & Unsafe.BitCast<T, byte>(value));
                    byte oldValue = CompareExchange(
                        ref Unsafe.As<T, byte>(ref location1),
                        newValue,
                        current);
                    if (oldValue == current)
                    {
                        return Unsafe.BitCast<byte, T>(oldValue);
                    }
                    current = oldValue;
                }
            }

            if (sizeof(T) == 2)
            {
                ushort current = Unsafe.BitCast<T, ushort>(location1);
                while (true)
                {
                    ushort newValue = (ushort)(current & Unsafe.BitCast<T, ushort>(value));
                    ushort oldValue = CompareExchange(
                        ref Unsafe.As<T, ushort>(ref location1),
                        newValue,
                        current);
                    if (oldValue == current)
                    {
                        return Unsafe.BitCast<ushort, T>(oldValue);
                    }
                    current = oldValue;
                }
            }

            if (sizeof(T) == 4)
            {
                return Unsafe.BitCast<int, T>(
                    And(
                        ref Unsafe.As<T, int>(ref location1),
                        Unsafe.BitCast<T, int>(value)));
            }

            return Unsafe.BitCast<long, T>(
                And(
                    ref Unsafe.As<T, long>(ref location1),
                    Unsafe.BitCast<T, long>(value)));
        }
        #endregion

        #region Or
        /// <summary>Bitwise "ors" two 32-bit signed integers and replaces the first integer with the result, as an atomic operation.</summary>
        /// <param name="location1">A variable containing the first value to be combined. The result is stored in <paramref name="location1"/>.</param>
        /// <param name="value">The value to be combined with the integer at <paramref name="location1"/>.</param>
        /// <returns>The original value in <paramref name="location1"/>.</returns>
        /// <exception cref="NullReferenceException">The address of <paramref name="location1"/> is a null pointer.</exception>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Or(ref int location1, int value)
        {
            int current = location1;
            while (true)
            {
                int newValue = current | value;
                int oldValue = CompareExchange(ref location1, newValue, current);
                if (oldValue == current)
                {
                    return oldValue;
                }
                current = oldValue;
            }
        }

        /// <summary>Bitwise "ors" two 32-bit unsigned integers and replaces the first integer with the result, as an atomic operation.</summary>
        /// <param name="location1">A variable containing the first value to be combined. The result is stored in <paramref name="location1"/>.</param>
        /// <param name="value">The value to be combined with the integer at <paramref name="location1"/>.</param>
        /// <returns>The original value in <paramref name="location1"/>.</returns>
        /// <exception cref="NullReferenceException">The address of <paramref name="location1"/> is a null pointer.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static uint Or(ref uint location1, uint value) =>
            (uint)Or(ref Unsafe.As<uint, int>(ref location1), (int)value);

        /// <summary>Bitwise "ors" two 64-bit signed integers and replaces the first integer with the result, as an atomic operation.</summary>
        /// <param name="location1">A variable containing the first value to be combined. The result is stored in <paramref name="location1"/>.</param>
        /// <param name="value">The value to be combined with the integer at <paramref name="location1"/>.</param>
        /// <returns>The original value in <paramref name="location1"/>.</returns>
        /// <exception cref="NullReferenceException">The address of <paramref name="location1"/> is a null pointer.</exception>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Or(ref long location1, long value)
        {
            long current = location1;
            while (true)
            {
                long newValue = current | value;
                long oldValue = CompareExchange(ref location1, newValue, current);
                if (oldValue == current)
                {
                    return oldValue;
                }
                current = oldValue;
            }
        }

        /// <summary>Bitwise "ors" two 64-bit unsigned integers and replaces the first integer with the result, as an atomic operation.</summary>
        /// <param name="location1">A variable containing the first value to be combined. The result is stored in <paramref name="location1"/>.</param>
        /// <param name="value">The value to be combined with the integer at <paramref name="location1"/>.</param>
        /// <returns>The original value in <paramref name="location1"/>.</returns>
        /// <exception cref="NullReferenceException">The address of <paramref name="location1"/> is a null pointer.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static ulong Or(ref ulong location1, ulong value) =>
            (ulong)Or(ref Unsafe.As<ulong, long>(ref location1), (long)value);

        /// <summary>Bitwise "ors" two values of type <typeparamref name="T"/> and replaces the first value with the result, as an atomic operation.</summary>
        /// <param name="location1">A variable containing the first value to be combined. The result is stored in <paramref name="location1"/>.</param>
        /// <param name="value">The value to be combined with the value at <paramref name="location1"/>.</param>
        /// <returns>The original value in <paramref name="location1"/>.</returns>
        /// <exception cref="NullReferenceException">The address of <paramref name="location1"/> is a null pointer.</exception>
        /// <exception cref="NotSupportedException">An unsupported <typeparamref name="T"/> is specified.</exception>
        /// <typeparam name="T">
        /// The type to be used for <paramref name="location1"/> and <paramref name="value"/>.
        /// This type must be an integer primitive type or an enum type backed by an integer type.
        /// Floating-point types (float, double) are not supported.
        /// </typeparam>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe T Or<T>(ref T location1, T value) where T : struct
        {
            // Only integer primitive types and enum types backed by integer types are supported.
            // Floating-point types and floating-point backed enums are not supported.
            if ((!typeof(T).IsPrimitive && !typeof(T).IsEnum) ||
                typeof(T) == typeof(float) || typeof(T) == typeof(double) ||
                (typeof(T).IsEnum && (typeof(T).GetEnumUnderlyingType() == typeof(float) || typeof(T).GetEnumUnderlyingType() == typeof(double))))
            {
                throw new NotSupportedException(SR.NotSupported_IntegerEnumOrPrimitiveTypeRequired);
            }

            // For 1-byte and 2-byte types, we need to use CompareExchange-based implementations
            // because there are no direct atomic Or operations for these sizes.
            if (sizeof(T) == 1)
            {
                byte current = Unsafe.BitCast<T, byte>(location1);
                while (true)
                {
                    byte newValue = (byte)(current | Unsafe.BitCast<T, byte>(value));
                    byte oldValue = CompareExchange(
                        ref Unsafe.As<T, byte>(ref location1),
                        newValue,
                        current);
                    if (oldValue == current)
                    {
                        return Unsafe.BitCast<byte, T>(oldValue);
                    }
                    current = oldValue;
                }
            }

            if (sizeof(T) == 2)
            {
                ushort current = Unsafe.BitCast<T, ushort>(location1);
                while (true)
                {
                    ushort newValue = (ushort)(current | Unsafe.BitCast<T, ushort>(value));
                    ushort oldValue = CompareExchange(
                        ref Unsafe.As<T, ushort>(ref location1),
                        newValue,
                        current);
                    if (oldValue == current)
                    {
                        return Unsafe.BitCast<ushort, T>(oldValue);
                    }
                    current = oldValue;
                }
            }

            if (sizeof(T) == 4)
            {
                return Unsafe.BitCast<int, T>(
                    Or(
                        ref Unsafe.As<T, int>(ref location1),
                        Unsafe.BitCast<T, int>(value)));
            }

            return Unsafe.BitCast<long, T>(
                Or(
                    ref Unsafe.As<T, long>(ref location1),
                    Unsafe.BitCast<T, long>(value)));
        }
        #endregion

        #region MemoryBarrier
        /// <summary>
        /// Synchronizes memory access as follows:
        /// The processor that executes the current thread cannot reorder instructions in such a way that memory accesses before
        /// the call to <see cref="MemoryBarrier"/> execute after memory accesses that follow the call to <see cref="MemoryBarrier"/>.
        /// </summary>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void MemoryBarrier() => MemoryBarrier(); // Must expand intrinsic

        /// <summary>
        /// Synchronizes memory access as follows: the processor executing the current thread cannot reorder
        /// instructions in such a way that memory reads before the call to <see cref="ReadMemoryBarrier"/>
        /// execute after memory accesses that follow the call to <see cref="ReadMemoryBarrier"/>.
        /// </summary>
        [Intrinsic]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void ReadMemoryBarrier()
        {
            ReadMemoryBarrier(); // Must expand intrinsic
        }

        /// <summary>Provides a process-wide memory barrier that ensures that reads and writes from any CPU cannot move across the barrier.</summary>
        /// <remarks>The runtime has no way to interrupt other processors, so this is a full barrier on the current one.</remarks>
        public static void MemoryBarrierProcessWide() => MemoryBarrier();
        #endregion
    }



    public sealed class AsyncLocal<T> : IAsyncLocal
    {
        private readonly Action<AsyncLocalValueChangedArgs<T>> _valueChangedHandler;

        public T Value
        {
            [return: MaybeNull]
            get
            {
                object localValue = ExecutionContext.GetLocalValue(this);
                if (typeof(T).IsValueType && localValue is null)
                {
                    return default(T);
                }
                return (T)localValue;
            }
            set
            {
                ExecutionContext.SetLocalValue(this, value, _valueChangedHandler is not null);
            }
        }

        public AsyncLocal()
        {
        }

        public AsyncLocal(Action<AsyncLocalValueChangedArgs<T>>? valueChangedHandler)
        {
            _valueChangedHandler = valueChangedHandler;
        }

        void IAsyncLocal.OnValueChanged(object previousValueObj, object currentValueObj, bool contextChanged)
        {
            T previousValue = ((previousValueObj is null) ? default(T) : ((T)previousValueObj));
            T currentValue = ((currentValueObj is null) ? default(T) : ((T)currentValueObj));
            _valueChangedHandler(new AsyncLocalValueChangedArgs<T>(previousValue, currentValue, contextChanged));
        }
    }


    public readonly struct AsyncLocalValueChangedArgs<T>
    {
        public T? PreviousValue { get; }

        public T? CurrentValue { get; }

        public bool ThreadContextChanged { get; }

        internal AsyncLocalValueChangedArgs(T previousValue, T currentValue, bool contextChanged)
        {
            PreviousValue = previousValue;
            CurrentValue = currentValue;
            ThreadContextChanged = contextChanged;
        }
    }


    internal static class AsyncLocalValueMap
    {
        private sealed class EmptyAsyncLocalValueMap : IAsyncLocalValueMap
        {
            public IAsyncLocalValueMap Set(IAsyncLocal key, object value, bool treatNullValueAsNonexistent)
            {
                if (value is null && treatNullValueAsNonexistent)
                {
                    return this;
                }
                return new OneElementAsyncLocalValueMap(KeyValuePair.Create(key, value));
            }

            public bool TryGetValue(IAsyncLocal key, out object value)
            {
                value = null;
                return false;
            }
        }

        private sealed class OneElementAsyncLocalValueMap : IAsyncLocalValueMap
        {
            private readonly KeyValuePair<IAsyncLocal, object> _item0;

            public OneElementAsyncLocalValueMap(KeyValuePair<IAsyncLocal, object> item0)
            {
                _item0 = item0;
            }

            public IAsyncLocalValueMap Set(IAsyncLocal key, object value, bool treatNullValueAsNonexistent)
            {
                if (value is not null || !treatNullValueAsNonexistent)
                {
                    KeyValuePair<IAsyncLocal, object> keyValuePair = KeyValuePair.Create(key, value);
                    if (key != _item0.Key)
                    {
                        return new TwoElementAsyncLocalValueMap(_item0, keyValuePair);
                    }
                    return new OneElementAsyncLocalValueMap(keyValuePair);
                }
                if (key != _item0.Key)
                {
                    return this;
                }
                return Empty;
            }

            public bool TryGetValue(IAsyncLocal key, out object value)
            {
                if (key == _item0.Key)
                {
                    value = _item0.Value;
                    return true;
                }
                value = null;
                return false;
            }
        }

        private sealed class TwoElementAsyncLocalValueMap : IAsyncLocalValueMap
        {
            private readonly KeyValuePair<IAsyncLocal, object> _item0;

            private readonly KeyValuePair<IAsyncLocal, object> _item1;

            public TwoElementAsyncLocalValueMap(KeyValuePair<IAsyncLocal, object> item0, KeyValuePair<IAsyncLocal, object> item1)
            {
                _item0 = item0;
                _item1 = item1;
            }

            public IAsyncLocalValueMap Set(IAsyncLocal key, object value, bool treatNullValueAsNonexistent)
            {
                if (value is not null || !treatNullValueAsNonexistent)
                {
                    KeyValuePair<IAsyncLocal, object> keyValuePair = KeyValuePair.Create(key, value);
                    if (key != _item0.Key)
                    {
                        if (key != _item1.Key)
                        {
                            return new ThreeElementAsyncLocalValueMap(_item0, _item1, keyValuePair);
                        }
                        return new TwoElementAsyncLocalValueMap(_item0, keyValuePair);
                    }
                    return new TwoElementAsyncLocalValueMap(keyValuePair, _item1);
                }
                if (key != _item0.Key)
                {
                    if (key != _item1.Key)
                    {
                        return this;
                    }
                    return new OneElementAsyncLocalValueMap(_item0);
                }
                return new OneElementAsyncLocalValueMap(_item1);
            }

            public bool TryGetValue(IAsyncLocal key, out object value)
            {
                if (key == _item0.Key)
                {
                    value = _item0.Value;
                    return true;
                }
                if (key == _item1.Key)
                {
                    value = _item1.Value;
                    return true;
                }
                value = null;
                return false;
            }
        }

        private sealed class ThreeElementAsyncLocalValueMap : IAsyncLocalValueMap
        {
            private readonly KeyValuePair<IAsyncLocal, object> _item0;

            private readonly KeyValuePair<IAsyncLocal, object> _item1;

            private readonly KeyValuePair<IAsyncLocal, object> _item2;

            public ThreeElementAsyncLocalValueMap(KeyValuePair<IAsyncLocal, object> item0, KeyValuePair<IAsyncLocal, object> item1, KeyValuePair<IAsyncLocal, object> item2)
            {
                _item0 = item0;
                _item1 = item1;
                _item2 = item2;
            }

            public IAsyncLocalValueMap Set(IAsyncLocal key, object value, bool treatNullValueAsNonexistent)
            {
                if (value is not null || !treatNullValueAsNonexistent)
                {
                    KeyValuePair<IAsyncLocal, object> keyValuePair = KeyValuePair.Create(key, value);
                    if (key != _item0.Key)
                    {
                        if (key != _item1.Key)
                        {
                            if (key != _item2.Key)
                            {
                                return new FourElementAsyncLocalValueMap(_item0, _item1, _item2, keyValuePair);
                            }
                            return new ThreeElementAsyncLocalValueMap(_item0, _item1, keyValuePair);
                        }
                        return new ThreeElementAsyncLocalValueMap(_item0, keyValuePair, _item2);
                    }
                    return new ThreeElementAsyncLocalValueMap(keyValuePair, _item1, _item2);
                }
                if (key != _item0.Key)
                {
                    if (key != _item1.Key)
                    {
                        if (key != _item2.Key)
                        {
                            return this;
                        }
                        return new TwoElementAsyncLocalValueMap(_item0, _item1);
                    }
                    return new TwoElementAsyncLocalValueMap(_item0, _item2);
                }
                return new TwoElementAsyncLocalValueMap(_item1, _item2);
            }

            public bool TryGetValue(IAsyncLocal key, out object value)
            {
                if (key == _item0.Key)
                {
                    value = _item0.Value;
                    return true;
                }
                if (key == _item1.Key)
                {
                    value = _item1.Value;
                    return true;
                }
                if (key == _item2.Key)
                {
                    value = _item2.Value;
                    return true;
                }
                value = null;
                return false;
            }
        }

        private sealed class FourElementAsyncLocalValueMap : IAsyncLocalValueMap
        {
            private readonly KeyValuePair<IAsyncLocal, object> _item0;

            private readonly KeyValuePair<IAsyncLocal, object> _item1;

            private readonly KeyValuePair<IAsyncLocal, object> _item2;

            private readonly KeyValuePair<IAsyncLocal, object> _item3;

            public FourElementAsyncLocalValueMap(KeyValuePair<IAsyncLocal, object> item0, KeyValuePair<IAsyncLocal, object> item1, KeyValuePair<IAsyncLocal, object> item2, KeyValuePair<IAsyncLocal, object> item3)
            {
                _item0 = item0;
                _item1 = item1;
                _item2 = item2;
                _item3 = item3;
            }

            public IAsyncLocalValueMap Set(IAsyncLocal key, object value, bool treatNullValueAsNonexistent)
            {
                if (value is not null || !treatNullValueAsNonexistent)
                {
                    KeyValuePair<IAsyncLocal, object> keyValuePair = KeyValuePair.Create(key, value);
                    if (key != _item0.Key)
                    {
                        if (key != _item1.Key)
                        {
                            if (key != _item2.Key)
                            {
                                if (key != _item3.Key)
                                {
                                    return new MultiElementAsyncLocalValueMap(new KeyValuePair<IAsyncLocal, object>[5] { _item0, _item1, _item2, _item3, keyValuePair });
                                }
                                return new FourElementAsyncLocalValueMap(_item0, _item1, _item2, keyValuePair);
                            }
                            return new FourElementAsyncLocalValueMap(_item0, _item1, keyValuePair, _item3);
                        }
                        return new FourElementAsyncLocalValueMap(_item0, keyValuePair, _item2, _item3);
                    }
                    return new FourElementAsyncLocalValueMap(keyValuePair, _item1, _item2, _item3);
                }
                if (key != _item0.Key)
                {
                    if (key != _item1.Key)
                    {
                        if (key != _item2.Key)
                        {
                            if (key != _item3.Key)
                            {
                                return this;
                            }
                            return new ThreeElementAsyncLocalValueMap(_item0, _item1, _item2);
                        }
                        return new ThreeElementAsyncLocalValueMap(_item0, _item1, _item3);
                    }
                    return new ThreeElementAsyncLocalValueMap(_item0, _item2, _item3);
                }
                return new ThreeElementAsyncLocalValueMap(_item1, _item2, _item3);
            }

            public bool TryGetValue(IAsyncLocal key, out object value)
            {
                if (key == _item0.Key)
                {
                    value = _item0.Value;
                    return true;
                }
                if (key == _item1.Key)
                {
                    value = _item1.Value;
                    return true;
                }
                if (key == _item2.Key)
                {
                    value = _item2.Value;
                    return true;
                }
                if (key == _item3.Key)
                {
                    value = _item3.Value;
                    return true;
                }
                value = null;
                return false;
            }
        }

        private sealed class MultiElementAsyncLocalValueMap : IAsyncLocalValueMap
        {
            internal const int MaxMultiElements = 16;

            private readonly KeyValuePair<IAsyncLocal, object>[] _keyValues;

            internal MultiElementAsyncLocalValueMap(KeyValuePair<IAsyncLocal, object>[] keyValues)
            {
                _keyValues = keyValues;
            }

            public IAsyncLocalValueMap Set(IAsyncLocal key, object value, bool treatNullValueAsNonexistent)
            {
                for (int i = 0; i < _keyValues.Length; i++)
                {
                    if (key == _keyValues[i].Key)
                    {
                        if (value is not null || !treatNullValueAsNonexistent)
                        {
                            KeyValuePair<IAsyncLocal, object>[] array = _keyValues.AsSpan().ToArray();
                            array[i] = KeyValuePair.Create(key, value);
                            return new MultiElementAsyncLocalValueMap(array);
                        }
                        if (_keyValues.Length == 5)
                        {
                            return i switch
                            {
                                0 => new FourElementAsyncLocalValueMap(_keyValues[1], _keyValues[2], _keyValues[3], _keyValues[4]),
                                1 => new FourElementAsyncLocalValueMap(_keyValues[0], _keyValues[2], _keyValues[3], _keyValues[4]),
                                2 => new FourElementAsyncLocalValueMap(_keyValues[0], _keyValues[1], _keyValues[3], _keyValues[4]),
                                3 => new FourElementAsyncLocalValueMap(_keyValues[0], _keyValues[1], _keyValues[2], _keyValues[4]),
                                _ => new FourElementAsyncLocalValueMap(_keyValues[0], _keyValues[1], _keyValues[2], _keyValues[3]),
                            };
                        }
                        KeyValuePair<IAsyncLocal, object>[] array2 = new KeyValuePair<IAsyncLocal, object>[_keyValues.Length - 1];
                        if (i != 0)
                        {
                            Array.Copy(_keyValues, array2, i);
                        }
                        if (i != _keyValues.Length - 1)
                        {
                            Array.Copy(_keyValues, i + 1, array2, i, _keyValues.Length - i - 1);
                        }
                        return new MultiElementAsyncLocalValueMap(array2);
                    }
                }
                if ((value is null) & treatNullValueAsNonexistent)
                {
                    return this;
                }
                if (_keyValues.Length < 16)
                {
                    KeyValuePair<IAsyncLocal, object>[] array3 = new KeyValuePair<IAsyncLocal, object>[_keyValues.Length + 1];
                    Array.Copy(_keyValues, array3, _keyValues.Length);
                    array3[_keyValues.Length] = KeyValuePair.Create(key, value);
                    return new MultiElementAsyncLocalValueMap(array3);
                }
                ManyElementAsyncLocalValueMap manyElementAsyncLocalValueMap = new ManyElementAsyncLocalValueMap(17);
                KeyValuePair<IAsyncLocal, object>[] keyValues = _keyValues;
                for (int j = 0; j < keyValues.Length; j++)
                {
                    KeyValuePair<IAsyncLocal, object> keyValuePair = keyValues[j];
                    manyElementAsyncLocalValueMap[keyValuePair.Key] = keyValuePair.Value;
                }
                manyElementAsyncLocalValueMap[key] = value;
                return manyElementAsyncLocalValueMap;
            }

            public bool TryGetValue(IAsyncLocal key, out object value)
            {
                KeyValuePair<IAsyncLocal, object>[] keyValues = _keyValues;
                for (int i = 0; i < keyValues.Length; i++)
                {
                    KeyValuePair<IAsyncLocal, object> keyValuePair = keyValues[i];
                    if (key == keyValuePair.Key)
                    {
                        value = keyValuePair.Value;
                        return true;
                    }
                }
                value = null;
                return false;
            }
        }

        private sealed class ManyElementAsyncLocalValueMap : Dictionary<IAsyncLocal, object>, IAsyncLocalValueMap
        {
            public ManyElementAsyncLocalValueMap(int capacity)
                : base(capacity)
            {
            }

            public IAsyncLocalValueMap Set(IAsyncLocal key, object value, bool treatNullValueAsNonexistent)
            {
                int count = base.Count;
                bool flag = ContainsKey(key);
                if (value is not null || !treatNullValueAsNonexistent)
                {
                    ManyElementAsyncLocalValueMap manyElementAsyncLocalValueMap = new ManyElementAsyncLocalValueMap(count + ((!flag) ? 1 : 0));
                    using (Enumerator enumerator = GetEnumerator())
                    {
                        while (enumerator.MoveNext())
                        {
                            KeyValuePair<IAsyncLocal, object> current = enumerator.Current;
                            manyElementAsyncLocalValueMap[current.Key] = current.Value;
                        }
                    }
                    manyElementAsyncLocalValueMap[key] = value;
                    return manyElementAsyncLocalValueMap;
                }
                if (flag)
                {
                    if (count == 17)
                    {
                        KeyValuePair<IAsyncLocal, object>[] array = new KeyValuePair<IAsyncLocal, object>[16];
                        int num = 0;
                        using (Enumerator enumerator = GetEnumerator())
                        {
                            while (enumerator.MoveNext())
                            {
                                KeyValuePair<IAsyncLocal, object> current2 = enumerator.Current;
                                if (key != current2.Key)
                                {
                                    array[num++] = current2;
                                }
                            }
                        }
                        return new MultiElementAsyncLocalValueMap(array);
                    }
                    ManyElementAsyncLocalValueMap manyElementAsyncLocalValueMap2 = new ManyElementAsyncLocalValueMap(count - 1);
                    using Enumerator enumerator = GetEnumerator();
                    while (enumerator.MoveNext())
                    {
                        KeyValuePair<IAsyncLocal, object> current3 = enumerator.Current;
                        if (key != current3.Key)
                        {
                            manyElementAsyncLocalValueMap2[current3.Key] = current3.Value;
                        }
                    }
                    return manyElementAsyncLocalValueMap2;
                }
                return this;
            }
        }

        public static IAsyncLocalValueMap Empty { get; } = new EmptyAsyncLocalValueMap();

        public static bool IsEmpty(IAsyncLocalValueMap asyncLocalValueMap)
        {
            return asyncLocalValueMap == Empty;
        }

        public static IAsyncLocalValueMap Create(IAsyncLocal key, object value, bool treatNullValueAsNonexistent)
        {
            if (value is null && treatNullValueAsNonexistent)
            {
                return Empty;
            }
            return new OneElementAsyncLocalValueMap(KeyValuePair.Create(key, value));
        }
    }


    internal interface IAsyncLocal
    {
        void OnValueChanged(object previousValue, object currentValue, bool contextChanged);
    }


    internal interface IAsyncLocalValueMap
    {
        bool TryGetValue(IAsyncLocal key, out object value);

        IAsyncLocalValueMap Set(IAsyncLocal key, object value, bool treatNullValueAsNonexistent);
    }


    internal static class Backoff
    {
        private const uint MaxExponentialBackoffBits = 7u;

        internal unsafe static int Exponential(uint attempt)
        {
            if (attempt != 0)
            {
                attempt = Math.Min(attempt, 7u);
                int num = (((int)(&attempt) * -1640531527) | int.MinValue) >>> (int)(byte)(32 - attempt);
                Thread.SpinWait(num);
                return num;
            }
            return 0;
        }
    }


    /// <summary>
    /// Propagates notification that operations should be canceled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <see cref="CancellationToken"/> may be created directly in an unchangeable canceled or non-canceled state
    /// using the CancellationToken's constructors. However, to have a CancellationToken that can change
    /// from a non-canceled to a canceled state,
    /// <see cref="CancellationTokenSource">CancellationTokenSource</see> must be used.
    /// CancellationTokenSource exposes the associated CancellationToken that may be canceled by the source through its
    /// <see cref="CancellationTokenSource.Token">Token</see> property.
    /// </para>
    /// <para>
    /// Once canceled, a token may not transition to a non-canceled state, and a token whose
    /// <see cref="CanBeCanceled"/> is false will never change to one that can be canceled.
    /// </para>
    /// <para>
    /// All members of this struct are thread-safe and may be used concurrently from multiple threads.
    /// </para>
    /// </remarks>
    [DebuggerDisplay("IsCancellationRequested = {IsCancellationRequested}")]
    public readonly struct CancellationToken : IEquatable<CancellationToken>
    {
        // The backing TokenSource.
        // if null, it implicitly represents the same thing as new CancellationToken(false).
        // When required, it will be instantiated to reflect this.
        private readonly CancellationTokenSource? _source;
        // !! warning. If more fields are added, the assumptions in CreateLinkedToken may no longer be valid

        /// <summary>
        /// Returns an empty CancellationToken value.
        /// </summary>
        /// <remarks>
        /// The <see cref="CancellationToken"/> value returned by this property will be non-cancelable by default.
        /// </remarks>
        public static CancellationToken None => default;

        /// <summary>
        /// Gets whether cancellation has been requested for this token.
        /// </summary>
        /// <value>Whether cancellation has been requested for this token.</value>
        /// <remarks>
        /// <para>
        /// This property indicates whether cancellation has been requested for this token,
        /// either through the token initially being constructed in a canceled state, or through
        /// calling <see cref="CancellationTokenSource.Cancel()">Cancel</see>
        /// on the token's associated <see cref="CancellationTokenSource"/>.
        /// </para>
        /// <para>
        /// If this property is true, it only guarantees that cancellation has been requested.
        /// It does not guarantee that every registered handler
        /// has finished executing, nor that cancellation requests have finished propagating
        /// to all registered handlers.  Additional synchronization may be required,
        /// particularly in situations where related objects are being canceled concurrently.
        /// </para>
        /// </remarks>
        public bool IsCancellationRequested => _source != null && _source.IsCancellationRequested;

        /// <summary>
        /// Gets whether this token is capable of being in the canceled state.
        /// </summary>
        /// <remarks>
        /// If CanBeCanceled returns false, it is guaranteed that the token will never transition
        /// into a canceled state, meaning that <see cref="IsCancellationRequested"/> will never
        /// return true.
        /// </remarks>
        public bool CanBeCanceled => _source != null;

        /// <summary>
        /// Gets a <see cref="Threading.WaitHandle"/> that is signaled when the token is canceled.</summary>
        /// <remarks>
        /// Accessing this property causes a <see cref="Threading.WaitHandle">WaitHandle</see>
        /// to be instantiated.  It is preferable to only use this property when necessary, and to then
        /// dispose the associated <see cref="CancellationTokenSource"/> instance at the earliest opportunity (disposing
        /// the source will dispose of this allocated handle).  The handle should not be closed or disposed directly.
        /// </remarks>
        /// <exception cref="ObjectDisposedException">The associated <see
        /// cref="CancellationTokenSource">CancellationTokenSource</see> has been disposed.</exception>
        public WaitHandle WaitHandle => (_source ?? CancellationTokenSource.s_neverCanceledSource).WaitHandle;

        // public CancellationToken()
        // this constructor is implicit for structs
        //   -> this should behaves exactly as for new CancellationToken(false)

        /// <summary>
        /// Internal constructor only a CancellationTokenSource should create a CancellationToken
        /// </summary>
        internal CancellationToken(CancellationTokenSource? source) => _source = source;

        /// <summary>
        /// Initializes the <see cref="CancellationToken">CancellationToken</see>.
        /// </summary>
        /// <param name="canceled">
        /// The canceled state for the token.
        /// </param>
        /// <remarks>
        /// Tokens created with this constructor will remain in the canceled state specified
        /// by the <paramref name="canceled"/> parameter.  If <paramref name="canceled"/> is false,
        /// both <see cref="CanBeCanceled"/> and <see cref="IsCancellationRequested"/> will be false.
        /// If <paramref name="canceled"/> is true,
        /// both <see cref="CanBeCanceled"/> and <see cref="IsCancellationRequested"/> will be true.
        /// </remarks>
        public CancellationToken(bool canceled) : this(canceled ? CancellationTokenSource.s_canceledSource : null)
        {
        }

        /// <summary>
        /// Registers a delegate that will be called when this <see cref="CancellationToken">CancellationToken</see> is canceled.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If this token is already in the canceled state, the
        /// delegate will be run immediately and synchronously. Any exception the delegate generates will be
        /// propagated out of this method call.
        /// </para>
        /// <para>
        /// The current <see cref="ExecutionContext">ExecutionContext</see>, if one exists, will be captured
        /// along with the delegate and will be used when executing it.
        /// </para>
        /// </remarks>
        /// <param name="callback">The delegate to be executed when the <see cref="CancellationToken">CancellationToken</see> is canceled.</param>
        /// <returns>The <see cref="CancellationTokenRegistration"/> instance that can
        /// be used to unregister the callback.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="callback"/> is null.</exception>
        public CancellationTokenRegistration Register(Action callback) => Register(callback, useSynchronizationContext: false);

        /// <summary>
        /// Registers a delegate that will be called when this
        /// <see cref="CancellationToken">CancellationToken</see> is canceled.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If this token is already in the canceled state, the
        /// delegate will be run immediately and synchronously. Any exception the delegate generates will be
        /// propagated out of this method call.
        /// </para>
        /// <para>
        /// The current <see cref="ExecutionContext">ExecutionContext</see>, if one exists, will be captured
        /// along with the delegate and will be used when executing it.
        /// </para>
        /// </remarks>
        /// <param name="callback">The delegate to be executed when the <see cref="CancellationToken">CancellationToken</see> is canceled.</param>
        /// <param name="useSynchronizationContext">A Boolean value that indicates whether to capture
        /// the current <see cref="SynchronizationContext">SynchronizationContext</see> and use it
        /// when invoking the <paramref name="callback"/>.</param>
        /// <returns>The <see cref="CancellationTokenRegistration"/> instance that can
        /// be used to unregister the callback.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="callback"/> is null.</exception>
        public CancellationTokenRegistration Register(Action callback, bool useSynchronizationContext)
        {
            ArgumentNullException.ThrowIfNull(callback);
            return Register(
                (Action<object?>)(static obj => ((Action)obj!)()),
                callback,
                useSynchronizationContext,
                useExecutionContext: true);
        }

        /// <summary>
        /// Registers a delegate that will be called when this
        /// <see cref="CancellationToken">CancellationToken</see> is canceled.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If this token is already in the canceled state, the
        /// delegate will be run immediately and synchronously. Any exception the delegate generates will be
        /// propagated out of this method call.
        /// </para>
        /// <para>
        /// The current <see cref="ExecutionContext">ExecutionContext</see>, if one exists, will be captured
        /// along with the delegate and will be used when executing it.
        /// </para>
        /// </remarks>
        /// <param name="callback">The delegate to be executed when the <see cref="CancellationToken">CancellationToken</see> is canceled.</param>
        /// <param name="state">The state to pass to the <paramref name="callback"/> when the delegate is invoked.  This may be null.</param>
        /// <returns>The <see cref="CancellationTokenRegistration"/> instance that can
        /// be used to unregister the callback.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="callback"/> is null.</exception>
        public CancellationTokenRegistration Register(Action<object?> callback, object? state) =>
            Register(callback, state, useSynchronizationContext: false, useExecutionContext: true);

        /// <summary>Registers a delegate that will be called when this <see cref="CancellationToken">CancellationToken</see> is canceled.</summary>
        /// <remarks>
        /// If this token is already in the canceled state, the delegate will be run immediately and synchronously. Any exception the delegate
        /// generates will be propagated out of this method call. The current <see cref="ExecutionContext">ExecutionContext</see>, if one exists,
        /// will be captured along with the delegate and will be used when executing it. The current <see cref="SynchronizationContext"/> is not captured.
        /// </remarks>
        /// <param name="callback">The delegate to be executed when the <see cref="CancellationToken">CancellationToken</see> is canceled.</param>
        /// <param name="state">The state to pass to the <paramref name="callback"/> when the delegate is invoked.  This may be null.</param>
        /// <returns>The <see cref="CancellationTokenRegistration"/> instance that can be used to unregister the callback.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="callback"/> is null.</exception>
        public CancellationTokenRegistration Register(Action<object?, CancellationToken> callback, object? state) =>
            Register(callback, state, useSynchronizationContext: false, useExecutionContext: true);

        /// <summary>
        /// Registers a delegate that will be called when this
        /// <see cref="CancellationToken">CancellationToken</see> is canceled.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If this token is already in the canceled state, the
        /// delegate will be run immediately and synchronously. Any exception the delegate generates will be
        /// propagated out of this method call.
        /// </para>
        /// <para>
        /// The current <see cref="ExecutionContext">ExecutionContext</see>, if one exists,
        /// will be captured along with the delegate and will be used when executing it.
        /// </para>
        /// </remarks>
        /// <param name="callback">The delegate to be executed when the <see cref="CancellationToken">CancellationToken</see> is canceled.</param>
        /// <param name="state">The state to pass to the <paramref name="callback"/> when the delegate is invoked.  This may be null.</param>
        /// <param name="useSynchronizationContext">A Boolean value that indicates whether to capture
        /// the current <see cref="SynchronizationContext">SynchronizationContext</see> and use it
        /// when invoking the <paramref name="callback"/>.</param>
        /// <returns>The <see cref="CancellationTokenRegistration"/> instance that can
        /// be used to unregister the callback.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="callback"/> is null.</exception>
        /// <exception cref="ObjectDisposedException">The associated <see
        /// cref="CancellationTokenSource">CancellationTokenSource</see> has been disposed.</exception>
        public CancellationTokenRegistration Register(Action<object?> callback, object? state, bool useSynchronizationContext) =>
            Register(callback, state, useSynchronizationContext, useExecutionContext: true);

        /// <summary>
        /// Registers a delegate that will be called when this
        /// <see cref="CancellationToken">CancellationToken</see> is canceled.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If this token is already in the canceled state, the delegate will be run immediately and synchronously.
        /// Any exception the delegate generates will be propagated out of this method call.
        /// </para>
        /// <para>
        /// <see cref="ExecutionContext">ExecutionContext</see> is not captured nor flowed
        /// to the callback's invocation.
        /// </para>
        /// </remarks>
        /// <param name="callback">The delegate to be executed when the <see cref="CancellationToken">CancellationToken</see> is canceled.</param>
        /// <param name="state">The state to pass to the <paramref name="callback"/> when the delegate is invoked.  This may be null.</param>
        /// <returns>The <see cref="CancellationTokenRegistration"/> instance that can
        /// be used to unregister the callback.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="callback"/> is null.</exception>
        public CancellationTokenRegistration UnsafeRegister(Action<object?> callback, object? state) =>
            Register(callback, state, useSynchronizationContext: false, useExecutionContext: false);

        /// <summary>Registers a delegate that will be called when this <see cref="CancellationToken">CancellationToken</see> is canceled.</summary>
        /// <remarks>
        /// If this token is already in the canceled state, the delegate will be run immediately and synchronously. Any exception the delegate
        /// generates will be propagated out of this method call. <see cref="ExecutionContext"/> is not captured nor flowed to the callback's invocation.
        /// </remarks>
        /// <param name="callback">The delegate to be executed when the <see cref="CancellationToken">CancellationToken</see> is canceled.</param>
        /// <param name="state">The state to pass to the <paramref name="callback"/> when the delegate is invoked.  This may be null.</param>
        /// <returns>The <see cref="CancellationTokenRegistration"/> instance that can be used to unregister the callback.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="callback"/> is null.</exception>
        public CancellationTokenRegistration UnsafeRegister(Action<object?, CancellationToken> callback, object? state) =>
            Register(callback, state, useSynchronizationContext: false, useExecutionContext: false);

        /// <summary>
        /// Registers a delegate that will be called when this
        /// <see cref="CancellationToken">CancellationToken</see> is canceled.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If this token is already in the canceled state, the
        /// delegate will be run immediately and synchronously. Any exception the delegate generates will be
        /// propagated out of this method call.
        /// </para>
        /// </remarks>
        /// <param name="callback">The delegate to be executed when the <see cref="CancellationToken">CancellationToken</see> is canceled.</param>
        /// <param name="state">The state to pass to the <paramref name="callback"/> when the delegate is invoked.  This may be null.</param>
        /// <param name="useSynchronizationContext">A Boolean value that indicates whether to capture
        /// the current <see cref="SynchronizationContext">SynchronizationContext</see> and use it
        /// when invoking the <paramref name="callback"/>.</param>
        /// <param name="useExecutionContext">true to capture the current ExecutionContext; otherwise, false.</param>
        /// <returns>The <see cref="CancellationTokenRegistration"/> instance that can
        /// be used to unregister the callback.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="callback"/> is null.</exception>
        /// <exception cref="ObjectDisposedException">The associated <see
        /// cref="CancellationTokenSource">CancellationTokenSource</see> has been disposed.</exception>
        private CancellationTokenRegistration Register(Delegate callback, object? state, bool useSynchronizationContext, bool useExecutionContext)
        {
            ArgumentNullException.ThrowIfNull(callback);

            CancellationTokenSource? source = _source;
            return source != null ?
                source.Register(callback, state, useSynchronizationContext ? SynchronizationContext.Current : null, useExecutionContext ? ExecutionContext.Capture() : null) :
                default; // Nothing to do for tokens than can never reach the canceled state. Give back a dummy registration.
        }

        /// <summary>
        /// Determines whether the current <see cref="CancellationToken">CancellationToken</see> instance is equal to the
        /// specified token.
        /// </summary>
        /// <param name="other">The other <see cref="CancellationToken">CancellationToken</see> to which to compare this
        /// instance.</param>
        /// <returns>True if the instances are equal; otherwise, false. Two tokens are equal if they are associated
        /// with the same <see cref="CancellationTokenSource">CancellationTokenSource</see> or if they were both constructed
        /// from public CancellationToken constructors and their <see cref="IsCancellationRequested"/> values are equal.</returns>
        public bool Equals(CancellationToken other) => _source == other._source;

        /// <summary>
        /// Determines whether the current <see cref="CancellationToken">CancellationToken</see> instance is equal to the
        /// specified <see cref="object"/>.
        /// </summary>
        /// <param name="other">The other object to which to compare this instance.</param>
        /// <returns>True if <paramref name="other"/> is a <see cref="CancellationToken">CancellationToken</see>
        /// and if the two instances are equal; otherwise, false. Two tokens are equal if they are associated
        /// with the same <see cref="CancellationTokenSource">CancellationTokenSource</see> or if they were both constructed
        /// from public CancellationToken constructors and their <see cref="IsCancellationRequested"/> values are equal.</returns>
        /// <exception cref="ObjectDisposedException">An associated <see
        /// cref="CancellationTokenSource">CancellationTokenSource</see> has been disposed.</exception>
        public override bool Equals([NotNullWhen(true)] object? other) => other is CancellationToken && Equals((CancellationToken)other);

        /// <summary>
        /// Serves as a hash function for a <see cref="CancellationToken">CancellationToken</see>.
        /// </summary>
        /// <returns>A hash code for the current <see cref="CancellationToken">CancellationToken</see> instance.</returns>
        public override int GetHashCode() => (_source ?? CancellationTokenSource.s_neverCanceledSource).GetHashCode();

        /// <summary>
        /// Determines whether two <see cref="CancellationToken">CancellationToken</see> instances are equal.
        /// </summary>
        /// <param name="left">The first instance.</param>
        /// <param name="right">The second instance.</param>
        /// <returns>True if the instances are equal; otherwise, false.</returns>
        /// <exception cref="ObjectDisposedException">An associated <see
        /// cref="CancellationTokenSource">CancellationTokenSource</see> has been disposed.</exception>
        public static bool operator ==(CancellationToken left, CancellationToken right) => left.Equals(right);

        /// <summary>
        /// Determines whether two <see cref="CancellationToken">CancellationToken</see> instances are not equal.
        /// </summary>
        /// <param name="left">The first instance.</param>
        /// <param name="right">The second instance.</param>
        /// <returns>True if the instances are not equal; otherwise, false.</returns>
        /// <exception cref="ObjectDisposedException">An associated <see
        /// cref="CancellationTokenSource">CancellationTokenSource</see> has been disposed.</exception>
        public static bool operator !=(CancellationToken left, CancellationToken right) => !left.Equals(right);

        /// <summary>
        /// Throws a <see cref="OperationCanceledException">OperationCanceledException</see> if
        /// this token has had cancellation requested.
        /// </summary>
        /// <remarks>
        /// This method provides functionality equivalent to:
        /// <code>
        /// if (token.IsCancellationRequested)
        ///    throw new OperationCanceledException(token);
        /// </code>
        /// </remarks>
        /// <exception cref="OperationCanceledException">The token has had cancellation requested.</exception>
        public void ThrowIfCancellationRequested()
        {
            if (IsCancellationRequested)
                ThrowOperationCanceledException();
        }

        // Throws an OCE; separated out to enable better inlining of ThrowIfCancellationRequested
        [DoesNotReturn]
        private void ThrowOperationCanceledException() =>
            throw new OperationCanceledException(SR.OperationCanceled, this);
    }


    /// <summary>
    /// Represents a callback delegate that has been registered with a <see cref="CancellationToken">CancellationToken</see>.
    /// </summary>
    /// <remarks>
    /// To unregister a callback, dispose the corresponding Registration instance.
    /// </remarks>
    public readonly struct CancellationTokenRegistration : IEquatable<CancellationTokenRegistration>, IDisposable, IAsyncDisposable
    {
        private readonly long _id;
        private readonly CancellationTokenSource.CallbackNode _node;

        internal CancellationTokenRegistration(long id, CancellationTokenSource.CallbackNode node)
        {
            _id = id;
            _node = node;
        }

        /// <summary>
        /// Disposes of the registration and unregisters the target callback from the associated
        /// <see cref="CancellationToken">CancellationToken</see>.
        /// If the target callback is currently executing, this method will wait until it completes, except
        /// in the degenerate cases where a callback method unregisters itself.
        /// </summary>
        public void Dispose()
        {
            if (_node is CancellationTokenSource.CallbackNode node && !node.Registrations.Unregister(_id, node))
            {
                WaitForCallbackIfNecessary(_id, node);

                static void WaitForCallbackIfNecessary(long id, CancellationTokenSource.CallbackNode node)
                {
                    // We're a valid registration but we were unable to unregister, which means the callback wasn't in the list,
                    // which means either it already executed or it's currently executing. We guarantee that we will not return
                    // if the callback is being executed (assuming we are not currently called by the callback itself)
                    // We achieve this by the following rules:
                    //    1. If we are called in the context of an executing callback, no need to wait (determined by tracking callback-executor threadID)
                    //       - if the currently executing callback is this CTR, then waiting would deadlock. (We choose to return rather than deadlock)
                    //       - if not, then this CTR cannot be the one executing, hence no need to wait
                    //    2. If unregistration failed, and we are on a different thread, then the callback may be running under control of cts.Cancel()
                    //       => poll until cts.ExecutingCallback is not the one we are trying to unregister.
                    CancellationTokenSource source = node.Registrations.Source;
                    if (source.IsCancellationRequested && // Running callbacks has commenced.
                        !source.IsCancellationCompleted && // Running callbacks hasn't finished.
                        node.Registrations.ThreadIDExecutingCallbacks != Environment.CurrentManagedThreadId) // The executing thread ID is not this thread's ID.
                    {
                        // Callback execution is in progress, the executing thread is different from this thread and has taken the callback for execution
                        // so observe and wait until this target callback is no longer the executing callback.
                        node.Registrations.WaitForCallbackToComplete(id);
                    }
                }
            }
        }

        /// <summary>
        /// Disposes of the registration and unregisters the target callback from the associated
        /// <see cref="CancellationToken">CancellationToken</see>.
        /// The returned <see cref="ValueTask"/> will complete once the associated callback
        /// is unregistered without having executed or once it's finished executing, except
        /// in the degenerate case where the callback itself is unregistering itself.
        /// </summary>
        public ValueTask DisposeAsync()
        {
            return _node is CancellationTokenSource.CallbackNode node && !node.Registrations.Unregister(_id, node) ?
                WaitForCallbackIfNecessaryAsync(_id, node) :
                default;

            static ValueTask WaitForCallbackIfNecessaryAsync(long id, CancellationTokenSource.CallbackNode node)
            {
                // Same as WaitForCallbackIfNecessary, except returning a task that'll be completed when callbacks complete.

                CancellationTokenSource source = node.Registrations.Source;
                if (source.IsCancellationRequested && // Running callbacks has commenced.
                    !source.IsCancellationCompleted && // Running callbacks hasn't finished.
                    node.Registrations.ThreadIDExecutingCallbacks != Environment.CurrentManagedThreadId) // The executing thread ID is not this thread's ID.
                {
                    // Callback execution is in progress, the executing thread is different from this thread and has taken the callback for execution
                    // so get a task that'll complete when this target callback is no longer the executing callback.
                    return node.Registrations.WaitForCallbackToCompleteAsync(id);
                }

                // Callback is either already completed, won't execute, or the callback itself is calling this.
                return default;
            }
        }

        /// <summary>Gets the <see cref="CancellationToken"/> with which this registration is associated.</summary>
        /// <remarks>
        /// If the registration isn't associated with a token (such as for a registration returned from a call
        /// to <see cref="CancellationToken.Register"/> on a token that already had cancellation requested),
        /// this will return a default token.
        /// </remarks>
        public CancellationToken Token =>
            _node is CancellationTokenSource.CallbackNode node ?
                new CancellationToken(node.Registrations.Source) : // avoid CTS.Token, which throws after disposal
                default;

        /// <summary>
        /// Disposes of the registration and unregisters the target callback from the associated
        /// <see cref="CancellationToken">CancellationToken</see>.
        /// </summary>
        public bool Unregister() =>
            _node is CancellationTokenSource.CallbackNode node && node.Registrations.Unregister(_id, node);

        /// <summary>
        /// Determines whether two <see
        /// cref="CancellationTokenRegistration">CancellationTokenRegistration</see>
        /// instances are equal.
        /// </summary>
        /// <param name="left">The first instance.</param>
        /// <param name="right">The second instance.</param>
        /// <returns>True if the instances are equal; otherwise, false.</returns>
        public static bool operator ==(CancellationTokenRegistration left, CancellationTokenRegistration right) => left.Equals(right);

        /// <summary>
        /// Determines whether two <see cref="CancellationTokenRegistration">CancellationTokenRegistration</see> instances are not equal.
        /// </summary>
        /// <param name="left">The first instance.</param>
        /// <param name="right">The second instance.</param>
        /// <returns>True if the instances are not equal; otherwise, false.</returns>
        public static bool operator !=(CancellationTokenRegistration left, CancellationTokenRegistration right) => !left.Equals(right);

        /// <summary>
        /// Determines whether the current <see cref="CancellationTokenRegistration">CancellationTokenRegistration</see> instance is equal to the
        /// specified <see cref="object"/>.
        /// </summary>
        /// <param name="obj">The other object to which to compare this instance.</param>
        /// <returns>True, if both this and <paramref name="obj"/> are equal. False, otherwise.
        /// Two <see cref="CancellationTokenRegistration">CancellationTokenRegistration</see> instances are equal if
        /// they both refer to the output of a single call to the same Register method of a
        /// <see cref="CancellationToken">CancellationToken</see>.
        /// </returns>
        public override bool Equals([NotNullWhen(true)] object? obj) => obj is CancellationTokenRegistration other && Equals(other);

        /// <summary>
        /// Determines whether the current <see cref="CancellationToken">CancellationToken</see> instance is equal to the
        /// specified <see cref="object"/>.
        /// </summary>
        /// <param name="other">The other <see cref="CancellationTokenRegistration">CancellationTokenRegistration</see> to which to compare this instance.</param>
        /// <returns>True, if both this and <paramref name="other"/> are equal. False, otherwise.
        /// Two <see cref="CancellationTokenRegistration">CancellationTokenRegistration</see> instances are equal if
        /// they both refer to the output of a single call to the same Register method of a
        /// <see cref="CancellationToken">CancellationToken</see>.
        /// </returns>
        public bool Equals(CancellationTokenRegistration other) => _node == other._node && _id == other._id;

        /// <summary>
        /// Serves as a hash function for a <see cref="CancellationTokenRegistration">CancellationTokenRegistration.</see>.
        /// </summary>
        /// <returns>A hash code for the current <see cref="CancellationTokenRegistration">CancellationTokenRegistration</see> instance.</returns>
        public override int GetHashCode() => _node != null ? _node.GetHashCode() ^ _id.GetHashCode() : _id.GetHashCode();
    }


    /// <summary>Signals to a <see cref="CancellationToken"/> that it should be canceled.</summary>
    /// <remarks>
    /// <para>
    /// <see cref="CancellationTokenSource"/> is used to instantiate a <see cref="CancellationToken"/> (via
    /// the source's <see cref="Token">Token</see> property) that can be handed to operations that wish to be
    /// notified of cancellation or that can be used to register asynchronous operations for cancellation. That
    /// token may have cancellation requested by calling to the source's <see cref="Cancel()"/> method.
    /// </para>
    /// <para>
    /// All members of this class, except <see cref="Dispose()"/>, are thread-safe and may be used
    /// concurrently from multiple threads.
    /// </para>
    /// </remarks>
    [DebuggerDisplay("IsCancellationRequested = {IsCancellationRequested}")]
    public class CancellationTokenSource : IDisposable
    {
        /// <summary>A <see cref="CancellationTokenSource"/> that's already canceled.</summary>
        internal static readonly CancellationTokenSource s_canceledSource = new CancellationTokenSource() { _state = States.NotifyingCompleteState };
        /// <summary>A <see cref="CancellationTokenSource"/> that's never canceled.  This isn't enforced programmatically, only by usage.  Do not cancel!</summary>
        internal static readonly CancellationTokenSource s_neverCanceledSource = new CancellationTokenSource();

        /// <summary>Delegate used with <see cref="Timer"/> to trigger cancellation of a <see cref="CancellationTokenSource"/>.</summary>
        private static readonly TimerCallback s_timerCallback = TimerCallback;
        private static void TimerCallback(object? state) => // separated out into a named method to improve Timer diagnostics in a debugger
            ((CancellationTokenSource)state!).NotifyCancellation(throwOnFirstException: false); // skip ThrowIfDisposed() check in Cancel()

        /// <summary>The current state of the CancellationTokenSource.</summary>
        private volatile States _state;
        /// <summary>Whether this <see cref="CancellationTokenSource"/> has been disposed.</summary>
        private bool _disposed;
        /// <summary>ITimer used by CancelAfter and Timer-related ctors. Used instead of Timer to avoid extra allocations and because the rooted behavior is desired.</summary>
        private volatile ITimer? _timer;
        /// <summary><see cref="Threading.WaitHandle"/> lazily initialized and returned from <see cref="WaitHandle"/>.</summary>
        private volatile ManualResetEvent? _kernelEvent;
        /// <summary>Registration state for the source.</summary>
        /// <remarks>Lazily-initialized, also serving as the lock to protect its contained state.</remarks>
        private Registrations? _registrations;

        /// <summary>Legal values for <see cref="_state"/>.</summary>
        private enum States
        {
            NotCanceledState = 0, // default value of _state
            NotifyingState = 1,
            NotifyingCompleteState = 2,
        }

        /// <summary>Gets whether cancellation has been requested for this <see cref="CancellationTokenSource" />.</summary>
        /// <value>Whether cancellation has been requested for this <see cref="CancellationTokenSource" />.</value>
        /// <remarks>
        /// <para>
        /// This property indicates whether cancellation has been requested for this token source, such as
        /// due to a call to its <see cref="Cancel()"/> method.
        /// </para>
        /// <para>
        /// If this property returns true, it only guarantees that cancellation has been requested. It does not
        /// guarantee that every handler registered with the corresponding token has finished executing, nor
        /// that cancellation requests have finished propagating to all registered handlers. Additional
        /// synchronization may be required, particularly in situations where related objects are being
        /// canceled concurrently.
        /// </para>
        /// </remarks>
        public bool IsCancellationRequested => _state != States.NotCanceledState;

        /// <summary>A simple helper to determine whether cancellation has finished.</summary>
        internal bool IsCancellationCompleted => _state == States.NotifyingCompleteState;

        /// <summary>Gets the <see cref="CancellationToken"/> associated with this <see cref="CancellationTokenSource"/>.</summary>
        /// <value>The <see cref="CancellationToken"/> associated with this <see cref="CancellationTokenSource"/>.</value>
        /// <exception cref="ObjectDisposedException">The token source has been disposed.</exception>
        public CancellationToken Token
        {
            get
            {
                ThrowIfDisposed();
                return new CancellationToken(this);
            }
        }

        internal WaitHandle WaitHandle
        {
            get
            {
                ThrowIfDisposed();

                // Return the handle if it was already allocated.
                if (_kernelEvent != null)
                {
                    return _kernelEvent;
                }

                // Lazily-initialize the handle.
                var mre = new ManualResetEvent(false);
                if (Interlocked.CompareExchange(ref _kernelEvent, mre, null) != null)
                {
                    mre.Dispose();
                }

                // There is a race condition between checking IsCancellationRequested and setting the event.
                // However, at this point, the kernel object definitely exists and the cases are:
                //   1. if IsCancellationRequested = true, then we will call Set()
                //   2. if IsCancellationRequested = false, then NotifyCancellation will see that the event exists, and will call Set().
                if (IsCancellationRequested)
                {
                    _kernelEvent.Set();
                }

                return _kernelEvent;
            }
        }

        /// <summary>Initializes the <see cref="CancellationTokenSource"/>.</summary>
        public CancellationTokenSource() { }

        /// <summary>
        /// Constructs a <see cref="CancellationTokenSource"/> that will be canceled after a specified time span.
        /// </summary>
        /// <param name="delay">The time span to wait before canceling this <see cref="CancellationTokenSource"/></param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The <paramref name="delay"/> is less than -1 or greater than the maximum allowed timer duration.
        /// </exception>
        /// <remarks>
        /// <para>
        /// The countdown for the delay starts during the call to the constructor.  When the delay expires,
        /// the constructed <see cref="CancellationTokenSource"/> is canceled, if it has
        /// not been canceled already.
        /// </para>
        /// <para>
        /// Subsequent calls to CancelAfter will reset the delay for the constructed
        /// <see cref="CancellationTokenSource"/>, if it has not been
        /// canceled already.
        /// </para>
        /// </remarks>
        public CancellationTokenSource(TimeSpan delay) : this(delay, TimeProvider.System)
        {
        }

        /// <summary>Initializes a new instance of the <see cref="CancellationTokenSource"/> class that will be canceled after the specified <see cref="TimeSpan"/>.</summary>
        /// <param name="delay">The time interval to wait before canceling this <see cref="CancellationTokenSource"/>.</param>
        /// <param name="timeProvider">The <see cref="TimeProvider"/> with which to interpret the <paramref name="delay"/>.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="delay"/>'s <see cref="TimeSpan.TotalMilliseconds"/> is less than -1 or greater than <see cref="uint.MaxValue"/> - 1.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is null.</exception>
        /// <remarks>
        /// The countdown for the delay starts during the call to the constructor.  When the delay expires,
        /// the constructed <see cref="CancellationTokenSource"/> is canceled, if it has
        /// not been canceled already. Subsequent calls to CancelAfter will reset the delay for the constructed
        /// <see cref="CancellationTokenSource"/>, if it has not been canceled already.
        /// </remarks>
        public CancellationTokenSource(TimeSpan delay, TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(timeProvider);
            long totalMilliseconds = (long)delay.TotalMilliseconds;
            if (totalMilliseconds < -1 || totalMilliseconds > Timer.MaxSupportedTimeout)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.delay);
            }

            InitializeWithTimer(delay, timeProvider);
        }

        /// <summary>
        /// Constructs a <see cref="CancellationTokenSource"/> that will be canceled after a specified time span.
        /// </summary>
        /// <param name="millisecondsDelay">The time span to wait before canceling this <see cref="CancellationTokenSource"/></param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The exception that is thrown when <paramref name="millisecondsDelay"/> is less than -1.
        /// </exception>
        /// <remarks>
        /// <para>
        /// The countdown for the millisecondsDelay starts during the call to the constructor.  When the millisecondsDelay expires,
        /// the constructed <see cref="CancellationTokenSource"/> is canceled (if it has
        /// not been canceled already).
        /// </para>
        /// <para>
        /// Subsequent calls to CancelAfter will reset the millisecondsDelay for the constructed
        /// <see cref="CancellationTokenSource"/>, if it has not been
        /// canceled already.
        /// </para>
        /// </remarks>
        public CancellationTokenSource(int millisecondsDelay)
        {
            if (millisecondsDelay < -1)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.millisecondsDelay);
            }

            InitializeWithTimer(TimeSpan.FromMilliseconds(millisecondsDelay), TimeProvider.System);
        }

        /// <summary>
        /// Common initialization logic when constructing a CTS with a delay parameter.
        /// A zero delay will result in immediate cancellation.
        /// </summary>
        private void InitializeWithTimer(TimeSpan millisecondsDelay, TimeProvider timeProvider)
        {
            if (millisecondsDelay == TimeSpan.Zero)
            {
                _state = States.NotifyingCompleteState;
            }
            else
            {
                if (timeProvider == TimeProvider.System)
                {
                    _timer = new TimerQueueTimer(s_timerCallback, this, millisecondsDelay, Timeout.InfiniteTimeSpan, flowExecutionContext: false);
                }
                else
                {
                    using (ExecutionContext.SuppressFlow())
                    {
                        _timer = timeProvider.CreateTimer(s_timerCallback, this, millisecondsDelay, Timeout.InfiniteTimeSpan);
                    }
                }
                // The timer roots this CTS instance while it's scheduled.  That is by design, so
                // that code like:
                //     new CancellationTokenSource(timeout).Token.Register(() => ...);
                // will successfully invoke the delegate after the timeout.
            }
        }

        /// <summary>Communicates a request for cancellation.</summary>
        /// <remarks>
        /// <para>
        /// The associated <see cref="CancellationToken" /> will be notified of the cancellation
        /// and will transition to a state where <see cref="CancellationToken.IsCancellationRequested"/> returns true.
        /// Any callbacks or cancelable operations registered with the <see cref="CancellationToken"/>  will be executed.
        /// </para>
        /// <para>
        /// Cancelable operations and callbacks registered with the token should not throw exceptions.
        /// However, this overload of Cancel will aggregate any exceptions thrown into a <see cref="AggregateException"/>,
        /// such that one callback throwing an exception will not prevent other registered callbacks from being executed.
        /// </para>
        /// <para>
        /// The <see cref="ExecutionContext"/> that was captured when each callback was registered
        /// will be reestablished when the callback is invoked.
        /// </para>
        /// </remarks>
        /// <exception cref="AggregateException">An aggregate exception containing all the exceptions thrown
        /// by the registered callbacks on the associated <see cref="CancellationToken"/>.</exception>
        /// <exception cref="ObjectDisposedException">This <see cref="CancellationTokenSource"/> has been disposed.</exception>
        public void Cancel() => Cancel(false);

        /// <summary>Communicates a request for cancellation.</summary>
        /// <remarks>
        /// <para>
        /// The associated <see cref="CancellationToken" /> will be notified of the cancellation and will transition to a state where
        /// <see cref="CancellationToken.IsCancellationRequested"/> returns true. Any callbacks or cancelable operations registered
        /// with the <see cref="CancellationToken"/>  will be executed.
        /// </para>
        /// <para>
        /// Cancelable operations and callbacks registered with the token should not throw exceptions.
        /// If <paramref name="throwOnFirstException"/> is true, an exception will immediately propagate out of the
        /// call to Cancel, preventing the remaining callbacks and cancelable operations from being processed.
        /// If <paramref name="throwOnFirstException"/> is false, this overload will aggregate any
        /// exceptions thrown into a <see cref="AggregateException"/>,
        /// such that one callback throwing an exception will not prevent other registered callbacks from being executed.
        /// </para>
        /// <para>
        /// The <see cref="ExecutionContext"/> that was captured when each callback was registered
        /// will be reestablished when the callback is invoked.
        /// </para>
        /// </remarks>
        /// <param name="throwOnFirstException">Specifies whether exceptions should immediately propagate.</param>
        /// <exception cref="AggregateException">An aggregate exception containing all the exceptions thrown
        /// by the registered callbacks on the associated <see cref="CancellationToken"/>.</exception>
        /// <exception cref="ObjectDisposedException">This <see cref="CancellationTokenSource"/> has been disposed.</exception>
        public void Cancel(bool throwOnFirstException)
        {
            ThrowIfDisposed();
            NotifyCancellation(throwOnFirstException);
        }

        /// <summary>Communicates a request for cancellation asynchronously.</summary>
        /// <returns>
        /// A task that will complete after cancelable operations and callbacks registered with the associated
        /// <see cref="CancellationToken" /> have completed.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The associated <see cref="CancellationToken" /> will be notified of the cancellation
        /// and will synchronously transition to a state where <see cref="CancellationToken.IsCancellationRequested"/> returns true.
        /// Any callbacks or cancelable operations registered with the <see cref="CancellationToken"/>  will be executed asynchronously,
        /// with the returned <see cref="Task"/> representing their eventual completion.
        /// </para>
        /// <para>
        /// Callbacks registered with the token should not throw exceptions.
        /// However, any such exceptions that are thrown will be aggregated into an <see cref="AggregateException"/>,
        /// such that one callback throwing an exception will not prevent other registered callbacks from being executed.
        /// </para>
        /// <para>
        /// The <see cref="ExecutionContext"/> that was captured when each callback was registered
        /// will be reestablished when the callback is invoked.
        /// </para>
        /// </remarks>
        /// <exception cref="ObjectDisposedException">This <see cref="CancellationTokenSource"/> has been disposed.</exception>
        public Task CancelAsync()
        {
            if (_disposed)
            {
                return Task.FromException(new ObjectDisposedException(GetType().FullName, SR.CancellationTokenSource_Disposed));
            }

            // Ensure IsCancellationRequested is synchronously transitioned as part of the synchronous call to CancelAsync.
            if (TransitionToCancellationRequested())
            {

                Registrations? registrations = Volatile.Read(ref _registrations);
                if (registrations is not null)
                {
                    registrations.EnterLock();
                    bool callbacksExisted = registrations.Callbacks is not null;
                    registrations.ExitLock();

                    if (callbacksExisted)
                    {
                        // At least at the time we checked, callback existed, so we queue a work item to invoke
                        // the handlers. It's possible those callbacks will have been unregistered by the time
                        // the work item is invoked... that's fine, it just means we spent a bit of energy we
                        // ultimately didn't have to.  Note we explicitly don't schedule each registration individually
                        // to run concurrently, as there's no guarantee they're independent and safe to do so.
                        return Task.Factory.StartNew(s =>
                        {
                            ((CancellationTokenSource)s!).ExecuteCallbackHandlers(throwOnFirstException: false);
                        }, this, CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
                    }
                }
            }

            // As with Cancel, CancelAsync returns immediately if cancellation has already been requested
            // even if cancellation isn't already completed.
            return Task.CompletedTask;
        }

        /// <summary>Schedules a Cancel operation on this <see cref="CancellationTokenSource"/>.</summary>
        /// <param name="delay">The time span to wait before canceling this <see cref="CancellationTokenSource"/>.
        /// </param>
        /// <exception cref="ObjectDisposedException">The exception thrown when this <see
        /// cref="CancellationTokenSource"/> has been disposed.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The <paramref name="delay"/> is less than -1 or greater than maximum allowed timer duration.
        /// </exception>
        /// <remarks>
        /// <para>
        /// The countdown for the delay starts during this call.  When the delay expires,
        /// this <see cref="CancellationTokenSource"/> is canceled, if it has
        /// not been canceled already.
        /// </para>
        /// <para>
        /// Subsequent calls to CancelAfter will reset the delay for this
        /// <see cref="CancellationTokenSource"/>, if it has not been canceled already.
        /// </para>
        /// </remarks>
        public void CancelAfter(TimeSpan delay)
        {
            long totalMilliseconds = (long)delay.TotalMilliseconds;
            if (totalMilliseconds < -1 || totalMilliseconds > Timer.MaxSupportedTimeout)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.delay);
            }

            CancelAfter((uint)totalMilliseconds);
        }

        /// <summary>
        /// Schedules a Cancel operation on this <see cref="CancellationTokenSource"/>.
        /// </summary>
        /// <param name="millisecondsDelay">The time span to wait before canceling this <see
        /// cref="CancellationTokenSource"/>.
        /// </param>
        /// <exception cref="ObjectDisposedException">The exception thrown when this <see
        /// cref="CancellationTokenSource"/> has been disposed.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The exception thrown when <paramref name="millisecondsDelay"/> is less than -1.
        /// </exception>
        /// <remarks>
        /// <para>
        /// The countdown for the millisecondsDelay starts during this call.  When the millisecondsDelay expires,
        /// this <see cref="CancellationTokenSource"/> is canceled, if it has
        /// not been canceled already.
        /// </para>
        /// <para>
        /// Subsequent calls to CancelAfter will reset the millisecondsDelay for this
        /// <see cref="CancellationTokenSource"/>, if it has not been
        /// canceled already.
        /// </para>
        /// </remarks>
        public void CancelAfter(int millisecondsDelay)
        {
            if (millisecondsDelay < -1)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.millisecondsDelay);
            }

            CancelAfter((uint)millisecondsDelay);
        }

        private void CancelAfter(uint millisecondsDelay)
        {
            ThrowIfDisposed();

            if (IsCancellationRequested)
            {
                return;
            }

            // There is a race condition here as a Cancel could occur between the check of
            // IsCancellationRequested and the creation of the timer.  This is benign; in the
            // worst case, a timer will be created that has no effect when it expires.

            // Also, if Dispose() is called right here (after ThrowIfDisposed(), before timer
            // creation), it would result in a leaked Timer object (at least until the timer
            // expired and Disposed itself).  But this would be considered bad behavior, as
            // Dispose() is not thread-safe and should not be called concurrently with CancelAfter().

            ITimer? timer = _timer;
            if (timer == null)
            {
                // Lazily initialize the timer in a thread-safe fashion.
                // Initially set to "never go off" because we don't want to take a
                // chance on a timer "losing" the initialization and then
                // cancelling the token before it (the timer) can be disposed.
                timer = new TimerQueueTimer(s_timerCallback, this, Timeout.UnsignedInfinite, Timeout.UnsignedInfinite, flowExecutionContext: false);
                ITimer? currentTimer = Interlocked.CompareExchange(ref _timer, timer, null);
                if (currentTimer != null)
                {
                    // We did not initialize the timer.  Dispose the new timer.
                    timer.Dispose();
                    timer = currentTimer;
                }
            }

            timer.Change(
                millisecondsDelay == Timeout.UnsignedInfinite ? Timeout.InfiniteTimeSpan : TimeSpan.FromMilliseconds(millisecondsDelay),
                Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Attempts to reset the <see cref="CancellationTokenSource"/> to be used for an unrelated operation.
        /// </summary>
        /// <returns>
        /// true if the <see cref="CancellationTokenSource"/> has not had cancellation requested and could
        /// have its state reset to be reused for a subsequent operation; otherwise, false.
        /// </returns>
        /// <remarks>
        /// <see cref="TryReset"/> is intended to be used by the sole owner of the <see cref="CancellationTokenSource"/>
        /// when it is known that the operation with which the <see cref="CancellationTokenSource"/> was used has
        /// completed, no one else will be attempting to cancel it, and any registrations still remaining are erroneous.
        /// Upon a successful reset, such registrations will no longer be notified for any subsequent cancellation of the
        /// <see cref="CancellationTokenSource"/>; however, if any component still holds a reference to this
        /// <see cref="CancellationTokenSource"/> either directly or indirectly via a <see cref="CancellationToken"/>
        /// handed out from it, polling via their reference will show the current state any time after the reset as
        /// it's the same instance.  Usage of <see cref="TryReset"/> concurrently with requesting cancellation is not
        /// thread-safe and may result in TryReset returning true even if cancellation was already requested and may result
        /// in registrations not being invoked as part of the concurrent cancellation request.
        /// </remarks>
        public bool TryReset()
        {
            ThrowIfDisposed();

            // We can only reset if cancellation has not yet been requested: we never want to allow a CancellationToken
            // to transition from canceled to non-canceled.
            if (_state == States.NotCanceledState)
            {
                // If there is no timer, then we're free to reset.  If there is a timer, then we need to first try
                // to reset it to be infinite so that it won't fire, and then recognize that it could have already
                // fired by the time we successfully changed it, and so check to see whether that's possibly the case.
                // If we successfully reset it and it never fired, then we can be sure it won't trigger cancellation.
                bool reset = _timer is null ||
                    (_timer is TimerQueueTimer timer && timer.Change(Timeout.UnsignedInfinite, Timeout.UnsignedInfinite) && !timer._everQueued);

                if (reset)
                {
                    // We're not canceled and no timer will run to cancel us.
                    // Clear out all the registrations, and return that we've successfully reset.
                    Volatile.Read(ref _registrations)?.UnregisterAll();
                    return true;
                }
            }

            // Failed to reset.
            return false;
        }

        /// <summary>Releases the resources used by this <see cref="CancellationTokenSource" />.</summary>
        /// <remarks>This method is not thread-safe for any other concurrent calls.</remarks>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="CancellationTokenSource" /> class and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                // We specifically tolerate that a callback can be unregistered
                // after the CTS has been disposed and/or concurrently with cts.Dispose().
                // This is safe without locks because Dispose doesn't interact with values
                // in the callback partitions, only nulling out the ref to existing partitions.
                //
                // We also tolerate that a callback can be registered after the CTS has been
                // disposed.  This is safe because InternalRegister is tolerant
                // of _callbackPartitions becoming null during its execution.  However,
                // we run the acceptable risk of _callbackPartitions getting reinitialized
                // to non-null if there is a race between Dispose and Register, in which case this
                // instance may unnecessarily hold onto a registered callback.  But that's no worse
                // than if Dispose wasn't safe to use concurrently, as Dispose would never be called,
                // and thus no handlers would be dropped.
                //
                // And, we tolerate Dispose being used concurrently with Cancel.  This is necessary
                // to properly support, e.g., LinkedCancellationTokenSource, where, due to common usage patterns,
                // it's possible for this pairing to occur with valid usage (e.g. a component accepts
                // an external CancellationToken and uses CreateLinkedTokenSource to combine it with an
                // internal source of cancellation, then Disposes of that linked source, which could
                // happen at the same time the external entity is requesting cancellation).

                ITimer? timer = _timer;
                if (timer != null)
                {
                    _timer = null;
                    timer.Dispose(); // ITimer.Dispose is thread-safe
                }

                _registrations = null; // allow the GC to clean up registrations

                // If a kernel event was created via WaitHandle, we'd like to Dispose of it.  However,
                // we only want to do so if it's not being used by Cancel concurrently.  First, we
                // interlocked exchange it to be null, and then we check whether cancellation is currently
                // in progress.  NotifyCancellation will only try to set the event if it exists after it's
                // transitioned to and while it's in the NotifyingState.
                if (_kernelEvent != null)
                {
                    ManualResetEvent? mre = Interlocked.Exchange<ManualResetEvent?>(ref _kernelEvent!, null);
                    if (mre != null && _state != States.NotifyingState)
                    {
                        mre.Dispose();
                    }
                }

                _disposed = true;
            }
        }

        /// <summary>Throws an exception if the source has been disposed.</summary>
        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                ThrowHelper.ThrowObjectDisposedException(ExceptionResource.CancellationTokenSource_Disposed);
            }
        }

        /// <summary>
        /// Registers a callback object. If cancellation has already occurred, the
        /// callback will have been run by the time this method returns.
        /// </summary>
        internal CancellationTokenRegistration Register(
            Delegate callback, object? stateForCallback, SynchronizationContext? syncContext, ExecutionContext? executionContext)
        {
            // If not canceled, register the handler; if canceled already, run the callback synchronously.
            if (!IsCancellationRequested)
            {
                // We allow Dispose to be called concurrently with Register.  While this is not a recommended practice,
                // consumers can and do use it this way.
                if (_disposed)
                {
                    return default;
                }

                // Get the registrations object. It's lazily initialized to keep the size of a CTS smaller for situations
                // where all operations associated with the CTS complete synchronously and never actually need to register,
                // or all only poll.
                Registrations? registrations = Volatile.Read(ref _registrations);
                if (registrations is null)
                {
                    registrations = new Registrations(this);
                    registrations = Interlocked.CompareExchange(ref _registrations, registrations, null) ?? registrations;
                }

                // If it looks like there's a node in the freelist we could grab, grab the lock and try to get, configure,
                // and register the node.
                CallbackNode? node = null;
                long id = 0;
                if (registrations.FreeNodeList is not null)
                {
                    registrations.EnterLock();
                    try
                    {
                        // Try to take a free node.  If we're able to, configure the node and register it.
                        node = registrations.FreeNodeList;
                        if (node is not null)
                        {
                            registrations.FreeNodeList = node.Next;

                            node.Id = id = registrations.NextAvailableId++;
                            node.Callback = callback;
                            node.CallbackState = stateForCallback;
                            node.ExecutionContext = executionContext;
                            node.SynchronizationContext = syncContext;
                            node.Next = registrations.Callbacks;
                            registrations.Callbacks = node;
                            node.Next?.Prev = node;
                        }
                    }
                    finally
                    {
                        registrations.ExitLock();
                    }
                }

                // If we were unsuccessful in using a free node, create a new one, configure it, and register it.
                if (node is null)
                {
                    // Allocate the node if we couldn't get one from the free list.  We avoid
                    // doing this while holding the spin lock, to avoid a potentially arbitrary
                    // amount of GC-related work under the lock, which we aim to keep very tight,
                    // just a few assignments.
                    node = new CallbackNode(registrations);

                    node.Callback = callback;
                    node.CallbackState = stateForCallback;
                    node.ExecutionContext = executionContext;
                    node.SynchronizationContext = syncContext;

                    registrations.EnterLock();
                    try
                    {
                        node.Id = id = registrations.NextAvailableId++;
                        node.Next = registrations.Callbacks;
                        node.Next?.Prev = node;
                        registrations.Callbacks = node;
                    }
                    finally
                    {
                        registrations.ExitLock();
                    }
                }

                if (!IsCancellationRequested || !registrations.Unregister(id, node))
                {
                    return new CancellationTokenRegistration(id, node);
                }
            }

            // Cancellation already occurred.  Run the callback on this thread and return an empty registration.
            Invoke(callback, stateForCallback, this);
            return default;
        }

        private void NotifyCancellation(bool throwOnFirstException)
        {
            if (TransitionToCancellationRequested())
            {
                // If we're the first to signal cancellation, do the main extra work.
                ExecuteCallbackHandlers(throwOnFirstException);
            }
        }

        /// <summary>Transitions from <see cref="States.NotCanceledState"/> to <see cref="States.NotifyingState"/>.</summary>
        /// <returns>true if it successfully transitioned; otherwise, false.</returns>
        /// <remarks>If it successfully transitions, it will also have disposed of <see cref="_timer"/> and set <see cref="_kernelEvent"/>.</remarks>
        private bool TransitionToCancellationRequested()
        {
            if (!IsCancellationRequested &&
                Interlocked.CompareExchange(ref _state, States.NotifyingState, States.NotCanceledState) == States.NotCanceledState)
            {
                // Dispose of the timer, if any.  Dispose may be running concurrently here, but ITimer.Dispose is thread-safe.
                ITimer? timer = _timer;
                if (timer != null)
                {
                    _timer = null;
                    timer.Dispose();
                }

                // Set the event if it's been lazily initialized and hasn't yet been disposed of.  Dispose may
                // be running concurrently, in which case either it'll have set m_kernelEvent back to null and
                // we won't see it here, or it'll see that we've transitioned to NOTIFYING and will skip disposing it,
                // leaving cleanup to finalization.
                _kernelEvent?.Set();

                return true;
            }

            return false;
        }

        /// <summary>Invoke all registered callbacks.</summary>
        /// <remarks>The handlers are invoked synchronously in LIFO order.</remarks>
        private void ExecuteCallbackHandlers(bool throwOnFirstException)
        {
            // If there are no callbacks to run, we can safely exit.  Any race conditions to lazy initialize it
            // will see IsCancellationRequested and will then run the callback themselves.
            Registrations? registrations = Interlocked.Exchange(ref _registrations, null);
            if (registrations is null)
            {
                Interlocked.Exchange(ref _state, States.NotifyingCompleteState);
                return;
            }

            // Record the threadID being used for running the callbacks.
            registrations.ThreadIDExecutingCallbacks = Environment.CurrentManagedThreadId;

            List<Exception>? exceptionList = null;
            try
            {
                // We call the delegates in LIFO order on each partition so that callbacks fire 'deepest first'.
                // This is intended to help with nesting scenarios so that child enlisters cancel before their parents.

                // Iterate through all nodes in the partition.  We remove each node prior
                // to processing it.  This allows for unregistration of subsequent registrations
                // to still be effective even as other registrations are being invoked.
                while (true)
                {
                    CallbackNode? node;
                    registrations.EnterLock();
                    try
                    {
                        // Pop the next registration from the callbacks list.
                        node = registrations.Callbacks;
                        if (node == null)
                        {
                            // No more registrations to process.
                            break;
                        }

                        node.Next?.Prev = null;
                        registrations.Callbacks = node.Next;

                        // Publish the intended callback ID, to ensure ctr.Dispose can tell if a wait is necessary.
                        // This write happens while the lock is held so that Dispose is either able to successfully
                        // unregister or is guaranteed to see an accurate executing callback ID, since it takes
                        // the same lock to remove the node from the callback list.
                        registrations.ExecutingCallbackId = node.Id;

                        // Now that we've grabbed the Id, reset the node's Id to 0.  This signals
                        // to code unregistering that the node is no longer associated with a callback.
                        node.Id = 0;
                    }
                    finally
                    {
                        registrations.ExitLock();
                    }

                    // Invoke the callback on this thread if there's no sync context or on the
                    // target sync context if there is one.
                    try
                    {
                        if (node.SynchronizationContext != null)
                        {
                            // Transition to the target syncContext and continue there.
                            node.SynchronizationContext.Send(static s =>
                            {
                                var n = (CallbackNode)s!;
                                n.Registrations.ThreadIDExecutingCallbacks = Environment.CurrentManagedThreadId;
                                n.ExecuteCallback();
                            }, node);
                            registrations.ThreadIDExecutingCallbacks = Environment.CurrentManagedThreadId; // above may have altered ThreadIDExecutingCallbacks, so reset it
                        }
                        else
                        {
                            node.ExecuteCallback();
                        }
                    }
                    catch (Exception ex) when (!throwOnFirstException)
                    {
                        // Store the exception and continue
                        (exceptionList ??= new List<Exception>()).Add(ex);
                    }

                    // Drop the node. While we could add it to the free list, doing so has cost (we'd need to take the lock again)
                    // and very limited value.  Since a source can only be canceled once, and after it's canceled registrations don't
                    // need nodes, the only benefit to putting this on the free list would be if Register raced with cancellation
                    // occurring, such that it could have used this free node but would instead need to allocate a new node (if
                    // there wasn't another free node available).
                }
            }
            finally
            {
                _state = States.NotifyingCompleteState;
                Interlocked.Exchange(ref registrations.ExecutingCallbackId, 0); // for safety, prevent reorderings crossing this point and seeing inconsistent state.
            }

            if (exceptionList != null)
            {
                throw new AggregateException(exceptionList);
            }
        }

        /// <summary>
        /// Creates a <see cref="CancellationTokenSource"/> that will be in the canceled state
        /// when any of the source tokens are in the canceled state.
        /// </summary>
        /// <param name="token1">The first <see cref="CancellationToken">CancellationToken</see> to observe.</param>
        /// <param name="token2">The second <see cref="CancellationToken">CancellationToken</see> to observe.</param>
        /// <returns>A <see cref="CancellationTokenSource"/> that is linked
        /// to the source tokens.</returns>
        public static CancellationTokenSource CreateLinkedTokenSource(CancellationToken token1, CancellationToken token2) =>
            !token1.CanBeCanceled ? CreateLinkedTokenSource(token2) :
            token2.CanBeCanceled ? new Linked2CancellationTokenSource(token1, token2) :
            (CancellationTokenSource)new Linked1CancellationTokenSource(token1);

        /// <summary>
        /// Creates a <see cref="CancellationTokenSource"/> that will be in the canceled state
        /// when the supplied token is in the canceled state.
        /// </summary>
        /// <param name="token">The <see cref="CancellationToken">CancellationToken</see> to observe.</param>
        /// <returns>A <see cref="CancellationTokenSource"/> that is linked to the source token.</returns>
        public static CancellationTokenSource CreateLinkedTokenSource(CancellationToken token) =>
            token.CanBeCanceled ? new Linked1CancellationTokenSource(token) : new CancellationTokenSource();

        /// <summary>
        /// Creates a <see cref="CancellationTokenSource"/> that will be in the canceled state
        /// when any of the source tokens are in the canceled state.
        /// </summary>
        /// <param name="tokens">The <see cref="CancellationToken">CancellationToken</see> instances to observe.</param>
        /// <returns>A <see cref="CancellationTokenSource"/> that is linked to the source tokens.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="tokens"/> is null.</exception>
        public static CancellationTokenSource CreateLinkedTokenSource(params CancellationToken[] tokens)
        {
            ArgumentNullException.ThrowIfNull(tokens);
            return CreateLinkedTokenSource((ReadOnlySpan<CancellationToken>)tokens);
        }

        /// <summary>
        /// Creates a <see cref="CancellationTokenSource"/> that will be in the canceled state
        /// when any of the source tokens are in the canceled state.
        /// </summary>
        /// <param name="tokens">The <see cref="CancellationToken">CancellationToken</see> instances to observe.</param>
        /// <returns>A <see cref="CancellationTokenSource"/> that is linked to the source tokens.</returns>
        public static CancellationTokenSource CreateLinkedTokenSource(params ReadOnlySpan<CancellationToken> tokens)
        {
            return tokens.Length switch
            {
                0 => throw new ArgumentException(SR.CancellationToken_CreateLinkedToken_TokensIsEmpty),
                1 => CreateLinkedTokenSource(tokens[0]),
                2 => CreateLinkedTokenSource(tokens[0], tokens[1]),

                // a defensive copy is not required as the array has value-items that have only a single reference field,
                // hence each item cannot be null itself, and reads of the payloads cannot be torn.
                _ => new LinkedNCancellationTokenSource(tokens),
            };
        }

        private sealed class Linked1CancellationTokenSource : CancellationTokenSource
        {
            private readonly CancellationTokenRegistration _reg1;

            internal Linked1CancellationTokenSource(CancellationToken token1)
            {
                _reg1 = token1.UnsafeRegister(LinkedNCancellationTokenSource.s_linkedTokenCancelDelegate, this);
            }

            protected override void Dispose(bool disposing)
            {
                if (!disposing || _disposed)
                {
                    return;
                }

                _reg1.Dispose();
                base.Dispose(disposing);
            }
        }

        private sealed class Linked2CancellationTokenSource : CancellationTokenSource
        {
            private readonly CancellationTokenRegistration _reg1;
            private readonly CancellationTokenRegistration _reg2;

            internal Linked2CancellationTokenSource(CancellationToken token1, CancellationToken token2)
            {
                _reg1 = token1.UnsafeRegister(LinkedNCancellationTokenSource.s_linkedTokenCancelDelegate, this);
                _reg2 = token2.UnsafeRegister(LinkedNCancellationTokenSource.s_linkedTokenCancelDelegate, this);
            }

            protected override void Dispose(bool disposing)
            {
                if (!disposing || _disposed)
                {
                    return;
                }

                _reg1.Dispose();
                _reg2.Dispose();
                base.Dispose(disposing);
            }
        }

        private sealed class LinkedNCancellationTokenSource : CancellationTokenSource
        {
            internal static readonly Action<object?> s_linkedTokenCancelDelegate = static s =>
            {
                ((CancellationTokenSource)s).NotifyCancellation(throwOnFirstException: false); // skip ThrowIfDisposed() check in Cancel()
            };
            private CancellationTokenRegistration[]? _linkingRegistrations;

            internal LinkedNCancellationTokenSource(ReadOnlySpan<CancellationToken> tokens)
            {
                _linkingRegistrations = new CancellationTokenRegistration[tokens.Length];

                for (int i = 0; i < tokens.Length; i++)
                {
                    if (tokens[i].CanBeCanceled)
                    {
                        _linkingRegistrations[i] = tokens[i].UnsafeRegister(s_linkedTokenCancelDelegate, this);
                    }
                    // Empty slots in the array will be default(CancellationTokenRegistration), which are nops to Dispose.
                    // Based on usage patterns, such occurrences should also be rare, such that it's not worth resizing
                    // the array and incurring the related costs.
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (!disposing || _disposed)
                {
                    return;
                }

                CancellationTokenRegistration[]? linkingRegistrations = _linkingRegistrations;
                if (linkingRegistrations != null)
                {
                    _linkingRegistrations = null; // release for GC once we're done enumerating
                    for (int i = 0; i < linkingRegistrations.Length; i++)
                    {
                        linkingRegistrations[i].Dispose();
                    }
                }

                base.Dispose(disposing);
            }
        }

        private static void Invoke(Delegate d, object? state, CancellationTokenSource source)
        {
            if (d is Action<object?> actionWithState)
            {
                actionWithState(state);
            }
            else
            {
                ((Action<object?, CancellationToken>)d)(state, new CancellationToken(source));
            }
        }

        /// <summary>Set of all the registrations in the token source.</summary>
        /// <remarks>
        /// Separated out into a separate instance to keep CancellationTokenSource smaller for the case where one is created but nothing is registered with it.
        /// This happens not infrequently, in particular when one is created for an operation that ends up completing synchronously / quickly.
        /// </remarks>
        internal sealed class Registrations
        {
            /// <summary>The associated source.</summary>
            public readonly CancellationTokenSource Source;
            /// <summary>Doubly-linked list of callbacks registered with the source. Callbacks are removed during unregistration and as they're invoked.</summary>
            public CallbackNode? Callbacks;
            /// <summary>Singly-linked list of free nodes that can be used for subsequent callback registrations.</summary>
            public CallbackNode? FreeNodeList;
            /// <summary>Every callback is assigned a unique, never-reused ID.  This defines the next available ID.</summary>
            public long NextAvailableId = 1; // avoid using 0, as that's the default long value and used to represent an empty node
            /// <summary>Tracks the running callback to assist ctr.Dispose() to wait for the target callback to complete.</summary>
            public long ExecutingCallbackId;
            /// <summary>The ID of the thread currently executing the main body of CTS.Cancel()</summary>
            /// <remarks>
            /// This helps us to know if a call to ctr.Dispose() is running 'within' a cancellation callback.
            /// This is updated as we move between the main thread calling cts.Cancel() and any syncContexts
            /// that are used to actually run the callbacks.
            /// </remarks>
            public volatile int ThreadIDExecutingCallbacks = -1;
            /// <summary>Spin lock that protects state in the instance.</summary>
            private volatile bool _locked;

            /// <summary>Initializes the instance.</summary>
            /// <param name="source">The associated source.</param>
            public Registrations(CancellationTokenSource source) => Source = source;

            [MethodImpl(MethodImplOptions.AggressiveInlining)] // used in only two places, one of which is a hot path
            private void Recycle(CallbackNode node)
            {
                // Clear out the unused node and put it on the singly-linked free list.
                // The only field we don't clear out is the associated Registrations, as that's fixed
                // throughout the node's lifetime.
                node.Id = 0;
                node.Callback = null;
                node.CallbackState = null;
                node.ExecutionContext = null;
                node.SynchronizationContext = null;

                node.Prev = null;
                node.Next = FreeNodeList;
                FreeNodeList = node;
            }

            /// <summary>Unregisters a callback.</summary>
            /// <param name="id">The expected id of the registration.</param>
            /// <param name="node">The callback node.</param>
            /// <returns>true if the node was found and removed; false if it couldn't be found or didn't match the provided id.</returns>
            public bool Unregister(long id, CallbackNode node)
            {
                if (id == 0)
                {
                    // In general, we won't get 0 passed in here.  However, race conditions between threads
                    // Unregistering and also zero'ing out the CancellationTokenRegistration could cause 0
                    // to be passed in here, in which case there's nothing to do. 0 is never a valid id.
                    return false;
                }

                EnterLock();
                try
                {
                    if (node.Id != id)
                    {
                        // Either:
                        // - The callback is currently or has already been invoked, in which case node.Id
                        //   will no longer equal the assigned id, as it will have transitioned to 0.
                        // - The registration was already disposed of, in which case node.Id will similarly
                        //   no longer equal the assigned id, as it will have transitioned to 0 and potentially
                        //   then to another (larger) value when reused for a new registration.
                        // In either case, there's nothing to unregister.
                        return false;
                    }

                    // The registration must still be in the callbacks list.  Remove it.
                    if (Callbacks == node)
                    {
                        Callbacks = node.Next;
                    }
                    else
                    {
                        node.Prev.Next = node.Next;
                    }

                    node.Next?.Prev = node.Prev;

                    Recycle(node);

                    return true;
                }
                finally
                {
                    ExitLock();
                }
            }

            /// <summary>Moves all registrations to the free list.</summary>
            public void UnregisterAll()
            {
                EnterLock();
                try
                {
                    // Null out all callbacks.
                    CallbackNode? node = Callbacks;
                    Callbacks = null;

                    // Reset and move each node that was in the callbacks list to the free list.
                    while (node != null)
                    {
                        CallbackNode? next = node.Next;
                        Recycle(node);
                        node = next;
                    }
                }
                finally
                {
                    ExitLock();
                }
            }

            /// <summary>
            /// Wait for a single callback to complete (or, more specifically, to not be running).
            /// It is ok to call this method if the callback has already finished.
            /// Calling this method before the target callback has been selected for execution would be an error.
            /// </summary>
            public void WaitForCallbackToComplete(long id)
            {
                SpinWait sw = default;
                while (Volatile.Read(ref ExecutingCallbackId) == id)
                {
                    sw.SpinOnce();  // spin, as we assume callback execution is fast and that this situation is rare.
                }
            }

            /// <summary>
            /// Asynchronously wait for a single callback to complete (or, more specifically, to not be running).
            /// It is ok to call this method if the callback has already finished.
            /// Calling this method before the target callback has been selected for execution would be an error.
            /// </summary>
            public async ValueTask WaitForCallbackToCompleteAsync(long id)
            {
                // Employ an async loop that'll poll for the currently executing callback to complete. While such polling isn't
                // ideal, we expect this to be a rare case (disposing while the associated callback is running), and brief when
                // it happens (so the polling will be minimal), and making this work with a callback mechanism will add additional
                // cost to other more common cases.
                while (Volatile.Read(ref ExecutingCallbackId) == id)
                {
                    await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
                }
            }

            /// <summary>Enters the lock for this instance.  The current thread must not be holding the lock, but that is not validated.</summary>
            public void EnterLock()
            {
                ref bool value = ref _locked;
                if (Interlocked.Exchange(ref value, true))
                {
                    Contention(ref value);

                    [MethodImpl(MethodImplOptions.NoInlining)]
                    static void Contention(ref bool value)
                    {
                        SpinWait sw = default;
                        do { sw.SpinOnce(); } while (Interlocked.Exchange(ref value, true));
                    }
                }
            }

            /// <summary>Exits the lock for this instance.  The current thread must be holding the lock, but that is not validated.</summary>
            public void ExitLock()
            {
                _locked = false;
            }
        }

        /// <summary>All of the state associated a registered callback, in a node that's part of a linked list of registered callbacks.</summary>
        internal sealed class CallbackNode
        {
            public readonly Registrations Registrations;
            public CallbackNode? Prev;
            public CallbackNode? Next;

            public long Id;
            public Delegate? Callback; // Action<object> or Action<object,CancellationToken>
            public object? CallbackState;
            public ExecutionContext? ExecutionContext;
            public SynchronizationContext? SynchronizationContext;

            public CallbackNode(Registrations registrations)
            {
                Registrations = registrations;
            }

            public void ExecuteCallback()
            {
                ExecutionContext? context = ExecutionContext;
                if (context is null)
                {
                    Invoke(Callback, CallbackState, Registrations.Source);
                    return;
                }
                ExecutionContext.RunInternal(context, static s =>
                {
                    var node = (CallbackNode)s!;
                    Invoke(node.Callback, node.CallbackState, node.Registrations.Source);
                }, this);
            }
        }


    }


    public delegate void ContextCallback(object? state);

    internal delegate void ContextCallback<TState>(ref TState state);


    /// <summary>
    /// Manages the execution context for the current thread.
    /// </summary>
    public sealed class ExecutionContext : IDisposable
    {
        internal static readonly ExecutionContext Default = new ExecutionContext();
        internal static readonly ExecutionContext DefaultFlowSuppressed =
            new ExecutionContext(AsyncLocalValueMap.Empty, new IAsyncLocal[0], isFlowSuppressed: true);

        private readonly IAsyncLocalValueMap? m_localValues;
        private readonly IAsyncLocal[]? m_localChangeNotifications;
        private readonly bool m_isFlowSuppressed;
        private readonly bool m_isDefault;

        private ExecutionContext()
        {
            m_isDefault = true;
        }

        private ExecutionContext(
            IAsyncLocalValueMap localValues,
            IAsyncLocal[]? localChangeNotifications,
            bool isFlowSuppressed)
        {
            m_localValues = localValues;
            m_localChangeNotifications = localChangeNotifications;
            m_isFlowSuppressed = isFlowSuppressed;
        }

        public static ExecutionContext? Capture()
        {
            ExecutionContext? executionContext = Thread.CurrentThread._executionContext;
            if (executionContext == null)
            {
                executionContext = Default;
            }
            else if (executionContext.m_isFlowSuppressed)
            {
                executionContext = null;
            }

            return executionContext;
        }

        // Allows capturing asynclocals for a FlowSuppressed ExecutionContext rather than returning null.
        internal static ExecutionContext? CaptureForRestore()
        {
            // This is a short cut for:
            //
            // ExecutionContext.RestoreFlow()
            // var ec = ExecutionContext.Capture()
            // ExecutionContext.SuppressFlow();
            // ...
            // ExecutionContext.Restore(ec)
            // ExecutionContext.SuppressFlow();

            return Thread.CurrentThread._executionContext;
        }

        // Capture for flowing/restoring across suspension points.
        // Respects m_isFlowSuppressed, but avoids 'null' -> 'Default' -> 'null' reinterpretation.
        internal static ExecutionContext? CaptureForSuspension(Thread currentThread)
        {
            ExecutionContext? executionContext = currentThread._executionContext;
            if (executionContext?.m_isFlowSuppressed == true)
            {
                executionContext = DefaultFlowSuppressed;
            }

            return executionContext;
        }

        private ExecutionContext? ShallowClone(bool isFlowSuppressed)
        {
            if (m_localValues == null || AsyncLocalValueMap.IsEmpty(m_localValues))
            {
                return isFlowSuppressed ?
                    DefaultFlowSuppressed :
                    null; // implies the default context
            }

            return new ExecutionContext(m_localValues, m_localChangeNotifications, isFlowSuppressed);
        }

        public static AsyncFlowControl SuppressFlow()
        {
            Thread currentThread = Thread.CurrentThread;
            ExecutionContext? executionContext = currentThread._executionContext ?? Default;

            AsyncFlowControl asyncFlowControl = default;
            if (!executionContext.m_isFlowSuppressed)
            {
                currentThread._executionContext = executionContext.ShallowClone(isFlowSuppressed: true);
                asyncFlowControl.Initialize(currentThread);
            }

            return asyncFlowControl;
        }

        public static void RestoreFlow()
        {
            Thread currentThread = Thread.CurrentThread;
            ExecutionContext? executionContext = currentThread._executionContext;
            if (executionContext == null || !executionContext.m_isFlowSuppressed)
            {
                throw new InvalidOperationException(SR.InvalidOperation_CannotRestoreUnsuppressedFlow);
            }

            currentThread._executionContext = executionContext.ShallowClone(isFlowSuppressed: false);
        }

        public static bool IsFlowSuppressed()
        {
            ExecutionContext? executionContext = Thread.CurrentThread._executionContext;
            return executionContext != null && executionContext.m_isFlowSuppressed;
        }

        internal bool HasChangeNotifications => m_localChangeNotifications != null;

        internal bool IsDefault => m_isDefault;

        internal bool InstanceIsFlowSuppressed => m_isFlowSuppressed;

        public static void Run(ExecutionContext executionContext, ContextCallback callback, object? state)
        {
            // Note: ExecutionContext.Run is an extremely hot function and used by every await, ThreadPool execution, etc.
            if (executionContext == null)
            {
                ThrowNullContext();
            }

            RunInternal(executionContext, callback, state);
        }

        internal static void RunInternal(ExecutionContext? executionContext, ContextCallback callback, object? state)
        {
            // Enregister previousExecutionCtx0 so they can be used in registers without EH forcing them to stack

            Thread currentThread = Thread.CurrentThread;
            ExecutionContext? previousExecutionCtx0 = currentThread._executionContext;
            if (previousExecutionCtx0 != null && previousExecutionCtx0.m_isDefault)
            {
                // Default is a null ExecutionContext internally
                previousExecutionCtx0 = null;
            }

            ExecutionContext? previousExecutionCtx = previousExecutionCtx0;
            SynchronizationContext? previousSyncCtx = currentThread._synchronizationContext;

            if (executionContext != null && executionContext.m_isDefault)
            {
                // Default is a null ExecutionContext internally
                executionContext = null;
            }

            if (previousExecutionCtx != executionContext)
            {
                RestoreChangedContextToThread(currentThread, executionContext, previousExecutionCtx);
            }

            ExceptionDispatchInfo? edi = null;
            try
            {
                callback.Invoke(state);
            }
            catch (Exception ex)
            {
                // Note: we have a "catch" rather than a "finally" because we want
                // to stop the first pass of EH here.  That way we can restore the previous
                // context before any of our callers' EH filters run.
                edi = ExceptionDispatchInfo.Capture(ex);
            }

            // The common case is that these have not changed, so avoid the cost of a write barrier if not needed.
            if (currentThread._synchronizationContext != previousSyncCtx)
            {
                // Restore changed SynchronizationContext back to previous
                currentThread._synchronizationContext = previousSyncCtx;
            }

            ExecutionContext? currentExecutionCtx = currentThread._executionContext;
            if (currentExecutionCtx != previousExecutionCtx)
            {
                RestoreChangedContextToThread(currentThread, previousExecutionCtx, currentExecutionCtx);
            }

            // If exception was thrown by callback, rethrow it now original contexts are restored
            edi?.Throw();
        }

        /// <summary>
        /// Restores a captured execution context to on the current thread.
        /// </summary>
        /// <remarks>
        /// To revert to the current execution context; capture it before Restore, and Restore it again.
        /// It will not automatically be reverted unlike <see cref="Run"/>.
        /// </remarks>
        /// <param name="executionContext">The ExecutionContext to set.</param>
        /// <exception cref="InvalidOperationException"><paramref name="executionContext"/> is null.</exception>
        public static void Restore(ExecutionContext executionContext)
        {
            if (executionContext == null)
            {
                ThrowNullContext();
            }

            RestoreInternal(executionContext);
        }

        internal static void RestoreInternal(ExecutionContext? executionContext)
        {
            Thread currentThread = Thread.CurrentThread;

            ExecutionContext? currentExecutionCtx = currentThread._executionContext;
            if (currentExecutionCtx != null && currentExecutionCtx.m_isDefault)
            {
                // Default is a null ExecutionContext internally
                currentExecutionCtx = null;
            }

            if (executionContext != null && executionContext.m_isDefault)
            {
                // Default is a null ExecutionContext internally
                executionContext = null;
            }

            if (currentExecutionCtx != executionContext)
            {
                RestoreChangedContextToThread(currentThread, executionContext, currentExecutionCtx);
            }
        }

        internal static void RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext? executionContext, ContextCallback callback, object state)
        {
            // ThreadPool starts on Default Context so we don't need to save the "previous" state as we know it is Default (null)

            // Default is a null ExecutionContext internally
            if (executionContext != null && !executionContext.m_isDefault)
            {
                // Non-Default context to restore
                RestoreChangedContextToThread(threadPoolThread, contextToRestore: executionContext, currentContext: null);
            }

            ExceptionDispatchInfo? edi = null;
            try
            {
                callback.Invoke(state);
            }
            catch (Exception ex)
            {
                // Note: we have a "catch" rather than a "finally" because we want
                // to stop the first pass of EH here.  That way we can restore the previous
                // context before any of our callers' EH filters run.
                edi = ExceptionDispatchInfo.Capture(ex);
            }

            // Enregister threadPoolThread as it crossed EH, and use enregistered variable
            Thread currentThread = threadPoolThread;

            ExecutionContext? currentExecutionCtx = currentThread._executionContext;

            // Restore changed SynchronizationContext back to Default
            currentThread._synchronizationContext = null;
            if (currentExecutionCtx != null)
            {
                // The EC always needs to be reset for this overload, as it will flow back to the caller if it performs
                // extra work prior to returning to the Dispatch loop. For example for Task-likes it will flow out of await points
                RestoreChangedContextToThread(currentThread, contextToRestore: null, currentExecutionCtx);
            }

            // If exception was thrown by callback, rethrow it now original contexts are restored
            edi?.Throw();
        }

        internal static void RunForThreadPoolUnsafe<TState>(ExecutionContext executionContext, Action<TState> callback, in TState state)
        {
            // We aren't running in try/catch as if an exception is directly thrown on the ThreadPool either process
            // will crash or its a ThreadAbortException.

            // Restore Non-Default context
            Thread.CurrentThread._executionContext = executionContext;
            if (executionContext.HasChangeNotifications)
            {
                OnValuesChanged(previousExecutionCtx: null, executionContext);
            }

            callback.Invoke(state);

            // ThreadPoolWorkQueue.Dispatch will handle notifications and reset EC and SyncCtx back to default
        }

        internal static void RestoreChangedContextToThread(Thread currentThread, ExecutionContext? contextToRestore, ExecutionContext? currentContext)
        {
            // Restore changed ExecutionContext back to previous
            currentThread._executionContext = contextToRestore;
            if ((currentContext != null && currentContext.HasChangeNotifications) ||
                (contextToRestore != null && contextToRestore.HasChangeNotifications))
            {
                // There are change notifications; trigger any affected
                OnValuesChanged(currentContext, contextToRestore);
            }
        }

        // Inline as only called in one place and always called
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void ResetThreadPoolThread(Thread currentThread)
        {
            ExecutionContext? currentExecutionCtx = currentThread._executionContext;

            // Reset to defaults
            currentThread._synchronizationContext = null;
            currentThread._executionContext = null;

            if (currentExecutionCtx != null && currentExecutionCtx.HasChangeNotifications)
            {
                OnValuesChanged(currentExecutionCtx, nextExecutionCtx: null);

                // Reset to defaults again without change notifications in case the Change handler changed the contexts
                currentThread._synchronizationContext = null;
                currentThread._executionContext = null;
            }
        }

        internal static void OnValuesChanged(ExecutionContext? previousExecutionCtx, ExecutionContext? nextExecutionCtx)
        {
            // Collect Change Notifications
            IAsyncLocal[]? previousChangeNotifications = previousExecutionCtx?.m_localChangeNotifications;
            IAsyncLocal[]? nextChangeNotifications = nextExecutionCtx?.m_localChangeNotifications;

            // Fire Change Notifications
            try
            {
                if (previousChangeNotifications != null && nextChangeNotifications != null)
                {
                    // Both contexts have change notifications, check previousExecutionCtx first
                    foreach (IAsyncLocal local in previousChangeNotifications)
                    {
                        previousExecutionCtx.m_localValues.TryGetValue(local, out object? previousValue);
                        nextExecutionCtx.m_localValues.TryGetValue(local, out object? currentValue);

                        if (previousValue != currentValue)
                        {
                            local.OnValueChanged(previousValue, currentValue, contextChanged: true);
                        }
                    }

                    if (nextChangeNotifications != previousChangeNotifications)
                    {
                        // Check for additional notifications in nextExecutionCtx
                        foreach (IAsyncLocal local in nextChangeNotifications)
                        {
                            // If the local has a value in the previous context, we already fired the event
                            // for that local in the code above.
                            if (!previousExecutionCtx.m_localValues.TryGetValue(local, out object? previousValue))
                            {
                                nextExecutionCtx.m_localValues.TryGetValue(local, out object? currentValue);
                                if (previousValue != currentValue)
                                {
                                    local.OnValueChanged(previousValue, currentValue, contextChanged: true);
                                }
                            }
                        }
                    }
                }
                else if (previousChangeNotifications != null)
                {
                    // No current values, so just check previous against null
                    foreach (IAsyncLocal local in previousChangeNotifications)
                    {
                        previousExecutionCtx.m_localValues.TryGetValue(local, out object? previousValue);
                        if (previousValue != null)
                        {
                            local.OnValueChanged(previousValue, null, contextChanged: true);
                        }
                    }
                }
                else // Implied: nextChangeNotifications != null
                {
                    // No previous values, so just check current against null
                    foreach (IAsyncLocal local in nextChangeNotifications!)
                    {
                        nextExecutionCtx.m_localValues.TryGetValue(local, out object? currentValue);
                        if (currentValue != null)
                        {
                            local.OnValueChanged(null, currentValue, contextChanged: true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Environment.FailFast(
                    SR.ExecutionContext_ExceptionInAsyncLocalNotification,
                    ex);
            }
        }

        [DoesNotReturn]
        [StackTraceHidden]
        private static void ThrowNullContext()
        {
            throw new InvalidOperationException(SR.InvalidOperation_NullContext);
        }

        internal static object? GetLocalValue(IAsyncLocal local)
        {
            ExecutionContext? current = Thread.CurrentThread._executionContext;
            if (current == null)
            {
                return null;
            }

            current.m_localValues.TryGetValue(local, out object? value);
            return value;
        }

        internal static void SetLocalValue(IAsyncLocal local, object? newValue, bool needChangeNotifications)
        {
            ExecutionContext? current = Thread.CurrentThread._executionContext;

            object? previousValue = null;
            bool hadPreviousValue = false;
            if (current != null)
            {
                hadPreviousValue = current.m_localValues.TryGetValue(local, out previousValue);
            }

            if (previousValue == newValue)
            {
                return;
            }

            // Regarding 'treatNullValueAsNonexistent: !needChangeNotifications' below:
            // - When change notifications are not necessary for this IAsyncLocal, there is no observable difference between
            //   storing a null value and removing the IAsyncLocal from 'm_localValues'
            // - When change notifications are necessary for this IAsyncLocal, the IAsyncLocal's absence in 'm_localValues'
            //   indicates that this is the first value change for the IAsyncLocal and it needs to be registered for change
            //   notifications. So in this case, a null value must be stored in 'm_localValues' to indicate that the IAsyncLocal
            //   is already registered for change notifications.
            IAsyncLocal[]? newChangeNotifications = null;
            IAsyncLocalValueMap newValues;
            bool isFlowSuppressed = false;
            if (current != null)
            {
                isFlowSuppressed = current.m_isFlowSuppressed;
                newValues = current.m_localValues.Set(local, newValue, treatNullValueAsNonexistent: !needChangeNotifications);
                newChangeNotifications = current.m_localChangeNotifications;
            }
            else
            {
                // First AsyncLocal
                newValues = AsyncLocalValueMap.Create(local, newValue, treatNullValueAsNonexistent: !needChangeNotifications);
            }

            //
            // Either copy the change notification array, or create a new one, depending on whether we need to add a new item.
            //
            if (needChangeNotifications)
            {
                if (hadPreviousValue)
                {
                    //Debug.Assert(newChangeNotifications != null);
                    //Debug.Assert(Array.IndexOf(newChangeNotifications, local) >= 0);
                }
                else if (newChangeNotifications == null)
                {
                    newChangeNotifications = [local];
                }
                else
                {
                    int newNotificationIndex = newChangeNotifications.Length;
                    Array.Resize(ref newChangeNotifications, newNotificationIndex + 1);
                    newChangeNotifications[newNotificationIndex] = local;
                }
            }

            Thread.CurrentThread._executionContext =
                (!isFlowSuppressed && AsyncLocalValueMap.IsEmpty(newValues)) ?
                null : // No values, return to Default context
                new ExecutionContext(newValues, newChangeNotifications, isFlowSuppressed);

            if (needChangeNotifications)
            {
                local.OnValueChanged(previousValue, newValue, contextChanged: false);
            }
        }

        public ExecutionContext CreateCopy()
        {
            return this; // since CoreCLR's ExecutionContext is immutable, we don't need to create copies.
        }

        public void Dispose()
        {
            // For CLR compat only
        }
    }

    public struct AsyncFlowControl : IEquatable<AsyncFlowControl>, IDisposable
    {
        private Thread? _thread;

        internal void Initialize(Thread currentThread)
        {
            _thread = currentThread;
        }

        public void Undo()
        {
            if (_thread is null)
            {
                return;
            }

            if (Thread.CurrentThread != _thread)
            {
                throw new InvalidOperationException(SR.InvalidOperation_CannotUseAFCOtherThread);
            }

            // An async flow control cannot be undone when a different execution context is applied. The .NET Framework
            // mutates the execution context when its state changes, and only changes the instance when an execution context
            // is applied (for instance, through ExecutionContext.Run). The framework prevents a suppressed-flow execution
            // context from being applied by returning null from ExecutionContext.Capture, so the only type of execution
            // context that can be applied is one whose flow is not suppressed. After suppressing flow and changing an async
            // local's value, the .NET Framework verifies that a different execution context has not been applied by
            // checking the execution context instance against the one saved from when flow was suppressed. In .NET Core,
            // since the execution context instance will change after changing the async local's value, it verifies that a
            // different execution context has not been applied, by instead ensuring that the current execution context's
            // flow is suppressed.
            if (!ExecutionContext.IsFlowSuppressed())
            {
                throw new InvalidOperationException(SR.InvalidOperation_AsyncFlowCtrlCtxMismatch);
            }

            _thread = null;
            ExecutionContext.RestoreFlow();
        }

        public void Dispose()
        {
            Undo();
        }

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            return obj is AsyncFlowControl asyncControl && Equals(asyncControl);
        }

        public bool Equals(AsyncFlowControl obj)
        {
            return _thread == obj._thread;
        }

        public override int GetHashCode()
        {
            return _thread?.GetHashCode() ?? 0;
        }

        public static bool operator ==(AsyncFlowControl a, AsyncFlowControl b) => a.Equals(b);

        public static bool operator !=(AsyncFlowControl a, AsyncFlowControl b) => !(a == b);
    }

    public interface IThreadPoolWorkItem
    {
        void Execute();
    }


    /// <summary>Represents a timer that can have its due time and period changed.</summary>
    /// <remarks>
    /// Implementations of <see cref="Change"/>, <see cref="IDisposable.Dispose"/>, and <see cref="IAsyncDisposable.DisposeAsync"/>
    /// must all be thread-safe such that the timer instance may be accessed concurrently from multiple threads.
    /// </remarks>
    public interface ITimer : IDisposable, IAsyncDisposable
    {
        /// <summary>Changes the start time and the interval between method invocations for a timer, using <see cref="TimeSpan"/> values to measure time intervals.</summary>
        /// <param name="dueTime">
        /// A <see cref="TimeSpan"/> representing the amount of time to delay before invoking the callback method specified when the <see cref="ITimer"/> was constructed.
        /// Specify <see cref="Timeout.InfiniteTimeSpan"/> to prevent the timer from restarting. Specify <see cref="TimeSpan.Zero"/> to restart the timer immediately.
        /// </param>
        /// <param name="period">
        /// The time interval between invocations of the callback method specified when the Timer was constructed.
        /// Specify <see cref="Timeout.InfiniteTimeSpan"/> to disable periodic signaling.
        /// </param>
        /// <returns><see langword="true"/> if the timer was successfully updated; otherwise, <see langword="false"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The <paramref name="dueTime"/> or <paramref name="period"/> parameter, in milliseconds, is less than -1 or greater than 4294967294.</exception>
        /// <remarks>
        /// It is the responsibility of the implementer of the ITimer interface to ensure thread safety.
        /// </remarks>
        bool Change(TimeSpan dueTime, TimeSpan period);
    }


    public static class LazyInitializer
    {
        public static T EnsureInitialized<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>([NotNull] ref T? target) where T : class
        {
            return Volatile.Read(in target) ?? EnsureInitializedCore(ref target);
        }

        private static T EnsureInitializedCore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>([NotNull] ref T target) where T : class
        {
            try
            {
                Interlocked.CompareExchange(ref target, Activator.CreateInstance<T>(), null);
            }
            catch (MissingMethodException)
            {
                throw new MissingMemberException(SR.Lazy_CreateValue_NoParameterlessCtorForT);
            }
            return target;
        }

        public static T EnsureInitialized<T>([NotNull] ref T? target, Func<T> valueFactory) where T : class
        {
            return Volatile.Read(in target) ?? EnsureInitializedCore(ref target, valueFactory);
        }

        private static T EnsureInitializedCore<T>([NotNull] ref T target, Func<T> valueFactory) where T : class
        {
            T value = valueFactory() ?? throw new InvalidOperationException(SR.Lazy_StaticInit_InvalidOperation);
            Interlocked.CompareExchange(ref target, value, null);
            return target;
        }

        public static T EnsureInitialized<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>([AllowNull] ref T target, ref bool initialized, [NotNullIfNotNull("syncLock")] ref object? syncLock)
        {
            if (Volatile.Read(in initialized))
            {
                return target;
            }
            return EnsureInitializedCore(ref target, ref initialized, ref syncLock);
        }

        private static T EnsureInitializedCore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>([AllowNull] ref T target, ref bool initialized, [NotNull] ref object syncLock)
        {
            lock (EnsureLockInitialized(ref syncLock))
            {
                if (!Volatile.Read(in initialized))
                {
                    try
                    {
                        target = Activator.CreateInstance<T>();
                    }
                    catch (MissingMethodException)
                    {
                        throw new MissingMemberException(SR.Lazy_CreateValue_NoParameterlessCtorForT);
                    }
                    Volatile.Write(ref initialized, value: true);
                }
            }
            return target;
        }

        public static T EnsureInitialized<T>([AllowNull] ref T target, ref bool initialized, [NotNullIfNotNull("syncLock")] ref object? syncLock, Func<T> valueFactory)
        {
            if (Volatile.Read(in initialized))
            {
                return target;
            }
            return EnsureInitializedCore(ref target, ref initialized, ref syncLock, valueFactory);
        }

        private static T EnsureInitializedCore<T>([AllowNull] ref T target, ref bool initialized, [NotNull] ref object syncLock, Func<T> valueFactory)
        {
            lock (EnsureLockInitialized(ref syncLock))
            {
                if (!Volatile.Read(in initialized))
                {
                    target = valueFactory();
                    Volatile.Write(ref initialized, value: true);
                }
            }
            return target;
        }

        public static T EnsureInitialized<T>([NotNull] ref T? target, [NotNullIfNotNull("syncLock")] ref object? syncLock, Func<T> valueFactory) where T : class
        {
            return Volatile.Read(in target) ?? EnsureInitializedCore(ref target, ref syncLock, valueFactory);
        }

        private static T EnsureInitializedCore<T>([NotNull] ref T target, [NotNull] ref object syncLock, Func<T> valueFactory) where T : class
        {
            lock (EnsureLockInitialized(ref syncLock))
            {
                if (Volatile.Read(in target) is null)
                {
                    Volatile.Write(ref target, valueFactory());
                    if (target is null)
                    {
                        throw new InvalidOperationException(SR.Lazy_StaticInit_InvalidOperation);
                    }
                }
            }
            return target;
        }

        private static object EnsureLockInitialized([NotNull] ref object syncLock)
        {
            return syncLock ?? Interlocked.CompareExchange(ref syncLock, new object(), null) ?? syncLock;
        }
    }


    public enum LazyThreadSafetyMode
    {
        None,
        PublicationOnly,
        ExecutionAndPublication
    }


    /// <summary>
    /// Provides a slimmed down version of <see cref="ManualResetEvent"/>.
    /// </summary>
    /// <remarks>
    /// All public and protected members of <see cref="ManualResetEventSlim"/> are thread-safe and may be used
    /// concurrently from multiple threads, with the exception of Dispose, which
    /// must only be used when all other operations on the <see cref="ManualResetEventSlim"/> have
    /// completed, and Reset, which should only be used when no other threads are
    /// accessing the event.
    /// </remarks>
    [DebuggerDisplay("Set = {IsSet}")]
    public class ManualResetEventSlim : IDisposable
    {
        // These are the default spin counts we use on single-proc and MP machines.
        private const int DEFAULT_SPIN_SP = 1;

        private ManualResetEvent? m_eventObj; // A true Win32 event used for waiting.
        private const int SpinCountState_MaxValue = (1 << 11) - 1; // 2047

        // A lock used for waiting and pulsing. Lazily initialized via EnsureLockObjectCreated()
        // We are not using thin/object lock here since our use pattern makes it nearly certain that
        // a thin lock would be inflated even if we use m_lock only once.
        private Lock? m_lock;

        // -- State -- //
        // For a packed word a uint would seem better, but Interlocked.* doesn't support them as uint isn't CLS-compliant.
        private int m_combinedState; // ie a uint. Used for the state items listed below.

        // 1-bit for  signalled state
        private const int SignalledState_BitMask = unchecked((int)0x80000000); // 1000 0000 0000 0000 0000 0000 0000 0000

        // 1-bit for disposed state
        private const int Dispose_BitMask = unchecked((int)0x40000000); // 0100 0000 0000 0000 0000 0000 0000 0000

        // 11-bits for m_spinCount
        private const int SpinCountState_BitMask = unchecked((int)0x3FF80000); // 0011 1111 1111 1000 0000 0000 0000 0000
        private const int SpinCountState_ShiftCount = 19;

        // 19-bits for m_waiters.  This allows support of 512K threads waiting which should be ample
        private const int NumWaitersState_BitMask = unchecked((int)0x0007FFFF); // 0000 0000 0000 0111 1111 1111 1111 1111
        private const int NumWaitersState_MaxValue = (1 << 19) - 1; // 512K-1

        // ----------- //

        /// <summary>
        /// Gets the underlying <see cref="Threading.WaitHandle"/> object for this <see
        /// cref="ManualResetEventSlim"/>.
        /// </summary>
        /// <value>The underlying <see cref="Threading.WaitHandle"/> event object for this <see
        /// cref="ManualResetEventSlim"/>.</value>
        /// <remarks>
        /// Accessing this property forces initialization of an underlying event object if one hasn't
        /// already been created.  To simply wait on this <see cref="ManualResetEventSlim"/>,
        /// the public Wait methods should be preferred.
        /// </remarks>
        public WaitHandle WaitHandle
        {
            get
            {
                ObjectDisposedException.ThrowIf(IsDisposed, this);
                if (m_eventObj == null)
                {
                    // Lazily initialize the event object if needed.
                    LazyInitializeEvent();
                }

                return m_eventObj;
            }
        }

        /// <summary>
        /// Gets whether the event is set.
        /// </summary>
        /// <value>true if the event has is set; otherwise, false.</value>
        public bool IsSet
        {
            get => IsSetCore;
            private set => IsSetCore = value;
        }

        /// <summary>
        /// Gets the number of spin waits that will be occur before falling back to a true wait.
        /// </summary>
        public int SpinCount
        {
            get => SpinCountCore;
            private set => SpinCountCore = value;
        }

        //-----------------------------------------------------------------------------------
        // Constructs a new event, optionally specifying the initial state and spin count.
        // The defaults are that the event is unsignaled and some reasonable default spin.
        //

        /// <summary>
        /// Initializes a new instance of the <see cref="ManualResetEventSlim"/>
        /// class with an initial state of nonsignaled.
        /// </summary>
        public ManualResetEventSlim()
            : this(false)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ManualResetEventSlim"/>
        /// class with a boolean value indicating whether to set the initial state to signaled.
        /// </summary>
        /// <param name="initialState">true to set the initial state signaled; false to set the initial state
        /// to nonsignaled.</param>
        public ManualResetEventSlim(bool initialState)
        {
            // Specify the default spin count, and use default spin if we're
            // on a multi-processor machine. Otherwise, we won't.
            Initialize(initialState, SpinWait.SpinCountForSpinBeforeWait);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ManualResetEventSlim"/>
        /// class with a Boolean value indicating whether to set the initial state to signaled and a specified
        /// spin count.
        /// </summary>
        /// <param name="initialState">true to set the initial state to signaled; false to set the initial state
        /// to nonsignaled.</param>
        /// <param name="spinCount">The number of spin waits that will occur before falling back to a true
        /// wait.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="spinCount"/> is less than
        /// 0 or greater than the maximum allowed value.</exception>
        public ManualResetEventSlim(bool initialState, int spinCount)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(spinCount);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(spinCount, SpinCountState_MaxValue);

            // We will suppress default spin  because the user specified a count.
            Initialize(initialState, spinCount);
        }

        /// <summary>
        /// Sets the state of the event to signaled, which allows one or more threads waiting on the event to
        /// proceed.
        /// </summary>
        public void Set()
        {
            Set(false);
        }

        /// <summary>
        /// Sets the state of the event to nonsignaled, which causes threads to block.
        /// </summary>
        /// <remarks>
        /// Unlike most of the members of <see cref="ManualResetEventSlim"/>, <see cref="Reset()"/> is not
        /// thread-safe and may not be used concurrently with other members of this instance.
        /// </remarks>
        public void Reset()

        {
            ResetCore();
        }

        /// <summary>
        /// Blocks the current thread until the current <see cref="ManualResetEventSlim"/> is set.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The maximum number of waiters has been exceeded.
        /// </exception>
        /// <remarks>
        /// The caller of this method blocks indefinitely until the current instance is set. The caller will
        /// return immediately if the event is currently in a set state.
        /// </remarks>
#if !FEATURE_WASM_MANAGED_THREADS
        [UnsupportedOSPlatform("browser")]
#endif
        public void Wait()
        {
            Wait(Timeout.Infinite, CancellationToken.None);
        }

        /// <summary>
        /// Blocks the current thread until the current <see cref="ManualResetEventSlim"/> receives a signal,
        /// while observing a <see cref="CancellationToken"/>.
        /// </summary>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to
        /// observe.</param>
        /// <exception cref="InvalidOperationException">
        /// The maximum number of waiters has been exceeded.
        /// </exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was
        /// canceled.</exception>
        /// <remarks>
        /// The caller of this method blocks indefinitely until the current instance is set. The caller will
        /// return immediately if the event is currently in a set state.
        /// </remarks>
#if !FEATURE_WASM_MANAGED_THREADS
        [UnsupportedOSPlatform("browser")]
#endif
        public void Wait(CancellationToken cancellationToken)
        {
            Wait(Timeout.Infinite, cancellationToken);
        }

        /// <summary>
        /// Blocks the current thread until the current <see cref="ManualResetEventSlim"/> is set, using a
        /// <see cref="TimeSpan"/> to measure the time interval.
        /// </summary>
        /// <param name="timeout">A <see cref="TimeSpan"/> that represents the number of milliseconds
        /// to wait, or a <see cref="TimeSpan"/> that represents -1 milliseconds to wait indefinitely.
        /// </param>
        /// <returns>true if the <see cref="ManualResetEventSlim"/> was set; otherwise,
        /// false.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is a negative
        /// number other than -1 milliseconds, which represents an infinite time-out -or- timeout is greater
        /// than <see cref="int.MaxValue"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// The maximum number of waiters has been exceeded.
        /// </exception>
#if !FEATURE_WASM_MANAGED_THREADS
        [UnsupportedOSPlatform("browser")]
#endif
        public bool Wait(TimeSpan timeout)
        {
            long totalMilliseconds = (long)timeout.TotalMilliseconds;

            ArgumentOutOfRangeException.ThrowIfLessThan(totalMilliseconds, -1, nameof(timeout));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(totalMilliseconds, int.MaxValue, nameof(timeout));

            return Wait((int)totalMilliseconds, CancellationToken.None);
        }

        /// <summary>
        /// Blocks the current thread until the current <see cref="ManualResetEventSlim"/> is set, using a
        /// <see cref="TimeSpan"/> to measure the time interval, while observing a <see
        /// cref="CancellationToken"/>.
        /// </summary>
        /// <param name="timeout">A <see cref="TimeSpan"/> that represents the number of milliseconds
        /// to wait, or a <see cref="TimeSpan"/> that represents -1 milliseconds to wait indefinitely.
        /// </param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to
        /// observe.</param>
        /// <returns>true if the <see cref="ManualResetEventSlim"/> was set; otherwise,
        /// false.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is a negative
        /// number other than -1 milliseconds, which represents an infinite time-out -or- timeout is greater
        /// than <see cref="int.MaxValue"/>.</exception>
        /// <exception cref="OperationCanceledException"><paramref
        /// name="cancellationToken"/> was canceled.</exception>
        /// <exception cref="InvalidOperationException">
        /// The maximum number of waiters has been exceeded.
        /// </exception>
#if !FEATURE_WASM_MANAGED_THREADS
        [UnsupportedOSPlatform("browser")]
#endif
        public bool Wait(TimeSpan timeout, CancellationToken cancellationToken)
        {
            long totalMilliseconds = (long)timeout.TotalMilliseconds;

            ArgumentOutOfRangeException.ThrowIfLessThan(totalMilliseconds, -1, nameof(timeout));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(totalMilliseconds, int.MaxValue, nameof(timeout));

            return Wait((int)totalMilliseconds, cancellationToken);
        }

        /// <summary>
        /// Blocks the current thread until the current <see cref="ManualResetEventSlim"/> is set, using a
        /// 32-bit signed integer to measure the time interval.
        /// </summary>
        /// <param name="millisecondsTimeout">The number of milliseconds to wait, or <see
        /// cref="Timeout.Infinite"/>(-1) to wait indefinitely.</param>
        /// <returns>true if the <see cref="ManualResetEventSlim"/> was set; otherwise,
        /// false.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="millisecondsTimeout"/> is a
        /// negative number other than -1, which represents an infinite time-out.</exception>
        /// <exception cref="InvalidOperationException">
        /// The maximum number of waiters has been exceeded.
        /// </exception>
#if !FEATURE_WASM_MANAGED_THREADS
        [UnsupportedOSPlatform("browser")]
#endif
        public bool Wait(int millisecondsTimeout)
        {
            return Wait(millisecondsTimeout, CancellationToken.None);
        }

        /// <summary>
        /// Blocks the current thread until the current <see cref="ManualResetEventSlim"/> is set, using a
        /// 32-bit signed integer to measure the time interval, while observing a <see
        /// cref="CancellationToken"/>.
        /// </summary>
        /// <param name="millisecondsTimeout">The number of milliseconds to wait, or <see
        /// cref="Timeout.Infinite"/>(-1) to wait indefinitely.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to
        /// observe.</param>
        /// <returns>true if the <see cref="ManualResetEventSlim"/> was set; otherwise,
        /// false.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="millisecondsTimeout"/> is a
        /// negative number other than -1, which represents an infinite time-out.</exception>
        /// <exception cref="InvalidOperationException">
        /// The maximum number of waiters has been exceeded.
        /// </exception>
        /// <exception cref="OperationCanceledException"><paramref
        /// name="cancellationToken"/> was canceled.</exception>
#if !FEATURE_WASM_MANAGED_THREADS
        [UnsupportedOSPlatform("browser")]
#endif
        public bool Wait(int millisecondsTimeout, CancellationToken cancellationToken)

        {
            return WaitCore(millisecondsTimeout, cancellationToken);
        }

        /// <summary>
        /// Releases all resources used by the current instance of <see cref="ManualResetEventSlim"/>.
        /// </summary>
        /// <remarks>
        /// Unlike most of the members of <see cref="ManualResetEventSlim"/>, <see cref="Dispose()"/> is not
        /// thread-safe and may not be used concurrently with other members of this instance.
        /// </remarks>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// When overridden in a derived class, releases the unmanaged resources used by the
        /// <see cref="ManualResetEventSlim"/>, and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources;
        /// false to release only unmanaged resources.</param>
        /// <remarks>
        /// Unlike most of the members of <see cref="ManualResetEventSlim"/>, <see cref="Dispose(bool)"/> is not
        /// thread-safe and may not be used concurrently with other members of this instance.
        /// </remarks>
        protected virtual void Dispose(bool disposing)

        {
            DisposeCore(disposing);
        }

        private bool IsSetCore
        {
            get
            {
                // Volatile to make sure checking IsSet is not optimized away or reordered
                // when called as a public API.
                return Volatile.Read(ref m_combinedState) < 0;
            }
            set
            {
                if (value)
                {
                    Interlocked.Or(ref m_combinedState, SignalledState_BitMask);
                }
                else
                {
                    Interlocked.And(ref m_combinedState, ~SignalledState_BitMask);
                }
            }
        }

        private int SpinCountCore
        {
            get => ExtractStatePortionAndShiftRight(m_combinedState, SpinCountState_BitMask, SpinCountState_ShiftCount);
            set
            {
                // Don't worry about thread safety because it's set one time from the constructor
                m_combinedState = (m_combinedState & ~SpinCountState_BitMask) | (value << SpinCountState_ShiftCount);
            }
        }

        private void InterlockedIncrementWaiters()
        {
            int currentState = m_combinedState;
            while (true)
            {
                // it is possible for the max number of waiters to be exceeded via user-code, hence we use a real exception here.
                if ((currentState & NumWaitersState_BitMask) >= NumWaitersState_MaxValue)
                    throw new InvalidOperationException(SR.Format(SR.ManualResetEventSlim_ctor_TooManyWaiters, NumWaitersState_MaxValue));

                // The waiters count has room for one more, so adding 1 to the full state
                // cannot carry out of NumWaitersState_BitMask.
                int oldState = Interlocked.CompareExchange(ref m_combinedState, currentState + 1, currentState);
                if (oldState == currentState)
                    return;

                // Highly unlikely contention, since we increment/decrement while holding a lock, just try again.
                currentState = oldState;
            }
        }

        private void InterlockedDecrementWaiters()
        {
            Interlocked.Decrement(ref m_combinedState);
        }

        /// <summary>
        /// Initializes the internal state of the event.
        /// </summary>
        /// <param name="initialState">Whether the event is set initially or not.</param>
        /// <param name="spinCount">The spin count that decides when the event will block.</param>
        private void Initialize(bool initialState, int spinCount)
        {
            m_combinedState = initialState ? SignalledState_BitMask : 0;

            SpinCount = Environment.IsSingleProcessor ? DEFAULT_SPIN_SP : spinCount;
        }

        /// <summary>
        /// Helper to ensure the lock object is created before first use.
        /// </summary>
        [MemberNotNull(nameof(m_lock))]
        private void EnsureLockObjectCreated()
        {
            if (m_lock is null)
            {
                Interlocked.CompareExchange(ref m_lock, new Lock(), null); // failure is benign. Someone else set the value.
            }
        }

        /// <summary>
        /// This method lazily initializes the public wait object. It uses CAS to guarantee that
        /// many threads racing to call this at once don't result in more than one event
        /// being stored and used. The event will be signaled or unsignaled depending on
        /// the state of the thin-event itself, with synchronization taken into account.
        /// </summary>
        private void LazyInitializeEvent()
        {
            bool preInitializeIsSet = IsSet;
            ManualResetEvent newEventObj = new ManualResetEvent(preInitializeIsSet);

            // We have to CAS this in case we are racing with another thread. We must
            // guarantee only one event is actually stored in this field.
            if (Interlocked.CompareExchange(ref m_eventObj, newEventObj, null) != null)
            {
                // Someone else set the value due to a race condition. Destroy the garbage event.
                newEventObj.Dispose();
            }
            else
            {
                // Now that the event is published, verify that the state hasn't changed since
                // we snapped the preInitializeState. Another thread could have done that
                // between our initial observation above and here. The barrier incurred from
                // the CAS above prevents this read
                // from moving earlier and being collapsed with our original one.
                bool currentIsSet = IsSet;
                if (currentIsSet != preInitializeIsSet)
                {

                    // We saw it as unsignaled, but it has since become set.
                    lock (newEventObj)
                    {
                        // If our event hasn't already been disposed of, we must set it.
                        if (m_eventObj == newEventObj)
                        {
                            newEventObj.Set();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Private helper to actually perform the Set.
        /// </summary>
        /// <param name="duringCancellation">Indicates whether we are calling Set() during cancellation.</param>
        /// <exception cref="OperationCanceledException">The object has been canceled.</exception>
        private void Set(bool duringCancellation)
        {
            // We need to ensure that the state change happens before we read the m_eventObj.
            // Interlocked guarantees that.
            long origState = Interlocked.Or(ref m_combinedState, SignalledState_BitMask);

            // If there were waiting threads when we flipped the state, we need to pulse them.
            if ((origState & NumWaitersState_BitMask) != 0)
            {
                using (m_lock.EnterScope())
                {
                    m_lock.PulseAll();
                }
            }

            ManualResetEvent? eventObj = m_eventObj;

            // Design-decision: do not set the event if we are in cancellation -> better to deadlock than to wake up waiters incorrectly
            // It would be preferable to wake up the event and have it throw OCE. This requires MRE to implement cancellation logic

            if (eventObj != null && !duringCancellation)
            {
                // We must surround this call to Set in a lock.  The reason is fairly subtle.
                // Sometimes a thread will issue a Wait and wake up after we have set m_state,
                // but before we have gotten around to setting m_eventObj (just below). That's
                // because Wait first checks m_state and will only access the event if absolutely
                // necessary.  However, the coding pattern { event.Wait(); event.Dispose() } is
                // quite common, and we must support it.  If the waiter woke up and disposed of
                // the event object before the setter has finished, however, we would try to set a
                // now-disposed Win32 event. Crash! To deal with this race condition, we use a lock to
                // protect access to the event object when setting and disposing of it.  We also
                // double-check that the event has not become null in the meantime when in the lock.

                lock (eventObj)
                {
                    // If somebody is waiting, we must set the event.
                    m_eventObj?.Set();
                }
            }
        }

        private void ResetCore()
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);

            // If there's an event, reset it.
            m_eventObj?.Reset();

            // There is a race condition here. If another thread Sets the event, we will get into a state
            // where m_state will be unsignaled, yet the Win32 event object will have been signaled.
            // This could cause waiting threads to wake up even though the event is in an
            // unsignaled state. This is fine -- those that are calling Reset concurrently are
            // responsible for doing "the right thing" -- e.g. rechecking the condition and
            // resetting the event manually.

            // And finally set our state back to unsignaled.
            IsSet = false;
        }

#if !FEATURE_WASM_MANAGED_THREADS
        [UnsupportedOSPlatform("browser")]
#endif
        private bool WaitCore(int millisecondsTimeout, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            cancellationToken.ThrowIfCancellationRequested(); // an early convenience check

            ArgumentOutOfRangeException.ThrowIfLessThan(millisecondsTimeout, -1);

            if (!IsSet)
            {
                if (millisecondsTimeout == 0)
                {
                    // For 0-timeouts, we just return immediately.
                    return false;
                }

                RuntimeFeature.ThrowIfBlockingIsNotSupported(millisecondsTimeout);

                // We spin briefly before falling back to allocating and/or waiting on a true event.
                long startTime = 0;
                int realMillisecondsTimeout = millisecondsTimeout; // this will be adjusted if necessary.

                if (millisecondsTimeout != Timeout.Infinite)
                {
                    // We will account for time spent spinning, so that we can decrement it from our
                    // timeout.  In most cases the time spent in this section will be negligible.  But
                    // we can't discount the possibility of our thread being switched out for a lengthy
                    // period of time.  The timeout adjustments only take effect when and if we actually
                    // decide to block in the kernel below.

                    startTime = Environment.TickCount64;
                }

                // Spin
                int spinCount = SpinCount;
                SpinWait spinner = default;
                while (spinner.Count < spinCount)
                {
                    spinner.SpinOnce(sleep1Threshold: -1);

                    if (IsSet)
                    {
                        return true;
                    }

                    if (spinner.Count >= 100 && spinner.Count % 10 == 0) // check the cancellation token if the user passed a very large spin count
                        cancellationToken.ThrowIfCancellationRequested();
                }

                // Now enter the lock and wait. Must be created before registering the cancellation callback,
                // which will try to take this lock.
                EnsureLockObjectCreated();

                // We must register and unregister the token outside of the lock, to avoid deadlocks.
                using (cancellationToken.UnsafeRegister(s_cancellationTokenCallback, this))
                {
                    // Loop to cope with spurious wakeups from other waits being canceled
                    while (!IsSet)
                    {
                        // If our token was canceled, we must throw and exit.
                        cancellationToken.ThrowIfCancellationRequested();

                        // update timeout (delays in wait commencement are due to spinning and/or spurious wakeups from other waits being canceled)
                        if (startTime != 0)
                        {
                            // TimeoutHelper.UpdateTimeOut returns a long but the value is capped as millisecondsTimeout is an int.
                            realMillisecondsTimeout = (int)TimeoutHelper.UpdateTimeOut(startTime, millisecondsTimeout);
                            if (realMillisecondsTimeout <= 0)
                                return false;
                        }

                        using (m_lock.EnterScope())
                        {
                            // There is a race condition that Set will fail to see that there are waiters as Set does not take the lock,
                            // so after updating waiters, we must check IsSet again.
                            // Also, we must ensure there cannot be any reordering of the assignment to Waiters and the
                            // read from IsSet.  This is guaranteed as InterlockedIncrementWaiters is a full memory barrier.
                            // If we see IsSet=false, then we are guaranteed that Set() will see that we are
                            // waiting and will pulse the monitor correctly.

                            InterlockedIncrementWaiters();

                            if (IsSet) // This check must occur after updating Waiters.
                            {
                                InterlockedDecrementWaiters(); // revert the increment.
                                return true;
                            }

                            // Now finally perform the wait.
                            try
                            {
                                // ** the actual wait **
                                if (!m_lock.Wait(realMillisecondsTimeout))
                                    return false; // return immediately if the timeout has expired.
                            }
                            finally
                            {
                                // Clean up: we're done waiting.
                                InterlockedDecrementWaiters();
                            }
                            // Now just loop back around, and the right thing will happen.  Either:
                            //     1. We had a spurious wake-up due to some other wait being canceled via a different cancellationToken (rewait)
                            // or  2. the wait was successful. (the loop will break)
                        }
                    }
                }
            } // automatically disposes (and unregisters) the callback

            return true; // done. The wait was satisfied.
        }

        private void DisposeCore(bool disposing)
        {
            if ((m_combinedState & Dispose_BitMask) != 0)
                return; // already disposed

            m_combinedState |= Dispose_BitMask; // set the dispose bit
            if (disposing)
            {
                // We will dispose of the event object.  We do this under a lock to protect
                // against the race condition outlined in the Set method above.
                ManualResetEvent? eventObj = m_eventObj;
                if (eventObj != null)
                {
                    lock (eventObj)
                    {
                        eventObj.Dispose();
                        m_eventObj = null;
                    }
                }
            }
        }

        private bool IsDisposed => (m_combinedState & Dispose_BitMask) != 0;

        /// <summary>
        /// Private helper method to wake up waiters when a cancellationToken gets canceled.
        /// </summary>
        private static readonly Action<object?> s_cancellationTokenCallback = new Action<object?>(CancellationTokenCallback);
        private static void CancellationTokenCallback(object? obj)
        {
            ManualResetEventSlim mre = (ManualResetEventSlim)obj;
            using (mre.m_lock.EnterScope())
            {
                mre.m_lock.PulseAll(); // awaken all waiters
            }
        }

        /// <summary>
        /// Private helper method - performs Mask and shift, particular helpful to extract a field from a packed word.
        /// eg ExtractStatePortionAndShiftRight(0x12345678, 0xFF000000, 24) => 0x12, ie extracting the top 8-bits as a simple integer
        ///
        /// ?? is there a common place to put this rather than being private to MRES?
        /// </summary>
        /// <param name="state"></param>
        /// <param name="mask"></param>
        /// <param name="rightBitShiftCount"></param>
        /// <returns></returns>
        private static int ExtractStatePortionAndShiftRight(int state, int mask, int rightBitShiftCount)
        {
            // convert to uint before shifting so that right-shift does not replicate the sign-bit,
            // then convert back to int.
            return unchecked((int)(((uint)(state & mask)) >> rightBitShiftCount));
        }
    }


    internal sealed class QueueUserWorkItemCallback : QueueUserWorkItemCallbackBase
    {
        private WaitCallback _callback;

        private readonly object _state;

        private readonly ExecutionContext _context;

        private static readonly Action<QueueUserWorkItemCallback> s_executionContextShim = delegate (QueueUserWorkItemCallback quwi)
        {
            WaitCallback callback = quwi._callback;
            quwi._callback = null;
            callback(quwi._state);
        };

        internal QueueUserWorkItemCallback(WaitCallback callback, object state, ExecutionContext context)
        {
            _callback = callback;
            _state = state;
            _context = context;
        }

        public override void Execute()
        {
            base.Execute();
            ExecutionContext.RunForThreadPoolUnsafe(_context, s_executionContextShim, this);
        }
    }

    internal sealed class QueueUserWorkItemCallback<TState> : QueueUserWorkItemCallbackBase
    {
        private Action<TState> _callback;

        private readonly TState _state;

        private readonly ExecutionContext _context;

        internal QueueUserWorkItemCallback(Action<TState> callback, TState state, ExecutionContext context)
        {
            _callback = callback;
            _state = state;
            _context = context;
        }

        public override void Execute()
        {
            base.Execute();
            Action<TState> callback = _callback;
            _callback = null;
            ExecutionContext.RunForThreadPoolUnsafe(_context, callback, in _state);
        }
    }


    internal abstract class QueueUserWorkItemCallbackBase : IThreadPoolWorkItem
    {
        public virtual void Execute()
        {
        }
    }


    internal sealed class QueueUserWorkItemCallbackDefaultContext : QueueUserWorkItemCallbackBase
    {
        private WaitCallback _callback;

        private readonly object _state;

        internal QueueUserWorkItemCallbackDefaultContext(WaitCallback callback, object state)
        {
            _callback = callback;
            _state = state;
        }

        public override void Execute()
        {
            base.Execute();
            WaitCallback callback = _callback;
            _callback = null;
            callback(_state);
        }
    }

    internal sealed class QueueUserWorkItemCallbackDefaultContext<TState> : QueueUserWorkItemCallbackBase
    {
        private Action<TState> _callback;

        private readonly TState _state;

        internal QueueUserWorkItemCallbackDefaultContext(Action<TState> callback, TState state)
        {
            _callback = callback;
            _state = state;
        }

        public override void Execute()
        {
            base.Execute();
            Action<TState> callback = _callback;
            _callback = null;
            callback(_state);
        }
    }


    public delegate void SendOrPostCallback(object? state);


    /// <summary>
    /// Limits the number of threads that can access a resource or pool of resources concurrently.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="SemaphoreSlim"/> provides a lightweight semaphore class that doesn't
    /// use Windows kernel semaphores.
    /// </para>
    /// <para>
    /// All public and protected members of <see cref="SemaphoreSlim"/> are thread-safe and may be used
    /// concurrently from multiple threads, with the exception of Dispose, which
    /// must only be used when all other operations on the <see cref="SemaphoreSlim"/> have
    /// completed.
    /// </para>
    /// </remarks>
    [DebuggerDisplay("Current Count = {m_currentCount}")]
    public class SemaphoreSlim : IDisposable
    {
        // The maximum semaphore value, it is initialized to Int.MaxValue if the client didn't specify it. it is used
        // to check if the count exceeded the maximum value or not.
        private readonly int m_maxCount;

        // Head of list representing asynchronous waits on the semaphore.
        private TaskNode? m_asyncHead;

        // Tail of list representing asynchronous waits on the semaphore.
        private TaskNode? m_asyncTail;

        // No maximum constant
        private const int NO_MAXIMUM = int.MaxValue;

        // Task in a linked list of asynchronous waiters
        private sealed class TaskNode : Task<bool>
        {
            internal TaskNode? Prev, Next;
            internal TaskNode() : base((object?)null, TaskCreationOptions.RunContinuationsAsynchronously) { }
        }

        private volatile int m_currentCount;

        // The number of synchronously waiting threads, it is set to zero in the constructor and increments before blocking the
        // threading and decrements it back after that. It is used as flag for the release call to know if there are
        // waiting threads in the monitor or not.
        private int m_waitCount;

        /// <summary>
        /// This is used to help prevent waking more waiters than necessary. It's not perfect and sometimes more waiters than
        /// necessary may still be woken, see <see cref="WaitUntilCountOrTimeout"/>.
        /// </summary>
        private int m_countOfWaitersPulsedToWake;

        // Object used to synchronize access to state on the instance.  The contained
        // Boolean value indicates whether the instance has been disposed.
        private readonly StrongBox<bool> m_lockObjAndDisposed;

        // Act as the semaphore wait handle, it's lazily initialized if needed, the first WaitHandle call initialize it
        // and wait an release sets and resets it respectively as long as it is not null
        private volatile ManualResetEvent? m_waitHandle;

        /// <summary>
        /// Private helper method to wake up waiters when a cancellationToken gets canceled.
        /// </summary>
        private static readonly Action<object?> s_cancellationTokenCanceledEventHandler = new Action<object?>(CancellationTokenCanceledEventHandler);

        public int CurrentCount => m_currentCount;

        public WaitHandle AvailableWaitHandle
        {
            get
            {
                CheckDispose();
                if (m_waitHandle is null)
                {
                    lock (m_lockObjAndDisposed)
                    {
                        if (m_waitHandle is null)
                        {
                            m_waitHandle = new ManualResetEvent(m_currentCount != 0);
                        }
                    }
                }
                return m_waitHandle;
            }
        }

        public SemaphoreSlim(int initialCount)
            : this(initialCount, int.MaxValue)
        {
        }

        public SemaphoreSlim(int initialCount, int maxCount)
        {
            if (initialCount < 0 || initialCount > maxCount)
            {
                throw new ArgumentOutOfRangeException(nameof(initialCount), initialCount, SR.SemaphoreSlim_ctor_InitialCountWrong);
            }
            if (maxCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxCount), maxCount, SR.SemaphoreSlim_ctor_MaxCountWrong);
            }
            m_maxCount = maxCount;
            m_currentCount = initialCount;
            m_lockObjAndDisposed = new StrongBox<bool>();
        }

        public void Wait()
        {
            WaitCore(-1L, CancellationToken.None);
        }

        public void Wait(CancellationToken cancellationToken)
        {
            WaitCore(-1L, cancellationToken);
        }

        public bool Wait(TimeSpan timeout)
        {
            long num = (long)timeout.TotalMilliseconds;
            if (num < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), timeout, SR.SemaphoreSlim_Wait_TimeSpanTimeoutWrong);
            }
            return WaitCore(num, CancellationToken.None);
        }

        public bool Wait(TimeSpan timeout, CancellationToken cancellationToken)
        {
            long num = (long)timeout.TotalMilliseconds;
            if (num < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), timeout, SR.SemaphoreSlim_Wait_TimeSpanTimeoutWrong);
            }
            return WaitCore(num, cancellationToken);
        }

        public bool Wait(int millisecondsTimeout)
        {
            if (millisecondsTimeout < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(millisecondsTimeout), millisecondsTimeout, SR.SemaphoreSlim_Wait_TimeoutWrong);
            }
            return WaitCore(millisecondsTimeout, CancellationToken.None);
        }

        public bool Wait(int millisecondsTimeout, CancellationToken cancellationToken)
        {
            if (millisecondsTimeout < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(millisecondsTimeout), millisecondsTimeout, SR.SemaphoreSlim_Wait_TimeoutWrong);
            }
            return WaitCore(millisecondsTimeout, cancellationToken);
        }

        private bool WaitCore(long millisecondsTimeout, CancellationToken cancellationToken)
        {
            CheckDispose();
            cancellationToken.ThrowIfCancellationRequested();
            if (millisecondsTimeout == 0L && m_currentCount == 0)
            {
                return false;
            }
            long startTime = 0L;
            if (millisecondsTimeout != -1 && millisecondsTimeout > 0)
            {
                startTime = Environment.TickCount64;
            }
            bool flag = false;
            Task<bool> task = null;
            bool lockTaken = false;
            CancellationTokenRegistration cancellationTokenRegistration = cancellationToken.UnsafeRegister(s_cancellationTokenCanceledEventHandler, this);
            try
            {
                if (m_currentCount == 0)
                {
                    int num = SpinWait.SpinCountForSpinBeforeWait * 4;
                    SpinWait spinWait = default(SpinWait);
                    while (spinWait.Count < num)
                    {
                        spinWait.SpinOnce(-1);
                        if (m_currentCount != 0)
                        {
                            break;
                        }
                    }
                }
                Monitor.Enter(m_lockObjAndDisposed, ref lockTaken);
                m_waitCount++;
                if (m_asyncHead is not null)
                {
                    task = WaitAsyncCore(millisecondsTimeout, cancellationToken);
                }
                else
                {
                    OperationCanceledException ex = null;
                    if (m_currentCount == 0)
                    {
                        if (millisecondsTimeout == 0L)
                        {
                            return false;
                        }
                        try
                        {
                            flag = WaitUntilCountOrTimeout(millisecondsTimeout, startTime, cancellationToken);
                        }
                        catch (OperationCanceledException ex2)
                        {
                            ex = ex2;
                        }
                    }
                    if (m_currentCount > 0)
                    {
                        flag = true;
                        m_currentCount--;
                    }
                    else if (ex is not null)
                    {
                        throw ex;
                    }
                    if (m_waitHandle is not null && m_currentCount == 0)
                    {
                        m_waitHandle.Reset();
                    }
                }
            }
            finally
            {
                if (lockTaken)
                {
                    m_waitCount--;
                    Monitor.Exit(m_lockObjAndDisposed);
                }
                cancellationTokenRegistration.Dispose();
            }
            return task?.GetAwaiter().GetResult() ?? flag;
        }

        private bool WaitUntilCountOrTimeout(long millisecondsTimeout, long startTime, CancellationToken cancellationToken)
        {
            int millisecondsTimeout2 = -1;
            while (m_currentCount == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool flag = false;
                if (millisecondsTimeout != -1)
                {
                    long num = TimeoutHelper.UpdateTimeOut(startTime, millisecondsTimeout);
                    if (num <= 0)
                    {
                        return false;
                    }
                    if (num <= int.MaxValue)
                    {
                        millisecondsTimeout2 = (int)num;
                    }
                    else
                    {
                        flag = true;
                        millisecondsTimeout2 = int.MaxValue;
                    }
                }
                bool flag2 = Monitor.Wait(m_lockObjAndDisposed, millisecondsTimeout2);
                if (m_countOfWaitersPulsedToWake != 0)
                {
                    m_countOfWaitersPulsedToWake--;
                }
                if (!flag && !flag2)
                {
                    return false;
                }
            }
            return true;
        }

        public Task WaitAsync()
        {
            return WaitAsyncCore(-1L, default(CancellationToken));
        }

        public Task WaitAsync(CancellationToken cancellationToken)
        {
            return WaitAsyncCore(-1L, cancellationToken);
        }

        public Task<bool> WaitAsync(int millisecondsTimeout)
        {
            if (millisecondsTimeout < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(millisecondsTimeout), millisecondsTimeout, SR.SemaphoreSlim_Wait_TimeoutWrong);
            }
            return WaitAsyncCore(millisecondsTimeout, default(CancellationToken));
        }

        public Task<bool> WaitAsync(TimeSpan timeout)
        {
            long num = (long)timeout.TotalMilliseconds;
            if (num < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), timeout, SR.SemaphoreSlim_Wait_TimeSpanTimeoutWrong);
            }
            return WaitAsyncCore(num, default(CancellationToken));
        }

        public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            long num = (long)timeout.TotalMilliseconds;
            if (num < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), timeout, SR.SemaphoreSlim_Wait_TimeSpanTimeoutWrong);
            }
            return WaitAsyncCore(num, cancellationToken);
        }

        public Task<bool> WaitAsync(int millisecondsTimeout, CancellationToken cancellationToken)
        {
            if (millisecondsTimeout < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(millisecondsTimeout), millisecondsTimeout, SR.SemaphoreSlim_Wait_TimeoutWrong);
            }
            return WaitAsyncCore(millisecondsTimeout, cancellationToken);
        }

        private Task<bool> WaitAsyncCore(long millisecondsTimeout, CancellationToken cancellationToken)
        {
            CheckDispose();
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<bool>(cancellationToken);
            }
            lock (m_lockObjAndDisposed)
            {
                if (m_currentCount > 0)
                {
                    m_currentCount--;
                    if (m_waitHandle is not null && m_currentCount == 0)
                    {
                        m_waitHandle.Reset();
                    }
                    return Task.FromResult(result: true);
                }
                if (millisecondsTimeout == 0L)
                {
                    return Task.FromResult(result: false);
                }
                TaskNode taskNode = CreateAndAddAsyncWaiter();
                return (millisecondsTimeout == -1 && !cancellationToken.CanBeCanceled) ? taskNode : WaitUntilCountOrTimeoutAsync(taskNode, millisecondsTimeout, cancellationToken);
            }
        }

        private TaskNode CreateAndAddAsyncWaiter()
        {
            TaskNode taskNode = new TaskNode();
            if (m_asyncHead is null)
            {
                m_asyncHead = taskNode;
                m_asyncTail = taskNode;
            }
            else
            {
                m_asyncTail.Next = taskNode;
                taskNode.Prev = m_asyncTail;
                m_asyncTail = taskNode;
            }
            return taskNode;
        }

        private bool RemoveAsyncWaiter(TaskNode task)
        {
            bool result = m_asyncHead == task || task.Prev is not null;
            TaskNode next = task.Next;
            if (next is not null)
            {
                next.Prev = task.Prev;
            }
            TaskNode prev = task.Prev;
            if (prev is not null)
            {
                prev.Next = task.Next;
            }
            if (m_asyncHead == task)
            {
                m_asyncHead = task.Next;
            }
            if (m_asyncTail == task)
            {
                m_asyncTail = task.Prev;
            }
            task.Next = (task.Prev = null);
            return result;
        }

        private async Task<bool> WaitUntilCountOrTimeoutAsync(TaskNode asyncWaiter, long millisecondsTimeout, CancellationToken cancellationToken)
        {
            await ((Task)asyncWaiter.WaitAsync(TimeSpan.FromMilliseconds(millisecondsTimeout), cancellationToken)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            if (cancellationToken.IsCancellationRequested)
            {
                await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
            }
            if (asyncWaiter.IsCompleted)
            {
                return true;
            }
            lock (m_lockObjAndDisposed)
            {
                if (RemoveAsyncWaiter(asyncWaiter))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return false;
                }
            }
            return await asyncWaiter.ConfigureAwait(continueOnCapturedContext: false);
        }

        public int Release()
        {
            return Release(1);
        }

        public int Release(int releaseCount)
        {
            CheckDispose();
            if (releaseCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(releaseCount), releaseCount, SR.SemaphoreSlim_Release_CountWrong);
            }
            int num;
            lock (m_lockObjAndDisposed)
            {
                int currentCount = m_currentCount;
                num = currentCount;
                if (m_maxCount - currentCount < releaseCount)
                {
                    throw new SemaphoreFullException();
                }
                currentCount += releaseCount;
                int waitCount = m_waitCount;
                int num2 = Math.Min(currentCount, waitCount) - m_countOfWaitersPulsedToWake;
                if (num2 > 0)
                {
                    if (num2 > releaseCount)
                    {
                        num2 = releaseCount;
                    }
                    m_countOfWaitersPulsedToWake += num2;
                    for (int i = 0; i < num2; i++)
                    {
                        Monitor.Pulse(m_lockObjAndDisposed);
                    }
                }
                if (m_asyncHead is not null)
                {
                    int num3 = currentCount - waitCount;
                    while (num3 > 0 && m_asyncHead is not null)
                    {
                        currentCount--;
                        num3--;
                        TaskNode asyncHead = m_asyncHead;
                        RemoveAsyncWaiter(asyncHead);
                        asyncHead.TrySetResult(result: true);
                    }
                }
                m_currentCount = currentCount;
                if (m_waitHandle is not null && num == 0 && currentCount > 0)
                {
                    m_waitHandle.Set();
                }
            }
            return num;
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                WaitHandle waitHandle = m_waitHandle;
                if (waitHandle is not null)
                {
                    waitHandle.Dispose();
                    m_waitHandle = null;
                }
                m_lockObjAndDisposed.Value = true;
                m_asyncHead = null;
                m_asyncTail = null;
            }
        }

        private static void CancellationTokenCanceledEventHandler(object obj)
        {
            SemaphoreSlim semaphoreSlim = (SemaphoreSlim)obj;
            lock (semaphoreSlim.m_lockObjAndDisposed)
            {
                Monitor.PulseAll(semaphoreSlim.m_lockObjAndDisposed);
            }
        }

        private void CheckDispose()
        {
            ObjectDisposedException.ThrowIf(m_lockObjAndDisposed.Value, this);
        }
    }


    public struct SpinWait
    {
        internal const int YieldThreshold = 10;

        private const int Sleep0EveryHowManyYields = 5;

        internal const int DefaultSleep1Threshold = 20;

        internal static readonly int SpinCountForSpinBeforeWait = (Environment.IsSingleProcessor ? 1 : 35);

        private int _count;

        public int Count
        {
            get
            {
                return _count;
            }
            internal set
            {
                _count = value;
            }
        }

        public bool NextSpinWillYield
        {
            get
            {
                if (_count < 10)
                {
                    return Environment.IsSingleProcessor;
                }
                return true;
            }
        }

        public void SpinOnce()
        {
            SpinOnceCore(20);
        }

        public void SpinOnce(int sleep1Threshold)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(sleep1Threshold, -1);
            if (sleep1Threshold >= 0 && sleep1Threshold < 10)
            {
                sleep1Threshold = 10;
            }
            SpinOnceCore(sleep1Threshold);
        }

        private void SpinOnceCore(int sleep1Threshold)
        {
            if ((_count >= 10 && ((_count >= sleep1Threshold && sleep1Threshold >= 0) || (_count - 10) % 2 == 0)) || Environment.IsSingleProcessor)
            {
                if (_count >= sleep1Threshold && sleep1Threshold >= 0)
                {
                    Thread.Sleep(1);
                }
                else if (((_count >= 10) ? ((_count - 10) / 2) : _count) % 5 == 4)
                {
                    Thread.Sleep(0);
                }
                else
                {
                    Thread.Yield();
                }
            }
            else
            {
                int num = 8;
                if (_count <= 30 && 1 << _count < num)
                {
                    num = 1 << _count;
                }
                Thread.SpinWait(num);
            }
            _count = ((_count == int.MaxValue) ? 10 : (_count + 1));
        }

        public void Reset()
        {
            _count = 0;
        }

        public static void SpinUntil(Func<bool> condition)
        {
            SpinUntil(condition, -1);
        }

        public static bool SpinUntil(Func<bool> condition, TimeSpan timeout)
        {
            long num = (long)timeout.TotalMilliseconds;
            if (num < -1 || num > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), timeout, SR.SpinWait_SpinUntil_TimeoutWrong);
            }
            return SpinUntil(condition, (int)num);
        }

        public static bool SpinUntil(Func<bool> condition, int millisecondsTimeout)
        {
            if (millisecondsTimeout < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(millisecondsTimeout), millisecondsTimeout, SR.SpinWait_SpinUntil_TimeoutWrong);
            }
            ArgumentNullException.ThrowIfNull(condition);
            uint num = 0u;
            if (millisecondsTimeout != 0 && millisecondsTimeout != -1)
            {
                num = (uint)Environment.TickCount;
            }
            SpinWait spinWait = default(SpinWait);
            while (!condition())
            {
                if (millisecondsTimeout == 0)
                {
                    return false;
                }
                spinWait.SpinOnce();
                if (millisecondsTimeout != -1 && spinWait.NextSpinWillYield && millisecondsTimeout <= (uint)(Environment.TickCount - (int)num))
                {
                    return false;
                }
            }
            return true;
        }
    }


    public class SynchronizationContext
    {
        private bool _requireWaitNotification;

        public static SynchronizationContext? Current => Thread.CurrentThread._synchronizationContext;

        protected void SetWaitNotificationRequired()
        {
            _requireWaitNotification = true;
        }

        public bool IsWaitNotificationRequired()
        {
            return _requireWaitNotification;
        }

        public virtual void Send(SendOrPostCallback d, object? state)
        {
            d(state);
        }

        public virtual void Post(SendOrPostCallback d, object? state)
        {
            ThreadPool.QueueUserWorkItem(delegate (KeyValuePair<SendOrPostCallback, object> s)
            {
                s.Key(s.Value);
            }, new KeyValuePair<SendOrPostCallback, object>(d, state), preferLocal: false);
        }

        public virtual void OperationStarted()
        {
        }

        public virtual void OperationCompleted()
        {
        }

        [CLSCompliant(false)]
        public virtual int Wait(nint[] waitHandles, bool waitAll, int millisecondsTimeout)
        {
            return WaitHelper(waitHandles, waitAll, millisecondsTimeout);
        }

        [CLSCompliant(false)]
        protected static int WaitHelper(nint[] waitHandles, bool waitAll, int millisecondsTimeout)
        {
            ArgumentNullException.ThrowIfNull(waitHandles);
            return WaitHandle.WaitMultipleIgnoringSyncContext(waitHandles, waitAll, millisecondsTimeout);
        }

        public static void SetSynchronizationContext(SynchronizationContext? syncContext)
        {
            Thread.CurrentThread._synchronizationContext = syncContext;
        }

        public virtual SynchronizationContext CreateCopy()
        {
            return new SynchronizationContext();
        }
    }


    public static class ThreadPool
    {
        private const bool IsWorkerTrackingEnabledInConfig = false;

        internal const string WorkerThreadName = ".NET TP Worker";

        internal static readonly ThreadPoolWorkQueue s_workQueue = new ThreadPoolWorkQueue();

        /// <summary>Shim used to invoke <see cref="IAsyncStateMachineBox.MoveNext"/> of the supplied <see cref="IAsyncStateMachineBox"/>.</summary>
        internal static readonly Action<object?> s_invokeAsyncStateMachineBox = static state =>
        {
            if (state is IAsyncStateMachineBox box)
            {
                box.MoveNext();
            }
            else
            {
                ThrowHelper.ThrowUnexpectedStateForKnownCallback(state);
            }
        };

        /// <summary>Shim used to invoke <see cref="Task.ExecuteDirectly"/> of a supplied <see cref="Task"/>.</summary>
        internal static readonly Action<object?> s_dispatchRuntimeAsyncContinuationsCallback = static state =>
        {
            if (state is Task t)
            {
                t.ExecuteDirectly(null);
            }
            else
            {
                ThrowHelper.ThrowUnexpectedStateForKnownCallback(state);
            }
        };

        public static long PendingWorkItemCount
        {
            get
            {
                ThreadPoolWorkQueue threadPoolWorkQueue = s_workQueue;
                return ThreadPoolWorkQueue.LocalCount + threadPoolWorkQueue.GlobalCount;
            }
        }

        public static bool QueueUserWorkItem(WaitCallback callBack)
        {
            return QueueUserWorkItem(callBack, null);
        }

        public static bool QueueUserWorkItem(WaitCallback callBack, object? state)
        {
            if (callBack is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.callBack);
            }
            ExecutionContext executionContext = ExecutionContext.Capture();
            object callback = ((executionContext is null || executionContext.IsDefault) ? ((QueueUserWorkItemCallbackBase)new QueueUserWorkItemCallbackDefaultContext(callBack, state)) : ((QueueUserWorkItemCallbackBase)new QueueUserWorkItemCallback(callBack, state, executionContext)));
            s_workQueue.Enqueue(callback, forceGlobal: true);
            return true;
        }

        public static bool QueueUserWorkItem<TState>(Action<TState> callBack, TState state, bool preferLocal)
        {
            if (callBack is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.callBack);
            }
            ExecutionContext executionContext = ExecutionContext.Capture();
            object callback = ((executionContext is null || executionContext.IsDefault) ? ((QueueUserWorkItemCallbackBase)new QueueUserWorkItemCallbackDefaultContext<TState>(callBack, state)) : ((QueueUserWorkItemCallbackBase)new QueueUserWorkItemCallback<TState>(callBack, state, executionContext)));
            s_workQueue.Enqueue(callback, !preferLocal);
            return true;
        }

        public static bool UnsafeQueueUserWorkItem<TState>(Action<TState> callBack, TState state, bool preferLocal)
        {
            if (callBack is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.callBack);
            }
            if ((object)callBack == s_invokeAsyncStateMachineBox)
            {
                if (!(state is IAsyncStateMachineBox))
                {
                    ThrowHelper.ThrowUnexpectedStateForKnownCallback(state);
                }
                UnsafeQueueUserWorkItemInternal(state, preferLocal);
                return true;
            }
            if ((object)callBack == s_dispatchRuntimeAsyncContinuationsCallback)
            {
                if (!(state is Task))
                {
                    ThrowHelper.ThrowUnexpectedStateForKnownCallback(state);
                }
                UnsafeQueueUserWorkItemInternal(state, preferLocal);
                return true;
            }
            s_workQueue.Enqueue(new QueueUserWorkItemCallbackDefaultContext<TState>(callBack, state), !preferLocal);
            return true;
        }

        public static bool UnsafeQueueUserWorkItem(WaitCallback callBack, object? state)
        {
            if (callBack is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.callBack);
            }
            object callback = new QueueUserWorkItemCallbackDefaultContext(callBack, state);
            s_workQueue.Enqueue(callback, forceGlobal: true);
            return true;
        }

        public static bool UnsafeQueueUserWorkItem(IThreadPoolWorkItem callBack, bool preferLocal)
        {
            if (callBack is null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.callBack);
            }
            if (callBack is Task)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.callBack);
            }
            UnsafeQueueUserWorkItemInternal(callBack, preferLocal);
            return true;
        }

        internal static void UnsafeQueueUserWorkItemInternal(object callBack, bool preferLocal)
        {
            s_workQueue.Enqueue(callBack, !preferLocal);
        }

        internal static void UnsafeQueueHighPriorityWorkItemInternal(IThreadPoolWorkItem callBack)
        {
            s_workQueue.EnqueueAtHighPriority(callBack);
        }

        internal static bool TryPopCustomWorkItem(object workItem)
        {
            return ThreadPoolWorkQueue.LocalFindAndPop(workItem);
        }

        internal static IEnumerable<object> GetQueuedWorkItems()
        {
            foreach (object highPriorityWorkItem in s_workQueue.highPriorityWorkItems)
            {
                yield return highPriorityWorkItem;
            }
            ConcurrentQueue<object>[] assignableWorkItemQueues = s_workQueue._assignableWorkItemQueues;
            foreach (ConcurrentQueue<object> concurrentQueue in assignableWorkItemQueues)
            {
                foreach (object item in concurrentQueue)
                {
                    yield return item;
                }
            }
            foreach (object workItem in s_workQueue.workItems)
            {
                yield return workItem;
            }
            ThreadPoolWorkQueue.WorkStealingQueue[] queues = ThreadPoolWorkQueue.WorkStealingQueueList.Queues;
            foreach (ThreadPoolWorkQueue.WorkStealingQueue workStealingQueue in queues)
            {
                if (workStealingQueue is null || workStealingQueue.m_array is null)
                {
                    continue;
                }
                object[] items = workStealingQueue.m_array;
                foreach (object obj in items)
                {
                    if (obj is not null)
                    {
                        yield return obj;
                    }
                }
            }
        }

        public static int ThreadCount => 0;

        public static long CompletedWorkItemCount => s_completedWorkItemCount;

        private static long s_completedWorkItemCount;

        internal static bool YieldFromDispatchLoop(int currentTickCount) => false;

        internal static void NotifyWorkItemProgress()
        {
        }

        internal static bool NotifyWorkItemComplete(int currentTimeMs)
        {
            Interlocked.Increment(ref s_completedWorkItemCount);
            return true;
        }

        internal static void EnsureWorkerRequested()
        {
        }

        internal static bool EnableWorkerTracking => false;

        internal static void ReportThreadStatus(bool isWorking)
        {
        }

        internal static bool NotifyThreadBlocked() => false;

        public static RegisteredWaitHandle RegisterWaitForSingleObject(
            WaitHandle waitObject, WaitOrTimerCallback callBack, object? state, int millisecondsTimeOutInterval, bool executeOnlyOnce)
        {
            RuntimeFeature.ThrowIfMultithreadingIsNotSupported();
            throw new PlatformNotSupportedException();
        }

        internal static void NotifyThreadUnblocked()
        {
        }
    }


    internal sealed class ThreadPoolWorkQueue
    {
        internal static class WorkStealingQueueList
        {
            private static WorkStealingQueue[] s_queues = new WorkStealingQueue[0];

            public static WorkStealingQueue[] Queues => s_queues;

            public static void Add(WorkStealingQueue queue)
            {
                WorkStealingQueue[] array;
                WorkStealingQueue[] array2;
                do
                {
                    array = s_queues;
                    array2 = new WorkStealingQueue[array.Length + 1];
                    Array.Copy(array, array2, array.Length);
                    array2[^1] = queue;
                }
                while (Interlocked.CompareExchange(ref s_queues, array2, array) != array);
            }

            public static void Remove(WorkStealingQueue queue)
            {
                WorkStealingQueue[] array;
                WorkStealingQueue[] array2;
                do
                {
                    array = s_queues;
                    if (array.Length == 0)
                    {
                        break;
                    }
                    int num = Array.IndexOf(array, queue);
                    if (num < 0)
                    {
                        break;
                    }
                    array2 = new WorkStealingQueue[array.Length - 1];
                    if (num == 0)
                    {
                        Array.Copy(array, 1, array2, 0, array2.Length);
                        continue;
                    }
                    if (num == array.Length - 1)
                    {
                        Array.Copy(array, array2, array2.Length);
                        continue;
                    }
                    Array.Copy(array, array2, num);
                    Array.Copy(array, num + 1, array2, num, array2.Length - num);
                }
                while (Interlocked.CompareExchange(ref s_queues, array2, array) != array);
            }
        }

        internal sealed class WorkStealingQueue
        {
            private const int INITIAL_SIZE = 32;

            internal volatile object[] m_array = new object[32];

            private volatile int m_mask = 31;

            private const int START_INDEX = 0;

            private volatile int m_headIndex;

            private volatile int m_tailIndex;

            private SpinLock m_foreignLock = new SpinLock(enableThreadOwnerTracking: false);

            public bool CanSteal => m_headIndex < m_tailIndex;

            public int Count
            {
                get
                {
                    bool lockTaken = false;
                    try
                    {
                        m_foreignLock.Enter(ref lockTaken);
                        return Math.Max(0, m_tailIndex - m_headIndex);
                    }
                    finally
                    {
                        if (lockTaken)
                        {
                            m_foreignLock.Exit(useMemoryBarrier: false);
                        }
                    }
                }
            }

            public void LocalPush(object obj)
            {
                int num = m_tailIndex;
                if (num == int.MaxValue)
                {
                    num = LocalPush_HandleTailOverflow();
                }
                if (num < m_headIndex + m_mask)
                {
                    m_array[num & m_mask] = obj;
                    m_tailIndex = num + 1;
                }
                else
                {
                    bool lockTaken = false;
                    try
                    {
                        m_foreignLock.Enter(ref lockTaken);
                        int headIndex = m_headIndex;
                        int num2 = m_tailIndex - m_headIndex;
                        if (num2 >= m_mask)
                        {
                            object[] array = new object[m_array.Length << 1];
                            for (int i = 0; i < m_array.Length; i++)
                            {
                                array[i] = m_array[(i + headIndex) & m_mask];
                            }
                            m_array = array;
                            m_headIndex = 0;
                            num = (m_tailIndex = num2);
                            m_mask = (m_mask << 1) | 1;
                        }
                        m_array[num & m_mask] = obj;
                        m_tailIndex = num + 1;
                    }
                    finally
                    {
                        if (lockTaken)
                        {
                            m_foreignLock.Exit(useMemoryBarrier: false);
                        }
                    }
                }
                Interlocked.MemoryBarrier();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private int LocalPush_HandleTailOverflow()
            {
                bool lockTaken = false;
                try
                {
                    m_foreignLock.Enter(ref lockTaken);
                    int num = m_tailIndex;
                    if (num == int.MaxValue)
                    {
                        m_headIndex &= m_mask;
                        num = (m_tailIndex &= m_mask);
                    }
                    return num;
                }
                finally
                {
                    if (lockTaken)
                    {
                        m_foreignLock.Exit(useMemoryBarrier: true);
                    }
                }
            }

            public bool LocalFindAndPop(object obj)
            {
                if (m_array[(m_tailIndex - 1) & m_mask] == obj)
                {
                    return LocalPop() is not null;
                }
                for (int num = m_tailIndex - 2; num >= m_headIndex; num--)
                {
                    if (m_array[num & m_mask] == obj)
                    {
                        bool lockTaken = false;
                        try
                        {
                            m_foreignLock.Enter(ref lockTaken);
                            if (m_array[num & m_mask] is null)
                            {
                                return false;
                            }
                            Volatile.Write(ref m_array[num & m_mask], null);
                            if (num == m_tailIndex)
                            {
                                m_tailIndex--;
                            }
                            else if (num == m_headIndex)
                            {
                                m_headIndex++;
                            }
                            return true;
                        }
                        finally
                        {
                            if (lockTaken)
                            {
                                m_foreignLock.Exit(useMemoryBarrier: false);
                            }
                        }
                    }
                }
                return false;
            }

            public object LocalPop()
            {
                if (m_headIndex >= m_tailIndex)
                {
                    return null;
                }
                return LocalPopCore();
            }

            private object LocalPopCore()
            {
                int num;
                object obj;
                while (true)
                {
                    int tailIndex = m_tailIndex;
                    if (m_headIndex >= tailIndex)
                    {
                        return null;
                    }
                    tailIndex--;
                    Interlocked.Exchange(ref m_tailIndex, tailIndex);
                    if (m_headIndex <= tailIndex)
                    {
                        num = tailIndex & m_mask;
                        obj = Volatile.Read(in m_array[num]);
                        if (obj is not null)
                        {
                            break;
                        }
                        continue;
                    }
                    bool lockTaken = false;
                    try
                    {
                        m_foreignLock.Enter(ref lockTaken);
                        if (m_headIndex <= tailIndex)
                        {
                            int num2 = tailIndex & m_mask;
                            object obj2 = Volatile.Read(in m_array[num2]);
                            if (obj2 is not null)
                            {
                                m_array[num2] = null;
                                return obj2;
                            }
                            continue;
                        }
                        m_tailIndex = tailIndex + 1;
                        return null;
                    }
                    finally
                    {
                        if (lockTaken)
                        {
                            m_foreignLock.Exit(useMemoryBarrier: false);
                        }
                    }
                }
                m_array[num] = null;
                return obj;
            }

            public object TrySteal(ref bool missedSteal)
            {
                while (CanSteal)
                {
                    bool lockTaken = false;
                    try
                    {
                        m_foreignLock.TryEnter(ref lockTaken);
                        if (lockTaken)
                        {
                            int headIndex = m_headIndex;
                            Interlocked.Exchange(ref m_headIndex, headIndex + 1);
                            if (headIndex < m_tailIndex)
                            {
                                int num = headIndex & m_mask;
                                object obj = Volatile.Read(in m_array[num]);
                                if (obj is null)
                                {
                                    continue;
                                }
                                m_array[num] = null;
                                return obj;
                            }
                            Interlocked.Exchange(ref m_headIndex, headIndex);
                        }
                    }
                    finally
                    {
                        if (lockTaken)
                        {
                            m_foreignLock.Exit(useMemoryBarrier: false);
                        }
                    }
                    missedSteal = true;
                    break;
                }
                return null;
            }
        }

        public enum DispatchResult
        {
            Spurious,
            Regular,
            ShouldStop
        }

        private const int ProcessorsPerAssignableWorkItemQueue = 16;

        private static readonly int s_assignableWorkItemQueueCount = ((Environment.ProcessorCount > 32) ? ((Environment.ProcessorCount + 15) / 16) : 0);

        internal readonly ConcurrentQueue<object> workItems = new ConcurrentQueue<object>();

        internal readonly ConcurrentQueue<object> highPriorityWorkItems = new ConcurrentQueue<object>();

        internal readonly ConcurrentQueue<object>[] _assignableWorkItemQueues = new ConcurrentQueue<object>[s_assignableWorkItemQueueCount];

        private readonly LowLevelLock _queueAssignmentLock = new LowLevelLock();

        private readonly int[] _assignedWorkItemQueueThreadCounts = ((s_assignableWorkItemQueueCount > 0) ? new int[s_assignableWorkItemQueueCount] : Array.Empty<int>());

        public const uint DispatchQuantumMs = 30u;

        public static long LocalCount
        {
            get
            {
                long num = 0L;
                WorkStealingQueue[] queues = WorkStealingQueueList.Queues;
                foreach (WorkStealingQueue workStealingQueue in queues)
                {
                    num += workStealingQueue.Count;
                }
                return num;
            }
        }

        public long GlobalCount
        {
            get
            {
                long num = (long)highPriorityWorkItems.Count + (long)workItems.Count;
                for (int i = 0; i < s_assignableWorkItemQueueCount; i++)
                {
                    num += _assignableWorkItemQueues[i].Count;
                }
                return num;
            }
        }

        public ThreadPoolWorkQueue()
        {
            for (int i = 0; i < s_assignableWorkItemQueueCount; i++)
            {
                _assignableWorkItemQueues[i] = new ConcurrentQueue<object>();
            }
        }

        private void AssignWorkItemQueue(ThreadPoolWorkQueueThreadLocals tl)
        {
            _queueAssignmentLock.Acquire();
            int num = -1;
            int num2 = int.MaxValue;
            int num3 = 0;
            for (int i = 0; i < s_assignableWorkItemQueueCount; i++)
            {
                int num4 = _assignedWorkItemQueueThreadCounts[i];
                if (num4 < 16)
                {
                    num = i;
                    _assignedWorkItemQueueThreadCounts[num] = num4 + 1;
                    break;
                }
                if (num4 < num2)
                {
                    num2 = num4;
                    num3 = i;
                }
            }
            if (num < 0)
            {
                num = num3;
                _assignedWorkItemQueueThreadCounts[num]++;
            }
            _queueAssignmentLock.Release();
            tl.queueIndex = num;
            tl.assignedGlobalWorkItemQueue = _assignableWorkItemQueues[num];
        }

        private void TryReassignWorkItemQueue(ThreadPoolWorkQueueThreadLocals tl)
        {
            int num = tl.queueIndex;
            if (num == 0 || !_queueAssignmentLock.TryAcquire())
            {
                return;
            }
            if (_assignedWorkItemQueueThreadCounts[num] > 1)
            {
                for (int i = 0; i < num; i++)
                {
                    if (_assignedWorkItemQueueThreadCounts[i] < 16)
                    {
                        _assignedWorkItemQueueThreadCounts[num]--;
                        num = i;
                        _assignedWorkItemQueueThreadCounts[num]++;
                        break;
                    }
                }
            }
            _queueAssignmentLock.Release();
            tl.queueIndex = num;
            tl.assignedGlobalWorkItemQueue = _assignableWorkItemQueues[num];
        }

        private void UnassignWorkItemQueue(ThreadPoolWorkQueueThreadLocals tl)
        {
            int queueIndex = tl.queueIndex;
            if (queueIndex == -1)
            {
                return;
            }
            _queueAssignmentLock.Acquire();
            int num = --_assignedWorkItemQueueThreadCounts[queueIndex];
            _queueAssignmentLock.Release();
            if (num <= 0)
            {
                bool flag = false;
                ConcurrentQueue<object> assignedGlobalWorkItemQueue = tl.assignedGlobalWorkItemQueue;
                object result;
                while (_assignedWorkItemQueueThreadCounts[queueIndex] <= 0 && assignedGlobalWorkItemQueue.TryDequeue(out result))
                {
                    workItems.Enqueue(result);
                    flag = true;
                }
                if (flag)
                {
                    ThreadPool.EnsureWorkerRequested();
                }
                tl.queueIndex = -1;
                tl.assignedGlobalWorkItemQueue = workItems;
            }
        }

        public ThreadPoolWorkQueueThreadLocals GetOrCreateThreadLocals()
        {
            return ThreadPoolWorkQueueThreadLocals.threadLocals ?? CreateThreadLocals();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private ThreadPoolWorkQueueThreadLocals CreateThreadLocals()
        {
            return ThreadPoolWorkQueueThreadLocals.threadLocals = new ThreadPoolWorkQueueThreadLocals(this);
        }

        public void Enqueue(object callback, bool forceGlobal)
        {
            ThreadPoolWorkQueueThreadLocals threadLocals;
            if (!forceGlobal && (threadLocals = ThreadPoolWorkQueueThreadLocals.threadLocals) is not null)
            {
                threadLocals.workStealingQueue.LocalPush(callback);
            }
            else
            {
                ((s_assignableWorkItemQueueCount > 0 && (threadLocals = ThreadPoolWorkQueueThreadLocals.threadLocals) is not null) ? threadLocals.assignedGlobalWorkItemQueue : workItems).Enqueue(callback);
            }
            ThreadPool.EnsureWorkerRequested();
        }

        public void EnqueueAtHighPriority(object workItem)
        {
            highPriorityWorkItems.Enqueue(workItem);
            ThreadPool.EnsureWorkerRequested();
        }

        internal static void TransferAllLocalWorkItemsToHighPriorityGlobalQueue()
        {
            ThreadPoolWorkQueueThreadLocals threadLocals = ThreadPoolWorkQueueThreadLocals.threadLocals;
            if (threadLocals is not null)
            {
                TransferAllLocalWorkItemsToHighPriorityGlobalQueue(threadLocals);
            }
        }

        internal static void TransferAllLocalWorkItemsToHighPriorityGlobalQueue(ThreadPoolWorkQueueThreadLocals tl)
        {
            ThreadPoolWorkQueue s_workQueue = ThreadPool.s_workQueue;
            bool flag = false;
            while (true)
            {
                object obj = tl.workStealingQueue.LocalPop();
                if (obj is null)
                {
                    break;
                }
                flag = true;
                try
                {
                    s_workQueue.highPriorityWorkItems.Enqueue(obj);
                }
                catch (OutOfMemoryException)
                {
                    tl.workStealingQueue.LocalPush(obj);
                    break;
                }
            }
            if (flag)
            {
                ThreadPool.EnsureWorkerRequested();
            }
        }

        internal static bool LocalFindAndPop(object callback)
        {
            return ThreadPoolWorkQueueThreadLocals.threadLocals?.workStealingQueue.LocalFindAndPop(callback) ?? false;
        }

        public object Dequeue(ThreadPoolWorkQueueThreadLocals tl, ref bool missedSteal)
        {
            object workItem = tl.workStealingQueue.LocalPop();
            if (workItem is not null)
            {
                return workItem;
            }
            if (tl.isProcessingHighPriorityWorkItems)
            {
                if (highPriorityWorkItems.TryDequeue(out workItem))
                {
                    return workItem;
                }
                tl.isProcessingHighPriorityWorkItems = false;
            }
            else if (!highPriorityWorkItems.IsEmpty && TryStartProcessingHighPriorityWorkItemsAndDequeue(tl, out workItem))
            {
                return workItem;
            }
            if (s_assignableWorkItemQueueCount > 0 && tl.assignedGlobalWorkItemQueue.TryDequeue(out workItem))
            {
                return workItem;
            }
            if (workItems.TryDequeue(out workItem))
            {
                return workItem;
            }
            uint num = tl.random.NextUInt32();
            if (s_assignableWorkItemQueueCount > 0)
            {
                int queueIndex = tl.queueIndex;
                int num2 = s_assignableWorkItemQueueCount;
                int num3 = num2 - 1;
                int num4 = (int)(num % (uint)num2);
                while (num2 > 0)
                {
                    if (num4 != queueIndex && _assignableWorkItemQueues[num4].TryDequeue(out workItem))
                    {
                        return workItem;
                    }
                    num4 = ((num4 < num3) ? (num4 + 1) : 0);
                    num2--;
                }
            }
            WorkStealingQueue workStealingQueue = tl.workStealingQueue;
            WorkStealingQueue[] queues = WorkStealingQueueList.Queues;
            int num5 = queues.Length;
            int num6 = num5 - 1;
            int num7 = (int)(num % (uint)num5);
            while (num5 > 0)
            {
                WorkStealingQueue workStealingQueue2 = queues[num7];
                if (workStealingQueue2 != workStealingQueue && workStealingQueue2.CanSteal)
                {
                    workItem = workStealingQueue2.TrySteal(ref missedSteal);
                    if (workItem is not null)
                    {
                        return workItem;
                    }
                }
                num7 = ((num7 < num6) ? (num7 + 1) : 0);
                num5--;
            }
            return null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool TryStartProcessingHighPriorityWorkItemsAndDequeue(ThreadPoolWorkQueueThreadLocals tl, [MaybeNullWhen(false)] out object workItem)
        {
            if (!highPriorityWorkItems.TryDequeue(out workItem))
            {
                return false;
            }
            tl.isProcessingHighPriorityWorkItems = true;
            return true;
        }

        internal static DispatchResult Dispatch()
        {
            ThreadPoolWorkQueue s_workQueue = ThreadPool.s_workQueue;
            ThreadPoolWorkQueueThreadLocals orCreateThreadLocals = s_workQueue.GetOrCreateThreadLocals();
            if (s_assignableWorkItemQueueCount > 0)
            {
                s_workQueue.AssignWorkItemQueue(orCreateThreadLocals);
            }
            bool missedSteal = false;
            object obj = s_workQueue.Dequeue(orCreateThreadLocals, ref missedSteal);
            if (obj is null)
            {
                if (s_assignableWorkItemQueueCount > 0)
                {
                    s_workQueue.UnassignWorkItemQueue(orCreateThreadLocals);
                }
                if (missedSteal)
                {
                    ThreadPool.EnsureWorkerRequested();
                }
                return DispatchResult.Spurious;
            }
            ThreadPool.EnsureWorkerRequested();
            Thread currentThread = orCreateThreadLocals.currentThread;
            currentThread._executionContext = null;
            currentThread._synchronizationContext = null;
            int num = Environment.TickCount;
            while (true)
            {
                if (obj is null)
                {
                    missedSteal = false;
                    obj = s_workQueue.Dequeue(orCreateThreadLocals, ref missedSteal);
                    if (obj is null)
                    {
                        if (s_assignableWorkItemQueueCount > 0)
                        {
                            s_workQueue.UnassignWorkItemQueue(orCreateThreadLocals);
                        }
                        if (missedSteal)
                        {
                            ThreadPool.EnsureWorkerRequested();
                        }
                        return DispatchResult.Regular;
                    }
                }
                if (ThreadPool.EnableWorkerTracking)
                {
                    DispatchWorkItemWithWorkerTracking(obj, currentThread);
                }
                else
                {
                    DispatchWorkItem(obj, currentThread);
                }
                obj = null;
                ExecutionContext.ResetThreadPoolThread(currentThread);
                currentThread.ResetThreadPoolThread();
                int tickCount = Environment.TickCount;
                if (!ThreadPool.NotifyWorkItemComplete(tickCount))
                {
                    TransferAllLocalWorkItemsToHighPriorityGlobalQueue(orCreateThreadLocals);
                    orCreateThreadLocals.isProcessingHighPriorityWorkItems = false;
                    if (s_assignableWorkItemQueueCount > 0)
                    {
                        s_workQueue.UnassignWorkItemQueue(orCreateThreadLocals);
                    }
                    return DispatchResult.ShouldStop;
                }
                if ((uint)(tickCount - num) >= 30u)
                {
                    if (ThreadPool.YieldFromDispatchLoop(tickCount))
                    {
                        break;
                    }
                    if (s_assignableWorkItemQueueCount > 0)
                    {
                        s_workQueue.TryReassignWorkItemQueue(orCreateThreadLocals);
                    }
                    num = tickCount;
                    
                }
            }
            orCreateThreadLocals.isProcessingHighPriorityWorkItems = false;
            if (s_assignableWorkItemQueueCount > 0)
            {
                s_workQueue.UnassignWorkItemQueue(orCreateThreadLocals);
            }
            return DispatchResult.Regular;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void DispatchWorkItemWithWorkerTracking(object workItem, Thread currentThread)
        {
            bool flag = false;
            try
            {
                ThreadPool.ReportThreadStatus(isWorking: true);
                flag = true;
                DispatchWorkItem(workItem, currentThread);
            }
            finally
            {
                if (flag)
                {
                    ThreadPool.ReportThreadStatus(isWorking: false);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DispatchWorkItem(object workItem, Thread currentThread)
        {
            if (workItem is Task task)
            {
                task.ExecuteDirectly(currentThread);
                return;
            }
            try
            {
                Unsafe.As<IThreadPoolWorkItem>(workItem).Execute();
            }
            catch (Exception ex) when (ExceptionHandling.IsHandledByGlobalHandler(ex))
            {
            }
        }
    }


    internal sealed class ThreadPoolWorkQueueThreadLocals
    {
        [ThreadStatic]
        public static ThreadPoolWorkQueueThreadLocals threadLocals;

        public bool isProcessingHighPriorityWorkItems;

        public int queueIndex;

        public ConcurrentQueue<object> assignedGlobalWorkItemQueue;

        public readonly ThreadPoolWorkQueue workQueue;

        public readonly ThreadPoolWorkQueue.WorkStealingQueue workStealingQueue;

        public readonly Thread currentThread;


        public readonly Random.XoshiroImpl random = new Random.XoshiroImpl();

        public ThreadPoolWorkQueueThreadLocals(ThreadPoolWorkQueue tpq)
        {
            assignedGlobalWorkItemQueue = tpq.workItems;
            queueIndex = -1;
            workQueue = tpq;
            workStealingQueue = new ThreadPoolWorkQueue.WorkStealingQueue();
            ThreadPoolWorkQueue.WorkStealingQueueList.Add(workStealingQueue);
            currentThread = Thread.CurrentThread;
        }

        ~ThreadPoolWorkQueueThreadLocals()
        {
            if (workStealingQueue is not null)
            {
                ThreadPoolWorkQueue.TransferAllLocalWorkItemsToHighPriorityGlobalQueue(this);
                ThreadPoolWorkQueue.WorkStealingQueueList.Remove(workStealingQueue);
            }
        }
    }


    public delegate void ThreadStart();


    public delegate void ParameterizedThreadStart(object? obj);


    public static class Timeout
    {
        public static readonly TimeSpan InfiniteTimeSpan = new TimeSpan(0, 0, 0, 0, -1);

        public const int Infinite = -1;

        internal const uint UnsignedInfinite = uint.MaxValue;
    }


    internal static class TimeoutHelper
    {
        public static long UpdateTimeOut(long startTime, long originalWaitMillisecondsTimeout)
        {
            ulong num = (ulong)(Environment.TickCount64 - startTime);
            if (num > long.MaxValue)
            {
                return 0L;
            }
            long num2 = originalWaitMillisecondsTimeout - (long)num;
            if (num2 <= 0)
            {
                return 0L;
            }
            return num2;
        }
    }


    [DebuggerDisplay("{DisplayString,nq}")]
    public sealed class Timer : MarshalByRefObject, IDisposable, IAsyncDisposable, ITimer
    {
        internal const uint MaxSupportedTimeout = 4294967294u;

        internal TimerHolder _timer;

        public static long ActiveCount
        {
            get
            {
                long num = 0L;
                TimerQueue[] instances = TimerQueue.Instances;
                foreach (TimerQueue timerQueue in instances)
                {
                    using (timerQueue.SharedLock.EnterScope())
                    {
                        num += timerQueue.ActiveCount;
                    }
                }
                return num;
            }
        }

        private string DisplayString => _timer._timer.DisplayString;

        public Timer(TimerCallback callback, object? state, int dueTime, int period)
            : this(callback, state, dueTime, period, flowExecutionContext: true)
        {
        }

        internal Timer(TimerCallback callback, object state, int dueTime, int period, bool flowExecutionContext)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(dueTime, -1);
            ArgumentOutOfRangeException.ThrowIfLessThan(period, -1);
            TimerSetup(callback, state, (uint)dueTime, (uint)period, flowExecutionContext);
        }

        public Timer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            long num = (long)dueTime.TotalMilliseconds;
            ArgumentOutOfRangeException.ThrowIfLessThan(num, -1L, "dueTime");
            ArgumentOutOfRangeException.ThrowIfGreaterThan(num, 4294967294L, "dueTime");
            long num2 = (long)period.TotalMilliseconds;
            ArgumentOutOfRangeException.ThrowIfLessThan(num2, -1L, "period");
            ArgumentOutOfRangeException.ThrowIfGreaterThan(num2, 4294967294L, "period");
            TimerSetup(callback, state, (uint)num, (uint)num2);
        }

        [CLSCompliant(false)]
        public Timer(TimerCallback callback, object? state, uint dueTime, uint period)
        {
            TimerSetup(callback, state, dueTime, period);
        }

        public Timer(TimerCallback callback, object? state, long dueTime, long period)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(dueTime, -1L);
            ArgumentOutOfRangeException.ThrowIfLessThan(period, -1L);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(dueTime, 4294967294L);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(period, 4294967294L);
            TimerSetup(callback, state, (uint)dueTime, (uint)period);
        }

        public Timer(TimerCallback callback)
        {
            TimerSetup(callback, this, uint.MaxValue, uint.MaxValue);
        }

        [MemberNotNull("_timer")]
        private void TimerSetup(TimerCallback callback, object state, uint dueTime, uint period, bool flowExecutionContext = true)
        {
            ArgumentNullException.ThrowIfNull(callback);
            _timer = new TimerHolder(new TimerQueueTimer(callback, state, dueTime, period, flowExecutionContext));
        }

        public bool Change(int dueTime, int period)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(dueTime, -1);
            ArgumentOutOfRangeException.ThrowIfLessThan(period, -1);
            return _timer._timer.Change((uint)dueTime, (uint)period);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            return _timer._timer.Change(dueTime, period);
        }

        [CLSCompliant(false)]
        public bool Change(uint dueTime, uint period)
        {
            return _timer._timer.Change(dueTime, period);
        }

        public bool Change(long dueTime, long period)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(dueTime, -1L);
            ArgumentOutOfRangeException.ThrowIfLessThan(period, -1L);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(dueTime, 4294967294L);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(period, 4294967294L);
            return _timer._timer.Change((uint)dueTime, (uint)period);
        }

        public bool Dispose(WaitHandle notifyObject)
        {
            ArgumentNullException.ThrowIfNull(notifyObject);
            return _timer.Dispose(notifyObject);
        }

        public void Dispose()
        {
            _timer.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            return _timer.DisposeAsync();
        }
    }


    public delegate void TimerCallback(object? state);


    internal sealed class TimerHolder
    {
        internal readonly TimerQueueTimer _timer;

        public TimerHolder(TimerQueueTimer timer)
        {
            _timer = timer;
        }

        ~TimerHolder()
        {
            _timer.Dispose();
        }

        public void Dispose()
        {
            _timer.Dispose();
            GC.SuppressFinalize(this);
        }

        public bool Dispose(WaitHandle notifyObject)
        {
            bool result = _timer.Dispose(notifyObject);
            GC.SuppressFinalize(this);
            return result;
        }

        public ValueTask DisposeAsync()
        {
            ValueTask result = _timer.DisposeAsync();
            GC.SuppressFinalize(this);
            return result;
        }
    }


    [DebuggerDisplay("Count = {CountForDebugger}")]
    internal sealed class TimerQueue : IThreadPoolWorkItem
    {
        private sealed class TimerQueueDebuggerTypeProxy
        {
            private readonly TimerQueue _queue;

            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public TimerQueueTimer[] Items => new List<TimerQueueTimer>(_queue.GetTimersForDebugger()).ToArray();

            public TimerQueueDebuggerTypeProxy(TimerQueue queue)
            {
                ArgumentNullException.ThrowIfNull(queue);
                _queue = queue;
            }
        }

        internal static readonly (long TickCount, DateTime Time) s_tickCountToTimeMap = (TickCount: Environment.TickCount64, Time: DateTime.UtcNow);

        private bool _isTimerScheduled;

        private long _currentTimerStartTicks;

        private uint _currentTimerDuration;

        private TimerQueueTimer _shortTimers;

        private TimerQueueTimer _longTimers;

        private long _currentAbsoluteThreshold = Environment.TickCount64 + 333;

        private const int ShortTimersThresholdMilliseconds = 333;

        private static List<TimerQueue> s_scheduledTimers;

        private static List<TimerQueue> s_scheduledTimersToFire;

        private static readonly AutoResetEvent s_timerEvent = new AutoResetEvent(initialState: false);

        private static readonly Lock s_timerEventLock = new Lock();

        private bool _isScheduled;

        private long _scheduledDueTimeMs;

        private readonly int _id;

        public static TimerQueue[] Instances { get; } = CreateTimerQueues();

        private int CountForDebugger
        {
            get
            {
                int num = 0;
                foreach (TimerQueueTimer item in GetTimersForDebugger())
                {
                    _ = item;
                    num++;
                }
                return num;
            }
        }

        internal Lock SharedLock { get; } = new Lock();

        public long ActiveCount { get; private set; }

        private static TimerQueue[] CreateTimerQueues()
        {
            TimerQueue[] array = new TimerQueue[Environment.ProcessorCount];
            for (int i = 0; i < array.Length; i++)
            {
                array[i] = new TimerQueue(i);
            }
            return array;
        }

        internal IEnumerable<TimerQueueTimer> GetTimersForDebugger()
        {
            for (TimerQueueTimer timer = _shortTimers; timer is not null; timer = timer._next)
            {
                yield return timer;
            }
            for (TimerQueueTimer timer = _longTimers; timer is not null; timer = timer._next)
            {
                yield return timer;
            }
        }

        private bool EnsureTimerFiresBy(uint requestedDuration)
        {
            uint num = Math.Min(requestedDuration, 268435455u);
            if (_isTimerScheduled)
            {
                long num2 = Environment.TickCount64 - _currentTimerStartTicks;
                if (num2 >= _currentTimerDuration)
                {
                    return true;
                }
                uint num3 = _currentTimerDuration - (uint)(int)num2;
                if (num >= num3)
                {
                    return true;
                }
            }
            if (SetTimer(num))
            {
                _isTimerScheduled = true;
                _currentTimerStartTicks = Environment.TickCount64;
                _currentTimerDuration = num;
                return true;
            }
            return false;
        }

        private void FireNextTimers()
        {
            TimerQueueTimer timerQueueTimer = null;
            using (SharedLock.EnterScope())
            {
                _isTimerScheduled = false;
                bool flag = false;
                uint num = uint.MaxValue;
                long tickCount = Environment.TickCount64;
                TimerQueueTimer timerQueueTimer2 = _shortTimers;
                for (int i = 0; i < 2; i++)
                {
                    while (timerQueueTimer2 is not null)
                    {
                        TimerQueueTimer next = timerQueueTimer2._next;
                        long num2 = tickCount - timerQueueTimer2._startTicks;
                        long num3 = timerQueueTimer2._dueTime - num2;
                        if (num3 <= 0)
                        {
                            timerQueueTimer2._everQueued = true;
                            if (timerQueueTimer2._period != uint.MaxValue)
                            {
                                timerQueueTimer2._startTicks = tickCount;
                                long num4 = num2 - timerQueueTimer2._dueTime;
                                timerQueueTimer2._dueTime = ((num4 >= timerQueueTimer2._period) ? 1u : (timerQueueTimer2._period - (uint)(int)num4));
                                if (timerQueueTimer2._dueTime < num)
                                {
                                    flag = true;
                                    num = timerQueueTimer2._dueTime;
                                }
                                bool flag2 = tickCount + timerQueueTimer2._dueTime - _currentAbsoluteThreshold <= 0;
                                if (timerQueueTimer2._short != flag2)
                                {
                                    MoveTimerToCorrectList(timerQueueTimer2, flag2);
                                }
                            }
                            else
                            {
                                DeleteTimer(timerQueueTimer2);
                            }
                            if (timerQueueTimer is null)
                            {
                                timerQueueTimer = timerQueueTimer2;
                            }
                            else
                            {
                                ThreadPool.UnsafeQueueUserWorkItemInternal(timerQueueTimer2, preferLocal: false);
                            }
                        }
                        else
                        {
                            if (num3 < num)
                            {
                                flag = true;
                                num = (uint)num3;
                            }
                            if (!timerQueueTimer2._short && num3 <= 333)
                            {
                                MoveTimerToCorrectList(timerQueueTimer2, shortList: true);
                            }
                        }
                        timerQueueTimer2 = next;
                    }
                    if (i != 0)
                    {
                        continue;
                    }
                    long num5 = _currentAbsoluteThreshold - tickCount;
                    if (num5 > 0)
                    {
                        if (_shortTimers is null && _longTimers is not null)
                        {
                            num = (uint)((int)num5 + 1);
                            flag = true;
                        }
                        break;
                    }
                    timerQueueTimer2 = _longTimers;
                    _currentAbsoluteThreshold = tickCount + 333;
                }
                if (flag)
                {
                    EnsureTimerFiresBy(num);
                }
            }
            timerQueueTimer?.Fire();
        }

        public bool UpdateTimer(TimerQueueTimer timer, uint dueTime, uint period)
        {
            long tickCount = Environment.TickCount64;
            long num = tickCount + dueTime;
            bool flag = _currentAbsoluteThreshold - num >= 0;
            if (timer._dueTime == uint.MaxValue)
            {
                timer._short = flag;
                LinkTimer(timer);
                long activeCount = ActiveCount + 1;
                ActiveCount = activeCount;
            }
            else if (timer._short != flag)
            {
                UnlinkTimer(timer);
                timer._short = flag;
                LinkTimer(timer);
            }
            timer._dueTime = dueTime;
            timer._period = ((period == 0) ? uint.MaxValue : period);
            timer._startTicks = tickCount;
            return EnsureTimerFiresBy(dueTime);
        }

        public void MoveTimerToCorrectList(TimerQueueTimer timer, bool shortList)
        {
            UnlinkTimer(timer);
            timer._short = shortList;
            LinkTimer(timer);
        }

        private void LinkTimer(TimerQueueTimer timer)
        {
            ref TimerQueueTimer reference = ref timer._short ? ref _shortTimers : ref _longTimers;
            timer._next = reference;
            TimerQueueTimer next = timer._next;
            if (next is not null)
            {
                next._prev = timer;
            }
            timer._prev = null;
            reference = timer;
        }

        private void UnlinkTimer(TimerQueueTimer timer)
        {
            TimerQueueTimer next = timer._next;
            if (next is not null)
            {
                next._prev = timer._prev;
            }
            if (_shortTimers == timer)
            {
                _shortTimers = next;
            }
            else if (_longTimers == timer)
            {
                _longTimers = next;
            }
            next = timer._prev;
            if (next is not null)
            {
                next._next = timer._next;
            }
        }

        public void DeleteTimer(TimerQueueTimer timer)
        {
            if (timer._dueTime != uint.MaxValue)
            {
                long activeCount = ActiveCount - 1;
                ActiveCount = activeCount;
                UnlinkTimer(timer);
                timer._prev = null;
                timer._next = null;
                timer._dueTime = uint.MaxValue;
                timer._period = uint.MaxValue;
                timer._startTicks = 0L;
                timer._short = false;
            }
        }

        private static List<TimerQueue> InitializeScheduledTimerManager_Locked()
        {
            List<TimerQueue> result = new List<TimerQueue>(Instances.Length);
            if (s_scheduledTimersToFire is null)
            {
                s_scheduledTimersToFire = new List<TimerQueue>(Instances.Length);
            }
            Thread thread = new Thread(TimerThread);
            thread.Name = ".NET Timer";
            thread.IsBackground = true;
            thread.UnsafeStart();
            s_scheduledTimers = result;
            return result;
        }

        private bool SetTimerPortable(uint actualDuration)
        {
            long scheduledDueTimeMs = Environment.TickCount64 + (int)actualDuration;
            AutoResetEvent autoResetEvent = s_timerEvent;
            using (s_timerEventLock.EnterScope())
            {
                if (!_isScheduled)
                {
                    (s_scheduledTimers ?? InitializeScheduledTimerManager_Locked()).Add(this);
                    _isScheduled = true;
                }
                _scheduledDueTimeMs = scheduledDueTimeMs;
            }
            autoResetEvent.Set();
            return true;
        }

        private static void TimerThread()
        {
            AutoResetEvent autoResetEvent = s_timerEvent;
            Lock obj = s_timerEventLock;
            List<TimerQueue> list = s_scheduledTimersToFire;
            List<TimerQueue> list2;
            using (obj.EnterScope())
            {
                list2 = s_scheduledTimers;
            }
            int num = -1;
            while (true)
            {
                autoResetEvent.WaitOne(num);
                long tickCount = Environment.TickCount64;
                num = int.MaxValue;
                using (obj.EnterScope())
                {
                    for (int num2 = list2.Count - 1; num2 >= 0; num2--)
                    {
                        TimerQueue timerQueue = list2[num2];
                        long num3 = timerQueue._scheduledDueTimeMs - tickCount;
                        if (num3 <= 0)
                        {
                            timerQueue._isScheduled = false;
                            list.Add(timerQueue);
                            int num4 = list2.Count - 1;
                            if (num2 != num4)
                            {
                                list2[num2] = list2[num4];
                            }
                            list2.RemoveAt(num4);
                        }
                        else if (num3 < num)
                        {
                            num = (int)num3;
                        }
                    }
                }
                if (list.Count > 0)
                {
                    foreach (TimerQueue item in list)
                    {
                        ThreadPool.UnsafeQueueHighPriorityWorkItemInternal(item);
                    }
                    list.Clear();
                }
                if (num == int.MaxValue)
                {
                    num = -1;
                }
            }
        }

        void IThreadPoolWorkItem.Execute()
        {
            FireNextTimers();
        }

        private TimerQueue(int id)
        {
            _id = id;
        }

        private bool SetTimer(uint actualDuration)
        {
            return SetTimerPortable(actualDuration);
        }

    }


    [DebuggerDisplay("{DisplayString,nq}")]
    internal sealed class TimerQueueTimer : ITimer, IDisposable, IAsyncDisposable, IThreadPoolWorkItem
    {
        internal sealed class TimerDebuggerTypeProxy
        {
            private readonly TimerQueueTimer _timer;

            public DateTime? EstimatedNextTimeUtc
            {
                get
                {
                    if (_timer._dueTime != uint.MaxValue)
                    {
                        long milliseconds = _timer._startTicks - TimerQueue.s_tickCountToTimeMap.TickCount + _timer._dueTime;
                        return TimerQueue.s_tickCountToTimeMap.Time + TimeSpan.FromMilliseconds(milliseconds);
                    }
                    return null;
                }
            }

            public TimeSpan? DueTime
            {
                get
                {
                    if (_timer._dueTime != uint.MaxValue)
                    {
                        return TimeSpan.FromMilliseconds(_timer._dueTime);
                    }
                    return null;
                }
            }

            public TimeSpan? Period
            {
                get
                {
                    if (_timer._period != uint.MaxValue)
                    {
                        return TimeSpan.FromMilliseconds(_timer._period);
                    }
                    return null;
                }
            }

            public TimerCallback Callback => _timer._timerCallback;

            public object State => _timer._state;

            public TimerDebuggerTypeProxy(Timer timer)
            {
                _timer = timer._timer._timer;
            }

            public TimerDebuggerTypeProxy(TimerQueueTimer timer)
            {
                _timer = timer;
            }
        }

        private readonly TimerQueue _associatedTimerQueue;

        internal TimerQueueTimer _next;

        internal TimerQueueTimer _prev;

        internal bool _short;

        internal long _startTicks;

        internal uint _dueTime;

        internal uint _period;

        private readonly TimerCallback _timerCallback;

        private readonly object _state;

        private readonly ExecutionContext _executionContext;

        private int _callbacksRunning;

        private bool _canceled;

        internal bool _everQueued;

        private object _notifyWhenNoCallbacksRunning;

        private static readonly ContextCallback s_callCallbackInContext = delegate (object state)
        {
            TimerQueueTimer timerQueueTimer = (TimerQueueTimer)state;
            timerQueueTimer._timerCallback(timerQueueTimer._state);
        };

        internal string DisplayString
        {
            get
            {
                string text = _timerCallback.Method.DeclaringType?.FullName;
                if (text is not null)
                {
                    text += ".";
                }
                return "DueTime = " + ((_dueTime == uint.MaxValue) ? "(not set)" : ((object)TimeSpan.FromMilliseconds(_dueTime)))?.ToString() + ", Period = " + ((_period == uint.MaxValue) ? "(not set)" : ((object)TimeSpan.FromMilliseconds(_period)))?.ToString() + ", " + text + _timerCallback.Method.Name + "(" + (_state?.ToString() ?? "null") + ")";
            }
        }

        internal TimerQueueTimer(TimerCallback timerCallback, object state, TimeSpan dueTime, TimeSpan period, bool flowExecutionContext)
            : this(timerCallback, state, GetMilliseconds(dueTime, "dueTime"), GetMilliseconds(period, "period"), flowExecutionContext)
        {
        }

        private static uint GetMilliseconds(TimeSpan time, [CallerArgumentExpression("time")] string parameter = null)
        {
            long num = (long)time.TotalMilliseconds;
            ArgumentOutOfRangeException.ThrowIfLessThan(num, -1L, parameter);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(num, 4294967294L, parameter);
            return (uint)num;
        }

        internal TimerQueueTimer(TimerCallback timerCallback, object state, uint dueTime, uint period, bool flowExecutionContext)
        {
            _timerCallback = timerCallback;
            _state = state;
            _dueTime = uint.MaxValue;
            _period = uint.MaxValue;
            if (flowExecutionContext)
            {
                _executionContext = ExecutionContext.Capture();
            }
            _associatedTimerQueue = TimerQueue.Instances[(uint)Thread.GetCurrentProcessorId() % TimerQueue.Instances.Length];
            if (dueTime != uint.MaxValue)
            {
                Change(dueTime, period);
            }
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            return Change(GetMilliseconds(dueTime, "dueTime"), GetMilliseconds(period, "period"));
        }

        internal bool Change(uint dueTime, uint period)
        {
            using (_associatedTimerQueue.SharedLock.EnterScope())
            {
                if (_canceled)
                {
                    return false;
                }
                _period = period;
                if (dueTime == uint.MaxValue)
                {
                    _associatedTimerQueue.DeleteTimer(this);
                    return true;
                }
                if (FrameworkEventSource.Log.IsEnabled(EventLevel.Informational, (EventKeywords)16L))
                {
                    FrameworkEventSource.Log.ThreadTransferSendObj(this, 1, string.Empty, multiDequeues: true, (int)dueTime, (int)period);
                }
                return _associatedTimerQueue.UpdateTimer(this, dueTime, period);
            }
        }

        public void Dispose()
        {
            using (_associatedTimerQueue.SharedLock.EnterScope())
            {
                if (!_canceled)
                {
                    _canceled = true;
                    _associatedTimerQueue.DeleteTimer(this);
                }
            }
        }

        public bool Dispose(WaitHandle toSignal)
        {
            bool flag = false;
            bool result;
            using (_associatedTimerQueue.SharedLock.EnterScope())
            {
                if (_canceled)
                {
                    result = false;
                }
                else
                {
                    _canceled = true;
                    _notifyWhenNoCallbacksRunning = toSignal;
                    _associatedTimerQueue.DeleteTimer(this);
                    flag = _callbacksRunning == 0;
                    result = true;
                }
            }
            if (flag)
            {
                SignalNoCallbacksRunning();
            }
            return result;
        }

        public ValueTask DisposeAsync()
        {
            using (_associatedTimerQueue.SharedLock.EnterScope())
            {
                object notifyWhenNoCallbacksRunning = _notifyWhenNoCallbacksRunning;
                if (_canceled)
                {
                    if (notifyWhenNoCallbacksRunning is WaitHandle)
                    {
                        InvalidOperationException ex = new InvalidOperationException(SR.InvalidOperation_TimerAlreadyClosed);
                        ex.SetCurrentStackTrace();
                        return ValueTask.FromException(ex);
                    }
                }
                else
                {
                    _canceled = true;
                    _associatedTimerQueue.DeleteTimer(this);
                }
                if (_callbacksRunning == 0)
                {
                    return default(ValueTask);
                }
                if (notifyWhenNoCallbacksRunning is null)
                {
                    return new ValueTask((Task)(_notifyWhenNoCallbacksRunning = new Task(null, TaskCreationOptions.RunContinuationsAsynchronously, promiseStyle: true)));
                }
                return new ValueTask((Task)notifyWhenNoCallbacksRunning);
            }
        }

        void IThreadPoolWorkItem.Execute()
        {
            Fire(isThreadPool: true);
        }

        internal void Fire(bool isThreadPool = false)
        {
            bool canceled;
            using (_associatedTimerQueue.SharedLock.EnterScope())
            {
                canceled = _canceled;
                if (!canceled)
                {
                    _callbacksRunning++;
                }
            }
            if (!canceled)
            {
                CallCallback(isThreadPool);
                bool flag;
                using (_associatedTimerQueue.SharedLock.EnterScope())
                {
                    _callbacksRunning--;
                    flag = _canceled && _callbacksRunning == 0 && _notifyWhenNoCallbacksRunning is not null;
                }
                if (flag)
                {
                    SignalNoCallbacksRunning();
                }
            }
        }

        internal void SignalNoCallbacksRunning()
        {
            object notifyWhenNoCallbacksRunning = _notifyWhenNoCallbacksRunning;
            if (notifyWhenNoCallbacksRunning is WaitHandle waitHandle)
            {
                (waitHandle as EventWaitHandle)?.Set();
            }
            else
            {
                ((Task)notifyWhenNoCallbacksRunning).TrySetResult();
            }
        }

        internal void CallCallback(bool isThreadPool)
        {
            if (FrameworkEventSource.Log.IsEnabled(EventLevel.Informational, (EventKeywords)16L))
            {
                FrameworkEventSource.Log.ThreadTransferReceiveObj(this, 1, string.Empty);
            }
            ExecutionContext executionContext = _executionContext;
            if (executionContext is null)
            {
                _timerCallback(_state);
            }
            else if (isThreadPool)
            {
                ExecutionContext.RunFromThreadPoolDispatchLoop(Thread.CurrentThread, executionContext, s_callCallbackInContext, this);
            }
            else
            {
                ExecutionContext.RunInternal(executionContext, s_callCallbackInContext, this);
            }
        }
    }


    public delegate void WaitCallback(object? state);

    internal struct LowLevelSpinWaiter
    {
        private int _spinningThreadCount;

        public bool SpinWaitForCondition(Func<object, bool> condition, object state, int spinCount, int sleep0Threshold)
        {
            int processorCount = Environment.ProcessorCount;
            int num = Interlocked.Increment(ref _spinningThreadCount);
            try
            {
                if (num <= processorCount)
                {
                    for (int i = ((processorCount <= 1) ? sleep0Threshold : 0); i < spinCount; i++)
                    {
                        Wait(i, sleep0Threshold, processorCount == 1);
                        if (condition(state))
                        {
                            return true;
                        }
                    }
                }
            }
            finally
            {
                Interlocked.Decrement(ref _spinningThreadCount);
            }
            return false;
        }

        public static void Wait(int spinIndex, int sleep0Threshold, bool isSingleProcessor)
        {
            if (!isSingleProcessor && (spinIndex < sleep0Threshold || (spinIndex - sleep0Threshold) % 2 != 0))
            {
                int num = 8;
                if (spinIndex <= 30 && 1 << spinIndex < num)
                {
                    num = 1 << spinIndex;
                }
                Thread.SpinWait(num);
            }
            else
            {
                Thread.UninterruptibleSleep0();
            }
        }
    }


    internal class ManagedThreadId
    {
        //
        // Binary tree used to keep track of active thread ids. Each node of the tree keeps track of 32 consecutive ids.
        // Implemented as immutable collection to avoid locks. Each modification creates a new top level node.
        //
        private class ImmutableIdDispenser
        {
            private readonly ImmutableIdDispenser? _left; // Child nodes
            private readonly ImmutableIdDispenser? _right;

            private readonly int _used; // Number of ids tracked by this node and all its childs
            private readonly int _size; // Maximum number of ids that can be tracked by this node and all its childs

            private readonly uint _bitmap; // Bitmap of ids tracked by this node

            private const int BitsPerNode = 32;

            private ImmutableIdDispenser(ImmutableIdDispenser? left, ImmutableIdDispenser? right, int used, int size, uint bitmap)
            {
                _left = left;
                _right = right;
                _used = used;
                _size = size;
                _bitmap = bitmap;
            }

            private void CheckInvariants()
            {
                int actualUsed = 0;

                uint countBits = _bitmap;
                while (countBits != 0)
                {
                    actualUsed += (int)(countBits & 1);
                    countBits >>= 1;
                }
                if (_left is not null)
                {
                    actualUsed += _left._used;
                }
                if (_right is not null)
                {
                    actualUsed += _right._used;
                }
            }

            private int ChildSize
            {
                get
                {
                    return (_size / 2) - (BitsPerNode / 2);
                }
            }

            public static ImmutableIdDispenser Empty
            {
                get
                {
                    // The empty dispenser has the id=0 allocated, so it is not really empty.
                    // It saves us from dealing with the corner case of true empty dispenser,
                    // and it ensures that IdNone will not be ever given out.
                    return new ImmutableIdDispenser(null, null, 1, BitsPerNode, 1);
                }
            }

            public ImmutableIdDispenser AllocateId(out int id)
            {
                if (_used == _size)
                {
                    id = _size;
                    return new ImmutableIdDispenser(this, null, _size + 1, checked(2 * _size + BitsPerNode), 1);
                }

                var bitmap = _bitmap;
                var left = _left;
                var right = _right;

                // Any free bits in current node?
                if (bitmap != uint.MaxValue)
                {
                    int bit = 0;
                    while ((bitmap & (uint)(1 << bit)) != 0)
                        bit++;
                    bitmap |= (uint)(1 << bit);
                    id = ChildSize + bit;
                }
                else
                {
                    if (left == null)
                    {
                        left = new ImmutableIdDispenser(null, null, 1, ChildSize, 1);
                        id = left.ChildSize;
                    }
                    else if (right == null)
                    {
                        right = new ImmutableIdDispenser(null, null, 1, ChildSize, 1);
                        id = ChildSize + BitsPerNode + right.ChildSize;
                    }
                    else
                    {
                        if (left._used < right._used)
                        {
                            left = left.AllocateId(out id);
                        }
                        else
                        {
                            right = right.AllocateId(out id);
                            id += (ChildSize + BitsPerNode);
                        }
                    }
                }
                return new ImmutableIdDispenser(left, right, _used + 1, _size, bitmap);
            }

            public ImmutableIdDispenser? RecycleId(int id)
            {
                if (_used == 1)
                    return null;

                var bitmap = _bitmap;
                var left = _left;
                var right = _right;

                int childSize = ChildSize;
                if (id < childSize)
                {
                    left = left.RecycleId(id);
                }
                else
                {
                    id -= childSize;
                    if (id < BitsPerNode)
                    {
                        bitmap &= ~(uint)(1 << id);
                    }
                    else
                    {
                        right = right.RecycleId(id - BitsPerNode);
                    }
                }
                return new ImmutableIdDispenser(left, right, _used - 1, _size, bitmap);
            }
        }

        public const int IdNone = 0;

        // The main thread takes the first available id, which is 1. This id will not be recycled until the process exit.
        // We use this id to detect the main thread and report it as a foreground one.
        public const int IdMainThread = 1;

        // We store ManagedThreadId both here and in the Thread.CurrentThread object. We store it here,
        // because we may need the id very early in the process lifetime (e.g., in ClassConstructorRunner),
        // when a Thread object cannot be created yet. We also store it in the Thread.CurrentThread object,
        // because that object may have longer lifetime than the OS thread.
        [ThreadStatic]
        private static ManagedThreadId t_currentThreadId;
        [ThreadStatic]
        private static int t_currentManagedThreadId;

        private static ImmutableIdDispenser? s_idDispenser;

        private int _managedThreadId;

        public int Id => _managedThreadId;

        public static int AllocateId()
        {
            if (s_idDispenser == null)
                Interlocked.CompareExchange(ref s_idDispenser, ImmutableIdDispenser.Empty, null);

            int id;

            var priorIdDispenser = Volatile.Read(ref s_idDispenser);
            for (; ; )
            {
                var updatedIdDispenser = priorIdDispenser.AllocateId(out id);
                var interlockedResult = Interlocked.CompareExchange(ref s_idDispenser, updatedIdDispenser, priorIdDispenser);
                if (object.ReferenceEquals(priorIdDispenser, interlockedResult))
                    break;
                priorIdDispenser = interlockedResult; // we already have a volatile read that we can reuse for the next loop
            }

            return id;
        }

        public static void RecycleId(int id)
        {
            if (id == IdNone)
            {
                return;
            }

            var priorIdDispenser = Volatile.Read(ref s_idDispenser);
            for (; ; )
            {
                var updatedIdDispenser = s_idDispenser.RecycleId(id);
                var interlockedResult = Interlocked.CompareExchange(ref s_idDispenser, updatedIdDispenser, priorIdDispenser);
                if (object.ReferenceEquals(priorIdDispenser, interlockedResult))
                    break;
                priorIdDispenser = interlockedResult; // we already have a volatile read that we can reuse for the next loop
            }
        }

        internal static int CurrentManagedThreadIdUnchecked => t_currentManagedThreadId;

        public static int Current
        {
            get
            {
                int currentManagedThreadId = t_currentManagedThreadId;
                if (currentManagedThreadId == IdNone)
                    return MakeForCurrentThread();
                else
                    return currentManagedThreadId;
            }
        }

        public static ManagedThreadId GetCurrentThreadId()
        {
            if (t_currentManagedThreadId == IdNone)
                MakeForCurrentThread();

            return t_currentThreadId;
        }

        private static int MakeForCurrentThread()
        {
            return SetForCurrentThread(new ManagedThreadId());
        }

        public static int SetForCurrentThread(ManagedThreadId threadId)
        {
            t_currentThreadId = threadId;
            t_currentManagedThreadId = threadId.Id;
            return threadId.Id;
        }

        public ManagedThreadId()
        {
            _managedThreadId = AllocateId();
        }

        ~ManagedThreadId()
        {
            RecycleId(_managedThreadId);
        }
    }


    internal struct ThreadBlockingInfo
    {
        public ref struct Scope
        {
            private object _object;

            private ThreadBlockingInfo _blockingInfo;

            public Scope(Lock lockObj, int timeoutMs)
                : this(lockObj, ObjectKind.Lock, timeoutMs)
            {
            }

            public Scope(Condition condition, int timeoutMs)
                : this(condition, ObjectKind.Condition, timeoutMs)
            {
            }

            private unsafe Scope(object obj, ObjectKind objectKind, int timeoutMs)
            {
                _blockingInfo = default(ThreadBlockingInfo);
                _object = obj;
                _blockingInfo.Push(Unsafe.AsPointer(in _object), objectKind, timeoutMs);
            }

            public void Dispose()
            {
                if (_object is not null)
                {
                    _blockingInfo.Pop();
                    _object = null;
                }
            }
        }

        public enum ObjectKind
        {
            MonitorLock,
            MonitorWait,
            Lock,
            Condition
        }

        [ThreadStatic]
        private unsafe static ThreadBlockingInfo* t_first;

        private unsafe void* _objectPtr;

        private ObjectKind _objectKind;

        private int _timeoutMs;

        private unsafe ThreadBlockingInfo* _next;

        public unsafe int LockOwnerManagedThreadId => _objectKind switch
        {
            ObjectKind.Lock => ((Lock)Unsafe.AsRef<object>(_objectPtr)).OwningManagedThreadId,
            ObjectKind.Condition => ((Condition)Unsafe.AsRef<object>(_objectPtr)).AssociatedLock.OwningManagedThreadId,
            _ => throw new UnreachableException(),
        };

        private unsafe void Push(void* objectPtr, ObjectKind objectKind, int timeoutMs)
        {
            _objectPtr = objectPtr;
            _objectKind = objectKind;
            _timeoutMs = timeoutMs;
            _next = t_first;
            t_first = (ThreadBlockingInfo*)Unsafe.AsPointer(ref this);
        }

        private unsafe void Pop()
        {
            t_first = _next;
            _objectPtr = null;
        }
    }


    public class LockRecursionException : Exception
    {
        public LockRecursionException()
        {
        }

        public LockRecursionException(string? message)
            : base(message)
        {
        }

        public LockRecursionException(string? message, Exception? innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Provides a mutual exclusion lock primitive where a thread trying to acquire the lock waits in a loop
    /// repeatedly checking until the lock becomes available.
    /// </summary>
    [DebuggerDisplay("IsHeld = {IsHeld}")]
    public struct SpinLock
    {
        // The lock is held if the low bit is set when owner tracking is off, or if the value is non-zero when
        // it is on. With tracking off the remaining bits count the waiters.
        private volatile int _owner;

        // After how many yields, call Sleep(1)
        private const int SLEEP_ONE_FREQUENCY = 40;

        // After how many yields, check the timeout
        private const int TIMEOUT_CHECK_FREQUENCY = 10;

        // Thr thread tracking disabled mask
        private const int LOCK_ID_DISABLE_MASK = unchecked((int)0x80000000); // 1000 0000 0000 0000 0000 0000 0000 0000

        // the lock is held by some thread, but we don't know which
        private const int LOCK_ANONYMOUS_OWNED = 0x1; // 0000 0000 0000 0000 0000 0000 0000 0001

        // Waiters mask if the thread tracking is disabled
        private const int WAITERS_MASK = ~(LOCK_ID_DISABLE_MASK | 1); // 0111 1111 1111 1111 1111 1111 1111 1110

        // The Thread tacking is disabled and the lock bit is set, used in Enter fast path to make sure the id is disabled and lock is available
        private const int ID_DISABLED_AND_ANONYMOUS_OWNED = unchecked((int)0x80000001); // 1000 0000 0000 0000 0000 0000 0000 0001

        // If the thread is unowned if:
        // m_owner zero and the thread tracking is enabled
        // m_owner & LOCK_ANONYMOUS_OWNED = zero and the thread tracking is disabled
        private const int LOCK_UNOWNED = 0;

        // The maximum number of waiters (only used if the thread tracking is disabled)
        // The actual maximum waiters count is this number divided by two because each waiter increments the waiters count by 2
        // The waiters count is calculated by m_owner & WAITERS_MASK 01111....110
        private const int MAXIMUM_WAITERS = WAITERS_MASK;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CompareExchange(ref int location, int value, int comparand, ref bool success)
        {
            int result = Interlocked.CompareExchange(ref location, value, comparand);
            success = (result == comparand);
            return result;
        }

        public SpinLock(bool enableThreadOwnerTracking)
        {
            _owner = LOCK_UNOWNED;
            if (!enableThreadOwnerTracking)
            {
                _owner |= LOCK_ID_DISABLE_MASK;
            }
        }

        public void Enter(ref bool lockTaken)
        {
            // Try to keep the code and branching in this method as small as possible in order to inline the method
            int observedOwner = _owner;
            if (lockTaken || // invalid parameter
                (observedOwner & ID_DISABLED_AND_ANONYMOUS_OWNED) != LOCK_ID_DISABLE_MASK || // thread tracking is enabled or the lock is already acquired
                CompareExchange(ref _owner, observedOwner | LOCK_ANONYMOUS_OWNED, observedOwner, ref lockTaken) != observedOwner) // acquiring the lock failed
            {
                ContinueTryEnter(Timeout.Infinite, ref lockTaken); // Then try the slow path if any of the above conditions is met
            }
        }

        public void TryEnter(ref bool lockTaken)
        {
            int observedOwner = _owner;
            if (((observedOwner & LOCK_ID_DISABLE_MASK) == 0) | lockTaken)
            {
                // Thread tracking enabled or invalid arg. Take slow path.
                ContinueTryEnter(0, ref lockTaken);
            }
            else if ((observedOwner & LOCK_ANONYMOUS_OWNED) != 0)
            {
                // Lock already held by someone
                lockTaken = false;
            }
            else
            {
                // Lock wasn't held; try to acquire it.
                CompareExchange(ref _owner, observedOwner | LOCK_ANONYMOUS_OWNED, observedOwner, ref lockTaken);
            }
        }

        public void TryEnter(TimeSpan timeout, ref bool lockTaken)
        {
            // Validate the timeout
            long totalMilliseconds = (long)timeout.TotalMilliseconds;
            if (totalMilliseconds < -1 || totalMilliseconds > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), timeout, SR.SpinLock_TryEnter_ArgumentOutOfRange);
            }

            // Call reliable enter with the int-based timeout milliseconds
            TryEnter((int)timeout.TotalMilliseconds, ref lockTaken);
        }

        public void TryEnter(int millisecondsTimeout, ref bool lockTaken)
        {
            int observedOwner = _owner;
            if (millisecondsTimeout < -1 || // invalid parameter
                lockTaken || // invalid parameter
                (observedOwner & ID_DISABLED_AND_ANONYMOUS_OWNED) != LOCK_ID_DISABLE_MASK || // thread tracking is enabled or the lock is already acquired
                CompareExchange(ref _owner, observedOwner | LOCK_ANONYMOUS_OWNED, observedOwner, ref lockTaken) != observedOwner) // acquiring the lock failed
            {
                ContinueTryEnter(millisecondsTimeout, ref lockTaken); // The call the slow pth
            }
        }

        private void ContinueTryEnter(int millisecondsTimeout, ref bool lockTaken)
        {
            if (lockTaken)
            {
                lockTaken = false;
                throw new ArgumentException(SR.SpinLock_TryReliableEnter_ArgumentException);
            }

            if (millisecondsTimeout < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(millisecondsTimeout), millisecondsTimeout, SR.SpinLock_TryEnter_ArgumentOutOfRange);
            }

            long startTime = 0;
            if (millisecondsTimeout != Timeout.Infinite && millisecondsTimeout != 0)
            {
                startTime = Environment.TickCount64;
            }

            if (IsThreadOwnerTrackingEnabled)
            {
                // Slow path for enabled thread tracking mode
                ContinueTryEnterWithThreadTracking(millisecondsTimeout, startTime, ref lockTaken);
                return;
            }

            // then thread tracking is disabled
            // In this case there are three ways to acquire the lock
            // 1- the first way the thread either tries to get the lock if it's free or updates the waiters, if the turn >= the processors count then go to 3 else go to 2
            // 2- In this step the waiter threads spins and tries to acquire the lock, the number of spin iterations and spin count is dependent on the thread turn
            // the late the thread arrives the more it spins and less frequent it check the lock availability
            // Also the spins count is increases each iteration
            // If the spins iterations finished and failed to acquire the lock, go to step 3
            // 3- This is the yielding step, there are two ways of yielding Thread.Yield and Sleep(1)
            // If the timeout is expired in after step 1, we need to decrement the waiters count before returning

            int observedOwner;
            int turn = int.MaxValue;
            // ***Step 1, take the lock or update the waiters

            // try to acquire the lock directly if possible or update the waiters count
            observedOwner = _owner;
            if ((observedOwner & LOCK_ANONYMOUS_OWNED) == LOCK_UNOWNED)
            {
                if (CompareExchange(ref _owner, observedOwner | LOCK_ANONYMOUS_OWNED, observedOwner, ref lockTaken) == observedOwner)
                {
                    // Acquired lock
                    return;
                }

                if (millisecondsTimeout == 0)
                {
                    // Did not acquire lock in CompareExchange and timeout is 0 so fail fast
                    return;
                }
            }
            else if (millisecondsTimeout == 0)
            {
                // Did not acquire lock as owned and timeout is 0 so fail fast
                return;
            }
            else // failed to acquire the lock, then try to update the waiters. If the waiters count reached the maximum, just break the loop to avoid overflow
            {
                if ((observedOwner & WAITERS_MASK) != MAXIMUM_WAITERS)
                {
                    // This can still overflow, but maybe there will never be that many waiters
                    turn = (Interlocked.Add(ref _owner, 2) & WAITERS_MASK) >> 1;
                }
            }

            // lock acquired failed and waiters updated

            // *** Step 2, Spinning and Yielding
            SpinWait spinner = default;
            if (turn > Environment.ProcessorCount)
            {
                spinner.Count = SpinWait.YieldThreshold;
            }
            while (true)
            {
                spinner.SpinOnce(SLEEP_ONE_FREQUENCY);

                observedOwner = _owner;
                if ((observedOwner & LOCK_ANONYMOUS_OWNED) == LOCK_UNOWNED)
                {
                    int newOwner = (observedOwner & WAITERS_MASK) == 0 ? // Gets the number of waiters, if zero
                           observedOwner | 1 // don't decrement it. just set the lock bit, it is zero because a previous call of Exit(false) which corrupted the waiters
                           : (observedOwner - 2) | 1; // otherwise decrement the waiters and set the lock bit

                    if (CompareExchange(ref _owner, newOwner, observedOwner, ref lockTaken) == observedOwner)
                    {
                        return;
                    }
                }

                if (spinner.Count % TIMEOUT_CHECK_FREQUENCY == 0)
                {
                    // Check the timeout.
                    if (millisecondsTimeout != Timeout.Infinite && TimeoutHelper.UpdateTimeOut(startTime, millisecondsTimeout) <= 0)
                    {
                        DecrementWaiters();
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// decrements the waiters, in case of the timeout is expired
        /// </summary>
        private void DecrementWaiters()
        {
            SpinWait spinner = default;
            while (true)
            {
                int observedOwner = _owner;
                if ((observedOwner & WAITERS_MASK) == 0)
                    return; // don't decrement the waiters if it's corrupted by previous call of Exit(false)
                if (Interlocked.CompareExchange(ref _owner, observedOwner - 2, observedOwner) == observedOwner)
                {
                    break;
                }
                spinner.SpinOnce();
            }
        }

        /// <summary>
        /// ContinueTryEnter for the thread tracking mode enabled
        /// </summary>
        private void ContinueTryEnterWithThreadTracking(int millisecondsTimeout, long startTime, ref bool lockTaken)
        {
            int lockUnowned = 0;
            // We are using thread IDs to mark ownership. Snap the thread ID and check for recursion.
            // We also must or the ID enablement bit, to ensure we propagate when we CAS it in.
            int newOwner = Environment.CurrentManagedThreadId;
            if (_owner == newOwner)
            {
                // We don't allow lock recursion.
                throw new LockRecursionException(SR.SpinLock_TryEnter_LockRecursionException);
            }

            SpinWait spinner = default;

            // Loop until the lock has been successfully acquired or, if specified, the timeout expires.
            while (true)
            {
                // We failed to get the lock, either from the fast route or the last iteration
                // and the timeout hasn't expired; spin once and try again.
                spinner.SpinOnce();

                // Test before trying to CAS, to avoid acquiring the line exclusively unnecessarily.

                if (_owner == lockUnowned)
                {
                    if (CompareExchange(ref _owner, newOwner, lockUnowned, ref lockTaken) == lockUnowned)
                    {
                        return;
                    }
                }
                // Check the timeout. We only RDTSC if the next spin will yield, to amortize the cost.
                if (millisecondsTimeout == 0 ||
                    (millisecondsTimeout != Timeout.Infinite && spinner.NextSpinWillYield &&
                    TimeoutHelper.UpdateTimeOut(startTime, millisecondsTimeout) <= 0))
                {
                    return;
                }
            }
        }

        public void Exit()
        {
            // This is the fast path for the thread tracking is disabled, otherwise go to the slow path
            if ((_owner & LOCK_ID_DISABLE_MASK) == 0)
                ExitSlowPath(true);
            else
                Interlocked.Decrement(ref _owner);
        }

        public void Exit(bool useMemoryBarrier)
        {
            // This is the fast path for the thread tracking is disabled and not to use memory barrier, otherwise go to the slow path
            // The reason not to add else statement if the usememorybarrier is that it will add more branching in the code and will prevent
            // method inlining, so this is optimized for useMemoryBarrier=false and Exit() overload optimized for useMemoryBarrier=true.
            int tmpOwner = _owner;
            if ((tmpOwner & LOCK_ID_DISABLE_MASK) != 0 & !useMemoryBarrier)
            {
                _owner = tmpOwner & (~LOCK_ANONYMOUS_OWNED);
            }
            else
            {
                ExitSlowPath(useMemoryBarrier);
            }
        }

        private void ExitSlowPath(bool useMemoryBarrier)
        {
            bool threadTrackingEnabled = (_owner & LOCK_ID_DISABLE_MASK) == 0;
            if (threadTrackingEnabled && !IsHeldByCurrentThread)
            {
                ThrowHelper.ThrowSynchronizationLockException_LockExit();
            }

            if (useMemoryBarrier)
            {
                if (threadTrackingEnabled)
                {
                    Interlocked.Exchange(ref _owner, LOCK_UNOWNED);
                }
                else
                {
                    Interlocked.Decrement(ref _owner);
                }
            }
            else
            {
                if (threadTrackingEnabled)
                {
                    _owner = LOCK_UNOWNED;
                }
                else
                {
                    int tmpOwner = _owner;
                    _owner = tmpOwner & (~LOCK_ANONYMOUS_OWNED);
                }
            }
        }

        public bool IsHeld
        {
            get
            {
                if (IsThreadOwnerTrackingEnabled)
                    return _owner != LOCK_UNOWNED;

                return (_owner & LOCK_ANONYMOUS_OWNED) != LOCK_UNOWNED;
            }
        }

        public bool IsHeldByCurrentThread
        {
            get
            {
                if (!IsThreadOwnerTrackingEnabled)
                {
                    throw new InvalidOperationException(SR.SpinLock_IsHeldByCurrentThread);
                }
                return (_owner & (~LOCK_ID_DISABLE_MASK)) == Environment.CurrentManagedThreadId;
            }
        }

        public bool IsThreadOwnerTrackingEnabled => (_owner & LOCK_ID_DISABLE_MASK) == 0;
    }

    /// <summary>
    /// A lightweight non-recursive mutex. Waits on a thread blocker, with a spinner before blocking.
    /// </summary>
    internal sealed class LowLevelLock : IDisposable
    {
        private const uint SpinCount = 8;

        private const uint LockedMask = 1;
        private const uint WaiterCountIncrement = 2;

        // Indicates that a thread has been signaled, but has not yet been released from the wait
        private const uint WaiterWoken = 1u << 31;

        private uint _state;

        private LowLevelThreadBlocker _blocker = new LowLevelThreadBlocker();

        ~LowLevelLock()
        {
            Dispose();
        }

        public void Dispose()
        {
            _blocker.Dispose();
            GC.SuppressFinalize(this);
        }

        public bool TryAcquire()
        {
            uint state = _state;
            return (state & LockedMask) == 0 && Interlocked.CompareExchange(ref _state, state + LockedMask, state) == state;
        }

        public void Acquire()
        {
            if (!TryAcquire())
            {
                SpinAndAcquire();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void SpinAndAcquire()
        {
            uint spins = Environment.IsSingleProcessor ? 0 : SpinCount;

            for (uint spinIndex = 0; spinIndex < spins; spinIndex++)
            {
                // Exponential backoff with some randomization to reduce contention
                Backoff.Exponential(spinIndex);

                if (TryAcquire())
                {
                    return;
                }
            }

            WaitAndAcquire();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void WaitAndAcquire()
        {
            // Register a waiter, or acquire the lock if it has become free since the spinner gave up
            uint collisions = 0;
            uint state;
            while (true)
            {
                state = _state;
                uint newState = (state & LockedMask) == 0 ? state + LockedMask : state + WaiterCountIncrement;
                if (Interlocked.CompareExchange(ref _state, newState, state) == state)
                {
                    break;
                }

                Backoff.Exponential(collisions++);
            }

            if ((state & LockedMask) == 0)
            {
                return;
            }

            while (true)
            {
                _blocker.Wait();

                // This thread was woken, so clear the woken bit and try to take the lock
                collisions = 0;
                while (true)
                {
                    state = _state;
                    uint newState = state & ~WaiterWoken;
                    if ((state & LockedMask) == 0)
                    {
                        newState -= WaiterCountIncrement;
                        newState += LockedMask;
                    }

                    if (Interlocked.CompareExchange(ref _state, newState, state) == state)
                    {
                        break;
                    }

                    Backoff.Exponential(collisions++);
                }

                if ((state & LockedMask) == 0)
                {
                    return;
                }
            }
        }

        public void Release()
        {
            // Unlock, then wake one waiter unless another one has been signaled and has not run yet
            uint state = Interlocked.Add(ref _state, unchecked((uint)-(int)LockedMask));
            if (state >= WaiterCountIncrement && state < WaiterWoken)
            {
                SignalWaiter();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void SignalWaiter()
        {
            uint collisions = 0;
            while (true)
            {
                uint state = _state;
                if (state < WaiterCountIncrement || state >= WaiterWoken)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _state, state | WaiterWoken, state) == state)
                {
                    _blocker.WakeOne();
                    return;
                }

                Backoff.Exponential(collisions++);
            }
        }
    }

    /// <summary>
    /// A cache-line sized counter blocked on through a futex
    /// </summary>
    internal unsafe struct LowLevelThreadBlocker : IDisposable
    {
        private int* _pState;

        public LowLevelThreadBlocker()
        {
            _pState = (int*)NativeMemory.AlignedAlloc(64, 64);
            *_pState = 0;
        }

        public void Dispose()
        {
            if (_pState is not null)
            {
                NativeMemory.AlignedFree(_pState);
                _pState = null;
            }
        }

        internal void Wait()
        {
            int state;
            do
            {
                state = *_pState;
                while (state == 0)
                {
                    LowLevelFutex.WaitOnAddress(_pState, state);
                    state = *_pState;
                }
            }
            while (Interlocked.CompareExchange(ref *_pState, state - 1, state) != state);
        }

        internal bool TimedWait(int timeoutMs)
        {
            if (timeoutMs == Timeout.Infinite)
            {
                Wait();
                return true;
            }

            long deadlineMs = Environment.TickCount64 + timeoutMs;
            int state;
            do
            {
                state = *_pState;
                while (state == 0)
                {
                    if (!LowLevelFutex.WaitOnAddressTimeout(_pState, state, timeoutMs))
                    {
                        return false;
                    }

                    timeoutMs = (int)(deadlineMs - Environment.TickCount64);
                    if (timeoutMs < 0)
                    {
                        return false;
                    }

                    state = *_pState;
                }
            }
            while (Interlocked.CompareExchange(ref *_pState, state - 1, state) != state);

            return true;
        }

        internal void WakeOne()
        {
            Interlocked.Increment(ref *_pState);
            LowLevelFutex.WakeByAddressSingle(_pState);
        }
    }

    /// <summary>
    /// A compare-and-wait synchronization primitive.
    /// Provides simple functionality to block and wake threads.
    /// </summary>
    internal static unsafe class LowLevelFutex
    {
        internal static void WaitOnAddress(int* address, int comparand)
        {
            Interop.Sys.LowLevelFutex_WaitOnAddress(address, comparand);
        }

        internal static bool WaitOnAddressTimeout(int* address, int comparand, int milliseconds)
        {
            if (milliseconds == -1)
            {
                WaitOnAddress(address, comparand);
                return true;
            }

            return Interop.Sys.LowLevelFutex_WaitOnAddressTimeout(address, comparand, milliseconds);
        }

        internal static void WakeByAddressSingle(int* address)
        {
            Interop.Sys.LowLevelFutex_WakeByAddressSingle(address);
        }
    }
    /// <summary>
    /// The exception that is thrown when the <see cref="SemaphoreSlim.Release()"/> method is called on a semaphore object whose count is already at the maximum.
    /// </summary>
    public class SemaphoreFullException : SystemException
    {
        public SemaphoreFullException()
            : base(SR.Threading_SemaphoreFullException)
        {
        }

        public SemaphoreFullException(string? message)
            : base(message ?? SR.Threading_SemaphoreFullException)
        {
        }

        public SemaphoreFullException(string? message, Exception? innerException)
            : base(message ?? SR.Threading_SemaphoreFullException, innerException)
        {
        }
    }
    public delegate void WaitOrTimerCallback(object? state, bool timedOut);

    public sealed class RegisteredWaitHandle : MarshalByRefObject
    {
        internal RegisteredWaitHandle()
        {
        }

        public bool Unregister(WaitHandle? waitObject) => true;
    }
}
