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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetCurrentProcessorId()
        {
            return ProcessorIdCache.GetCurrentProcessorId();
        }

        //internal static int GetCurrentProcessorNumber() => Interop.Sys.SchedGetCpu();
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern int GetCurrentProcessorNumber();
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
    public abstract class WaitHandle
    {
        internal const int MaxWaitHandles = 64;

        protected static readonly IntPtr InvalidHandle = new IntPtr(-1);
    }
    public partial class EventWaitHandle : WaitHandle
    {
        public EventWaitHandle(bool initialState, EventResetMode mode)
        {
            CreateEventCore(initialState, mode);
        }

        private void CreateEventCore(bool initialState, EventResetMode mode)
        {
            ValidateMode(mode);
        }

        private static void ValidateMode(EventResetMode mode)
        {
            if (mode != EventResetMode.AutoReset && mode != EventResetMode.ManualReset)
            {
                throw new ArgumentException();
            }
        }
    }
    public sealed class AutoResetEvent : EventWaitHandle
    {
        public AutoResetEvent(bool initialState) : base(initialState, EventResetMode.AutoReset) { }
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
            int currentThreadId = TryEnter_Inlined(timeoutMs: -1);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private int EnterAndGetCurrentThreadId()
        {
            int currentThreadId = TryEnter_Inlined(timeoutMs: -1);
            return currentThreadId;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int TryEnter_Inlined(int timeoutMs)
        {
            throw new NotSupportedException();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Scope EnterScope() => new Scope(this, EnterAndGetCurrentThreadId());

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void Exit(int currentThreadId)
        {
            if (_owningThreadId != currentThreadId)
            {
                throw new SynchronizationLockException();
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
                throw new NotSupportedException();
                //bool signaled = WaitEvent.Set();
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
        }
    }
    public static class Monitor
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Enter(object obj)
        {
            ObjectHeader.AcquireThinLock(obj);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Exit(object obj)
        {
            if (obj is null) throw new ArgumentNullException();
            ObjectHeader.Release(obj);
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
        #endregion

        #region And
        #endregion

        #region Or
        #endregion

        #region MemoryBarrier
        #endregion
    }
}